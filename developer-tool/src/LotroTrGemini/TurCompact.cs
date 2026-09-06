using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LotroTrGemini;

public static class TurCompact
{
	private static readonly string[][] Phrases = new string[43][]
	{
		new string[2] { "lütfen ", "" },
		new string[2] { "Lütfen ", "" },
		new string[2] { "şimdi ", "" },
		new string[2] { "Şimdi ", "" },
		new string[2] { "aşağıdaki ", "" },
		new string[2] { "Aşağıdaki ", "" },
		new string[2] { "yukarıdaki ", "" },
		new string[2] { "bulunmaktadır", "var" },
		new string[2] { "bulunuyor", "var" },
		new string[2] { "gerekmektedir", "gerekli" },
		new string[2] { "gerekmektedir.", "gerekli." },
		new string[2] { "mümkün değildir", "olamaz" },
		new string[2] { "mümkün değil", "olamaz" },
		new string[2] { "yapabilirsiniz", "yapabilirsin" },
		new string[2] { "edebilirsiniz", "edebilirsin" },
		new string[2] { "seçebilirsiniz", "seçebilirsin" },
		new string[2] { "kullanabilirsiniz", "kullanabilirsin" },
		new string[2] { "gidebilirsiniz", "gidebilirsin" },
		new string[2] { "sahip olmanız gerekir", "gerekli" },
		new string[2] { "sahip olmalısınız", "gerekli" },
		new string[2] { "izin verilmez", "yasak" },
		new string[2] { "izin verilmiyor", "yasak" },
		new string[2] { "yer alan", "" },
		new string[2] { "olarak bilinen", "" },
		new string[2] { " şeklinde", "" },
		new string[2] { " bir şekilde", "" },
		new string[2] { " nedeniyle", " yüzünden" },
		new string[2] { " sebebiyle", " yüzünden" },
		new string[2] { " ile ilgili", " hakkında" },
		new string[2] { " hakkında daha fazla", " hakkında" },
		new string[2] { "çalışın lütfen", "çalış" },
		new string[2] { "isteklerinizi yavaşlatın", "yavaşlayın" },
		new string[2] { "Hızlı eşya tamir etmeye çalışıyorsunuz", "Çok hızlı tamir" },
		new string[2] { "Tamir için yeterli paran yok", "Tamir için paran yetmiyor" },
		new string[2] { "Yeterli paran yok", "Paran yetmiyor" },
		new string[2] { "kullanılamıyor", "kapalı" },
		new string[2] { "şu anda kullanılamıyor", "şu an kapalı" },
		new string[2] { "Karakter şu anda kullanılamıyor", "Karakter kapalı" },
		new string[2] { "Tek Elle Kullanılan ", "Tek Elli " },
		new string[2] { "İki Elle Kullanılan ", "Çift Elli " },
		new string[2] { "İki Ellikli ", "Çift Elli " },
		new string[2] { "Tek Elle ", "Tek Elli " },
		new string[2] { "  ", " " }
	};

	private static readonly Regex MultiSpace = new Regex(" {2,}", RegexOptions.Compiled);

	private static readonly Regex SpaceBeforePunct = new Regex("\\s+([,.!?;:])", RegexOptions.Compiled);

	public static int BudgetChars(string en)
	{
		if (string.IsNullOrEmpty(en))
		{
			return 12;
		}
		int length = en.Length;
		if (length <= 3)
		{
			return length + 8;
		}
		if (length <= 12)
		{
			return Math.Max(length + 4, (int)((double)length * 1.35));
		}
		if (length <= 40)
		{
			return (int)((double)length * 1.2) + 2;
		}
		if (length <= 120)
		{
			return (int)((double)length * 1.12) + 2;
		}
		return (int)((double)length * 1.08) + 4;
	}

