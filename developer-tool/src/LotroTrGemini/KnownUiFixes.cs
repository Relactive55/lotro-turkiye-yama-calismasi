using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LotroTrGemini;

/// <summary>
/// Handles verified official UI variants that the conservative flat fallback
/// catalog intentionally does not expose as ordinary translation rows.
/// </summary>
public static class KnownUiFixes
{
	private const int CharacterSelectionDid = unchecked((int)0x250001BDu);
	private const int FellowshipMenuDid = unchecked((int)0x250001AFu);
	private const int FellowshipUiDid = unchecked((int)0x250001BBu);
	private const int GondolinTitleDid = unchecked((int)0x2503B6C1u);
	private static readonly int[] CharacterCreationDids =
	{
		CharacterSelectionDid,
		unchecked((int)0x25004744u), // Hobbit
		unchecked((int)0x25004765u), // Man
		unchecked((int)0x25004780u), // Man racial trait: morale
		unchecked((int)0x25004781u), // Man racial trait: strength
		unchecked((int)0x25004782u), // Elf
		unchecked((int)0x2500479Du), // Dwarf
		unchecked((int)0x25002D96u), // Champion
		unchecked((int)0x25002DC1u), // Captain
		unchecked((int)0x25002DD6u), // Minstrel
		unchecked((int)0x25002DEDu), // Lore-master
		unchecked((int)0x25002DFEu), // Guardian
		unchecked((int)0x25002E13u), // Burglar
		unchecked((int)0x25002E21u), // Hunter
		unchecked((int)0x2500B1A7u), // Rune-keeper
		unchecked((int)0x2500B69Fu), // Warden
		unchecked((int)0x2502EBC1u), // Beorning
		unchecked((int)0x25043FB8u), // Brawler
		unchecked((int)0x2504CEECu)  // Mariner
	};
	private static readonly string[] CharacterSelectionSource = { "", " of ", " Character Slots Used" };
	private static readonly string[] CharacterSelectionTarget = { "", " / ", " KARAKTER YUVASI KULLANILIYOR" };

	private sealed class FlatUiFix
	{
		public string Key;
		public string Source;
		public string Target;

		public FlatUiFix(string key, string source, string target)
		{
			Key = key;
			Source = source;
			Target = target;
		}
	}

