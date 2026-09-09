using System;
using System.Collections.Generic;

namespace LotroTrGemini;

/// <summary>
/// Exact, reviewable UI strings that are too short or too contextual for the
/// source-text translation memory.  Rules are intentionally fail-closed: a
/// row-key rule is used only when the official source still has the expected
/// prefix, and a title rule only matches the complete source string.
/// </summary>
public static class ManualUiText
{
	private sealed class RowRule
	{
		public readonly string Key;
		public readonly string SourcePrefix;
		public readonly string Target;

		public RowRule(string key, string sourcePrefix, string target)
		{
			Key = key;
			SourcePrefix = sourcePrefix;
			Target = target;
		}
	}

	private static readonly Dictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		// Character creation and equipment slots.
		{ "Skin", "Ten" }, { "Face", "Yüz" }, { "Eyes", "Gözler" }, { "Hair", "Saç" },
		{ "Nose", "Burun" }, { "Jaw", "Çene" }, { "Mouth", "Ağız" },
		{ "Class slot", "Sınıf yuvası" }, { "Class Slot", "Sınıf yuvası" },
		{ "Shoulder slot", "Omuz yuvası" }, { "Head slot", "Baş yuvası" },
		{ "Back slot", "Sırt yuvası" }, { "Chest slot", "Göğüs yuvası" },
		{ "Gloves slot", "Eldiven yuvası" }, { "Feet slot", "Ayak yuvası" },
		{ "Belt slot", "Kemer yuvası" }, { "Ranged Weapon Slot", "Menzilli silah yuvası" },
		{ "Ranged weapon slot", "Menzilli silah yuvası" }, { "Craft Tool slot", "Zanaat aracı yuvası" },
		{ "Primary Weapon Slot", "Birincil silah yuvası" },
		{ "Secondary Weapon/Shield Slot", "İkincil silah/kalkan yuvası" },
		{ "Fully Expands Quest Tree", "Görev ağacını tamamen genişletir" },
		{ "Fully Collapses Quest Tree", "Görev ağacını tamamen daraltır" },

		// Character, reputation, filter and chat panels.
		{ "Champion", "Şampiyon" }, { "Burglar", "Hırsız" }, { "Captain", "Kumandan" },
		{ "Minstrel", "Ozan" }, { "Lore-master", "İrfan Ustası" }, { "Guardian", "Muhafız" },
		{ "Hunter", "Avcı" }, { "Rune-keeper", "Rün Alimi" }, { "Warden", "Gözcü" },
		{ "Beorning", "Deri Değiştiren" }, { "Brawler", "Dövüşçü" }, { "Mariner", "Denizci" },
		{ "Elves of Rivendell", "Ayrıkvadi Elfleri" }, { "Thorin's Hall", "Thorin Salonu" },
		{ "Men", "İnsanlar" }, { "men", "insanlar" }, { "Bree", "Bree" },
		{ "Large", "Büyük" }, { "Small", "Küçük" }, { "Misc", "Çeşitli" }, { "Misc.", "Çeşitli" },
		{ "Filter Panel", "Filtre Paneli" }, { "Loot", "Ganimet" }, { "Advancement", "İlerleme" },
		{ "Outside Depot", "Dış depo" }, { "Trade", "Takas" }, { "Trade (German)", "Takas (Almanca)" },
		{ "Trade (French)", "Takas (Fransızca)" }, { "Combat Event", "Savaş olayı" },
		{ "Event Broadcast", "Etkinlik duyurusu" }, { "Kinship Officer", "Kardeşlik görevlisi" },
		{ "World", "Dünya" }, { "World (German)", "Dünya (Almanca)" },
		{ "World (French)", "Dünya (Fransızca)" }, { "Say", "Söyle" }, { "Standard", "Standart" },
		{ "Narration", "Anlatım" }, { "User Chat", "Kullanıcı sohbeti" }, { "Regional", "Bölgesel" },
		{ "OOC", "Oyun dışı" }, { "LFF", "Grup aranıyor" },
		{ "Before the Shadow, Prologue", "Gölgeden Önce, Prolog" },

