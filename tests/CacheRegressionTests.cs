using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using LotroTrGemini;

internal static class CacheRegressionTests
{
    private const int Did = 0x25000010;
    private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

    public static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "lotro-cache-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int passed = 0;
        try
        {
            string datPath = Path.Combine(directory, "synthetic.dat");
            string recordsPath = Path.Combine(directory, "catalog.jsonl.gz");
            string metadataPath = Path.Combine(directory, "catalog.json");
            string fastPath = Path.Combine(directory, "catalog.bin.gz");
            byte[] originalPayload = BuildPayload("First", "Use %s", "Last");
            byte[] originalDat = BuildDat(originalPayload);
            File.WriteAllBytes(datPath, originalDat);
            CatalogCacheMetadata metadata = CatalogCache.Build(datPath, recordsPath, metadataPath, new[] { "First" });
            string sourceCatalog = CatalogHash(originalPayload);
            Check(metadata.schema_version == 2 && metadata.fast_schema_version == 1
                && metadata.known_ui_fixes_sha256 == KnownUiFixes.ContentRevisionSha256
                && metadata.source_dat_sha256 == Hash(originalDat)
                && metadata.source_catalog_sha256 == sourceCatalog
                && metadata.cache_base_catalog_sha256 == sourceCatalog
                && metadata.cache_record_count == 3
                && File.ReadAllBytes(datPath).SequenceEqual(originalDat),
                "cache build binds source bytes, catalog, format and automatic UI revision without changing DAT");
            passed++;

            byte[] validJson = File.ReadAllBytes(recordsPath);
            byte[] validFast = File.ReadAllBytes(fastPath);
            string validMetadata = File.ReadAllText(metadataPath, Encoding.UTF8);
            Action restore = () =>
            {
                File.WriteAllBytes(recordsPath, validJson);
                File.WriteAllBytes(fastPath, validFast);
                File.WriteAllText(metadataPath, validMetadata, new UTF8Encoding(false));
            };
            Func<VerifiedCatalogCache> open = () => CatalogCache.OpenVerified(recordsPath, metadataPath,
                Hash(originalDat), originalDat.Length, sourceCatalog);
            Dictionary<string, string> decisions = new Dictionary<string, string> { { "25000010:0:-1:1", "Kullan %s" } };
            string candidateHash = CatalogHash(BuildPayload("First", "Kullan %s", "Last"));
            VerifiedCatalogCache snapshot = open();
            Check(snapshot.ComputeCandidateHash(null) == sourceCatalog
                && snapshot.ComputeCandidateHash(new Dictionary<string, string>()) == sourceCatalog
                && candidateHash != sourceCatalog,
                "verified snapshot returns only the unchanged base catalog identity");
            passed++;
            ExpectProjectionRejected(() => snapshot.ComputeCandidateHash(decisions), "record-only snapshot refuses a changed candidate projection");
            ExpectProjectionRejected(() => CatalogCache.ComputeCandidateHash(recordsPath, decisions), "compatibility API refuses a changed candidate projection");
            ExpectProjectionRejected(() => snapshot.ComputeCandidateHash(new Dictionary<string, string> { { "25000010:0:-1:1", "Use %s" } }),
                "record-only snapshot refuses a nonempty apparent no-op decision");
            ExpectProjectionRejected(() => snapshot.ComputeCandidateHash(new Dictionary<string, string> { { "25000010:0:-1:1", "Use \uFF05s" } }),
                "record-only snapshot refuses normalized-equal text with different raw bytes");
            passed += 4;

            File.Delete(datPath);
            File.Delete(fastPath);
            Check(open().ComputeCandidateHash(null) == sourceCatalog
                && CatalogCache.ComputeCandidateHash(recordsPath, null) == sourceCatalog,
                "verified JSON fallback works without reopening source DAT");
            passed++;
            restore();

            DateTime timestamp = File.GetLastWriteTimeUtc(fastPath);
            byte[] badFast = (byte[])validFast.Clone();
            badFast[badFast.Length / 2] ^= 0x20;
            File.WriteAllBytes(fastPath, badFast);
            File.SetLastWriteTimeUtc(fastPath, timestamp);
            ExpectInvalid(() => open(), "same-size same-timestamp binary replacement is rejected");
            ExpectInvalid(() => CatalogCache.ComputeCandidateHash(recordsPath, null), "empty decisions cannot bypass binary integrity");
            Check(snapshot.ComputeCandidateHash(null) == sourceCatalog,
                "already-verified snapshot is independent of later disk replacement");
            passed += 3;
            restore();

            byte[] badJson = (byte[])validJson.Clone();
            badJson[badJson.Length / 2] ^= 0x20;
            File.WriteAllBytes(recordsPath, badJson);
            ExpectInvalid(() => open(), "JSON artifact corruption is rejected even when binary artifact is intact");
            byte[] beforeMaterialize = File.ReadAllBytes(fastPath);
            ExpectInvalid(() => CatalogCache.MaterializeFastRecords(recordsPath, metadataPath, fastPath),
                "materialization rejects unverified JSON source");
            Check(File.ReadAllBytes(fastPath).SequenceEqual(beforeMaterialize), "failed materialization preserves existing binary artifact");
            passed += 3;
            restore();

            foreach (Action<CatalogCacheMetadata> mutation in new Action<CatalogCacheMetadata>[]
            {
                m => m.schema_version = 1,
                m => m.fast_schema_version = 9,
                m => m.known_ui_fixes_sha256 = CatalogIdentity.Sha256Hex("old-ui"),
                m => m.source_dat_sha256 = "invalid",
                m => m.source_dat_size = 0,
                m => m.source_catalog_sha256 = "invalid",
                m => m.records_asset_name = "different.jsonl.gz",
                m => m.fast_records_asset_name = "../outside.bin.gz",
                m => m.fast_records_asset_sha256 = null,
                m => m.cache_record_count = 4,
                m => { m.cache_record_count = 4; m.source_record_count = 4; },
                m => m.cache_base_catalog_sha256 = CatalogIdentity.Sha256Hex("counterfeit-base")
            })
            {
                metadata = Serializer.Deserialize<CatalogCacheMetadata>(validMetadata);
                mutation(metadata);
                Save(metadataPath, metadata);
                ExpectInvalid(() => CatalogCache.ComputeCandidateHash(recordsPath, null), "incoherent cache metadata is rejected");
                passed++;
                restore();
            }

            ExpectInvalid(() => CatalogCache.OpenVerified(recordsPath, metadataPath,
                CatalogIdentity.Sha256Hex("different-dat"), originalDat.Length, sourceCatalog), "source DAT hash must match independent anchor");
            ExpectInvalid(() => CatalogCache.OpenVerified(recordsPath, metadataPath,
                Hash(originalDat), originalDat.Length + 1, sourceCatalog), "source DAT size must match independent anchor");
            ExpectInvalid(() => CatalogCache.OpenVerified(recordsPath, metadataPath,
                Hash(originalDat), originalDat.Length, CatalogIdentity.Sha256Hex("different-catalog")), "source catalog must match independent anchor");
            passed += 3;

            Array fixes = (Array)typeof(KnownUiFixes).GetField("FlatFixes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            object fix = fixes.GetValue(0);
            FieldInfo targetField = fix.GetType().GetField("Target");
            string savedTarget = (string)targetField.GetValue(fix);
            string savedRevision = KnownUiFixes.ContentRevisionSha256;
            try
            {
                targetField.SetValue(fix, savedTarget + " changed");
                Check(KnownUiFixes.ContentRevisionSha256 != savedRevision, "changing automatic UI text changes cache content identity");
                ExpectInvalid(() => open(), "cache from an older automatic UI table is rejected");
                passed += 2;
            }
            finally { targetField.SetValue(fix, savedTarget); }

            metadata = Serializer.Deserialize<CatalogCacheMetadata>(validMetadata);
            metadata.fast_records_asset_name = null;
            metadata.fast_records_asset_sha256 = null;
            Save(metadataPath, metadata);
            File.WriteAllBytes(fastPath, badFast);
            Check(open().ComputeCandidateHash(null) == sourceCatalog,
                "undeclared adjacent binary file is ignored instead of trusted by filename");
            passed++;
            restore();

            // Keep the compressed checksum coherent, so format checks must
            // reject an invalid header or a truncated final key themselves.
            foreach (byte[] invalidBinary in new[] { Encoding.ASCII.GetBytes("INVALID!\u0001\0\0\0"), TruncatedBinary() })
            {
                WriteGzip(fastPath, invalidBinary);
                metadata = Serializer.Deserialize<CatalogCacheMetadata>(validMetadata);
                metadata.fast_records_asset_sha256 = Hash(File.ReadAllBytes(fastPath));
                Save(metadataPath, metadata);
                ExpectInvalid(() => open(), "checksum-coherent invalid binary format is rejected");
                passed++;
                restore();
            }

            string customFastPath = Path.Combine(directory, "custom-cache.bin.gz");
            CatalogCache.MaterializeFastRecords(recordsPath, metadataPath, customFastPath);
            File.WriteAllBytes(fastPath, badFast);
            Check(open().ComputeCandidateHash(null) == sourceCatalog,
                "materialized custom binary asset is selected by verified metadata");
            passed++;

            CatalogCache.RefreshExactTermIndex(recordsPath, metadataPath, new[] { "Use %s", "FIRST" });
            CatalogCache.RefreshExactTermIndex(recordsPath, metadataPath, new[] { "Use %s", "first" });
            metadata = Serializer.Deserialize<CatalogCacheMetadata>(File.ReadAllText(metadataPath, Encoding.UTF8));
            Check(metadata.exact_term_index.Values.All(keys => keys.Count == 1)
                && metadata.exact_term_index.Count == 2
                && open().ComputeCandidateHash(null) == sourceCatalog,
                "verified exact-term index refresh remains idempotent and preserves cache identity");
            passed++;
            ExpectInvalid(() => snapshot.ComputeCandidateHash(new Dictionary<string, string> { { "missing", "target" } }),
                "verified snapshot rejects decisions for absent keys");
            passed++;
            passed += RunFlatAnchorProjectionRegression(directory);
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("cache_tests_passed=" + passed);
        return passed;
    }

    private static int RunFlatAnchorProjectionRegression(string directory)
    {
        byte[] payload = BuildOverlappingFlatPayload();
        LocBin bin = LocBin.Parse(payload, Did);
        List<LocRow> rows = bin.GetRows(Did);
        LocRow changed = rows.Single(row => row.Original == "Prefix one ");
        long originalPosition = 0;
        List<CatalogRecord> originalRecords = bin.GetCatalogRecords(Did, ref originalPosition);
        changed.Translation = "Prefix two ";
        byte[] rebuilt = bin.Rebuild(rows);
        long candidatePosition = 0;
        List<CatalogRecord> candidateRecords = LocBin.Parse(rebuilt, Did).GetCatalogRecords(Did, ref candidatePosition);
        CatalogRecord before = originalRecords.Single(record => record.Source == "\u1848\n\u5DF7");
        CatalogRecord after = candidateRecords.Single(record => record.Key == before.Key);
        Check(bin.UsedFlatFallback && before.Source == after.Source
            && before.RecordFingerprint != after.RecordFingerprint
            && originalRecords.Count == candidateRecords.Count,
            "flat pseudo-anchor fingerprint changes when neighboring raw text changes despite unchanged row text and count");

        string datPath = Path.Combine(directory, "flat-source.dat");
        string recordsPath = Path.Combine(directory, "flat.records.jsonl.gz");
        string metadataPath = Path.Combine(directory, "flat.records.json");
        byte[] dat = BuildDat(payload);
        File.WriteAllBytes(datPath, dat);
        CatalogCacheMetadata metadata = CatalogCache.Build(datPath, recordsPath, metadataPath, null);
        VerifiedCatalogCache snapshot = CatalogCache.OpenVerified(recordsPath, metadataPath,
            Hash(dat), dat.Length, CatalogIdentity.ComputeCatalogHash(originalRecords));
        Check(snapshot.ComputeCandidateHash(null) == metadata.cache_base_catalog_sha256
            && snapshot.BaseCatalogSha256 != CatalogIdentity.ComputeCatalogHash(candidateRecords),
            "verified flat cache base is not a substitute for the rebuilt candidate catalog");
        ExpectProjectionRejected(() => snapshot.ComputeCandidateHash(new Dictionary<string, string> { { changed.Key, changed.Translation } }),
            "overlapping flat anchor requires actual DAT candidate verification instead of an inferred hash");
        return 3;
    }

    private static byte[] BuildOverlappingFlatPayload()
    {
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode, true))
        {
            writer.Write(0);
            writer.Write(Did);
            writer.Write(1);
            writer.Write((byte)0); // Force the flat fallback scanner.
            writer.Write(0x13579BDF2468ACE0L);
            writer.Write(2);
            const string prefix = "Prefix one ";
            writer.Write((byte)prefix.Length);
            writer.Write(Encoding.Unicode.GetBytes(prefix));
            writer.Write((byte)0);
            writer.Write(1);
            writer.Write((byte)3);
            writer.Write((ushort)0x1848);
            writer.Write((ushort)0x000A);
            // The low word completes a plausible false string. The scanner's
            // eight-byte hash for that false record overlaps the prefix tail.
            writer.Write(0x0BA15DF7L);
            writer.Write(1);
            const string suffix = "Suffix";
            writer.Write((byte)suffix.Length);
            writer.Write(Encoding.Unicode.GetBytes(suffix));
            return stream.ToArray();
        }
    }

    private static void ExpectProjectionRejected(Action action, string name)
    {
        try { action(); }
        catch (InvalidDataException ex)
        {
            if (ex.Message.IndexOf("actual DAT candidate", StringComparison.Ordinal) < 0) throw;
            Console.WriteLine("PASS " + name);
            return;
        }
        throw new Exception("FAIL " + name + ": expected a requirement to verify the actual candidate");
    }

    private static void Save(string path, CatalogCacheMetadata metadata)
        => File.WriteAllText(path, Serializer.Serialize(metadata), new UTF8Encoding(false));

    private static string Hash(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static string CatalogHash(byte[] payload)
    {
        long position = 0;
        return CatalogIdentity.ComputeCatalogHash(LocBin.Parse(payload, Did).GetCatalogRecords(Did, ref position));
    }

    private static byte[] BuildPayload(params string[] values)
    {
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode, true))
        {
            writer.Write(0);
            writer.Write(Did);
            writer.Write(1);
            writer.Write((byte)1);
            writer.Write(123456789L);
            writer.Write(values.Length);
            foreach (string value in values)
            {
                writer.Write((byte)value.Length);
                writer.Write(Encoding.Unicode.GetBytes(value));
            }
            writer.Write(0);
            writer.Write((byte)0);
            return stream.ToArray();
        }
    }

    private static byte[] BuildDat(byte[] payload)
    {
        byte[] bytes = new byte[3072];
        using (MemoryStream stream = new MemoryStream(bytes))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            stream.Position = 320;
            writer.Write(TurbineDat.MagicBt);
            writer.Write(1024u);
            writer.Write((uint)bytes.Length);
            for (int i = 0; i < 5; i++) writer.Write(0u);
            writer.Write(1024u);
            stream.Position = 1024 + 504;
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(Did);
            writer.Write(2048u);
            writer.Write((uint)payload.Length);
            writer.Write(0u);
            writer.Write(1u);
            writer.Write((uint)payload.Length);
            writer.Write(0u);
            stream.Position = 2048;
            writer.Write(0u);
            writer.Write(payload);
        }
        return bytes;
    }

    private static byte[] TruncatedBinary()
    {
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("LTCCACHE1"));
            writer.Write(1);
            writer.Write((byte)100); // Claims a key length, with no key bytes.
            return stream.ToArray();
        }
    }

    private static void WriteGzip(string path, byte[] bytes)
    {
        using (FileStream stream = File.Create(path))
        using (GZipStream gzip = new GZipStream(stream, CompressionLevel.Optimal)) gzip.Write(bytes, 0, bytes.Length);
    }

    private static void ExpectInvalid(Action action, string name)
    {
        try { action(); }
        catch (InvalidDataException) { Console.WriteLine("PASS " + name); return; }
        throw new Exception("FAIL " + name + ": expected InvalidDataException");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL " + name);
        Console.WriteLine("PASS " + name);
    }
}
