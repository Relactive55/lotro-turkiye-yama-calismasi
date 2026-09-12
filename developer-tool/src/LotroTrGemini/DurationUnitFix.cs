using System;
using System.Text.RegularExpressions;

namespace LotroTrGemini;

/// <summary>
/// Normalizes the compact duration units used by LOTRO tooltips.  The client
/// emits values such as 1m and 12s inside translated strings; these are not
/// localization records of their own, so they must be normalized together
/// with the translated tooltip text.
/// </summary>
public static class DurationUnitFix
{
	private static readonly Regex CompactDuration = new Regex(
		@"(?:(?<![\p{L}\d])|(?<=[msh]))([+-]?)(\d+(?:[.,]\d+)?)\s*(m(?:in(?:ute)?s?)?|s(?:n|ec(?:ond)?s?)?|h(?:r?s?|ours?))(?![\p{L}])",
		RegexOptions.CultureInvariant | RegexOptions.Compiled);
	private static readonly Regex ImportedCooldownGarble = new Regex(
		@"(?<![\p{L}\d])(\d+(?:[.,]\d+)?)\s*Sakin\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex ImportedGraceGarble = new Regex(
		@"(?<![\p{L}\d])(\d+(?:[.,]\d+)?)\s*Grace\b",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex SignedDuration = new Regex(
		@"(?<![\p{L}\d])([+-])(\d+(?:[.,]\d+)?)\s*(m(?:in(?:ute)?s?)?|s(?:n|ec(?:ond)?s?)?|h(?:r?s?|ours?))(?![\p{L}])",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly string[] DurationWords =
	{
		"süre", "bekleme", "soğuma", "soğutma", "cooldown", "duration", "zaman", "time",
		"buff", "debuff", "indüksiyon", "induction", "sersem", "stun", "bağışıklık",
		"immunity", "kök", "root", "geri sayım", "sayım"
	};
	private static readonly string[] RangeOrMeasurementWords =
	{
		"menzil", "range", "içinde", "alanda", "alan ", "aralığı", "metre", "meter", "mesafe",
		"duvar", "dekoratif", "süsleme", "silah", "kılıç", "panel", "salon", "smial", "festival"
	};

	/// <summary>
	/// Converts numeric duration suffixes in an already translated value.
	/// Source English must be kept intact, so callers handling a source/target
	/// pair should use <see cref="NormalizeTranslated"/>.
	/// </summary>
	public static string Normalize(string text)
	{
		if (string.IsNullOrEmpty(text)) return text;
		// A few old machine translations joined a compact cooldown to the
		// following Turkish word (for example “20Sakin olun.”) or left the
		// English grace marker attached (“5Grace”).  These are duration values,
		// not ordinary words; expose the number before normalizing units.
		text = ImportedCooldownGarble.Replace(text, "$1 saniye");
		text = ImportedGraceGarble.Replace(text, "$1 saniyelik hoşgörü süresi");
		return CompactDuration.Replace(text, match =>
		{
			string sign = match.Groups[1].Value;
			string value = match.Groups[2].Value;
			string unit = match.Groups[3].Value;
			// A bare lowercase m is ambiguous in LOTRO: it is also used for
			// metres/ranges and in housing item dimensions.  Convert it only when
			// the surrounding tooltip identifies a duration or it is part of a
			// compact duration pair such as 1m30s.  Explicit min/minute values are
			// unambiguous and are always converted.
			if (unit.Length == 1 && unit[0] == 'm' && !IsDurationMinute(text, match))
				return match.Value;
			return sign + value + " " + LabelFor(unit);
		});
	}

	/// <summary>
	/// Applies the unit conversion only when the target is actually translated.
	/// This prevents an unchanged English source row from being silently edited.
	/// </summary>
	public static string NormalizeTranslated(string source, string target)
	{
		if (string.IsNullOrEmpty(target) || string.Equals(source, target, StringComparison.Ordinal))
			return target;
		string normalized = Normalize(target);
		// Preserve a signed cooldown/duration when a previous translation dropped
		// the sign (for example source “-5min” -> target “5min”).
		foreach (Match signed in SignedDuration.Matches(source ?? string.Empty))
		{
			string sign = signed.Groups[1].Value;
			string value = signed.Groups[2].Value;
			string label = LabelFor(signed.Groups[3].Value);
			string wanted = sign + value + " " + label;
			if (normalized.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0) continue;
			string unsigned = value + " " + label;
			int index = normalized.IndexOf(unsigned, StringComparison.OrdinalIgnoreCase);
			if (index >= 0)
				normalized = normalized.Substring(0, index) + wanted + normalized.Substring(index + unsigned.Length);
		}
		return normalized;
	}

	private static string LabelFor(string unit)
	{
		char first = char.ToLowerInvariant(unit[0]);
		if (first == 'm') return "dk";
		if (first == 's') return "saniye";
		return "saat";
	}

	private static bool IsDurationMinute(string text, Match match)
	{
		string unit = match.Groups[3].Value;
		if (unit.Length != 1) return true;

		int afterStart = match.Index + match.Length;
		string after = text.Substring(afterStart, Math.Min(24, text.Length - afterStart));
		string before = text.Substring(Math.Max(0, match.Index - 24),
			match.Index - Math.Max(0, match.Index - 24));
		// Adjacent compact units form a single duration expression.  Do not
		// treat an earlier 30s in “30s için 5m alanda” as part of the later 5m.
		if (Regex.IsMatch(after, @"^\s*\d+(?:[.,]\d+)?\s*[sh](?![\p{L}])")
			|| Regex.IsMatch(before, @"\d+(?:[.,]\d+)?\s*[sh]\s*$"))
			return true;

		int durationDistance = NearestWordDistance(text, match, DurationWords, 72);
		int rangeDistance = NearestWordDistance(text, match, RangeOrMeasurementWords, 72);
		if (rangeDistance >= 0 && (durationDistance < 0 || rangeDistance <= durationDistance))
			return false;
		return durationDistance >= 0;
	}

	private static int NearestWordDistance(string text, Match match, string[] words, int radius)
	{
		int start = Math.Max(0, match.Index - radius);
		int end = Math.Min(text.Length, match.Index + match.Length + radius);
		string window = text.Substring(start, end - start);
		int best = -1;
		foreach (string word in words)
		{
			int offset = 0;
			while (offset < window.Length)
			{
				int found = window.IndexOf(word, offset, StringComparison.OrdinalIgnoreCase);
				if (found < 0) break;
				int absolute = start + found;
				int distance = absolute < match.Index
					? match.Index - (absolute + word.Length)
					: absolute - (match.Index + match.Length);
				if (distance < 0) distance = 0;
				if (best < 0 || distance < best) best = distance;
				offset = found + Math.Max(1, word.Length);
			}
		}
		return best;
	}
}
