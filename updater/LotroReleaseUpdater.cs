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
    public string patch_mode { get; set; }
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
    public string candidate_dat_sha256 { get; set; }
    public long candidate_dat_size { get; set; }
    public string asset_kind { get; set; }
    public string minimum_updater_version { get; set; }
    public string source_catalog_sha256 { get; set; }
    public string base_patch_version { get; set; }
    public string base_release_tag { get; set; }
    public long base_release_id { get; set; }
    public long base_asset_id { get; set; }
    public string base_asset_name { get; set; }
    public long base_asset_size { get; set; }
    public string base_asset_sha256 { get; set; }
    public string base_candidate_dat_sha256 { get; set; }
    public long base_candidate_dat_size { get; set; }
    public string base_candidate_catalog_sha256 { get; set; }
    public int chain_depth { get; set; }
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

/// <summary>
/// One verified release asset in an ordered semantic patch chain. The caller
/// downloads and installs these packages from oldest to newest.
/// </summary>
public sealed class PatchPackage
{
    public StableRelease release { get; internal set; }
    public ReleaseManifest manifest { get; internal set; }
    public string path { get; internal set; }
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
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("LOTR-Turkce-Yama/1");
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
    public const string CurrentUpdaterVersion = "1.4.0.0";
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

    /// <summary>
    /// Loads a stable release by immutable tag. Chained delta manifests use
    /// this endpoint to find their predecessor without duplicating the large
    /// predecessor asset in every new release.
    /// </summary>
    public async Task<Tuple<StableRelease, ReleaseManifest>> CheckReleaseByTagAsync(string releaseTag, CancellationToken cancellationToken)
    {
        if (!IsSafeReleaseTag(releaseTag))
            throw new UpdaterFailure("CHAIN_INVALID", "Önceki release etiketi geçersiz.");
        Uri uri = new Uri("https://api.github.com/repos/" + Owner + "/" + Repository + "/releases/tags/" + Uri.EscapeDataString(releaseTag));
        string releaseJson = await _transport.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
        StableRelease release;
        try { release = _json.Deserialize<StableRelease>(releaseJson); }
        catch (Exception ex) { throw new UpdaterFailure("RELEASE_JSON_INVALID", ex.Message); }
        if (release == null || release.draft || release.prerelease || release.id < 1
            || !string.Equals(release.tag_name, releaseTag, StringComparison.Ordinal))
            throw new UpdaterFailure("CHAIN_INVALID", "Önceki stable release doğrulanamadı.");
        return await LoadReleaseManifestByReleaseAsync(release, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Tuple<StableRelease, ReleaseManifest>> LoadReleaseManifestByReleaseAsync(StableRelease release, CancellationToken cancellationToken)
    {
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
            if (cached.Length == manifest.asset_size && string.Equals(HashFile(path, cancellationToken), manifest.asset_sha256, StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// Resolves and downloads the release asset. Current public releases are
    /// one full_dat package; the semantic chain code below remains only for
    /// reading historical manifests during migration.
    /// </summary>
    public async Task<List<PatchPackage>> DownloadPatchChainAsync(
        StableRelease latestRelease,
        ReleaseManifest latestManifest,
        string cacheDirectory,
        InstalledPatchState installedState,
        CancellationToken cancellationToken,
        IProgress<DownloadProgress> progress = null)
    {
        if (latestRelease == null) throw new ArgumentNullException(nameof(latestRelease));
        if (latestManifest == null) throw new ArgumentNullException(nameof(latestManifest));
        if (string.IsNullOrWhiteSpace(cacheDirectory)) throw new ArgumentException("cacheDirectory");
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsInstalledFileValid(installedState, cancellationToken)) installedState = null;
        if (IsStateAtManifest(installedState, latestManifest))
            return new List<PatchPackage>();

        List<PatchPackage> chain = await ResolvePatchChainAsync(latestRelease, latestManifest, cancellationToken).ConfigureAwait(false);
        int start = FindChainStart(chain, installedState);
        if (start >= chain.Count) return new List<PatchPackage>();

        Directory.CreateDirectory(cacheDirectory);
        long totalBytes = 0;
        for (int i = start; i < chain.Count; i++) totalBytes = checked(totalBytes + chain[i].manifest.asset_size);
        long completedBytes = 0;
        List<PatchPackage> result = new List<PatchPackage>(chain.Count - start);
        for (int i = start; i < chain.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PatchPackage package = chain[i];
            string packageCache = Path.Combine(cacheDirectory, package.manifest.asset_sha256.ToLowerInvariant());
            Directory.CreateDirectory(packageCache);
            string path = Path.Combine(packageCache, package.manifest.asset_name);
            long progressOffset = completedBytes;
            IProgress<DownloadProgress> childProgress = progress == null
                ? null
                : new InlineProgress<DownloadProgress>(value => progress.Report(new DownloadProgress
                {
                    DownloadedBytes = progressOffset + value.DownloadedBytes,
                    TotalBytes = totalBytes,
                    Percentage = totalBytes < 1 ? 100 : (int)Math.Min(100L, (progressOffset + value.DownloadedBytes) * 100L / totalBytes)
                }));
            await DownloadPatchAsync(package.release, package.manifest, packageCache, cancellationToken, childProgress).ConfigureAwait(false);
            package.path = path;
            completedBytes = checked(completedBytes + package.manifest.asset_size);
            if (progress != null)
                progress.Report(new DownloadProgress { DownloadedBytes = completedBytes, TotalBytes = totalBytes, Percentage = (int)(completedBytes * 100L / totalBytes) });
            result.Add(package);
        }
        return result;
    }

    public Task<List<PatchPackage>> DownloadPatchChainAsync(
        StableRelease latestRelease,
        ReleaseManifest latestManifest,
        string cacheDirectory,
        CancellationToken cancellationToken,
        IProgress<DownloadProgress> progress = null)
    {
        return DownloadPatchChainAsync(latestRelease, latestManifest, cacheDirectory, null, cancellationToken, progress);
    }

    /// <summary>Returns the immutable package chain for legacy manifests.</summary>
    public async Task<List<PatchPackage>> ResolvePatchChainAsync(
        StableRelease latestRelease,
        ReleaseManifest latestManifest,
        CancellationToken cancellationToken)
    {
        if (latestRelease == null) throw new ArgumentNullException(nameof(latestRelease));
        if (latestManifest == null) throw new ArgumentNullException(nameof(latestManifest));
        List<PatchPackage> reverse = new List<PatchPackage>();
        HashSet<string> seenTags = new HashSet<string>(StringComparer.Ordinal);
        StableRelease release = latestRelease;
        ReleaseManifest manifest = latestManifest;
        for (int depth = 0; depth <= 32; depth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seenTags.Add(release.tag_name))
                throw new UpdaterFailure("CHAIN_INVALID", "Semantic release zincirinde döngü bulundu.");
            reverse.Add(new PatchPackage { release = release, manifest = manifest });
            if (!IsIncrementalManifest(manifest))
            {
                reverse.Reverse();
                return reverse;
            }
            ValidateIncrementalManifestPointer(manifest);
            Tuple<StableRelease, ReleaseManifest> predecessor = await CheckReleaseByTagAsync(manifest.base_release_tag, cancellationToken).ConfigureAwait(false);
            StableRelease baseRelease = predecessor.Item1;
            ReleaseManifest baseManifest = predecessor.Item2;
            if (baseRelease.id != manifest.base_release_id
                || !string.Equals(baseRelease.tag_name, manifest.base_release_tag, StringComparison.Ordinal)
                || !string.Equals(baseManifest.patch_version, manifest.base_patch_version, StringComparison.Ordinal)
                || baseManifest.asset_id != manifest.base_asset_id
                || !string.Equals(baseManifest.asset_name, manifest.base_asset_name, StringComparison.Ordinal)
                || baseManifest.asset_size != manifest.base_asset_size
                || !string.Equals(baseManifest.asset_sha256, manifest.base_asset_sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdaterFailure("CHAIN_INVALID", "Semantic release zinciri predecessor asset kimliği ile eşleşmiyor.");
            if (baseManifest.asset_kind != SemanticPatchKind
                || baseManifest.source_dat_size != manifest.source_dat_size
                || !string.Equals(baseManifest.source_dat_sha256, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(baseManifest.source_catalog_sha256, manifest.source_catalog_sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdaterFailure("CHAIN_INVALID", "Düzeltme zinciri farklı bir oyun kaynağına ait.");
            if (!string.Equals(baseManifest.candidate_catalog_sha256, manifest.base_candidate_catalog_sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdaterFailure("CHAIN_INVALID", "Semantic release zinciri predecessor katalog kimliği ile eşleşmiyor.");
            if (!string.IsNullOrWhiteSpace(baseManifest.candidate_dat_sha256)
                && (!string.Equals(baseManifest.candidate_dat_sha256, manifest.base_candidate_dat_sha256, StringComparison.OrdinalIgnoreCase)
                    || (baseManifest.candidate_dat_size > 0 && baseManifest.candidate_dat_size != manifest.base_candidate_dat_size)))
                throw new UpdaterFailure("CHAIN_INVALID", "Semantic release zinciri predecessor DAT kimliği ile eşleşmiyor.");
            if (IsIncrementalManifest(baseManifest))
            {
                if (manifest.chain_depth < 2 || baseManifest.chain_depth != manifest.chain_depth - 1)
                    throw new UpdaterFailure("CHAIN_INVALID", "Semantic release zinciri derinlik bilgisi ile eşleşmiyor.");
            }
            else if (manifest.chain_depth != 1)
                throw new UpdaterFailure("CHAIN_INVALID", "Semantic release zinciri kök derinliği ile eşleşmiyor.");
            release = baseRelease;
            manifest = baseManifest;
        }
        throw new UpdaterFailure("CHAIN_INVALID", "Semantic release zinciri güvenli derinlik sınırını aşıyor.");
    }

    /// <summary>Installs already downloaded packages in order.</summary>
    public async Task<InstalledPatchState> InstallPatchChainAsync(
        string gameDirectory,
        IList<PatchPackage> packages,
        string statePath,
        CancellationToken cancellationToken)
    {
        return await InstallPatchChainAsync(gameDirectory, packages, statePath, cancellationToken, null).ConfigureAwait(false);
    }

    /// <summary>
    /// Installs the chain on a worker thread supplied by the caller while
    /// reporting phase changes.  The callback is deliberately text-only so UI
    /// clients can marshal it to their own thread without sharing updater state.
    /// </summary>
    public async Task<InstalledPatchState> InstallPatchChainAsync(
        string gameDirectory,
        IList<PatchPackage> packages,
        string statePath,
        CancellationToken cancellationToken,
        Action<string> progress)
    {
        if (packages == null || packages.Count == 0)
        {
            InstalledPatchState existing = ParseState(TryRead(statePath));
            if (existing == null) throw new UpdaterFailure("CHAIN_INVALID", "Kurulacak semantic paket zinciri boş.");
            return existing;
        }
        InstalledPatchState state = null;
        foreach (PatchPackage package in packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (package == null || package.manifest == null || string.IsNullOrWhiteSpace(package.path))
                throw new UpdaterFailure("CHAIN_INVALID", "Semantic paket zinciri eksik.");
            progress?.Invoke("" + package.manifest.patch_version + " düzeltmesi uygulanıyor; DAT okunuyor...");
            state = await InstallPatchAsync(gameDirectory, package.path, package.manifest, statePath, cancellationToken, progress).ConfigureAwait(false);
        }
        return state;
    }

    private static bool IsIncrementalManifest(ReleaseManifest manifest)
    {
        return manifest != null && string.Equals(manifest.patch_mode, SemanticPatchBuilder.IncrementalPatchMode, StringComparison.Ordinal);
    }

    private static void ValidateIncrementalManifestPointer(ReleaseManifest manifest)
    {
        if (manifest == null || !IsIncrementalManifest(manifest)
            || !IsSafeReleaseTag(manifest.base_release_tag)
            || manifest.base_release_id < 1
            || manifest.base_asset_id < 1
            || string.IsNullOrWhiteSpace(manifest.base_patch_version)
            || !IsSafeFileName(manifest.base_asset_name)
            || manifest.base_asset_size < 1
            || !Hex64(manifest.base_asset_sha256)
            || !Hex64(manifest.base_candidate_catalog_sha256)
            || !Hex64(manifest.base_candidate_dat_sha256)
            || manifest.base_candidate_dat_size < 1
            || !Hex64(manifest.candidate_dat_sha256)
            || manifest.candidate_dat_size < 1
            || manifest.chain_depth < 1 || manifest.chain_depth > 32)
            throw new UpdaterFailure("CHAIN_INVALID", "Incremental semantic manifest predecessor bilgileri geçersiz.");
    }

    private static int FindChainStart(IList<PatchPackage> chain, InstalledPatchState installedState)
    {
        if (installedState == null) return 0;
        for (int i = chain.Count - 1; i >= 0; i--)
            if (IsStateAtManifest(installedState, chain[i].manifest)) return i + 1;
        return 0;
    }

    internal static bool IsStateAtManifest(InstalledPatchState state, ReleaseManifest manifest)
    {
        if (state == null || manifest == null
            || state.release_id != manifest.release_id
            || state.asset_id != manifest.asset_id
            || !string.Equals(state.release_tag, manifest.release_tag, StringComparison.Ordinal)
            || !string.Equals(state.patch_version, manifest.patch_version, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(state.file)
            || state.size < 1)
            return false;
        return true;
    }

    // The caller must bind state to the selected game directory, never trust
    // state.file as an instruction to inspect an unrelated installation.
    internal static bool IsInstalledFileValid(InstalledPatchState state, CancellationToken token, string gameDirectory = null)
    {
        if (state == null || !Hex64(state.sha256) || state.size < 1) return false;
        try
        {
            string directory = Path.GetFullPath(gameDirectory ?? state.game_dir);
            string target = Path.Combine(directory, "client_local_English.dat");
            if (!string.Equals(Path.GetFullPath(state.file), target, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFullPath(state.game_dir), directory, StringComparison.OrdinalIgnoreCase)) return false;
            return File.Exists(target) && new FileInfo(target).Length == state.size
                && string.Equals(HashFile(target, token), state.sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        public InlineProgress(Action<T> report) { _report = report; }
        public void Report(T value) { _report(value); }
    }

    public Task<InstalledPatchState> InstallFullDatAsync(string gameDirectory, string patchPath, ReleaseManifest manifest, string statePath, CancellationToken cancellationToken)
    {
        return InstallFullDatAsync(gameDirectory, patchPath, manifest, statePath, cancellationToken, null);
    }

    public Task<InstalledPatchState> InstallFullDatAsync(string gameDirectory, string patchPath, ReleaseManifest manifest, string statePath, CancellationToken cancellationToken, Action<string> progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LotroPathValidator.Validate(gameDirectory);
        ProcessGuard.EnsureClosed();
        if (manifest == null || manifest.asset_kind != "full_dat") throw new UpdaterFailure("UNSUPPORTED_ASSET_KIND", "Yalnız full_dat release kurulabilir.");
        progress?.Invoke("Çeviri paketi doğrulanıyor...");
        VerifyFile(patchPath, manifest.asset_size, manifest.asset_sha256, cancellationToken);
        string target = Path.Combine(gameDirectory, "client_local_English.dat");
        if (!File.Exists(target)) throw new UpdaterFailure("LOTRO_DAT_MISSING", "LOTRO client_local_English.dat bulunamadı.");
        EnsureDatUnlocked(target);
        string priorStateText = TryRead(statePath);
        string currentHash = HashFile(target, cancellationToken);
        // A full-DAT release is deliberately independent of the file currently
        // installed in the game directory. The complete, verified translated
        // DAT is copied atomically below, so a previous patch, a launcher
        // rewrite, or a missing/old state file must not cause a baseline
        // mismatch failure.
        EnsureFreeSpace(gameDirectory, checked(new FileInfo(target).Length + manifest.asset_size + 64L * 1024 * 1024));
        string backup = BackupFile(target, gameDirectory, cancellationToken, currentHash);
        string candidate = target + ".lotro-candidate.part";
        bool replacementStarted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke("Tam DAT kopyalanıyor ve doğrulanıyor...");
            CopyAndVerify(patchPath, candidate, manifest.asset_size, manifest.asset_sha256, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke("Tam DAT güvenli biçimde yerleştiriliyor...");
            ProcessGuard.EnsureClosed();
            EnsureDatUnlocked(target);
            cancellationToken.ThrowIfCancellationRequested();
            replacementStarted = true;
            ReplaceFile(candidate, target);
            VerifyFile(target, manifest.asset_size, manifest.asset_sha256, cancellationToken);
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
            if (replacementStarted) TryRestore(backup, target);
            if (priorStateText == null) TryDelete(statePath); else WriteTextAtomic(statePath, priorStateText);
            TryDelete(statePath + ".part");
            throw;
        }
        finally { TryDelete(candidate); }
    }

    /// <summary>Common verified install entry point.</summary>
    public Task<InstalledPatchState> InstallPatchAsync(string gameDirectory, string patchPath, ReleaseManifest manifest, string statePath, CancellationToken cancellationToken)
    {
        return InstallPatchAsync(gameDirectory, patchPath, manifest, statePath, cancellationToken, null);
    }

    public Task<InstalledPatchState> InstallPatchAsync(string gameDirectory, string patchPath, ReleaseManifest manifest, string statePath, CancellationToken cancellationToken, Action<string> progress)
    {
        ManifestValidator.EnsureUpdaterSupported(manifest);
        if (manifest != null && manifest.asset_kind == SemanticPatchKind)
        {
            if (string.Equals(manifest.patch_mode, SemanticPatchBuilder.IncrementalPatchMode, StringComparison.Ordinal))
                return InstallIncrementalSemanticDeltaPatchAsync(gameDirectory, patchPath, manifest, statePath, cancellationToken, progress);
            return InstallSemanticDeltaPatchAsync(gameDirectory, patchPath, manifest, statePath, cancellationToken, progress);
        }
        return InstallFullDatAsync(gameDirectory, patchPath, manifest, statePath, cancellationToken, progress);
    }

    /// <summary>
    /// Installs a small correction layer on the currently installed translated
    /// DAT. The predecessor DAT/catalog hashes are checked before any writable
    /// copy is created, and the old file/state are restored on every failure.
    /// </summary>
    public Task<InstalledPatchState> InstallIncrementalSemanticDeltaPatchAsync(
        string gameDirectory,
        string patchPath,
        ReleaseManifest manifest,
        string statePath,
        CancellationToken cancellationToken)
    {
        return InstallIncrementalSemanticDeltaPatchAsync(gameDirectory, patchPath, manifest, statePath, cancellationToken, null);
    }

    public Task<InstalledPatchState> InstallIncrementalSemanticDeltaPatchAsync(
        string gameDirectory,
        string patchPath,
        ReleaseManifest manifest,
        string statePath,
        CancellationToken cancellationToken,
        Action<string> progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LotroPathValidator.Validate(gameDirectory);
        ProcessGuard.EnsureClosed();
        if (manifest == null || manifest.asset_kind != SemanticPatchKind
            || !string.Equals(manifest.patch_mode, SemanticPatchBuilder.IncrementalPatchMode, StringComparison.Ordinal))
            throw new UpdaterFailure("UNSUPPORTED_ASSET_KIND", "Incremental semantic delta bekleniyordu.");
        ValidateIncrementalManifestPointer(manifest);
        progress?.Invoke("Çeviri paketi doğrulanıyor...");
        VerifyFile(patchPath, manifest.asset_size, manifest.asset_sha256, cancellationToken);

        SemanticPatchDocument document;
        try { document = SemanticPatchSerializer.ReadFile(patchPath); }
        catch (Exception ex) { throw new UpdaterFailure("SEMANTIC_PATCH_INVALID", "Incremental semantic patch doğrulanamadı: " + ex.Message); }
        if (!string.Equals(document.patch_mode, SemanticPatchBuilder.IncrementalPatchMode, StringComparison.Ordinal)
            || !string.Equals(document.patch_version, manifest.patch_version, StringComparison.Ordinal)
            || !string.Equals(document.source_dat_sha256, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase)
            || document.source_dat_size != manifest.source_dat_size
            || document.base_candidate_dat_size != manifest.base_candidate_dat_size
            || !string.Equals(document.base_candidate_dat_sha256, manifest.base_candidate_dat_sha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(document.base_patch_version, manifest.base_patch_version, StringComparison.Ordinal)
            || !string.Equals(document.base_candidate_catalog_sha256, manifest.base_candidate_catalog_sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("SEMANTIC_PATCH_BASELINE_MISMATCH", "Incremental semantic patch manifest ile eşleşmiyor.");
        if (!string.IsNullOrWhiteSpace(manifest.source_catalog_sha256)
            && !string.Equals(document.source_catalog_sha256, manifest.source_catalog_sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("SEMANTIC_PATCH_CATALOG_MISMATCH", "Incremental semantic patch kaynak katalog kimliği manifest ile eşleşmiyor.");

        string target = Path.Combine(gameDirectory, "client_local_English.dat");
        if (!File.Exists(target)) throw new UpdaterFailure("LOTRO_DAT_MISSING", "LOTRO client_local_English.dat bulunamadı.");
        EnsureDatUnlocked(target);
        string priorStateText = TryRead(statePath);
        InstalledPatchState priorState = ParseState(priorStateText);
        string currentHash = HashFile(target, cancellationToken);
        long currentSize = new FileInfo(target).Length;

        if (priorState != null
            && IsStateAtManifest(priorState, manifest)
            && priorState.size == currentSize
            && string.Equals(priorState.sha256, currentHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(priorState.candidate_catalog_sha256, manifest.candidate_catalog_sha256, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(priorState);

        if (priorState == null
            || priorState.release_id != manifest.base_release_id
            || priorState.asset_id != manifest.base_asset_id
            || !string.Equals(priorState.release_tag, manifest.base_release_tag, StringComparison.Ordinal)
            || !string.Equals(priorState.source_dat_sha256, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(priorState.patch_version, manifest.base_patch_version, StringComparison.Ordinal)
            || !string.Equals(priorState.candidate_catalog_sha256, manifest.base_candidate_catalog_sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("PATCH_CHAIN_BASE_MISMATCH", "Bu düzeltmenin predecessor Türkçe paketi kurulu değil.");
        if (!string.IsNullOrWhiteSpace(manifest.base_candidate_dat_sha256)
            && !string.Equals(manifest.base_candidate_dat_sha256, currentHash, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("PATCH_CHAIN_BASE_MISMATCH", "Mevcut Türkçe DAT predecessor SHA-256 ile eşleşmiyor.");
        if (manifest.base_candidate_dat_size > 0 && manifest.base_candidate_dat_size != currentSize)
            throw new UpdaterFailure("PATCH_CHAIN_BASE_MISMATCH", "Mevcut Türkçe DAT predecessor boyutu ile eşleşmiyor.");

        string candidate = target + ".lotro-candidate.part";
        TryDelete(candidate);
        long requiredSpace = checked(2L * currentSize + Math.Max(256L * 1024 * 1024, currentSize / 10) + 64L * 1024 * 1024);
        EnsureFreeSpace(gameDirectory, requiredSpace);
        progress?.Invoke("Temiz kaynak yedeği doğrulanıyor...");
        string sourceBackup;
        if (!TryGetPriorCleanBackup(gameDirectory, priorState, out sourceBackup, cancellationToken))
            sourceBackup = FindVerifiedCleanSourceBackup(gameDirectory, manifest, cancellationToken);
        if (string.IsNullOrWhiteSpace(sourceBackup))
            throw new UpdaterFailure("PATCH_CHAIN_BACKUP_MISSING", "Temiz LOTRO DAT yedeği bulunamadı; oyun dosyanız değiştirilmedi.");
        string sourceBackupHash = string.Equals(sourceBackup, priorState.source_backup_file, StringComparison.OrdinalIgnoreCase)
            ? priorState.source_backup_sha256 : manifest.source_dat_sha256;
        progress?.Invoke("Geri dönüş yedeği hazırlanıyor...");
        string rollback = BackupFile(target, gameDirectory, cancellationToken, currentHash);
        bool replacementStarted = false;
        try
        {
            progress?.Invoke("Önceki Türkçe DAT kataloğu doğrulanıyor...");
            ManagedSemanticDatPatcher.Result built;
            try
            {
                built = ManagedSemanticDatPatcher.BuildIncrementalCandidate(
                    target,
                    candidate,
                    document,
                    manifest.candidate_catalog_sha256,
                    cancellationToken,
                    progress);
            }
            catch (InvalidDataException ex) when (IsLocalizationRebuildFailure(ex))
            {
                throw CreateLocalizationRebuildFailure(ex);
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke("Türkçe düzeltme adayı doğrulanıyor ve yerleştiriliyor...");
            if (built.DatSize != manifest.candidate_dat_size
                || !string.Equals(built.DatSha256, manifest.candidate_dat_sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdaterFailure("CANDIDATE_DAT_MISMATCH", "Düzeltme sonucu manifestteki DAT kimliğiyle eşleşmiyor.");
            ProcessGuard.EnsureClosed();
            EnsureDatUnlocked(target);
            cancellationToken.ThrowIfCancellationRequested();
            replacementStarted = true;
            ReplaceFile(candidate, target);
            VerifyFile(target, built.DatSize, built.DatSha256, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            InstalledPatchState state = new InstalledPatchState
            {
                game_version = manifest.game_version,
                source_dat_sha256 = priorState.source_dat_sha256,
                patch_version = manifest.patch_version,
                release_tag = manifest.release_tag,
                release_id = manifest.release_id,
                asset_id = manifest.asset_id,
                file = target,
                sha256 = built.DatSha256,
                size = built.DatSize,
                game_dir = gameDirectory,
                source_backup_file = sourceBackup,
                source_backup_sha256 = sourceBackupHash,
                source_backup_catalog_sha256 = priorState.source_backup_catalog_sha256,
                candidate_catalog_sha256 = built.CatalogSha256,
                installed_at = DateTime.UtcNow.ToString("o")
            };
            WriteStateAtomic(statePath, state);
            return Task.FromResult(state);
        }
        catch
        {
            if (replacementStarted) TryRestore(rollback, target);
            if (priorStateText == null) TryDelete(statePath); else WriteTextAtomic(statePath, priorStateText);
            TryDelete(statePath + ".part");
            throw;
        }
        finally { TryDelete(candidate); }
    }

    public Task<InstalledPatchState> InstallSemanticDeltaPatchAsync(string gameDirectory, string patchPath, ReleaseManifest manifest, string statePath, CancellationToken cancellationToken)
    {
        return InstallSemanticDeltaPatchAsync(gameDirectory, patchPath, manifest, statePath, cancellationToken, null);
    }

    public Task<InstalledPatchState> InstallSemanticDeltaPatchAsync(string gameDirectory, string patchPath, ReleaseManifest manifest, string statePath, CancellationToken cancellationToken, Action<string> progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LotroPathValidator.Validate(gameDirectory);
        ProcessGuard.EnsureClosed();
        if (manifest == null || manifest.asset_kind != SemanticPatchKind)
            throw new UpdaterFailure("UNSUPPORTED_ASSET_KIND", "Semantic delta patch bekleniyordu.");
        progress?.Invoke("Çeviri paketi doğrulanıyor...");
        VerifyFile(patchPath, manifest.asset_size, manifest.asset_sha256, cancellationToken);

        SemanticPatchDocument document;
        try
        {
            document = SemanticPatchSerializer.ReadFile(patchPath);
        }
        catch (Exception ex)
        {
            throw new UpdaterFailure("SEMANTIC_PATCH_INVALID", "Semantic patch doğrulanamadı: " + ex.Message);
        }
        if (string.Equals(document.patch_mode, SemanticPatchBuilder.IncrementalPatchMode, StringComparison.Ordinal)
            || string.Equals(manifest.patch_mode, SemanticPatchBuilder.IncrementalPatchMode, StringComparison.Ordinal)
            || !string.Equals(document.patch_version, manifest.patch_version, StringComparison.Ordinal)
            || document.source_dat_size != manifest.source_dat_size
            || !string.Equals(document.source_dat_sha256, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("SEMANTIC_PATCH_BASELINE_MISMATCH", "Semantic patch baseline manifest ile eşleşmiyor.");
        if (!string.IsNullOrWhiteSpace(manifest.source_catalog_sha256)
            && !string.Equals(document.source_catalog_sha256, manifest.source_catalog_sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("SEMANTIC_PATCH_CATALOG_MISMATCH", "Semantic patch catalog kimliği manifest ile eşleşmiyor.");

        string target = Path.Combine(gameDirectory, "client_local_English.dat");
        if (!File.Exists(target)) throw new UpdaterFailure("LOTRO_DAT_MISSING", "LOTRO client_local_English.dat bulunamadı.");
        EnsureDatUnlocked(target);
        string priorStateText = TryRead(statePath);
        InstalledPatchState priorState = ParseState(priorStateText);
        string currentHash = HashFile(target, cancellationToken);
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
                && string.Equals(HashFile(priorState.source_backup_file, cancellationToken), manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase))
                cleanSource = priorState.source_backup_file;
        }
        // A backup is not evidence that the current DAT still belongs to this
        // baseline: the official launcher may have updated it in the meantime.
        if (cleanSource == null && priorState != null && priorState.size == currentSize
            && string.Equals(priorState.sha256, currentHash, StringComparison.OrdinalIgnoreCase))
            cleanSource = FindVerifiedCleanSourceBackup(gameDirectory, manifest, cancellationToken);
        if (cleanSource == null
            && priorState != null
            && !string.Equals(priorState.source_dat_sha256, manifest.source_dat_sha256, StringComparison.OrdinalIgnoreCase)
            && TryGetPriorCleanBackup(gameDirectory, priorState, out string priorCleanBackup, cancellationToken))
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
                cancellationToken,
                progress);
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
        string rollback = currentIsCleanBaseline ? sourceBackup : BackupFile(target, gameDirectory, cancellationToken, currentHash);
        bool replacementStarted = false;
        try
        {
            progress?.Invoke("Temiz LOTRO DAT doğrulandı; Türkçe satırlar yazılıyor...");
            ManagedSemanticDatPatcher.Result built;
            try
            {
                built = ManagedSemanticDatPatcher.BuildCandidate(
                    sourceBackup,
                    candidate,
                    document,
                    manifest.candidate_catalog_sha256,
                    cancellationToken,
                    progress);
            }
            catch (InvalidDataException ex) when (IsLocalizationRebuildFailure(ex))
            {
                throw CreateLocalizationRebuildFailure(ex);
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke("Aday DAT baştan sona doğrulanıyor ve yerleştiriliyor...");
            if (!string.IsNullOrWhiteSpace(manifest.candidate_dat_sha256)
                && (built.DatSize != manifest.candidate_dat_size
                    || !string.Equals(built.DatSha256, manifest.candidate_dat_sha256, StringComparison.OrdinalIgnoreCase)))
                throw new UpdaterFailure("CANDIDATE_DAT_MISMATCH", "Yama sonucu manifestteki DAT kimliğiyle eşleşmiyor.");
            ProcessGuard.EnsureClosed();
            EnsureDatUnlocked(target);
            cancellationToken.ThrowIfCancellationRequested();
            replacementStarted = true;
            ReplaceFile(candidate, target);
            VerifyFile(target, built.DatSize, built.DatSha256, cancellationToken);
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
                source_backup_sha256 = manifest.source_dat_sha256,
                source_backup_catalog_sha256 = manifest.source_catalog_sha256,
                candidate_catalog_sha256 = built.CatalogSha256,
                installed_at = DateTime.UtcNow.ToString("o")
            };
            WriteStateAtomic(statePath, state);
            return Task.FromResult(state);
        }
        catch
        {
            if (replacementStarted) TryRestore(rollback, target);
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
        CancellationToken cancellationToken,
        Action<string> progress)
    {
        string candidate = target + ".lotro-candidate.part";
        string cleanCandidate = target + ".clean-recovery.part";
        string sourceBackup = SemanticCleanSourceBackupPath(gameDirectory, manifest);
        long allowance = checked(Math.Max(currentSize, manifest.source_dat_size) + Math.Max(256L * 1024 * 1024, currentSize / 10));
        EnsureFreeSpace(gameDirectory, checked(allowance * 3L + 64L * 1024 * 1024));
        string rollback = BackupFile(target, gameDirectory, cancellationToken);
        bool replacementStarted = false;
        TryDelete(candidate);
        TryDelete(cleanCandidate);
        try
        {
            ManagedSemanticDatPatcher.RecoveryResult recovered;
            try
            {
                recovered = ManagedSemanticDatPatcher.RecoverUpdatedPatchedDat(
                    target,
                    priorCleanBackup,
                    cleanCandidate,
                    candidate,
                    document,
                    manifest.candidate_catalog_sha256,
                    cancellationToken,
                    progress);
            }
            catch (InvalidDataException ex) when (IsLocalizationRebuildFailure(ex))
            {
                throw CreateLocalizationRebuildFailure(ex);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(sourceBackup));
            MoveOrReplace(cleanCandidate, sourceBackup);
            VerifyFile(sourceBackup, recovered.CleanSource.DatSize, recovered.CleanSource.DatSha256, cancellationToken);
            progress?.Invoke("Güncellenen DAT güvenle birleştiriliyor...");
            ProcessGuard.EnsureClosed();
            EnsureDatUnlocked(target);
            cancellationToken.ThrowIfCancellationRequested();
            replacementStarted = true;
            ReplaceFile(candidate, target);
            VerifyFile(target, recovered.Translated.DatSize, recovered.Translated.DatSha256, cancellationToken);
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
            if (replacementStarted) TryRestore(rollback, target);
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
            VerifyFile(backup, manifest.source_dat_size, manifest.source_dat_sha256, cancellationToken);
            return backup;
        }
        string part = backup + ".part";
        TryDelete(part);
        try
        {
            CopyAndVerify(source, part, manifest.source_dat_size, manifest.source_dat_sha256, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(part, backup);
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

    private static string FindVerifiedCleanSourceBackup(string gameDirectory, ReleaseManifest manifest, CancellationToken cancellationToken = default(CancellationToken))
    {
        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.Combine(gameDirectory, ".lotro-turkce-backups");
        if (!Directory.Exists(directory)) return null;
        string expected = CleanSourceBackupPath(gameDirectory, manifest);
        if (IsVerifiedFile(expected, manifest.source_dat_size, manifest.source_dat_sha256, cancellationToken)) return expected;
        try
        {
            foreach (string candidate in Directory.GetFiles(directory, "client_local_English*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsVerifiedFile(candidate, manifest.source_dat_size, manifest.source_dat_sha256, cancellationToken)) return candidate;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        return null;
    }

    private static bool TryGetPriorCleanBackup(string gameDirectory, InstalledPatchState state, out string cleanBackup, CancellationToken cancellationToken = default(CancellationToken))
    {
        cleanBackup = null;
        cancellationToken.ThrowIfCancellationRequested();
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
            if (!info.Exists || info.Length < 1 || !string.Equals(HashFile(candidate, cancellationToken), state.source_backup_sha256, StringComparison.OrdinalIgnoreCase)) return false;
            cleanBackup = candidate;
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    private static bool IsVerifiedFile(string path, long size, string sha256, CancellationToken cancellationToken = default(CancellationToken))
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            FileInfo info = new FileInfo(path);
            return info.Exists && info.Length == size
                && string.Equals(HashFile(path, cancellationToken), sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) { throw; }
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

    internal static bool IsSafeReleaseTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80) return false;
        if (value[0] == '.' || value[0] == '-') return false;
        foreach (char c in value)
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-')) return false;
        return true;
    }

    private static bool IsLocalizationRebuildFailure(InvalidDataException error)
    {
        if (error == null || string.IsNullOrWhiteSpace(error.Message)) return false;
        return error.Message.IndexOf("Localization identity rebuild", StringComparison.OrdinalIgnoreCase) >= 0
            || error.Message.IndexOf("Localization rebuild hedef", StringComparison.OrdinalIgnoreCase) >= 0
            || error.Message.IndexOf("Localization rebuild satır", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static UpdaterFailure CreateLocalizationRebuildFailure(InvalidDataException error)
    {
        return new UpdaterFailure(
            "DAT_REBUILD_INCOMPATIBLE",
            "Yama adayı doğrulanamadı (" + error.Message
            + "). Kurulum durduruldu; bu hata oyun dosyanızın bozuk olduğunu göstermez. Hata kodunu yama geliştiricisine iletin.");
    }

    private static bool Hex64(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
        foreach (char c in value) if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    internal static string HashFile(string path)
    {
        return HashFile(path, CancellationToken.None);
    }

    internal static string HashFile(string path, CancellationToken token)
    {
        using (FileStream f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        using (SHA256 sha = SHA256.Create())
        {
            byte[] buffer = new byte[1024 * 1024];
            int read;
            token.ThrowIfCancellationRequested();
            while ((read = f.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                sha.TransformBlock(buffer, 0, read, buffer, 0);
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return FixedGitHubTransport.ToHex(sha.Hash);
        }
    }

    private static void VerifyFile(string path, long size, string sha256, CancellationToken token = default(CancellationToken))
    {
        token.ThrowIfCancellationRequested();
        FileInfo info = new FileInfo(path);
        if (!info.Exists || info.Length != size || !string.Equals(HashFile(path, token), sha256, StringComparison.OrdinalIgnoreCase))
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

    private static string BackupFile(string target, string gameDirectory, CancellationToken token = default(CancellationToken), string verifiedHash = null)
    {
        string dir = Path.Combine(gameDirectory, ".lotro-turkce-backups");
        Directory.CreateDirectory(dir);
        string backup = Path.Combine(dir, Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".bak");
        try
        {
            // Reuse the source hash already measured by this install; verify
            // the destination independently after copying, not both files again.
            CopyAndVerify(target, backup, new FileInfo(target).Length, verifiedHash ?? HashFile(target, token), token);
            return backup;
        }
        catch { TryDelete(backup); throw; }
    }

    private static void CopyAndVerify(string source, string destination, long size, string sha256, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        VerifyFile(destination, size, sha256, cancellationToken);
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
            if (!File.Exists(backup)) throw new IOException("Geri dönüş yedeği bulunamadı.");
            long expectedSize = new FileInfo(backup).Length;
            if (expectedSize < 1) throw new IOException("Geri dönüş yedeği boş.");
            string expectedHash = HashFile(backup, CancellationToken.None);
            TryDelete(temp);
            CopyAndVerify(backup, temp, expectedSize, expectedHash, CancellationToken.None);
            if (File.Exists(target))
            {
                try { File.Replace(temp, target, null, true); }
                catch (PlatformNotSupportedException) { File.Copy(temp, target, true); }
                catch (IOException) { File.Copy(temp, target, true); }
            }
            else File.Move(temp, target);
            VerifyFile(target, expectedSize, expectedHash, CancellationToken.None);
        }
        catch (Exception ex)
        {
            throw new UpdaterFailure("ROLLBACK_FAILED", "Oyun dosyası geri yüklenemedi. Yedek korundu: " + backup + ". " + ex.Message);
        }
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
    public static void EnsureUpdaterSupported(ReleaseManifest manifest)
    {
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.minimum_updater_version)) return;
        if (!Version.TryParse(manifest.minimum_updater_version, out Version minimum))
            throw new UpdaterFailure("MANIFEST_INVALID", "Gerekli kurulum aracı sürümü geçersiz.");
        if (minimum > new Version(LotroReleaseUpdater.CurrentUpdaterVersion))
            throw new UpdaterFailure("UPDATER_TOO_OLD", "Bu yama için daha yeni kurulum aracı gerekiyor. GitHub yayınındaki güncel LOTR TÜRKÇE YAMA kurulum aracını indirin.");
    }

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
                || (m.candidate_dat_sha256 == null
                    ? m.candidate_dat_size != 0
                    : !Hex64(m.candidate_dat_sha256) || m.candidate_dat_size < 1)
                || m.safe_translated_count < 0 || m.skipped_changed_count < 0 || m.critical_review_required_count != 0
                || (!string.IsNullOrWhiteSpace(m.patch_mode)
                    && m.patch_mode != SemanticPatchBuilder.FullPatchMode
                    && m.patch_mode != SemanticPatchBuilder.IncrementalPatchMode)
                || (m.patch_mode == SemanticPatchBuilder.IncrementalPatchMode
                    && (!LotroReleaseUpdater.IsSafeReleaseTag(m.base_release_tag)
                        || m.base_release_id < 1
                        || m.base_asset_id < 1
                        || string.IsNullOrWhiteSpace(m.base_patch_version)
                        || !LotroReleaseUpdater.IsSafeFileName(m.base_asset_name)
                        || m.base_asset_size < 1
                        || !Hex64(m.base_asset_sha256)
                        || !Hex64(m.base_candidate_catalog_sha256)
                        || !Hex64(m.base_candidate_dat_sha256)
                        || m.base_candidate_dat_size < 1
                        || !Hex64(m.candidate_dat_sha256)
                        || m.candidate_dat_size < 1
                        || m.chain_depth < 1 || m.chain_depth > 32)));
        if (commonInvalid || semanticInvalid)
            throw new UpdaterFailure("MANIFEST_INVALID", "Manifest şeması veya release kimliği geçersiz.");
        EnsureUpdaterSupported(m);
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
