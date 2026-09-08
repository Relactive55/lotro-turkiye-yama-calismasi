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
/// A read-only catalog anchor for base verification and local lookups. The
/// fields cannot prove the catalog hash after text changes: flat fallback
/// record identities can depend on neighboring raw payload bytes. The
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
    public int fast_schema_version { get; set; }
    public string known_ui_fixes_sha256 { get; set; }
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
/// The base catalog identity established by reading verified cache bytes.
/// It does not certify any candidate containing translation decisions.
/// </summary>
public sealed class VerifiedCatalogCache
{
    public string BaseCatalogSha256 { get; }

    internal VerifiedCatalogCache(string baseHash)
    {
        BaseCatalogSha256 = baseHash;
    }

    public string ComputeCandidateHash(IDictionary<string, string> decisions)
    {
        if (decisions != null && decisions.Count != 0)
            throw new InvalidDataException("record-only catalog cache cannot prove a candidate hash for any nonempty decision set; build and fully verify the actual DAT candidate");
        return BaseCatalogSha256;
    }
}

/// <summary>
/// Builds and consumes a persistent catalog cache.  A caller may build it
/// once from a verified read-only DAT, then reuse the verified base identity
/// and exact-term index. Changed candidate hashes require actual DAT validation.
/// </summary>
public static class CatalogCache
{
    private const string CacheKind = "catalog_record_cache";
    public const int SchemaVersion = 2;
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
        RequireDistinctPaths(datPath, recordsPath, metadataPath, fastRecordsPath);
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
        string sourceDatHash;
        long sourceDatSize;
        JavaScriptSerializer recordSerializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        using (SHA256 sourceCatalogSha = SHA256.Create())
        using (SHA256 cacheCatalogSha = SHA256.Create())
        using (FileStream sourceLock = OpenReadLocked(datPath))
        using (FileStream output = new FileStream(recordsPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
        using (GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: false))
        using (StreamWriter writer = new StreamWriter(gzip, new UTF8Encoding(false), 1024 * 1024))
        using (FileStream fastOutput = new FileStream(fastRecordsPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
        using (GZipStream fastGzip = new GZipStream(fastOutput, CompressionLevel.Optimal, leaveOpen: false))
        using (BinaryWriter fastWriter = new BinaryWriter(fastGzip, Encoding.UTF8, leaveOpen: false))
        using (TurbineDat dat = new TurbineDat())
        {
            sourceDatSize = sourceLock.Length;
            sourceDatHash = HashStream(sourceLock);
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
            schema_version = SchemaVersion,
            cache_kind = CacheKind,
            fast_schema_version = FastSchemaVersion,
            known_ui_fixes_sha256 = KnownUiFixes.ContentRevisionSha256,
            source_dat_sha256 = sourceDatHash,
            source_dat_size = sourceDatSize,
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

    /// <summary>
    /// Compatibility entry point for a trusted local cache. It validates the
    /// metadata and artifact content on every call. Release tooling should use
    /// OpenVerified with independently recorded source identity. Both entry
    /// points reject nonempty decisions because record fields alone do not
    /// capture raw byte dependencies between flat fallback record identities.
    /// </summary>
    public static string ComputeCandidateHash(string recordsPath, IDictionary<string, string> decisions)
    {
        CatalogCacheMetadata metadata = ReadMetadata(recordsPath, GetMetadataPath(recordsPath));
        ReadVerifiedRecords(recordsPath, metadata, false);
        return CreateVerifiedSnapshot(metadata).ComputeCandidateHash(decisions);
    }

    public static VerifiedCatalogCache OpenVerified(string recordsPath, string metadataPath,
        string expectedSourceDatSha256, long expectedSourceDatSize, string expectedSourceCatalogSha256)
    {
        CatalogCacheMetadata metadata = ReadMetadata(recordsPath, metadataPath);
        if (!SourceDigest.Matches(metadata.source_dat_sha256, expectedSourceDatSha256)
            || metadata.source_dat_size != expectedSourceDatSize
            || !SourceDigest.Matches(metadata.source_catalog_sha256, expectedSourceCatalogSha256))
            throw new InvalidDataException("catalog cache source identity does not match the expected source anchor; rebuild the cache");
        ReadVerifiedRecords(recordsPath, metadata, false);
        return CreateVerifiedSnapshot(metadata);
    }

    private static VerifiedCatalogCache CreateVerifiedSnapshot(CatalogCacheMetadata metadata)
    {
        return new VerifiedCatalogCache(metadata.cache_base_catalog_sha256.ToLowerInvariant());
    }

    /// <summary>
    /// Materializes the binary sidecar for an existing JSONL cache. This lets
    /// a valid local cache gain the fast hash path without reopening the source
    /// DAT. Legacy caches without a UI content identity must be rebuilt.
    /// </summary>
    public static string MaterializeFastRecords(string recordsPath, string metadataPath, string fastRecordsPath)
    {
        CatalogCacheMetadata metadata = ReadMetadata(recordsPath, metadataPath);
        List<CatalogCacheRecord> records = ReadVerifiedRecords(recordsPath, metadata, true);
        if (string.IsNullOrWhiteSpace(fastRecordsPath)) fastRecordsPath = GetFastRecordsPath(recordsPath);
        RequireDistinctPaths(recordsPath, metadataPath, fastRecordsPath);
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(recordsPath)),
            Path.GetDirectoryName(Path.GetFullPath(fastRecordsPath)), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("binary cache must be in the same directory as the JSONL cache", nameof(fastRecordsPath));
        EnsureParent(fastRecordsPath);
        using (FileStream output = new FileStream(fastRecordsPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
        using (GZipStream fastGzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: false))
        using (BinaryWriter fastWriter = new BinaryWriter(fastGzip, Encoding.UTF8, leaveOpen: false))
        {
            fastWriter.Write(FastMagic);
            fastWriter.Write(FastSchemaVersion);
            foreach (CatalogCacheRecord record in records) WriteFastRecord(fastWriter, record);
            fastWriter.Flush();
        }
        metadata.fast_records_asset_name = Path.GetFileName(fastRecordsPath);
        metadata.fast_records_asset_sha256 = HashFile(fastRecordsPath);
        WriteMetadata(metadataPath, metadata);
        return fastRecordsPath;
    }

    /// <summary>
    /// Refreshes exact-term lookup lists from an existing cache. This is a
    /// metadata-only operation and never touches the source DAT.
    /// </summary>
    public static void RefreshExactTermIndex(string recordsPath, string metadataPath, IEnumerable<string> exactTerms)
    {
        CatalogCacheMetadata metadata = ReadMetadata(recordsPath, metadataPath);
        List<CatalogCacheRecord> records = ReadVerifiedRecords(recordsPath, metadata, false);
        Dictionary<string, List<string>> index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        // Rebuilding the index must be idempotent.  A maintainer may refresh
        // terms more than once while curating a release; retaining old lists
        // would duplicate keys and make the metadata grow on every refresh.
        foreach (string term in (metadata.exact_term_index == null ? Enumerable.Empty<string>() : metadata.exact_term_index.Keys)
            .Concat(exactTerms ?? Enumerable.Empty<string>()))
        {
            string normalized = CatalogIdentity.NormalizeSource(term);
            if (!string.IsNullOrWhiteSpace(normalized)) index[normalized] = new List<string>();
        }
        foreach (CatalogCacheRecord record in records)
        {
            if (index.TryGetValue(record.s ?? string.Empty, out List<string> keys)) keys.Add(record.k);
        }
        metadata.exact_term_index = index;
        WriteMetadata(metadataPath, metadata);
    }

    private static List<CatalogCacheRecord> ReadVerifiedRecords(string recordsPath, CatalogCacheMetadata metadata, bool preferJson)
    {
        // Hash and decode the same open handles. On Windows FileShare.Read
        // prevents replacement or writing between verification and parsing.
        List<CatalogCacheRecord> records;
        using (FileStream json = OpenReadLocked(recordsPath))
        {
            EnsureAssetHash(json, metadata.records_asset_sha256, "JSONL");
            string fastPath = string.IsNullOrEmpty(metadata.fast_records_asset_name) ? null
                : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(recordsPath)), metadata.fast_records_asset_name);
            if (!preferJson && fastPath != null && File.Exists(fastPath))
            {
                using (FileStream fast = OpenReadLocked(fastPath))
                {
                    EnsureAssetHash(fast, metadata.fast_records_asset_sha256, "binary");
                    records = ReadFastRecords(fast, metadata.cache_record_count);
                }
            }
            else records = ReadJsonRecords(json, metadata.cache_record_count);
        }
        if (records.Count != metadata.cache_record_count)
            throw new InvalidDataException("catalog cache record count does not match metadata");
        HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < records.Count; i++)
        {
            CatalogCacheRecord record = records[i];
            if (record == null || string.IsNullOrWhiteSpace(record.k) || !keys.Add(record.k)
                || record.p != i || !record.k.StartsWith(record.d.ToString("X8", CultureInfo.InvariantCulture) + ":", StringComparison.Ordinal)
                || record.s == null || record.ts == null
                || !SourceDigest.IsValid(record.rf) || !SourceDigest.IsValid(record.st)
                || !SourceDigest.IsValid(record.sf) || !SourceDigest.IsValid(record.cf))
                throw new InvalidDataException("catalog cache record identity or ordering is invalid");
        }
        string baseHash = ComputeBaseCatalogHash(records);
        if (!SourceDigest.Matches(baseHash, metadata.cache_base_catalog_sha256))
            throw new InvalidDataException("catalog cache base hash does not match verified records");
        return records;
    }

    private static List<CatalogCacheRecord> ReadJsonRecords(Stream input, long expectedCount)
    {
        List<CatalogCacheRecord> records = new List<CatalogCacheRecord>();
        JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true))
        using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8, false, 1024 * 1024))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                CatalogCacheRecord record = serializer.Deserialize<CatalogCacheRecord>(line);
                if (record == null || string.IsNullOrWhiteSpace(record.k)) throw new InvalidDataException("catalog cache record is invalid");
                records.Add(record);
                if (records.Count > expectedCount) throw new InvalidDataException("catalog cache contains excess records");
            }
        }
        return records;
    }

    private static List<CatalogCacheRecord> ReadFastRecords(Stream input, long expectedCount)
    {
        List<CatalogCacheRecord> records = new List<CatalogCacheRecord>();
        using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true))
        using (BinaryReader reader = new BinaryReader(gzip, Encoding.UTF8, leaveOpen: false))
        {
            try
            {
                byte[] magic = reader.ReadBytes(FastMagic.Length);
                if (!magic.SequenceEqual(FastMagic) || reader.ReadInt32() != FastSchemaVersion)
                    throw new InvalidDataException("catalog cache binary sidecar header is invalid");
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("catalog cache binary sidecar header is truncated", ex);
            }
            for (long i = 0; i < expectedCount; i++)
            {
                CatalogCacheRecord record = new CatalogCacheRecord();
                try
                {
                    record.k = reader.ReadString();
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
            if (gzip.ReadByte() != -1) throw new InvalidDataException("catalog cache binary sidecar contains excess records or trailing data");
        }
        return records;
    }

    private static string ComputeBaseCatalogHash(List<CatalogCacheRecord> records)
    {
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

    private static CatalogCacheMetadata ReadMetadata(string recordsPath, string metadataPath)
    {
        if (string.IsNullOrWhiteSpace(recordsPath) || !File.Exists(recordsPath))
            throw new FileNotFoundException("catalog cache records not found", recordsPath);
        if (string.IsNullOrWhiteSpace(metadataPath) || !File.Exists(metadataPath))
            throw new FileNotFoundException("catalog cache metadata not found; rebuild the cache", metadataPath);
        RequireDistinctPaths(recordsPath, metadataPath);
        CatalogCacheMetadata metadata;
        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
            metadata = serializer.Deserialize<CatalogCacheMetadata>(File.ReadAllText(metadataPath, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            throw new InvalidDataException("catalog cache metadata is invalid", ex);
        }
        if (metadata == null || metadata.schema_version != SchemaVersion || metadata.fast_schema_version != FastSchemaVersion
            || !string.Equals(metadata.cache_kind, CacheKind, StringComparison.Ordinal)
            || !SourceDigest.Matches(metadata.known_ui_fixes_sha256, KnownUiFixes.ContentRevisionSha256))
            throw new InvalidDataException("catalog cache schema or automatic UI fix revision is stale; rebuild the cache");
        if (!SourceDigest.IsValid(metadata.source_dat_sha256) || metadata.source_dat_size <= 0
            || !SourceDigest.IsValid(metadata.source_catalog_sha256) || !SourceDigest.IsValid(metadata.cache_base_catalog_sha256)
            || metadata.source_record_count <= 0 || metadata.source_record_count != metadata.cache_record_count
            || metadata.cache_record_count > int.MaxValue)
            throw new InvalidDataException("catalog cache source identity or record count is invalid");
        if (!IsAssetName(metadata.records_asset_name) || !string.Equals(metadata.records_asset_name, Path.GetFileName(recordsPath), StringComparison.Ordinal)
            || !SourceDigest.IsValid(metadata.records_asset_sha256))
            throw new InvalidDataException("catalog cache JSONL asset identity is invalid");
        bool hasFastName = !string.IsNullOrEmpty(metadata.fast_records_asset_name);
        bool hasFastHash = !string.IsNullOrEmpty(metadata.fast_records_asset_sha256);
        if (hasFastName != hasFastHash || (hasFastName && (!IsAssetName(metadata.fast_records_asset_name)
            || !SourceDigest.IsValid(metadata.fast_records_asset_sha256)
            || string.Equals(metadata.fast_records_asset_name, metadata.records_asset_name, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("catalog cache binary asset identity is invalid");
        return metadata;
    }

    private static bool IsAssetName(string name) => !string.IsNullOrWhiteSpace(name)
        && name != "." && name != ".." && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static void EnsureAssetHash(FileStream stream, string expected, string kind)
    {
        if (!SourceDigest.Matches(HashStream(stream), expected))
            throw new InvalidDataException("catalog cache " + kind + " asset SHA-256 mismatch; rebuild or restore the cache");
        stream.Position = 0;
    }

    private static void WriteMetadata(string path, CatalogCacheMetadata metadata)
    {
        JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        File.WriteAllText(path, serializer.Serialize(metadata), new UTF8Encoding(false));
    }

    private static void RequireDistinctPaths(params string[] paths)
    {
        HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
            if (!unique.Add(Path.GetFullPath(path))) throw new ArgumentException("cache inputs and outputs must use different paths");
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

    private static string GetMetadataPath(string recordsPath)
    {
        if (string.IsNullOrWhiteSpace(recordsPath)) throw new ArgumentException("recordsPath");
        const string suffix = ".jsonl.gz";
        return recordsPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? recordsPath.Substring(0, recordsPath.Length - suffix.Length) + ".json"
            : recordsPath + ".json";
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
        using (FileStream stream = OpenReadLocked(path)) return HashStream(stream);
    }

    private static FileStream OpenReadLocked(string path)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
    }

    private static string HashStream(Stream stream)
    {
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
