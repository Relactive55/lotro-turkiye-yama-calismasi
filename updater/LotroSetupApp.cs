using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LotroTurkceYama.Setup;

internal static class LotroSetupApp
{
    internal const string ProductName = "LOTR TÜRKÇE YAMA";

    [STAThread]
    private static void Main()
    {
        using (Mutex singleInstance = new Mutex(true, "Local\\LOTRO_Turkce_Yama_Setup", out bool firstInstance))
        {
            if (!firstInstance)
            {
                MessageBox.Show("Kurulum aracı zaten açık. Açık pencereden devam edin.", ProductName);
                return;
            }
            // GitHub requires TLS 1.2 on .NET Framework installations too.
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }
}

internal sealed class SetupForm : Form
{
    private readonly Label _status = new Label();
    private readonly Button _install = new Button();
    private readonly Button _rollback = new Button();
    private readonly Button _support = new Button();
    private readonly Button _browse = new Button();
    private readonly TextBox _pathBox = new TextBox();
    private readonly ProgressBar _progress = new ProgressBar();
    private readonly Label _versions = new Label();
    private readonly Label _credit = new Label();
    private readonly ToolTip _toolTip = new ToolTip();
    private CancellationTokenSource _cancel;
    private Tuple<StableRelease, ReleaseManifest> _available;
    private string _gameDirectory;
    private bool _checking;
    private bool _closePending;

    public SetupForm()
    {
        Text = LotroSetupApp.ProductName;
        ClientSize = new Size(720, 540);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(12, 15, 20);
        BackgroundImageLayout = ImageLayout.Zoom;
        BackgroundImage = LoadBackgroundImage();
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        Panel surface = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(150, 0, 0, 0),
            Padding = new Padding(18, 16, 18, 12)
        };
        Controls.Add(surface);

        Label title = new Label
        {
            Text = "LOTR TÜRKÇE YAMA",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(20, 12)
        };
        surface.Controls.Add(title);

        _status.AutoSize = false;
        _status.SetBounds(20, 48, 664, 48);
        _status.Text = "Güncellemeler kontrol ediliyor...";
        _status.ForeColor = Color.White;
        _status.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
        _status.BackColor = Color.Transparent;
        surface.Controls.Add(_status);

        Label pathLabel = new Label
        {
            Text = "LOTRO Oyun Klasörü",
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(22, 304)
        };
        surface.Controls.Add(pathLabel);
        _pathBox.SetBounds(150, 300, 430, 28);
        _pathBox.ReadOnly = true;
        _pathBox.BackColor = Color.FromArgb(35, 35, 40);
        _pathBox.ForeColor = Color.White;
        _pathBox.BorderStyle = BorderStyle.FixedSingle;
        surface.Controls.Add(_pathBox);
        _browse.Text = "Gözat...";
        _browse.SetBounds(590, 300, 94, 28);
        _browse.FlatStyle = FlatStyle.Flat;
        _browse.BackColor = Color.FromArgb(55, 58, 68);
        _browse.ForeColor = Color.White;
        _browse.Click += BrowseClicked;
        surface.Controls.Add(_browse);

        _progress.SetBounds(20, 344, 664, 14);
        _progress.Style = ProgressBarStyle.Marquee;
        surface.Controls.Add(_progress);

        _install.Text = "YAMAYI KUR";
        _install.SetBounds(124, 376, 150, 42);
        _install.Enabled = false;
        StyleActionButton(_install, Color.FromArgb(42, 139, 105));
        _install.Click += InstallClicked;
        surface.Controls.Add(_install);

        _rollback.Text = "GERİ AL";
        _rollback.SetBounds(285, 376, 150, 42);
        _rollback.Enabled = false;
        StyleActionButton(_rollback, Color.FromArgb(42, 139, 105));
        _rollback.Click += RollbackClicked;
        surface.Controls.Add(_rollback);

        _support.Text = "DESTEK / BAĞIŞ";
        _support.SetBounds(446, 376, 150, 42);
        StyleActionButton(_support, Color.FromArgb(32, 117, 199));
        _support.Click += SupportClicked;
        _toolTip.SetToolTip(_support, "Teşekkürler");
        surface.Controls.Add(_support);

