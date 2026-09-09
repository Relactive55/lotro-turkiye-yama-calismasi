using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LotroTrGemini;

public static class TurTextFix
{
	private static readonly string[][] PhraseFixes = new string[18][]
	{
		new string[2] { "Please provide the text you want me to translate.", "" },
		new string[2] { "Demir Katlama The Ironfold", "Demir Kıvrım (The Ironfold)" },
		new string[2] { "Demir Katlama", "Demir Kıvrım" },
		new string[2] { "isimlendirme Yönergeleri", "İsimlendirme Yönergeleri" },
		new string[2] { "isimlendirme yönergeleri", "İsimlendirme yönergeleri" },
		new string[2] { "Mevcut Görev Eylemleri Available to you.", "Mevcut Görev Eylemleri" },
		new string[2] { "Mevcut Görev Eylemleri Available to you", "Mevcut Görev Eylemleri" },
		new string[2] { " Available to you.", "" },
		new string[2] { " Available to you", "" },
		new string[2] { " and claim your reward!", "" },
		new string[2] { " and claim your reward.", "" },
		new string[2] { " and claim your reward", "" },
		new string[2] { " and claim the reward.", "" },
		new string[2] { " and claim victory!", "" },
		new string[2] { " and claim victory", "" },
		new string[2] { " Available to barter.", " Takas edilebilir." },
		new string[2] { "Tohumlarıyla with", "tohumlarıyla" },
		new string[2] { "Toplu Onar", "Tümünü Onar" }
	};

