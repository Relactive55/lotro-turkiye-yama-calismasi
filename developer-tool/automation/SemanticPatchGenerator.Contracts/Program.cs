using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using LotroTrGemini;
using LotroTurkceYama.Setup;

internal static class GeneratorContractTests
{
    private static int passed;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private static readonly Type Generator = typeof(SemanticPatchDocument).Assembly.GetType("Program", true);

    private static int Main()
    {
        CatalogRecord original = Record("Defeat the Orc");
        Check(CanReuse(Record("Defeat the Orc"), original), "matching source may reuse reference");
        Check(!CanReuse(Record("Defeat the Orc Captain"), original), "changed English at same key cannot reuse reference");
        Check(!CanReuse(Record("Defeat the Orc", 1), original), "different key cannot reuse reference");
        Check(!CanReuse(Record("Defeat the Orc", 0, "changed-shape"), original), "changed shape cannot reuse reference");
        Check(!CanReuse(original, null), "missing clean reference source rejected");
        Expect<InvalidDataException>(() => Run("current.dat", "candidates.jsonl", "output.json", "test", "reference.dat"),
            "reference DAT requires paired clean source");
        Expect<InvalidDataException>(() => Run("current.dat", "candidates.jsonl", "output.json", "test", "-", "-", "-", "source.dat"),
            "reference source requires paired translated DAT");

        string temporary = Path.Combine(Path.GetTempPath(), "lotro-generator-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            VerifyReferenceCommand(temporary);
            VerifyIncrementalCommand(temporary);
            VerifyRootCommand(temporary);
        }
        finally
        {
            string resolved = Path.GetFullPath(temporary);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("lotro-generator-contract-", StringComparison.Ordinal))
                throw new IOException("Refusing to delete an unexpected fixture directory.");
            Directory.Delete(resolved, true);
        }
        Console.WriteLine("SEMANTIC_GENERATOR_CONTRACT_PASS|checks=" + passed);
        return 0;
    }

    private static void VerifyReferenceCommand(string temporary)
    {
        const int did = 0x25000020;
        string oldClean = Path.Combine(temporary, "old-clean.dat");
        string newClean = Path.Combine(temporary, "new-clean.dat");
        string translated = Path.Combine(temporary, "old-translated.dat");
        string pool = Path.Combine(temporary, "empty-candidates.jsonl");
        File.WriteAllBytes(oldClean, SyntheticDat("Open", did));
        File.WriteAllBytes(newClean, SyntheticDat("Close", did));
        File.WriteAllBytes(translated, SyntheticDat("Ac", did));
        File.WriteAllText(pool, string.Empty);
        string staleOutput = Path.Combine(temporary, "stale-reference-patch.json");
        Check(Run(newClean, pool, staleOutput, "new-source", translated, "-", "-", oldClean) == 0
            && SemanticPatchSerializer.Deserialize(File.ReadAllText(staleOutput)).entries.Count == 0,
            "full generator keeps changed English instead of approving old reference meaning");
        string matchingOutput = Path.Combine(temporary, "matching-reference-patch.json");
        Check(Run(oldClean, pool, matchingOutput, "same-source", translated, "-", "-", oldClean) == 0
            && SemanticPatchSerializer.Deserialize(File.ReadAllText(matchingOutput)).entries[0].target == "Ac",
            "full generator retains same-baseline reference translation");
        CatalogRecord sourceRecord;
        using (TurbineDat dat = new TurbineDat())
        {
            dat.Open(oldClean, false);
            long position = 0;
            sourceRecord = LocBin.Parse(dat.ReadRaw(dat.ListLocalization()[0]), did).GetCatalogRecords(did, ref position)[0];
        }
        string decisions = Path.Combine(temporary, "manual-decisions.jsonl");
        string manualOutput = Path.Combine(temporary, "manual-output.json");
        File.WriteAllText(decisions, Json.Serialize(new { dat_key = sourceRecord.Key, action = "translate", target = "Ac" }));
        Expect<InvalidDataException>(() => Run(oldClean, pool, manualOutput, "manual", "-", "-", decisions),
            "manual translation without source and token proof is rejected");
        File.WriteAllText(decisions, Json.Serialize(new { dat_key = sourceRecord.Key, action = "translate", target = "Ac",
            source_digest = sourceRecord.SourceDigest, token_signature = sourceRecord.TokenSignature }));
        Expect<InvalidDataException>(() => Run(newClean, pool, manualOutput, "manual", "-", "-", decisions),
            "manual decision cannot approve changed English at reused key");
        Check(!File.Exists(manualOutput), "rejected manual decision writes no patch");
        Check(Run(oldClean, pool, manualOutput, "manual", "-", "-", decisions) == 0
            && SemanticPatchSerializer.Deserialize(File.ReadAllText(manualOutput)).entries[0].target == "Ac",
            "source-bound manual decision remains supported");
    }

    private static void VerifyIncrementalCommand(string temporary)
    {
        const int did = 0x25000020;
        string predecessorPath = Path.Combine(temporary, "predecessor.dat");
        File.WriteAllBytes(predecessorPath, SyntheticDat("Open", did));
        CatalogRecord record;
        string catalogHash;
        using (TurbineDat dat = new TurbineDat())
        {
            dat.Open(predecessorPath, false);
            DatEntry entry = dat.ListLocalization()[0];
            long position = 0;
            var records = LocBin.Parse(dat.ReadRaw(entry), did).GetCatalogRecords(did, ref position);
            record = records[0];
            catalogHash = CatalogIdentity.ComputeCatalogHash(records);
        }
        string predecessorHash = Hash(predecessorPath);
        ReleaseManifest predecessor = new ReleaseManifest
        {
            schema_version = 1, patch_version = "root-1", patch_mode = "full",
            release_id = 100, release_tag = "patch-root-1", asset_id = 101,
            asset_name = "lotro-turkce-yama-root-1.semantic.json", asset_size = 42,
            asset_sha256 = CatalogIdentity.Sha256Hex("root semantic asset"),
            source_dat_sha256 = CatalogIdentity.Sha256Hex("original clean baseline"), source_dat_size = 3000,
            source_catalog_sha256 = CatalogIdentity.Sha256Hex("original clean catalog"),
            candidate_dat_sha256 = predecessorHash, candidate_dat_size = new FileInfo(predecessorPath).Length,
            candidate_catalog_sha256 = catalogHash, asset_kind = SemanticPatchBuilder.PatchKind,
            game_version = "1.2.3.4", minimum_updater_version = LotroReleaseUpdater.CurrentUpdaterVersion,
            safe_translated_count = 1
        };
        string manifestPath = Path.Combine(temporary, "predecessor-manifest.json");
        File.WriteAllText(manifestPath, Json.Serialize(predecessor), new UTF8Encoding(false));
        TranslationCandidate correction = new TranslationCandidate
        {
            entry_identity = record.EntryIdentity, dat_key = record.Key, source_digest = record.SourceDigest,
            token_signature = record.TokenSignature, target = "Ac", translation_status = TranslationStatuses.HumanApproved,
            translation_engine = "human", translation_engine_version = "contract"
        };
        string correctionPath = Path.Combine(temporary, "corrections.jsonl");
        File.WriteAllText(correctionPath, Json.Serialize(correction) + "\n", new UTF8Encoding(false));
        string output = Path.Combine(temporary, "delta-output");
        Check(Run("--incremental", predecessorPath, manifestPath, correctionPath, output, "correction-1") == 0,
            "delta command builds verified private candidate");
        ReleaseManifest template = Json.Deserialize<ReleaseManifest>(File.ReadAllText(Path.Combine(output, "manifest-template.json")));
        string patchPath = Path.Combine(output, template.asset_name);
        SemanticPatchDocument patch = SemanticPatchSerializer.ReadFile(patchPath);
        Check(patch.entries.Count == 1 && patch.patch_mode == "incremental"
            && patch.entries[0].source_digest == record.SourceDigest, "delta binds predecessor stored text");
        Check(patch.source_dat_sha256 == predecessor.source_dat_sha256
            && patch.source_catalog_sha256 == predecessor.source_catalog_sha256
            && patch.base_candidate_dat_sha256 == predecessorHash, "delta retains official baseline and exact predecessor");
        Check(template.chain_depth == 1 && template.base_release_id == 100 && template.base_asset_id == 101
            && template.base_asset_sha256 == predecessor.asset_sha256, "template preserves predecessor release pointer");
        Check(template.candidate_dat_sha256 == Hash(Path.Combine(output, "private-candidate.dat"))
            && template.asset_sha256 == Hash(patchPath)
            && template.asset_size == new FileInfo(patchPath).Length, "template records actual candidate and asset hashes");
        Check(template.release_id == 0 && template.asset_id == 0, "template cannot impersonate an uploaded release");
        Expect<UpdaterFailure>(() => ManifestValidator.Validate(template,
            new StableRelease { id = 0, tag_name = template.release_tag }, new ReleaseAsset { id = -1 }),
            "installer rejects unfinished template");
        template.release_id = 200;
        template.asset_id = 201;
        ManifestValidator.Validate(template,
            new StableRelease { id = template.release_id, tag_name = template.release_tag }, new ReleaseAsset { id = 202 });
        Check(true, "template becomes valid after binding actual release and asset identifiers");
        Check(Hash(predecessorPath) == predecessorHash, "source DAT remains unchanged");
        string repeatedOutput = Path.Combine(temporary, "repeated-delta-output");
        Run("--incremental", predecessorPath, manifestPath, correctionPath, repeatedOutput, "correction-1");
        Check(Hash(Path.Combine(repeatedOutput, template.asset_name)) == template.asset_sha256
            && Hash(Path.Combine(repeatedOutput, "private-candidate.dat")) == template.candidate_dat_sha256,
            "same verified inputs produce identical delta and candidate bytes");
        Expect<IOException>(() => Run("--incremental", predecessorPath, manifestPath, correctionPath, output, "correction-2"),
            "existing output directory is never overwritten");

        correction.source_digest = CatalogIdentity.Sha256Hex("old source text");
        File.WriteAllText(correctionPath, Json.Serialize(correction), new UTF8Encoding(false));
        string rejectedOutput = Path.Combine(temporary, "rejected-output");
        Expect<InvalidDataException>(() => Run("--incremental", predecessorPath, manifestPath, correctionPath, rejectedOutput, "correction-2"),
            "old source digest cannot be restamped by delta command");
        Check(!Directory.Exists(rejectedOutput), "rejected correction produces no output");
        correction.source_digest = record.SourceDigest;
        correction.translation_status = TranslationStatuses.MachineTranslated;
        File.WriteAllText(correctionPath, Json.Serialize(correction), new UTF8Encoding(false));
        Expect<InvalidDataException>(() => Run("--incremental", predecessorPath, manifestPath, correctionPath, rejectedOutput, "correction-2"),
            "unapproved correction is rejected");

        predecessor.candidate_dat_sha256 = null;
        File.WriteAllText(manifestPath, Json.Serialize(predecessor), new UTF8Encoding(false));
        Expect<InvalidDataException>(() => Run("--incremental", predecessorPath, manifestPath, correctionPath, rejectedOutput, "correction-2"),
            "legacy missing predecessor DAT identity is rejected");
        predecessor.candidate_dat_sha256 = predecessorHash;
        correction.translation_status = TranslationStatuses.HumanApproved;
        File.WriteAllText(correctionPath, Json.Serialize(correction), new UTF8Encoding(false));
        predecessor.minimum_updater_version = "1.2.0.0";
        File.WriteAllText(manifestPath, Json.Serialize(predecessor), new UTF8Encoding(false));
        Expect<InvalidDataException>(() => Run("--incremental", predecessorPath, manifestPath, correctionPath, rejectedOutput, "correction-2"),
            "exact hashes cannot rehabilitate a predecessor made before the native framing fix");
        Check(!Directory.Exists(rejectedOutput), "obsolete framing predecessor produces no incremental package");
        predecessor.minimum_updater_version = LotroReleaseUpdater.CurrentUpdaterVersion;
        predecessor.patch_mode = "incremental";
        predecessor.chain_depth = 32;
        File.WriteAllText(manifestPath, Json.Serialize(predecessor), new UTF8Encoding(false));
        Expect<InvalidDataException>(() => Run("--incremental", predecessorPath, manifestPath, correctionPath, rejectedOutput, "correction-2"),
            "chain limit requires a new root");
    }

    private static void VerifyRootCommand(string temporary)
    {
        string source = Path.Combine(temporary, "old-clean.dat");
        string asset = Path.Combine(temporary, "manual-output.json");
        var patch = SemanticPatchSerializer.Deserialize(File.ReadAllText(asset));
        var legacy = new ReleaseManifest
        {
            schema_version = 1, patch_version = patch.patch_version, patch_mode = "full",
            asset_kind = SemanticPatchBuilder.PatchKind, asset_sha256 = Hash(asset), asset_size = new FileInfo(asset).Length,
            source_dat_sha256 = patch.source_dat_sha256, source_dat_size = patch.source_dat_size,
            source_catalog_sha256 = patch.source_catalog_sha256, game_version = "1.2.3.4",
            candidate_catalog_sha256 = CatalogIdentity.Sha256Hex("deliberately wrong legacy projection")
        };
        string manifest = Path.Combine(temporary, "legacy-root.json");
        File.WriteAllText(manifest, Json.Serialize(legacy));
        string output = Path.Combine(temporary, "verified-root");
        string sourceHash = Hash(source);
        Check(Run("--verified-root", source, asset, manifest, "-", output, "root-2", "1.2.3.4") == 0,
            "legacy missing binary identity is replaced only by clean-source rebuild");
        var template = Json.Deserialize<ReleaseManifest>(File.ReadAllText(Path.Combine(output, "manifest-template.json")));
        Check(template.candidate_dat_sha256 == Hash(Path.Combine(output, "private-candidate.dat"))
            && template.candidate_catalog_sha256 != legacy.candidate_catalog_sha256,
            "verified root identities come from actual result, never legacy projection");
        Check(template.chain_depth == 0 && template.base_release_id == 0 && template.base_candidate_dat_sha256 == null,
            "new root carries no unverified predecessor pointer");
        Check(template.release_id == 0 && template.asset_id == 0 && Hash(source) == sourceHash,
            "verified root preserves source and requires publication binding");
        Expect<IOException>(() => Run("--verified-root", source, asset, manifest, "-", output, "root-3", "1.2.3.4"),
            "root generation never overwrites an existing output");
        string rejected = Path.Combine(temporary, "rejected-root");
        Expect<InvalidDataException>(() => Run("--repair-native-framing", source, asset, manifest, "-", rejected, "root-3", "1.2.3.4"),
            "native migration rejects a source without the modern format signature");
        Check(!Directory.Exists(rejected) && Hash(source) == sourceHash,
            "rejected framing migration neither mutates source nor creates a package");
        Expect<InvalidDataException>(() => Run("--verified-root", source, asset, manifest, "-", rejected, "root-3", "1.2.3.5"),
            "old clean source cannot masquerade as a newly detected official version");
        legacy.asset_sha256 = CatalogIdentity.Sha256Hex("tampered asset");
        File.WriteAllText(manifest, Json.Serialize(legacy));
        Expect<InvalidDataException>(() => Run("--verified-root", source, asset, manifest, "-", rejected, "root-3", "1.2.3.4"),
            "root rejects semantic asset identity mismatch");
        Check(!Directory.Exists(rejected), "rejected root has no output or publication template");
    }

    private static object Invoke(string name, params object[] arguments)
    {
        try { return Generator.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    private static int Run(params string[] args) { return (int)Invoke("Main", new object[] { args }); }
    private static bool CanReuse(CatalogRecord current, CatalogRecord old) { return (bool)Invoke("CanReuseReference", current, old); }
    private static CatalogRecord Record(string source, int index = 0, string shape = "same-shape")
    {
        return CatalogIdentity.FromLocRow(new LocRow { Did = 0x25000020, RecordIndex = index, Original = source },
            "same-record", shape, "same-context", 0);
    }
    private static string Hash(string path)
    {
        using (FileStream input = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", string.Empty).ToLowerInvariant();
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        passed++;
        Console.WriteLine("PASS " + name);
    }
    private static void Expect<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, name); return; }
        throw new Exception("Expected " + typeof(T).Name + ": " + name);
    }

    private static byte[] SyntheticDat(string text, int did)
    {
        byte[] payload;
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode, true))
        {
            writer.Write(0); writer.Write(did); writer.Write(1); writer.Write((byte)1);
            writer.Write((long)123456789); writer.Write(1); writer.Write((byte)text.Length);
            writer.Write(Encoding.Unicode.GetBytes(text)); writer.Write(0); writer.Write((byte)0);
            writer.Flush(); payload = stream.ToArray();
        }
        byte[] result = new byte[3072];
        using (MemoryStream stream = new MemoryStream(result))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            stream.Position = 320;
            writer.Write(TurbineDat.MagicBt); writer.Write(1024u); writer.Write((uint)result.Length);
            for (int i = 0; i < 5; i++) writer.Write(0u);
            writer.Write(1024u);
            stream.Position = 1024 + 504;
            writer.Write(1u); writer.Write(0u); writer.Write(unchecked((uint)did)); writer.Write(2048u);
            writer.Write((uint)payload.Length); writer.Write(0u); writer.Write(1u);
            writer.Write((uint)payload.Length); writer.Write(0u);
            stream.Position = 2048; writer.Write(0u); writer.Write(payload);
        }
        return result;
    }
}