        _versions.SetBounds(20, 454, 430, 38);
        _versions.ForeColor = Color.White;
        _versions.BackColor = Color.Transparent;
        _versions.Text = "Program: " + LotroReleaseUpdater.CurrentUpdaterVersion + "\nYama: kontrol ediliyor...";
        surface.Controls.Add(_versions);
        _credit.SetBounds(548, 465, 136, 22);
        _credit.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
        _credit.ForeColor = Color.White;
        _credit.BackColor = Color.Transparent;
        _credit.Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold);
        _credit.Text = "Relactive";
        surface.Controls.Add(_credit);
        Shown += async (sender, args) => await CheckAsync();
        FormClosing += (sender, args) =>
        {
            if (_cancel == null) return;
            args.Cancel = true;
            _closePending = true;
            _cancel.Cancel();
            _install.Enabled = false;
            _status.Text = "Güvenli iptal tamamlanıyor; pencere ardından kapanacak...";
        };
    }

    private static Image LoadBackgroundImage()
    {
        try
        {
            Assembly assembly = typeof(SetupForm).Assembly;
            string resourceName = null;
            foreach (string name in assembly.GetManifestResourceNames())
                if (name.EndsWith("lotro-online-background.png", StringComparison.OrdinalIgnoreCase)) { resourceName = name; break; }
            if (resourceName != null)
            {
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                using (Image source = Image.FromStream(stream)) return new Bitmap(source);
            }
            string backgroundPath = Path.Combine(Application.StartupPath, "assets", "lotro-online-background.png");
            return File.Exists(backgroundPath) ? Image.FromFile(backgroundPath) : null;
        }
        catch { return null; }
    }

    private static void StyleActionButton(Button button, Color color)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private void BrowseClicked(object sender, EventArgs e)
    {
        using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "LOTRO oyun klasörünü seçin" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                LotroPathValidator.Validate(dialog.SelectedPath);
                _gameDirectory = dialog.SelectedPath;
                _pathBox.Text = _gameDirectory;
                _status.Text = "LOTRO klasörü seçildi. Hazır olduğunuzda yamayı kurabilirsiniz.";
            }
            catch (UpdaterFailure ex) { MessageBox.Show(this, ex.Message, ex.Code, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }

    private void SupportClicked(object sender, EventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://www.shopier.com/poe2tr/50856020") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, "Destek sayfası açılamadı: " + ex.Message, LotroSetupApp.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async void RollbackClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_gameDirectory)) return;
        if (MessageBox.Show(this, "Kurulu Türkçe yamayı kaldırıp temiz LOTRO DAT yedeğine dönmek istiyor musunuz?", "Geri Al", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            _rollback.Enabled = false;
            _install.Enabled = false;
            _progress.Style = ProgressBarStyle.Marquee;
            _status.Text = "Temiz LOTRO dosyası geri yükleniyor...";
            string statePath = Path.Combine(_gameDirectory, "installed_patch.json");
            using (IReleaseTransport transport = new FixedGitHubTransport())
                await Task.Run(() => new LotroReleaseUpdater(transport).RollbackInstalledPatchAsync(_gameDirectory, statePath, CancellationToken.None));
            _status.Text = "Yama geri alındı; temiz LOTRO DAT geri yüklendi.";
            UpdateVersionInfo(null);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Geri alma başarısız", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { _progress.Style = ProgressBarStyle.Continuous; _install.Enabled = true; _rollback.Enabled = false; }
    }

    private async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        _available = null;
        _install.Enabled = false;
        _install.Text = "Yama Yap";
        _status.Text = "Güncellemeler kontrol ediliyor...";
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.MarqueeAnimationSpeed = 24;
        try
        {
            using (IReleaseTransport transport = new FixedGitHubTransport())
            {
                LotroReleaseUpdater updater = new LotroReleaseUpdater(transport);
                Tuple<StableRelease, ReleaseManifest> available = await updater.CheckLatestAsync(CancellationToken.None);
                _available = available;
            }
            if (IsDisposed) return;
            if (string.IsNullOrWhiteSpace(_gameDirectory))
                _gameDirectory = await Task.Run(() => LotroGameLocator.FindFirst());
            if (IsDisposed) return;
            string statePath = _gameDirectory == null ? null : Path.Combine(_gameDirectory, "installed_patch.json");
            InstalledPatchState state = ReadState(statePath);
            _pathBox.Text = _gameDirectory ?? "LOTRO klasörü otomatik bulunamadı";
            UpdateVersionInfo(state);
            bool current = LotroReleaseUpdater.IsStateAtManifest(state, _available.Item2)
                && await Task.Run(() => LotroReleaseUpdater.IsInstalledFileValid(state, CancellationToken.None, _gameDirectory));
            if (IsDisposed) return;
            if (current)
            {
                _status.Text = "Türkçe yamanız güncel.";
                _install.Text = "Tekrar Kontrol Et";
                _install.Enabled = true;
                _rollback.Enabled = state != null && !string.IsNullOrWhiteSpace(state.source_backup_file);
            }
            else
            {
                string kind = _available.Item2.asset_kind == LotroReleaseUpdater.SemanticPatchKind
                    ? "Türkçe çeviri"
                    : "tam paket";
                _status.Text = "Yeni Türkçe yama bulundu (" + kind + "): " + _available.Item2.patch_version
                    + (_gameDirectory == null ? "\nLOTRO klasörü kurulum sırasında seçilecek." : "\nLOTRO otomatik bulundu.");
                _install.Text = "Yama Yap";
                _install.Enabled = true;
                _rollback.Enabled = state != null && !string.IsNullOrWhiteSpace(state.source_backup_file);
            }
        }
        catch (UpdaterFailure ex)
        {
            if (IsDisposed) return;
            if (ex.Code == "NO_STABLE_RELEASE")
                _status.Text = "Yayınlanmış kararlı Türkçe yama bulunamadı.";
            else if (ex.Code == "RELEASE_HTTP_FAILED" && ex.Message.EndsWith(": 404", StringComparison.Ordinal))
                _status.Text = "GitHub projesine dışarıdan erişilemiyor (404). Proje sahibi hesap kısıtlamasını kontrol etmelidir.";
            else if (ex.Code == "UPDATER_TOO_OLD")
                _status.Text = ex.Message;
            else if (ex.Code == "FULL_DAT_REQUIRED")
                _status.Text = ex.Message;
            else
                _status.Text = "Güncelleme kontrol edilemedi. İnternet bağlantınızı kontrol edip yeniden deneyin.";
            _install.Text = "Tekrar Dene";
            _install.Enabled = true;
            _rollback.Enabled = false;
        }
        catch
        {
            if (IsDisposed) return;
            _status.Text = "Güncelleme kontrol edilemedi. İnternet bağlantınızı kontrol edip yeniden deneyin.";
            _install.Text = "Tekrar Dene";
            _install.Enabled = true;
            _rollback.Enabled = false;
        }
        finally
        {
            _checking = false;
            if (!IsDisposed) _progress.Style = ProgressBarStyle.Continuous;
        }
    }

    private async void InstallClicked(object sender, EventArgs e)
    {
        if (_cancel != null)
        {
            _status.Text = "İptal ediliyor; mevcut oyun dosyanız korunuyor...";
            _install.Enabled = false;
            _cancel.Cancel();
            return;
        }
        if (_available != null && string.Equals(_install.Text, "Tekrar Kontrol Et", StringComparison.Ordinal))
        {
            _available = null;
            await CheckAsync();
            return;
        }
        if (_available == null)
        {
            await CheckAsync();
            return;
        }
        _install.Enabled = false;
        _progress.Value = 0;
        _progress.Style = ProgressBarStyle.Continuous;
        _cancel = new CancellationTokenSource();
        CancellationToken token = _cancel.Token;
        bool acceptingProgress = true;
        bool downloading = true;
        try
        {
            IProgress<DownloadProgress> progress = new Progress<DownloadProgress>(value =>
            {
                if (IsDisposed || !acceptingProgress || !downloading || token.IsCancellationRequested) return;
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = Math.Max(0, Math.Min(100, value.Percentage));
                _status.Text = "Yama indiriliyor... %" + value.Percentage + " (" + (value.DownloadedBytes / 1048576d).ToString("N1") + " / " + (value.TotalBytes / 1048576d).ToString("N1") + " MB)";
            });
            using (IReleaseTransport transport = new FixedGitHubTransport())
            {
                LotroReleaseUpdater updater = new LotroReleaseUpdater(transport);
                string gameDir = _gameDirectory ?? LocateGameDirectory();
                if (string.IsNullOrWhiteSpace(gameDir)) throw new OperationCanceledException();
                LotroPathValidator.Validate(gameDir);
                string cache = Path.Combine(Path.GetTempPath(), "lotro-turkce-yama");
                LotroReleaseUpdater.PrunePackageCache(cache, null, 2);
                string statePath = Path.Combine(gameDir, "installed_patch.json");
                InstalledPatchState installedState = ReadState(statePath);
                if (installedState != null && !string.Equals(installedState.game_dir, gameDir, StringComparison.OrdinalIgnoreCase)) installedState = null;
                _status.Text = _available.Item2.asset_kind == LotroReleaseUpdater.SemanticPatchKind
                    ? "İmzalı çeviri paketi doğrulanıyor..."
                    : "Tam Türkçe DAT paketi doğrulanıyor...";
                _progress.Style = ProgressBarStyle.Marquee;
                _progress.MarqueeAnimationSpeed = 24;
                _install.Text = "İptal";
                _install.Enabled = true;
                List<PatchPackage> packages = await Task.Run(() => updater.DownloadPatchChainAsync(
                    _available.Item1,
                    _available.Item2,
                    cache,
                    installedState,
                    token,
                    progress), token);
                downloading = false;
                if (packages.Count == 0)
                {
                    _status.Text = "Türkçe yamanız güncel.";
                    return;
                }
                _progress.Style = ProgressBarStyle.Marquee;
                _progress.MarqueeAnimationSpeed = 24;
                _status.Text = "Oyun sürümü doğrulanıyor; Türkçe yama hazırlanıyor...";
                _install.Text = "İptal";
                _install.Enabled = true;
                Action<string> installProgress = message =>
                {
                    if (IsDisposed || !IsHandleCreated || !acceptingProgress || token.IsCancellationRequested) return;
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            if (!IsDisposed && acceptingProgress && !token.IsCancellationRequested)
                            {
                                _progress.Style = ProgressBarStyle.Marquee;
                                _status.Text = message;
                            }
                        }));
                    }
                    catch (InvalidOperationException) { }
                };
                // DAT parsing/rebuild is CPU and disk intensive. Keep it off
                // the WinForms thread so the window continues animating and
                // the user can see each verified phase instead of a freeze.
                await Task.Run(
                    () => updater.InstallPatchChainAsync(gameDir, packages, statePath, token, installProgress),
                    token);
                LotroReleaseUpdater.PrunePackageCache(cache, new[] { _available.Item2.asset_sha256 }, 2);
                acceptingProgress = false;
                _gameDirectory = gameDir;
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = 100;
                _status.Text = "Türkçe yama kuruldu.";
                InstalledPatchState installed = ReadState(statePath);
                UpdateVersionInfo(installed);
                _rollback.Enabled = installed != null && !string.IsNullOrWhiteSpace(installed.source_backup_file);
                MessageBox.Show(this, "Türkçe yama başarıyla kuruldu.", LotroSetupApp.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException) { acceptingProgress = false; _progress.Style = ProgressBarStyle.Continuous; _status.Text = "Kurulum iptal edildi."; }
        catch (UpdaterFailure ex) when (ex.Code == "PATCH_RELEASE_PENDING")
        {
            acceptingProgress = false;
            MessageBox.Show(this, ex.Message, "Yeni Türkçe yama bekleniyor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _progress.Style = ProgressBarStyle.Continuous;
            _status.Text = "Yeni oyun sürümü için Türkçe yama hazırlanıyor.";
        }
        catch (UpdaterFailure ex) { acceptingProgress = false; _progress.Style = ProgressBarStyle.Continuous; MessageBox.Show(this, ex.Message, ex.Code, MessageBoxButtons.OK, MessageBoxIcon.Warning); _status.Text = "Kurulum yapılamadı."; }
        catch (Exception ex) { acceptingProgress = false; _progress.Style = ProgressBarStyle.Continuous; MessageBox.Show(this, ex.Message, "Kurulum yapılamadı", MessageBoxButtons.OK, MessageBoxIcon.Error); _status.Text = "Kurulum yapılamadı."; }
        finally
        {
            acceptingProgress = false;
            if (!IsDisposed)
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.MarqueeAnimationSpeed = 0;
                _install.Text = "Yama Yap";
                _install.Enabled = true;
            }
            _cancel.Dispose();
            _cancel = null;
            if (_closePending && !IsDisposed) Close();
        }
    }

    private void UpdateVersionInfo(InstalledPatchState state)
    {
        _versions.Text = "Program: " + LotroReleaseUpdater.CurrentUpdaterVersion
            + "\nKurulu yama: " + (state?.patch_version ?? "yok")
            + " | Son: " + (_available?.Item2.patch_version ?? "bilinmiyor");
    }

    private static string LocateGameDirectory()
    {
        string automatic = LotroGameLocator.FindFirst();
        if (automatic != null) return automatic;
        using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "LOTRO oyun klasörünü seçin" }) return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static InstalledPatchState ReadState(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (StreamReader reader = new StreamReader(stream))
                return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<InstalledPatchState>(reader.ReadToEnd());
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) { throw new UpdaterFailure("STATE_IO_FAILED", "Kurulu yama state dosyası okunamadı: " + ex.Message); }
    }
}
