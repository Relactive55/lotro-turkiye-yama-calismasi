using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using LotroTrGemini;

namespace LotroTurkceYama.Setup;

/// <summary>
/// Applies an already validated semantic patch to a private copy of the user's
/// official DAT. The whole apply plan is validated before the candidate file is
/// created; a partial or best-effort write is never accepted.
/// </summary>
public static class ManagedSemanticDatPatcher
{
    public sealed class Result
    {
        public int Applied { get; internal set; }
        public int TouchedDids { get; internal set; }
        public int RecordCount { get; internal set; }
        public string CatalogSha256 { get; internal set; }
        public string DatSha256 { get; internal set; }
        public long DatSize { get; internal set; }
    }

    private sealed class Unit
    {
        public DatEntry Entry;
        public byte[] Raw;
        public byte[] Payload;
        public bool WasCompressed;
        public LocBin Bin;
        public List<LocRow> Rows;
    }

    public static Result BuildCandidate(
        string sourcePath,
        string candidatePath,
        SemanticPatchDocument patch,
        string expectedCandidateCatalogSha256,
        CancellationToken cancellationToken,
        Action<string> progress = null)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Semantic patch kaynak DAT bulunamadı.", sourcePath);
        if (string.IsNullOrWhiteSpace(candidatePath)) throw new ArgumentException("candidatePath");
        SemanticPatchValidator.EnsureValid(patch);
        progress = progress ?? delegate { };

        FileInfo sourceInfo = new FileInfo(sourcePath);
        if (sourceInfo.Length != patch.source_dat_size
            || !string.Equals(HashFile(sourcePath), patch.source_dat_sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("SEMANTIC_PATCH_BASELINE_MISMATCH", "Kaynak DAT boyutu veya SHA-256 değeri semantic patch ile eşleşmiyor.");

        progress("Kaynak katalog doğrulanıyor...");
        Dictionary<int, Unit> units = new Dictionary<int, Unit>();
        List<CatalogRecord> records = new List<CatalogRecord>();
        List<LocRow> rows = new List<LocRow>();
        long position = 0;
        using (TurbineDat dat = new TurbineDat())
        {
            dat.Open(sourcePath, false);
            foreach (DatEntry entry in dat.ListLocalization())
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] raw = dat.ReadRaw(entry);
                bool compressed = TurbineDat.LooksCompressed(raw);
                byte[] payload = TurbineDat.MaybeDecompress(raw);
                if (compressed && ReferenceEquals(raw, payload))
                    throw new InvalidDataException("Sıkıştırılmış localization alt dosyası açılamadı: 0x" + entry.Id.ToString("X8"));
                LocBin bin = LocBin.Parse(payload, entry.Id);
                List<LocRow> unitRows = bin.GetRows(entry.Id);
                if (unitRows.Count == 0)
                {
                    // The official DAT contains a small number of valid, empty
                    // localization containers. The read-only catalog extractor
                    // excludes these from the catalog as well, so preserve them
                    // byte-for-byte and continue.
                    continue;
                }
                List<CatalogRecord> unitRecords = bin.GetCatalogRecords(entry.Id, ref position);
                if (unitRecords.Count != unitRows.Count)
                    throw new InvalidDataException("Localization katalog/satır sayısı uyuşmuyor: 0x" + entry.Id.ToString("X8"));
                units.Add(entry.Id, new Unit
                {
                    Entry = entry,
                    Raw = raw,
                    Payload = payload,
                    WasCompressed = compressed,
                    Bin = bin,
                    Rows = unitRows
                });
                rows.AddRange(unitRows);
                records.AddRange(unitRecords);
            }
        }

