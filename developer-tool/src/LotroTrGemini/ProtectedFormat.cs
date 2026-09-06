using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LotroTrGemini;

public sealed class ProtectedFormatResult
{
	public bool IsValid { get; internal set; }
	public string Reason { get; internal set; }
	public IReadOnlyList<string> SourceTokens { get; internal set; }
	public IReadOnlyList<string> TranslationTokens { get; internal set; }
	public IReadOnlyList<string> SourceNumbers { get; internal set; }
	public IReadOnlyList<string> TranslationNumbers { get; internal set; }
}

/// <summary>
/// Fail-closed validation for LOTRO placeholders, control codes, tags and numeric values.
/// Tokens are compared as an ordered stream; XML-like tags are additionally checked as a stack.
/// </summary>
public static class ProtectedFormat
{
	private static readonly Regex TokenRegex = new Regex(
		@"%(?:\d+\$)?[sdif]|\{\d+(?:[^}]*)?\}|\\[nrt]|</?[^<>]+>|(?<![\p{L}\p{N}_])\[[^\]\r\n]{1,64}\]",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);
	private static readonly Regex NumberRegex = new Regex(
		@"(?<![\p{L}_])[-+]?(?:\d+(?:[.,]\d+)?)(?![\p{L}_])",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private sealed class Parsed
	{
		public readonly List<string> Tokens = new List<string>();
		public readonly List<string> Numbers = new List<string>();
		public bool Valid = true;
		public string Reason;
	}

	public static ProtectedFormatResult Validate(string source, string translation)
	{
		Parsed expected = Parse(source);
		Parsed actual = Parse(translation);
		ProtectedFormatResult result = new ProtectedFormatResult
		{
			SourceTokens = expected.Tokens,
			TranslationTokens = actual.Tokens,
			SourceNumbers = expected.Numbers,
			TranslationNumbers = actual.Numbers,
			IsValid = false
		};
		if (!expected.Valid)
		{
			result.Reason = "source protected format invalid: " + expected.Reason;
			return result;
		}
		if (!actual.Valid)
		{
			result.Reason = "translation protected format invalid: " + actual.Reason;
			return result;
		}
		if (!SequenceEqual(expected.Tokens, actual.Tokens))
		{
			result.Reason = "ordered protected token stream differs";
			return result;
		}
		if (!SequenceEqual(expected.Numbers, actual.Numbers))
		{
			result.Reason = "numeric/game value stream differs";
			return result;
		}
		result.IsValid = true;
		result.Reason = "ok";
		return result;
	}

	public static bool HasSameProtectedTokens(string source, string translation)
	{
		return Validate(source, translation).IsValid;
	}

	/// <summary>
	/// Returns a compact digest of the ordered protected-token and numeric-value
	/// streams. It is metadata, not a replacement for validating the actual
	/// source/target pair.
	/// </summary>
	public static string GetTokenSignature(string text)
	{
		Parsed parsed = Parse(text ?? string.Empty);
		StringBuilder canonical = new StringBuilder("lotro-token-signature-v1|");
		canonical.Append(parsed.Valid ? "valid|" : "invalid|");
		canonical.Append("tokens=");
		foreach (string token in parsed.Tokens)
		{
			canonical.Append(token.Length).Append(':').Append(token).Append('|');
		}
		canonical.Append("numbers=");
		foreach (string number in parsed.Numbers)
		{
			canonical.Append(number.Length).Append(':').Append(number).Append('|');
		}
		using (SHA256 sha = SHA256.Create())
		{
			byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
			StringBuilder result = new StringBuilder(hash.Length * 2);
			foreach (byte value in hash) result.Append(value.ToString("x2"));
			return result.ToString();
		}
	}

	private static Parsed Parse(string text)
	{
		Parsed parsed = new Parsed();
		if (text == null)
		{
			parsed.Valid = false;
			parsed.Reason = "null text";
			return parsed;
		}
		MatchCollection matches = TokenRegex.Matches(text);
		List<Tuple<int, int>> protectedSpans = new List<Tuple<int, int>>(matches.Count);
		Stack<string> tagStack = new Stack<string>();
		HashSet<string> openingTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		HashSet<string> closingTagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Match match in matches)
		{
			if (match.Value.Length > 2 && match.Value[0] == '<'
				&& TryParseTag(match.Value, out string tagName, out bool opening, out bool closing, out bool selfClosing)
				&& !selfClosing)
			{
				if (opening) openingTagNames.Add(tagName);
				if (closing) closingTagNames.Add(tagName);
			}
		}
		HashSet<string> pairedTagNames = new HashSet<string>(openingTagNames, StringComparer.OrdinalIgnoreCase);
		pairedTagNames.IntersectWith(closingTagNames);
		if (openingTagNames.Count != 0 && closingTagNames.Count != 0 && pairedTagNames.Count == 0)
		{
			parsed.Valid = false;
			parsed.Reason = "tag opening/closing names differ";
			return parsed;
		}
		foreach (Match match in matches)
		{
			parsed.Tokens.Add(match.Value);
			protectedSpans.Add(Tuple.Create(match.Index, match.Index + match.Length));
			if (match.Value[0] != '<')
			{
				continue;
			}
			if (!TryParseTag(match.Value, out string name, out bool opening, out bool closing, out bool selfClosing))
			{
				parsed.Valid = false;
				parsed.Reason = "malformed tag " + match.Value;
				return parsed;
			}
			// LOTRO also uses angle-bracket placeholders such as <name>,
			// <plugin name> and <insert instructions>. They are protected as
			// exact opaque tokens, but they are not XML nesting elements. Only
			// names that actually have a closing token in this value participate
			// in the stack check.
			if (!pairedTagNames.Contains(name))
			{
				continue;
			}
			if (selfClosing)
			{
				continue;
			}
			if (opening)
			{
				tagStack.Push(name);
			}
			else if (closing)
			{
				// Localization records can be fragments: one value may begin by
				// closing a tag opened in the previous value, or end with a tag
				// closed in the next value. Exact ordered token equality still
				// protects those boundary tags. Validate nesting only while this
				// value has an active local stack.
				if (tagStack.Count == 0)
				{
					continue;
				}
				if (!string.Equals(tagStack.Pop(), name, StringComparison.OrdinalIgnoreCase))
				{
					parsed.Valid = false;
					parsed.Reason = "tag nesting mismatch at " + match.Value;
					return parsed;
				}
			}
		}
		// An unmatched trailing opening tag may close in the next localized
		// value. Its exact token is still compared source-to-target.
		foreach (Match number in NumberRegex.Matches(text))
		{
			if (protectedSpans.Any(span => number.Index < span.Item2 && number.Index + number.Length > span.Item1))
			{
				continue;
			}
			parsed.Numbers.Add(NormalizeNumber(number.Value));
		}
		return parsed;
	}

