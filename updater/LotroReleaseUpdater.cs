using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using LotroTrGemini;

namespace LotroTurkceYama.Setup;

public sealed class UpdaterFailure : Exception
{
    public string Code { get; }

    public UpdaterFailure(string code, string message) : base(message)
    {
        Code = code;
    }
}

public sealed class ReleaseManifest
{
    public int schema_version { get; set; }
    public string patch_version { get; set; }
    public string release_tag { get; set; }
    public long release_id { get; set; }
    public long asset_id { get; set; }
    public string asset_name { get; set; }
    public long asset_size { get; set; }
    public string asset_sha256 { get; set; }
    public string source_dat_sha256 { get; set; }
    public long source_dat_size { get; set; }
    public string game_version { get; set; }
    public string candidate_catalog_sha256 { get; set; }
    public string asset_kind { get; set; }
    public string minimum_updater_version { get; set; }
    public string source_catalog_sha256 { get; set; }
    public string translation_catalog_version { get; set; }
    public string patch_generator_version { get; set; }
    public string translation_provider { get; set; }
    public string translation_model_version { get; set; }
    public int safe_translated_count { get; set; }
    public int skipped_changed_count { get; set; }
    public int critical_review_required_count { get; set; }
}

public sealed class InstalledPatchState
{
    public string game_version { get; set; }
    public string source_dat_sha256 { get; set; }
    public string patch_version { get; set; }
    public string release_tag { get; set; }
    public long release_id { get; set; }
    public long asset_id { get; set; }
    public string file { get; set; }
    public string sha256 { get; set; }
    public long size { get; set; }
    public string game_dir { get; set; }
    public string source_backup_file { get; set; }
    public string source_backup_sha256 { get; set; }
    public string source_backup_catalog_sha256 { get; set; }
    public string candidate_catalog_sha256 { get; set; }
    public string installed_at { get; set; }
}

public sealed class ReleaseAsset
{
    public long id { get; set; }
    public string name { get; set; }
    public string browser_download_url { get; set; }
    public long size { get; set; }
}

public sealed class StableRelease
{
    public long id { get; set; }
    public string tag_name { get; set; }
    public bool draft { get; set; }
    public bool prerelease { get; set; }
    public ReleaseAsset[] assets { get; set; }
}

public interface IReleaseTransport : IDisposable
{
    Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken);
    Task<DownloadResult> DownloadAsync(Uri uri, string partPath, long expectedSize, string expectedSha256, CancellationToken cancellationToken);
}

public interface IProgressReleaseTransport : IReleaseTransport
{
    Task<DownloadResult> DownloadAsync(Uri uri, string partPath, long expectedSize, string expectedSha256, IProgress<DownloadProgress> progress, CancellationToken cancellationToken);
}

public sealed class DownloadProgress
{
    public long DownloadedBytes { get; internal set; }
    public long TotalBytes { get; internal set; }
    public int Percentage { get; internal set; }
}

public sealed class DownloadResult
{
    public long Size { get; set; }
    public string Sha256 { get; set; }
}

