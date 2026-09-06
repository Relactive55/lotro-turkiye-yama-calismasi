using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace LotroTrGemini;

public static class TranslationStatuses
{
    public const string HumanApproved = "HUMAN_APPROVED";
    public const string TmReused = "TM_REUSED";
    public const string Glossary = "GLOSSARY";
    public const string MachineTranslated = "MACHINE_TRANSLATED";
    public const string ReviewRequired = "REVIEW_REQUIRED";
    public const string Untranslated = "UNTRANSLATED";
    public const string ExcludedFromAutoPatch = "EXCLUDED_FROM_AUTO_PATCH";

    public static bool IsPatchable(string value)
    {
        return string.Equals(value, HumanApproved, StringComparison.Ordinal)
            || string.Equals(value, TmReused, StringComparison.Ordinal)
            || string.Equals(value, Glossary, StringComparison.Ordinal)
            || string.Equals(value, MachineTranslated, StringComparison.Ordinal);
    }
}

/// <summary>
/// A public-safe semantic patch row. It intentionally contains no raw English
/// source text; the source is resolved from the user's current DAT and checked
/// against source_digest before a row can be applied.
/// </summary>
public sealed class SemanticPatchEntry
{
    public string entry_identity { get; set; }
    public string dat_key { get; set; }
    public int did { get; set; }
    public int record_index { get; set; }
    public int group_index { get; set; }
    public int index_in_group { get; set; }
    public string source_digest { get; set; }
    public string token_signature { get; set; }
    public string target { get; set; }
    public string translation_status { get; set; }
    public string translation_engine { get; set; }
    public string translation_engine_version { get; set; }
    public string classification { get; set; }
    public bool critical_ui { get; set; }
}

public sealed class SemanticPatchCounts
{
    public int safe_translated_count { get; set; }
    public int skipped_changed_count { get; set; }
    public int critical_review_required_count { get; set; }
    public int ambiguous_count { get; set; }
    public int removed_count { get; set; }
    public int review_required_count { get; set; }
}

public sealed class SemanticPatchDocument
{
    public int schema_version { get; set; }
    public string patch_kind { get; set; }
    public string patch_version { get; set; }
    public string source_dat_sha256 { get; set; }
    public long source_dat_size { get; set; }
    public string source_catalog_sha256 { get; set; }
    public string translation_catalog_version { get; set; }
    public string patch_generator_version { get; set; }
    public string translation_provider { get; set; }
    public string translation_model_version { get; set; }
    public string translation_model_sha256 { get; set; }
    public SemanticPatchCounts counts { get; set; }
    public List<SemanticPatchEntry> entries { get; set; }
}

/// <summary>One candidate produced by the approved/TM/glossary/MT pipeline.</summary>
public sealed class TranslationCandidate
{
    public string entry_identity { get; set; }
    public string dat_key { get; set; }
    public string source_digest { get; set; }
    public string token_signature { get; set; }
    public string target { get; set; }
    public string translation_status { get; set; }
    public string translation_engine { get; set; }
    public string translation_engine_version { get; set; }
    public bool critical_ui { get; set; }
}

public sealed class SemanticPatchApplyResult
{
    public int Applied { get; internal set; }
    public int SourceChanged { get; internal set; }
    public int Ambiguous { get; internal set; }
    public int CriticalSkipped { get; internal set; }
    public int Rejected { get; internal set; }
    public int Missing { get; internal set; }
    public bool BaselineMismatch { get; internal set; }
    public List<string> Warnings { get; } = new List<string>();
}

/// <summary>
/// Deterministic, fail-closed semantic patch builder. It is independent of the
/// DAT writer: generation can run in CI while the native/managed writer remains
/// a separately tested capability gate.
/// </summary>
public static class SemanticPatchBuilder
{
    public const string PatchKind = "semantic_delta_patch";

