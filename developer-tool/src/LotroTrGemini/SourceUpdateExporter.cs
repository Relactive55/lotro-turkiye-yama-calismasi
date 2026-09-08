using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace LotroTrGemini;

public sealed class SourceUpdateBundleFile
{
    public string Path { get; internal set; }
    public int Part { get; internal set; }
    public int Parts { get; internal set; }
    public int RecordCount { get; internal set; }
}

public sealed class SourceUpdateExportResult
{
    public string UpdateId { get; internal set; }
    public string UpdatedDatPath { get; internal set; }
    public string StatePath { get; internal set; }
    public CatalogSnapshot UpdatedSnapshot { get; internal set; }
    public CatalogDiffSummary Summary { get; internal set; }
    public int CandidateRecordCount { get; internal set; }
    public int ExcludedRecordCount { get; internal set; }
    public int AmbiguousRecordCount { get; internal set; }
    public bool BaselineInitialized { get; internal set; }
    public string OutputDirectory { get; internal set; }
    public List<SourceUpdateBundleFile> Bundles { get; } = new List<SourceUpdateBundleFile>();
}

/// <summary>
/// Reads a clean English DAT, compares it with the last accepted local
/// snapshot, and emits private source bundles.  It never writes the input DAT
/// and never performs a network request.
/// </summary>
public static class SourceUpdateExporter
{
    private const long MaxBundleBytes = 60L * 1024L * 1024L;
    private const int InitialPartSize = 10000;