public sealed class FixedGitHubTransport : IProgressReleaseTransport
{
    private static readonly HashSet<string> AllowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"
    };

    private readonly HttpClient _client;

    public FixedGitHubTransport()
    {
        HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false };
        _client = new HttpClient(handler, true);
        _client.Timeout = TimeSpan.FromMinutes(30);
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("LOTRO-Turkce-Yama-Setup/1");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        using (HttpResponseMessage response = await SendAllowedAsync(uri, cancellationToken).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
                throw new UpdaterFailure("RELEASE_HTTP_FAILED", "GitHub isteği başarısız: " + (int)response.StatusCode);
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
    }

    public async Task<DownloadResult> DownloadAsync(Uri uri, string partPath, long expectedSize, string expectedSha256, CancellationToken cancellationToken)
    {
        return await DownloadAsync(uri, partPath, expectedSize, expectedSha256, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DownloadResult> DownloadAsync(Uri uri, string partPath, long expectedSize, string expectedSha256, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        if (expectedSize < 1) throw new UpdaterFailure("DOWNLOAD_VERIFICATION_FAILED", "Beklenen asset boyutu geçersiz.");
        EnsureDiskSpace(partPath, expectedSize);
        string directory = Path.GetDirectoryName(partPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        try
        {
            using (HttpResponseMessage response = await SendAllowedAsync(uri, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    throw new UpdaterFailure("DOWNLOAD_HTTP_FAILED", "Yama asset isteği başarısız: " + (int)response.StatusCode);
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != expectedSize)
                    throw new UpdaterFailure("DOWNLOAD_VERIFICATION_FAILED", "Content-Length manifest ile uyuşmuyor.");

                using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (FileStream output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] buffer = new byte[1024 * 1024];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > expectedSize) throw new UpdaterFailure("DOWNLOAD_VERIFICATION_FAILED", "İndirilen asset beklenenden büyük.");
                        sha.TransformBlock(buffer, 0, read, null, 0);
                        await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                        progress?.Report(new DownloadProgress
                        {
                            DownloadedBytes = total,
                            TotalBytes = expectedSize,
                            Percentage = (int)Math.Min(100L, total * 100L / expectedSize)
                        });
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    output.Flush(true);
                    string actual = ToHex(sha.Hash);
                    if (total != expectedSize || !string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new UpdaterFailure("DOWNLOAD_VERIFICATION_FAILED", "Asset boyutu veya SHA-256 değeri uyuşmuyor.");
                    return new DownloadResult { Size = total, Sha256 = actual };
                }
            }
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAllowedAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                HttpResponseMessage response = await SendAllowedOnceAsync(uri, cancellationToken).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                bool transient = status == 408 || status == 429 || status >= 500;
                if (!transient || attempt == 2) return response;
                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < 2) { }
            await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), cancellationToken).ConfigureAwait(false);
        }
        throw new UpdaterFailure("RELEASE_HTTP_FAILED", "GitHub bağlantısı kurulamadı.");
    }

    private async Task<HttpResponseMessage> SendAllowedOnceAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (int redirect = 0; redirect < 5; redirect++)
        {
            ValidateUri(uri);
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode < 300 || (int)response.StatusCode >= 400) return response;
                Uri next = response.Headers.Location == null ? null : new Uri(uri, response.Headers.Location);
                response.Dispose();
                if (next == null) throw new UpdaterFailure("REDIRECT_REJECTED", "GitHub yönlendirmesi konum içermiyor.");
                uri = next;
            }
        }
        throw new UpdaterFailure("REDIRECT_REJECTED", "Yönlendirme limiti aşıldı.");
    }

    public static void ValidateUri(Uri uri)
    {
        if (uri == null || uri.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(uri.Host))
            throw new UpdaterFailure("ENDPOINT_REJECTED", "İzin verilmeyen veya HTTPS olmayan GitHub endpoint'i.");
    }

    public static void ValidateReleaseAssetUri(Uri uri, string releaseTag, string assetName)
    {
        ValidateUri(uri);
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return;
        string expectedPrefix = "/" + LotroReleaseUpdater.Owner + "/" + LotroReleaseUpdater.Repository + "/releases/download/" + releaseTag + "/";
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (!path.StartsWith(expectedPrefix, StringComparison.Ordinal) || !string.Equals(path.Substring(expectedPrefix.Length), assetName, StringComparison.Ordinal))
            throw new UpdaterFailure("ENDPOINT_REJECTED", "Release asset yolu sabit repository ile eşleşmiyor.");
    }

    public void Dispose() { _client.Dispose(); }

    internal static string ToHex(byte[] bytes)
    {
        StringBuilder b = new StringBuilder(bytes.Length * 2);
        foreach (byte value in bytes) b.Append(value.ToString("x2"));
        return b.ToString();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void EnsureDiskSpace(string path, long expectedSize)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) throw new IOException("cache drive is unknown");
            DriveInfo drive = new DriveInfo(root);
            long margin = Math.Max(16L * 1024 * 1024, Math.Min(256L * 1024 * 1024, expectedSize / 100));
            if (drive.AvailableFreeSpace < expectedSize + margin)
                throw new UpdaterFailure("INSUFFICIENT_DISK_SPACE", "Yama indirmek için yeterli disk alanı yok.");
        }
        catch (UpdaterFailure) { throw; }
        catch (Exception ex) { throw new UpdaterFailure("DISK_SPACE_CHECK_FAILED", ex.Message); }
    }
}

public sealed class LotroReleaseUpdater
{
    public const string Owner = "Relactive55";
    public const string Repository = "lotro-turkiye-yama-calismasi";
    public const string ManifestAssetName = "manifest.json";
    public const string PatchAssetPrefix = "lotro-turkce-yama-";
    public const string SemanticPatchKind = "semantic_delta_patch";
    public static readonly Uri LatestReleaseUri = new Uri("https://api.github.com/repos/Relactive55/lotro-turkiye-yama-calismasi/releases/latest");

    private readonly IReleaseTransport _transport;
    private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

