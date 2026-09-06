using System;
using System.Collections.Generic;

namespace LotroTrGemini;

public enum DiffClassification
{
	UNCHANGED,
	NEW,
	MODIFIED,
	REMOVED,
	MOVED,
	AMBIGUOUS
}

public sealed class CatalogDiffRecord
{
	public DiffClassification Classification { get; internal set; }
	public double Confidence { get; internal set; }
	public bool ReviewRequired { get; internal set; }
	public CatalogRecord OldRecord { get; internal set; }
	public CatalogRecord NewRecord { get; internal set; }
	public string OldKey { get { return OldRecord == null ? null : OldRecord.Key; } }
	public string NewKey { get { return NewRecord == null ? null : NewRecord.Key; } }
	public string SourceFingerprint { get { return (NewRecord ?? OldRecord)?.SourceFingerprint; } }
	public string Reason { get; internal set; }
}

public sealed class CatalogDiffSummary
{
	public int Unchanged { get; internal set; }
	public int New { get; internal set; }
	public int Modified { get; internal set; }
	public int Removed { get; internal set; }
	public int Moved { get; internal set; }
	public int Ambiguous { get; internal set; }
	public int ReviewRequired { get; internal set; }
}

/// <summary>
/// Conservative multi-signal matcher. It never carries a translation on an ambiguous match.
/// </summary>
public static class CatalogDiff
{
	public static List<CatalogDiffRecord> Compare(IList<CatalogRecord> oldRecords, IList<CatalogRecord> newRecords)
	{
		oldRecords = oldRecords ?? new List<CatalogRecord>();
		newRecords = newRecords ?? new List<CatalogRecord>();
		HashSet<CatalogRecord> unmatchedOld = new HashSet<CatalogRecord>(oldRecords);
		Dictionary<string, List<CatalogRecord>> byStable = Index(oldRecords, StableKey);
		Dictionary<string, List<CatalogRecord>> bySourceContext = Index(oldRecords, SourceContextKey);
		Dictionary<string, List<CatalogRecord>> bySource = Index(oldRecords, SourceKey);
		List<CatalogDiffRecord> result = new List<CatalogDiffRecord>(oldRecords.Count + newRecords.Count);

		List<CatalogRecord> orderedNew = new List<CatalogRecord>(newRecords);
		orderedNew.Sort((left, right) => left.Position.CompareTo(right.Position));
		foreach (CatalogRecord current in orderedNew)
		{
			List<CatalogRecord> candidates = Find(byStable, StableKey(current), unmatchedOld);
			if (candidates.Count == 1)
			{
				CatalogRecord old = candidates[0];
				unmatchedOld.Remove(old);
				result.Add(Matched(old, current, 1.0, "stable DID + structural + record + context identity"));
				continue;
			}
			if (candidates.Count > 1)
			{
				Consume(unmatchedOld, candidates);
				result.Add(Ambiguous(current, candidates, "duplicate stable identity; manual review required"));
				continue;
			}

			candidates = Find(bySourceContext, SourceContextKey(current), unmatchedOld);
			if (candidates.Count == 1)
			{
				CatalogRecord old = candidates[0];
				unmatchedOld.Remove(old);
				result.Add(Matched(old, current, 0.90, "same DID + source + context; structural identity changed"));
				continue;
			}
			if (candidates.Count > 1)
			{
				Consume(unmatchedOld, candidates);
				result.Add(Ambiguous(current, candidates, "duplicate DID + source + context; manual review required"));
				continue;
			}

			candidates = Find(bySource, SourceKey(current), unmatchedOld);
			if (candidates.Count == 1)
			{
				CatalogRecord old = candidates[0];
				unmatchedOld.Remove(old);
				result.Add(Matched(old, current, 0.75, "unique source fingerprint only; position is auxiliary"));
				continue;
			}
			if (candidates.Count > 1)
			{
				Consume(unmatchedOld, candidates);
				result.Add(Ambiguous(current, candidates, "duplicate source fingerprint; text alone is not identity"));
				continue;
			}
			result.Add(NewRecord(current));
		}

		foreach (CatalogRecord old in unmatchedOld)
		{
			result.Add(new CatalogDiffRecord
			{
				Classification = DiffClassification.REMOVED,
				Confidence = 1.0,
				ReviewRequired = old.CriticalUi,
				OldRecord = old,
				Reason = old.CriticalUi ? "record removed from critical UI range; manual review required" : "no safe new identity match"
			});
		}
		result.Sort(CompareResultOrder);
		return result;
	}

