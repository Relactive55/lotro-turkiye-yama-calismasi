using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using LotroTrGemini;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 4 || args.Length > 7)
        {
            Console.Error.WriteLine("Usage: SemanticPatchGenerator <source.dat> <candidates.jsonl> <output.json> <patch-version> [verified-reference.dat] [private-review.jsonl] [manual-decisions.jsonl]");
            return 2;
        }
        string sourcePath = Path.GetFullPath(args[0]);
        string candidatePath = Path.GetFullPath(args[1]);
        string outputPath = Path.GetFullPath(args[2]);
        string patchVersion = args[3];
        string referencePath = args.Length >= 5 ? Path.GetFullPath(args[4]) : null;
        string reviewPath = args.Length >= 6 ? Path.GetFullPath(args[5]) : null;
        string decisionsPath = args.Length == 7 ? Path.GetFullPath(args[6]) : null;
        if (!File.Exists(sourcePath) || !File.Exists(candidatePath)) throw new FileNotFoundException("Source DAT or candidate pool is missing.");
        if (referencePath != null && !File.Exists(referencePath)) throw new FileNotFoundException("Verified reference DAT is missing.", referencePath);

        Console.WriteLine("Aday havuzu okunuyor...");
        Dictionary<string, TranslationCandidate> candidates = LoadCandidates(candidatePath);
        Console.WriteLine("Aday sayısı: " + candidates.Count);
        Dictionary<string, ManualDecision> decisions = decisionsPath == null
            ? new Dictionary<string, ManualDecision>(StringComparer.Ordinal)
            : LoadDecisions(decisionsPath);
        HashSet<string> usedDecisions = new HashSet<string>(StringComparer.Ordinal);

        Console.WriteLine("Resmi DAT kataloğu çıkarılıyor...");
        List<CatalogRecord> records = ExtractCatalog(sourcePath);
        string sourceCatalogHash = CatalogIdentity.ComputeCatalogHash(records);
        Console.WriteLine("Kaynak katalog: " + records.Count + " kayıt, SHA-256 " + sourceCatalogHash);
        Dictionary<string, string> referenceTargets = referencePath == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : ExtractTargets(referencePath);
        if (referencePath != null) Console.WriteLine("Doğrulanmış referans satırı: " + referenceTargets.Count);

        SemanticPatchDocument document = new SemanticPatchDocument
        {
            schema_version = 1,
            patch_kind = SemanticPatchBuilder.PatchKind,
            patch_version = patchVersion,
            source_dat_sha256 = HashFile(sourcePath),
            source_dat_size = new FileInfo(sourcePath).Length,
            source_catalog_sha256 = sourceCatalogHash,
            translation_catalog_version = Path.GetFileNameWithoutExtension(candidatePath),
            patch_generator_version = "semantic-generator-v1",
            translation_provider = "curated-pool",
            translation_model_version = "mixed-audited",
            translation_model_sha256 = string.Empty,
            counts = new SemanticPatchCounts(),
            entries = new List<SemanticPatchEntry>(candidates.Count)
        };

        int rejected = 0;
        int unmatchedCritical = 0;
        List<ReviewRow> rejectedReview = new List<ReviewRow>();
        foreach (CatalogRecord record in records)
        {
            if (IsExcluded(record)) continue;
            decisions.TryGetValue(record.Key, out ManualDecision decision);
            if (decision != null)
            {
                usedDecisions.Add(record.Key);
                if (string.Equals(decision.action, "preserve", StringComparison.Ordinal)) continue;
            }
            candidates.TryGetValue(record.Key, out TranslationCandidate candidate);
            if (referenceTargets.TryGetValue(record.Key, out string referenceTarget)
                && !string.Equals(referenceTarget, record.Source, StringComparison.Ordinal))
            {
                if (candidate == null || !string.Equals(candidate.target, referenceTarget, StringComparison.Ordinal))
                {
                    candidate = new TranslationCandidate
                    {
                        entry_identity = record.EntryIdentity,
                        dat_key = record.Key,
                        source_digest = record.SourceDigest,
                        token_signature = record.TokenSignature,
                        target = referenceTarget,
                        translation_status = TranslationStatuses.HumanApproved,
                        translation_engine = "verified-reference",
                        translation_engine_version = patchVersion,
                        critical_ui = record.CriticalUi
                    };
                }
                else if (record.CriticalUi)
                {
                    candidate.translation_status = TranslationStatuses.HumanApproved;
                    candidate.translation_engine = "verified-reference";
                    candidate.translation_engine_version = patchVersion;
                }
            }
            if (decision != null && string.Equals(decision.action, "translate", StringComparison.Ordinal))
            {
                candidate = new TranslationCandidate
                {
                    entry_identity = record.EntryIdentity,
                    dat_key = record.Key,
                    source_digest = record.SourceDigest,
                    token_signature = record.TokenSignature,
                    target = decision.target,
                    translation_status = TranslationStatuses.HumanApproved,
                    translation_engine = "manual-critical-review",
                    translation_engine_version = patchVersion,
                    critical_ui = record.CriticalUi
                };
            }
            if (candidate == null)
            {
                if (record.CriticalUi)
                {
                    unmatchedCritical++;
                    rejectedReview.Add(ReviewRow.From(record, null, "missing"));
                }
                continue;
            }
            string rejection = Validate(record, candidate);
            if (rejection != null)
            {
                rejected++;
                rejectedReview.Add(ReviewRow.From(record, candidate, rejection));
                if (record.CriticalUi)
                {
                    unmatchedCritical++;
                }
                continue;
            }
            document.entries.Add(new SemanticPatchEntry
            {
                entry_identity = record.EntryIdentity,
                dat_key = record.Key,
                did = record.Did,
                record_index = record.RecordIndex,
                group_index = record.GroupIndex,
                index_in_group = record.IndexInGroup,
                source_digest = record.SourceDigest,
                token_signature = record.TokenSignature,
                target = candidate.target,
                translation_status = candidate.translation_status,
                translation_engine = candidate.translation_engine ?? string.Empty,
                translation_engine_version = candidate.translation_engine_version ?? string.Empty,
                classification = DiffClassification.UNCHANGED.ToString(),
                critical_ui = record.CriticalUi
            });
        }
        if (usedDecisions.Count != decisions.Count)
            throw new InvalidDataException("Manual decision contains unknown DAT keys: "
                + string.Join(",", decisions.Keys.Where(key => !usedDecisions.Contains(key)).Take(20)));
        document.entries.Sort(delegate (SemanticPatchEntry left, SemanticPatchEntry right)
        {
            int result = string.CompareOrdinal(left.entry_identity, right.entry_identity);
            return result != 0 ? result : string.CompareOrdinal(left.dat_key, right.dat_key);
        });
        document.counts.safe_translated_count = document.entries.Count;
        document.counts.review_required_count = rejected;
        document.counts.critical_review_required_count = unmatchedCritical;
        SemanticPatchValidator.EnsureValid(document);

        if (reviewPath != null) WritePrivateReview(reviewPath, rejectedReview);
        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporary = outputPath + ".part";
        if (File.Exists(temporary)) File.Delete(temporary);
        File.WriteAllText(temporary, SemanticPatchSerializer.Serialize(document), new UTF8Encoding(false));
        ReplaceAtomic(temporary, outputPath);
        Console.WriteLine("SEMANTIC_PATCH_GENERATED|entries=" + document.entries.Count
            + "|rejected=" + rejected + "|critical_review=" + unmatchedCritical
            + "|review_rows=" + rejectedReview.Count
            + "|bytes=" + new FileInfo(outputPath).Length + "|sha256=" + HashFile(outputPath));
        return 0;
    }

    private static void WritePrivateReview(string path, List<ReviewRow> rows)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporary = path + ".part";
        if (File.Exists(temporary)) File.Delete(temporary);
        JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        using (StreamWriter writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
            foreach (ReviewRow row in rows) writer.WriteLine(json.Serialize(row));
        ReplaceAtomic(temporary, path);
    }

    private static void ReplaceAtomic(string temporary, string destination)
    {
        if (!File.Exists(destination))
        {
            File.Move(temporary, destination);
            return;
        }
        IOException last = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.Replace(temporary, destination, null, true);
                return;
            }
            catch (IOException ex)
            {
                last = ex;
                System.Threading.Thread.Sleep(250 * (attempt + 1));
            }
        }
        throw last ?? new IOException("Atomic output replacement failed.");
    }

    private sealed class ReviewRow
    {
        public string dat_key { get; set; }
        public string source { get; set; }
        public string candidate_target { get; set; }
        public string candidate_status { get; set; }
        public string rejection { get; set; }

        public static ReviewRow From(CatalogRecord record, TranslationCandidate candidate, string rejection)
        {
            return new ReviewRow
            {
                dat_key = record.Key,
                source = record.Source,
                candidate_target = candidate == null ? string.Empty : candidate.target,
                candidate_status = candidate == null ? string.Empty : candidate.translation_status,
                rejection = rejection
            };
        }
    }

    private sealed class ManualDecision
    {
        public string dat_key { get; set; }
        public string action { get; set; }
        public string target { get; set; }
    }

    private static Dictionary<string, TranslationCandidate> LoadCandidates(string path)
    {
        Dictionary<string, TranslationCandidate> result = new Dictionary<string, TranslationCandidate>(StringComparer.Ordinal);
        JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        int lineNumber = 0;
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            TranslationCandidate candidate;
            try { candidate = json.Deserialize<TranslationCandidate>(line); }
            catch (Exception ex) { throw new InvalidDataException("Candidate JSONL line " + lineNumber + ": " + ex.Message); }
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.dat_key))
                throw new InvalidDataException("Candidate DAT key missing at line " + lineNumber + ".");
            if (result.ContainsKey(candidate.dat_key))
                throw new InvalidDataException("Duplicate candidate DAT key: " + candidate.dat_key);
            result.Add(candidate.dat_key, candidate);
        }
        return result;
    }

    private static Dictionary<string, ManualDecision> LoadDecisions(string path)
    {
        Dictionary<string, ManualDecision> result = new Dictionary<string, ManualDecision>(StringComparer.Ordinal);
        JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        int lineNumber = 0;
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            ManualDecision decision = json.Deserialize<ManualDecision>(line);
            if (decision == null || string.IsNullOrWhiteSpace(decision.dat_key)
                || (decision.action != "preserve" && decision.action != "translate")
                || (decision.action == "translate" && string.IsNullOrWhiteSpace(decision.target)))
                throw new InvalidDataException("Invalid manual decision at line " + lineNumber + ".");
            if (result.ContainsKey(decision.dat_key))
                throw new InvalidDataException("Duplicate manual decision DAT key: " + decision.dat_key);
            result.Add(decision.dat_key, decision);
        }
        return result;
    }

    private static List<CatalogRecord> ExtractCatalog(string path)
    {
        List<CatalogRecord> records = new List<CatalogRecord>(850000);
        long position = 0;
        using (TurbineDat dat = new TurbineDat())
        {
            dat.Open(path, false);
            foreach (DatEntry entry in dat.ListLocalization())
            {
                byte[] raw = dat.ReadRaw(entry);
                byte[] payload = TurbineDat.MaybeDecompress(raw);
                if (TurbineDat.LooksCompressed(raw) && ReferenceEquals(raw, payload))
                    throw new InvalidDataException("Compressed localization entry could not be read: 0x" + entry.Id.ToString("X8"));
                records.AddRange(LocBin.Parse(payload, entry.Id).GetCatalogRecords(entry.Id, ref position));
            }
        }
        return records;
    }

    private static Dictionary<string, string> ExtractTargets(string path)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
        using (TurbineDat dat = new TurbineDat())
        {
            dat.Open(path, false);
            foreach (DatEntry entry in dat.ListLocalization())
            {
                byte[] raw = dat.ReadRaw(entry);
                byte[] payload = TurbineDat.MaybeDecompress(raw);
                if (TurbineDat.LooksCompressed(raw) && ReferenceEquals(raw, payload))
                    throw new InvalidDataException("Compressed localization entry could not be read: 0x" + entry.Id.ToString("X8"));
                foreach (LocRow row in LocBin.Parse(payload, entry.Id).GetRows(entry.Id))
                {
                    if (!result.ContainsKey(row.Key)) result.Add(row.Key, row.Original ?? string.Empty);
                }
            }
        }
        return result;
    }

    private static bool IsExcluded(CatalogRecord record)
    {
        return CatalogIdentity.IsExcludedFromTranslation(record.Did, record.RecordIndex, record.GroupIndex, record.IndexInGroup);
    }

    private static string Validate(CatalogRecord record, TranslationCandidate candidate)
    {
        if (!TranslationStatuses.IsPatchable(candidate.translation_status)) return "status";
        if (record.CriticalUi && !string.Equals(candidate.translation_status, TranslationStatuses.HumanApproved, StringComparison.Ordinal)) return "critical";
        if (!SourceDigest.Matches(record.SourceDigest, candidate.source_digest)) return "source";
        if (!string.Equals(record.TokenSignature, candidate.token_signature, StringComparison.OrdinalIgnoreCase)) return "token";
        if (string.IsNullOrWhiteSpace(candidate.target) || candidate.target.IndexOf('\0') >= 0) return "target";
        if (string.Equals(record.Source, candidate.target, StringComparison.Ordinal)) return "unchanged";
        ProtectedFormatResult format = ProtectedFormat.Validate(record.Source, candidate.target);
        return format.IsValid ? null : format.Reason;
    }

    private static string HashFile(string path)
    {
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        using (SHA256 sha = SHA256.Create())
        {
            StringBuilder value = new StringBuilder(64);
            foreach (byte item in sha.ComputeHash(stream)) value.Append(item.ToString("x2"));
            return value.ToString();
        }
    }
}
