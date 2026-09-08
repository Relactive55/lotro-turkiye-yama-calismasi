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
                ExpectCancellationAtProgress(updater, game, Path.Combine(cache, patchName), manifest, state,
                    "Tam DAT güvenli biçimde yerleştiriliyor",
                    "full DAT final-progress cancellation preserves live DAT timestamp and state");
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
        ReleaseManifest futureUpdaterManifest = CloneManifest(semanticManifest);
        futureUpdaterManifest.minimum_updater_version = "9999.0.0.0";
        Expect("UPDATER_TOO_OLD", () => ManifestValidator.Validate(futureUpdaterManifest, semanticRelease, semanticRelease.assets[0]),
            "future minimum updater version is rejected clearly");
        ReleaseManifest malformedUpdaterManifest = CloneManifest(semanticManifest);
        malformedUpdaterManifest.minimum_updater_version = "not-a-version";
        Expect("MANIFEST_INVALID", () => ManifestValidator.Validate(malformedUpdaterManifest, semanticRelease, semanticRelease.assets[0]),
            "malformed minimum updater version is rejected");
        foreach (Tuple<string, long> pair in new[]
        {
            Tuple.Create<string, long>(null, 1),
            Tuple.Create<string, long>(null, -1),
            Tuple.Create(" ", 1L),
            Tuple.Create("", 0L),
            Tuple.Create(new string('0', 64), 0L)
        })
        {
            ReleaseManifest unpairedCandidate = CloneManifest(semanticManifest);
            unpairedCandidate.candidate_dat_sha256 = pair.Item1;
            unpairedCandidate.candidate_dat_size = pair.Item2;
            Expect("MANIFEST_INVALID", () => ManifestValidator.Validate(unpairedCandidate, semanticRelease, semanticRelease.assets[0]),
                "candidate DAT digest and size must be a valid pair");
        }
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

                string installedDatPath = Path.Combine(semanticGame, "client_local_English.dat");
                string installedStatePath = Path.Combine(semanticGame, "installed_patch.json");
                if (!LotroReleaseUpdater.IsInstalledFileValid(installed, CancellationToken.None, semanticGame))
                    throw new Exception("verified installed state was rejected");
                string otherGame = CreateTemp();
                try
                {
                    if (LotroReleaseUpdater.IsInstalledFileValid(installed, CancellationToken.None, otherGame))
                        throw new Exception("installed state accepted for a different game directory");
                    Pass("installed state is bound to the selected game directory");
                }
                finally { TryDeleteDirectory(otherGame); }
                byte[] installedBytes = File.ReadAllBytes(installedDatPath);
                DateTime installedTime = File.GetLastWriteTimeUtc(installedDatPath);
                byte[] tamperedInstalled = (byte[])installedBytes.Clone();
                tamperedInstalled[tamperedInstalled.Length - 1] ^= 1;
                File.WriteAllBytes(installedDatPath, tamperedInstalled);
                if (LotroReleaseUpdater.IsInstalledFileValid(installed, CancellationToken.None, semanticGame))
                    throw new Exception("same-size installed DAT modification was trusted");
                Pass("installed state rejects same-size DAT content changes");
                File.WriteAllBytes(installedDatPath, installedBytes);
                File.SetLastWriteTimeUtc(installedDatPath, installedTime);

                ReleaseManifest wrongRootDigest = CloneManifest(semanticManifest);
                wrongRootDigest.release_id += 100; // Exercise a new release, not the idempotent exit.
                wrongRootDigest.candidate_dat_size = installed.size;
                wrongRootDigest.candidate_dat_sha256 = new string('0', 64);
                ExpectInstallationUnchanged("CANDIDATE_DAT_MISMATCH", installedDatPath, installedStatePath,
                    () => semanticUpdater.InstallPatchAsync(semanticGame, Path.Combine(semanticCache, semanticName),
                        wrongRootDigest, installedStatePath, CancellationToken.None).GetAwaiter().GetResult(),
                    "wrong root candidate digest preserves installed DAT and state");
                ReleaseManifest wrongRootSize = CloneManifest(wrongRootDigest);
                wrongRootSize.candidate_dat_sha256 = installed.sha256;
                wrongRootSize.candidate_dat_size = installed.size + 1;
                ExpectInstallationUnchanged("CANDIDATE_DAT_MISMATCH", installedDatPath, installedStatePath,
                    () => semanticUpdater.InstallPatchAsync(semanticGame, Path.Combine(semanticCache, semanticName),
                        wrongRootSize, installedStatePath, CancellationToken.None).GetAwaiter().GetResult(),
                    "wrong root candidate size preserves installed DAT and state");

                ReleaseManifest cancelledRoot = CloneManifest(wrongRootDigest);
                cancelledRoot.candidate_dat_sha256 = installed.sha256;
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    bool candidateCreated = false;
                    ExpectInstallationUnchanged("CANCELLED", installedDatPath, installedStatePath,
                        () => semanticUpdater.InstallPatchAsync(semanticGame, Path.Combine(semanticCache, semanticName),
                            cancelledRoot, installedStatePath, cancellation.Token, message =>
                            {
                                if (message.StartsWith("Türkçe satırlar DAT adayına yazılıyor", StringComparison.Ordinal))
                                {
                                    candidateCreated = File.Exists(installedDatPath + ".lotro-candidate.part");
                                    cancellation.Cancel();
                                }
                            }).GetAwaiter().GetResult(),
                        "cancellation after candidate copy cleans temporary DAT without restoring the live DAT");
                    if (!candidateCreated) throw new Exception("cancellation did not reach a created candidate");
                }
                ExpectCancellationAtProgress(semanticUpdater, semanticGame, Path.Combine(semanticCache, semanticName),
                    cancelledRoot, installedStatePath, "Aday DAT baştan sona doğrulanıyor ve yerleştiriliyor",
                    "root final-progress cancellation preserves live DAT timestamp and state");

                // The existing backup is an exact match for this manifest,
                // but it must not justify replacing a newly changed live DAT.
                Verify(installed.source_backup_file, semanticSource.Length, semanticSourceHash);
                byte[] updatedWithoutMatchingRelease = (byte[])installedBytes.Clone();
                updatedWithoutMatchingRelease[updatedWithoutMatchingRelease.Length - 1] ^= 1;
                File.WriteAllBytes(installedDatPath, updatedWithoutMatchingRelease);
                ExpectInstallationUnchanged("PATCH_RELEASE_PENDING", installedDatPath, installedStatePath,
                    () => semanticUpdater.InstallPatchAsync(semanticGame, Path.Combine(semanticCache, semanticName),
                        semanticManifest, installedStatePath, CancellationToken.None).GetAwaiter().GetResult(),
                    "an exact old clean backup cannot downgrade a newer official DAT");
                File.WriteAllBytes(installedDatPath, installedBytes);
                File.SetLastWriteTimeUtc(installedDatPath, installedTime);

                // A chained correction changes only the row that needs a fix.
                // The predecessor release/asset is resolved by tag, but is not
                // downloaded when the installed state already matches it.
                string incrementalGame = CreateTemp();
                try
                {
                    File.WriteAllBytes(Path.Combine(incrementalGame, "client_local_English.dat"), semanticSource);
                    File.WriteAllBytes(Path.Combine(incrementalGame, "LotroLauncher.exe"), new byte[] { 0 });
                    string incrementalStatePath = Path.Combine(incrementalGame, "installed_patch.json");
                    InstalledPatchState baseInstalled = await semanticUpdater.InstallPatchAsync(
                        incrementalGame,
                        Path.Combine(semanticCache, semanticName),
                        semanticManifest,
                        incrementalStatePath,
                        CancellationToken.None);
                    string baseDatPath = Path.Combine(incrementalGame, "client_local_English.dat");
                    List<CatalogRecord> baseCatalog = ReadCatalog(baseDatPath);
                    CatalogRecord baseRecord = baseCatalog[0];
                    SemanticPatchDocument incrementalDocument = new SemanticPatchDocument
                    {
                        schema_version = 1,
                        patch_kind = SemanticPatchBuilder.PatchKind,
                        patch_mode = SemanticPatchBuilder.IncrementalPatchMode,
                        patch_version = "tr-2026.09.07.incremental",
                        source_dat_sha256 = semanticSourceHash,
                        source_dat_size = semanticSource.Length,
                        source_catalog_sha256 = semanticSourceCatalogHash,
                        base_patch_version = semanticDocument.patch_version,
                        base_candidate_dat_sha256 = baseInstalled.sha256,
                        base_candidate_dat_size = baseInstalled.size,
                        base_candidate_catalog_sha256 = baseInstalled.candidate_catalog_sha256,
                        translation_catalog_version = "catalog-test-incremental",
                        patch_generator_version = "generator-test",
                        translation_provider = "OPUS",
                        translation_model_version = "unverified",
                        counts = new SemanticPatchCounts { safe_translated_count = 1 },
                        entries = new List<SemanticPatchEntry>
                        {
                            new SemanticPatchEntry
                            {
                                entry_identity = baseRecord.EntryIdentity,
                                dat_key = baseRecord.Key,
                                did = baseRecord.Did,
                                record_index = baseRecord.RecordIndex,
                                group_index = baseRecord.GroupIndex,
                                index_in_group = baseRecord.IndexInGroup,
                                source_digest = baseRecord.SourceDigest,
                                token_signature = baseRecord.TokenSignature,
                                target = "Düzeltilmiş",
                                translation_status = TranslationStatuses.HumanApproved,
                                translation_engine = "fixture",
                                translation_engine_version = "2",
                                classification = DiffClassification.UNCHANGED.ToString(),
                                critical_ui = false
                            }
                        }
                    };
                    string previewPath = Path.Combine(incrementalGame, "incremental-preview.dat");
                    ManagedSemanticDatPatcher.Result preview = ManagedSemanticDatPatcher.BuildIncrementalCandidate(
                        baseDatPath,
                        previewPath,
                        incrementalDocument,
                        null,
                        CancellationToken.None);
                    TryDelete(previewPath);
                    byte[] incrementalBytes = Encoding.UTF8.GetBytes(SemanticPatchSerializer.Serialize(incrementalDocument));
                    // Different releases may reuse an asset name. Their cached
                    // contents must remain distinct when the full chain is needed.
                    string incrementalName = semanticName;
                    ReleaseManifest incrementalManifest = new ReleaseManifest
                    {
                        schema_version = 1,
                        patch_version = incrementalDocument.patch_version,
                        patch_mode = SemanticPatchBuilder.IncrementalPatchMode,
                        release_tag = "tr-2026.09.07-incremental",
                        release_id = 46,
                        asset_id = 7,
                        asset_name = incrementalName,
                        asset_size = incrementalBytes.Length,
                        asset_sha256 = Hash(incrementalBytes),
                        source_dat_sha256 = semanticSourceHash,
                        source_dat_size = semanticSource.Length,
                        source_catalog_sha256 = semanticSourceCatalogHash,
                        candidate_catalog_sha256 = preview.CatalogSha256,
                        candidate_dat_sha256 = preview.DatSha256,
                        candidate_dat_size = preview.DatSize,
                        game_version = "fixture-3",
                        asset_kind = LotroReleaseUpdater.SemanticPatchKind,
                        base_patch_version = semanticManifest.patch_version,
                        base_release_tag = semanticManifest.release_tag,
                        base_release_id = semanticRelease.id,
                        base_asset_id = semanticManifest.asset_id,
                        base_asset_name = semanticManifest.asset_name,
                        base_asset_size = semanticManifest.asset_size,
                        base_asset_sha256 = semanticManifest.asset_sha256,
                        base_candidate_dat_sha256 = baseInstalled.sha256,
                        base_candidate_dat_size = baseInstalled.size,
                        base_candidate_catalog_sha256 = baseInstalled.candidate_catalog_sha256,
                        chain_depth = 1,
                        safe_translated_count = 1,
                        skipped_changed_count = 0,
                        critical_review_required_count = 0
                    };
                    StableRelease incrementalRelease = new StableRelease
                    {
                        id = incrementalManifest.release_id,
                        tag_name = incrementalManifest.release_tag,
                        draft = false,
                        prerelease = false,
                        assets = new[]
                        {
                            new ReleaseAsset { id = 6, name = "manifest.json", browser_download_url = "https://github.com/Relactive55/lotro-turkiye-yama-calismasi/releases/download/" + incrementalManifest.release_tag + "/manifest.json" },
                            new ReleaseAsset { id = incrementalManifest.asset_id, name = incrementalName, size = incrementalBytes.Length, browser_download_url = "https://github.com/Relactive55/lotro-turkiye-yama-calismasi/releases/download/" + incrementalManifest.release_tag + "/" + incrementalName }
                        }
                    };
                    string incrementalManifestJson = new JavaScriptSerializer().Serialize(incrementalManifest);
                    incrementalRelease.assets[0].size = Encoding.UTF8.GetByteCount(incrementalManifestJson);
                    ManifestValidator.Validate(incrementalManifest, incrementalRelease, incrementalRelease.assets[0]);
                    ChainTransport chainTransport = new ChainTransport(
                        semanticRelease,
                        new JavaScriptSerializer().Serialize(semanticManifest),
                        semanticBytes,
                        incrementalRelease,
                        incrementalManifestJson,
                        incrementalBytes);
                    LotroReleaseUpdater chainUpdater = new LotroReleaseUpdater(chainTransport);
                    string chainCache = CreateTemp();
                    try
                    {
                        List<PatchPackage> packages = await chainUpdater.DownloadPatchChainAsync(
                            incrementalRelease,
                            incrementalManifest,
                            chainCache,
                            baseInstalled,
                            CancellationToken.None);
                        if (packages.Count != 1 || packages[0].manifest.patch_version != incrementalManifest.patch_version)
                            throw new Exception("chain did not trim the installed predecessor");
                        if (chainTransport.Downloaded.Count != 1 || chainTransport.Downloaded[0].IndexOf(incrementalName, StringComparison.Ordinal) < 0)
                            throw new Exception("chain downloaded the large predecessor unexpectedly");

                        string staleCache = CreateTemp();
                        byte[] verifiedBaseBytes = File.ReadAllBytes(baseDatPath);
                        DateTime verifiedBaseTime = File.GetLastWriteTimeUtc(baseDatPath);
                        try
                        {
                            byte[] staleBytes = (byte[])verifiedBaseBytes.Clone();
                            staleBytes[staleBytes.Length - 1] ^= 1;
                            File.WriteAllBytes(baseDatPath, staleBytes);
                            int requestsBefore = chainTransport.Downloaded.Count;
                            List<PatchPackage> fullChain = await chainUpdater.DownloadPatchChainAsync(
                                incrementalRelease, incrementalManifest, staleCache, baseInstalled, CancellationToken.None);
                            if (fullChain.Count != 2 || chainTransport.Downloaded.Count != requestsBefore + 2)
                                throw new Exception("stale installed state incorrectly trimmed the predecessor");
                            Pass("same-size stale DAT state downloads the complete patch chain");
                            if (string.Equals(fullChain[0].path, fullChain[1].path, StringComparison.OrdinalIgnoreCase)
                                || Path.GetFileName(fullChain[0].path) != Path.GetFileName(fullChain[1].path))
                                throw new Exception("same-named release assets did not receive separate cache paths");
                            Verify(fullChain[0].path, semanticBytes.Length, Hash(semanticBytes));
                            Verify(fullChain[1].path, incrementalBytes.Length, Hash(incrementalBytes));
                            Pass("same asset filename in two releases keeps both verified cache contents");
                        }
                        finally
                        {
                            File.WriteAllBytes(baseDatPath, verifiedBaseBytes);
                            File.SetLastWriteTimeUtc(baseDatPath, verifiedBaseTime);
                            TryDeleteDirectory(staleCache);
                        }

                        ReleaseManifest wrongIncrementalDigest = CloneManifest(incrementalManifest);
                        wrongIncrementalDigest.candidate_dat_sha256 = new string('0', 64);
                        ExpectInstallationUnchanged("CANDIDATE_DAT_MISMATCH", baseDatPath, incrementalStatePath,
                            () => chainUpdater.InstallPatchAsync(incrementalGame, packages[0].path,
                                wrongIncrementalDigest, incrementalStatePath, CancellationToken.None).GetAwaiter().GetResult(),
                            "wrong incremental candidate digest preserves predecessor DAT and state");
                        ReleaseManifest wrongIncrementalSize = CloneManifest(incrementalManifest);
                        wrongIncrementalSize.candidate_dat_size++;
                        ExpectInstallationUnchanged("CANDIDATE_DAT_MISMATCH", baseDatPath, incrementalStatePath,
                            () => chainUpdater.InstallPatchAsync(incrementalGame, packages[0].path,
                                wrongIncrementalSize, incrementalStatePath, CancellationToken.None).GetAwaiter().GetResult(),
                            "wrong incremental candidate size preserves predecessor DAT and state");
                        ExpectCancellationAtProgress(chainUpdater, incrementalGame, packages[0].path,
                            incrementalManifest, incrementalStatePath, "Türkçe düzeltme adayı doğrulanıyor ve yerleştiriliyor",
                            "incremental final-progress cancellation preserves predecessor DAT timestamp and state");
                        InstalledPatchState incrementalInstalled = await chainUpdater.InstallPatchChainAsync(
                            incrementalGame,
                            packages,
                            incrementalStatePath,
                            CancellationToken.None);
                        List<CatalogRecord> incrementalCatalog = ReadCatalog(baseDatPath);
                        if (incrementalCatalog.Count != 1 || incrementalCatalog[0].Source != "Düzeltilmiş")
                            throw new Exception("incremental correction target not installed");
                        if (incrementalInstalled.source_dat_sha256 != semanticSourceHash
                            || incrementalInstalled.candidate_catalog_sha256 != preview.CatalogSha256)
                            throw new Exception("incremental state baseline/candidate mismatch");
                        Pass("incremental semantic chain downloads only the correction layer");
                        Pass("incremental semantic layer applies directly to predecessor DAT");
                    }
                    finally { TryDeleteDirectory(chainCache); }
                }
                finally { TryDeleteDirectory(incrementalGame); }

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
                ExpectCancellationAtProgress(semanticUpdater, semanticGame, nextPatchPath, nextManifest,
                    Path.Combine(semanticGame, "installed_patch.json"), "Güncellenen DAT güvenle birleştiriliyor",
                    "recovery final-progress cancellation preserves updated live DAT timestamp and state");
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
                if (!File.Exists(upgraded.source_backup_file)) throw new Exception("pending-release test requires the prior clean backup");
                Verify(upgraded.source_backup_file, new FileInfo(upgraded.source_backup_file).Length, upgraded.source_backup_sha256);
                ExpectInstallationUnchanged("PATCH_RELEASE_PENDING", Path.Combine(semanticGame, "client_local_English.dat"),
                    Path.Combine(semanticGame, "installed_patch.json"), () => semanticUpdater.InstallPatchAsync(
                    semanticGame,
                    nextPatchPath,
                    nextManifest,
                    Path.Combine(semanticGame, "installed_patch.json"),
                    CancellationToken.None).GetAwaiter().GetResult(), "new official version waits for matching release despite an existing clean backup");
                Verify(Path.Combine(semanticGame, "client_local_English.dat"), newerWithoutRelease.Length, pendingHash);
            }
            finally { TryDeleteDirectory(semanticGame); }
        }
        finally { TryDeleteDirectory(semanticCache); TryDeleteDirectory(semanticRoot); }

        FakeTransport draftTransport = new FakeTransport(new StableRelease { id = 43, tag_name = "draft", draft = true, prerelease = false, assets = new ReleaseAsset[0] }, "{}", patch);
        Expect("NO_STABLE_RELEASE", () => new LotroReleaseUpdater(draftTransport).CheckLatestAsync(CancellationToken.None).GetAwaiter().GetResult(), "draft release rejected");
        DeveloperSafetyTests.Run();
        _passed += PatcherRegressionTests.Run();
        _passed += CacheRegressionTests.Run();
        Console.WriteLine("behavior_tests_passed=" + _passed);
    }

    public static void Main() { MainAsync().GetAwaiter().GetResult(); }

    private static string CreateTemp() { string p = Path.Combine(Path.GetTempPath(), "lotro-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
    private static string Hash(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return FixedGitHubTransport.ToHex(sha.ComputeHash(bytes)); }
    private static ReleaseManifest CloneManifest(ReleaseManifest manifest)
    {
        JavaScriptSerializer serializer = new JavaScriptSerializer();
        return serializer.Deserialize<ReleaseManifest>(serializer.Serialize(manifest));
    }

    private static void ExpectCancellationAtProgress(LotroReleaseUpdater updater, string gameDirectory, string patchPath,
        ReleaseManifest manifest, string statePath, string phase, string name)
    {
        using (CancellationTokenSource cancellation = new CancellationTokenSource())
        {
            bool reached = false;
            ExpectInstallationUnchanged("CANCELLED", Path.Combine(gameDirectory, "client_local_English.dat"), statePath,
                () => updater.InstallPatchAsync(gameDirectory, patchPath, manifest, statePath, cancellation.Token, message =>
                {
                    if (message.StartsWith(phase, StringComparison.Ordinal))
                    {
                        reached = true;
                        cancellation.Cancel();
                    }
                }).GetAwaiter().GetResult(), name);
            if (!reached) throw new Exception("cancellation did not reach the final replacement phase");
        }
    }

    private static void ExpectInstallationUnchanged(string code, string target, string statePath, Action action, string name)
    {
        long size = new FileInfo(target).Length;
        string hash = LotroReleaseUpdater.HashFile(target);
        DateTime timestamp = File.GetLastWriteTimeUtc(target);
        string state = File.Exists(statePath) ? Convert.ToBase64String(File.ReadAllBytes(statePath)) : null;
        bool rejected = false;
        try { action(); }
        catch (UpdaterFailure ex) { if (ex.Code != code) throw; rejected = true; }
        catch (OperationCanceledException) { if (code != "CANCELLED") throw; rejected = true; }
        if (!rejected) throw new Exception("expected " + code);
        Verify(target, size, hash);
        if (File.GetLastWriteTimeUtc(target) != timestamp) throw new Exception("rejected candidate rewrote the live DAT");
        string currentState = File.Exists(statePath) ? Convert.ToBase64String(File.ReadAllBytes(statePath)) : null;
        if (state != currentState) throw new Exception("rejected candidate changed installed state");
        if (File.Exists(target + ".lotro-candidate.part")) throw new Exception("rejected candidate left a temporary DAT");
        Pass(name);
    }
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
    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
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

    private sealed class ChainTransport : IReleaseTransport
    {
        private readonly StableRelease _baseRelease;
        private readonly string _baseManifest;
        private readonly byte[] _basePatch;
        private readonly StableRelease _latestRelease;
        private readonly string _latestManifest;
        private readonly byte[] _latestPatch;
        public readonly List<string> Downloaded = new List<string>();

        public ChainTransport(
            StableRelease baseRelease,
            string baseManifest,
            byte[] basePatch,
            StableRelease latestRelease,
            string latestManifest,
            byte[] latestPatch)
        {
            _baseRelease = baseRelease;
            _baseManifest = baseManifest;
            _basePatch = basePatch;
            _latestRelease = latestRelease;
            _latestManifest = latestManifest;
            _latestPatch = latestPatch;
        }

        public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
        {
            string absolute = uri.AbsoluteUri;
            if (absolute.EndsWith("/releases/latest", StringComparison.Ordinal))
                return Task.FromResult(new JavaScriptSerializer().Serialize(_latestRelease));
            if (absolute.IndexOf("/releases/tags/" + _baseRelease.tag_name, StringComparison.Ordinal) >= 0)
                return Task.FromResult(new JavaScriptSerializer().Serialize(_baseRelease));
            if (absolute.IndexOf("/" + _baseRelease.tag_name + "/manifest.json", StringComparison.Ordinal) >= 0)
                return Task.FromResult(_baseManifest);
            if (absolute.IndexOf("/" + _latestRelease.tag_name + "/manifest.json", StringComparison.Ordinal) >= 0)
                return Task.FromResult(_latestManifest);
            throw new InvalidOperationException("unexpected GET " + absolute);
        }

        public Task<DownloadResult> DownloadAsync(Uri uri, string partPath, long expectedSize, string expectedSha256, CancellationToken cancellationToken)
        {
            string absolute = uri.AbsoluteUri;
            bool latest = absolute.IndexOf("/" + _latestRelease.tag_name + "/", StringComparison.Ordinal) >= 0;
            byte[] bytes = latest ? _latestPatch : _basePatch;
            Downloaded.Add(absolute);
            File.WriteAllBytes(partPath, bytes);
            return Task.FromResult(new DownloadResult { Size = bytes.Length, Sha256 = Hash(bytes) });
        }

        public void Dispose() { }
    }
}
