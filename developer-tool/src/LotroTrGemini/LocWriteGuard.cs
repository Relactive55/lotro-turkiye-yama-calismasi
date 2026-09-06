using System;
using System.Collections.Generic;
using System.IO;

namespace LotroTrGemini;

public static class LocWriteGuard
{
	public sealed class CriticalMigrationResult
	{
		public readonly HashSet<int> Transplanted = new HashSet<int>();

		public int Compatible;

		public int Skipped;

		public int Failed;
	}

	public sealed class Result
	{
		public byte[] Blob;
		public string Reason;
		public int Compacted;
	}

	public sealed class AllowedCriticalRule
	{
		public string Source;

		public string Target;
	}

	public static Result TryBuild(LocBin bin, IList<LocRow> rows, byte[] originalRaw, bool wasCompressed)
	{
		Result result = new Result();
		if (bin == null || rows == null || originalRaw == null || originalRaw.Length == 0)
		{
			result.Reason = "geçersiz";
			return result;
		}
		byte[] originalPayload = wasCompressed ? TurbineDat.MaybeDecompress(originalRaw) : originalRaw;
		if (originalPayload == null || originalPayload.Length == 0)
		{
			result.Reason = "payload yok";
			return result;
		}
		string[] snapshot = new string[rows.Count];
		for (int i = 0; i < rows.Count; i++)
		{
			snapshot[i] = rows[i].Translation;
		}
		try
		{
			for (int i = 0; i < rows.Count; i++)
			{
				rows[i].Translation = rows[i].Original;
			}
			byte[] identity;
			try
			{
				identity = bin.Rebuild(rows);
			}
			catch (Exception ex)
			{
				result.Reason = "id rebuild: " + ex.Message;
				Restore(rows, snapshot);
				return result;
			}
			if (!BytesEqual(identity, originalPayload))
			{
				result.Reason = "identity mismatch (format güvenli değil)";
				Restore(rows, snapshot);
				return result;
			}
			for (int i = 0; i < rows.Count; i++)
			{
				string original = rows[i].Original ?? "";
				string translation = snapshot[i] ?? original;
				rows[i].Translation = string.Equals(original, translation, StringComparison.Ordinal)
					? original
					: TextGuard.Sanitize(original, translation, allowCompact: false);
			}
			byte[] translatedPayload;
			try
			{
				translatedPayload = bin.Rebuild(rows);
			}
			catch (Exception ex)
			{
				result.Reason = "tr rebuild: " + ex.Message;
				Restore(rows, snapshot);
				return result;
			}
			int did = rows.Count > 0 ? rows[0].Did : bin.FileId;
			string verifyReason;
			byte[] packed = TurbineDat.FitBlob(translatedPayload, originalRaw, wasCompressed);
			if (packed == null)
			{
				byte[] preferred = TurbineDat.PackBlob(translatedPayload, wasCompressed, 0);
				byte[] alternate = TurbineDat.PackBlob(translatedPayload, !wasCompressed, 0);
				packed = alternate.Length < preferred.Length ? alternate : preferred;
				long growthLimit = Math.Max((long)originalRaw.Length * 4L, (long)originalRaw.Length + 16777216L);
				if (packed.LongLength > growthLimit)
				{
					result.Reason = "güvensiz alt dosya boyut artışı";
					Restore(rows, snapshot);
					return result;
				}
			}
			byte[] packedPayload = TurbineDat.LooksCompressed(packed) ? TurbineDat.MaybeDecompress(packed) : packed;
			if (!VerifyRebuiltRows(packedPayload, did, rows, out verifyReason))
			{
				result.Reason = "paket " + verifyReason;
				Restore(rows, snapshot);
				return result;
			}
			if (BytesEqual(packed, originalRaw))
			{
				result.Reason = "aynı";
				return result;
			}
			result.Blob = packed;
			result.Reason = "ok";
			return result;
		}
		catch (Exception ex)
		{
			result.Reason = ex.Message;
			Restore(rows, snapshot);
			return result;
		}
	}