	public static CatalogDiffSummary Summarize(IList<CatalogDiffRecord> records)
	{
		CatalogDiffSummary summary = new CatalogDiffSummary();
		if (records == null) return summary;
		foreach (CatalogDiffRecord record in records)
		{
			switch (record.Classification)
			{
				case DiffClassification.UNCHANGED: summary.Unchanged++; break;
				case DiffClassification.NEW: summary.New++; break;
				case DiffClassification.MODIFIED: summary.Modified++; break;
				case DiffClassification.REMOVED: summary.Removed++; break;
				case DiffClassification.MOVED: summary.Moved++; break;
				case DiffClassification.AMBIGUOUS: summary.Ambiguous++; break;
			}
			if (record.ReviewRequired) summary.ReviewRequired++;
		}
		return summary;
	}

	private static CatalogDiffRecord Matched(CatalogRecord old, CatalogRecord current, double confidence, string method)
	{
		bool sameSource = string.Equals(old.SourceFingerprint, current.SourceFingerprint, StringComparison.Ordinal);
		DiffClassification classification;
		string reason;
		if (!sameSource)
		{
			classification = DiffClassification.MODIFIED;
			reason = method + "; source fingerprint changed";
		}
		else if (old.Position != current.Position || !string.Equals(old.Key, current.Key, StringComparison.Ordinal))
		{
			classification = DiffClassification.MOVED;
			reason = method + "; source unchanged but position/key moved";
		}
		else
		{
			classification = DiffClassification.UNCHANGED;
			reason = method + "; source and position unchanged";
		}
		bool review = old.CriticalUi || current.CriticalUi;
		if (review) reason += "; critical UI requires human review";
		return new CatalogDiffRecord
		{
			Classification = classification,
			Confidence = confidence,
			ReviewRequired = review,
			OldRecord = old,
			NewRecord = current,
			Reason = reason
		};
	}

	private static CatalogDiffRecord Ambiguous(CatalogRecord current, IList<CatalogRecord> candidates, string reason)
	{
		bool critical = current.CriticalUi;
		return new CatalogDiffRecord
		{
			Classification = DiffClassification.AMBIGUOUS,
			Confidence = 0.0,
			ReviewRequired = true,
			NewRecord = current,
			Reason = critical ? reason + "; critical UI requires human review" : reason
		};
	}

	private static CatalogDiffRecord NewRecord(CatalogRecord current)
	{
		return new CatalogDiffRecord
		{
			Classification = DiffClassification.NEW,
			Confidence = 1.0,
			ReviewRequired = current.CriticalUi,
			NewRecord = current,
			Reason = current.CriticalUi ? "no safe match; critical UI requires human review" : "no safe identity match"
		};
	}

	private static Dictionary<string, List<CatalogRecord>> Index(IList<CatalogRecord> records, Func<CatalogRecord, string> key)
	{
		Dictionary<string, List<CatalogRecord>> result = new Dictionary<string, List<CatalogRecord>>(StringComparer.Ordinal);
		foreach (CatalogRecord record in records)
		{
			string value = key(record);
			if (!result.TryGetValue(value, out List<CatalogRecord> list)) result[value] = list = new List<CatalogRecord>();
			list.Add(record);
		}
		return result;
	}

	private static List<CatalogRecord> Find(Dictionary<string, List<CatalogRecord>> index, string key, HashSet<CatalogRecord> unmatched)
	{
		List<CatalogRecord> result = new List<CatalogRecord>();
		if (!index.TryGetValue(key, out List<CatalogRecord> list)) return result;
		foreach (CatalogRecord record in list) if (unmatched.Contains(record)) result.Add(record);
		return result;
	}

	private static void Consume(HashSet<CatalogRecord> unmatched, IList<CatalogRecord> candidates)
	{
		foreach (CatalogRecord candidate in candidates) unmatched.Remove(candidate);
	}

	private static string StableKey(CatalogRecord record) { return record.StableIdentity; }
	private static string SourceContextKey(CatalogRecord record) { return record.Did.ToString("X8") + "|" + (record.SourceFingerprint ?? "") + "|" + (record.ContextFingerprint ?? ""); }
	private static string SourceKey(CatalogRecord record) { return (record.SourceFingerprint ?? ""); }

	private static int CompareResultOrder(CatalogDiffRecord left, CatalogDiffRecord right)
	{
		long leftPosition = left.NewRecord == null ? long.MaxValue : left.NewRecord.Position;
		long rightPosition = right.NewRecord == null ? long.MaxValue : right.NewRecord.Position;
		int result = leftPosition.CompareTo(rightPosition);
		if (result != 0) return result;
		long leftOld = left.OldRecord == null ? long.MaxValue : left.OldRecord.Position;
		long rightOld = right.OldRecord == null ? long.MaxValue : right.OldRecord.Position;
		return leftOld.CompareTo(rightOld);
	}
}
