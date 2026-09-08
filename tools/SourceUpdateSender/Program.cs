using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Drawing;
using System.Windows.Forms;
using LotroTrGemini;

namespace LotroSourceUpdateSender;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SenderForm());
    }
}

internal sealed class SenderSettings
{
    public string repository { get; set; } = "Relactive55/lotro-turkiye-yama-kaynak";
    public string branch { get; set; } = "main";
    public string last_updated_dat { get; set; } = "";
    public string state_path { get; set; } = "";
}

internal sealed class GitHubCommandResult
{
    public int ExitCode { get; set; }
    public string StandardOutput { get; set; } = "";
    public string StandardError { get; set; } = "";
}

internal sealed class GitHubSourceUploader
{
    private readonly string _repository;
    private readonly string _branch;
    private readonly Action<string> _progress;

    public GitHubSourceUploader(string repository, string branch, Action<string> progress)
    {
        _repository = ValidateRepository(repository);
        _branch = ValidateBranch(branch);
        _progress = progress ?? delegate { };
    }

    public void EnsurePrivateRepository()
    {
        GitHubCommandResult result = RunGh("repo view " + Quote(_repository) + " --json visibility --jq .visibility", null, 60000);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Private kaynak deposu bulunamadı veya GitHub yetkilendirmesi eksik.\r\n" + CleanError(result));
        if (!string.Equals(result.StandardOutput.Trim(), "PRIVATE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Kaynak deposu PRIVATE olmalıdır. Public depoya ham kaynak gönderilmedi.");
    }

    public void Upload(SourceUpdateExportResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        EnsurePrivateRepository();
        foreach (SourceUpdateBundleFile bundle in result.Bundles)
        {
            _progress(string.Format("GitHub private depoya {0}/{1} gönderiliyor…", bundle.Part, bundle.Parts));
            UploadOne(result.UpdateId, bundle.Path, bundle.Part);
        }
    }

    private void UploadOne(string updateId, string path, int part)
    {
        FileInfo file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Kaynak paketi bulunamadı.", path);
        if (file.Length > 60L * 1024L * 1024L) throw new InvalidDataException("Kaynak paketi GitHub sınırından büyük.");
        string remotePath = "incoming/" + updateId + "/" + Path.GetFileName(path);
        string requestPath = Path.Combine(Path.GetTempPath(), "lotro-source-upload-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            string content = Convert.ToBase64String(File.ReadAllBytes(path));
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 20 };
            string body = serializer.Serialize(new Dictionary<string, object>
            {
                ["message"] = "LOTRO source update " + updateId + " part " + part.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["content"] = content,
                ["branch"] = _branch
            });
            File.WriteAllText(requestPath, body, new UTF8Encoding(false));
            string endpoint = "repos/" + _repository + "/contents/" + remotePath;
            GitHubCommandResult result = RunGh("api --method PUT " + Quote(endpoint) + " --input " + Quote(requestPath) + " --silent", null, 300000);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("GitHub kaynak paketi gönderilemedi (part " + part + ").\r\n" + CleanError(result));
        }
        finally
        {
            try { if (File.Exists(requestPath)) File.Delete(requestPath); } catch { }
        }
    }

    private static string ValidateRepository(string value)
    {
        string repo = (value ?? "").Trim();
        string[] parts = repo.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace) || parts.Any(part => part.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.'))))
            throw new ArgumentException("GitHub deposu owner/repository biçiminde olmalıdır.", nameof(value));
        return repo;
    }