    public static SemanticPatchDocument Build(
        string patchVersion,
        string sourceDatSha256,
        long sourceDatSize,
        string sourceCatalogSha256,
        IList<CatalogDiffRecord> diff,
        IEnumerable<TranslationCandidate> candidates,
        string translationCatalogVersion,
        string patchGeneratorVersion,
        string translationProvider,
        string translationModelVersion,
        string translationModelSha256)
    {
        if (string.IsNullOrWhiteSpace(patchVersion)) throw new ArgumentException("patchVersion");
        if (!SourceDigest.IsValid(sourceDatSha256)) throw new ArgumentException("sourceDatSha256");
        if (sourceDatSize < 1) throw new ArgumentOutOfRangeException(nameof(sourceDatSize));
        if (!SourceDigest.IsValid(sourceCatalogSha256)) throw new ArgumentException("sourceCatalogSha256");

        Dictionary<string, List<TranslationCandidate>> byIdentity = IndexCandidates(candidates);
        SemanticPatchDocument document = new SemanticPatchDocument
        {
            schema_version = 1,
            patch_kind = PatchKind,
            patch_version = patchVersion,
            source_dat_sha256 = sourceDatSha256.ToLowerInvariant(),
            source_dat_size = sourceDatSize,
            source_catalog_sha256 = sourceCatalogSha256.ToLowerInvariant(),
            translation_catalog_version = translationCatalogVersion ?? string.Empty,
            patch_generator_version = patchGeneratorVersion ?? string.Empty,
            translation_provider = translationProvider ?? string.Empty,
            translation_model_version = translationModelVersion ?? string.Empty,
            translation_model_sha256 = translationModelSha256 ?? string.Empty,
            counts = new SemanticPatchCounts(),
            entries = new List<SemanticPatchEntry>()
        };

        HashSet<string> emittedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (CatalogDiffRecord item in diff ?? new List<CatalogDiffRecord>())
        {
            CatalogRecord current = item == null ? null : item.NewRecord;
            if (item == null || current == null)
            {
                if (item != null && item.Classification == DiffClassification.REMOVED) document.counts.removed_count++;
                continue;
            }

            if (item.Classification == DiffClassification.REMOVED)
            {
                document.counts.removed_count++;
                continue;
            }
            if (CatalogIdentity.IsExcludedFromTranslation(current.Did, current.RecordIndex, current.GroupIndex, current.IndexInGroup))
                continue;
            if (item.Classification == DiffClassification.AMBIGUOUS)
            {
                document.counts.ambiguous_count++;
                if (current.CriticalUi) document.counts.critical_review_required_count++;
                continue;
            }

            TranslationCandidate candidate = FindCandidate(byIdentity, current);
            if (candidate == null)
            {
                if (item.Classification == DiffClassification.NEW || item.Classification == DiffClassification.MODIFIED)
                    document.counts.skipped_changed_count++;
                if (current.CriticalUi) document.counts.critical_review_required_count++;
                continue;
            }

            string rejection;
            if (!IsSafeCandidate(item, current, candidate, out rejection))
            {
                document.counts.review_required_count++;
                if (item.Classification == DiffClassification.NEW || item.Classification == DiffClassification.MODIFIED)
                    document.counts.skipped_changed_count++;
                if (current.CriticalUi) document.counts.critical_review_required_count++;
                continue;
            }

            string identity = current.EntryIdentity;
            if (!emittedKeys.Add(current.Key))
            {
                document.counts.ambiguous_count++;
                document.counts.review_required_count++;
                continue;
            }

            document.entries.Add(new SemanticPatchEntry
            {
                entry_identity = identity,
                dat_key = current.Key,
                did = current.Did,
                record_index = current.RecordIndex,
                group_index = current.GroupIndex,
                index_in_group = current.IndexInGroup,
                source_digest = current.SourceDigest,
                token_signature = current.TokenSignature,
                target = candidate.target,
                translation_status = candidate.translation_status,
                translation_engine = candidate.translation_engine ?? string.Empty,
                translation_engine_version = candidate.translation_engine_version ?? string.Empty,
                classification = item.Classification.ToString(),
                critical_ui = current.CriticalUi
            });
            document.counts.safe_translated_count++;
        }

        document.entries.Sort(delegate (SemanticPatchEntry left, SemanticPatchEntry right)
        {
            int result = string.CompareOrdinal(left.entry_identity, right.entry_identity);
            return result != 0 ? result : string.CompareOrdinal(left.dat_key, right.dat_key);
        });
        SemanticPatchValidator.EnsureValid(document);
        return document;
    }