	private static readonly Dictionary<string, string> ExactEn = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		{ "Tough", "Sert" },
		{ "Brittle", "Kırılgan" },
		{ "Substantial", "Sağlam" },
		{ "Flimsy", "Dayanıksız" },
		{ "Weak", "Zayıf" },
		{ "Repair All", "Tümünü Onar" },
		{ "Total:", "Toplam:" },
		{ "Back", "Geri" },
		{ "Cancel", "İptal" },
		{ "Accept", "Kabul" },
		{ "Decline", "Reddet" },
		{ "Continue", "Devam" },
		{ "Close", "Kapat" },
		{ "Options", "Seçenekler" },
		{ "Settings", "Ayarlar" },
		{ "Dagger[E]", "Hançer[E]" },
		{ "Javelin[E]", "Cirit[E]" },
		{ "Spear[E]", "Mızrak[E]" },
		{ "Shield[E]", "Kalkan[E]" },
		{ "Halberd[E]", "Balta-mızrak[E]" },
		{ "Polearm[E]", "Saplı silah[E]" },
		{ "One-handed Hammer", "Tek Elli Çekiç" },
		{ "Two-handed Club", "Çift Elli Sopalık" },
		{ "Weapon Aura", "Silah Aurası" },
		{ "Quest Actions available", "Mevcut Görev Eylemleri" },
		{ "Host:", "Sunucu:" },
		{ "Enter Monster Play", "Canavar Oyununa Gir" },
		{ "Character", "Karakter" },
		{ "Equipment", "Ekipman" },
		{ "Cosmetic Outfits", "Kozmetik Kıyafetler" },
		{ "Show All Stats", "Tüm İst. Göster" },
		{ "Basic Stats", "Temel İstatistikler" },
		{ "Morale", "Moral" },
		{ "Power", "Güç" },
		{ "Armour", "Zırh" },
		{ "Might", "Kudret" },
		{ "Agility", "Çeviklik" },
		{ "Vitality", "Zindelik" },
		{ "Will", "İrade" },
		{ "Fate", "Kader" },
		{ "Offence", "Saldırı" },
		{ "Critical Rating", "Kritik Vuruş Oranı" },
		{ "Physical Mastery", "Fiziksel Ustalık" },
		{ "Defence", "Savunma" },
		{ "Avoidance", "Kaçınma" },
		{ "Parry", "Savuşturma" },
		{ "Evade", "Kaçınma" },
		{ "Enhance Character", "Karakteri Geliştir" },
		{ "Skills", "Yetenekler" },
		{ "Title", "Unvan" },
		{ "Biography", "Biyografi" },
		{ "Wallet", "Cüzdan" },
		{ "House", "Ev" },
		{ "Hobby", "Hobi" },
		{ "Reputation", "İtibar" },
		{ "The War", "Savaş" }
	};

	public static string ExactForEnglish(string en)
	{
		if (en == null)
		{
			return null;
		}
		if (ExactEn.TryGetValue(en, out var value))
		{
			return value;
		}
		return ManualUiText.ExactForEnglish(en);
	}

	public static string Fix(string tur, string en)
	{
		if (en != null)
		{
			string text = ExactForEnglish(en);
			if (text != null)
			{
				return text;
			}
		}
		if (tur == null)
		{
			tur = "";
		}
		if (tur.Length == 0)
		{
			return tur;
		}
		if (tur.IndexOf("Please provide the text you want me to translate", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "";
		}
		string[][] phraseFixes = PhraseFixes;
		foreach (string[] array in phraseFixes)
		{
			if (array[0].Length != 0 && tur.IndexOf(array[0], StringComparison.Ordinal) >= 0)
			{
				tur = tur.Replace(array[0], array[1]);
			}
		}
		tur = tur.Replace("þ", "ş").Replace("Þ", "Ş").Replace("ð", "ğ")
			.Replace("Ð", "Ğ")
			.Replace("Ã¼", "ü")
			.Replace("Ã¶", "ö")
			.Replace("Ã§", "ç")
			.Replace("ÄŸ", "ğ")
			.Replace("ÅŸ", "ş")
			.Replace("Ä±", "ı")
			.Replace("Ãœ", "Ü")
			.Replace("Ã–", "Ö")
			.Replace("Ã‡", "Ç");
		tur = Regex.Replace(tur, "\\s+Available to you\\.?\\s*$", "", RegexOptions.IgnoreCase);
		tur = Regex.Replace(tur, "\\s+and claim your reward[!.,]?\\s*$", "", RegexOptions.IgnoreCase);
		tur = Regex.Replace(tur, "\\s+and claim the reward[!.,]?\\s*$", "", RegexOptions.IgnoreCase);
		tur = Regex.Replace(tur, "\\s+and claim victory[!.,]?\\s*$", "", RegexOptions.IgnoreCase);
		tur = Regex.Replace(tur, "\\s+with\\s*$", "", RegexOptions.IgnoreCase);
		tur = Regex.Replace(tur, " {2,}", " ");
		tur = Regex.Replace(tur, "\\.\\.+", ".");
		tur = Regex.Replace(tur, "\\s+\\.", ".");
		tur = Regex.Replace(tur, "\\s+,", ",");
		// LOTRO uses m/s suffixes for minutes/seconds in skill and item
		// tooltips.  Translate only an already-translated value so source
		// English remains untouched and the ms (milliseconds) token is safe.
		if (!string.IsNullOrEmpty(en) && !string.Equals(tur, en, StringComparison.Ordinal))
		{
			tur = Regex.Replace(tur, @"(?<![\\p{L}\\d])(\\d+(?:[.,]\\d+)?)\\s*m(?![\\p{L}])", "$1 dk", RegexOptions.IgnoreCase);
			tur = Regex.Replace(tur, @"(?<![\\p{L}\\d])(\\d+(?:[.,]\\d+)?)\\s*s(?![\\p{L}])", "$1 sn", RegexOptions.IgnoreCase);
		}
		tur = tur.Trim();
		tur = Regex.Replace(tur, "(>)isimlendirme", "$1İsimlendirme");
		if (tur == "Zor" && en != null && (en == "Tough" || en.EndsWith("Tough")))
		{
			tur = "Sert";
		}
		if (tur == "Önemli" && en == "Substantial")
		{
			tur = "Sağlam";
		}
		if (tur == "Toplu Onar" && en == "Repair All")
		{
			tur = "Tümünü Onar";
		}
		if (!string.IsNullOrEmpty(en) && en.Length > 12 && tur.Length > en.Length && tur.EndsWith(en, StringComparison.Ordinal))
		{
			tur = tur.Substring(0, tur.Length - en.Length).Trim();
		}
		return tur;
	}
}