	public static Result TryBuildUncompressed(LocBin bin, IList<LocRow> rows, byte[] originalPayload)
	{
		Result result = new Result();
		if (bin == null || rows == null || originalPayload == null || originalPayload.Length == 0)
		{
			result.Reason = "geçersiz";
			return result;
		}
		string[] snapshot = new string[rows.Count];
		for (int i = 0; i < rows.Count; i++) snapshot[i] = rows[i].Translation;
		try
		{
			for (int i = 0; i < rows.Count; i++) rows[i].Translation = rows[i].Original;
			byte[] identity = bin.Rebuild(rows);
			if (!BytesEqual(identity, originalPayload))
			{
				result.Reason = "identity mismatch (format güvenli değil)";
				Restore(rows, snapshot);
				return result;
			}
			for (int i = 0; i < rows.Count; i++)
			{
				string original = rows[i].Original ?? "";
				string translation = snapshot[i] ?? original;
				rows[i].Translation = string.Equals(original, translation, StringComparison.Ordinal)
					? original
					: TextGuard.Sanitize(original, translation, allowCompact: false);
			}
			byte[] translated = bin.Rebuild(rows);
			long growthLimit = Math.Max((long)originalPayload.Length * 4L, (long)originalPayload.Length + 16777216L);
			if (translated.LongLength > growthLimit)
			{
				result.Reason = "güvensiz alt dosya boyut artışı";
				Restore(rows, snapshot);
				return result;
			}
			int did = rows.Count > 0 ? rows[0].Did : bin.FileId;
			string verifyReason;
			if (!VerifyRebuiltRows(translated, did, rows, out verifyReason))
			{
				result.Reason = "payload " + verifyReason;
				Restore(rows, snapshot);
				return result;
			}
			if (BytesEqual(translated, originalPayload))
			{
				result.Reason = "aynı";
				return result;
			}
			result.Blob = translated;
			result.Reason = "ok";
			return result;
		}
		catch (Exception ex)
		{
			result.Reason = ex.Message;
			Restore(rows, snapshot);
			return result;
		}
	}

	private static bool VerifyRebuiltRows(byte[] payload, int did, IList<LocRow> expected, out string reason)
	{
		reason = null;
		try
		{
			List<LocRow> actual = LocBin.Parse(payload, did).GetRows(did);
			if (actual.Count != expected.Count)
			{
				reason = $"satır kaybı {actual.Count}/{expected.Count}";
				return false;
			}
			Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
			for (int i = 0; i < actual.Count; i++)
			{
				values[actual[i].Key] = actual[i].Original ?? "";
			}
			for (int i = 0; i < expected.Count; i++)
			{
				string wanted = expected[i].Translation ?? expected[i].Original ?? "";
				if (!values.TryGetValue(expected[i].Key, out string found) || !string.Equals(found, wanted, StringComparison.Ordinal))
				{
					reason = "satır doğrulama hatası";
					return false;
				}
			}
			return true;
		}
		catch
		{
			reason = "tr parse fail";
			return false;
		}
	}

	public static bool IsCriticalUiDid(int did)
	{
		uint value = unchecked((uint)did);
		// 0x250001A0-0x250001FF contains the client-wide grammar/token/string-table
		// records. Treating these rows as ordinary text can replace structural
		// sentinels such as {{keepspaces}} and makes the game display
		// <STRING TABLE ERROR> across the UI.
		return value >= 0x250001A0u && value <= 0x250001FFu;
	}

	public static string AlignCriticalWhitespace(string source, string target)
	{
		if (string.IsNullOrEmpty(target)) return target;
		string leading = EdgeWhitespace(source, true);
		string trailing = EdgeWhitespace(source, false);
		int start = 0;
		while (start < target.Length && char.IsWhiteSpace(target[start])) start++;
		int end = target.Length - 1;
		while (end >= start && char.IsWhiteSpace(target[end])) end--;
		string middle = end >= start ? target.Substring(start, end - start + 1) : "";
		return leading + middle + trailing;
	}