    private static Dictionary<string, List<TranslationCandidate>> IndexCandidates(IEnumerable<TranslationCandidate> candidates)
    {
        Dictionary<string, List<TranslationCandidate>> result = new Dictionary<string, List<TranslationCandidate>>(StringComparer.Ordinal);
        foreach (TranslationCandidate candidate in candidates ?? Enumerable.Empty<TranslationCandidate>())
        {
            if (candidate == null) continue;
            string identity = candidate.entry_identity;
            if (!string.IsNullOrWhiteSpace(identity)) AddCandidate(result, identity, candidate);
            if (!string.IsNullOrWhiteSpace(candidate.dat_key)
                && !string.Equals(candidate.dat_key, identity, StringComparison.Ordinal))
                AddCandidate(result, candidate.dat_key, candidate);
        }
        return result;
    }

    private static void AddCandidate(Dictionary<string, List<TranslationCandidate>> index, string key, TranslationCandidate candidate)
    {
        if (!index.TryGetValue(key, out List<TranslationCandidate> list))
        {
            list = new List<TranslationCandidate>();
            index.Add(key, list);
        }
        list.Add(candidate);
    }

    private static TranslationCandidate FindCandidate(Dictionary<string, List<TranslationCandidate>> byIdentity, CatalogRecord record)
    {
        if (byIdentity.TryGetValue(record.EntryIdentity, out List<TranslationCandidate> direct) && direct.Count == 1)
            return direct[0];
        if (byIdentity.TryGetValue(record.Key, out List<TranslationCandidate> byKey) && byKey.Count == 1)
            return byKey[0];
        return null;
    }

    private static bool IsSafeCandidate(CatalogDiffRecord diff, CatalogRecord record, TranslationCandidate candidate, out string rejection)
    {
        rejection = null;
        if (!TranslationStatuses.IsPatchable(candidate.translation_status))
        {
            rejection = "translation status is not patchable";
            return false;
        }
        if (record.CriticalUi && !string.Equals(candidate.translation_status, TranslationStatuses.HumanApproved, StringComparison.Ordinal))
        {
            rejection = "critical UI requires human approval";
            return false;
        }
        if (!SourceDigest.Matches(record.SourceDigest, candidate.source_digest ?? record.SourceDigest))
        {
            rejection = "source digest differs from current catalog";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(candidate.token_signature)
            && !string.Equals(candidate.token_signature, record.TokenSignature, StringComparison.OrdinalIgnoreCase))
        {
            rejection = "token signature differs from current catalog";
            return false;
        }
        if (string.IsNullOrWhiteSpace(candidate.target) || candidate.target.IndexOf('\0') >= 0)
        {
            rejection = "empty or binary target";
            return false;
        }
        ProtectedFormatResult format = ProtectedFormat.Validate(record.Source, candidate.target);
        if (!format.IsValid)
        {
            rejection = format.Reason;
            return false;
        }
        if (string.Equals(record.Source, candidate.target, StringComparison.Ordinal))
        {
            rejection = "target is unchanged English";
            return false;
        }
        return true;
    }
}

public static class SemanticPatchValidator
{
    public static void EnsureValid(SemanticPatchDocument document)
    {
        string reason;
        if (!TryValidate(document, out reason)) throw new InvalidDataException(reason);
    }

