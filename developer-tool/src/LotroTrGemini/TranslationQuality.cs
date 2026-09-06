using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LotroTrGemini;

public static class TranslationQuality
{
	private static readonly Regex NumberRe = new Regex("(?<![A-Za-z])(?:%\\s*)?(\\d+(?:[.,]\\d+)?)(?:\\s*%)?", RegexOptions.Compiled);

	private static readonly Regex ModelLeakRe = new Regex("(?i)^\\s*(translation|turkish|türkçe|çeviri|here is|işte)\\s*:|</?think>|ZXQ\\d+QXZ|okay,?\\s*(i understand|understood).*(provide|bring on).*(text)|please provide (me with )?(your |the )?text|\\*\\*\\s*translate me\\s*\\*\\*", RegexOptions.Compiled);

	private static readonly Regex EnglishWordRe = new Regex(@"(?i)\b(the|a|an|and|or|of|to|for|in|on|at|by|with|from|into|this|that|these|those|you|your|we|they|he|she|my|our|their|his|her|is|are|was|were|be|been|will|can|cannot|not|do|does|did|has|have|had|got|more|who|what|where|why|how|there|here|out|about|if|then|than|but|all|any|some|many|much|new|old|make|made|come|comes|go|goes|see|look|feel|speak|talk|teach|damage|quest|level|use|uses|using|requires|required|available|defeat|return|collect|item|items|skill|skills|morale|armour|armor|cooldown|reward|rewards|press|select|open|close|accept|decline|repair|purchase|player|enemy|target|increases|decreases|while|when|after|before|gain|grants|received|against|within|only|each|character|equipment|cosmetic|outfits|show|basic|stats|power|might|agility|vitality|fate|offence|critical|rating|physical|mastery|defence|avoidance|parry|evade|enhance|title|biography|wallet|house|hobby|reputation|war)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex TurkishSignalRe = new Regex(@"(?i)[çğıİöşü]|(?<!['’])\b(ve|veya|için|ile|bir|bu|şu|olarak|olan|olur|değil|görev|hasar|seviye|kullan|kazan|düşman|süre|yetenek|üzerinde|tarafından|yen|dön|konuş|topla|teslim|sandık|zırh|yüzük|göğüs|ayak|baş|bölge|tamamla|koru|azalt|artır|durum|lanetli|doyumsuz|canavar|hediye|heykel|incele|beyaz|alev|eldiven|veba|yayan|lekeli|güçlü|zayıf|dayanıklı|kalan|toplam|mevcut|gereken|gerektirir|verir|alınan|yapılan|etki|maliyet|başarı|artış|azalış|kabul|reddet|kapat|aç|devam|iptal|onar|satın|sat|saldırı|savunma|güç|kader|irade|kudret|canlılık|bereket|itibar|rütbe|hedef|oyuncu|yaratık|ganimet|tarif|malzeme|usta|acemi|kullanılabilir|tamamlandı)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex TurkishProperSuffixRe = new Regex(@"(?i)\p{L}+['’](a|e|da|de|ta|te|dan|den|tan|ten|ı|i|u|ü|ın|in|un|ün|ya|ye)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex TurkishExtraSignalRe = new Regex(@"(?i)\b(tuz)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	public static bool LooksCorruptPair(string source, string translation)
	{
		if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(translation))
		{
			return true;
		}
		if (!string.Equals(source, translation, StringComparison.Ordinal) && translation.IndexOf("EskiReferans", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		if (string.Equals(source, translation, StringComparison.Ordinal))
		{
			return false;
		}
		if (!TextGuard.HasSameProtectedTokens(source, translation) || ModelLeakRe.IsMatch(translation))
		{
			return true;
		}
		if (!HaveSameNumbers(source, translation))
		{
			return true;
		}
		if (LooksEnglishInsteadOfTurkish(source, translation))
		{
			return true;
		}
		int sourceLetters = CountLetters(source);
		int targetLetters = CountLetters(translation);
		if (sourceLetters >= 30 && targetLetters < Math.Max(8, sourceLetters * 28 / 100))
		{
			return true;
		}
		if (source.Length <= 48 && translation.Length >= 120 && translation.Length > source.Length * 5)
		{
			return true;
		}
		return translation.IndexOf('�') >= 0 || translation.IndexOf("Ãƒ", StringComparison.Ordinal) >= 0;
	}

	/// <summary>
	/// İnsan denetiminden geçmiş Key+Source+Target kuralları için yalnız yapısal
	/// güvenliği sınar. Türkçe her zaman özel harf içermez (ör. "Parth Rhavas'a");
	/// bu nedenle genel İngilizce/Türkçe dil sezgisinin yanlış pozitifini burada
	/// kullanmayız.
	/// </summary>
	public static bool LooksUnsafeApprovedPair(string source, string translation)
	{
		if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(translation))
		{
			return true;
		}
		if (!string.Equals(source, translation, StringComparison.Ordinal)
			&& translation.IndexOf("EskiReferans", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		if (!TextGuard.HasSameProtectedTokens(source, translation)
			|| ModelLeakRe.IsMatch(translation)
			|| !HaveSameNumbers(source, translation))
		{
			return true;
		}
		int sourceLetters = CountLetters(source);
		int targetLetters = CountLetters(translation);
		if (sourceLetters >= 30 && targetLetters < Math.Max(8, sourceLetters * 28 / 100))
		{
			return true;
		}
		if (source.Length <= 48 && translation.Length >= 120 && translation.Length > source.Length * 5)
		{
			return true;
		}
		return translation.IndexOf('�') >= 0 || translation.IndexOf("Ãƒ", StringComparison.Ordinal) >= 0;
	}

	public static bool LooksEnglishInsteadOfTurkish(string source, string translation)
	{
		if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(translation) || HasTurkishSignal(translation))
		{
			return false;
		}
		if (!EnglishWordRe.IsMatch(source))
		{
			return false;
		}
		int hits = EnglishWordRe.Matches(translation).Count;
		return hits >= ((translation.Length <= 80) ? 1 : 2);
	}

	public static bool LooksTurkishText(string text)
	{
		return !string.IsNullOrWhiteSpace(text) && HasTurkishSignal(text);
	}

	public static bool LooksEnglishText(string text)
	{
		return !string.IsNullOrWhiteSpace(text) && !HasTurkishSignal(text) && EnglishWordRe.IsMatch(text);
	}

	private static bool HasTurkishSignal(string text)
	{
		if (TurkishSignalRe.IsMatch(text) || TurkishProperSuffixRe.IsMatch(text) || TurkishExtraSignalRe.IsMatch(text))
		{
			return true;
		}
		// "At" is the Turkish noun used for the standalone Horse label. The
		// English preposition does not appear as a capitalized one-word UI value.
		return string.Equals(text.Trim(), "At", StringComparison.Ordinal);
	}

	private static bool HaveSameNumbers(string source, string translation)
	{
		Dictionary<string, int> expected = Count(NumberRe.Matches(source));
		Dictionary<string, int> actual = Count(NumberRe.Matches(translation));
		if (expected.Count != actual.Count)
		{
			return false;
		}
		foreach (KeyValuePair<string, int> item in expected)
		{
			if (!actual.TryGetValue(item.Key, out var count) || count != item.Value)
			{
				return false;
			}
		}
		return true;
	}

	private static Dictionary<string, int> Count(MatchCollection matches)
	{
		Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (Match match in matches)
		{
			string value = match.Groups[1].Value.Replace(',', '.');
			if (!counts.TryGetValue(value, out var count))
			{
				count = 0;
			}
			counts[value] = count + 1;
		}
		return counts;
	}

	private static int CountLetters(string text)
	{
		int count = 0;
		foreach (char value in text ?? "")
		{
			if (char.IsLetter(value)) count++;
		}
		return count;
	}
}