	private static bool TryParseTag(string raw, out string name, out bool opening, out bool closing, out bool selfClosing)
	{
		name = null;
		opening = false;
		closing = false;
		selfClosing = false;
		if (raw.Length < 3 || raw[0] != '<' || raw[raw.Length - 1] != '>') return false;
		string inner = raw.Substring(1, raw.Length - 2).Trim();
		if (inner.Length == 0) return false;
		if (inner[0] == '/')
		{
			closing = true;
			inner = inner.Substring(1).Trim();
		}
		if (inner.EndsWith("/", StringComparison.Ordinal))
		{
			selfClosing = true;
			inner = inner.Substring(0, inner.Length - 1).Trim();
		}
		Match nameMatch = Regex.Match(inner, @"^[A-Za-z][A-Za-z0-9_.:-]*");
		if (!nameMatch.Success) return false;
		name = nameMatch.Value;
		if (!closing && !selfClosing) opening = true;
		return true;
	}

	private static bool SequenceEqual(IList<string> left, IList<string> right)
	{
		if (left.Count != right.Count) return false;
		for (int i = 0; i < left.Count; i++)
		{
			if (!string.Equals(left[i], right[i], StringComparison.Ordinal)) return false;
		}
		return true;
	}

	private static string NormalizeNumber(string value)
	{
		return value.Replace(',', '.');
	}
}