    public static SourceUpdateExportResult Create(
        string updatedDatPath,
        string baselineDatPath,
        string statePath,
        string outputDirectory,
        string officialVersion,
        CancellationToken cancellationToken,
        Action<string> progress = null)
    {
        ValidateDatPath(updatedDatPath, "Güncel DAT");
        if (!string.IsNullOrWhiteSpace(baselineDatPath)) ValidateDatPath(baselineDatPath, "Eski temiz DAT");
        if (!string.IsNullOrWhiteSpace(baselineDatPath) && PathsEqual(updatedDatPath, baselineDatPath))
            throw new InvalidOperationException("Güncel DAT ile eski temiz DAT aynı dosya olamaz.");
        if (string.IsNullOrWhiteSpace(statePath)) throw new ArgumentException("statePath");
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("outputDirectory");

        ReadOnlyCatalogExtractor extractor = new ReadOnlyCatalogExtractor();
        progress?.Invoke("Güncel temiz DAT salt okunur taranıyor…");
        CatalogSnapshot updated = extractor.Extract(updatedDatPath, cancellationToken);
        EnsureCleanEnglish(updated);

        CatalogSnapshot baseline;
        if (File.Exists(statePath))
        {
            progress?.Invoke("Önceki katalog durumu doğrulanıyor…");
            baseline = SourceCatalogStateStore.Load(statePath);
        }
        else if (!string.IsNullOrWhiteSpace(baselineDatPath))
        {
            if (!File.Exists(baselineDatPath))
                throw new InvalidOperationException("Eski temiz DAT bulunamadı.");
            progress?.Invoke("İlk temel katalog oluşturuluyor…");
            baseline = extractor.Extract(baselineDatPath, cancellationToken);
            EnsureCleanEnglish(baseline);
        }
        else
        {
            // A single-DAT sender can bootstrap its local state from the first
            // clean English DAT. There is no safe diff to publish yet; after
            // this snapshot is committed, each later DAT is compared with the
            // last successfully submitted catalog.
            progress?.Invoke("İlk temiz DAT temel olarak kaydediliyor…");
            baseline = updated;
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Invoke("Yeni ve değişen metinler karşılaştırılıyor…");
        List<CatalogDiffRecord> diff = CatalogDiff.Compare(baseline.Records, updated.Records);
        CatalogDiffSummary summary = CatalogDiff.Summarize(diff);
        List<CatalogDiffRecord> candidates = new List<CatalogDiffRecord>();
        int excluded = 0;
        foreach (CatalogDiffRecord item in diff)
        {
            CatalogRecord record = item?.NewRecord;
            if (item == null || record == null) continue;
            if (item.Classification != DiffClassification.NEW && item.Classification != DiffClassification.MODIFIED) continue;
            if (CatalogIdentity.IsExcludedFromTranslation(record.Did, record.RecordIndex, record.GroupIndex, record.IndexInGroup))
            {
                excluded++;
                continue;
            }
            candidates.Add(item);
        }

        string updateId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
            + "-" + updated.DatSha256.Substring(0, 12);
        string runDirectory = Path.Combine(Path.GetFullPath(outputDirectory), updateId);
        Directory.CreateDirectory(runDirectory);
        SourceUpdateExportResult result = new SourceUpdateExportResult
        {
            UpdateId = updateId,
            UpdatedDatPath = Path.GetFullPath(updatedDatPath),
            StatePath = Path.GetFullPath(statePath),
            UpdatedSnapshot = updated,
            Summary = summary,
            CandidateRecordCount = candidates.Count,
            ExcludedRecordCount = excluded,
            AmbiguousRecordCount = summary.Ambiguous,
            BaselineInitialized = !File.Exists(statePath) && string.IsNullOrWhiteSpace(baselineDatPath),
            OutputDirectory = runDirectory
        };

        if (candidates.Count > 0)
        {
            int parts = (int)Math.Ceiling(candidates.Count / (double)InitialPartSize);
            int offset = 0;
            int part = 0;
            while (offset < candidates.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                part++;
                int count = Math.Min(InitialPartSize, candidates.Count - offset);
                string serialized = null;
                while (count > 0)
                {
                    List<CatalogDiffRecord> chunk = candidates.GetRange(offset, count);
                    SourceBundleDocument document = SourceBundleBuilder.Build(updated, chunk, includeSourceText: true);
                    document.dat_metadata["source_update_id"] = updateId;
                    document.dat_metadata["part"] = part.ToString(CultureInfo.InvariantCulture);
                    document.dat_metadata["parts"] = parts.ToString(CultureInfo.InvariantCulture);
                    document.dat_metadata["diff_new"] = summary.New.ToString(CultureInfo.InvariantCulture);
                    document.dat_metadata["diff_modified"] = summary.Modified.ToString(CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(officialVersion) && IsVersion(officialVersion))
                        document.dat_metadata["official_game_version"] = officialVersion.Trim();
                    serialized = SourceBundleSerializer.Serialize(document);
                    if (Encoding.UTF8.GetByteCount(serialized) <= MaxBundleBytes || count <= 100) break;
                    count = Math.Max(100, count / 2);
                }
                if (count <= 0 || string.IsNullOrEmpty(serialized)) throw new InvalidDataException("Kaynak paketi oluşturulamadı.");
                string fileName = string.Format(CultureInfo.InvariantCulture, "part-{0:D4}-of-{1:D4}.source-bundle.json", part, parts);
                string path = Path.Combine(runDirectory, fileName);
                File.WriteAllText(path, serialized, new UTF8Encoding(false));
                result.Bundles.Add(new SourceUpdateBundleFile { Path = path, Part = part, Parts = parts, RecordCount = count });
                offset += count;
                progress?.Invoke(string.Format(CultureInfo.InvariantCulture, "Kaynak paketi {0}/{1} hazırlandı ({2:N0} kayıt)…", part, parts, offset));
            }
        }
        else
        {
            progress?.Invoke("Yeni veya değişmiş metin bulunmadı.");
        }

        return result;
    }

    public static void CommitState(SourceUpdateExportResult result)
    {
        if (result == null || result.UpdatedSnapshot == null || string.IsNullOrWhiteSpace(result.StatePath))
            throw new ArgumentException("source update result is invalid", nameof(result));
        SourceCatalogStateStore.Save(result.UpdatedSnapshot, result.StatePath);
    }

    private static void ValidateDatPath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new FileNotFoundException(label + " bulunamadı.", path);
        if (!string.Equals(Path.GetExtension(path), ".dat", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(label + " .dat dosyası olmalıdır.");
        if (!string.Equals(Path.GetFileName(path), "client_local_English.dat", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(label + " dosya adı client_local_English.dat olmalıdır.");
    }

    private static void EnsureCleanEnglish(CatalogSnapshot snapshot)
    {
        int TurkishRows = 0;
        foreach (CatalogRecord record in snapshot.Records)
            if (TranslationQuality.LooksTurkishText(record.Source)) TurkishRows++;
        int threshold = Math.Max(50000, snapshot.Records.Count / 10);
        if (TurkishRows >= threshold)
            throw new InvalidDataException("Seçilen DAT temiz İngilizce kaynak gibi görünmüyor; Türkçe sinyalli kayıt sayısı " + TurkishRows.ToString("N0", CultureInfo.InvariantCulture) + ".");
        // The LOTRO client legitimately contains anchored/flat fallback and
        // empty localization payloads.  They are recorded for review by the
        // extractor but are not, by themselves, evidence of a bad source.
        if (snapshot.ParseErrorCount > 0)
            throw new InvalidDataException("Seçilen DAT katalog doğrulamasından geçmedi; ayrıştırma hatası var.");
    }

    private static bool IsVersion(string value)
    {
        string[] parts = value.Trim().Split('.');
        if (parts.Length != 4) return false;
        foreach (string part in parts) if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)) return false;
        return true;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }
}