    private static string ValidateBranch(string value)
    {
        string branch = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(branch) || branch.Contains("..") || branch.Contains(" ") || branch.Contains("~") || branch.Contains("^") || branch.Contains(":") || branch.Contains("\\"))
            throw new ArgumentException("GitHub branch adı geçersiz.", nameof(value));
        return branch;
    }

    private static string FindGh()
    {
        string configured = Environment.GetEnvironmentVariable("GH_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GitHub CLI", "gh.exe")
        };
        foreach (string candidate in candidates) if (File.Exists(candidate)) return candidate;
        return "gh.exe";
    }

    private static GitHubCommandResult RunGh(string arguments, string workingDirectory, int timeoutMs)
    {
        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = FindGh(),
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? AppDomain.CurrentDomain.BaseDirectory : workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using (Process process = new Process { StartInfo = start })
        {
            try
            {
                if (!process.Start()) throw new InvalidOperationException("GitHub CLI başlatılamadı.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("GitHub CLI (gh) bulunamadı. GitHub CLI kurulup `gh auth login` ile bir kez giriş yapılmalı.", ex);
            }
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException("GitHub işlemi zaman aşımına uğradı.");
            }
            Task.WaitAll(stdout, stderr);
            return new GitHubCommandResult { ExitCode = process.ExitCode, StandardOutput = stdout.Result ?? "", StandardError = stderr.Result ?? "" };
        }
    }

    private static string CleanError(GitHubCommandResult result)
    {
        string error = (result.StandardError ?? "").Trim();
        if (string.IsNullOrWhiteSpace(error)) error = (result.StandardOutput ?? "").Trim();
        if (error.Length > 1000) error = error.Substring(0, 1000);
        return error;
    }

    private static string Quote(string value)
    {
        return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

internal sealed class SenderForm : Form
{
    private readonly string _settingsDirectory;
    private readonly string _settingsPath;
    private SenderSettings _settings;
    private TextBox _updated;
    private TextBox _state;
    private TextBox _repository;
    private TextBox _branch;
    private TextBox _log;
    private Label _status;
    private Button _send;
    private Button _cancel;
    private CancellationTokenSource _cancellation;

    public SenderForm()
    {
        _settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Relactive", "LotroSourceSender");
        _settingsPath = Path.Combine(_settingsDirectory, "settings.json");
        _settings = LoadSettings();
        Text = "LOTRO Kaynak Güncellemesi";
        Width = 780;
        Height = 520;
        MinimumSize = new Size(700, 440);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.25f);
        BackColor = Color.FromArgb(248, 246, 241);
        BuildUi();
        LoadValues();
        FormClosing += delegate { SaveSettings(); };
    }

    private void BuildUi()
    {
        TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 9, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        for (int i = 0; i < 7; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _updated = AddPathRow(table, 0, "Tek temiz DAT", "DAT seç", BrowseUpdated);
        _state = AddTextRow(table, 1, "Durum (otomatik)", true);
        _repository = AddTextRow(table, 2, "Private GitHub", false);
        _branch = AddTextRow(table, 3, "Branch", false);

        Label note = new Label
        {
            Text = "Tek temiz client_local_English.dat dosyasını her güncellemede yenisiyle değiştirin. Önceki katalog yerel durum dosyasından otomatik alınır; ham DAT gönderilmez.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = Color.FromArgb(70, 70, 70),
            Padding = new Padding(0, 5, 0, 0)
        };
        table.Controls.Add(note, 0, 5);
        table.SetColumnSpan(note, 3);

        _status = new Label { Text = "Hazır", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(30, 90, 50), Padding = new Padding(0, 5, 0, 0) };
        table.Controls.Add(_status, 0, 6);
        table.SetColumnSpan(_status, 2);
        FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        _send = new Button { Text = "GitHub'a Gönder", Width = 130, Height = 28, BackColor = Color.FromArgb(56, 120, 84), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        _cancel = new Button { Text = "İptal", Width = 70, Height = 28, Enabled = false, FlatStyle = FlatStyle.Flat };
        actions.Controls.Add(_send);
        actions.Controls.Add(_cancel);
        table.Controls.Add(actions, 2, 6);

        _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        table.Controls.Add(_log, 0, 7);
        table.SetColumnSpan(_log, 3);
        table.SetRowSpan(_log, 2);
        Controls.Add(table);
        _send.Click += async delegate { await SendAsync(); };
        _cancel.Click += delegate { _cancellation?.Cancel(); };
    }

    private TextBox AddPathRow(TableLayoutPanel table, int row, string label, string buttonText, EventHandler click)
    {
        Label caption = new Label { Text = label, Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 0) };
        TextBox box = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White };
        Button button = new Button { Text = buttonText, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        button.Click += click;
        table.Controls.Add(caption, 0, row);
        table.Controls.Add(box, 1, row);
        table.Controls.Add(button, 2, row);
        return box;
    }

    private TextBox AddTextRow(TableLayoutPanel table, int row, string label, bool readOnly)
    {
        Label caption = new Label { Text = label, Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 0) };
        TextBox box = new TextBox { Dock = DockStyle.Fill, ReadOnly = readOnly, BackColor = readOnly ? Color.FromArgb(240, 240, 240) : Color.White };
        table.Controls.Add(caption, 0, row);
        table.Controls.Add(box, 1, row);
        table.SetColumnSpan(box, 2);
        return box;
    }

    private void LoadValues()
    {
        _updated.Text = Existing(_settings.last_updated_dat);
        _state.Text = string.IsNullOrWhiteSpace(_settings.state_path) ? DefaultStatePath(_updated.Text) : _settings.state_path;
        _repository.Text = string.IsNullOrWhiteSpace(_settings.repository) ? "Relactive55/lotro-turkiye-yama-kaynak" : _settings.repository;
        _branch.Text = string.IsNullOrWhiteSpace(_settings.branch) ? "main" : _settings.branch;
    }

    private void BrowseUpdated(object sender, EventArgs e)
    {
        using (OpenFileDialog dialog = DatDialog("Tek temiz client_local_English.dat seçin"))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _updated.Text = dialog.FileName;
            if (!File.Exists(_state.Text)) _state.Text = DefaultStatePath(dialog.FileName);
            Append("Güncel DAT seçildi: " + Path.GetFileName(dialog.FileName));
        }
    }

    private async Task SendAsync()
    {
        string updated = (_updated.Text ?? "").Trim();
        string state = (_state.Text ?? "").Trim();
        string repository = (_repository.Text ?? "").Trim();
        string branch = (_branch.Text ?? "").Trim();
        if (!File.Exists(updated)) { ShowError("Önce güncel temiz DAT dosyasını seçin."); return; }
        if (string.IsNullOrWhiteSpace(state)) { ShowError("Yerel durum dosyası yolu boş."); return; }

        SaveSettings();
        _cancellation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            SourceUpdateExportResult result = await Task.Run(() => SourceUpdateExporter.Create(updated, "", state, Path.Combine(_settingsDirectory, "out"), "", _cancellation.Token, Append), _cancellation.Token);
            Append(string.Format("Karşılaştırma: yeni={0:N0}, değişmiş={1:N0}, belirsiz={2:N0}, gönderilecek={3:N0}", result.Summary.New, result.Summary.Modified, result.Summary.Ambiguous, result.CandidateRecordCount));
            if (result.Bundles.Count > 0)
            {
                GitHubSourceUploader uploader = new GitHubSourceUploader(repository, branch, Append);
                await Task.Run(() => uploader.Upload(result), _cancellation.Token);
                SourceUpdateExporter.CommitState(result);
                TryDeleteDirectory(result.OutputDirectory);
                Append("Kaynak durumu güncellendi; GitHub Actions otomatik çeviri için çalışacak.");
            }
            else if (result.BaselineInitialized)
            {
                SourceUpdateExporter.CommitState(result);
                TryDeleteDirectory(result.OutputDirectory);
                Append("İlk temiz DAT kaydedildi; patch oluşturulmadı. Sonraki güncellemede yalnızca yeni DAT'ı seçin.");
            }
            else
            {
                SourceUpdateExporter.CommitState(result);
                TryDeleteDirectory(result.OutputDirectory);
                Append("Yeni/değişmiş metin yok; yerel durum güncellendi.");
            }
            SetStatus("Tamamlandı", Color.FromArgb(30, 120, 60));
        }
        catch (OperationCanceledException)
        {
            Append("İşlem iptal edildi; yerel durum değiştirilmedi.");
            SetStatus("İptal edildi", Color.DarkOrange);
        }
        catch (Exception ex)
        {
            Append("Hata: " + ex.Message);
            SetStatus("Gönderim başarısız", Color.DarkRed);
            ShowError(ex.Message);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            SetBusy(false);
        }
    }

    private OpenFileDialog DatDialog(string title)
    {
        return new OpenFileDialog { Title = title, Filter = "LOTRO DAT|client_local_English.dat|DAT dosyaları|*.dat|Tüm dosyalar|*.*", CheckFileExists = true, Multiselect = false, InitialDirectory = ExistingDirectory(_updated?.Text) };
    }

    private static string DefaultStatePath(string updated)
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Relactive", "LotroSourceSender", "state", "catalog.jsonl.gz");
    }

    private SenderSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                SenderSettings value = serializer.Deserialize<SenderSettings>(File.ReadAllText(_settingsPath, Encoding.UTF8));
                if (value != null) return value;
            }
        }
        catch { }
        return new SenderSettings();
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            _settings.repository = (_repository?.Text ?? _settings.repository).Trim();
            _settings.branch = (_branch?.Text ?? _settings.branch).Trim();
            _settings.last_updated_dat = (_updated?.Text ?? _settings.last_updated_dat).Trim();
            _settings.state_path = (_state?.Text ?? _settings.state_path).Trim();
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            File.WriteAllText(_settingsPath, serializer.Serialize(_settings), new UTF8Encoding(false));
        }
        catch { }
    }

    private void SetBusy(bool busy)
    {
        if (InvokeRequired) { BeginInvoke(new Action<bool>(SetBusy), busy); return; }
        _send.Enabled = !busy;
        _cancel.Enabled = busy;
        UseWaitCursor = busy;
    }

    private void SetStatus(string text, Color color)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string, Color>(SetStatus), text, color); return; }
        _status.Text = text;
        _status.ForeColor = color;
    }

    private void Append(string text)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(Append), text); return; }
        _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
        SetStatus(text, Color.FromArgb(30, 90, 50));
    }

    private void ShowError(string text)
    {
        if (InvokeRequired) { BeginInvoke(new Action<string>(ShowError), text); return; }
        MessageBox.Show(this, text, "LOTRO Kaynak Güncellemesi", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
    }

    private static string Existing(string value) => !string.IsNullOrWhiteSpace(value) && File.Exists(value) ? value : "";
    private static string ExistingDirectory(string path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(Path.GetDirectoryName(path)) ? Path.GetDirectoryName(path) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private static void TryDeleteDirectory(string path)
    {
        try { if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }
}
