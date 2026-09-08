using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using LotroTrGemini;
using LotroTurkceYama.Setup;

// Rebuild a root from the anchored clean source. Legacy candidate metadata is
// deliberately not trusted: new result identities come from the actual writer.
internal static class RootGeneration
{
    internal static int Run(string[] args)
    {
        if (args.Length != 8)
        {
            Console.Error.WriteLine("Usage: SemanticPatchGenerator --verified-root <clean.dat> <full-semantic.json> <manifest.json> <manual-decisions-or-dash> <new-output-directory> <new-version> <official-game-version>");
            return 2;
        }
        string source = Path.GetFullPath(args[1]);
        string asset = Path.GetFullPath(args[2]);
        string output = Path.GetFullPath(args[5]);
        string version = args[6];
        if (!LotroReleaseUpdater.IsSafeReleaseTag(version) || version.Length > 64)
            throw new InvalidDataException("Invalid new root version.");
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("Output directory must be new; existing files are never overwritten.");
        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        var oldManifest = json.Deserialize<ReleaseManifest>(File.ReadAllText(args[3], Encoding.UTF8));
        if (oldManifest == null || oldManifest.patch_version == version || oldManifest.game_version != args[7]
            || oldManifest.asset_kind != SemanticPatchBuilder.PatchKind
            || (!string.IsNullOrEmpty(oldManifest.patch_mode) && oldManifest.patch_mode != SemanticPatchBuilder.FullPatchMode)
            || oldManifest.chain_depth != 0 || oldManifest.critical_review_required_count != 0
            || !SourceDigest.IsValid(oldManifest.asset_sha256)
            || new FileInfo(asset).Length != oldManifest.asset_size
            || !SourceDigest.Matches(Program.HashFile(asset), oldManifest.asset_sha256))
            throw new InvalidDataException("Anchored full asset and official version must match the supplied manifest.");
        var clock = Stopwatch.StartNew();
        Action<string> log = message => Console.WriteLine("[" + clock.Elapsed.ToString(@"hh\:mm\:ss") + "] " + message);
        log("Loading anchored semantic asset...");
        var patch = SemanticPatchSerializer.Deserialize(File.ReadAllText(asset, Encoding.UTF8));
        if ((!string.IsNullOrEmpty(patch.patch_mode) && patch.patch_mode != SemanticPatchBuilder.FullPatchMode)
            || patch.patch_version != oldManifest.patch_version || patch.source_dat_size != oldManifest.source_dat_size
            || !SourceDigest.Matches(patch.source_dat_sha256, oldManifest.source_dat_sha256)
            || !SourceDigest.Matches(patch.source_catalog_sha256, oldManifest.source_catalog_sha256)
            || patch.counts.critical_review_required_count != 0 || patch.counts.ambiguous_count != 0)
            throw new InvalidDataException("Full semantic baseline or quality gate mismatch.");

        // Keep an OS read lock for the entire build, including correction reads.
        // Another process cannot change the source between hash and copy.
        using (var sourceLock = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (sourceLock.Length != patch.source_dat_size || !SourceDigest.Matches(Program.HashFile(source), patch.source_dat_sha256))
                throw new InvalidDataException("Clean source does not match the independently anchored baseline.");
            if (args[4] != "-") ApplyDecisions(source, patch, Program.LoadDecisions(args[4]), version);
            patch.patch_version = version;
            patch.patch_mode = SemanticPatchBuilder.FullPatchMode;
            patch.patch_generator_version = "semantic-generator-v3-verified-root";
            patch.translation_catalog_version += "+reviewed-root-" + version;
            patch.entries = patch.entries.OrderBy(entry => entry.entry_identity, StringComparer.Ordinal)
                .ThenBy(entry => entry.dat_key, StringComparer.Ordinal).ToList();
            patch.counts.safe_translated_count = patch.entries.Count;
            SemanticPatchValidator.EnsureValid(patch);
            Directory.CreateDirectory(output);
            string candidatePath = Path.Combine(output, "private-candidate.dat");
            // This is a producer, not an installer bypass. No manifest is issued
            // until the full candidate, every target and source preservation pass.
            var candidate = ManagedSemanticDatPatcher.BuildCandidate(source, candidatePath, patch, null, CancellationToken.None, log);
            if (!SourceDigest.Matches(Program.HashFile(source), patch.source_dat_sha256))
                throw new InvalidDataException("Source changed during generation; no manifest issued.");
            string assetName = "lotro-turkce-yama-" + version + ".semantic.json";
            string assetPath = Path.Combine(output, assetName);
            File.WriteAllText(assetPath, SemanticPatchSerializer.Serialize(patch), new UTF8Encoding(false));
            var template = new ReleaseManifest
            {
                schema_version = 1, patch_version = version, patch_mode = SemanticPatchBuilder.FullPatchMode,
                release_tag = "v" + version, release_id = 0, asset_id = 0,
                asset_name = assetName, asset_size = new FileInfo(assetPath).Length, asset_sha256 = Program.HashFile(assetPath),
                asset_kind = SemanticPatchBuilder.PatchKind, game_version = args[7],
                source_dat_sha256 = patch.source_dat_sha256, source_dat_size = patch.source_dat_size,
                source_catalog_sha256 = patch.source_catalog_sha256,
                candidate_dat_sha256 = candidate.DatSha256, candidate_dat_size = candidate.DatSize,
                candidate_catalog_sha256 = candidate.CatalogSha256,
                minimum_updater_version = LotroReleaseUpdater.CurrentUpdaterVersion,
                chain_depth = 0, safe_translated_count = patch.entries.Count,
                skipped_changed_count = patch.counts.skipped_changed_count, critical_review_required_count = 0,
                translation_catalog_version = patch.translation_catalog_version,
                patch_generator_version = patch.patch_generator_version,
                translation_provider = patch.translation_provider, translation_model_version = patch.translation_model_version
            };
            File.WriteAllText(Path.Combine(output, "manifest-template.json"), json.Serialize(template), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "verification.json"), json.Serialize(new
            {
                status = "VERIFIED_ROOT", source_unchanged = true, applied = candidate.Applied,
                touched_dids = candidate.TouchedDids, records = candidate.RecordCount,
                candidate_sha256 = candidate.DatSha256, candidate_catalog_sha256 = candidate.CatalogSha256,
                candidate_size = candidate.DatSize, elapsed_seconds = clock.Elapsed.TotalSeconds,
                published = false, in_game_verified = false
            }), new UTF8Encoding(false));
            log("VERIFIED_ROOT_PASS|entries=" + patch.entries.Count + "|candidate=" + candidate.DatSha256
                + "|catalog=" + candidate.CatalogSha256 + "|manifest=template-only");
        }
        return 0;
    }

    private static void ApplyDecisions(string source, SemanticPatchDocument patch,
        Dictionary<string, Program.ManualDecision> decisions, string version)
    {
        var wanted = new HashSet<int>();
        foreach (string key in decisions.Keys)
        {
            int separator = key.IndexOf(':');
            if (separator != 8) throw new InvalidDataException("Invalid correction key.");
            wanted.Add(unchecked((int)Convert.ToUInt32(key.Substring(0, separator), 16)));
        }
        var records = new Dictionary<string, CatalogRecord>(StringComparer.Ordinal);
        using (var dat = new TurbineDat())
        {
            dat.Open(source, false);
            dat.BuildEntryIndex();
            foreach (int did in wanted)
            {
                if (!dat.TryGetEntry(did, out DatEntry entry)) throw new InvalidDataException("Correction DID missing.");
                long position = 0;
                var bin = LocBin.Parse(TurbineDat.MaybeDecompress(dat.ReadRaw(entry)), did);
                foreach (var record in bin.GetCatalogRecords(did, ref position)) records.Add(record.Key, record);
            }
        }
        var entries = patch.entries.ToDictionary(entry => entry.dat_key, StringComparer.Ordinal);
        foreach (var pair in decisions)
        {
            if (!records.TryGetValue(pair.Key, out CatalogRecord record) || Program.IsExcluded(record))
                throw new InvalidDataException("Correction missing or structurally excluded: " + pair.Key);
            var decision = pair.Value;
            if (decision.action == "preserve") { entries.Remove(pair.Key); continue; }
            if (!SourceDigest.Matches(decision.source_digest, record.SourceDigest)
                || !SourceDigest.Matches(decision.token_signature, record.TokenSignature)
                || !ProtectedFormat.HasSameProtectedTokens(record.Source, decision.target))
                throw new InvalidDataException("Correction source or protected format mismatch: " + pair.Key);
            entries[pair.Key] = new SemanticPatchEntry
            {
                dat_key = record.Key, entry_identity = record.EntryIdentity, did = record.Did,
                record_index = record.RecordIndex, group_index = record.GroupIndex, index_in_group = record.IndexInGroup,
                source_digest = record.SourceDigest, token_signature = record.TokenSignature, target = decision.target,
                critical_ui = record.CriticalUi, classification = "NEW", translation_status = TranslationStatuses.HumanApproved,
                translation_engine = "human", translation_engine_version = "reviewed-" + version
            };
        }
        patch.entries = entries.Values.ToList();
    }
}