    public static bool TryValidate(SemanticPatchDocument document, out string reason)
    {
        reason = null;
        if (document == null || document.schema_version != 1 || document.patch_kind != SemanticPatchBuilder.PatchKind)
            return Fail("schema_version or patch_kind is invalid", out reason);
        if (string.IsNullOrWhiteSpace(document.patch_version)
            || !SourceDigest.IsValid(document.source_dat_sha256)
            || document.source_dat_size < 1
            || !SourceDigest.IsValid(document.source_catalog_sha256))
            return Fail("patch identity or source baseline is invalid", out reason);
        if (document.counts == null || document.entries == null)
            return Fail("counts and entries are required", out reason);
        if (document.counts.safe_translated_count != document.entries.Count)
            return Fail("safe_translated_count does not match entries", out reason);

        HashSet<string> datKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (SemanticPatchEntry entry in document.entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.entry_identity) || string.IsNullOrWhiteSpace(entry.dat_key))
                return Fail("entry identity is missing", out reason);
            if (CatalogIdentity.IsExcludedFromTranslation(entry.did, entry.record_index, entry.group_index, entry.index_in_group))
                return Fail("excluded entry is present", out reason);
            if (!datKeys.Add(entry.dat_key))
                return Fail("duplicate DAT key", out reason);
            if (entry.did < 0 || entry.record_index < 0 || entry.group_index < -1 || entry.index_in_group < 0)
                return Fail("entry coordinates are invalid", out reason);
            if (!SourceDigest.IsValid(entry.source_digest) || !SourceDigest.IsValid(entry.token_signature))
                return Fail("entry digest or token signature is invalid", out reason);
            if (string.IsNullOrWhiteSpace(entry.target) || entry.target.IndexOf('\0') >= 0)
                return Fail("entry target is empty or binary", out reason);
            if (!TranslationStatuses.IsPatchable(entry.translation_status))
                return Fail("entry status is not patchable", out reason);
            if (entry.critical_ui && !string.Equals(entry.translation_status, TranslationStatuses.HumanApproved, StringComparison.Ordinal))
                return Fail("critical UI entry is not human approved", out reason);
            if (entry.classification == DiffClassification.AMBIGUOUS.ToString()
                || entry.classification == DiffClassification.REMOVED.ToString())
                return Fail("ambiguous or removed entry is present", out reason);
        }
        return true;
    }

    private static bool Fail(string value, out string reason)
    {
        reason = value;
        return false;
    }
}

public static class SemanticPatchSerializer
{
    public static string Serialize(SemanticPatchDocument document)
    {
        SemanticPatchValidator.EnsureValid(document);
        JavaScriptSerializer serializer = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 100
        };
        return serializer.Serialize(document);
    }

    public static SemanticPatchDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("semantic patch JSON is empty");
        try
        {
            SemanticPatchDocument document = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<SemanticPatchDocument>(json);
            SemanticPatchValidator.EnsureValid(document);
            return document;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) { throw new InvalidDataException("semantic patch JSON is invalid: " + ex.Message); }
    }
}

