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

    public sealed class RecoveryResult
    {
        public Result CleanSource { get; internal set; }
        public Result Translated { get; internal set; }
    }

    private sealed class Unit
    {
        public DatEntry Entry;
        public byte[] Raw;
        public byte[] Payload;
        public bool WasCompressed;
        public LocBin Bin;
        public List<LocRow> Rows;
        public HashSet<string> ChangedKeys = new HashSet<string>(StringComparer.Ordinal);
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

        MarkChangedUnits(units, patch.entries.Select(entry => entry.dat_key));

        List<Unit> touched = units.Values
            .Where(unit => unit.ChangedKeys.Count != 0)
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

    /// <summary>
    /// Recovers from the normal live-service sequence where the official launcher
    /// updates a DAT that was translated by an earlier release.  Only rows whose
    /// new English identity is proven by either the updated DAT or the prior clean
    /// backup are changed.  Both resulting catalogs must match the release hashes
    /// exactly, otherwise neither file is accepted.
    /// </summary>
    public static RecoveryResult RecoverUpdatedPatchedDat(
        string updatedPatchedPath,
        string priorCleanPath,
        string cleanCandidatePath,
        string translatedCandidatePath,
        SemanticPatchDocument patch,
        string expectedCandidateCatalogSha256,
        CancellationToken cancellationToken,
        Action<string> progress = null)
    {
        if (string.IsNullOrWhiteSpace(updatedPatchedPath) || !File.Exists(updatedPatchedPath))
            throw new FileNotFoundException("Güncellenmiş LOTRO DAT bulunamadı.", updatedPatchedPath);
        if (string.IsNullOrWhiteSpace(priorCleanPath) || !File.Exists(priorCleanPath))
            throw new FileNotFoundException("Önceki temiz LOTRO DAT yedeği bulunamadı.", priorCleanPath);
        SemanticPatchValidator.EnsureValid(patch);
        progress = progress ?? delegate { };

        progress("Oyun güncellemesinden kalan DAT güvenli biçimde çözümleniyor...");
        Dictionary<int, Unit> units;
        List<CatalogRecord> currentRecords;
        List<LocRow> currentRows;
        LoadDat(updatedPatchedPath, cancellationToken, out units, out currentRecords, out currentRows);
        List<CatalogRecord> priorRecords = ExtractCatalog(priorCleanPath, cancellationToken);

        Dictionary<string, CatalogRecord> currentByKey = UniqueByKey(currentRecords);
        Dictionary<string, List<CatalogRecord>> currentByIdentity = GroupByIdentity(currentRecords);
        Dictionary<string, CatalogRecord> priorByKey = UniqueByKey(priorRecords);
        Dictionary<string, List<CatalogRecord>> priorByIdentity = GroupByIdentity(priorRecords);
        Dictionary<string, LocRow> rowsByKey = currentRows.ToDictionary(item => item.Key, StringComparer.Ordinal);
        Dictionary<string, LocRow> resolvedRows = new Dictionary<string, LocRow>(StringComparer.Ordinal);

        foreach (SemanticPatchEntry entry in patch.entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CatalogRecord current = ResolveRecord(entry, currentByKey, currentByIdentity);
            if (current == null || !rowsByKey.TryGetValue(current.Key, out LocRow row))
                throw new UpdaterFailure("UPDATED_DAT_RECOVERY_REJECTED", "Güncellenmiş DAT içinde beklenen metin satırı bulunamadı: " + entry.dat_key);

            string provenSource = null;
            if (SourceDigest.Matches(current.SourceDigest, entry.source_digest))
            {
                provenSource = current.Source;
            }
            else
            {
                CatalogRecord prior = ResolveRecord(entry, priorByKey, priorByIdentity);
                if (prior != null && SourceDigest.Matches(prior.SourceDigest, entry.source_digest))
                    provenSource = prior.Source;
            }
            if (provenSource == null
                || !string.Equals(ProtectedFormat.GetTokenSignature(provenSource), entry.token_signature, StringComparison.OrdinalIgnoreCase))
                throw new UpdaterFailure("UPDATED_DAT_RECOVERY_REJECTED", "Metnin yeni resmî İngilizce kaynağı doğrulanamadı: " + entry.dat_key);
            ProtectedFormatResult format = ProtectedFormat.Validate(provenSource, entry.target);
            if (!format.IsValid)
                throw new UpdaterFailure("UPDATED_DAT_RECOVERY_REJECTED", "Metin biçimi doğrulanamadı: " + entry.dat_key + " (" + format.Reason + ")");

            row.Translation = provenSource;
            units[current.Did].ChangedKeys.Add(row.Key);
            resolvedRows.Add(entry.dat_key, row);
        }

        TryDelete(cleanCandidatePath);
        TryDelete(translatedCandidatePath);
        try
        {
            Result clean = WriteAndVerifyCandidate(
                updatedPatchedPath,
                cleanCandidatePath,
                units,
                currentRecords.Count,
                patch.source_catalog_sha256,
                cancellationToken,
                0,
                null);

            foreach (SemanticPatchEntry entry in patch.entries)
                resolvedRows[entry.dat_key].Translation = entry.target;

            Result translated = WriteAndVerifyCandidate(
                updatedPatchedPath,
                translatedCandidatePath,
                units,
                currentRecords.Count,
                expectedCandidateCatalogSha256,
                cancellationToken,
                patch.entries.Count,
                patch.entries);
            progress("Oyun güncellemesi güvenle birleştirildi ve Türkçe DAT doğrulandı.");
            return new RecoveryResult { CleanSource = clean, Translated = translated };
        }
        catch
        {
            TryDelete(cleanCandidatePath);
            TryDelete(translatedCandidatePath);
            throw;
        }
    }

    private static void LoadDat(
        string path,
        CancellationToken cancellationToken,
        out Dictionary<int, Unit> units,
        out List<CatalogRecord> records,
        out List<LocRow> rows)
    {
        units = new Dictionary<int, Unit>();
        records = new List<CatalogRecord>();
        rows = new List<LocRow>();
        long position = 0;
        using (TurbineDat dat = new TurbineDat())
        {
            dat.Open(path, false);
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
                if (unitRows.Count == 0) continue;
                List<CatalogRecord> unitRecords = bin.GetCatalogRecords(entry.Id, ref position);
                if (unitRecords.Count != unitRows.Count)
                    throw new InvalidDataException("Localization katalog/satır sayısı uyuşmuyor: 0x" + entry.Id.ToString("X8"));
                units.Add(entry.Id, new Unit { Entry = entry, Raw = raw, Payload = payload, WasCompressed = compressed, Bin = bin, Rows = unitRows });
                rows.AddRange(unitRows);
                records.AddRange(unitRecords);
            }
        }
    }

    private static Result WriteAndVerifyCandidate(
        string basePath,
        string candidatePath,
        Dictionary<int, Unit> units,
        int expectedRecordCount,
        string expectedCatalogSha256,
        CancellationToken cancellationToken,
        int applied,
        IList<SemanticPatchEntry> expectedTargets)
    {
        List<Unit> touched = units.Values
            .Where(unit => unit.ChangedKeys.Count != 0)
            .OrderBy(unit => unchecked((uint)unit.Entry.Id))
            .ToList();
        CopyFile(basePath, candidatePath, cancellationToken);
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
        List<CatalogRecord> candidateRecords = ExtractCatalog(candidatePath, cancellationToken);
        if (candidateRecords.Count != expectedRecordCount)
            throw new InvalidDataException("Aday DAT satır sayısı değişti: " + candidateRecords.Count + "/" + expectedRecordCount);
        string catalogHash = CatalogIdentity.ComputeCatalogHash(candidateRecords);
        if (!string.Equals(catalogHash, expectedCatalogSha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdaterFailure("CANDIDATE_CATALOG_MISMATCH", "Aday DAT katalog SHA-256 değeri beklenen kimlikle eşleşmiyor.");
        if (expectedTargets != null)
        {
            Dictionary<string, CatalogRecord> byKey = UniqueByKey(candidateRecords);
            Dictionary<string, List<CatalogRecord>> byIdentity = GroupByIdentity(candidateRecords);
            foreach (SemanticPatchEntry entry in expectedTargets)
            {
                CatalogRecord record = ResolveRecord(entry, byKey, byIdentity);
                if (record == null || !string.Equals(record.Source, CatalogIdentity.NormalizeSource(entry.target), StringComparison.Ordinal))
                    throw new UpdaterFailure("CANDIDATE_TARGET_MISMATCH", "Kurtarılan DAT hedef satır doğrulaması başarısız: " + entry.dat_key);
            }
        }
        FileInfo info = new FileInfo(candidatePath);
        return new Result
        {
            Applied = applied,
            TouchedDids = touched.Count,
            RecordCount = candidateRecords.Count,
            CatalogSha256 = catalogHash,
            DatSha256 = HashFile(candidatePath),
            DatSize = info.Length
        };
    }

    private static Dictionary<string, CatalogRecord> UniqueByKey(IEnumerable<CatalogRecord> records)
    {
        Dictionary<string, CatalogRecord> result = new Dictionary<string, CatalogRecord>(StringComparer.Ordinal);
        foreach (CatalogRecord record in records)
            if (!result.ContainsKey(record.Key)) result.Add(record.Key, record);
        return result;
    }

    private static Dictionary<string, List<CatalogRecord>> GroupByIdentity(IEnumerable<CatalogRecord> records)
    {
        Dictionary<string, List<CatalogRecord>> result = new Dictionary<string, List<CatalogRecord>>(StringComparer.Ordinal);
        foreach (CatalogRecord record in records)
        {
            if (!result.TryGetValue(record.EntryIdentity, out List<CatalogRecord> list))
            {
                list = new List<CatalogRecord>();
                result.Add(record.EntryIdentity, list);
            }
            list.Add(record);
        }
        return result;
    }

    private static CatalogRecord ResolveRecord(
        SemanticPatchEntry entry,
        Dictionary<string, CatalogRecord> byKey,
        Dictionary<string, List<CatalogRecord>> byIdentity)
    {
        if (byKey.TryGetValue(entry.dat_key, out CatalogRecord exact)) return exact;
        if (byIdentity.TryGetValue(entry.entry_identity, out List<CatalogRecord> matches) && matches.Count == 1) return matches[0];
        return null;
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
        // The LOTRO client is stricter than the managed round-trip parser. Keep
        // every localization subfile in its original storage representation;
        // changing compressed data to raw (or raw to compressed) can produce a
        // parseable DAT that the game still refuses to load.
        byte[] blob = TurbineDat.PackBlob(translated, unit.WasCompressed, 0);
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

    private static void MarkChangedUnits(Dictionary<int, Unit> units, IEnumerable<string> keys)
    {
        foreach (string key in keys ?? Enumerable.Empty<string>())
        {
            int separator = key == null ? -1 : key.IndexOf(':');
            if (separator != 8 || !int.TryParse(key.Substring(0, 8), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int did))
                throw new UpdaterFailure("SEMANTIC_PATCH_INVALID", "DAT satır anahtarı geçersiz: " + key);
            if (!units.TryGetValue(did, out Unit unit))
                throw new UpdaterFailure("SEMANTIC_PATCH_APPLY_REJECTED", "Değiştirilecek localization alt dosyası bulunamadı: " + key);
            unit.ChangedKeys.Add(key);
        }
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
