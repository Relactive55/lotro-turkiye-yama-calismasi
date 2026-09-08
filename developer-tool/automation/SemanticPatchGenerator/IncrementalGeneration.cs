using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using LotroTrGemini;
using LotroTurkceYama.Setup;

internal static class IncrementalGeneration
{
    internal static int Run(string[] args)
    {
        if (args.Length != 6)
        {
            Console.Error.WriteLine("Usage: SemanticPatchGenerator --incremental <predecessor.dat> <predecessor-manifest.json> <corrections.jsonl> <new-output-directory> <patch-version>");
            return 2;
        }
        string predecessorPath = Path.GetFullPath(args[1]);
        string manifestPath = Path.GetFullPath(args[2]);
        string correctionPath = Path.GetFullPath(args[3]);
        string outputDirectory = Path.GetFullPath(args[4]);
        string patchVersion = args[5];
        Version minimumUpdater = new Version(LotroReleaseUpdater.CurrentUpdaterVersion);
        if (!LotroReleaseUpdater.IsSafeReleaseTag(patchVersion) || patchVersion.Length > 64)
            throw new InvalidDataException("Patch version is not a safe release identifier.");
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory))
            throw new IOException("Output directory must not already exist; no existing candidate or input is overwritten.");
        if (!File.Exists(predecessorPath) || !File.Exists(manifestPath) || !File.Exists(correctionPath))
            throw new FileNotFoundException("Predecessor DAT, manifest or correction pool is missing.");

        JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        ReleaseManifest predecessor = json.Deserialize<ReleaseManifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
        ValidatePredecessor(predecessor, patchVersion);
        Version predecessorMinimum;
        if (Version.TryParse(predecessor.minimum_updater_version, out predecessorMinimum)
            && minimumUpdater < predecessorMinimum)
            throw new InvalidDataException("Minimum updater version cannot be reduced below the predecessor requirement.");
        if (new FileInfo(predecessorPath).Length != predecessor.candidate_dat_size
            || !SourceDigest.Matches(Program.HashFile(predecessorPath), predecessor.candidate_dat_sha256))
            throw new InvalidDataException("Predecessor DAT does not match the manifest candidate hash and size.");

        Console.WriteLine("Predecessor catalog is being verified...");
        List<CatalogRecord> records = Program.ExtractCatalog(predecessorPath);
        if (!SourceDigest.Matches(CatalogIdentity.ComputeCatalogHash(records), predecessor.candidate_catalog_sha256))
            throw new InvalidDataException("Predecessor catalog does not match the manifest.");
        SemanticPatchDocument patch = BuildDocument(predecessor, records,
            Program.LoadCandidates(correctionPath).Values, patchVersion, Path.GetFileNameWithoutExtension(correctionPath));

        Directory.CreateDirectory(outputDirectory);
        string candidatePath = Path.Combine(outputDirectory, "private-candidate.dat");
        ManagedSemanticDatPatcher.Result candidate = ManagedSemanticDatPatcher.BuildIncrementalCandidate(
            predecessorPath, candidatePath, patch, null, CancellationToken.None, Console.WriteLine);
        string assetName = "lotro-turkce-yama-" + patchVersion + ".semantic.json.gz";
        string assetPath = Path.Combine(outputDirectory, assetName);
        SemanticPatchSerializer.WriteFile(assetPath, patch, true);
        ReleaseManifest template = BuildManifestTemplate(predecessor, patch, candidate, assetName,
            new FileInfo(assetPath).Length, Program.HashFile(assetPath), minimumUpdater.ToString());
        File.WriteAllText(Path.Combine(outputDirectory, "manifest-template.json"), json.Serialize(template), new UTF8Encoding(false));
        Console.WriteLine("INCREMENTAL_PATCH_GENERATED|entries=" + patch.entries.Count
            + "|bytes=" + template.asset_size + "|sha256=" + template.asset_sha256
            + "|candidate_sha256=" + candidate.DatSha256
            + "|manifest=template-only; release_id and asset_id must be assigned after upload");
        return 0;
    }

    internal static void ValidatePredecessor(ReleaseManifest predecessor, string patchVersion)
    {
        if (predecessor == null || predecessor.schema_version != 1
            || predecessor.asset_kind != SemanticPatchBuilder.PatchKind
            || predecessor.release_id < 1 || predecessor.asset_id < 1 || predecessor.asset_size < 1
            || !LotroReleaseUpdater.IsSafeReleaseTag(predecessor.release_tag)
            || !LotroReleaseUpdater.IsSafeFileName(predecessor.asset_name)
            || !predecessor.asset_name.StartsWith(LotroReleaseUpdater.PatchAssetPrefix, StringComparison.Ordinal)
            || !SourceDigest.IsValid(predecessor.asset_sha256)
            || !SourceDigest.IsValid(predecessor.source_dat_sha256) || predecessor.source_dat_size < 1
            || !SourceDigest.IsValid(predecessor.source_catalog_sha256)
            || !SourceDigest.IsValid(predecessor.candidate_dat_sha256) || predecessor.candidate_dat_size < 1
            || !SourceDigest.IsValid(predecessor.candidate_catalog_sha256)
            || string.IsNullOrWhiteSpace(predecessor.patch_version)
            || string.IsNullOrWhiteSpace(predecessor.game_version)
            || string.Equals(predecessor.patch_version, patchVersion, StringComparison.Ordinal)
            || predecessor.critical_review_required_count != 0)
            throw new InvalidDataException("A verified semantic predecessor manifest with exact candidate identities is required.");
        bool incremental = predecessor.patch_mode == SemanticPatchBuilder.IncrementalPatchMode;
        if (!Version.TryParse(predecessor.minimum_updater_version, out Version framingVersion)
            || framingVersion < new Version(1, 3, 0, 0))
            throw new InvalidDataException("Predecessor predates the native DAT framing fix; build a new clean-source root instead.");
        if ((!incremental && !string.IsNullOrEmpty(predecessor.patch_mode) && predecessor.patch_mode != SemanticPatchBuilder.FullPatchMode)
            || (incremental && (predecessor.chain_depth < 1 || predecessor.chain_depth >= 32))
            || (!incremental && predecessor.chain_depth != 0))
            throw new InvalidDataException("Predecessor chain depth or mode is invalid; create a new root package at the chain limit.");
        // Reuse runtime manifest validation for predecessor pointers and quality
        // fields. The supplied metadata is local input; this is not a network
        // authentication check of a GitHub release.
        ManifestValidator.Validate(predecessor,
            new StableRelease { id = predecessor.release_id, tag_name = predecessor.release_tag },
            new ReleaseAsset { id = predecessor.asset_id == 1 ? 2 : 1, name = LotroReleaseUpdater.ManifestAssetName });
    }

    internal static SemanticPatchDocument BuildDocument(ReleaseManifest predecessor, IList<CatalogRecord> records,
        IEnumerable<TranslationCandidate> corrections, string patchVersion, string catalogVersion)
    {
        ValidatePredecessor(predecessor, patchVersion);
        Dictionary<string, CatalogRecord> byKey = records.ToDictionary(record => record.Key, StringComparer.Ordinal);
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        List<TranslationCandidate> accepted = new List<TranslationCandidate>();
        List<CatalogDiffRecord> selected = new List<CatalogDiffRecord>();
        foreach (TranslationCandidate correction in corrections ?? Enumerable.Empty<TranslationCandidate>())
        {
            CatalogRecord record;
            if (correction == null || string.IsNullOrWhiteSpace(correction.dat_key)
                || !seen.Add(correction.dat_key) || !byKey.TryGetValue(correction.dat_key, out record))
                throw new InvalidDataException("Correction keys must be unique and present in the verified predecessor catalog.");
            if (Program.IsExcluded(record)) throw new InvalidDataException("Correction targets a protected record: " + record.Key);
            if (correction.translation_status != TranslationStatuses.HumanApproved
                || !SourceDigest.Matches(record.SourceDigest, correction.source_digest)
                || !SourceDigest.Matches(record.TokenSignature, correction.token_signature))
                throw new InvalidDataException("Correction must be human approved and bound to the exact predecessor text: " + record.Key);
            if (string.Equals(record.Source, correction.target, StringComparison.Ordinal)) continue;
            string rejection = Program.Validate(record, correction);
            if (rejection != null) throw new InvalidDataException("Unsafe correction " + record.Key + ": " + rejection);
            accepted.Add(correction);
            selected.Add(new CatalogDiffRecord { Classification = DiffClassification.UNCHANGED, OldRecord = record, NewRecord = record });
        }
        if (accepted.Count == 0) throw new InvalidDataException("No changed, approved corrections were supplied.");
        SemanticPatchDocument patch = SemanticPatchBuilder.BuildIncremental(patchVersion,
            predecessor.source_dat_sha256, predecessor.source_dat_size, predecessor.source_catalog_sha256,
            predecessor.patch_version, predecessor.candidate_dat_sha256, predecessor.candidate_dat_size,
            predecessor.candidate_catalog_sha256, selected, accepted, catalogVersion,
            "semantic-generator-v2", "human-corrections", "human-reviewed", string.Empty);
        if (patch.entries.Count != accepted.Count || patch.counts.critical_review_required_count != 0
            || patch.counts.review_required_count != 0 || patch.counts.ambiguous_count != 0)
            throw new InvalidDataException("Every correction must pass the semantic builder without omissions.");
        return patch;
    }

    internal static ReleaseManifest BuildManifestTemplate(ReleaseManifest predecessor, SemanticPatchDocument patch,
        ManagedSemanticDatPatcher.Result candidate, string assetName, long assetSize, string assetHash, string minimumUpdater)
    {
        return new ReleaseManifest
        {
            schema_version = 1, patch_version = patch.patch_version, patch_mode = SemanticPatchBuilder.IncrementalPatchMode,
            release_tag = "patch-" + patch.patch_version, release_id = 0, asset_id = 0,
            asset_name = assetName, asset_size = assetSize, asset_sha256 = assetHash,
            source_dat_sha256 = predecessor.source_dat_sha256, source_dat_size = predecessor.source_dat_size,
            source_catalog_sha256 = predecessor.source_catalog_sha256, game_version = predecessor.game_version,
            candidate_catalog_sha256 = candidate.CatalogSha256, candidate_dat_sha256 = candidate.DatSha256,
            candidate_dat_size = candidate.DatSize, asset_kind = SemanticPatchBuilder.PatchKind,
            minimum_updater_version = minimumUpdater,
            base_patch_version = predecessor.patch_version, base_release_tag = predecessor.release_tag,
            base_release_id = predecessor.release_id, base_asset_id = predecessor.asset_id,
            base_asset_name = predecessor.asset_name, base_asset_size = predecessor.asset_size,
            base_asset_sha256 = predecessor.asset_sha256,
            base_candidate_dat_sha256 = predecessor.candidate_dat_sha256,
            base_candidate_dat_size = predecessor.candidate_dat_size,
            base_candidate_catalog_sha256 = predecessor.candidate_catalog_sha256,
            chain_depth = predecessor.chain_depth + 1,
            translation_catalog_version = patch.translation_catalog_version,
            patch_generator_version = patch.patch_generator_version, translation_provider = patch.translation_provider,
            translation_model_version = patch.translation_model_version,
            safe_translated_count = patch.counts.safe_translated_count, skipped_changed_count = 0,
            critical_review_required_count = 0
        };
    }
}
