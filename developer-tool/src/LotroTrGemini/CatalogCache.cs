using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace LotroTrGemini;

/// <summary>
/// A read-only, reusable catalog anchor.  It contains the fields needed to
/// reproduce a candidate catalog hash without opening the DAT again.  The
/// compressed record stream is an internal maintainer artifact; it is never
/// used as a DAT input and cannot write to the game file.
/// </summary>
public sealed class CatalogCacheRecord
{
    public string k { get; set; }
    public long p { get; set; }
    public int d { get; set; }
    public string s { get; set; }
    public string rf { get; set; }
    public string st { get; set; }
    public string sf { get; set; }
    public string cf { get; set; }
    public string ts { get; set; }
}

public sealed class CatalogCacheMetadata
{
    public int schema_version { get; set; }
    public string cache_kind { get; set; }
    public string source_dat_sha256 { get; set; }
    public long source_dat_size { get; set; }
    public string source_catalog_sha256 { get; set; }
    public string cache_base_catalog_sha256 { get; set; }
    public long source_record_count { get; set; }
    public long cache_record_count { get; set; }
    public string records_asset_name { get; set; }
    public string records_asset_sha256 { get; set; }
    public string fast_records_asset_name { get; set; }
    public string fast_records_asset_sha256 { get; set; }
    public Dictionary<string, List<string>> exact_term_index { get; set; }
}

/// <summary>
/// Builds and consumes a persistent catalog cache.  A caller may build it
/// once from a verified read-only DAT, then use ComputeCandidateHash for
/// subsequent small translation decisions without a native DAT scan.
/// </summary>
public static class CatalogCache
{
    private const string CacheKind = "catalog_record_cache";
    private const int FastSchemaVersion = 1;
    private static readonly byte[] FastMagic = Encoding.ASCII.GetBytes("LTCCACHE1");

    public static CatalogCacheMetadata Build(
        string datPath,
        string recordsPath,
        string metadataPath,
        IEnumerable<string> exactTerms)
    {
        if (string.IsNullOrWhiteSpace(datPath) || !File.Exists(datPath))
            throw new FileNotFoundException("DAT not found", datPath);
        if (string.IsNullOrWhiteSpace(recordsPath)) throw new ArgumentException("recordsPath");
        if (string.IsNullOrWhiteSpace(metadataPath)) throw new ArgumentException("metadataPath");
        EnsureParent(recordsPath);
        EnsureParent(metadataPath);
        string fastRecordsPath = GetFastRecordsPath(recordsPath);
        EnsureParent(fastRecordsPath);

        Dictionary<string, List<string>> termIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string term in exactTerms ?? Enumerable.Empty<string>())
        {
            string normalized = CatalogIdentity.NormalizeSource(term);
            if (!string.IsNullOrWhiteSpace(normalized) && !termIndex.ContainsKey(normalized))
                termIndex[normalized] = new List<string>();
        }

