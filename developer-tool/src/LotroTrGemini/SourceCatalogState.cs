using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace LotroTrGemini;

/// <summary>
/// Private local catalog state used by the source-update sender.  It keeps a
/// verified snapshot between game updates without copying the proprietary DAT
/// into the repository.  The state is never used as a DAT input.
/// </summary>
public static class SourceCatalogStateStore
{
    public const int SchemaVersion = 1;
    private const string StateKind = "lotro_source_catalog_state";
    private const int MaxRecords = 2000000;

    private sealed class Header
    {
        public int schema_version { get; set; }
        public string state_kind { get; set; }
        public string source_dat_sha256 { get; set; }
        public long source_dat_size { get; set; }
        public string source_catalog_sha256 { get; set; }
        public int block_size { get; set; }
        public int vnum_dat_file { get; set; }
        public int vnum_game_data { get; set; }
        public uint dat_file_id { get; set; }
        public string dat_stamp { get; set; }
        public string first_iteration_guid { get; set; }
        public int localization_did_count { get; set; }
        public int structured_payload_count { get; set; }
        public int fallback_payload_count { get; set; }
        public int empty_payload_count { get; set; }
        public int parse_error_count { get; set; }
        public long record_count { get; set; }
    }

    private sealed class StateRecord
    {
        public long p { get; set; }
        public int d { get; set; }
        public int ri { get; set; }
        public int gi { get; set; }
        public int ii { get; set; }
        public string rf { get; set; }
        public string st { get; set; }
        public string s { get; set; }
        public string sf { get; set; }
        public string cf { get; set; }
        public string ts { get; set; }
        public string sd { get; set; }
        public bool c { get; set; }
    }

    public static CatalogSnapshot Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Kaynak katalog durumu bulunamadı.", path);