	// These are exact row identities, not substring replacements.  The helper
	// refuses to write if the official source row is absent or has drifted.
	private static readonly FlatUiFix[] FlatFixes =
	{
		// Character-creation race records are protected fallback tables.  Some
		// rows were already present in the old semantic baseline with a bad
		// Turkish target, so each such current value is listed as a source variant
		// and is replaced by the reviewed text below.  Untouched English rows are
		// covered by ManualUiText.ExactForEnglish in ApplyFlatUiFixes.
		new FlatUiFix("25004744:0:-1:0", "\\nGünde altı kare yemekle basit bir hayatın tadını çıkarırken en mutlu olan, hobbitler harekete çağrıldıklarında sağlam ve güvenilirdir.\\n\\n", "\\nGünde altı doyurucu öğünle sade bir hayat sürerken en mutlu olan Hobbitler, harekete çağrıldıklarında sağlam ve güvenilirdir.\\n\\n"),
		new FlatUiFix("25004744:2:-1:0", "Gün içinde altı öğün basit bir yaşamın tadını çıkarmaktan en çok keyif alan Hobbitler, harekete geçirilmek gerektiğinde sağlam ve güvenilirdir.", "Günde altı doyurucu öğünle sade bir hayat sürerken en mutlu olan Hobbitler, harekete çağrıldıklarında sağlam ve güvenilirdir."),
		new FlatUiFix("25004744:4:-1:0", "O deli erkek hobbitler. Bla bla bla.", "Erkek hobbitler."),
		new FlatUiFix("25004744:7:-1:0", "O deli dişi Hobbitler. Bla bla bla.", "Kadın hobbitler."),

		new FlatUiFix("25004765:0:-1:0", "\\nElfler kadar uzun ömürlü, cüceler kadar sağlam veya hobbitler kadar dayanıklı olmayan erkekler, cesaretleri ve beceriklilikleri ile ünlüdür.\\n\\n", "\\nElfler kadar uzun ömürlü, Cüceler kadar sağlam veya Hobbitler kadar dayanıklı olmasalar da İnsanlar cesaretleri ve beceriklilikleriyle tanınır.\\n\\n"),
		new FlatUiFix("25004765:1:-1:0", "İnsanın Irkı (Kadın)", "İnsan Irkı (Kadın)"),
		new FlatUiFix("25004765:2:-1:0", "Elfler kadar uzun ömürlü olmasalar da, cüceler gibi sağlam veya hobbitler kadar dirençli olmayan İnsanlar, cesaretleri ve zekalarıyla tanınırlar. Güçlü bir kavimdir onlar, kaderleri Orta Dünya'nın baskın halkı olmak olsa da, iradeleri daha zayıf ve Düşman'ın baştan çıkarıcılarına ve hilelerine karşı daha yatkındır.", "Elfler kadar uzun ömürlü, Cüceler kadar sağlam veya Hobbitler kadar dayanıklı olmasalar da İnsanlar cesaretleri ve beceriklilikleriyle tanınır. Güçlü bir halktır; Orta Dünya'nın baskın halkı olmaya yazgılıdırlar, ancak iradeleri daha zayıftır ve Düşman'ın ayartılarına ve hilelerine daha açıktırlar."),
		new FlatUiFix("25004765:4:-1:0", "Erkek insanlar. Bla bla bla.", "Erkek insanlar."),
		new FlatUiFix("25004765:7:-1:0", "Kadın insanlar mı? Hıh? Bla bla bla.", "Kadın insanlar."),
		new FlatUiFix("25004765:9:-1:0", "İnsanın Irkı (Erkek)", "İnsan Irkı (Erkek)"),

		new FlatUiFix("25004780:0:-1:0", "Artan Moral Restorasyonu - İnsanlar diğer ırklara göre daha hızlı harekete geçebilir. (İyileştiriciler, insanlar üzerinde daha fazla iyileşme sağlar)", "Artan Moral Yenilenmesi - İnsanlar diğer ırklara göre daha çabuk toparlanır. (İyileştirme etkileri İnsanlar üzerinde daha güçlüdür.)"),
		new FlatUiFix("25004780:2:-1:0", "Artan Moral Restorasyonu - İnsanlar diğer ırklardan daha hızlı cesaretlenirler.", "Artan Moral Yenilenmesi - İnsanlar diğer ırklara göre daha çabuk cesaretlenir."),
		new FlatUiFix("25004781:0:-1:0", "Geliştirilmiş Güç - Boromir gibi insanların başarabileceği güç gösterileri şarkılara değerdir.", "Geliştirilmiş Kuvvet - Boromir gibi insanların sergileyebildiği güç gösterileri şarkılara değerdir."),
		new FlatUiFix("25004781:2:-1:0", "Geliştirilmiş Güç - Boromir gibi insanların başarabileceği güç gösterileri şarkılara değerdir.", "Geliştirilmiş Kuvvet - Boromir gibi insanların sergileyebildiği güç gösterileri şarkılara değerdir."),
		new FlatUiFix("25004780:3:-1:0", "+5% Incoming Healing", "+5% Gelen İyileştirme"),

		new FlatUiFix("25004782:0:-1:0", "\\nUzun zaman önce, Elfler genç ırkları karşıladılar. Middle-earth İhtiyacı büyük olduğunda onlarla müttefik oldular, ama yüzyıllarca süren savaş, ihanet ve zorluklar onları inzivalarına karşı şiddetle korudu.\\n\\n", "\\nUzun zaman önce Elfler, Orta Dünya'nın genç ırklarını kabul edip ihtiyaç büyük olduğunda onlarla ittifak kurdu; ancak yüzyıllar süren savaş, ihanet ve zorluklar onları inzivalarını korumakta kararlı hâle getirdi.\\n\\n"),
		new FlatUiFix("25004782:2:-1:0", "Bu seçenek henüz tamamlanmadı.", "Bu seçenek henüz uygulanmadı."),
		new FlatUiFix("25004782:7:-1:0", "Kadın Elfler. Bla bla bla.", "Kadın elfler."),

		new FlatUiFix("2500479D:0:-1:0", "\\nTaştan ve madenden yapılmış kuyumcular, cüceler, düşmanın yolsuzluğuna karşı dirençli, ama açgözlülüğe karşı değil, donuk bir halktır.\\n\\n", "\\nTaşın sakinleri ve metal madencileri olan Cüceler cesur bir halktır; Düşman'ın yozlaşmasına dirençlidirler, ancak açgözlülüğe karşı değil.\\n\\n"),
		new FlatUiFix("2500479D:1:-1:0", "KULLANILMAMALIDIR", "KULLANILMAMALI"),
		new FlatUiFix("2500479D:2:-1:0", "bir Cüce[n]", "bir cüce[n]"),
		new FlatUiFix("2500479D:3:-1:0", "Aradaki farkı söylemekte iyi şanslar.", "Aradaki farkı anlamakta bol şans."),
		new FlatUiFix("2500479D:5:-1:0", "KULLANILMAMALIDIR", "KULLANILMAMALI"),

		new FlatUiFix("250001AF:2:-1:0", "Durability ", "Dayanıklılık "),
		new FlatUiFix("250001AF:14:-1:0", "Rank: ", "Rütbe: "),
		new FlatUiFix("250001AF:56:-1:0", "Minimum Level: ", "Gerekli Seviye: "),
		new FlatUiFix("250001AF:330:-1:0", "Minimum Level ", "Gerekli Seviye "),
		new FlatUiFix("250001AF:51:-1:0", "Not earned", "Kazanılmadı"),
		new FlatUiFix("250001AF:98:-1:0", "Rank: ", "Rütbe: "),
		new FlatUiFix("250001AF:146:-1:0", "Pets", "Evcil Hayvanlar"),
		new FlatUiFix("250001AF:208:-1:0", "\\nRank ", "\\nRütbe "),
		new FlatUiFix("250001AF:233:-1:0", "Rank: ", "Rütbe: "),
		new FlatUiFix("250001AF:287:-1:1", " #1:{Point[1]|Points} to Next Rank", " #1:{Puan[1]|Puan} Sonraki Rütbeye"),
		new FlatUiFix("250001AF:293:-1:0", "Tactical", "Taktiksel"),
		new FlatUiFix("250001AF:295:-1:0", "Equipped", "Kuşanıldı"),
		new FlatUiFix("250001AF:381:-1:0", "Bind On Acquire", "Alındığında Bağlanır"),
		new FlatUiFix("250001AF:622:-1:0", "Equipped", "Kuşanıldı"),
		new FlatUiFix("250001AF:326:-1:0", "No", "Hayır"),
		new FlatUiFix("250001AF:343:-1:0", "Rank: ", "Rütbe: "),
		new FlatUiFix("250001AF:371:-1:0", "Rank: ", "Rütbe: "),
		new FlatUiFix("250001AF:384:-1:0", "Next Rank:", "Sonraki Rütbe:"),
		new FlatUiFix("250001AF:539:-1:0", "Next Rank", "Sonraki Rütbe"),
		new FlatUiFix("250001AF:128:-1:0", "<rgb=#666666>Bound to ", "<rgb=#666666>Bağlı olduğu kişi: "),
		new FlatUiFix("250001AF:527:-1:0", "<rgb=#666666>Bound to Account</rgb>", "<rgb=#666666>Hesaba Bağlı</rgb>"),
		new FlatUiFix("250001BB:0:-1:0", "Pocket slot", "Cep Yuvası"),
		new FlatUiFix("250001BB:29:-1:0", "Edit Mode", "Düzenleme Modu"),
		new FlatUiFix("250001BB:52:-1:0", "Press to show the Tutorial Hint associated with this panel", "Bu panelle ilişkili öğretici ipucunu göstermek için basın"),
		new FlatUiFix("250001BB:167:-1:0", "Click to sort all your items", "Tüm eşyalarınızı sıralamak için tıklayın"),
		new FlatUiFix("250001BB:181:-1:0", "You need ", "Gereken deneyim: "),
		new FlatUiFix("250001BB:181:-1:1", " XP for level ", " DP; hedef seviye: "),
		new FlatUiFix("250001BB:298:-1:0", "examine", "İncele"),
		new FlatUiFix("250001BB:517:-1:0", "Search", "Ara"),
		new FlatUiFix("250001BB:520:-1:0", "Toggle Lock Mode", "Kilit Modunu Aç/Kapat"),
		new FlatUiFix("250001BB:526:-1:0", "Block", "Engelle"),
		new FlatUiFix("250001BB:535:-1:0", "No", "Hayır"),
		new FlatUiFix("250001BB:544:-1:0", "Left bracelet slot", "Sol Bileklik Yuvası"),
		new FlatUiFix("250001BB:545:-1:0", "Right bracelet slot", "Sağ Bileklik Yuvası"),
		new FlatUiFix("250001BB:633:-1:0", "Left earring slot", "Sol Küpe Yuvası"),
		new FlatUiFix("250001BB:634:-1:0", "Right earring slot", "Sağ Küpe Yuvası"),
		new FlatUiFix("250001BB:690:-1:0", "Left ring slot", "Sol Yüzük Yuvası"),
		new FlatUiFix("250001BB:692:-1:0", "Right ring slot", "Sağ Yüzük Yuvası"),
		new FlatUiFix("250001BB:863:-1:0", "Necklace slot", "Kolye Yuvası"),
		new FlatUiFix("250001BB:869:-1:0", "Do not show again", "Bir daha gösterme"),
		new FlatUiFix("250001BB:448:-1:0", "You need ", "Gereken deneyim: "),
		new FlatUiFix("250001BB:448:-1:1", " XP for level ", " DP; hedef seviye: "),
		new FlatUiFix("250001BB:448:-1:2", "\\nRemaining VIP Bonus: ", "\\nKalan VIP Bonusu: "),
		new FlatUiFix("250001BB:448:-1:3", " XP\\nRemaining Purchased Bonus: ", " DP\\nKalan Satın Alınan Bonusu: "),
		new FlatUiFix("250001BB:448:-1:4", " XP", " DP"),
		new FlatUiFix("250001BB:501:-1:0", "You need ", "Gereken deneyim: "),
		new FlatUiFix("250001BB:501:-1:1", " XP for level ", " DP; hedef seviye: "),
		new FlatUiFix("250001BB:501:-1:2", "\\nRemaining VIP Bonus: ", "\\nKalan VIP Bonusu: "),
		new FlatUiFix("250001BB:501:-1:3", " XP\\n Remaining Purchased Bonus: ", " DP\\n Kalan Satın Alınan Bonusu: "),
		new FlatUiFix("250001BB:501:-1:4", " XP\\nYour experience gains are currently suppressed.", " DP\\nDeneyim kazanımlarınız şu anda devre dışı."),
		new FlatUiFix("250001BB:891:-1:0", "You need ", "Gereken deneyim: "),
		new FlatUiFix("250001BB:891:-1:1", " XP for level ", " DP; hedef seviye: "),
		new FlatUiFix("250001BB:891:-1:2", ".\\nYour experience gains are currently suppressed.", ".\\nDeneyim kazanımlarınız şu anda devre dışı."),
		new FlatUiFix("250001BB:156:-1:0", "Accept", "Kabul Et"),
		new FlatUiFix("250001BB:817:-1:0", "Accept", "Kabul Et"),
		new FlatUiFix("250001BB:848:-1:0", "Accept", "Kabul Et"),
		new FlatUiFix("250001BB:595:-1:0", "Loading...", "Yükleniyor..."),
		new FlatUiFix("250001BB:725:-1:0", "Kills Above Rating: ", "Üst Dereceli Rakip Öldürme: "),
		new FlatUiFix("250001BB:736:-1:0", "Kills Below Rating: ", "Alt Dereceli Rakip Öldürme: "),
		new FlatUiFix("250001AF:272:-1:0", "Players", "Oyuncular"),

		new FlatUiFix("250001BB:48:-1:0", "Rank ", "Rütbe "),
		new FlatUiFix("250001BB:56:-1:0", "Friends", "Arkadaşlar"),
		new FlatUiFix("250001BB:61:-1:0", "System", "Sistem"),
		new FlatUiFix("250001BB:63:-1:0", "Deed Log", "Başarım Günlüğü"),
		new FlatUiFix("250001BB:91:-1:0", "Killing Blows: ", "Öldürücü Vuruşlar: "),
		new FlatUiFix("250001BB:121:-1:0", "Social", "Sosyal"),
		new FlatUiFix("250001BB:141:-1:0", "Traits", "Özellikler"),
		new FlatUiFix("250001BB:210:-1:0", "Kills/Deaths: ", "Öldürme/Ölüm: "),
		new FlatUiFix("250001BB:219:-1:0",
			"<li>Raid Locks are used for larger instances which may take multiple sessions to complete.</li><li>The Lock of the instance is dictated by the leader.</li> <li>For the raid group to enter the instance, the leader's locks must contain all of your locks. (having no locks counts)</li><li>If you have any locks that the leader does not, then your raid group cannot enter the instance.</li><li>To meet the above requirements you may need to switch leadership status temporarily.</li>",
			"<li>Baskın kilitleri, tamamlanması birden fazla oturum sürebilen büyük örnek alanlar için kullanılır.</li><li>Örnek alanın kilidini lider belirler.</li> <li>Baskın grubunun örnek alana girebilmesi için liderin kilitlerinde sizin tüm kilitleriniz bulunmalıdır. (Hiç kilidinizin olmaması da sayılır.)</li><li>Liderde olmayan kilitleriniz varsa baskın grubunuz örnek alana giremez.</li><li>Yukarıdaki koşulları karşılamak için liderliği geçici olarak değiştirmeniz gerekebilir.</li>"),
		new FlatUiFix("250001BB:229:-1:0", "Traits", "Özellikler"),
		new FlatUiFix("250001BB:247:-1:0", "Rank", "Rütbe"),
		new FlatUiFix("250001BB:259:-1:0", "Highest Rating Defeated: ", "Yenilen En Yüksek Derece: "),
		new FlatUiFix("250001BB:291:-1:0", "Progress to Next Rank", "Sonraki Rütbeye İlerleme"),
		new FlatUiFix("250001BB:326:-1:0", "inventory", "envanter"),
		new FlatUiFix("250001BB:390:-1:0", "Total Renown: ", "Toplam Ün: "),
		new FlatUiFix("250001BB:422:-1:0", "Rating: ", "Derece: "),
		new FlatUiFix("250001BB:431:-1:0", "Map", "Harita"),
		new FlatUiFix("250001BB:459:-1:0", "Raid Locks", "Baskın Kilitleri"),
		new FlatUiFix("250001BB:464:-1:0", "Fellowing", "Yoldaşlık"),
		new FlatUiFix("250001BB:466:-1:0", "Kills: ", "Öldürmeler: "),
		new FlatUiFix("250001BB:487:-1:0", "Rating: ", "Derece: "),
		new FlatUiFix("250001BB:503:-1:0", "Vitality", "Canlılık"),
		new FlatUiFix("250001BB:564:-1:0", "Traits", "Özellikler"),
		new FlatUiFix("250001BB:578:-1:0", "Assist Window", "Yardım Penceresi"),
		new FlatUiFix("250001BB:599:-1:0", "Inventory", "Envanter"),
		new FlatUiFix("250001BB:604:-1:0", "Raid", "Baskın"),
		new FlatUiFix("250001BB:613:-1:0", "Kinship", "Kardeşlik"),
		new FlatUiFix("250001BB:647:-1:0",
			"To equip a skill, drag it from the Skills Panel to an available quickslot at the bottom of the game screen.",
			"Bir yeteneği kuşanmak için, onu Yetenekler Paneli'nden oyun ekranının altındaki uygun bir hızlı yuvaya sürükleyin."),
		new FlatUiFix("250001BB:674:-1:0", "Completed Fellowship Manoeuvres", "Tamamlanan Kardeşlik Manevraları"),
		new FlatUiFix("250001BB:686:-1:0", "Inventory", "Envanter"),
		new FlatUiFix("250001BB:722:-1:0", "map", "harita"),
		new FlatUiFix("250001BB:742:-1:0", "Rank: ", "Rütbe: "),
		new FlatUiFix("250001BB:754:-1:0", "Quest Log", "Görev Günlüğü"),
		new FlatUiFix("250001BB:759:-1:0", "Crafting", "Zanaatkârlık"),
		new FlatUiFix("250001BB:795:-1:0", "Invite", "Davet Et"),
		new FlatUiFix("250001BB:803:-1:0", "Show Skills:", "Yetenekleri Göster:"),
		new FlatUiFix("250001BB:804:-1:0", "Skill Queue", "Yetenek Kuyruğu"),
		new FlatUiFix("250001BB:801:-1:0", "Mount skills will be shown here after they are acquired.", "Binek becerileri edinildikten sonra burada gösterilecektir."),
		new FlatUiFix("250001BB:846:-1:0", "Deaths: ", "Ölümler: "),
		new FlatUiFix("250001BB:880:-1:0", "Rank: ", "Rütbe: "),
		new FlatUiFix("250001BB:895:-1:0", "Assist Window", "Yardım Penceresi"),
		new FlatUiFix("250001BB:900:-1:0",
			"To equip a hobby, drag it from the Hobbies Panel to an available quickslot at the bottom of the game screen.",
			"Bir hobiyi kuşanmak için, onu Hobi Paneli'nden oyun ekranının altındaki uygun bir hızlı yuvaya sürükleyin."),
		new FlatUiFix("250001BB:917:-1:0", "Traits", "Özellikler"),
		// Shared vendor, barter and chat labels that live in the protected
		// fallback table.  Keep these exact-key fixes fail-closed so a changed
		// official row cannot be silently rewritten.
		new FlatUiFix("250001BB:59:-1:0", "Combat", "Savaş"),
		new FlatUiFix("250001BB:32:-1:0", "Bag 1", "Çanta 1"),
		new FlatUiFix("250001BB:34:-1:0", "Bag 2", "Çanta 2"),
		new FlatUiFix("250001BB:35:-1:0", "Bag 3", "Çanta 3"),
		new FlatUiFix("250001BB:36:-1:0", "Bag 4", "Çanta 4"),
		new FlatUiFix("250001BB:38:-1:0", "Bag 5", "Çanta 5"),
		new FlatUiFix("250001BB:40:-1:0", "Bag 6", "Çanta 6"),
		new FlatUiFix("250001BB:62:-1:0", "Open/close backpack", "Çantayı aç/kapat"),
		new FlatUiFix("250001BB:179:-1:0", "Active Quests:", "Aktif Görevler:"),
		new FlatUiFix("250001BB:314:-1:0", "Quest Tracker", "Görev İzleyici"),
		new FlatUiFix("250001BB:380:-1:0", "Quest History", "Görev Geçmişi"),
		new FlatUiFix("250001BB:391:-1:0", "Delete currently selected character.", "Seçili karakteri sil."),
		new FlatUiFix("250001BB:919:-1:0", "Open the plugin manager to manage the loading of your player-made Lua plugins. ", "Eklenti yöneticisini açarak oyuncular tarafından oluşturulan Lua eklentilerinizin yüklenmesini yönetin. "),
		// The server-shutdown countdown is another protected fallback table.  The
		// countdown values are named variants, so translate their display words
		// while retaining the parameter markers used by the client.
		new FlatUiFix("250001FE:6:-1:0", "This world will be shutting down in #2:", "Bu dünya kapanacak #2:"),
		new FlatUiFix("250001FE:6:-1:1", " #2:{second[1]|seconds}! Please log out!\\n", " #2:{saniye[1]|saniye}! Oturumu kapatın!\\n"),
		new FlatUiFix("250001FE:13:-1:0", "This world is shutting down NOW! Log out!\\n", "Bu dünya ŞİMDİ kapanıyor! Oturumu kapatın!\\n"),
		new FlatUiFix("250001FE:40:-1:0", "This world will be shutting down in #1:", "Bu dünya kapanacak #1:"),
		new FlatUiFix("250001FE:40:-1:1", " #1:{minute[1]|minutes}. Please log out.\\n", " #1:{dakika[1]|dakika} içinde. Lütfen oturumu kapatın.\\n"),
		new FlatUiFix("250001FE:48:-1:0", "This world will be shutting down in #1:", "Bu dünya kapanacak #1:"),
		new FlatUiFix("250001FE:48:-1:1", " #1:{hour[1]|hours}.\\n", " #1:{saat[1]|saat}.\\n"),
		new FlatUiFix("250001FE:49:-1:0", "This world will be shutting down in #1:", "Bu dünya kapanacak #1:"),
		new FlatUiFix("250001FE:49:-1:1", " #1:{minute[1]|minutes} and #2:", " #1:{dakika[1]|dakika} ve #2:"),
		new FlatUiFix("250001FE:49:-1:2", " #2:{second[1]|seconds}. Please log out.\\n", " #2:{saniye[1]|saniye} sonra kapanacak. Lütfen oturumu kapatın.\\n"),
		new FlatUiFix("250001BB:481:-1:0", "Melee", "Yakın Dövüş"),
		new FlatUiFix("250001BB:513:-1:0", "Crafting: ", "Zanaatkârlık: "),
		new FlatUiFix("250001BB:624:-1:0", "Skills", "Yetenekler"),
		new FlatUiFix("250001BB:650:-1:0", "Cost: ", "Maliyet: "),
		new FlatUiFix("250001BB:675:-1:0", "Bag", "Çanta"),
		new FlatUiFix("250001BB:133:-1:0", "Barter", "Takas"),
		new FlatUiFix("250001BB:154:-1:0", "Comments", "Yorumlar"),
		new FlatUiFix("250001BB:162:-1:0", "All", "Tümü"),
		new FlatUiFix("250001BB:324:-1:0", "Items To Trade", "Takas Edilecek Eşyalar"),
		new FlatUiFix("250001BB:345:-1:0", "Filter by Profile:", "Profile Göre Filtrele:"),
		new FlatUiFix("250001BB:511:-1:0", "Close Window", "Pencereyi Kapat"),
		new FlatUiFix("250001BB:602:-1:0", "All", "Tümü"),
		new FlatUiFix("250001BB:609:-1:0", "General", "Genel"),
		new FlatUiFix("250001BB:629:-1:0", "All", "Tümü"),
		new FlatUiFix("250001BB:714:-1:0", "Shop", "Mağaza"),
		new FlatUiFix("250001BB:845:-1:0", "Barter", "Takas"),
		new FlatUiFix("250001BB:876:-1:0", "Item to Receive", "Alınacak Eşya"),

		// Shared skill-tooltip vocabulary. These are exact protected fallback
		// rows, so every tooltip gets the same terminology without a broad
		// substring replacement.
		new FlatUiFix("250001AF:3:-1:0", "Cooldown: ", "Bekleme Süresi: "),
		new FlatUiFix("250001AF:10:-1:0", "Duration: ", "Süre: "),
		new FlatUiFix("250001AF:13:-1:0", "Cost: ", "Maliyet: "),
		new FlatUiFix("250001AF:16:-1:0", "Resistances:", "Dirençler:"),
		new FlatUiFix("250001AF:18:-1:0", "Cooldown: ", "Bekleme Süresi: "),
		new FlatUiFix("250001AF:25:-1:0", "Adds #1:", "Ekler #1:"),
		new FlatUiFix("250001AF:25:-1:2", " #1:{points|point[1]} every #3:", " #1:{points|point[1]} her #3:"),
		new FlatUiFix("250001AF:62:-1:0", "Ranged", "Menzilli"),
		new FlatUiFix("250001AF:78:-1:0", "Cost: ", "Maliyet: "),
		new FlatUiFix("250001AF:88:-1:0", "Cooldown: ", "Bekleme Süresi: "),
		new FlatUiFix("250001AF:95:-1:0", "Your Cooldown: ", "Bekleme Süreniz: "),
		new FlatUiFix("250001AF:103:-1:0", "Skill Type: ", "Yetenek Türü: "),
		new FlatUiFix("250001AF:104:-1:0", "Cooldown Remaining: ", "Kalan Bekleme Süresi: "),
		new FlatUiFix("250001AF:137:-1:0", "Fast", "Hızlı"),
		new FlatUiFix("250001AF:152:-1:0", "Cost: ", "Maliyet: "),
		new FlatUiFix("250001AF:156:-1:0", "Reflect ", "Yansıtır "),
		new FlatUiFix("250001AF:156:-1:1", " of ", " oranında "),
		new FlatUiFix("250001AF:156:-1:2", "damage", "hasar"),
		new FlatUiFix("250001AF:157:-1:0", "Receive ", "Alır "),
		new FlatUiFix("250001AF:157:-1:1", " damage", " hasar"),
		new FlatUiFix("250001AF:181:-1:0", "Damage Stats:", "Hasar İstatistikleri:"),
		new FlatUiFix("250001AF:209:-1:0", "Cooldown: ", "Bekleme Süresi: "),
		new FlatUiFix("250001AF:210:-1:0", "Reflect effect:", "Yansıtma etkisi:"),
		new FlatUiFix("250001AF:222:-1:0", "Deals ", "Hasar verir "),
		new FlatUiFix("250001AF:229:-1:0", "Cost: ", "Maliyet: "),
		new FlatUiFix("250001AF:263:-1:0", "damage", "hasar"),
		new FlatUiFix("250001AF:286:-1:0", "Resistance: ", "Direnç: "),
		new FlatUiFix("250001AF:298:-1:0", "Damage Source Qualifiers:", "Hasar Kaynağı Niteleyicileri:"),
		new FlatUiFix("250001AF:300:-1:0", "Fast\\n", "Hızlı\\n"),
		new FlatUiFix("250001AF:306:-1:0", "Adds ", "Ekler "),
		new FlatUiFix("250001AF:306:-1:1", " to ", " için "),
		new FlatUiFix("250001AF:320:-1:0", "Max Targets: ", "Maks. Hedef: "),
		new FlatUiFix("250001AF:314:-1:0", "Receive ", "Alır "),
		new FlatUiFix("250001AF:314:-1:1", " damage", " hasar"),
		new FlatUiFix("250001AF:319:-1:0", "Cooldown: ", "Bekleme Süresi: "),
		new FlatUiFix("250001AF:344:-1:0", "Off-hand", "Yardımcı El"),
		new FlatUiFix("250001AF:364:-1:0", "Adds ", "Ekler "),
		new FlatUiFix("250001AF:364:-1:1", " to ", " için "),
		new FlatUiFix("250001AF:374:-1:0", "Induction: ", "Hazırlık: "),
		new FlatUiFix("250001AF:378:-1:0", "Your cooldown remaining: ", "Kalan bekleme süreniz: "),
		new FlatUiFix("250001AF:380:-1:0", "Negate ", "Engeller "),
		new FlatUiFix("250001AF:380:-1:1", " damage", " hasar"),
		new FlatUiFix("250001AF:398:-1:0", "Adds ", "Ekler "),
		new FlatUiFix("250001AF:425:-1:0", "Weapon DPS", "Silah DPS"),
		new FlatUiFix("250001AF:439:-1:0", "Damage Mitigation:", "Hasar Azaltma:"),
		new FlatUiFix("250001AF:440:-1:0", "Adds:", "Ekler:"),
		new FlatUiFix("250001AF:466:-1:0", "Negate ", "Engeller "),
		new FlatUiFix("250001AF:466:-1:1", " damage", " hasar"),
		new FlatUiFix("250001AF:470:-1:0", "Item Duration: ", "Eşya Süresi: "),
		new FlatUiFix("250001AF:490:-1:0", "Cooldown remaining: ", "Kalan bekleme süresi: "),
		new FlatUiFix("250001AF:492:-1:0", "Cost", "Maliyet"),
		new FlatUiFix("250001AF:520:-1:0", "Cooldown Remaining: ", "Kalan Bekleme Süresi: "),
		new FlatUiFix("250001AF:522:-1:0", "Main-hand", "Ana El"),
		new FlatUiFix("250001AF:544:-1:0", "Adds ", "Ekler "),
		new FlatUiFix("250001AF:544:-1:1", " to ", " için "),
		new FlatUiFix("250001AF:544:-1:2", " initially", " başlangıçta"),
		new FlatUiFix("250001AF:556:-1:0", "Channel Duration: ", "Kanal Süresi: "),
		new FlatUiFix("250001AF:564:-1:0", "Damage ", "Hasar "),
		new FlatUiFix("250001AF:577:-1:0", "Receive effect:", "Alma etkisi:"),
		new FlatUiFix("250001AF:579:-1:1", " of damage", " hasar"),
		new FlatUiFix("250001AF:606:-1:0", "Reflect ", "Yansıtır "),
		new FlatUiFix("250001AF:621:-1:1", " Damage to ", " Hasar verir: "),
		new FlatUiFix("250001AF:421:-1:0", "Resistance: ", "Direnç: "),
		new FlatUiFix("250001AF:434:-1:0", "Worth: ", "Değeri: "),
		new FlatUiFix("250001AF:67:-1:0", "Item Level: ", "Eşya Seviyesi: "),
		new FlatUiFix("250001AF:315:-1:0", "Item level: ", "Eşya Seviyesi: "),

		// Help/map labels in the same protected UI table.
		new FlatUiFix("250001BB:14:-1:0", "Clear All", "Tümünü Temizle"),
		new FlatUiFix("250001BB:90:-1:0", "Show Quest Guide On Map", "Haritada Görev Rehberini Göster"),
		new FlatUiFix("250001BB:206:-1:0", "Filter Map Notes", "Harita Notlarını Filtrele"),
		new FlatUiFix("250001BB:221:-1:0", "Cancel", "İptal"),
		new FlatUiFix("250001BB:323:-1:0", "Cancel", "İptal"),
		new FlatUiFix("250001BB:333:-1:0", "Cancel", "İptal"),
		new FlatUiFix("250001BB:335:-1:0", "Cancel", "İptal"),
		new FlatUiFix("250001BB:392:-1:0", "Select All", "Tümünü Seç"),
		new FlatUiFix("250001BB:565:-1:0", "Cancel", "İptal"),
		new FlatUiFix("250001BB:576:-1:0", "Cancel", "İptal"),
		new FlatUiFix("250001BB:616:-1:0", "Cancel", "İptal"),
		new FlatUiFix("250001BB:640:-1:0", "Open in an external browser?", "Harici Tarayıcıda Aç?"),
		new FlatUiFix("250001BB:648:-1:0", "Show Map", "Haritayı Göster"),
		new FlatUiFix("250001BB:830:-1:0", "Cancel", "İptal"),
		new FlatUiFix("250001BB:915:-1:0", "Yes", "Evet"),
		new FlatUiFix("250001BB:926:-1:0", "Cancel", "İptal")
	};