        long position = 0;
        long recordCount = 0;
        string sourceCatalogHash;
        string cacheBaseCatalogHash;
        JavaScriptSerializer recordSerializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        using (SHA256 sourceCatalogSha = SHA256.Create())
        using (SHA256 cacheCatalogSha = SHA256.Create())
        using (FileStream output = new FileStream(recordsPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
        using (GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: false))
        using (StreamWriter writer = new StreamWriter(gzip, new UTF8Encoding(false), 1024 * 1024))
        using (FileStream fastOutput = new FileStream(fastRecordsPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
        using (GZipStream fastGzip = new GZipStream(fastOutput, CompressionLevel.Optimal, leaveOpen: false))
        using (BinaryWriter fastWriter = new BinaryWriter(fastGzip, Encoding.UTF8, leaveOpen: false))
        using (TurbineDat dat = new TurbineDat())
        {
            fastWriter.Write(FastMagic);
            fastWriter.Write(FastSchemaVersion);
            dat.Open(datPath, writable: false);
            foreach (DatEntry entry in dat.ListLocalization())
            {
                byte[] originalPayload = TurbineDat.MaybeDecompress(dat.ReadRaw(entry));
                LocBin originalBin = LocBin.Parse(originalPayload, entry.Id);
                bool automatic = KnownUiFixes.HasAutomaticFix(entry.Id);
                LocBin activeBin = originalBin;
                if (automatic)
                {
                    byte[] activePayload = KnownUiFixes.ApplyTranslatedPayload(entry.Id, originalPayload);
                    activeBin = LocBin.Parse(activePayload, entry.Id);
                }

                long start = position;
                long originalPosition = start;
                long activePosition = start;
                List<CatalogRecord> originalRecords = originalBin.GetCatalogRecords(entry.Id, ref originalPosition);
                List<CatalogRecord> activeRecords = ReferenceEquals(activeBin, originalBin)
                    ? originalRecords
                    : activeBin.GetCatalogRecords(entry.Id, ref activePosition);
                if (originalRecords.Count != activeRecords.Count)
                    throw new InvalidDataException("automatic UI fix changed catalog row count for " + entry.Id.ToString("X8"));

                for (int i = 0; i < originalRecords.Count; i++)
                {
                    CatalogRecord before = originalRecords[i];
                    CatalogRecord active = activeRecords[i];
                    AppendHashLine(sourceCatalogSha, before);
                    AppendHashLine(cacheCatalogSha, active);
                    CatalogCacheRecord cached = new CatalogCacheRecord
                    {
                        k = active.Key,
                        p = active.Position,
                        d = active.Did,
                        s = active.Source ?? string.Empty,
                        rf = active.RecordFingerprint ?? string.Empty,
                        st = active.StructuralFingerprint ?? string.Empty,
                        sf = active.SourceFingerprint ?? string.Empty,
                        cf = active.ContextFingerprint ?? string.Empty,
                        ts = active.TokenSignature ?? string.Empty
                    };
                    writer.WriteLine(recordSerializer.Serialize(cached));
                    WriteFastRecord(fastWriter, cached);
                    recordCount++;
                    if (termIndex.TryGetValue(cached.s, out List<string> keys)) keys.Add(cached.k);
                }
                position += activeRecords.Count;
            }
            writer.Flush();
            fastWriter.Flush();
            sourceCatalogSha.TransformFinalBlock(new byte[0], 0, 0);
            cacheCatalogSha.TransformFinalBlock(new byte[0], 0, 0);
            sourceCatalogHash = ToHex(sourceCatalogSha.Hash);
            cacheBaseCatalogHash = ToHex(cacheCatalogSha.Hash);
        }

        CatalogCacheMetadata metadata = new CatalogCacheMetadata
        {
            schema_version = 1,
            cache_kind = CacheKind,
            source_dat_sha256 = HashFile(datPath),
            source_dat_size = new FileInfo(datPath).Length,
            source_catalog_sha256 = sourceCatalogHash,
            cache_base_catalog_sha256 = cacheBaseCatalogHash,
            source_record_count = recordCount,
            cache_record_count = recordCount,
            records_asset_name = Path.GetFileName(recordsPath),
            records_asset_sha256 = HashFile(recordsPath),
            fast_records_asset_name = Path.GetFileName(fastRecordsPath),
            fast_records_asset_sha256 = HashFile(fastRecordsPath),
            exact_term_index = termIndex
        };

        JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        File.WriteAllText(metadataPath, serializer.Serialize(metadata), new UTF8Encoding(false));
        return metadata;
    }

    /// <summary>Recomputes the candidate catalog hash from the cached records.</summary>
    public static string ComputeCandidateHash(string recordsPath, IDictionary<string, string> decisions)
    {
        if (string.IsNullOrWhiteSpace(recordsPath) || !File.Exists(recordsPath))
            throw new FileNotFoundException("catalog cache records not found", recordsPath);
        if ((decisions == null || decisions.Count == 0)
            && TryReadCacheBaseHash(recordsPath, out string cacheBaseHash))
            return cacheBaseHash;
        string fastRecordsPath = GetFastRecordsPath(recordsPath);
        List<CatalogCacheRecord> records = File.Exists(fastRecordsPath)
            ? ReadFastRecords(fastRecordsPath)
            : ReadJsonRecords(recordsPath);
        return ComputeCandidateHashFromRecords(records, decisions);
    }

    /// <summary>
    /// Materializes the binary sidecar for an existing JSONL cache. This lets
    /// an already-published cache gain the fast hash path without reopening the
    /// source DAT.
    /// </summary>
    public static string MaterializeFastRecords(string recordsPath, string metadataPath, string fastRecordsPath)
    {
        if (string.IsNullOrWhiteSpace(recordsPath) || !File.Exists(recordsPath))
            throw new FileNotFoundException("catalog cache records not found", recordsPath);
        if (string.IsNullOrWhiteSpace(fastRecordsPath)) fastRecordsPath = GetFastRecordsPath(recordsPath);
        EnsureParent(fastRecordsPath);
        JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        long count = 0;
        using (FileStream input = new FileStream(recordsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: false))
        using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8, false, 1024 * 1024))
        using (FileStream output = new FileStream(fastRecordsPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
        using (GZipStream fastGzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: false))
        using (BinaryWriter fastWriter = new BinaryWriter(fastGzip, Encoding.UTF8, leaveOpen: false))
        {
            fastWriter.Write(FastMagic);
            fastWriter.Write(FastSchemaVersion);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                CatalogCacheRecord record = serializer.Deserialize<CatalogCacheRecord>(line);
                if (record == null || string.IsNullOrWhiteSpace(record.k)) throw new InvalidDataException("catalog cache record is invalid");
                WriteFastRecord(fastWriter, record);
                count++;
            }
            fastWriter.Flush();
        }
        if (!string.IsNullOrWhiteSpace(metadataPath) && File.Exists(metadataPath))
        {
            CatalogCacheMetadata metadata = serializer.Deserialize<CatalogCacheMetadata>(File.ReadAllText(metadataPath, Encoding.UTF8));
            if (metadata == null) throw new InvalidDataException("catalog cache metadata is invalid");
            metadata.fast_records_asset_name = Path.GetFileName(fastRecordsPath);
            metadata.fast_records_asset_sha256 = HashFile(fastRecordsPath);
            File.WriteAllText(metadataPath, serializer.Serialize(metadata), new UTF8Encoding(false));
        }
        return fastRecordsPath;
    }

