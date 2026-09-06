using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace LotroTrGemini;

/// <summary>
/// A small, client-assisted source update. It carries only the records that
/// need new translation; raw English text is opt-in and omitted by default.
/// </summary>
public sealed class SourceBundleRecord
{
    public string entry_identity { get; set; }
    public string dat_key { get; set; }
    public int did { get; set; }
    public int record_index { get; set; }
    public int group_index { get; set; }
    public int index_in_group { get; set; }
    public string source_digest { get; set; }
    public string token_signature { get; set; }
    public string classification { get; set; }
    public bool critical_ui { get; set; }
    public string source { get; set; }
}

public sealed class SourceBundleDocument
{
    public int schema_version { get; set; }
    public string bundle_kind { get; set; }
    public string source_dat_sha256 { get; set; }
    public long source_dat_size { get; set; }
    public string source_catalog_sha256 { get; set; }
    public Dictionary<string, string> dat_metadata { get; set; }
    public List<SourceBundleRecord> records { get; set; }
}

/// <summary>
/// Builds a privacy-preserving source bundle for the CLIENT_ASSISTED flow.
/// The builder is deliberately independent of the DAT writer and never reads
/// or uploads a file by itself.
/// </summary>
public static class SourceBundleBuilder
{
    public const string BundleKind = "client_assisted_source_update";

    public static SourceBundleDocument Build(
        CatalogSnapshot snapshot,
        IList<CatalogDiffRecord> diff,
        bool includeSourceText = false)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        return Build(
            snapshot.DatSha256,
            snapshot.DatSize,
            snapshot.CatalogSha256,
            diff,
            includeSourceText,
            Metadata(snapshot));
    }

    public static SourceBundleDocument Build(
        string sourceDatSha256,
        long sourceDatSize,
        string sourceCatalogSha256,
        IList<CatalogDiffRecord> diff,
        bool includeSourceText = false,
        IDictionary<string, string> datMetadata = null)
    {
        if (!SourceDigest.IsValid(sourceDatSha256)) throw new ArgumentException("sourceDatSha256");
        if (sourceDatSize < 1) throw new ArgumentOutOfRangeException(nameof(sourceDatSize));
        if (!SourceDigest.IsValid(sourceCatalogSha256)) throw new ArgumentException("sourceCatalogSha256");

        SourceBundleDocument document = new SourceBundleDocument
        {
            schema_version = 1,
            bundle_kind = BundleKind,
            source_dat_sha256 = sourceDatSha256.ToLowerInvariant(),
            source_dat_size = sourceDatSize,
            source_catalog_sha256 = sourceCatalogSha256.ToLowerInvariant(),
            dat_metadata = CopyMetadata(datMetadata),
            records = new List<SourceBundleRecord>()
        };
        HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (CatalogDiffRecord item in diff ?? new List<CatalogDiffRecord>())
        {
            if (item == null
                || (item.Classification != DiffClassification.NEW && item.Classification != DiffClassification.MODIFIED)
                || item.NewRecord == null)
                continue;

            CatalogRecord record = item.NewRecord;
            string identity = record.EntryIdentity;
            if (string.IsNullOrWhiteSpace(identity) || !identities.Add(identity))
                throw new InvalidDataException("source bundle duplicate or empty entry_identity");

            document.records.Add(new SourceBundleRecord
            {
                entry_identity = identity,
                dat_key = record.Key,
                did = record.Did,
                record_index = record.RecordIndex,
                group_index = record.GroupIndex,
                index_in_group = record.IndexInGroup,
                source_digest = record.SourceDigest,
                token_signature = record.TokenSignature,
                classification = item.Classification.ToString(),
                critical_ui = record.CriticalUi,
                source = includeSourceText ? record.Source : null
            });
        }
        document.records.Sort((left, right) => string.CompareOrdinal(left.entry_identity, right.entry_identity));
        SourceBundleValidator.EnsureValid(document);
        return document;
    }

    private static Dictionary<string, string> Metadata(CatalogSnapshot snapshot)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(result, "block_size", snapshot.BlockSize);
        Add(result, "vnum_dat_file", snapshot.VnumDatFile);
        Add(result, "vnum_game_data", snapshot.VnumGameData);
        Add(result, "dat_file_id", snapshot.DatFileId);
        if (!string.IsNullOrWhiteSpace(snapshot.DatIdStamp)) result["dat_stamp"] = snapshot.DatIdStamp;
        if (!string.IsNullOrWhiteSpace(snapshot.FirstIterationGuid)) result["first_iteration_guid"] = snapshot.FirstIterationGuid;
        return result;
    }

    private static void Add(Dictionary<string, string> target, string key, int value)
    {
        if (value > 0) target[key] = value.ToString(CultureInfo.InvariantCulture);
    }

    private static void Add(Dictionary<string, string> target, string key, uint value)
    {
        if (value > 0) target[key] = value.ToString(CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, string> CopyMetadata(IDictionary<string, string> metadata)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (metadata == null) return result;
        foreach (KeyValuePair<string, string> pair in metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null) result[pair.Key] = pair.Value;
        }
        return result;
    }
}