	/// <summary>
	/// Content identity for caches that already include these corrections.
	/// Update the domain version if correction semantics change; table or
	/// character-selection text changes invalidate caches automatically.
	/// </summary>
	public static string ContentRevisionSha256
	{
		get
		{
			using (MemoryStream stream = new MemoryStream())
			using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
			using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
			{
				writer.Write("lotro-known-ui-fixes-v6-character-creation-race-class-exact-vocabulary");
				foreach (var fix in HiddenTooltipFixes) { writer.Write(fix.Item1); writer.Write(fix.Item2); }
				writer.Write(CharacterSelectionDid);
				writer.Write(CharacterSelectionSource.Length);
				foreach (string value in CharacterSelectionSource) writer.Write(value);
				writer.Write(CharacterSelectionTarget.Length);
				foreach (string value in CharacterSelectionTarget) writer.Write(value);
				writer.Write(FellowshipMenuDid);
				writer.Write(FellowshipUiDid);
				writer.Write(GondolinTitleDid);
				foreach (var fix in GondolinTitleFixes) { writer.Write(fix.Item1); writer.Write(fix.Item2); }
				writer.Write(FlatFixes.Length);
				foreach (FlatUiFix fix in FlatFixes.OrderBy(item => item.Key, StringComparer.Ordinal))
				{
					writer.Write(fix.Key);
					writer.Write(fix.Source);
					writer.Write(fix.Target);
				}
				writer.Flush();
				return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
			}
		}
	}