	public static bool IsSafeCriticalTranslation(string source, string target)
	{
		if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(target)
			|| string.Equals(source, target, StringComparison.Ordinal)
			|| TranslationQuality.LooksCorruptPair(source, target)
			|| !TextGuard.HasSameProtectedTokens(source, target))
		{
			return false;
		}
		if (CountChar(source, '\r') != CountChar(target, '\r')
			|| CountChar(source, '\n') != CountChar(target, '\n')
			|| CountChar(source, '\t') != CountChar(target, '\t'))
		{
			return false;
		}
		return string.Equals(EdgeWhitespace(source, true), EdgeWhitespace(target, true), StringComparison.Ordinal)
			&& string.Equals(EdgeWhitespace(source, false), EdgeWhitespace(target, false), StringComparison.Ordinal);
	}

	private static int CountChar(string value, char wanted)
	{
		int count = 0;
		foreach (char c in value ?? "") if (c == wanted) count++;
		return count;
	}

	private static string EdgeWhitespace(string value, bool leading)
	{
		if (string.IsNullOrEmpty(value)) return "";
		if (leading)
		{
			int i = 0;
			while (i < value.Length && char.IsWhiteSpace(value[i])) i++;
			return value.Substring(0, i);
		}
		int j = value.Length - 1;
		while (j >= 0 && char.IsWhiteSpace(value[j])) j--;
		return value.Substring(j + 1);
	}

	public static bool CriticalUiPayloadsUnchanged(string sourcePath, string candidatePath, out string reason)
	{
		reason = null;
		try
		{
			if (!CriticalUiMetadataMatches(sourcePath, candidatePath, out string metadataReason))
			{
				reason = metadataReason;
				return false;
			}
			Dictionary<int, byte[]> source = ReadCriticalUiPayloads(sourcePath);
			Dictionary<int, byte[]> candidate = ReadCriticalUiPayloads(candidatePath);
			if (source.Count == 0 || source.Count != candidate.Count)
			{
				reason = $"kritik tablo sayısı değişti ({source.Count}/{candidate.Count})";
				return false;
			}
			foreach (KeyValuePair<int, byte[]> item in source)
			{
				if (!candidate.TryGetValue(item.Key, out byte[] value) || !BytesEqual(item.Value, value))
				{
					reason = "kritik tablo değişti: 0x" + item.Key.ToString("X8");
					return false;
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			reason = "kritik tablo doğrulanamadı: " + ex.Message;
			return false;
		}
	}

	/// <summary>
	/// Temiz İngilizce bir büyük güncellemede, önceki çalışan Türkçe DAT'taki
	/// kritik UI tablolarını yalnız dış DAT metadata'sı ile metin dışındaki tüm
	/// LocBin yapısı birebir aynıysa ve
	/// referans gerçekten daha fazla Türkçe içeriyorsa yeni kapsayıcıya taşır.
	/// Uyuşmayan tablolar güvenli biçimde resmî kaynakta bırakılır.
	/// </summary>
	public static CriticalMigrationResult TransplantCompatibleCriticalPayloads(string sourcePath, string referencePath, string candidatePath, ISet<int> excludedDids = null)
	{
		CriticalMigrationResult result = new CriticalMigrationResult();
		Dictionary<int, byte[]> payloads = new Dictionary<int, byte[]>();
		Dictionary<int, int[]> sourceMetadata = new Dictionary<int, int[]>();
		Dictionary<int, int> sourceVersions = new Dictionary<int, int>();
		using (DatExportSession source = new DatExportSession())
		using (DatExportSession reference = new DatExportSession(1))
		{
			source.Open(sourcePath, writable: false);
			reference.Open(referencePath, writable: false);
			Dictionary<int, int[]> sourceMap = source.LoadSizeMap();
			Dictionary<int, int[]> referenceMap = reference.LoadSizeMap();
			foreach (KeyValuePair<int, int[]> item in sourceMap)
			{
				int did = item.Key;
				if (!IsCriticalUiDid(did) || !referenceMap.TryGetValue(did, out int[] refMeta))
				{
					continue;
				}
				if (excludedDids != null && excludedDids.Contains(did))
				{
					result.Skipped++;
					continue;
				}
				try
				{
					int sourceVersion;
					int referenceVersion;
					byte[] sourcePayload = source.ReadSubfile(did, item.Value[0], out sourceVersion);
					byte[] referencePayload = reference.ReadSubfile(did, refMeta[0], out referenceVersion);
					if (sourceVersion != referenceVersion || item.Value[1] != refMeta[1])
					{
						result.Skipped++;
						continue;
					}
					LocBin sourceBin = LocBin.Parse(sourcePayload, did);
					LocBin referenceBin = LocBin.Parse(referencePayload, did);
					List<LocRow> sourceRows = sourceBin.GetRows(did);
					List<LocRow> referenceRows = referenceBin.GetRows(did);
					if (!CriticalStructuresMatch(sourceBin, sourceRows, referenceBin, referenceRows))
					{
						result.Skipped++;
						continue;
					}
					int sourceTurkish = 0;
					int referenceTurkish = 0;
					bool forbidden = false;
					for (int i = 0; i < sourceRows.Count; i++)
					{
						string sourceText = sourceRows[i].Original ?? "";
						string referenceText = referenceRows[i].Original ?? "";
						if (TranslationQuality.LooksTurkishText(sourceText)) sourceTurkish++;
						if (TranslationQuality.LooksTurkishText(referenceText)) referenceTurkish++;
						if (referenceText.IndexOf("EskiReferans", StringComparison.OrdinalIgnoreCase) >= 0 || referenceText.IndexOf('�') >= 0)
						{
							forbidden = true;
						}
					}
					if (forbidden || referenceTurkish <= sourceTurkish)
					{
						result.Skipped++;
						continue;
					}
					result.Compatible++;
					payloads[did] = referencePayload;
					sourceMetadata[did] = item.Value;
					sourceVersions[did] = sourceVersion;
				}
				catch
				{
					result.Failed++;
				}
			}
		}
		if (payloads.Count == 0)
		{
			return result;
		}
		using (DatExportSession candidate = new DatExportSession())
		{
			candidate.Open(candidatePath, writable: true);
			foreach (KeyValuePair<int, byte[]> item in payloads)
			{
				try
				{
					int[] meta = sourceMetadata[item.Key];
					if (candidate.WriteSubfile(item.Key, item.Value, sourceVersions[item.Key], meta[1]) < 0)
					{
						result.Failed++;
						continue;
					}
					result.Transplanted.Add(item.Key);
				}
				catch
				{
					result.Failed++;
				}
			}
			candidate.Flush();
		}
		return result;
	}

	private static bool CriticalStructuresMatch(LocBin sourceBin, IList<LocRow> sourceRows, LocBin referenceBin, IList<LocRow> referenceRows)
	{
		if (sourceBin == null || referenceBin == null || sourceRows == null || referenceRows == null
			|| sourceBin.UsedFlatFallback || referenceBin.UsedFlatFallback
			|| sourceRows.Count != referenceRows.Count)
		{
			return false;
		}
		for (int i = 0; i < sourceRows.Count; i++)
		{
			if (!string.Equals(sourceRows[i].Key, referenceRows[i].Key, StringComparison.Ordinal))
			{
				return false;
			}
			// Her iki LocBin aynı deterministik metinlerle yeniden kurulur. Böylece
			// metin içeriği göz ardı edilirken kayıt hash'leri, grup sayıları,
			// bilinmeyen alanlar, sıra, şema ve trailing bytes karşılaştırılır.
			string marker = "__LOTRO_STRUCTURE_ROW_" + i.ToString("X8") + "__";
			sourceRows[i].Translation = marker;
			referenceRows[i].Translation = marker;
		}
		return BytesEqual(sourceBin.Rebuild(sourceRows), referenceBin.Rebuild(referenceRows));
	}

	public static bool CriticalUiPayloadsMatchExpected(string sourcePath, string referencePath, string candidatePath, ISet<int> transplanted, IDictionary<string, AllowedCriticalRule> allowedRowTargets, out string reason)
	{
		reason = null;
		try
		{
			if (!CriticalUiMetadataMatches(sourcePath, candidatePath, out string metadataReason))
			{
				reason = metadataReason;
				return false;
			}
			Dictionary<int, byte[]> source = ReadCriticalUiPayloads(sourcePath);
			Dictionary<int, byte[]> candidate = ReadCriticalUiPayloads(candidatePath);
			Dictionary<int, byte[]> reference = (transplanted != null && transplanted.Count > 0)
				? ReadCriticalUiPayloads(referencePath)
				: new Dictionary<int, byte[]>();
			if (source.Count == 0 || source.Count != candidate.Count)
			{
				reason = $"kritik tablo sayısı değişti ({source.Count}/{candidate.Count})";
				return false;
			}
			foreach (KeyValuePair<int, byte[]> item in source)
			{
				byte[] expected = item.Value;
				if (transplanted != null && transplanted.Contains(item.Key))
				{
					if (!reference.TryGetValue(item.Key, out expected))
					{
						reason = "kritik referans eksik: 0x" + item.Key.ToString("X8");
						return false;
					}
				}
				if (!candidate.TryGetValue(item.Key, out byte[] actual))
				{
					reason = "kritik tablo eksik: 0x" + item.Key.ToString("X8");
					return false;
				}
				if (!RequiredAllowedCriticalRowsMatch(item.Value, actual, item.Key, allowedRowTargets, out string requiredReason))
				{
					reason = "zorunlu kritik çeviri doğrulanamadı: 0x" + item.Key.ToString("X8") + " (" + requiredReason + ")";
					return false;
				}
				if (!BytesEqual(expected, actual) && !OnlyAllowedCriticalRowsChanged(expected, actual, item.Key, allowedRowTargets, out string rowReason))
				{
					reason = "kritik tablo beklenmeyen biçimde değişti: 0x" + item.Key.ToString("X8") + " (" + rowReason + ")";
					return false;
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			reason = "kritik tablo doğrulanamadı: " + ex.Message;
			return false;
		}
	}

	private static bool RequiredAllowedCriticalRowsMatch(byte[] sourcePayload, byte[] actualPayload, int did, IDictionary<string, AllowedCriticalRule> allowedRowTargets, out string reason)
	{
		reason = null;
		if (allowedRowTargets == null || allowedRowTargets.Count == 0)
		{
			return true;
		}
		try
		{
			List<LocRow> sourceRows = LocBin.Parse(sourcePayload, did).GetRows(did);
			List<LocRow> actualRows = LocBin.Parse(actualPayload, did).GetRows(did);
			Dictionary<string, string> actualValues = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (LocRow row in actualRows) actualValues[row.Key] = row.Original ?? "";
			foreach (LocRow row in sourceRows)
			{
				if (!allowedRowTargets.TryGetValue(row.Key, out AllowedCriticalRule rule) || rule == null)
				{
					continue;
				}
				string source = row.Original ?? "";
				string expected = string.Equals(source, rule.Source ?? "", StringComparison.Ordinal)
					? (rule.Target ?? "")
					: source;
				if (!actualValues.TryGetValue(row.Key, out string actual) || !string.Equals(actual, expected, StringComparison.Ordinal))
				{
					reason = "hedef eksik: " + row.Key;
					return false;
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			reason = ex.Message;
			return false;
		}
	}

	private static bool CriticalUiMetadataMatches(string sourcePath, string candidatePath, out string reason)
	{
		reason = null;
		try
		{
			using (DatExportSession source = new DatExportSession())
			using (DatExportSession candidate = new DatExportSession(1))
			{
				source.Open(sourcePath, writable: false);
				candidate.Open(candidatePath, writable: false);
				Dictionary<int, int[]> sourceMap = source.LoadSizeMap();
				Dictionary<int, int[]> candidateMap = candidate.LoadSizeMap();
				foreach (KeyValuePair<int, int[]> item in sourceMap)
				{
					if (!IsCriticalUiDid(item.Key)) continue;
					if (!candidateMap.TryGetValue(item.Key, out int[] candidateMeta))
					{
						reason = "kritik metadata eksik: 0x" + item.Key.ToString("X8");
						return false;
					}
					int sourceVersion;
					int candidateVersion;
					source.ReadSubfile(item.Key, item.Value[0], out sourceVersion);
					candidate.ReadSubfile(item.Key, candidateMeta[0], out candidateVersion);
					if (sourceVersion != candidateVersion || item.Value[1] != candidateMeta[1]
						|| source.GetCompressionFlag(item.Key) != candidate.GetCompressionFlag(item.Key))
					{
						reason = "kritik metadata değişti: 0x" + item.Key.ToString("X8");
						return false;
					}
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			reason = "kritik metadata doğrulanamadı: " + ex.Message;
			return false;
		}
	}

	private static bool OnlyAllowedCriticalRowsChanged(byte[] expectedPayload, byte[] actualPayload, int did, IDictionary<string, AllowedCriticalRule> allowedRowTargets, out string reason)
	{
		reason = null;
		if (allowedRowTargets == null || allowedRowTargets.Count == 0)
		{
			reason = "izinli satır yok";
			return false;
		}
		try
		{
			LocBin expectedBin = LocBin.Parse(expectedPayload, did);
			LocBin actualBin = LocBin.Parse(actualPayload, did);
			List<LocRow> expectedRows = expectedBin.GetRows(did);
			List<LocRow> actualRows = actualBin.GetRows(did);
			if (expectedRows.Count != actualRows.Count)
			{
				reason = $"satır sayısı {expectedRows.Count}/{actualRows.Count}";
				return false;
			}
			Dictionary<string, string> actualValues = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (LocRow row in actualRows) actualValues[row.Key] = row.Original ?? "";
			int changed = 0;
			foreach (LocRow row in expectedRows)
			{
				string expected = row.Original ?? "";
				if (!actualValues.TryGetValue(row.Key, out string actual))
				{
					reason = "anahtar eksik: " + row.Key;
					return false;
				}
				if (string.Equals(expected, actual, StringComparison.Ordinal))
				{
					continue;
				}
				changed++;
				if (!allowedRowTargets.TryGetValue(row.Key, out AllowedCriticalRule rule) || rule == null
					|| !string.Equals(expected, rule.Source ?? "", StringComparison.Ordinal)
					|| !string.Equals(actual, rule.Target ?? "", StringComparison.Ordinal)
					|| TranslationQuality.LooksCorruptPair(expected, actual))
				{
					reason = "izin dışı satır: " + row.Key;
					return false;
				}
			}
			if (changed == 0)
			{
				reason = "payload farklı, satır farkı yok";
				return false;
			}
			foreach (LocRow row in expectedRows)
			{
				string source = row.Original ?? "";
				if (allowedRowTargets.TryGetValue(row.Key, out AllowedCriticalRule rule)
					&& rule != null
					&& string.Equals(source, rule.Source ?? "", StringComparison.Ordinal))
				{
					row.Translation = rule.Target ?? "";
				}
				else
				{
					row.Translation = source;
				}
			}
			byte[] exactExpected = expectedBin.Rebuild(expectedRows);
			if (!BytesEqual(exactExpected, actualPayload))
			{
				reason = "izinli satır dışındaki payload baytları değişti";
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			reason = ex.Message;
			return false;
		}
	}

	private static Dictionary<int, byte[]> ReadCriticalUiPayloads(string path)
	{
		if (string.IsNullOrEmpty(path) || !File.Exists(path))
		{
			throw new FileNotFoundException("DAT yok", path);
		}
		Dictionary<int, byte[]> result = new Dictionary<int, byte[]>();
		using (DatExportSession session = new DatExportSession())
		{
			session.Open(path, writable: false);
			Dictionary<int, int[]> sizes = session.LoadSizeMap();
			foreach (KeyValuePair<int, int[]> item in sizes)
			{
				if (!IsCriticalUiDid(item.Key))
				{
					continue;
				}
				int version;
				result[item.Key] = session.ReadSubfile(item.Key, item.Value[0], out version);
			}
		}
		return result;
	}

	private static void Restore(IList<LocRow> rows, string[] snap)
	{
		for (int i = 0; i < rows.Count && i < snap.Length; i++)
		{
			rows[i].Translation = snap[i];
		}
	}

	private static bool BytesEqual(byte[] a, byte[] b)
	{
		if (a == null || b == null || a.Length != b.Length)
		{
			return false;
		}
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i])
			{
				return false;
			}
		}
		return true;
	}
}