public static class SourceBundleValidator
{
    public static void EnsureValid(SourceBundleDocument document)
    {
        if (!TryValidate(document, out string reason)) throw new InvalidDataException(reason);
    }

    public static bool TryValidate(SourceBundleDocument document, out string reason)
    {
        reason = null;
        if (document == null || document.schema_version != 1 || document.bundle_kind != SourceBundleBuilder.BundleKind)
            return Fail("schema_version or bundle_kind is invalid", out reason);
        if (!SourceDigest.IsValid(document.source_dat_sha256)
            || document.source_dat_size < 1
            || !SourceDigest.IsValid(document.source_catalog_sha256))
            return Fail("source bundle baseline is invalid", out reason);
        if (document.records == null || document.records.Count > 100000)
            return Fail("source bundle records are missing or too large", out reason);

        HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> datKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (SourceBundleRecord record in document.records)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.entry_identity) || string.IsNullOrWhiteSpace(record.dat_key))
                return Fail("source bundle entry identity is missing", out reason);
            if (!identities.Add(record.entry_identity)
                || !datKeys.Add(record.did.ToString("X8", CultureInfo.InvariantCulture) + "|" + record.dat_key))
                return Fail("source bundle contains duplicate identity", out reason);
            if (record.did < 0 || record.record_index < 0 || record.group_index < -1 || record.index_in_group < 0)
                return Fail("source bundle coordinates are invalid", out reason);
            string expectedDatKey = record.did.ToString("X8", CultureInfo.InvariantCulture) + ":" + record.record_index + ":" + record.group_index + ":" + record.index_in_group;
            if (!string.Equals(record.dat_key, expectedDatKey, StringComparison.Ordinal))
                return Fail("source bundle dat_key does not match coordinates", out reason);
            if (CatalogIdentity.IsCriticalUiDid(record.did) && !record.critical_ui)
                return Fail("critical UI record cannot be downgraded", out reason);
            if (!SourceDigest.IsValid(record.source_digest) || !SourceDigest.IsValid(record.token_signature))
                return Fail("source bundle digest or token signature is invalid", out reason);
            if (record.classification != DiffClassification.NEW.ToString()
                && record.classification != DiffClassification.MODIFIED.ToString())
                return Fail("source bundle may contain only NEW or MODIFIED records", out reason);
            if (record.source != null && record.source.Length > 16000)
                return Fail("source bundle source text is too long", out reason);
        }
        return true;
    }

    private static bool Fail(string value, out string reason)
    {
        reason = value;
        return false;
    }
}

/// <summary>
/// Uses an ordered object graph so the same bundle is byte-for-byte stable.
/// Null source text is omitted from the JSON rather than emitted as a field.
/// </summary>
public static class SourceBundleSerializer
{
    public static string Serialize(SourceBundleDocument document)
    {
        SourceBundleValidator.EnsureValid(document);
        OrderedDictionary root = new OrderedDictionary();
        root["schema_version"] = document.schema_version;
        root["bundle_kind"] = document.bundle_kind;
        root["source_dat_sha256"] = document.source_dat_sha256;
        root["source_dat_size"] = document.source_dat_size;
        root["source_catalog_sha256"] = document.source_catalog_sha256;
        OrderedDictionary metadata = new OrderedDictionary();
        foreach (KeyValuePair<string, string> pair in (document.dat_metadata ?? new Dictionary<string, string>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            metadata[pair.Key] = pair.Value;
        root["dat_metadata"] = metadata;
        List<OrderedDictionary> records = new List<OrderedDictionary>();
        foreach (SourceBundleRecord record in document.records.OrderBy(item => item.entry_identity, StringComparer.Ordinal))
        {
            OrderedDictionary value = new OrderedDictionary();
            value["entry_identity"] = record.entry_identity;
            value["dat_key"] = record.dat_key;
            value["did"] = record.did;
            value["record_index"] = record.record_index;
            value["group_index"] = record.group_index;
            value["index_in_group"] = record.index_in_group;
            value["source_digest"] = record.source_digest;
            value["token_signature"] = record.token_signature;
            value["classification"] = record.classification;
            value["critical_ui"] = record.critical_ui;
            if (record.source != null) value["source"] = record.source;
            records.Add(value);
        }
        root["records"] = records;
        return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 }.Serialize(root);
    }

    public static SourceBundleDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("source bundle JSON is empty");
        try
        {
            SourceBundleDocument document = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 100
            }.Deserialize<SourceBundleDocument>(json);
            SourceBundleValidator.EnsureValid(document);
            return document;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) { throw new InvalidDataException("source bundle JSON is invalid: " + ex.Message); }
    }
}