    /// <summary>
    /// Refreshes exact-term lookup lists from an existing cache. This is a
    /// metadata-only operation and never touches the source DAT.
    /// </summary>
    public static void RefreshExactTermIndex(string recordsPath, string metadataPath, IEnumerable<string> exactTerms)
    {
        if (string.IsNullOrWhiteSpace(metadataPath) || !File.Exists(metadataPath))
            throw new FileNotFoundException("catalog cache metadata not found", metadataPath);
        JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        CatalogCacheMetadata metadata = serializer.Deserialize<CatalogCacheMetadata>(File.ReadAllText(metadataPath, Encoding.UTF8));
        if (metadata == null || metadata.schema_version != 1 || !string.Equals(metadata.cache_kind, CacheKind, StringComparison.Ordinal))
            throw new InvalidDataException("catalog cache metadata is invalid");
        Dictionary<string, List<string>> index = metadata.exact_term_index
            ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        // Rebuilding the index must be idempotent.  A maintainer may refresh
        // terms more than once while curating a release; retaining old lists
        // would duplicate keys and make the metadata grow on every refresh.
        foreach (List<string> keys in index.Values) keys.Clear();
        foreach (string term in exactTerms ?? Enumerable.Empty<string>())
        {
            string normalized = CatalogIdentity.NormalizeSource(term);
            if (!string.IsNullOrWhiteSpace(normalized)) index[normalized] = new List<string>();
        }
        string fastRecordsPath = GetFastRecordsPath(recordsPath);
        List<CatalogCacheRecord> records = File.Exists(fastRecordsPath)
            ? ReadFastRecords(fastRecordsPath)
            : ReadJsonRecords(recordsPath);
        foreach (CatalogCacheRecord record in records)
        {
            if (index.TryGetValue(record.s ?? string.Empty, out List<string> keys)) keys.Add(record.k);
        }
        metadata.exact_term_index = index;
        File.WriteAllText(metadataPath, serializer.Serialize(metadata), new UTF8Encoding(false));
    }

