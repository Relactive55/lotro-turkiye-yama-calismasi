using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using LotroTrGemini;
using LotroTurkceYama.Setup;

internal static class UpdaterBehaviorTests
{
    private static int _passed;

    private static void Pass(string name) { _passed++; Console.WriteLine("PASS " + name); }

    private static void Expect(string code, Action action, string name)
    {
        try { action(); throw new Exception("expected " + code); }
        catch (UpdaterFailure ex) { if (!string.Equals(ex.Code, code, StringComparison.Ordinal)) throw; Pass(name); }
    }

    private static void ExpectInvalidData(Action action, string name)
    {
        try { action(); throw new Exception("expected invalid DAT rejection"); }
        catch (InvalidDataException) { Pass(name); }
    }

    private static async Task MainAsync()
    {
        string steamFixture = CreateTemp();
        try
        {
            string steamApps = Path.Combine(steamFixture, "steamapps");
            string gameFixture = Path.Combine(steamApps, "common", "Custom LOTRO Folder");
            Directory.CreateDirectory(gameFixture);
            File.WriteAllText(Path.Combine(steamApps, "appmanifest_212500.acf"), "\"AppState\"\n{\n  \"appid\" \"212500\"\n  \"installdir\" \"Custom LOTRO Folder\"\n}\n");
            File.WriteAllBytes(Path.Combine(gameFixture, "client_local_English.dat"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(gameFixture, "LotroLauncher.exe"), new byte[] { 1 });
            bool found = false;
            foreach (string candidate in LotroGameLocator.FindSteamGameDirectories(steamFixture))
                if (LotroGameLocator.IsValid(candidate) && string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(gameFixture), StringComparison.OrdinalIgnoreCase)) found = true;
            if (!found) throw new Exception("Steam LOTRO discovery failed");
            Pass("Steam manifest LOTRO discovery");
        }
        finally { TryDeleteDirectory(steamFixture); }

        byte[] clean = Encoding.UTF8.GetBytes("clean-baseline-fixture");
        byte[] patch = Encoding.UTF8.GetBytes("turkish-patch-fixture");
        string cleanHash = Hash(clean), patchHash = Hash(patch);
        string patchName = "lotro-turkce-yama-test.dat";
        ReleaseManifest manifest = new ReleaseManifest
        {
            schema_version = 1, patch_version = "2026.09.05.test", release_tag = "patch-2026.09.05-test",
            release_id = 42, asset_id = 2, asset_name = patchName, asset_size = patch.Length,
            asset_sha256 = patchHash, source_dat_sha256 = cleanHash, source_dat_size = clean.Length,
            game_version = "fixture", asset_kind = "full_dat"
        };
        StableRelease release = new StableRelease
        {
            id = 42, tag_name = manifest.release_tag, draft = false, prerelease = false,
            assets = new[]
            {
                new ReleaseAsset { id = 1, name = "manifest.json", size = 1, browser_download_url = "https://github.com/Relactive55/lotro-turkiye-yama-calismasi/releases/download/patch-2026.09.05-test/manifest.json" },
                new ReleaseAsset { id = 2, name = patchName, size = patch.Length, browser_download_url = "https://objects.githubusercontent.com/lotro-turkce-yama-test.dat" }
            }
        };
        string manifestJson = new JavaScriptSerializer().Serialize(manifest);
        release.assets[0].size = Encoding.UTF8.GetByteCount(manifestJson);
        FakeTransport transport = new FakeTransport(release, manifestJson, patch);
        LotroReleaseUpdater updater = new LotroReleaseUpdater(transport);
        Tuple<StableRelease, ReleaseManifest> checkedRelease = await updater.CheckLatestAsync(CancellationToken.None);
        if (checkedRelease.Item2.asset_id != 2) throw new Exception("release asset identity");
        Pass("fake stable release and manifest check");

        string cache = CreateTemp();
        try
        {
            await updater.DownloadPatchAsync(release, manifest, cache, CancellationToken.None);
            Verify(Path.Combine(cache, patchName), patch.Length, patchHash);
            Pass("streaming asset fixture verified");

            string game = CreateTemp();
            try
            {
                File.WriteAllBytes(Path.Combine(game, "client_local_English.dat"), clean);
                File.WriteAllBytes(Path.Combine(game, "LotroLauncher.exe"), new byte[] { 0 });
                string state = Path.Combine(game, "installed_patch.json");
                await updater.InstallFullDatAsync(game, Path.Combine(cache, patchName), manifest, state, CancellationToken.None);
                Verify(Path.Combine(game, "client_local_English.dat"), patch.Length, patchHash);
                if (!File.Exists(state)) throw new Exception("state not written");
                if (Directory.GetFiles(Path.Combine(game, ".lotro-turkce-backups")).Length != 1) throw new Exception("backup not written");
                Pass("fake LOTRO backup/install/state");
            }
            finally { TryDeleteDirectory(game); }

            string wrongGame = CreateTemp();
            try
            {
                byte[] wrong = Encoding.UTF8.GetBytes("wrong-baseline");
                File.WriteAllBytes(Path.Combine(wrongGame, "client_local_English.dat"), wrong);
                File.WriteAllBytes(Path.Combine(wrongGame, "LotroLauncher.exe"), new byte[] { 0 });
                Expect("OUTDATED_LOTRO_PATCH", () => updater.InstallFullDatAsync(wrongGame, Path.Combine(cache, patchName), manifest, Path.Combine(wrongGame, "installed_patch.json"), CancellationToken.None).GetAwaiter().GetResult(), "baseline mismatch fail-closed");
                Verify(Path.Combine(wrongGame, "client_local_English.dat"), wrong.Length, Hash(wrong));
            }
            finally { TryDeleteDirectory(wrongGame); }

            string rollbackGame = CreateTemp();
            try
            {
                File.WriteAllBytes(Path.Combine(rollbackGame, "client_local_English.dat"), clean);
                File.WriteAllBytes(Path.Combine(rollbackGame, "LotroLauncher.exe"), new byte[] { 0 });
                Expect("", () => updater.InstallFullDatAsync(rollbackGame, Path.Combine(cache, patchName), manifest, rollbackGame + "\0state", CancellationToken.None).GetAwaiter().GetResult(), "state failure triggers rollback");
                Verify(Path.Combine(rollbackGame, "client_local_English.dat"), clean.Length, cleanHash);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException) { Pass("state failure triggers rollback"); Verify(Path.Combine(rollbackGame, "client_local_English.dat"), clean.Length, cleanHash); }
            finally { TryDeleteDirectory(rollbackGame); }
        }
        finally { TryDeleteDirectory(cache); }

        const int syntheticDid = 0x25001000;
        byte[] semanticSource = CreateSyntheticDat("Open", syntheticDid);
        byte[] semanticExpected = CreateSyntheticDat("Aç", syntheticDid);
        string semanticSourceHash = Hash(semanticSource);
        string semanticRoot = CreateTemp();
        string semanticSourcePath = Path.Combine(semanticRoot, "source.dat");
        string semanticExpectedPath = Path.Combine(semanticRoot, "expected.dat");
        File.WriteAllBytes(semanticSourcePath, semanticSource);
        File.WriteAllBytes(semanticExpectedPath, semanticExpected);
        byte[] badHeader = (byte[])semanticSource.Clone();
        Buffer.BlockCopy(BitConverter.GetBytes((uint)(badHeader.Length + 1)), 0, badHeader, 328, 4);
        string badHeaderPath = Path.Combine(semanticRoot, "bad-header.dat");
        File.WriteAllBytes(badHeaderPath, badHeader);
        ExpectInvalidData(() => { using (TurbineDat dat = new TurbineDat()) { dat.Open(badHeaderPath, false); dat.ValidateLocalizationChains(); } }, "DAT header size corruption rejected");

        byte[] badChain = (byte[])semanticSource.Clone();
        Buffer.BlockCopy(BitConverter.GetBytes(uint.MaxValue), 0, badChain, 2048, 4);
        string badChainPath = Path.Combine(semanticRoot, "bad-chain.dat");
        File.WriteAllBytes(badChainPath, badChain);
        ExpectInvalidData(() => { using (TurbineDat dat = new TurbineDat()) { dat.Open(badChainPath, false); dat.ValidateLocalizationChains(); } }, "DAT out-of-range block pointer rejected");
        List<CatalogRecord> semanticSourceCatalog = ReadCatalog(semanticSourcePath);
        List<CatalogRecord> semanticExpectedCatalog = ReadCatalog(semanticExpectedPath);
        CatalogRecord semanticRecord = semanticSourceCatalog[0];
        string semanticSourceCatalogHash = CatalogIdentity.ComputeCatalogHash(semanticSourceCatalog);
        string semanticCandidateCatalogHash = CatalogIdentity.ComputeCatalogHash(semanticExpectedCatalog);
        SemanticPatchDocument semanticDocument = new SemanticPatchDocument
        {
            schema_version = 1,
            patch_kind = SemanticPatchBuilder.PatchKind,
            patch_version = "tr-2026.09.05.semantic",
            source_dat_sha256 = semanticSourceHash,
            source_dat_size = semanticSource.Length,
            source_catalog_sha256 = semanticSourceCatalogHash,
            translation_catalog_version = "catalog-test",
            patch_generator_version = "generator-test",
            translation_provider = "OPUS",
            translation_model_version = "unverified",
            counts = new SemanticPatchCounts { safe_translated_count = 1 },
            entries = new List<SemanticPatchEntry>
            {
                new SemanticPatchEntry
                {
                    entry_identity = semanticRecord.EntryIdentity,
                    dat_key = semanticRecord.Key,
                    did = semanticRecord.Did,
                    record_index = semanticRecord.RecordIndex,
                    group_index = semanticRecord.GroupIndex,
                    index_in_group = semanticRecord.IndexInGroup,
                    source_digest = semanticRecord.SourceDigest,
                    token_signature = semanticRecord.TokenSignature,
                    target = "Aç",
                    translation_status = TranslationStatuses.HumanApproved,
                    translation_engine = "fixture",
                    translation_engine_version = "1",
                    classification = DiffClassification.UNCHANGED.ToString(),
                    critical_ui = false
                }
            }
        };
        byte[] semanticBytes = Encoding.UTF8.GetBytes(SemanticPatchSerializer.Serialize(semanticDocument));
        string semanticHash = Hash(semanticBytes);
        string semanticName = "lotro-turkce-yama-semantic-test.json";
        ReleaseManifest semanticManifest = new ReleaseManifest
        {
            schema_version = 1,
            patch_version = semanticDocument.patch_version,
            release_tag = "tr-2026.09.05-semantic",
            release_id = 44,
            asset_id = 4,
            asset_name = semanticName,
            asset_size = semanticBytes.Length,
            asset_sha256 = semanticHash,
            source_dat_sha256 = semanticSourceHash,
            source_dat_size = semanticSource.Length,
            source_catalog_sha256 = semanticDocument.source_catalog_sha256,
            candidate_catalog_sha256 = semanticCandidateCatalogHash,
            game_version = "fixture",
            asset_kind = LotroReleaseUpdater.SemanticPatchKind,
            safe_translated_count = 1,
            skipped_changed_count = 0,
            critical_review_required_count = 0
        };
        StableRelease semanticRelease = new StableRelease
        {
            id = 44,
            tag_name = semanticManifest.release_tag,
            draft = false,
            prerelease = false,
            assets = new[]
            {
                new ReleaseAsset { id = 3, name = "manifest.json", size = 1, browser_download_url = "https://github.com/Relactive55/lotro-turkiye-yama-calismasi/releases/download/tr-2026.09.05-semantic/manifest.json" },
                new ReleaseAsset { id = 4, name = semanticName, size = semanticBytes.Length, browser_download_url = "https://github.com/Relactive55/lotro-turkiye-yama-calismasi/releases/download/tr-2026.09.05-semantic/" + semanticName }
            }
        };
        string semanticManifestJson = new JavaScriptSerializer().Serialize(semanticManifest);
        semanticRelease.assets[0].size = Encoding.UTF8.GetByteCount(semanticManifestJson);
        FakeTransport semanticTransport = new FakeTransport(semanticRelease, semanticManifestJson, semanticBytes);
        LotroReleaseUpdater semanticUpdater = new LotroReleaseUpdater(semanticTransport);
        Tuple<StableRelease, ReleaseManifest> semanticChecked = await semanticUpdater.CheckLatestAsync(CancellationToken.None);
        if (semanticChecked.Item2.asset_kind != LotroReleaseUpdater.SemanticPatchKind) throw new Exception("semantic release kind");
        Pass("semantic delta release manifest accepted");
        semanticManifest.critical_review_required_count = 1;
        Expect("MANIFEST_INVALID", () => ManifestValidator.Validate(semanticManifest, semanticRelease, semanticRelease.assets[0]), "semantic release with unresolved critical rows rejected");
        semanticManifest.critical_review_required_count = 0;
        string semanticCache = CreateTemp();
        try
        {
            await semanticUpdater.DownloadPatchAsync(semanticRelease, semanticManifest, semanticCache, CancellationToken.None);
            string semanticGame = CreateTemp();
            try
            {
                File.WriteAllBytes(Path.Combine(semanticGame, "client_local_English.dat"), semanticSource);
                File.WriteAllBytes(Path.Combine(semanticGame, "LotroLauncher.exe"), new byte[] { 0 });
                InstalledPatchState installed = await semanticUpdater.InstallPatchAsync(
                    semanticGame,
                    Path.Combine(semanticCache, semanticName),
                    semanticManifest,
                    Path.Combine(semanticGame, "installed_patch.json"),
                    CancellationToken.None);
                List<CatalogRecord> installedCatalog = ReadCatalog(Path.Combine(semanticGame, "client_local_English.dat"));
                if (installedCatalog.Count != 1 || installedCatalog[0].Source != "Aç") throw new Exception("semantic target not installed");
                if (FirstLocalizationIsCompressed(Path.Combine(semanticGame, "client_local_English.dat"))
                    != FirstLocalizationIsCompressed(semanticSourcePath))
                    throw new Exception("semantic writer changed the official storage representation");
                using (TurbineDat sourceDat = new TurbineDat())
                using (TurbineDat installedDat = new TurbineDat())
                {
                    sourceDat.Open(semanticSourcePath, false);
                    installedDat.Open(Path.Combine(semanticGame, "client_local_English.dat"), false);
                    DatEntry sourceEntry = sourceDat.ListLocalization()[0];
                    DatEntry installedEntry = installedDat.ListLocalization()[0];
                    if (installedEntry.Offset == sourceEntry.Offset && installedEntry.Size2 != sourceEntry.Size2)
                        throw new Exception("semantic writer changed the official in-place allocation metadata");
                }
                if (installed.candidate_catalog_sha256 != semanticCandidateCatalogHash) throw new Exception("semantic catalog state mismatch");
                if (!File.Exists(installed.source_backup_file)) throw new Exception("clean source backup missing");
                Verify(installed.source_backup_file, semanticSource.Length, semanticSourceHash);
                Pass("semantic DAT patch backup/install/round-trip");
                Pass("semantic writer preserves official storage representation");
                Pass("semantic writer preserves official in-place allocation metadata");

                // Simulate the official launcher updating a DAT that already
                // contains our Turkish row. The binary version changes while
                // the unchanged localized row remains Turkish.
                byte[] nextClean = (byte[])semanticSource.Clone();
                nextClean[nextClean.Length - 1] ^= 0x01;
                byte[] updatedPatched = File.ReadAllBytes(Path.Combine(semanticGame, "client_local_English.dat"));
                updatedPatched[updatedPatched.Length - 1] ^= 0x01;
                File.WriteAllBytes(Path.Combine(semanticGame, "client_local_English.dat"), updatedPatched);

                SemanticPatchDocument nextDocument = new SemanticPatchDocument
                {
                    schema_version = 1,
                    patch_kind = SemanticPatchBuilder.PatchKind,
                    patch_version = "tr-2026.09.06.semantic",
                    source_dat_sha256 = Hash(nextClean),
                    source_dat_size = nextClean.Length,
                    source_catalog_sha256 = semanticSourceCatalogHash,
                    translation_catalog_version = "catalog-test-2",
                    patch_generator_version = "generator-test",
                    translation_provider = "OPUS",
                    translation_model_version = "unverified",
                    counts = new SemanticPatchCounts { safe_translated_count = 1 },
                    entries = semanticDocument.entries
                };
                byte[] nextPatchBytes = Encoding.UTF8.GetBytes(SemanticPatchSerializer.Serialize(nextDocument));
                string nextPatchPath = Path.Combine(semanticCache, "lotro-turkce-yama-semantic-update-test.json");
                File.WriteAllBytes(nextPatchPath, nextPatchBytes);
                ReleaseManifest nextManifest = new ReleaseManifest
                {
                    schema_version = 1,
                    patch_version = nextDocument.patch_version,
                    release_tag = "tr-2026.09.06-semantic",
                    release_id = 45,
                    asset_id = 5,
                    asset_name = Path.GetFileName(nextPatchPath),
                    asset_size = nextPatchBytes.Length,
                    asset_sha256 = Hash(nextPatchBytes),
                    source_dat_sha256 = nextDocument.source_dat_sha256,
                    source_dat_size = nextDocument.source_dat_size,
                    source_catalog_sha256 = nextDocument.source_catalog_sha256,
                    candidate_catalog_sha256 = semanticCandidateCatalogHash,
                    game_version = "fixture-2",
                    asset_kind = LotroReleaseUpdater.SemanticPatchKind,
                    safe_translated_count = 1
                };
                InstalledPatchState upgraded = await semanticUpdater.InstallPatchAsync(
                    semanticGame,
                    nextPatchPath,
                    nextManifest,
                    Path.Combine(semanticGame, "installed_patch.json"),
                    CancellationToken.None);
                List<CatalogRecord> upgradedCatalog = ReadCatalog(Path.Combine(semanticGame, "client_local_English.dat"));
                if (upgradedCatalog.Count != 1 || upgradedCatalog[0].Source != "Aç") throw new Exception("updated patched DAT was not recovered");
                if (upgraded.source_backup_catalog_sha256 != semanticSourceCatalogHash) throw new Exception("recovered clean catalog state missing");
                InstalledPatchState repeated = await semanticUpdater.InstallPatchAsync(
                    semanticGame,
                    nextPatchPath,
                    nextManifest,
                    Path.Combine(semanticGame, "installed_patch.json"),
                    CancellationToken.None);
                if (repeated.sha256 != upgraded.sha256) throw new Exception("idempotent re-run changed state");
                Pass("official update over patched DAT auto-recovers in one action");
                Pass("same semantic release re-run is idempotent");

                byte[] newerWithoutRelease = File.ReadAllBytes(Path.Combine(semanticGame, "client_local_English.dat"));
                newerWithoutRelease[newerWithoutRelease.Length - 2] ^= 0x01;
                File.WriteAllBytes(Path.Combine(semanticGame, "client_local_English.dat"), newerWithoutRelease);
                string pendingHash = Hash(newerWithoutRelease);
                Expect("PATCH_RELEASE_PENDING", () => semanticUpdater.InstallPatchAsync(
                    semanticGame,
                    nextPatchPath,
                    nextManifest,
                    Path.Combine(semanticGame, "installed_patch.json"),
                    CancellationToken.None).GetAwaiter().GetResult(), "new official version waits for matching Turkish release");
                Verify(Path.Combine(semanticGame, "client_local_English.dat"), newerWithoutRelease.Length, pendingHash);
            }
            finally { TryDeleteDirectory(semanticGame); }
        }
        finally { TryDeleteDirectory(semanticCache); TryDeleteDirectory(semanticRoot); }

        FakeTransport draftTransport = new FakeTransport(new StableRelease { id = 43, tag_name = "draft", draft = true, prerelease = false, assets = new ReleaseAsset[0] }, "{}", patch);
        Expect("NO_STABLE_RELEASE", () => new LotroReleaseUpdater(draftTransport).CheckLatestAsync(CancellationToken.None).GetAwaiter().GetResult(), "draft release rejected");
        DeveloperSafetyTests.Run();
        Console.WriteLine("behavior_tests_passed=" + _passed);
    }