		// Skill and item tooltip vocabulary.
		{ "Set Trap", "Tuzak Kur" }, { "Quick Escape", "Hızlı Kaçış" },
		{ "Easily Inspired", "Kolay İlham Alan" }, { "Strong Men", "Güçlü İnsanlar" },
		{ "Consumed On Use", "Kullanılınca tüketilir" },
		{ "Not usable in Monster Play", "Canavar Oyununda kullanılamaz" },
		{ "Cooldown", "Bekleme süresi" }, { "Cooldown:", "Bekleme süresi:" },
		{ "Duration", "Süre" }, { "Duration:", "Süre:" },
		{ "Cooldown Remaining", "Kalan bekleme süresi" },
		{ "Cooldown Remaining:", "Kalan bekleme süresi:" },
		{ "Required", "Gerekli" }, { "Required:", "Gerekli:" }, { "Required Level:", "Gerekli seviye:" },
		{ "Class Difficulty:", "Sınıf zorluğu:" }, { "Role:", "Rolü:" },
		{ "Gameplay:", "Oyun:" }, { "Lore:", "Bilgi:" },
		{ "Gondolin", "Gondolin" }, { "Imladris", "İmladris" },
		{ "Forest Elk", "Orman Geyiği" }, { "Grey Horse", "Gri At" }, { "Happy Pig", "Mutlu Domuz" }
	};

	private static readonly RowRule[] Rows =
	{
		// Class descriptions are deliberately concise so the character-creation
		// panel remains readable at the game's fixed width.
		new RowRule("25002D96:10:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Temel</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> Hasar</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> Şampiyonlar yakın dövüşte silahlarını savurarak aynı anda birçok düşmana yüksek hasar verir. Her vuruş Coşku kazandırır; bu kaynak daha güçlü yetenekleri açar. Özel duruşlar saldırıyı veya savunmayı güçlendirir.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> Şampiyonlar savaşın amansız ustalarıdır. Bu sınıf, silah becerisi Legolas’ın yay ustalığına denk olan Glóin oğlu Gimli’den esinlenmiştir.</li>"),
		new RowRule("25002DC1:8:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Orta</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> Destek</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> Kumandanlar ağır zırhlı yakın dövüşçüler ve güçlendirme ustalarıdır. Bir Sancaktar çağırabilir, düşmanları İşaretleyebilir ve kardeşliklerini iyileştirme ile güçlendirmelerle destekleyebilirler.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> Orta Dünya kumandanları, Özgür Halkları zafere götürür. Bu sınıf Gondor’un son kralı, silah ustası Eärnur’dan esinlenmiştir.</li>"),
		new RowRule("25002DD6:10:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Orta</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> İyileştirme</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> Ozanlar iyileştirme yetenekleriyle tanınır; Savaş Ezgisi ile güçlü hasar da verirler. Baladlar daha güçlü ezgilerin, sonunda da kudretli Marşların kilidini açar. Kardeşlikte müttefiklerini iyileştirip desteklerler.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> Ozanlar şarkı ve öykülerle yoldaşlarının moralini yükseltir, hatta karanlığa karşı gerçek güç sözleri söyleyebilir. Bu sınıf, sesi dostu ve düşmanı büyüleyen Lúthien Tinúviel’den esinlenmiştir.</li>"),
		new RowRule("25002DED:8:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Gelişmiş</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> Hasar, Destek</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> İrfan Ustaları doğal dünyanın ve gizli bilgilerin ustalarıdır. Element bilgilerini çevik zekâyla birleştirir, hayvan yoldaşları çağırır ve doğanın gücünü düşmanlarına karşı kullanırlar. Kardeşlikte iyileştirme ve engelleme ile de destek olabilirler.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> İrfan Ustaları bilgiyi arar, eski çağların sırlarını öğrenir ve Düşman’ın güçlerine karşı koyarlar. Bu sınıf, Ayrıkvadi’nin elf efendisi Elrond’dan esinlenmiştir.</li>"),
		new RowRule("25002DFE:8:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Temel</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> Savunma</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> Muhafızlar ağır zırh giyer ve en korkunç düşmanlara karşı dayanabilmelerini sağlayan savunma yetenekleri kullanır. Düşmanların dikkatini üzerlerine çekerek daha kırılgan müttefiklerini korurlar.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> Muhafızlar zayıfları koruyan, ihtiyaç sahiplerini savunan sadık yoldaşlardır. Bu sınıf, Frodo’ya bağlılığı sınırsız olan hobbit Samwise Gamgee’den esinlenmiştir.</li>"),
		new RowRule("25002E13:8:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Gelişmiş</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> Destek</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> Hırsızlar zayıflatma ustasıdır; düşmanların daha az hasar vermesini veya daha kolay yenilmesini sağlarlar. Hileleriyle düşmanları zayıflatır, sonra bu hileleri kaldırarak ek fayda kazanırlar. Gizlenebilir ve Kardeşlik Manevraları başlatabilirler.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> Hırsızlar gizlilik ve şaşırtmacanın ustalarıdır. Bu sınıf, Thorin ve Bölük’le Yalnız Dağ’a giden ünlü Bilbo Baggins’ten esinlenmiştir.</li>"),
		new RowRule("25002E21:8:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Temel</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> Hasar, Destek</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> Avcılar uzaktaki tek bir düşmana yüksek hasar vermekte ustadır. Yay yetenekleri Odak üretir; düşman yaklaşırsa çift silahları son darbeyi vurur. Kardeşlikte düşmanları birer birer hızla indirirler.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> Avcılar kır ve ormanların, özellikle yayın ustalarıdır. Hayatta kalma becerileriyle yoldaşlarına yol gösterir ve düşmanlara tuzak kurarlar. Bu sınıf, Mirkwood Prensi Legolas’tan esinlenmiştir.</li>"),
		new RowRule("2500B1A7:10:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Orta</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> Hasar / İyileştirme</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> Rün Alimleri her savaşta hasar veya iyileştirme yeteneklerine odaklanmayı seçer. Bu seçim Uyumlarını değiştirir ve daha güçlü yeteneklerin kilidini açarken diğerlerini kısıtlar. Kardeşlikte müttefikleri iyileştirir ya da düşmana büyük hasar verirler.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> Rün Alimleri gerçek adların ve güçlü rün sözlerinin ustasıdır. Angerthas ve Tengwar bilgileriyle Özgür Halklara yardım ederler. Bu sınıf, usta elf demircisi Celebrimbor’dan esinlenmiştir.</li>"),
		new RowRule("2500B69F:10:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Gelişmiş</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> Savunma</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> Gözcüler hareketli yakın dövüşçülerdir; savaşı başlatmak ve uzaktaki düşmanlara karşı cirit kullanırlar. Özel kalkanları ve savunma yetenekleriyle mücadelede uzun süre kalırlar. Temel becerileri Gambit zinciri oluşturur.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> Gözcüler medeni toprakların sınırlarını korur, yabanın yaratıklarını uzak tutar. Orta zırhla hızlı ve sessiz hareket eder, temel saldırı birleşimleriyle ustaca Gambitler kurarlar. Bu sınıf Lothlórien sınır bekçisi Haldir ve kardeşlerinden esinlenmiştir.</li>"),
		new RowRule("2502EBC1:10:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Gelişmiş</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> Hasar / Destek</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> Deri Değiştirenler savaşta ayı biçimine dönüşebilir. Ayı biçiminde öfkelerini biriktirip ağır darbeler vurmayı veya kararlılıklarını güçlendirmeyi seçerler; vahşi doğa bilgileriyle müttefiklerine de yardım ederler.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> Deri Değiştirenler Orta Dünya’nın içine kapanık halkıdır ve soylarını Beorn’a dayandırırlar. Beş Ordunun Savaşı’nda Özgür Halkların yanında duran Beorn’dan esinlenmiştir.</li>"),
		new RowRule("25043FB8:10:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Gelişmiş</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> Saldırı / Savunma</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> Dövüşçüler, Metanet adlı kaynakla güçlenen saldırgan yakın dövüşçülerdir. Tek hedefe karşı çok etkilidir; birden fazla düşmana karşı da kendilerini savunabilirler. Kardeşlikte sertlikleri düşmanların dikkatini üzerlerinde tutar.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> Dövüşçüler düşmanlarını devirip müttefiklerini koruyan güçlü savaşçılardır. Bu sınıf, savaş alanına çıplak elleriyle çıkan Helm Hammerhand’den esinlenmiştir.</li>"),
		new RowRule("2504CEEC:10:-1:0", "\\n<li><rgb=#FFFF00>Class Difficulty:</rgb>",
			"\\n<li><rgb=#FFFF00>Sınıf Zorluğu:</rgb> Gelişmiş</li>\\n\\n<li><rgb=#FFFF00>Rolü:</rgb> Saldırı / Destek</li>\\n\\n<li><rgb=#FFFF00>Oyun:</rgb> Denizciler kılıçlarını ustaca kullanan kendinden emin yakın dövüşçülerdir. Savaşın akışını sezerek tarzlarını değiştirir, hareket birleşimleriyle güçlü yetenek zincirleri kurarlar. Kardeşlikte denizci şarkılarıyla müttefiklerini koordine eder ve düşmanları şaşırtan karışımlar hazırlarlar.</li>\\n\\n<li><rgb=#FFFF00>Bilgi:</rgb> Denizciler çevrelerini ve tuhaf araçları kullanarak savaşta avantaj sağlayan becerikli dövüşçülerdir. Bu sınıf, Valar’dan yardım istemek için Uzak Batı’ya tek başına yelken açan Eärendil’den esinlenmiştir.</li>")
	};

	public static string ExactForEnglish(string source)
	{
		if (source == null) return null;
		if (Exact.TryGetValue(source, out string target)) return target;
		return ManualCollectionText.ExactForEnglish(source);
	}

	public static int Apply(IList<LocRow> rows)
	{
		if (rows == null || rows.Count == 0) return 0;
		Dictionary<string, RowRule> byKey = new Dictionary<string, RowRule>(StringComparer.Ordinal);
		foreach (RowRule rule in Rows) byKey[rule.Key] = rule;
		int changed = 0;
		foreach (LocRow row in rows)
		{
			if (row == null) continue;
			string source = row.Original ?? "";
			string target = null;
			if (byKey.TryGetValue(row.Key, out RowRule rule)
				&& source.StartsWith(rule.SourcePrefix, StringComparison.Ordinal))
			{
				target = PreserveClassLineBreaks(source, rule.Target);
			}
			else
			{
				target = ExactForEnglish(source);
			}
			if (string.IsNullOrEmpty(target) || string.Equals(target, row.Translation ?? "", StringComparison.Ordinal)) continue;
			row.Translation = target;
			changed++;
		}
		return changed;
	}

	private static string PreserveClassLineBreaks(string source, string target)
	{
		if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)
			|| source.IndexOf("<li>", StringComparison.Ordinal) < 0)
		{
			return target;
		}
		const string marker = "\\n";
		int sourceClose = source.LastIndexOf("</li>", StringComparison.Ordinal);
		int targetClose = target.LastIndexOf("</li>", StringComparison.Ordinal);
		if (sourceClose < 0 || targetClose < 0) return target;
		int sourceBreaks = Count(source.Substring(0, sourceClose), marker);
		int targetBreaks = Count(target.Substring(0, targetClose), marker);
		int missing = sourceBreaks - targetBreaks;
		if (missing <= 0) return target;
		string extra = "";
		for (int i = 0; i < missing; i++) extra += marker;
		return target.Insert(targetClose, extra);
	}

	private static int Count(string value, string token)
	{
		int count = 0;
		int offset = 0;
		while (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(token))
		{
			int found = value.IndexOf(token, offset, StringComparison.Ordinal);
			if (found < 0) break;
			count++;
			offset = found + token.Length;
		}
		return count;
	}
}
