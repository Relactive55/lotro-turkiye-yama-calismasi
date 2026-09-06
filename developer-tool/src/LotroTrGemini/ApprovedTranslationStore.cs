using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LotroTrGemini;

/// <summary>
/// Güncelleme metinlerini yalnız doğrulandıkları DAT anahtarında uygular.
/// Kaynak-metin belleğinin kısa veya bağlama göre değişen UI sözcüklerini
/// yanlış yere taşıma riskini böylece ortadan kaldırır.
/// </summary>
public sealed class ApprovedTranslationStore
{
	private sealed class Rule
	{
		public string Source;

		public string Target;
	}

	private readonly Dictionary<string, Rule> _rules = new Dictionary<string, Rule>(StringComparer.Ordinal);

	public int Count => _rules.Count;

	public ApprovedTranslationStore(string path)
	{
		Load(path);
	}

	private void Load(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return;
		}
		foreach (string line in File.ReadLines(path, Encoding.UTF8))
		{
			if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
			{
				continue;
			}
			string[] fields = line.Split(new[] { '\t' }, 3);
			if (fields.Length != 3 || string.Equals(fields[0], "Key", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string key = fields[0].Trim();
			string source = Unescape(fields[1]);
			string target = Unescape(fields[2]);
			if (key.Length == 0 || source.Length == 0 || target.Length == 0 || string.Equals(source, target, StringComparison.Ordinal))
			{
				continue;
			}
			if (TranslationQuality.LooksUnsafeApprovedPair(source, target))
			{
				continue;
			}
			_rules[key] = new Rule { Source = source, Target = target };
		}
	}

	public int ApplyToRows(IList<LocRow> rows)
	{
		if (rows == null || rows.Count == 0 || _rules.Count == 0)
		{
			return 0;
		}
		int changed = 0;
		foreach (LocRow row in rows)
		{
			if (row == null || (LocWriteGuard.IsCriticalUiDid(row.Did) && !IsAllowedCriticalKey(row.Key)) || !_rules.TryGetValue(row.Key, out Rule rule))
			{
				continue;
			}
			string source = row.Original ?? "";
			if (!string.Equals(source, rule.Source, StringComparison.Ordinal))
			{
				continue;
			}
			// Bu dosyadaki hedefler insan denetiminden geçmiş ve kaynak satırın
			// baş/son boşluklarıyla birlikte hazırlanmıştır. Genel metin düzelticisi
			// Trim/boşluk normalizasyonu yaptığı için dinamik segment birleşimlerini
			// bozabilir; onaylı hedefi byte-anlamlı sınırlarıyla aynen kullan.
			string target = rule.Target;
			if (string.IsNullOrWhiteSpace(target) || string.Equals(source, target, StringComparison.Ordinal) || TranslationQuality.LooksUnsafeApprovedPair(source, target))
			{
				continue;
			}
			row.Translation = target;
			changed++;
		}
		return changed;
	}

	/// <summary>
	/// Yazım aşamasında genel "özel ad/teknik metin" sezgisinin, insan
	/// denetiminden geçmiş tam Key+Source+Target kuralını geri çevirmesini önler.
	/// Üç alanın da ordinal olarak aynı olması gerekir.
	/// </summary>
	public bool IsApprovedTarget(string key, string source, string target)
	{
		return !string.IsNullOrEmpty(key)
			&& _rules.TryGetValue(key, out Rule rule)
			&& string.Equals(rule.Source ?? "", source ?? "", StringComparison.Ordinal)
			&& string.Equals(rule.Target ?? "", target ?? "", StringComparison.Ordinal);
	}

	private static bool IsAllowedCriticalKey(string key)
	{
		if (string.IsNullOrEmpty(key) || key.Length < 8)
		{
			return false;
		}
		uint did;
		return uint.TryParse(key.Substring(0, 8), System.Globalization.NumberStyles.HexNumber,
			System.Globalization.CultureInfo.InvariantCulture, out did)
			&& LocWriteGuard.IsCriticalUiDid(unchecked((int)did));
	}

	private static string Unescape(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return "";
		}
		StringBuilder output = new StringBuilder(value.Length);
		for (int i = 0; i < value.Length; i++)
		{
			if (value[i] == '\\' && i + 1 < value.Length)
			{
				switch (value[i + 1])
				{
					case 'n':
						output.Append('\n');
						i++;
						continue;
					case 't':
						output.Append('\t');
						i++;
						continue;
					case '\\':
						output.Append('\\');
						i++;
						continue;
				}
			}
			output.Append(value[i]);
		}
		return output.ToString();
	}
}