        string sourceCatalogHash = CatalogIdentity.ComputeCatalogHash(records);
        progress("Kaynak katalog: " + records.Count + " kayıt, SHA-256 " + sourceCatalogHash);
        if (!string.Equals(sourceCatalogHash, patch.source_catalog_sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("SEMANTIC_PATCH_CATALOG_MISMATCH", "Kaynak DAT katalog kimliği semantic patch ile eşleşmiyor.");

        SemanticPatchApplyResult apply = SemanticPatchApplier.ApplyToRows(patch, records, rows, patch.source_dat_sha256);
        if (apply.BaselineMismatch || apply.Applied != patch.entries.Count || apply.SourceChanged != 0
            || apply.Ambiguous != 0 || apply.CriticalSkipped != 0 || apply.Rejected != 0 || apply.Missing != 0)
        {
            throw new UpdaterFailure(
                "SEMANTIC_PATCH_APPLY_REJECTED",
                "Semantic patch eksiksiz uygulanamadı; DAT değiştirilmedi. "
                + "applied=" + apply.Applied + "/" + patch.entries.Count
                + ", changed=" + apply.SourceChanged + ", ambiguous=" + apply.Ambiguous
                + ", critical=" + apply.CriticalSkipped + ", rejected=" + apply.Rejected
                + ", missing=" + apply.Missing);
        }

        List<Unit> touched = units.Values
            .Where(unit => unit.Rows.Any(row => !string.Equals(row.Original, row.Translation, StringComparison.Ordinal)))
            .OrderBy(unit => unchecked((uint)unit.Entry.Id))
            .ToList();
        if (touched.Count == 0 || patch.entries.Count == 0)
            throw new UpdaterFailure("SEMANTIC_PATCH_EMPTY", "Semantic patch uygulanabilir değişiklik içermiyor.");

        TryDelete(candidatePath);
        CopyFile(sourcePath, candidatePath, cancellationToken);
        try
        {
            progress("Türkçe satırlar DAT adayına yazılıyor...");
            using (TurbineDat candidate = new TurbineDat())
            {
                candidate.Open(candidatePath, true);
                candidate.BuildEntryIndex();
                foreach (Unit unit in touched)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] blob = BuildVerifiedBlob(unit);
                    if (!candidate.TryGetEntry(unit.Entry.Id, out DatEntry current) || current == null)
                        throw new InvalidDataException("Aday DAT girdisi bulunamadı: 0x" + unit.Entry.Id.ToString("X8"));
                    int capacity = candidate.MeasureCapacity(current.Offset);
                    if (blob.Length > capacity && !candidate.ExpandChain(current.Offset, blob.Length))
                        throw new IOException("Aday DAT blok zinciri genişletilemedi: 0x" + unit.Entry.Id.ToString("X8"));
                    candidate.WriteChain(current.Offset, blob);
                    if (!candidate.UpdateEntrySize(unit.Entry.Id, checked((uint)blob.Length)))
                        throw new IOException("Aday DAT entry boyutu güncellenemedi: 0x" + unit.Entry.Id.ToString("X8"));
                }
            }

            progress("Aday DAT baştan sona yeniden doğrulanıyor...");
            List<CatalogRecord> candidateRecords = ExtractCatalog(candidatePath, cancellationToken);
            if (candidateRecords.Count != records.Count)
                throw new InvalidDataException("Aday DAT satır sayısı değişti: " + candidateRecords.Count + "/" + records.Count);
            string candidateCatalogHash = CatalogIdentity.ComputeCatalogHash(candidateRecords);
            if (!string.IsNullOrWhiteSpace(expectedCandidateCatalogSha256)
                && !string.Equals(candidateCatalogHash, expectedCandidateCatalogSha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdaterFailure("CANDIDATE_CATALOG_MISMATCH", "Aday DAT katalog SHA-256 değeri manifest ile eşleşmiyor.");

            Dictionary<string, CatalogRecord> candidateByKey = candidateRecords.ToDictionary(item => item.Key, StringComparer.Ordinal);
            foreach (SemanticPatchEntry entry in patch.entries)
            {
                if (!candidateByKey.TryGetValue(entry.dat_key, out CatalogRecord record)
                    || !string.Equals(record.Source, CatalogIdentity.NormalizeSource(entry.target), StringComparison.Ordinal))
                    throw new UpdaterFailure("CANDIDATE_TARGET_MISMATCH", "Aday DAT hedef satır doğrulaması başarısız: " + entry.dat_key);
            }

            FileInfo resultInfo = new FileInfo(candidatePath);
            return new Result
            {
                Applied = apply.Applied,
                TouchedDids = touched.Count,
                RecordCount = candidateRecords.Count,
                CatalogSha256 = candidateCatalogHash,
                DatSha256 = HashFile(candidatePath),
                DatSize = resultInfo.Length
            };
        }
        catch
        {
            TryDelete(candidatePath);
            throw;
        }
    }

    private static byte[] BuildVerifiedBlob(Unit unit)
    {
        string[] translations = unit.Rows.Select(row => row.Translation).ToArray();
        foreach (LocRow row in unit.Rows) row.Translation = row.Original;
        byte[] identity = unit.Bin.Rebuild(unit.Rows);
        if (!BytesEqual(identity, unit.Payload))
            throw new InvalidDataException("Localization identity rebuild başarısız: 0x" + unit.Entry.Id.ToString("X8"));
        for (int i = 0; i < unit.Rows.Count; i++) unit.Rows[i].Translation = translations[i];
        byte[] translated = unit.Bin.Rebuild(unit.Rows);
        long growthLimit = Math.Max((long)unit.Raw.Length * 4L, (long)unit.Raw.Length + 16L * 1024 * 1024);
        byte[] preferred = TurbineDat.PackBlob(translated, unit.WasCompressed, 0);
        byte[] alternate = TurbineDat.PackBlob(translated, !unit.WasCompressed, 0);
        byte[] blob = alternate.Length < preferred.Length ? alternate : preferred;
        if (blob.LongLength > growthLimit)
            throw new InvalidDataException("Localization alt dosyası güvenli büyüme sınırını aştı: 0x" + unit.Entry.Id.ToString("X8"));
        byte[] verifyPayload = TurbineDat.MaybeDecompress(blob);
        List<LocRow> verifyRows = LocBin.Parse(verifyPayload, unit.Entry.Id).GetRows(unit.Entry.Id);
        if (verifyRows.Count != unit.Rows.Count)
            throw new InvalidDataException("Localization rebuild satır kaybı: 0x" + unit.Entry.Id.ToString("X8"));
        for (int i = 0; i < unit.Rows.Count; i++)
        {
            if (!string.Equals(verifyRows[i].Key, unit.Rows[i].Key, StringComparison.Ordinal)
                || !string.Equals(verifyRows[i].Original, unit.Rows[i].Translation, StringComparison.Ordinal))
                throw new InvalidDataException("Localization rebuild hedef uyuşmazlığı: 0x" + unit.Entry.Id.ToString("X8"));
        }
        return blob;
    }

    private static List<CatalogRecord> ExtractCatalog(string path, CancellationToken cancellationToken)
    {
        List<CatalogRecord> records = new List<CatalogRecord>();
        long position = 0;
        using (TurbineDat dat = new TurbineDat())
        {
            dat.Open(path, false);
            foreach (DatEntry entry in dat.ListLocalization())
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] raw = dat.ReadRaw(entry);
                byte[] payload = TurbineDat.MaybeDecompress(raw);
                if (TurbineDat.LooksCompressed(raw) && ReferenceEquals(raw, payload))
                    throw new InvalidDataException("Aday localization alt dosyası açılamadı: 0x" + entry.Id.ToString("X8"));
                LocBin bin = LocBin.Parse(payload, entry.Id);
                List<CatalogRecord> current = bin.GetCatalogRecords(entry.Id, ref position);
                if (current.Count == 0)
                    continue;
                records.AddRange(current);
            }
        }
        return records;
    }

    private static void CopyFile(string source, string destination, CancellationToken cancellationToken)
    {
        using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024, FileOptions.SequentialScan))
        using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4 * 1024 * 1024, FileOptions.SequentialScan))
        {
            byte[] buffer = new byte[4 * 1024 * 1024];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
            }
            output.Flush(true);
        }
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

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null || left.Length != right.Length) return false;
        int diff = 0;
        for (int i = 0; i < left.Length; i++) diff |= left[i] ^ right[i];
        return diff == 0;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