/// <summary>
/// Applies the semantic contract to an extracted catalog/row list. The DAT
/// writer is intentionally supplied by a separate adapter; this core routine
/// proves source-digest, token and critical-UI admission without touching files.
/// </summary>
public static class SemanticPatchApplier
{
    public static SemanticPatchApplyResult ApplyToRows(
        SemanticPatchDocument patch,
        IList<CatalogRecord> currentRecords,
        IList<LocRow> rows,
        string currentDatSha256 = null)
    {
        SemanticPatchValidator.EnsureValid(patch);
        SemanticPatchApplyResult result = new SemanticPatchApplyResult();
        if (!string.IsNullOrWhiteSpace(currentDatSha256)
            && !string.Equals(currentDatSha256, patch.source_dat_sha256, StringComparison.OrdinalIgnoreCase))
        {
            result.BaselineMismatch = true;
            result.Warnings.Add("current DAT SHA-256 does not match the patch baseline");
            return result;
        }

        Dictionary<string, List<CatalogRecord>> byIdentity = IndexRecords(currentRecords, record => record.EntryIdentity);
        Dictionary<string, List<CatalogRecord>> byDigest = IndexRecords(
            currentRecords,
            record => record.Did.ToString("X8") + "|" + record.SourceDigest + "|" + record.TokenSignature);
        Dictionary<string, CatalogRecord> recordsByKey = new Dictionary<string, CatalogRecord>(StringComparer.Ordinal);
        foreach (CatalogRecord record in currentRecords ?? new List<CatalogRecord>())
            if (record != null && !recordsByKey.ContainsKey(record.Key)) recordsByKey.Add(record.Key, record);
        Dictionary<string, LocRow> byKey = new Dictionary<string, LocRow>(StringComparer.Ordinal);
        foreach (LocRow row in rows ?? new List<LocRow>())
        {
            if (row != null && !byKey.ContainsKey(row.Key)) byKey.Add(row.Key, row);
        }

        foreach (SemanticPatchEntry entry in patch.entries)
        {
            CatalogRecord record = FindRecord(entry, recordsByKey, byIdentity, byDigest, result);
            if (record == null) continue;
            if (!SourceDigest.Matches(record.SourceDigest, entry.source_digest))
            {
                result.SourceChanged++;
                result.Warnings.Add(entry.dat_key + ": source_digest mismatch; English fallback kept");
                continue;
            }
            if (!string.Equals(record.TokenSignature, entry.token_signature, StringComparison.OrdinalIgnoreCase))
            {
                result.Rejected++;
                result.Warnings.Add(entry.dat_key + ": token signature mismatch");
                continue;
            }
            if (record.CriticalUi && !string.Equals(entry.translation_status, TranslationStatuses.HumanApproved, StringComparison.Ordinal))
            {
                result.CriticalSkipped++;
                continue;
            }
            ProtectedFormatResult format = ProtectedFormat.Validate(record.Source, entry.target);
            if (!format.IsValid)
            {
                result.Rejected++;
                result.Warnings.Add(entry.dat_key + ": " + format.Reason);
                continue;
            }
            if (!byKey.TryGetValue(record.Key, out LocRow row))
            {
                result.Missing++;
                result.Warnings.Add(entry.dat_key + ": current row is not writable");
                continue;
            }
            row.Translation = entry.target;
            result.Applied++;
        }
        return result;
    }

    private static CatalogRecord FindRecord(
        SemanticPatchEntry entry,
        Dictionary<string, CatalogRecord> byKey,
        Dictionary<string, List<CatalogRecord>> byIdentity,
        Dictionary<string, List<CatalogRecord>> byDigest,
        SemanticPatchApplyResult result)
    {
        if (byKey.TryGetValue(entry.dat_key, out CatalogRecord exact)) return exact;
        if (byIdentity.TryGetValue(entry.entry_identity, out List<CatalogRecord> direct))
        {
            if (direct.Count == 1) return direct[0];
            result.Ambiguous++;
            result.Warnings.Add(entry.entry_identity + ": duplicate stable identity");
            return null;
        }
        string digestKey = entry.did.ToString("X8") + "|" + entry.source_digest + "|" + entry.token_signature;
        if (byDigest.TryGetValue(digestKey, out List<CatalogRecord> fallback))
        {
            if (fallback.Count == 1) return fallback[0];
            result.Ambiguous++;
            result.Warnings.Add(entry.entry_identity + ": duplicate source digest");
            return null;
        }
        result.Missing++;
        result.Warnings.Add(entry.entry_identity + ": current source identity not found");
        return null;
    }

    private static Dictionary<string, List<CatalogRecord>> IndexRecords(
        IEnumerable<CatalogRecord> records,
        Func<CatalogRecord, string> key)
    {
        Dictionary<string, List<CatalogRecord>> result = new Dictionary<string, List<CatalogRecord>>(StringComparer.Ordinal);
        foreach (CatalogRecord record in records ?? Enumerable.Empty<CatalogRecord>())
        {
            if (record == null) continue;
            string value = key(record) ?? string.Empty;
            if (!result.TryGetValue(value, out List<CatalogRecord> list))
            {
                list = new List<CatalogRecord>();
                result.Add(value, list);
            }
            list.Add(record);
        }
        return result;
    }
}
