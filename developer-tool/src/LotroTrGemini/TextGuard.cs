using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LotroTrGemini;

public static class TextGuard
{
	private static readonly HashSet<string> EnUiWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"a", "an", "the", "of", "and", "or", "to", "for", "in", "on",
		"at", "by", "with", "from", "into", "your", "you", "is", "are", "was",
		"were", "be", "been", "will", "shall", "can", "may", "must", "not", "no",
		"yes", "all", "any", "some", "this", "that", "these", "those", "repair", "shop",
		"tool", "tools", "head", "chest", "feet", "back", "legs", "wrist", "ranged", "off-hand",
		"offhand", "weak", "tough", "brittle", "substantial", "flimsy", "normal", "processing", "complete", "quest",
		"accept", "decline", "use", "item", "items", "bag", "bags", "slot", "slots", "level",
		"class", "race", "trait", "traits", "skill", "skills", "damage", "armour", "armor", "defence",
		"defense", "power", "morale", "fate", "will", "might", "agility", "vitality", "woodworking", "prospector",
		"aspiring", "crafter", "tailor", "skinning", "knife", "farming", "cooking", "supplies", "scholar", "glass",
		"smithing", "hammer", "forester", "axe", "jeweller", "jeweler", "map", "note", "notes", "right",
		"left", "middle", "north", "south", "east", "west", "click", "select", "press", "bind",
		"unbound", "bound", "empty", "full", "new", "old", "high", "low", "max", "min",
		"next", "previous", "done", "cancel", "close", "open", "buy", "sell", "trade", "vendor",
		"store", "price", "cost", "free", "rare", "unique", "legendary", "common", "start", "stop",
		"pause", "resume", "load", "save", "settings", "options", "exit", "quit", "help", "about",
		"error", "warning", "success", "failed", "failure", "unknown", "none", "default", "custom", "player",
		"enemy", "monster", "creature", "boss", "minion", "ally", "friend", "group", "fellowship", "raid",
		"instance", "region", "area", "zone", "loc", "location", "objective", "reward", "rewards", "xp",
		"experience", "gold", "silver", "copper", "quantity", "amount", "count", "total", "remaining", "current",
		"available", "unavailable", "required", "optional", "white", "black", "red", "blue", "green", "yellow",
		"orange", "purple", "brown", "grey", "gray", "dark", "light", "dagger", "javelin", "spear",
		"shield", "halberd", "polearm", "hammer", "club", "staff", "bow", "sword", "axe", "mace",
		"crossbow", "weapon", "weapons", "armour", "armor", "one-handed", "two-handed", "one", "handed", "two",
		"hand", "aura", "hammer", "club", "axe", "knife", "torch", "key", "coin", "potion",
		"scroll", "recipe", "material", "component", "ingredient", "trophy", "deed", "emote",
		"character", "equipment", "cosmetic", "outfits", "show", "basic", "stats", "offence", "critical",
		"rating", "physical", "mastery", "avoidance", "parry", "evade", "enhance", "title", "biography",
		"wallet", "house", "hobby", "reputation", "war"
	};

	private static readonly Regex TokenRe = new Regex("[A-Za-z]+(?:['’][A-Za-z]+)?(?:-[A-Za-z]+)?", RegexOptions.Compiled);

	private static readonly Regex ProtectedTokenRe = new Regex("%(?:\\d+\\$)?[sdif]|\\{\\d+(?:[^}]*)?\\}|\\\\[nrt]|<[^>]+>|\\[[^\\]\\r\\n]{1,64}\\]", RegexOptions.Compiled);

	public static bool ShouldKeepAsIs(string text)
	{
		if (text == null || text.Length == 0)
		{
			return true;
		}
		if (text.Length == 1)
		{
			if (char.IsLetter(text[0]))
			{
				return false;
			}
			return true;
		}
		bool flag = false;
		bool flag2 = false;
		foreach (char c in text)
		{
			if (!char.IsWhiteSpace(c))
			{
				flag2 = true;
				if (char.IsLetter(c))
				{
					flag = true;
					break;
				}
			}
		}
		if (!flag2)
		{
			return true;
		}
		if (!flag)
		{
			return true;
		}
		string text2 = text.Trim();
		if (IsInternalPlaceholder(text2))
		{
			return true;
		}
		string visible = ProtectedTokenRe.Replace(text2, "");
		if (!HasLetter(visible))
		{
			return true;
		}
		// Şirket çalışanı unvanları ve hukuk/kredi satırları oyun metni değildir.
		// Çeviri belleğindeki eski boşluk normalizasyonlarının bu satırları
		// değiştirmesine izin verme; kaynak payload'ı byte-anlamlı olarak koru.
		if (visible.IndexOf("ASSOCIATE GENERAL COUNSEL", StringComparison.OrdinalIgnoreCase) >= 0 &&
			visible.IndexOf("LEGAL AND BUSINESS AFFAIRS", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		if (string.Equals(text2, "the", StringComparison.Ordinal) || string.Equals(text2, "a", StringComparison.Ordinal) || string.Equals(text2, "an", StringComparison.Ordinal))
		{
			return true;
		}
		if (text2.Length <= 6 && text2.Length >= 2 && text2[0] == '[' && text2[text2.Length - 1] == ']')
		{
			return true;
		}
		if (IsLikelyProperName(text2))
		{
			return true;
		}
		return false;
	}

	public static bool IsLikelyProperName(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		string text2 = text.Trim();
		if (text2.Length < 2 || text2.Length > 48)
		{
			return false;
		}
		if (text2.IndexOfAny(new char[12]
		{
			'\n', '\r', '%', '{', '}', '<', '>', '=', ':', ';',
			'?', '!'
		}) >= 0)
		{
			return false;
		}
		for (int i = 0; i < text2.Length; i++)
		{
			if (char.IsDigit(text2[i]))
			{
				return false;
			}
		}
		MatchCollection matchCollection = TokenRe.Matches(text2);
		if (matchCollection.Count == 0 || matchCollection.Count > 5)
		{
			return false;
		}
		int num = 0;
		for (int j = 0; j < text2.Length; j++)
		{
			if (char.IsLetter(text2[j]))
			{
				num++;
			}
		}
		int num2 = 0;
		foreach (Match item in matchCollection)
		{
			num2 += item.Value.Length;
		}
		if (num != num2)
		{
			return false;
		}
		int num3 = 0;
		int num4 = 0;
		foreach (Match item2 in matchCollection)
		{
			string value = item2.Value;
			if (EnUiWords.Contains(value))
			{
				num3++;
				if (!IsParticle(value))
				{
					return false;
				}
			}
			else
			{
				if (!IsTitleishWord(value))
				{
					return false;
				}
				num4++;
			}
		}
		if (num4 < 1)
		{
			return false;
		}
		if (num4 == 0)
		{
			return false;
		}
		if (matchCollection.Count == 1)
		{
			string value2 = matchCollection[0].Value;
			if (EnUiWords.Contains(value2))
			{
				return false;
			}
			if (char.IsLower(value2[0]))
			{
				return false;
			}
			if (IsAllCaps(value2) && value2.Length <= 6)
			{
				return true;
			}
			if (IsTitleishWord(value2))
			{
				return value2.Length >= 3;
			}
			return false;
		}
		return num4 >= matchCollection.Count - num3;
	}

	private static bool IsParticle(string w)
	{
		if (!string.Equals(w, "of", StringComparison.OrdinalIgnoreCase) && !string.Equals(w, "the", StringComparison.OrdinalIgnoreCase) && !string.Equals(w, "de", StringComparison.OrdinalIgnoreCase) && !string.Equals(w, "von", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(w, "van", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static bool IsTitleishWord(string w)
	{
		if (string.IsNullOrEmpty(w))
		{
			return false;
		}
		if (w.Length == 1)
		{
			return char.IsUpper(w[0]);
		}
		if (char.IsUpper(w[0]))
		{
			bool flag = true;
			for (int i = 1; i < w.Length; i++)
			{
				char c = w[i];
				if (c != '\'' && c != '’' && c != '-' && char.IsLetter(c) && !char.IsLower(c) && i > 0)
				{
					if (IsAllCaps(w))
					{
						return true;
					}
					if (i != 1 || (w[0] != 'M' && w[0] != 'O'))
					{
						flag = false;
						break;
					}
				}
			}
			if (IsAllCaps(w))
			{
				if (w.Length >= 2)
				{
					return w.Length <= 12;
				}
				return false;
			}
			if (!flag)
			{
				return Regex.IsMatch(w, "^[A-Z][a-z]+(?:['’][A-Za-z]+)?(?:-[A-Z][a-z]+)?$");
			}
			return true;
		}
		return false;
	}

	private static bool IsAllCaps(string w)
	{
		bool result = false;
		for (int i = 0; i < w.Length; i++)
		{
			if (char.IsLetter(w[i]))
			{
				result = true;
				if (!char.IsUpper(w[i]))
				{
					return false;
				}
			}
		}
		return result;
	}

	public static bool IsCorruptTranslation(string original, string translation)
	{
		if (original == null)
		{
			return false;
		}
		if (translation == null)
		{
			return true;
		}
		if (string.Equals(original, translation, StringComparison.Ordinal))
		{
			return false;
		}
		if (!HasSameProtectedTokens(original, translation))
		{
			return true;
		}
		if (ShouldKeepAsIs(original) && !string.Equals(original.Trim(), translation.Trim(), StringComparison.Ordinal))
		{
			return true;
		}
		if (original.Length <= 3 && !HasLetter(original) && HasLetter(translation))
		{
			return true;
		}
		if (HasCyrillic(translation) && !HasCyrillic(original) && !HasLetter(original))
		{
			return true;
		}
		if (original.Length <= 2 && translation.Length >= 3 && !HasLetter(original))
		{
			return true;
		}
		return false;
	}

	private static bool IsInternalPlaceholder(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}
		string value = text.Trim();
		return value.StartsWith("DNT", StringComparison.OrdinalIgnoreCase)
			|| value.StartsWith("TBD", StringComparison.OrdinalIgnoreCase)
			|| value.StartsWith("TODO", StringComparison.OrdinalIgnoreCase)
			|| value.StartsWith("WIP", StringComparison.OrdinalIgnoreCase)
			|| value.StartsWith("DEBUG", StringComparison.OrdinalIgnoreCase)
			|| value.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "NOT USED", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "NOT PUBLISHED", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "Generic Quest Items[ps]", StringComparison.OrdinalIgnoreCase);
	}

	public static bool HasSameProtectedTokens(string original, string translation)
	{
		return ProtectedFormat.HasSameProtectedTokens(original, translation);
	}

	public static string Sanitize(string original, string translation, bool allowCompact = true)
	{
		if (original == null)
		{
			return translation;
		}
		string text = TurTextFix.ExactForEnglish(original);
		if (text != null)
		{
			return text;
		}
		if (ShouldKeepAsIs(original))
		{
			if (!allowCompact && translation != null && translation.Length > 0)
			{
				return translation;
			}
			return original;
		}
		if (IsCorruptTranslation(original, translation))
		{
			return original;
		}
		string text2 = translation ?? original;
		if (allowCompact)
		{
			text2 = TurTextFix.Fix(text2, original);
		}
		else if (text2.IndexOf("Please provide the text you want me to translate", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return original;
		}
		if (string.IsNullOrEmpty(text2) && !string.IsNullOrEmpty(original))
		{
			return original;
		}
		if (allowCompact)
		{
			text2 = TurCompact.FitToBudget(original, text2);
			if (string.IsNullOrEmpty(text2) && !string.IsNullOrEmpty(original))
			{
				return original;
			}
		}
		return text2;
	}

	private static bool HasLetter(string s)
	{
		if (s == null)
		{
			return false;
		}
		for (int i = 0; i < s.Length; i++)
		{
			if (char.IsLetter(s[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasCyrillic(string s)
	{
		if (s == null)
		{
			return false;
		}
		for (int i = 0; i < s.Length; i++)
		{
			if (s[i] >= 'Ѐ' && s[i] <= 'ӿ')
			{
				return true;
			}
		}
		return false;
	}
}