    private static List<CatalogCacheRecord> ReadJsonRecords(string recordsPath)
    {
        List<CatalogCacheRecord> records = new List<CatalogCacheRecord>();
        JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        using (FileStream input = new FileStream(recordsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: false))
        using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8, false, 1024 * 1024))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                CatalogCacheRecord record = serializer.Deserialize<CatalogCacheRecord>(line);
                if (record == null || string.IsNullOrWhiteSpace(record.k)) throw new InvalidDataException("catalog cache record is invalid");
                records.Add(record);
            }
        }
        return records;
    }

    private static List<CatalogCacheRecord> ReadFastRecords(string fastRecordsPath)
    {
        List<CatalogCacheRecord> records = new List<CatalogCacheRecord>();
        using (FileStream input = new FileStream(fastRecordsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: false))
        using (BinaryReader reader = new BinaryReader(gzip, Encoding.UTF8, leaveOpen: false))
        {
            byte[] magic = reader.ReadBytes(FastMagic.Length);
            if (!magic.SequenceEqual(FastMagic) || reader.ReadInt32() != FastSchemaVersion)
                throw new InvalidDataException("catalog cache binary sidecar header is invalid");
            while (true)
            {
                string key;
                try { key = reader.ReadString(); }
                catch (EndOfStreamException) { break; }
                CatalogCacheRecord record = new CatalogCacheRecord { k = key };
                try
                {
                    record.p = reader.ReadInt64();
                    record.d = reader.ReadInt32();
                    record.s = reader.ReadString();
                    record.rf = reader.ReadString();
                    record.st = reader.ReadString();
                    record.sf = reader.ReadString();
                    record.cf = reader.ReadString();
                    record.ts = reader.ReadString();
                }
                catch (EndOfStreamException ex)
                {
                    throw new InvalidDataException("catalog cache binary sidecar is truncated", ex);
                }
                if (string.IsNullOrWhiteSpace(record.k)) throw new InvalidDataException("catalog cache binary key is empty");
                records.Add(record);
            }
        }
        return records;
    }

    private static string ComputeCandidateHashFromRecords(List<CatalogCacheRecord> records, IDictionary<string, string> decisions)
    {
        records.Sort((left, right) => left.p.CompareTo(right.p));
        Dictionary<string, CatalogCacheRecord> byKey = records.ToDictionary(record => record.k, StringComparer.Ordinal);
        HashSet<string> changedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> decision in decisions ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            if (!byKey.TryGetValue(decision.Key, out CatalogCacheRecord record))
                throw new InvalidDataException("decision key is not present in catalog cache: " + decision.Key);
            string target = CatalogIdentity.NormalizeSource(decision.Value ?? string.Empty);
            if (!string.Equals(record.s, target, StringComparison.Ordinal))
            {
                record.s = target;
                changedKeys.Add(record.k);
            }
        }

        for (int i = 0; i < records.Count; i++)
        {
            CatalogCacheRecord record = records[i];
            bool sourceChanged = changedKeys.Contains(record.k);
            if (sourceChanged)
                record.sf = CatalogIdentity.Sha256Hex("source|" + CatalogIdentity.NormalizeSource(record.s));
            bool hasPrevious = i > 0 && records[i - 1].d == record.d;
            bool hasNext = i + 1 < records.Count && records[i + 1].d == record.d;
            bool contextChanged = sourceChanged
                || (hasPrevious && changedKeys.Contains(records[i - 1].k))
                || (hasNext && changedKeys.Contains(records[i + 1].k));
            if (!contextChanged) continue;
            string previous = hasPrevious ? records[i - 1].s : "<BOF>";
            string next = hasNext ? records[i + 1].s : "<EOF>";
            string previousRecord = hasPrevious ? records[i - 1].rf : string.Empty;
            string nextRecord = hasNext ? records[i + 1].rf : string.Empty;
            record.cf = CatalogIdentity.BuildContextFingerprint(previous, next, previousRecord, nextRecord);
        }

        using (SHA256 sha = SHA256.Create())
        {
            foreach (CatalogCacheRecord record in records)
            {
                string line = record.d.ToString("X8", CultureInfo.InvariantCulture) + "|"
                    + (record.rf ?? string.Empty) + "|"
                    + (record.st ?? string.Empty) + "|"
                    + (record.sf ?? string.Empty) + "|"
                    + (record.cf ?? string.Empty) + "\n";
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return ToHex(sha.Hash);
        }
    }

    private static bool TryReadCacheBaseHash(string recordsPath, out string hash)
    {
        hash = null;
        string metadataPath = recordsPath;
        const string suffix = ".jsonl.gz";
        if (metadataPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            metadataPath = metadataPath.Substring(0, metadataPath.Length - suffix.Length) + ".json";
        else
            metadataPath += ".json";
        if (!File.Exists(metadataPath)) return false;
        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
            CatalogCacheMetadata metadata = serializer.Deserialize<CatalogCacheMetadata>(File.ReadAllText(metadataPath, Encoding.UTF8));
            if (metadata == null || !SourceDigest.IsValid(metadata.cache_base_catalog_sha256)) return false;
            hash = metadata.cache_base_catalog_sha256.ToLowerInvariant();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteFastRecord(BinaryWriter writer, CatalogCacheRecord record)
    {
        writer.Write(record.k ?? string.Empty);
        writer.Write(record.p);
        writer.Write(record.d);
        writer.Write(record.s ?? string.Empty);
        writer.Write(record.rf ?? string.Empty);
        writer.Write(record.st ?? string.Empty);
        writer.Write(record.sf ?? string.Empty);
        writer.Write(record.cf ?? string.Empty);
        writer.Write(record.ts ?? string.Empty);
    }

    private static string GetFastRecordsPath(string recordsPath)
    {
        const string suffix = ".jsonl.gz";
        if (recordsPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return recordsPath.Substring(0, recordsPath.Length - suffix.Length) + ".bin.gz";
        return recordsPath + ".bin.gz";
    }

    private static void AppendHashLine(SHA256 sha, CatalogRecord record)
    {
        string line = record.Did.ToString("X8", CultureInfo.InvariantCulture) + "|"
            + (record.RecordFingerprint ?? string.Empty) + "|"
            + (record.StructuralFingerprint ?? string.Empty) + "|"
            + (record.SourceFingerprint ?? string.Empty) + "|"
            + (record.ContextFingerprint ?? string.Empty) + "\n";
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
    }

    private static void EnsureParent(string path)
    {
        string parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
    }

    private static string HashFile(string path)
    {
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        using (SHA256 sha = SHA256.Create()) return ToHex(sha.ComputeHash(stream));
    }

    private static string ToHex(byte[] bytes)
    {
        if (bytes == null) return string.Empty;
        StringBuilder result = new StringBuilder(bytes.Length * 2);
        foreach (byte value in bytes) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        return result.ToString();
    }

}
