using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace LotroTrGemini;

/// <summary>
/// Version-tolerant identity data for one extracted localization value.
/// Position is retained as metadata, never as the sole matching key.
/// </summary>
public sealed class CatalogRecord
{
	public int Did { get; internal set; }
	public long Position { get; internal set; }
	public int RecordIndex { get; internal set; }
	public int GroupIndex { get; internal set; }
	public int IndexInGroup { get; internal set; }
	public string RecordFingerprint { get; internal set; }
	public string StructuralFingerprint { get; internal set; }
	public string Source { get; internal set; }
	public string SourceFingerprint { get; internal set; }
	public string SourceDigest { get; internal set; }
	public string TokenSignature { get; internal set; }
	public string ContextFingerprint { get; internal set; }
	public bool CriticalUi { get; internal set; }

	public string EntryIdentity
	{
		get { return StableIdentity; }
	}

	public string Key
	{
		get { return Did.ToString("X8") + ":" + RecordIndex + ":" + GroupIndex + ":" + IndexInGroup; }
	}

	public string StableIdentity
	{
		get { return CatalogIdentity.BuildStableKey(this); }
	}
}

public static class CatalogIdentity
{
	public static CatalogRecord FromLocRow(
		LocRow row,
		string recordFingerprint,
		string structuralFingerprint,
		string contextFingerprint,
		long position)
	{
		if (row == null) throw new ArgumentNullException(nameof(row));
		string source = NormalizeSource(row.Original);
		string tokenSignature = ProtectedFormat.GetTokenSignature(source);
		CatalogRecord result = new CatalogRecord
		{
			Did = row.Did,
			Position = position,
			RecordIndex = row.RecordIndex,
			GroupIndex = row.GroupIndex,
			IndexInGroup = row.IndexInGroup,
			RecordFingerprint = NormalizeFingerprint(recordFingerprint, "record|" + row.Did + "|" + row.RecordIndex),
			StructuralFingerprint = NormalizeFingerprint(structuralFingerprint, "structural|" + row.GroupIndex),
			Source = source,
			SourceFingerprint = Sha256Hex("source|" + source),
			TokenSignature = tokenSignature,
			ContextFingerprint = NormalizeFingerprint(contextFingerprint, "context|" + source),
			CriticalUi = IsCriticalUiDid(row.Did)
		};
		result.SourceDigest = LotroTrGemini.SourceDigest.ForRecord(result);
		return result;
	}

	public static string NormalizeSource(string value)
	{
		return (value ?? string.Empty).Normalize(NormalizationForm.FormKC).Replace("\r\n", "\n").Replace('\r', '\n');
	}

	public static string BuildStableKey(CatalogRecord record)
	{
		if (record == null) throw new ArgumentNullException(nameof(record));
		// Context is an auxiliary disambiguation signal. Neighboring text can
		// change during an update, so it must not invalidate the stable identity
		// of an otherwise unchanged record.
		return record.Did.ToString("X8") + "|" + (record.StructuralFingerprint ?? string.Empty) + "|" + (record.RecordFingerprint ?? string.Empty);
	}

	public static string BuildContextFingerprint(string previousSource, string nextSource, string previousRecord, string nextRecord)
	{
		return Sha256Hex("context|prev=" + NormalizeSource(previousSource) + "|next=" + NormalizeSource(nextSource) + "|prev-record=" + (previousRecord ?? string.Empty) + "|next-record=" + (nextRecord ?? string.Empty));
	}

	public static string Sha256Hex(string value)
	{
		using (SHA256 sha = SHA256.Create())
		{
			return FixedHex(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty)));
		}
	}

	public static string ComputeCatalogHash(IList<CatalogRecord> records)
	{
		if (records == null) throw new ArgumentNullException(nameof(records));
		List<CatalogRecord> ordered = new List<CatalogRecord>(records);
		ordered.Sort(delegate (CatalogRecord left, CatalogRecord right)
		{
			int result = left.Position.CompareTo(right.Position);
			if (result != 0) return result;
			return string.CompareOrdinal(left.Key, right.Key);
		});
		StringBuilder content = new StringBuilder();
		foreach (CatalogRecord record in ordered)
		{
			content.Append(record.Did.ToString("X8")).Append('|')
				.Append(record.RecordFingerprint ?? string.Empty).Append('|')
				.Append(record.StructuralFingerprint ?? string.Empty).Append('|')
				.Append(record.SourceFingerprint ?? string.Empty).Append('|')
				.Append(record.ContextFingerprint ?? string.Empty).Append('\n');
		}
		return Sha256Hex(content.ToString());
	}

	public static bool IsCriticalUiDid(int did)
	{
		uint value = unchecked((uint)did);
		return value >= 0x250001A0u && value <= 0x250001FFu;
	}

	public static bool IsExcludedFromTranslation(int did, int recordIndex, int groupIndex, int indexInGroup)
	{
		return unchecked((uint)did) == 0x25033EC0u
			|| (unchecked((uint)did) == 0x250001BBu && recordIndex == 364 && groupIndex == -1 && indexInGroup == 0);
	}

	private static string NormalizeFingerprint(string value, string fallback)
	{
		return string.IsNullOrWhiteSpace(value) ? Sha256Hex(fallback) : value.ToLowerInvariant();
	}

	private static string FixedHex(byte[] bytes)
	{
		StringBuilder result = new StringBuilder(bytes.Length * 2);
		foreach (byte value in bytes) result.Append(value.ToString("x2"));
		return result.ToString();
	}
}