	public static bool HasAutomaticFix(int did)
	{
		return CharacterCreationDids.Contains(did) || did == FellowshipMenuDid || did == FellowshipUiDid
			|| did == GondolinTitleDid || did == unchecked((int)0x250001FEu);
	}

	public static byte[] ApplyTranslatedPayload(int did, byte[] payload)
	{
		if (payload == null || payload.Length == 0) return payload;
		if (did == CharacterSelectionDid)
		{
			// The slot-count record and the remaining character-creation labels live
			// in the same protected fallback table.  Apply both passes so an old
			// semantic baseline cannot leave race/class text in English.
			return ApplyFlatUiFixes(did, ApplyCharacterSelectionFix(payload));
		}
		if (did == FellowshipMenuDid || did == FellowshipUiDid || did == unchecked((int)0x250001FEu))
		{
			byte[] result = ApplyFlatUiFixes(did, payload);
			return did == FellowshipMenuDid ? ApplyHiddenTooltipFixes(result) : result;
		}
		if (CharacterCreationDids.Contains(did)) return ApplyFlatUiFixes(did, payload);
		if (did == GondolinTitleDid) return ApplyGondolinTitleFixes(payload);
		return payload;
	}

	private static byte[] ApplyCharacterSelectionFix(byte[] payload)
	{
		byte[] source = BuildRecord(CharacterSelectionSource);
		byte[] target = BuildRecord(CharacterSelectionTarget);
		int sourceOffset = IndexOf(payload, source);
		if (sourceOffset < 0)
		{
			if (IndexOf(payload, target) >= 0) return payload;
			throw new InvalidDataException("Doğrulanmış karakter yuvası UI kaydı bulunamadı.");
		}
		if (IndexOf(payload, source, sourceOffset + 1) >= 0)
			throw new InvalidDataException("Karakter yuvası UI kaydı benzersiz değil.");

		byte[] result = new byte[payload.Length - source.Length + target.Length];
		Buffer.BlockCopy(payload, 0, result, 0, sourceOffset);
		Buffer.BlockCopy(target, 0, result, sourceOffset, target.Length);
		Buffer.BlockCopy(payload, sourceOffset + source.Length, result, sourceOffset + target.Length,
			payload.Length - sourceOffset - source.Length);
		return result;
	}