        JavaScriptSerializer serializer = Serializer();
        Header header;
        CatalogSnapshot snapshot;
        using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: false))
        using (StreamReader reader = new StreamReader(gzip, new UTF8Encoding(false), false, 1024 * 1024))
        {
            string first = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(first)) throw new InvalidDataException("Kaynak katalog durumu başlığı eksik.");
            try { header = serializer.Deserialize<Header>(first); }
            catch (Exception ex) { throw new InvalidDataException("Kaynak katalog durumu başlığı geçersiz.", ex); }
            ValidateHeader(header);
            snapshot = new CatalogSnapshot
            {
                DatSize = header.source_dat_size,
                DatSha256 = header.source_dat_sha256.ToLowerInvariant(),
                BlockSize = header.block_size,
                VnumDatFile = header.vnum_dat_file,
                VnumGameData = header.vnum_game_data,
                DatFileId = header.dat_file_id,
                DatIdStamp = header.dat_stamp,
                FirstIterationGuid = header.first_iteration_guid,
                LocalizationDidCount = header.localization_did_count,
                StructuredPayloadCount = header.structured_payload_count,
                FallbackPayloadCount = header.fallback_payload_count,
                EmptyPayloadCount = header.empty_payload_count,
                ParseErrorCount = header.parse_error_count
            };

            long position = 0;
            HashSet<string> identities = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                if (snapshot.Records.Count >= MaxRecords) throw new InvalidDataException("Kaynak katalog durumu çok büyük.");
                StateRecord state;
                try { state = serializer.Deserialize<StateRecord>(line); }
                catch (Exception ex) { throw new InvalidDataException("Kaynak katalog kaydı geçersiz.", ex); }
                CatalogRecord record = Restore(state, position);
                if (!identities.Add(record.EntryIdentity) || !keys.Add(record.Key))
                    throw new InvalidDataException("Kaynak katalog tekrarlı kimlik içeriyor.");
                snapshot.Records.Add(record);
                position++;
            }
            if (snapshot.Records.Count != header.record_count)
                throw new InvalidDataException("Kaynak katalog kaydı sayısı başlıkla eşleşmiyor.");
        }

        string actual = CatalogIdentity.ComputeCatalogHash(snapshot.Records);
        if (!SourceDigest.Matches(actual, snapshot.CatalogSha256))
            throw new InvalidDataException("Kaynak katalog SHA-256 doğrulaması başarısız.");
        return snapshot;
    }

    public static void Save(CatalogSnapshot snapshot, string path)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (snapshot.Records == null || snapshot.Records.Count == 0) throw new InvalidDataException("Boş katalog durumu kaydedilemez.");
        if (!SourceDigest.IsValid(snapshot.DatSha256) || snapshot.DatSize < 1 || !SourceDigest.IsValid(snapshot.CatalogSha256))
            throw new InvalidDataException("Katalog durumu kimliği geçersiz.");
        if (snapshot.Records.Count > MaxRecords) throw new InvalidDataException("Katalog durumu çok büyük.");
        string fullPath = Path.GetFullPath(path);
        string parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        JavaScriptSerializer serializer = Serializer();
        try
        {
            Header header = new Header
            {
                schema_version = SchemaVersion,
                state_kind = StateKind,
                source_dat_sha256 = snapshot.DatSha256.ToLowerInvariant(),
                source_dat_size = snapshot.DatSize,
                source_catalog_sha256 = snapshot.CatalogSha256.ToLowerInvariant(),
                block_size = snapshot.BlockSize,
                vnum_dat_file = snapshot.VnumDatFile,
                vnum_game_data = snapshot.VnumGameData,
                dat_file_id = snapshot.DatFileId,
                dat_stamp = snapshot.DatIdStamp,
                first_iteration_guid = snapshot.FirstIterationGuid,
                localization_did_count = snapshot.LocalizationDidCount,
                structured_payload_count = snapshot.StructuredPayloadCount,
                fallback_payload_count = snapshot.FallbackPayloadCount,
                empty_payload_count = snapshot.EmptyPayloadCount,
                parse_error_count = snapshot.ParseErrorCount,
                record_count = snapshot.Records.Count
            };
            using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
            using (GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: false))
            using (StreamWriter writer = new StreamWriter(gzip, new UTF8Encoding(false), 1024 * 1024))
            {
                writer.WriteLine(serializer.Serialize(header));
                foreach (CatalogRecord record in snapshot.Records.OrderBy(item => item.Position))
                {
                    writer.WriteLine(serializer.Serialize(new StateRecord
                    {
                        p = record.Position,
                        d = record.Did,
                        ri = record.RecordIndex,
                        gi = record.GroupIndex,
                        ii = record.IndexInGroup,
                        rf = record.RecordFingerprint ?? string.Empty,
                        st = record.StructuralFingerprint ?? string.Empty,
                        s = record.Source ?? string.Empty,
                        sf = record.SourceFingerprint ?? string.Empty,
                        cf = record.ContextFingerprint ?? string.Empty,
                        ts = record.TokenSignature ?? string.Empty,
                        sd = record.SourceDigest ?? string.Empty,
                        c = record.CriticalUi
                    }));
                }
                writer.Flush();
            }
            if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
            else File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch { }
            }
        }
    }

    private static CatalogRecord Restore(StateRecord state, long expectedPosition)
    {
        if (state == null || state.p != expectedPosition || state.d < 0 || state.ri < 0 || state.gi < -1 || state.ii < 0
            || state.s == null || !SourceDigest.IsValid(state.rf) || !SourceDigest.IsValid(state.st)
            || !SourceDigest.IsValid(state.sf) || !SourceDigest.IsValid(state.cf) || !SourceDigest.IsValid(state.ts)
            || !SourceDigest.IsValid(state.sd))
            throw new InvalidDataException("Kaynak katalog kaydı kimliği veya sırası geçersiz.");
        bool critical = CatalogIdentity.IsCriticalUiDid(state.d);
        if (critical != state.c) throw new InvalidDataException("Kaynak katalog kritik UI kimliği değişmiş.");
        CatalogRecord record = new CatalogRecord
        {
            Position = state.p,
            Did = state.d,
            RecordIndex = state.ri,
            GroupIndex = state.gi,
            IndexInGroup = state.ii,
            RecordFingerprint = state.rf.ToLowerInvariant(),
            StructuralFingerprint = state.st.ToLowerInvariant(),
            Source = CatalogIdentity.NormalizeSource(state.s),
            SourceFingerprint = state.sf.ToLowerInvariant(),
            ContextFingerprint = state.cf.ToLowerInvariant(),
            TokenSignature = state.ts.ToLowerInvariant(),
            SourceDigest = state.sd.ToLowerInvariant(),
            CriticalUi = critical
        };
        if (!SourceDigest.Matches(record.SourceDigest, SourceDigest.ForRecord(record)))
            throw new InvalidDataException("Kaynak katalog source digest doğrulaması başarısız.");
        if (!string.Equals(record.TokenSignature, ProtectedFormat.GetTokenSignature(record.Source), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Kaynak katalog token imzası doğrulaması başarısız.");
        return record;
    }

    private static void ValidateHeader(Header header)
    {
        if (header == null || header.schema_version != SchemaVersion || header.state_kind != StateKind
            || !SourceDigest.IsValid(header.source_dat_sha256) || header.source_dat_size < 1
            || !SourceDigest.IsValid(header.source_catalog_sha256) || header.record_count < 1 || header.record_count > MaxRecords)
            throw new InvalidDataException("Kaynak katalog durumu şeması veya kimliği geçersiz.");
    }

    private static JavaScriptSerializer Serializer()
    {
        return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
    }
}
