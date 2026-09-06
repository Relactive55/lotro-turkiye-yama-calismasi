using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LotroTurkceYama.Setup;

internal static class LotroSetupApp
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
    }
}

internal sealed class SetupForm : Form
{
    private readonly Label _status = new Label();
    private readonly Button _install = new Button();
    private readonly ProgressBar _progress = new ProgressBar();
    private CancellationTokenSource _cancel;
    private Tuple<StableRelease, ReleaseManifest> _available;
    private string _gameDirectory;

    public SetupForm()
    {
        Text = "LOTRO Türkçe Yama";
        Width = 560;
        Height = 220;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        _status.AutoSize = false;
        _status.SetBounds(24, 24, 500, 52);
        _status.Text = "Güncellemeler kontrol ediliyor...";
        _progress.SetBounds(24, 88, 500, 18);
        _install.Text = "Yama Yap";
        _install.SetBounds(24, 124, 140, 32);
        _install.Enabled = false;
        _install.Click += InstallClicked;
        Controls.Add(_status);
        Controls.Add(_progress);
        Controls.Add(_install);
        Shown += async (sender, args) => await CheckAsync();
        FormClosed += (sender, args) => { if (_cancel != null) _cancel.Cancel(); };
    }

    private async Task CheckAsync()
    {
        _install.Enabled = false;
        try
        {
            using (IReleaseTransport transport = new FixedGitHubTransport())
            {
                LotroReleaseUpdater updater = new LotroReleaseUpdater(transport);
                _available = await updater.CheckLatestAsync(CancellationToken.None);
            }
            _gameDirectory = LotroGameLocator.FindFirst();
            string statePath = _gameDirectory == null ? null : Path.Combine(_gameDirectory, "installed_patch.json");
            InstalledPatchState state = ReadState(statePath);
            if (state != null && state.asset_id == _available.Item2.asset_id && string.Equals(state.release_tag, _available.Item2.release_tag, StringComparison.Ordinal))
            {
                _status.Text = "Türkçe yamanız güncel.";
            }
            else
            {
                string kind = _available.Item2.asset_kind == LotroReleaseUpdater.SemanticPatchKind
                    ? "semantic delta"
                    : "tam DAT";
                _status.Text = "Yeni Türkçe yama bulundu (" + kind + "): " + _available.Item2.patch_version
                    + (_gameDirectory == null ? "\nLOTRO klasörü kurulum sırasında seçilecek." : "\nLOTRO otomatik bulundu.");
                _install.Enabled = true;
            }
        }
        catch (UpdaterFailure ex)
        {
            _status.Text = ex.Code == "NO_STABLE_RELEASE" ? "Yayınlanmış stable Türkçe yama bulunamadı." : "Güncelleme kontrol edilemedi.";
        }
        catch { _status.Text = "Güncelleme kontrol edilemedi."; }
    }

    private async void InstallClicked(object sender, EventArgs e)
    {
        if (_available == null) return;
        _install.Enabled = false;
        _progress.Value = 0;
        _cancel = new CancellationTokenSource();
        try
        {
            IProgress<DownloadProgress> progress = new Progress<DownloadProgress>(value =>
            {
                if (IsDisposed) return;
                _progress.Value = Math.Max(0, Math.Min(100, value.Percentage));
                _status.Text = "Yama indiriliyor... " + value.Percentage + "% (" + value.DownloadedBytes.ToString("N0") + "/" + value.TotalBytes.ToString("N0") + " bytes)";
            });
            using (IReleaseTransport transport = new FixedGitHubTransport())
            {
                LotroReleaseUpdater updater = new LotroReleaseUpdater(transport);
                string gameDir = _gameDirectory ?? LocateGameDirectory();
                if (string.IsNullOrWhiteSpace(gameDir)) throw new OperationCanceledException();
                LotroPathValidator.Validate(gameDir);
                string cache = Path.Combine(Path.GetTempPath(), "lotro-turkce-yama");
                await updater.DownloadPatchAsync(_available.Item1, _available.Item2, cache, _cancel.Token, progress);
                string statePath = Path.Combine(gameDir, "installed_patch.json");
                string patchPath = Path.Combine(cache, _available.Item2.asset_name);
                _status.Text = "Oyun sürümü doğrulanıyor ve Türkçe yama otomatik birleştiriliyor...";
                await updater.InstallPatchAsync(gameDir, patchPath, _available.Item2, statePath, _cancel.Token);
                _gameDirectory = gameDir;
                _status.Text = "Türkçe yama kuruldu.";
                MessageBox.Show(this, "Türkçe yama başarıyla kuruldu.", "LOTRO Türkçe Yama", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (OperationCanceledException) { _status.Text = "Kurulum iptal edildi."; }
        catch (UpdaterFailure ex) { MessageBox.Show(this, ex.Message, ex.Code, MessageBoxButtons.OK, MessageBoxIcon.Warning); _status.Text = "Kurulum yapılamadı."; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Kurulum yapılamadı", MessageBoxButtons.OK, MessageBoxIcon.Error); _status.Text = "Kurulum yapılamadı."; }
        finally
        {
            if (!IsDisposed) _install.Enabled = true;
            _cancel.Dispose();
            _cancel = null;
        }
    }

    private static string LocateGameDirectory()
    {
        string automatic = LotroGameLocator.FindFirst();
        if (automatic != null) return automatic;
        using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "LOTRO oyun klasörünü seçin" }) return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static InstalledPatchState ReadState(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<InstalledPatchState>(File.ReadAllText(path)); }
        catch { return null; }
    }
}