	private static byte[] ApplyFlatUiFixes(int did, byte[] payload)
	{
		LocBin bin = LocBin.Parse(payload, did);
		if (!bin.UsedFlatFallback)
			throw new InvalidDataException("Doğrulanmış düz UI tablosu beklenen fallback biçiminde değil: 0x" + did.ToString("X8"));

		List<LocRow> rows = bin.GetRows(did);
		Dictionary<string, LocRow> byKey = new Dictionary<string, LocRow>(StringComparer.Ordinal);
		foreach (LocRow row in rows)
		{
			if (byKey.ContainsKey(row.Key)) throw new InvalidDataException("UI satır anahtarı benzersiz değil: " + row.Key);
			byKey.Add(row.Key, row);
		}

		List<FlatUiFix> fixes = FlatFixes.Where(item => item.Key.StartsWith(did.ToString("X8") + ":", StringComparison.Ordinal)).ToList();
		// Structural fallback tables are intentionally kept out of the semantic
		// catalog, but their reviewed labels and row-key class descriptions are
		// still safe to translate.  Run the same exact/key-aware manual table used
		// by the editor, then turn only changed rows into fail-closed fixes.
		HashSet<string> knownFixKeys = new HashSet<string>(fixes.Select(item => item.Key), StringComparer.Ordinal);
		ManualUiText.Apply(rows);
		foreach (LocRow row in rows)
		{
			if (knownFixKeys.Contains(row.Key)) continue;
			if (string.IsNullOrEmpty(row.Translation) || string.Equals(row.Original, row.Translation, StringComparison.Ordinal)) continue;
			if (!ProtectedFormat.HasSameProtectedTokens(row.Original, row.Translation)) continue;
			fixes.Add(new FlatUiFix(row.Key, row.Original, row.Translation));
			knownFixKeys.Add(row.Key);
		}
		if (fixes.Count == 0) return payload;
		HashSet<string> changedKeys = new HashSet<string>(StringComparer.Ordinal);
		foreach (FlatUiFix fix in fixes)
		{
			if (!byKey.TryGetValue(fix.Key, out LocRow row))
				throw new InvalidDataException("Doğrulanmış UI satırı bulunamadı: " + fix.Key);
			bool sourceMatches = string.Equals(row.Original, fix.Source, StringComparison.Ordinal)
				|| string.Equals(row.Original, fix.Target, StringComparison.Ordinal);
			if (!sourceMatches)
			{
				// A root build applies the automatic pass after semantic targets, but
				// the same helper is also used directly on a clean DAT for cache and
				// recovery checks.  Accept the clean English form only when the
				// reviewed exact vocabulary independently resolves to this exact target;
				// otherwise a source drift remains fail-closed.
				string cleanTarget = ManualUiText.ExactForEnglish(row.Original);
				if (!string.Equals(cleanTarget, fix.Target, StringComparison.Ordinal)
					|| !ProtectedFormat.HasSameProtectedTokens(row.Original, cleanTarget))
					throw new InvalidDataException("UI satırı beklenmeyen kaynakla eşleşti: " + fix.Key);
			}
			if (string.Equals(row.Original, fix.Source, StringComparison.Ordinal)) changedKeys.Add(fix.Key);
			row.Translation = fix.Target;
		}
		if (changedKeys.Count == 0) return payload;

		byte[] rebuilt = bin.Rebuild(rows);
		LocBin verifyBin = LocBin.Parse(rebuilt, did);
		List<LocRow> verifyRows = verifyBin.GetRows(did);
		if (verifyRows.Count != rows.Count)
			throw new InvalidDataException("UI tablosu yeniden oluşturulurken satır sayısı değişti: 0x" + did.ToString("X8"));
		Dictionary<string, FlatUiFix> fixesByKey = fixes.ToDictionary(item => item.Key, StringComparer.Ordinal);
		for (int i = 0; i < rows.Count; i++)
		{
			if (!string.Equals(rows[i].Key, verifyRows[i].Key, StringComparison.Ordinal))
				throw new InvalidDataException("UI tablosu satır sırası değişti: 0x" + did.ToString("X8"));
			if (fixesByKey.TryGetValue(rows[i].Key, out FlatUiFix fix))
			{
				if (!string.Equals(verifyRows[i].Original, fix.Target, StringComparison.Ordinal))
					throw new InvalidDataException("UI hedef satırı doğrulanamadı: " + fix.Key);
			}
			else if (!string.Equals(verifyRows[i].Original, rows[i].Original, StringComparison.Ordinal))
				throw new InvalidDataException("UI tablosunda beklenmeyen satır değişti: " + rows[i].Key);
		}
		return rebuilt;
	}

