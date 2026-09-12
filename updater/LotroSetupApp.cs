using System;
using System.Collections.Generic;
using System.IO;
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
    private readonly ProgressBar _progress = new ProgressBar();
    private readonly Label _versions = new Label();
    private readonly Label _credit = new Label();
    private CancellationTokenSource _cancel;
    private Tuple<StableRelease, ReleaseManifest> _available;
    private string _gameDirectory;
    private bool _checking;
    private bool _closePending;

    public SetupForm()
    {
        Text = LotroSetupApp.ProductName;
        Width = 560;
        Height = 260;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
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
        _versions.SetBounds(24, 172, 390, 36);
        _versions.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        _versions.ForeColor = System.Drawing.Color.Black;
        _versions.Text = "Program: " + LotroReleaseUpdater.CurrentUpdaterVersion + "\nYama: kontrol ediliyor...";
        _credit.SetBounds(420, 184, 104, 22);
        _credit.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        _credit.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
        _credit.ForeColor = System.Drawing.Color.Black;
        _credit.Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold);
        _credit.Text = "Relactive";
        Controls.Add(_versions);
        Controls.Add(_credit);
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
                if (available.Item2 == null || available.Item2.asset_kind != "full_dat")
                    throw new UpdaterFailure("FULL_DAT_REQUIRED", "Bu sürüm yalnızca eksiksiz Türkçe DAT paketi kullanır; yama katmanı yayınlanmıyor.");
                _available = available;
            }
            if (IsDisposed) return;
            _gameDirectory = await Task.Run(() => LotroGameLocator.FindFirst());
            if (IsDisposed) return;
            string statePath = _gameDirectory == null ? null : Path.Combine(_gameDirectory, "installed_patch.json");
            InstalledPatchState state = ReadState(statePath);
            UpdateVersionInfo(state);
            bool current = LotroReleaseUpdater.IsStateAtManifest(state, _available.Item2)
                && await Task.Run(() => LotroReleaseUpdater.IsInstalledFileValid(state, CancellationToken.None, _gameDirectory));
            if (IsDisposed) return;
            if (current)
            {
                _status.Text = "Türkçe yamanız güncel.";
                _install.Text = "Tekrar Kontrol Et";
                _install.Enabled = true;
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
        }
        catch
        {
            if (IsDisposed) return;
            _status.Text = "Güncelleme kontrol edilemedi. İnternet bağlantınızı kontrol edip yeniden deneyin.";
            _install.Text = "Tekrar Dene";
            _install.Enabled = true;
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
        if (_available == null)
        {
            await CheckAsync();
            return;
        }
        if (_available.Item2 == null || _available.Item2.asset_kind != "full_dat")
        {
            MessageBox.Show(this, "Bu sürüm yalnızca eksiksiz Türkçe DAT paketi kullanır; yama katmanı yayınlanmıyor.", LotroSetupApp.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                string statePath = Path.Combine(gameDir, "installed_patch.json");
                InstalledPatchState installedState = ReadState(statePath);
                if (installedState != null && !string.Equals(installedState.game_dir, gameDir, StringComparison.OrdinalIgnoreCase)) installedState = null;
                _status.Text = "Tam Türkçe DAT paketi doğrulanıyor...";
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
                acceptingProgress = false;
                _gameDirectory = gameDir;
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Value = 100;
                _status.Text = "Türkçe yama kuruldu.";
                UpdateVersionInfo(ReadState(statePath));
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
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<InstalledPatchState>(File.ReadAllText(path)); }
        catch { return null; }
    }
}