    public LotroReleaseUpdater(IReleaseTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<Tuple<StableRelease, ReleaseManifest>> CheckLatestAsync(CancellationToken cancellationToken)
    {
        string releaseJson = await _transport.GetStringAsync(LatestReleaseUri, cancellationToken).ConfigureAwait(false);
        StableRelease release;
        try { release = _json.Deserialize<StableRelease>(releaseJson); }
        catch (Exception ex) { throw new UpdaterFailure("RELEASE_JSON_INVALID", ex.Message); }
        if (release == null || release.draft || release.prerelease || release.id < 1 || string.IsNullOrWhiteSpace(release.tag_name))
            throw new UpdaterFailure("NO_STABLE_RELEASE", "Kullanılabilir stable release yok.");
        ReleaseAsset manifestAsset = FindUniqueAsset(release.assets, ManifestAssetName);
        if (manifestAsset == null || manifestAsset.id < 1 || string.IsNullOrWhiteSpace(manifestAsset.browser_download_url))
            throw new UpdaterFailure("MANIFEST_ASSET_MISSING", "Stable release içinde manifest.json bulunamadı.");
        FixedGitHubTransport.ValidateReleaseAssetUri(new Uri(manifestAsset.browser_download_url), release.tag_name, ManifestAssetName);
        string manifestJson = await _transport.GetStringAsync(new Uri(manifestAsset.browser_download_url), cancellationToken).ConfigureAwait(false);
        if (manifestAsset.size < 1 || Encoding.UTF8.GetByteCount(manifestJson) != manifestAsset.size)
            throw new UpdaterFailure("MANIFEST_VERIFICATION_FAILED", "Manifest boyutu release asset ile uyuşmuyor.");
        ReleaseManifest manifest;
        try { manifest = _json.Deserialize<ReleaseManifest>(manifestJson); }
        catch (Exception ex) { throw new UpdaterFailure("MANIFEST_JSON_INVALID", ex.Message); }
        ManifestValidator.Validate(manifest, release, manifestAsset);
        ReleaseAsset patchAsset = FindUniqueAsset(release.assets, manifest.asset_name);
        if (patchAsset == null || patchAsset.id != manifest.asset_id || patchAsset.size != manifest.asset_size || string.IsNullOrWhiteSpace(patchAsset.browser_download_url))
            throw new UpdaterFailure("PATCH_ASSET_MISSING", "Manifest'teki patch asset stable release ile eşleşmiyor.");
        FixedGitHubTransport.ValidateReleaseAssetUri(new Uri(patchAsset.browser_download_url), release.tag_name, manifest.asset_name);
        return Tuple.Create(release, manifest);
    }

    public Task<DownloadResult> DownloadPatchAsync(StableRelease release, ReleaseManifest manifest, string cacheDirectory, CancellationToken cancellationToken)
    {
        return DownloadPatchAsync(release, manifest, cacheDirectory, cancellationToken, null);
    }

    public Task<DownloadResult> DownloadPatchAsync(StableRelease release, ReleaseManifest manifest, string cacheDirectory, CancellationToken cancellationToken, IProgress<DownloadProgress> progress)
    {
        if (release == null) throw new ArgumentNullException(nameof(release));
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        if (manifest.asset_kind != "full_dat" && manifest.asset_kind != SemanticPatchKind)
            throw new UpdaterFailure("UNSUPPORTED_ASSET_KIND", "Release asset türü desteklenmiyor.");
        if (!IsSafeFileName(manifest.asset_name)) throw new UpdaterFailure("ASSET_NAME_INVALID", "Asset adı güvenli değil.");
        ReleaseAsset patchAsset = FindUniqueAsset(release.assets, manifest.asset_name);
        if (patchAsset == null || patchAsset.id != manifest.asset_id || patchAsset.size != manifest.asset_size || string.IsNullOrWhiteSpace(patchAsset.browser_download_url))
            throw new UpdaterFailure("PATCH_ASSET_MISSING", "Patch asset stable release ile eşleşmiyor.");
        FixedGitHubTransport.ValidateReleaseAssetUri(new Uri(patchAsset.browser_download_url), release.tag_name, manifest.asset_name);
        string path = Path.Combine(cacheDirectory, manifest.asset_name);
        string part = path + ".part";
        return DownloadPatchCoreAsync(patchAsset.browser_download_url, manifest, path, part, cancellationToken, progress);
    }

    private async Task<DownloadResult> DownloadPatchCoreAsync(string url, ReleaseManifest manifest, string path, string part, CancellationToken cancellationToken, IProgress<DownloadProgress> progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            FileInfo cached = new FileInfo(path);
            if (cached.Length == manifest.asset_size && string.Equals(HashFile(path), manifest.asset_sha256, StringComparison.OrdinalIgnoreCase))
                return new DownloadResult { Size = cached.Length, Sha256 = manifest.asset_sha256 };
            TryDelete(path);
        }
        DownloadResult result;
        if (progress != null && _transport is IProgressReleaseTransport progressTransport)
            result = await progressTransport.DownloadAsync(new Uri(url), part, manifest.asset_size, manifest.asset_sha256, progress, cancellationToken).ConfigureAwait(false);
        else
            result = await _transport.DownloadAsync(new Uri(url), part, manifest.asset_size, manifest.asset_sha256, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(part, path);
        return result;
    }

    public Task<InstalledPatchState> InstallFullDatAsync(string gameDirectory, string patchPath, ReleaseManifest manifest, string statePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LotroPathValidator.Validate(gameDirectory);
        ProcessGuard.EnsureClosed();
        if (manifest == null || manifest.asset_kind != "full_dat") throw new UpdaterFailure("UNSUPPORTED_ASSET_KIND", "Yalnız full_dat release kurulabilir.");
        VerifyFile(patchPath, manifest.asset_size, manifest.asset_sha256);
        string target = Path.Combine(gameDirectory, "client_local_English.dat");
        if (!File.Exists(target)) throw new UpdaterFailure("LOTRO_DAT_MISSING", "LOTRO client_local_English.dat bulunamadı.");
        EnsureDatUnlocked(target);
        string priorStateText = TryRead(statePath);
        InstalledPatchState priorState = ParseState(priorStateText);
        string currentHash = HashFile(target);
        bool cleanBaseline = string.Equals(currentHash, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase);
        bool knownPreviousPatch = priorState != null
            && string.Equals(priorState.source_dat_sha256, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentHash, priorState.sha256, StringComparison.OrdinalIgnoreCase)
            && priorState.size == new FileInfo(target).Length;
        if (!cleanBaseline && !knownPreviousPatch)
            throw new UpdaterFailure("OUTDATED_LOTRO_PATCH", "Mevcut LOTRO DAT için release baseline kimliği doğrulanamadı.");

        string backup = BackupFile(target, gameDirectory);
        string candidate = target + ".lotro-candidate.part";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyAndVerify(patchPath, candidate, manifest.asset_size, manifest.asset_sha256, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceFile(candidate, target);
            VerifyFile(target, manifest.asset_size, manifest.asset_sha256);
            cancellationToken.ThrowIfCancellationRequested();
            InstalledPatchState state = new InstalledPatchState
            {
                game_version = manifest.game_version,
                source_dat_sha256 = manifest.source_dat_sha256,
                patch_version = manifest.patch_version,
                release_tag = manifest.release_tag,
                release_id = manifest.release_id,
                asset_id = manifest.asset_id,
                file = target,
                sha256 = manifest.asset_sha256,
                size = manifest.asset_size,
                game_dir = gameDirectory,
                installed_at = DateTime.UtcNow.ToString("o")
            };
            WriteStateAtomic(statePath, state);
            return Task.FromResult(state);
        }
        catch
        {
            TryRestore(backup, target);
            if (priorStateText == null) TryDelete(statePath); else WriteTextAtomic(statePath, priorStateText);
            TryDelete(statePath + ".part");
            throw;
        }
        finally { TryDelete(candidate); }
    }

    /// <summary>Common verified install entry point.</summary>
    public Task<InstalledPatchState> InstallPatchAsync(string gameDirectory, string patchPath, ReleaseManifest manifest, string statePath, CancellationToken cancellationToken)
    {
        if (manifest != null && manifest.asset_kind == SemanticPatchKind)
            return InstallSemanticDeltaPatchAsync(gameDirectory, patchPath, manifest, statePath, cancellationToken);
        return InstallFullDatAsync(gameDirectory, patchPath, manifest, statePath, cancellationToken);
    }

    public Task<InstalledPatchState> InstallSemanticDeltaPatchAsync(string gameDirectory, string patchPath, ReleaseManifest manifest, string statePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LotroPathValidator.Validate(gameDirectory);
        ProcessGuard.EnsureClosed();
        if (manifest == null || manifest.asset_kind != SemanticPatchKind)
            throw new UpdaterFailure("UNSUPPORTED_ASSET_KIND", "Semantic delta patch bekleniyordu.");
        VerifyFile(patchPath, manifest.asset_size, manifest.asset_sha256);

        SemanticPatchDocument document;
        try
        {
            document = SemanticPatchSerializer.Deserialize(File.ReadAllText(patchPath, Encoding.UTF8));
        }
        catch (Exception ex)
        {
            throw new UpdaterFailure("SEMANTIC_PATCH_INVALID", "Semantic patch doğrulanamadı: " + ex.Message);
        }
        if (!string.Equals(document.source_dat_sha256, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("SEMANTIC_PATCH_BASELINE_MISMATCH", "Semantic patch baseline manifest ile eşleşmiyor.");
        if (!string.IsNullOrWhiteSpace(manifest.source_catalog_sha256)
            && !string.Equals(document.source_catalog_sha256, manifest.source_catalog_sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("SEMANTIC_PATCH_CATALOG_MISMATCH", "Semantic patch catalog kimliği manifest ile eşleşmiyor.");

        string target = Path.Combine(gameDirectory, "client_local_English.dat");
        if (!File.Exists(target)) throw new UpdaterFailure("LOTRO_DAT_MISSING", "LOTRO client_local_English.dat bulunamadı.");
        EnsureDatUnlocked(target);
        string priorStateText = TryRead(statePath);
        InstalledPatchState priorState = ParseState(priorStateText);
        string currentHash = HashFile(target);
        long currentSize = new FileInfo(target).Length;

        // Re-running the same release is an idempotent success. This is the
        // ordinary path after a user opens the updater more than once.
        if (priorState != null
            && priorState.release_id == manifest.release_id
            && priorState.asset_id == manifest.asset_id
            && priorState.size == currentSize
            && string.Equals(priorState.sha256, currentHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(priorState.candidate_catalog_sha256, manifest.candidate_catalog_sha256, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(priorState);

        bool currentIsCleanBaseline = currentSize == manifest.source_dat_size
            && string.Equals(currentHash, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase);
        string cleanSource = null;
        if (currentIsCleanBaseline)
        {
            cleanSource = target;
        }
        else if (priorState != null
            && priorState.size == currentSize
            && string.Equals(priorState.sha256, currentHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(priorState.source_backup_sha256, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(priorState.source_backup_file)
            && File.Exists(priorState.source_backup_file))
        {
            FileInfo backupInfo = new FileInfo(priorState.source_backup_file);
            if (backupInfo.Length == manifest.source_dat_size
                && string.Equals(HashFile(priorState.source_backup_file), manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase))
                cleanSource = priorState.source_backup_file;
        }
        if (cleanSource == null)
            cleanSource = FindVerifiedCleanSourceBackup(gameDirectory, manifest);
        if (cleanSource == null
            && priorState != null
            && !string.Equals(priorState.source_dat_sha256, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase)
            && TryGetPriorCleanBackup(gameDirectory, priorState, out string priorCleanBackup))
        {
            return RecoverAndInstallUpdatedPatchedDat(
                gameDirectory,
                target,
                priorCleanBackup,
                document,
                manifest,
                statePath,
                priorStateText,
                currentSize,
                cancellationToken);
        }
        if (cleanSource == null
            && priorState != null
            && string.Equals(priorState.source_dat_sha256, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure(
                "PATCH_RELEASE_PENDING",
                "LOTRO güncellenmiş, fakat bu oyun sürümü için yeni Türkçe paket henüz yayımlanmamış. Oyun dosyanız değiştirilmedi; programı daha sonra yeniden açmanız yeterli.");
        if (cleanSource == null)
            throw new UpdaterFailure("OUTDATED_LOTRO_PATCH", "Bu DAT güvenli biçimde otomatik birleştirilemedi. Program hiçbir oyun dosyasını değiştirmedi.");

        string candidate = target + ".lotro-candidate.part";
        TryDelete(candidate);
        string expectedSourceBackup = CleanSourceBackupPath(gameDirectory, manifest);
        long candidateAllowance = checked(manifest.source_dat_size + Math.Max(256L * 1024 * 1024, manifest.source_dat_size / 10));
        long requiredSpace = candidateAllowance + 64L * 1024 * 1024;
        if (!File.Exists(expectedSourceBackup)) requiredSpace = checked(requiredSpace + manifest.source_dat_size);
        if (!currentIsCleanBaseline) requiredSpace = checked(requiredSpace + currentSize);
        EnsureFreeSpace(gameDirectory, requiredSpace);

        string sourceBackup = EnsureCleanSourceBackup(cleanSource, gameDirectory, manifest, cancellationToken);
        // On a first install the verified clean-source backup is also a complete
        // rollback copy. Subsequent installs keep the currently patched DAT as a
        // separate rollback point.
        string rollback = currentIsCleanBaseline ? sourceBackup : BackupFile(target, gameDirectory);
        try
        {
            ManagedSemanticDatPatcher.Result built = ManagedSemanticDatPatcher.BuildCandidate(
                sourceBackup,
                candidate,
                document,
                manifest.candidate_catalog_sha256,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceFile(candidate, target);
            VerifyFile(target, built.DatSize, built.DatSha256);
            cancellationToken.ThrowIfCancellationRequested();
            InstalledPatchState state = new InstalledPatchState
            {
                game_version = manifest.game_version,
                source_dat_sha256 = manifest.source_dat_sha256,
                patch_version = manifest.patch_version,
                release_tag = manifest.release_tag,
                release_id = manifest.release_id,
                asset_id = manifest.asset_id,
                file = target,
                sha256 = built.DatSha256,
                size = built.DatSize,
                game_dir = gameDirectory,
                source_backup_file = sourceBackup,
                source_backup_sha256 = HashFile(sourceBackup),
                source_backup_catalog_sha256 = manifest.source_catalog_sha256,
                candidate_catalog_sha256 = built.CatalogSha256,
                installed_at = DateTime.UtcNow.ToString("o")
            };
            WriteStateAtomic(statePath, state);
            return Task.FromResult(state);
        }
        catch
        {
            TryRestore(rollback, target);
            if (priorStateText == null) TryDelete(statePath); else WriteTextAtomic(statePath, priorStateText);
            TryDelete(statePath + ".part");
            throw;
        }
        finally { TryDelete(candidate); }
    }

    private Task<InstalledPatchState> RecoverAndInstallUpdatedPatchedDat(
        string gameDirectory,
        string target,
        string priorCleanBackup,
        SemanticPatchDocument document,
        ReleaseManifest manifest,
        string statePath,
        string priorStateText,
        long currentSize,
        CancellationToken cancellationToken)
    {
        string candidate = target + ".lotro-candidate.part";
        string cleanCandidate = target + ".clean-recovery.part";
        string sourceBackup = SemanticCleanSourceBackupPath(gameDirectory, manifest);
        long allowance = checked(Math.Max(currentSize, manifest.source_dat_size) + Math.Max(256L * 1024 * 1024, currentSize / 10));
        EnsureFreeSpace(gameDirectory, checked(allowance * 3L + 64L * 1024 * 1024));
        string rollback = BackupFile(target, gameDirectory);
        TryDelete(candidate);
        TryDelete(cleanCandidate);
        try
        {
            ManagedSemanticDatPatcher.RecoveryResult recovered = ManagedSemanticDatPatcher.RecoverUpdatedPatchedDat(
                target,
                priorCleanBackup,
                cleanCandidate,
                candidate,
                document,
                manifest.candidate_catalog_sha256,
                cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(sourceBackup));
            MoveOrReplace(cleanCandidate, sourceBackup);
            VerifyFile(sourceBackup, recovered.CleanSource.DatSize, recovered.CleanSource.DatSha256);
            ReplaceFile(candidate, target);
            VerifyFile(target, recovered.Translated.DatSize, recovered.Translated.DatSha256);
            cancellationToken.ThrowIfCancellationRequested();

            InstalledPatchState state = new InstalledPatchState
            {
                game_version = manifest.game_version,
                source_dat_sha256 = manifest.source_dat_sha256,
                patch_version = manifest.patch_version,
                release_tag = manifest.release_tag,
                release_id = manifest.release_id,
                asset_id = manifest.asset_id,
                file = target,
                sha256 = recovered.Translated.DatSha256,
                size = recovered.Translated.DatSize,
                game_dir = gameDirectory,
                source_backup_file = sourceBackup,
                source_backup_sha256 = recovered.CleanSource.DatSha256,
                source_backup_catalog_sha256 = recovered.CleanSource.CatalogSha256,
                candidate_catalog_sha256 = recovered.Translated.CatalogSha256,
                installed_at = DateTime.UtcNow.ToString("o")
            };
            WriteStateAtomic(statePath, state);
            return Task.FromResult(state);
        }
        catch
        {
            TryRestore(rollback, target);
            if (priorStateText == null) TryDelete(statePath); else WriteTextAtomic(statePath, priorStateText);
            TryDelete(statePath + ".part");
            throw;
        }
        finally
        {
            TryDelete(candidate);
            TryDelete(cleanCandidate);
        }
    }

    private static string EnsureCleanSourceBackup(string source, string gameDirectory, ReleaseManifest manifest, CancellationToken cancellationToken)
    {
        string backup = CleanSourceBackupPath(gameDirectory, manifest);
        string directory = Path.GetDirectoryName(backup);
        Directory.CreateDirectory(directory);
        if (File.Exists(backup))
        {
            VerifyFile(backup, manifest.source_dat_size, manifest.source_dat_sha256);
            return backup;
        }
        string part = backup + ".part";
        TryDelete(part);
        try
        {
            CopyAndVerify(source, part, manifest.source_dat_size, manifest.source_dat_sha256, cancellationToken);
            File.Move(part, backup);
            VerifyFile(backup, manifest.source_dat_size, manifest.source_dat_sha256);
            return backup;
        }
        catch
        {
            TryDelete(part);
            throw;
        }
    }

    private static string CleanSourceBackupPath(string gameDirectory, ReleaseManifest manifest)
    {
        string directory = Path.Combine(gameDirectory, ".lotro-turkce-backups");
        string name = "client_local_English.clean." + manifest.source_dat_sha256.Substring(0, 16).ToLowerInvariant() + ".dat";
        return Path.Combine(directory, name);
    }

    private static string SemanticCleanSourceBackupPath(string gameDirectory, ReleaseManifest manifest)
    {
        string directory = Path.Combine(gameDirectory, ".lotro-turkce-backups");
        string name = "client_local_English.semantic-clean." + manifest.source_catalog_sha256.Substring(0, 16).ToLowerInvariant() + ".dat";
        return Path.Combine(directory, name);
    }

    private static string FindVerifiedCleanSourceBackup(string gameDirectory, ReleaseManifest manifest)
    {
        string directory = Path.Combine(gameDirectory, ".lotro-turkce-backups");
        if (!Directory.Exists(directory)) return null;
        string expected = CleanSourceBackupPath(gameDirectory, manifest);
        if (IsVerifiedFile(expected, manifest.source_dat_size, manifest.source_dat_sha256)) return expected;
        try
        {
            foreach (string candidate in Directory.GetFiles(directory, "client_local_English*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsVerifiedFile(candidate, manifest.source_dat_size, manifest.source_dat_sha256)) return candidate;
            }
        }
        catch { }
        return null;
    }

    private static bool TryGetPriorCleanBackup(string gameDirectory, InstalledPatchState state, out string cleanBackup)
    {
        cleanBackup = null;
        if (state == null || string.IsNullOrWhiteSpace(state.source_backup_file)
            || string.IsNullOrWhiteSpace(state.source_backup_sha256))
            return false;
        try
        {
            string backupRoot = Path.GetFullPath(Path.Combine(gameDirectory, ".lotro-turkce-backups"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(state.source_backup_file);
            if (!candidate.StartsWith(backupRoot, StringComparison.OrdinalIgnoreCase)) return false;
            FileInfo info = new FileInfo(candidate);
            if (!info.Exists || info.Length < 1 || !string.Equals(HashFile(candidate), state.source_backup_sha256, StringComparison.OrdinalIgnoreCase)) return false;
            cleanBackup = candidate;
            return true;
        }
        catch { return false; }
    }

    private static bool IsVerifiedFile(string path, long size, string sha256)
    {
        try
        {
            FileInfo info = new FileInfo(path);
            return info.Exists && info.Length == size
                && string.Equals(HashFile(path), sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void EnsureFreeSpace(string directory, long requiredBytes)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(directory));
            DriveInfo drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < requiredBytes)
                throw new UpdaterFailure(
                    "INSUFFICIENT_DISK_SPACE",
                    "Güvenli kurulum için en az " + FormatGiB(requiredBytes)
                    + " boş alan gerekiyor. Kullanılabilir: " + FormatGiB(drive.AvailableFreeSpace) + ".");
        }
        catch (UpdaterFailure) { throw; }
        catch (Exception ex)
        {
            throw new UpdaterFailure("DISK_SPACE_CHECK_FAILED", "Boş disk alanı doğrulanamadı: " + ex.Message);
        }
    }

    private static string FormatGiB(long bytes)
    {
        return (bytes / 1073741824d).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    }

    private static ReleaseAsset FindUniqueAsset(ReleaseAsset[] assets, string name)
    {
        if (assets == null || string.IsNullOrEmpty(name)) return null;
        ReleaseAsset found = null;
        foreach (ReleaseAsset asset in assets)
        {
            if (asset == null || !string.Equals(asset.name, name, StringComparison.Ordinal)) continue;
            if (found != null) throw new UpdaterFailure("DUPLICATE_RELEASE_ASSET", "Release içinde aynı asset adı birden fazla kez bulundu.");
            found = asset;
        }
        return found;
    }

    internal static bool IsSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return value == Path.GetFileName(value) && !value.Contains("..") && !value.Contains("/") && !value.Contains("\\");
    }

    internal static string HashFile(string path)
    {
        using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        using (SHA256 sha = SHA256.Create()) return FixedGitHubTransport.ToHex(sha.ComputeHash(f));
    }

    private static void VerifyFile(string path, long size, string sha256)
    {
        FileInfo info = new FileInfo(path);
        if (!info.Exists || info.Length != size || !string.Equals(HashFile(path), sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("FILE_VERIFICATION_FAILED", "Dosya boyutu veya SHA-256 doğrulaması başarısız.");
    }

    private static void EnsureDatUnlocked(string target)
    {
        try
        {
            using (FileStream probe = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.SequentialScan)) { }
        }
        catch (IOException)
        {
            throw new UpdaterFailure("LOTRO_DAT_LOCKED", "LOTRO DAT dosyası kullanımda; oyun ve launcher'ı kapatın.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new UpdaterFailure("LOTRO_DAT_LOCKED", "LOTRO DAT dosyası kullanımda; oyun ve launcher'ı kapatın.");
        }
    }

    private static string BackupFile(string target, string gameDirectory)
    {
        string dir = Path.Combine(gameDirectory, ".lotro-turkce-backups");
        Directory.CreateDirectory(dir);
        string backup = Path.Combine(dir, Path.GetFileName(target) + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak");
        File.Copy(target, backup, false);
        FileInfo a = new FileInfo(target), b = new FileInfo(backup);
        if (a.Length != b.Length || !string.Equals(HashFile(target), HashFile(backup), StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("BACKUP_VERIFICATION_FAILED", "Backup boyutu veya SHA-256 doğrulaması başarısız.");
        return backup;
    }

    private static void CopyAndVerify(string source, string destination, long size, string sha256, CancellationToken cancellationToken)
    {
        using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
        {
            byte[] buffer = new byte[1024 * 1024];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
            }
            output.Flush(true);
        }
        VerifyFile(destination, size, sha256);
    }

    private static void ReplaceFile(string candidate, string target)
    {
        string fallback = target + ".replace.part";
        TryDelete(fallback);
        try { File.Replace(candidate, target, null, true); }
        catch (PlatformNotSupportedException) { File.Copy(candidate, fallback, true); File.Delete(target); File.Move(fallback, target); }
        catch (IOException) { File.Copy(candidate, fallback, true); File.Replace(fallback, target, null, true); }
    }

    private static void MoveOrReplace(string candidate, string target)
    {
        if (!File.Exists(target))
        {
            File.Move(candidate, target);
            return;
        }
        ReplaceFile(candidate, target);
    }

    private static void TryRestore(string backup, string target)
    {
        string temp = target + ".rollback.part";
        try
        {
            if (!File.Exists(backup)) return;
            TryDelete(temp);
            File.Copy(backup, temp, true);
            if (File.Exists(target))
            {
                try { File.Replace(temp, target, null, true); }
                catch (PlatformNotSupportedException) { File.Copy(temp, target, true); }
                catch (IOException) { File.Copy(temp, target, true); }
            }
            else File.Move(temp, target);
        }
        catch { }
        finally { TryDelete(temp); }
    }

    private static string TryRead(string path) { try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null; } catch { return null; } }
    private InstalledPatchState ParseState(string text) { try { return string.IsNullOrWhiteSpace(text) ? null : _json.Deserialize<InstalledPatchState>(text); } catch { throw new UpdaterFailure("STATE_INVALID", "installed_patch.json bozuk."); } }

    private void WriteStateAtomic(string path, InstalledPatchState state) { WriteTextAtomic(path, _json.Serialize(state)); }
    private static void WriteTextAtomic(string path, string text)
    {
        string directory = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        string temp = path + ".part"; File.WriteAllText(temp, text, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null, true); else File.Move(temp, path);
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}

public static class ManifestValidator
{
    public static void Validate(ReleaseManifest m, StableRelease release, ReleaseAsset manifestAsset)
    {
        bool commonInvalid = m == null || m.schema_version != 1 || release == null || manifestAsset == null
            || m.release_id != release.id
            || !string.Equals(m.release_tag, release.tag_name, StringComparison.Ordinal)
            || m.asset_id < 1 || m.asset_id == manifestAsset.id
            || string.IsNullOrWhiteSpace(m.asset_name)
            || string.Equals(m.asset_name, LotroReleaseUpdater.ManifestAssetName, StringComparison.Ordinal)
            || !m.asset_name.StartsWith(LotroReleaseUpdater.PatchAssetPrefix, StringComparison.Ordinal)
            || !LotroReleaseUpdater.IsSafeFileName(m.asset_name)
            || m.asset_size < 1 || m.source_dat_size < 1
            || !Hex64(m.asset_sha256) || !Hex64(m.source_dat_sha256)
            || string.IsNullOrWhiteSpace(m.patch_version)
            || string.IsNullOrWhiteSpace(m.game_version)
            || (m.asset_kind != "full_dat" && m.asset_kind != LotroReleaseUpdater.SemanticPatchKind);
        bool semanticInvalid = m != null && m.asset_kind == LotroReleaseUpdater.SemanticPatchKind
            && (!Hex64(m.source_catalog_sha256) || !Hex64(m.candidate_catalog_sha256)
                || m.safe_translated_count < 0 || m.skipped_changed_count < 0 || m.critical_review_required_count != 0);
        if (commonInvalid || semanticInvalid)
            throw new UpdaterFailure("MANIFEST_INVALID", "Manifest şeması veya release kimliği geçersiz.");
    }

    private static bool Hex64(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length != 64) return false;
        foreach (char c in s) if (!Uri.IsHexDigit(c)) return false;
        return true;
    }
}

public static class LotroPathValidator
{
    private static readonly string[] MarkerFiles = { "LotroLauncher.exe", "lotroclient.exe", "lotroclient64.exe", "lotroinvoker.exe" };

    public static void Validate(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) throw new UpdaterFailure("INVALID_LOTRO_DIRECTORY", "LOTRO oyun dizini bulunamadı.");
        if (!File.Exists(Path.Combine(directory, "client_local_English.dat"))) throw new UpdaterFailure("INVALID_LOTRO_DIRECTORY", "LOTRO DAT dosyası bu dizinde bulunamadı.");
        foreach (string marker in MarkerFiles) if (File.Exists(Path.Combine(directory, marker))) return;
        throw new UpdaterFailure("INVALID_LOTRO_DIRECTORY", "Bu dizin LOTRO kurulumu olarak doğrulanamadı.");
    }
}

public static class ProcessGuard
{
    private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lotroclient", "lotroclient64", "lotrolauncher", "lotroinvoker" };

    public static void EnsureClosed()
    {
        foreach (Process process in Process.GetProcesses())
        {
            try { if (Names.Contains(process.ProcessName)) throw new UpdaterFailure("LOTRO_RUNNING", "LOTRO ve launcher'ı kapatın."); }
            finally { process.Dispose(); }
        }
    }
}