	// Empty-leading variants are deliberately absent from the heuristic catalog.
	// Match the complete verified native record (ID, variants, parameter IDs and
	// subgroup count), never a loose byte/text substring. Keep parameter bytes.
	private static readonly Tuple<byte[], byte[]>[] HiddenTooltipFixes =
	{
		Hidden(0x0640A225, new[] { "", "m Range" }, new[] { "", "m Menzil" }, 0x005A6195),
		Hidden(0x0ADE6325, new[] { "", "m Range" }, new[] { "", "m Menzil" }, 0x005A6195),
		Hidden(0x028B5915, new[] { "", " ", " Damage" }, new[] { "", " ", " Hasar" }, 0x04624A34, 0x05BE1855),
		Hidden(0x0F616255, new[] { "", " Damage" }, new[] { "", " Hasar" }, 0x0E0F7997),
		Hidden(0x01894215, new[] { "", " - ", " ", " Damage" }, new[] { "", " - ", " ", " Hasar" }, 0x02864455, 0x048615B5, 0x05BE1855)
	};

	// The title suffix is stored in a conservative fallback table whose layout
	// is not safe to rebuild through ordinary catalog rows. Match its complete
	// native record so the player-created name before the suffix is untouched.
	private static readonly Tuple<byte[], byte[]>[] GondolinTitleFixes =
	{
		Hidden(0x0A4B0FF5, new[] { "#1:", "#1:{ [E]}#2:", " #3:", " of Gondolin" },
			new[] { "#1:", "#1:{ [E]}#2:", " #3:", " (Gondolinli)" },
			0x0005662B, 0x00052615, 0x08A72645)
	};