	public static string FitToBudget(string en, string tr)
	{
		if (string.IsNullOrEmpty(tr))
		{
			return tr ?? "";
		}
		if (string.IsNullOrEmpty(en))
		{
			return tr;
		}
		List<string> map = new List<string>();
		string tur = Protect(tr, map);
		tur = ApplyPhrases(tur);
		tur = MultiSpace.Replace(tur, " ").Trim();
		tur = SpaceBeforePunct.Replace(tur, "$1");
		int num = BudgetChars(en);
		if (tur.Length <= num)
		{
			return Restore(tur, map);
		}
		tur = ApplyPhrases(tur);
		tur = ShorterVerbs(tur);
		tur = MultiSpace.Replace(tur, " ").Trim();
		if (tur.Length <= num)
		{
			return Restore(tur, map);
		}
		tur = TrimToBudget(tur, num);
		return Restore(tur, map);
	}

	private static string Protect(string s, List<string> map)
	{
		return Regex.Replace(s, "(\\\\n|\\\\r|%\\d*\\$?[sdif]|%\\w+|\\{[^}]+\\}|\\[[^\\]]+\\])", delegate(Match m)
		{
			int count = map.Count;
			map.Add(m.Value);
			return "\ue000" + count.ToString("X") + "\ue001";
		});
	}

	private static string Restore(string s, List<string> map)
	{
		if (map.Count == 0)
		{
			return s;
		}
		return Regex.Replace(s, "\\uE000([0-9A-Fa-f]+)\\uE001", delegate(Match m)
		{
			int num = Convert.ToInt32(m.Groups[1].Value, 16);
			return (num >= 0 && num < map.Count) ? map[num] : m.Value;
		});
	}

	private static string ApplyPhrases(string tur)
	{
		string[][] phrases = Phrases;
		foreach (string[] array in phrases)
		{
			if (array[0].Length != 0 && tur.IndexOf(array[0], StringComparison.Ordinal) >= 0)
			{
				tur = tur.Replace(array[0], array[1]);
			}
		}
		return tur;
	}

	private static string ShorterVerbs(string tur)
	{
		tur = Regex.Replace(tur, "abilirsiniz\\b", "ebilirsin");
		tur = Regex.Replace(tur, "abilirsiniz\\b", "ebilirsin");
		tur = Regex.Replace(tur, "malısınız\\b", "malısın");
		tur = Regex.Replace(tur, "melisiniz\\b", "melisin");
		tur = Regex.Replace(tur, "yor musunuz\\b", "yor musun");
		tur = Regex.Replace(tur, "yor musunuz\\?", "yor musun?");
		tur = Regex.Replace(tur, "\\bçok fazla\\b", "çok");
		tur = Regex.Replace(tur, "\\bbiraz daha\\b", "daha");
		tur = Regex.Replace(tur, "\\bherhangi bir\\b", "bir");
		tur = Regex.Replace(tur, "\\bbu işlemi\\b", "bunu");
		tur = Regex.Replace(tur, "\\byapmaya çalışıyorsunuz\\b", "deniyorsun");
		tur = Regex.Replace(tur, "\\byapmaya çalışıyorsun\\b", "deniyorsun");
		return tur;
	}

	private static string TrimToBudget(string tur, int bud)
	{
		if (tur.Length <= bud)
		{
			return tur;
		}
		if (bud < 8)
		{
			return tur.Substring(0, Math.Min(tur.Length, bud)).Trim();
		}
		int num = -1;
		for (int num2 = Math.Min(tur.Length - 1, bud); num2 >= bud * 2 / 3; num2--)
		{
			char c = tur[num2];
			if (c == '.' || c == '!' || c == '?' || c == '\n')
			{
				num = num2 + 1;
				break;
			}
		}
		if (num < 0)
		{
			for (int num3 = Math.Min(tur.Length - 1, bud); num3 >= bud * 2 / 3; num3--)
			{
				if (tur[num3] == ' ' || tur[num3] == ',' || tur[num3] == ';')
				{
					num = num3;
					break;
				}
			}
		}
		if (num < 8)
		{
			num = bud;
		}
		if (num > tur.Length)
		{
			num = tur.Length;
		}
		string text = tur.Substring(0, num).Trim();
		if (text.Length > 0 && !".!?…".Contains(text[text.Length - 1].ToString()))
		{
			_ = text.Length;
		}
		return text;
	}
}