    public static void Main() { MainAsync().GetAwaiter().GetResult(); }

    private static string CreateTemp() { string p = Path.Combine(Path.GetTempPath(), "lotro-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
    private static string Hash(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return FixedGitHubTransport.ToHex(sha.ComputeHash(bytes)); }
    private static List<CatalogRecord> ReadCatalog(string path)
    {
        List<CatalogRecord> records = new List<CatalogRecord>();
        long position = 0;
        using (TurbineDat dat = new TurbineDat())
        {
            dat.Open(path, false);
            foreach (DatEntry entry in dat.ListLocalization())
            {
                byte[] payload = TurbineDat.MaybeDecompress(dat.ReadRaw(entry));
                records.AddRange(LocBin.Parse(payload, entry.Id).GetCatalogRecords(entry.Id, ref position));
            }
        }
        return records;
    }
    private static byte[] CreateSyntheticDat(string text, int did)
    {
        byte[] payload;
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode, true))
        {
            writer.Write(0);
            writer.Write(did);
            writer.Write(1);
            writer.Write((byte)1);
            writer.Write((long)123456789);
            writer.Write(1);
            if (text.Length >= 128) throw new ArgumentOutOfRangeException(nameof(text));
            writer.Write((byte)text.Length);
            writer.Write(Encoding.Unicode.GetBytes(text));
            writer.Write(0);
            writer.Write((byte)0);
            writer.Flush();
            payload = stream.ToArray();
        }

        const int blockSize = 1024;
        const int directoryOffset = 1024;
        const int dataOffset = 2048;
        byte[] dat = new byte[3072];
        using (MemoryStream stream = new MemoryStream(dat))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            stream.Position = 320;
            writer.Write(TurbineDat.MagicBt);
            writer.Write((uint)blockSize);
            writer.Write((uint)dat.Length);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((uint)directoryOffset);

            stream.Position = directoryOffset + 504;
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(unchecked((uint)did));
            writer.Write((uint)dataOffset);
            writer.Write((uint)payload.Length);
            writer.Write(0u);
            writer.Write(1u);
            writer.Write((uint)payload.Length);
            writer.Write(0u);

            stream.Position = dataOffset;
            writer.Write(0u);
            writer.Write(payload);
        }
        return dat;
    }
    private static bool FirstLocalizationIsCompressed(string path)
    {
        using (TurbineDat dat = new TurbineDat())
        {
            dat.Open(path, false);
            List<DatEntry> entries = dat.ListLocalization();
            if (entries.Count == 0) throw new Exception("localization fixture missing");
            return TurbineDat.LooksCompressed(dat.ReadRaw(entries[0]));
        }
    }
    private static void Verify(string path, long size, string hash) { if (!File.Exists(path) || new FileInfo(path).Length != size || !string.Equals(LotroReleaseUpdater.HashFile(path), hash, StringComparison.OrdinalIgnoreCase)) throw new Exception("verify " + path); }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }

    private sealed class FakeTransport : IReleaseTransport
    {
        private readonly StableRelease _release; private readonly string _manifest; private readonly byte[] _patch;
        public FakeTransport(StableRelease release, string manifest, byte[] patch) { _release = release; _manifest = manifest; _patch = patch; }
        public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken) { return Task.FromResult(uri.AbsoluteUri.EndsWith("/releases/latest", StringComparison.Ordinal) ? new JavaScriptSerializer().Serialize(_release) : _manifest); }
        public Task<DownloadResult> DownloadAsync(Uri uri, string partPath, long expectedSize, string expectedSha256, CancellationToken cancellationToken)
        {
            File.WriteAllBytes(partPath, _patch);
            return Task.FromResult(new DownloadResult { Size = _patch.Length, Sha256 = Hash(_patch) });
        }
        public void Dispose() { }
    }
}