	private static byte[] ApplyGondolinTitleFixes(byte[] payload)
	{
		return ApplyExactRecordFixes(payload, GondolinTitleFixes, "Gondolin unvan kaydı");
	}

	private static Tuple<byte[], byte[]> Hidden(uint id, string[] source, string[] target, params uint[] parameters)
	{
		return Tuple.Create(HiddenRecord(id, source, parameters), HiddenRecord(id, target, parameters));
	}
	private static byte[] HiddenRecord(uint id, string[] variants, uint[] parameters)
	{
		using (var stream = new MemoryStream())
		using (var writer = new BinaryWriter(stream))
		{
			writer.Write((ulong)id); writer.Write(BuildRecord(variants));
			writer.Write(parameters.Length);
			foreach (uint parameter in parameters) writer.Write(parameter);
			writer.Write((byte)0); writer.Flush(); return stream.ToArray();
		}
	}
	internal static byte[] ApplyHiddenTooltipFixes(byte[] payload)
	{
		return ApplyExactRecordFixes(payload, HiddenTooltipFixes, "Hidden tooltip record");
	}

	private static byte[] ApplyExactRecordFixes(byte[] payload, Tuple<byte[], byte[]>[] fixes, string label)
	{
		foreach (var fix in fixes)
		{
			int source = IndexOf(payload, fix.Item1), target = IndexOf(payload, fix.Item2);
			if (source < 0 && target >= 0 && IndexOf(payload, fix.Item2, target + 1) < 0) continue;
			if (source < 0 || target >= 0 || IndexOf(payload, fix.Item1, source + 1) >= 0)
				throw new InvalidDataException(label + " is missing, changed or ambiguous.");
			byte[] result = new byte[payload.Length - fix.Item1.Length + fix.Item2.Length];
			Buffer.BlockCopy(payload, 0, result, 0, source);
			Buffer.BlockCopy(fix.Item2, 0, result, source, fix.Item2.Length);
			Buffer.BlockCopy(payload, source + fix.Item1.Length, result, source + fix.Item2.Length, payload.Length - source - fix.Item1.Length);
			payload = result;
		}
		return payload;
	}

	private static byte[] BuildRecord(params string[] variants)
	{
		using (MemoryStream stream = new MemoryStream())
		using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode, true))
		{
			writer.Write(variants.Length);
			foreach (string variant in variants)
			{
				if (variant.Length >= 128) throw new InvalidDataException("UI düzeltme metni çok uzun.");
				writer.Write((byte)variant.Length);
				writer.Write(Encoding.Unicode.GetBytes(variant));
			}
			return stream.ToArray();
		}
	}

	private static int IndexOf(byte[] haystack, byte[] needle, int start = 0)
	{
		for (int i = start; i <= haystack.Length - needle.Length; i++)
		{
			int j = 0;
			while (j < needle.Length && haystack[i + j] == needle[j]) j++;
			if (j == needle.Length) return i;
		}
		return -1;
	}
}
