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
		{ "Gloves Slot", "Eldiven Yuvası" }, { "Feet Slot", "Ayak Yuvası" },
		{ "Legs slot", "Bacak yuvası" }, { "Legs Slot", "Bacak Yuvası" },
		{ "Gloves", "Eldivenler" }, { "Feet", "Ayak" }, { "Legs", "Bacaklar" }, { "Shoulder", "Omuz" },
		{ "Belt slot", "Kemer yuvası" }, { "Ranged Weapon Slot", "Menzilli silah yuvası" },
		{ "Ranged weapon slot", "Menzilli silah yuvası" }, { "Craft Tool slot", "Zanaat aracı yuvası" },
		{ "Primary Weapon Slot", "Birincil silah yuvası" },
		{ "Secondary Weapon/Shield Slot", "İkincil silah/kalkan yuvası" },
		{ "Fully Expands Quest Tree", "Görev ağacını tamamen genişletir" },
		{ "Fully Collapses Quest Tree", "Görev ağacını tamamen daraltır" },

		// Character-selection and launcher labels. These short strings occur in
		// the protected client-wide table, so they cannot safely rely on a
		// source-text-only translation-memory lookup.
		{ "Loading...", "Yükleniyor..." }, { "Loading... Please Wait", "Yükleniyor... Lütfen bekleyin" },
		{ "Delete Character", "Karakteri Sil" }, { "Change Race", "Irkı Değiştir" },
		{ "Characters", "Karakterler" }, { "Free Account", "Ücretsiz Hesap" },
		{ "Slots Used", "Kullanılan Yuvalar" }, { "Page", "Sayfa" },
		{ "Page 1/1", "Sayfa 1/1" }, { "Get Points & Account Upgrades", "Puanlar ve Hesap Yükseltmeleri" },
		{ "Get Points &", "Puanlar ve" }, { "Account Upgrades", "Hesap Yükseltmeleri" },
		{ "Monster Play", "Canavar Oyunu" }, { "Manage Plugins", "Eklentileri Yönet" },
		{ "Credits", "Emeği Geçenler" }, { "Movie Library", "Film Kütüphanesi" },
		{ "Quit", "Çıkış" }, { "Enter Middle-earth", "Orta Dünya'ya Gir" },
		{ "Entering Middle-earth", "Orta Dünya'ya Giriliyor" }, { "System", "Sistem" },
		{ "Inventory", "Envanter" }, { "Crafting", "Zanaatkârlık" }, { "Social", "Sosyal" },
		{ "Deed Log", "Başarım Günlüğü" }, { "Map", "Harita" }, { "Traits", "Özellikler" },
		{ "Hobbit Presents", "Hobbit Hediyeleri" }, { "Collections", "Koleksiyonlar" },
		{ "Filters", "Filtreler" }, { "Quest Log", "Görev Günlüğü" },
		{ "Friends", "Arkadaşlar" }, { "Combat", "Savaş" }, { "Skills", "Yetenekler" },
		{ "Character Selection", "Karakter Seçimi" }, { "Create", "Oluştur" },
		{ "Create Character", "Karakter Oluştur" }, { "Rename Character", "Karakteri Yeniden Adlandır" },
		{ "Play LOTRO", "LOTRO'yu Oyna" }, { "Play Movie", "Filmi Oynat" },
		{ "View the credits.", "Emeği geçenleri görüntüle." }, { "Start Monster Play", "Canavar Oyununu Başlat" },
		{ "Enter Monster Play", "Canavar Oyununa Gir" }, { "Get Characters", "Karakterleri Al" },
		{ "Get Perks", "Avantajları Al" }, { "Apply Session Buff", "Oturum Güçlendirmesini Uygula" },
		{ "Apply Change", "Değişikliği Uygula" }, { "Restore Character", "Karakteri Geri Yükle" },
		{ "Randomize", "Rastgeleleştir" }, { "Random Name", "Rastgele İsim" },
		{ "Generate Name", "İsim Oluştur" }, { "Customize", "Özelleştir" },
		{ "Details", "Ayrıntılar" }, { "Description", "Açıklama" }, { "Controls", "Kontroller" },
		{ "Level", "Seviye" }, { "Name", "İsim" }, { "Name:", "İsim:" }, { "Name (A-Z)", "İsim (A-Z)" },
		{ "Male", "Erkek" }, { "Female", "Kadın" }, { "Gender", "Cinsiyet" },
		{ "Head", "Baş" }, { "Body", "Vücut" }, { "Ears", "Kulaklar" },
		{ "Eye Colour", "Göz Rengi" }, { "Skin Colour", "Ten Rengi" }, { "Hair Colour", "Saç Rengi" },
		{ "Hair Style", "Saç Stili" }, { "Facial Hair", "Yüz Kılı" }, { "Height", "Boy" },
		{ "Body Type", "Vücut Tipi" }, { "Face Customization", "Yüz Özelleştirme" },
		{ "Before The Shadow", "Gölgeden Önce" }, { "Naming Guidelines", "İsimlendirme Yönergeleri" },
		{ "Presets", "Hazır Ayarlar" }, { "Session Buffs", "Oturum Güçlendirmeleri" },
		{ "Unavailable", "Kullanılamıyor" }, { "Update complete!", "Güncelleme tamamlandı!" },
		{ "Downloading update...", "Güncelleme indiriliyor..." }, { "Creating ...", "Oluşturuluyor..." },
		{ "Connecting...", "Bağlanıyor..." }, { "Disconnect", "Bağlantıyı Kes" },
		{ "Connexion successful!", "Bağlantı başarılı!" },
		{ "Connexion failed:" + "\\n", "Bağlantı başarısız:" + "\\n" },
		{ "Keyboard", "Klavye" }, { "Zoom In", "Yakınlaştır" }, { "Zoom Out", "Uzaklaştır" },
		{ "Next", "İleri" }, { "Back", "Geri" }, { "Cancel", "İptal" }, { "Continue", "Devam" },
		{ "Accept", "Kabul Et" }, { "Decline", "Reddet" }, { "Close", "Kapat" },
		{ "Yes", "Evet" }, { "No", "Hayır" }, { "Apply", "Uygula" }, { "Search", "Ara" },
		{ "Clear", "Temizle" }, { "Clear All", "Tümünü Temizle" }, { "All", "Tümü" },
		{ "Open World Map", "Dünya Haritasını Aç" }, { "Fullscreen Map", "Tam Ekran Haritası" },
		{ "Show Map", "Haritayı Göster" }, { "Close Window", "Pencereyi Kapat" },
		{ "Close inventory panel", "Envanter panelini kapat" }, { "Close the Social Panel", "Sosyal Paneli Kapat" },
		{ "Event Quickslots", "Etkinlik Hızlı Yuvaları" }, { "Mini Panel", "Mini Panel" },
		{ "Item Wear", "Eşya Yıpranması" }, { "Group Skills", "Grup Yetenekleri" },
		{ "Unlock", "Kilidi Aç" }, { "Reset", "Sıfırla" }, { "Refresh List", "Listeyi Yenile" },
		{ "Select Currency", "Para Birimi Seç" }, { "Training", "Eğitim" }, { "Report Bug", "Hata Bildir" },
		{ "Restore", "Geri Yükle" }, { "Active", "Aktif" }, { "Rotate Right", "Sağa Döndür" },
		{ "Neighbourhood Name", "Mahalle Adı" }, { "Expires in ", "Sona ermesine kalan süre: " },
		{ "Quests & Deeds", "Görevler ve Başarımlar" }, { "Group Leader", "Grup Lideri" },
		{ "Click and drag to move panel", "Paneli taşımak için tıklayıp sürükleyin" },
		{ "Appraise Item", "Eşyayı Değerlendir" }, { "Toggle Wallet", "Cüzdanı Aç/Kapat" },
		{ "Destroy", "Yok Et" }, { "Take All", "Tümünü Al" }, { "Show Completed Quests", "Tamamlanan Görevleri Göster" },
		{ "Customize Toolbar Slots", "Araç Çubuğu Yuvalarını Özelleştir" }, { "Quest Actions Button", "Görev Eylemleri Düğmesi" },
		{ "Open Entire Quest History for selected Quest.", "Seçili görevin tüm geçmişini aç." },
		{ "Filter this item on all characters", "Bu eşyayı tüm karakterlerde filtrele" },
		{ "Hide when Crafting", "Zanaatkârlık sırasında gizle" }, { "Lock and unlock items in your inventory", "Envanterindeki eşyaları kilitle ve kilidini aç" },
		{ "Toggle Decoration Mode", "Dekorasyon Modunu Aç/Kapat" }, { "Show Skills:", "Yetenekleri Göster:" },
		{ "Skill Queue", "Yetenek Kuyruğu" }, { "Invite", "Davet Et" }, { "Barter", "Takas" },
		{ "Comments", "Yorumlar" }, { "Items To Trade", "Takas Edilecek Eşyalar" }, { "General", "Genel" },
		{ "Shop", "Mağaza" }, { "Item to Receive", "Alınacak Eşya" }, { "Cost: ", "Maliyet: " },
		{ "Melee", "Yakın Dövüş" }, { "Ranged", "Menzilli" }, { "Fast", "Hızlı" }, { "Damage", "Hasar" },
		{ "Resistance: ", "Direnç: " }, { "Item Level: ", "Eşya Seviyesi: " }, { "Required crafting facility: ", "Gerekli zanaat tesisi: " },
		{ "Impact Arrows", "Darbeli Oklar" }, { "Barbed Fury", "Dikenli Öfke" },
		{ "Fast Draw", "Hızlı Çekiş" }, { "Hindering Shot", "Engelleyici Atış" },
		{ "Mercy Kill", "Merhamet Öldürüşü" }, { "Run Speed", "Koşu Hızı" },
		{ "Incoming Healing", "Gelen İyileştirme" }, { "Mitigations", "Hasar Azaltmaları" },
		{ "Damage Source", "Hasar Kaynağı" }, { "Damage Type", "Hasar Türü" },
		{ "Bind On Acquire", "Alındığında Bağlanır" }, { "Armour", "Zırh" }, { "Armor", "Zırh" },
		{ "Equipped", "Kuşanıldı" }, { "Worn", "Giyildi" }, { "Vitality", "Zindelik" },
		{ "Might", "Kuvvet" }, { "Agility", "Çeviklik" }, { "Will", "İrade" }, { "Fate", "Kader" },
		{ "Light Armour", "Hafif Zırh" }, { "Medium Armour", "Orta Zırh" }, { "Heavy Armour", "Ağır Zırh" },
		{ "Tactical", "Taktiksel" }, { "Physical Mastery", "Fiziksel Ustalık" },
		{ "Critical Defence", "Kritik Savunma" }, { "Critical Defense", "Kritik Savunma" },
		{ "Block", "Blok" }, { "Parry", "Savuş" }, { "Evade", "Kaçınma" },
		{ "Damage Source Qualifiers:", "Hasar Kaynağı Niteleyicileri:" },
		{ "Damage Type:", "Hasar Türü:" }, { "Damage Source:", "Hasar Kaynağı:" },
		{ "OPEN THE PLUGIN MANAGER TO MANAGE THE LOADING OF YOUR PLAYER-MADE LUA PLUGINS.", "Oyuncu yapımı LUA eklentilerinin yüklenmesini yönetmek için eklenti yöneticisini açın." },
		{ "Open the plugin manager to manage the loading of your player-made LUA plugins.", "Oyuncu yapımı LUA eklentilerinin yüklenmesini yönetmek için eklenti yöneticisini açın." },
		{ "DELETE CURRENTLY SELECTED CHARACTER.", "Şu anda seçili karakteri siler." },
		{ "Delete currently selected character.", "Şu anda seçili karakteri siler." },
		{ "Next Rank", "Sonraki Rütbe" }, { "Rank", "Rütbe" }, { "Ranks", "Rütbeler" },
		{ "Points", "Puan" }, { "Point", "Puan" }, { "Not earned", "Kazanılmadı" },
		{ "Earned", "Kazanıldı" }, { "Requirements", "Gereksinimler" }, { "Requirement", "Gereksinim" },

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
		{ "Forest Elk", "Orman Geyiği" }, { "Grey Horse", "Gri At" }, { "Happy Pig", "Mutlu Domuz" },

		// Common combat, effect and item vocabulary.  These are complete source
		// labels (not substring rules), so a proper name containing one of these
		// words is never changed accidentally.
		{ "Healing", "İyileştirme" }, { "Poison", "Zehir" }, { "Disease", "Hastalık" },
		{ "Fear", "Korku" }, { "Wound", "Yara" }, { "Fury", "Öfke" }, { "Stun", "Sersemletme" },
		{ "Gate", "Kapı" }, { "Heavy", "Ağır" }, { "Medium", "Orta" }, { "Light", "Hafif" },
		{ "Pip Cost", "Pip Maliyeti" }, { "Attack Speed", "Saldırı Hızı" },
		{ " Run Speed", " Koşu Hızı" }, { " Target Run Speed", " Hedef Koşu Hızı" },
		{ " Out-of-Combat Run Speed", " Savaş Dışı Koşu Hızı" }, { " Out of Combat Run Speed", " Savaş Dışı Koşu Hızı" },
		{ " Incoming Healing", " Gelen İyileştirme" }, { " Outgoing Healing", " Giden İyileştirme" },
		{ "Movement Speed", "Hareket Hızı" }, { "Stealth Movement Speed", "Gizli Hareket Hızı" },
		{ "Combat Stealth Speed", "Savaş Gizlilik Hızı" }, { "Maximum Health", "Maksimum Sağlık" },
		{ "Outgoing Healing", "Giden İyileştirme" }, { "Effect Duration", "Etki Süresi" },
		{ "Healing Over Time", "Zaman İçinde İyileştirme" }, { "Anthem Duration", "Ezgi Süresi" },
		{ "Fleetness Duration", "Çeviklik Süresi" }, { "Charge Duration", "Hücum Süresi" },
		{ "Dash Duration", "Atılma Süresi" }, { "Slow", "Yavaşlatma" },
		{ "Tactical Rank", "Taktik Rütbesi" }, { "Attack Duration", "Saldırı Süresi" },
		{ "Improved Hindering Shot", "Geliştirilmiş Engelleyici Atış" },
		{ "Penetrating Shot", "Delici Atış" }, { "Surprise Attack", "Sürpriz Saldırı" },
		{ "Pinning Shot", "Mıhlayıcı Atış" }, { "Wild Attack", "Vahşi Saldırı" },
		{ "Powerful Shot", "Güçlü Atış" }, { "Strong Draw", "Güçlü Çekiş" },
		{ "True Shot", "Gerçek Atış" }, { "Heavy Draw", "Ağır Çekiş" }, { "Long Draw", "Uzun Çekiş" },
		{ "Crippling Shot", "Sakatlayıcı Atış" }, { "Distracting Shot", "Dikkat Dağıtan Atış" },
		{ "Merciful Shot", "Merhametli Atış" }, { "Deadly Shot", "Ölümcül Atış" },
		{ "Snaring Shot", "Kapan Atışı" }, { "Barbed Shot", "Dikenli Atış" },
		{ "Entangling Shot", "Dolaştırıcı Atış" }, { "Raining Arrows", "Yağan Oklar" },
		{ "Arrows Overhead", "Tepedeki Oklar" }, { "Penetrating Arrows", "Delici Oklar" },
		{ "Poison Arrow", "Zehirli Ok" }, { "Poison Arrow Attack", "Zehirli Ok Saldırısı" },
		{ "Poison Strike", "Zehir Saldırısı" }, { "Poison Spray", "Zehir Püskürtme" },
		{ "Poison Cloud", "Zehir Bulutu" }, { "Noxious Poison Cloud", "Zararlı Zehir Bulutu" },
		{ "Crippling Poison", "Sakatlayıcı Zehir" }, { "Fatal Poison", "Ölümcül Zehir" },
		{ "Deadly Poison", "Öldürücü Zehir" }, { "Lethargic Poison", "Uyuşukluk Zehiri" },
		{ "Virulent Poison", "Şiddetli Zehir" }, { "Acrid Poison", "Acı Zehir" },
		{ "Latent Poison", "Gizli Zehir" }, { "Sapping Poison", "Sömürücü Zehir" },
		{ "Remove Corruption", "Yozlaşmayı Kaldır" }, { "Remove Fear", "Korkuyu Kaldır" },
		{ "Remove Wounds", "Yaraları Kaldır" }, { "Remove Poison", "Zehri Kaldır" },
		{ "Remove Disease", "Hastalığı Kaldır" }, { "Cure Poison", "Zehri İyileştir" },
		{ "Festering Wound", "İrinli Yara" }, { "Deep Wound", "Derin Yara" },
		{ "Bleeding Wound", "Kanayan Yara" }, { "Gushing Wound", "Fışkıran Yara" },
		{ "Weeping Wound", "Sızlayan Yara" }, { "Infected Wound", "Enfekte Yara" },
		{ "Debilitating Wound", "Zayıflatıcı Yara" }, { "Jagged Wound", "Tırtıklı Yara" },
		{ "Grievous Wound", "Ağır Yara" }, { "Mortal Wound", "Ölümcül Yara" },
		{ "Burning Wound", "Yanan Yara" }, { "Lingering Fear", "Kalıcı Korku" },
		{ "Hounding Fear", "Takipçi Korkusu" }, { "Blind Fury", "Kör Öfke" },
		{ "Controlled Fury", "Kontrollü Öfke" }, { "Rising Fury", "Yükselen Öfke" },
		{ "Orkish Fury", "Ork Öfkesi" }, { "Battle Fury", "Savaş Öfkesi" },
		{ "Dwarven Fury", "Cüce Öfkesi" }, { "Ancient Fury", "Kadim Öfke" },
		{ "Vile Fury", "Aşağılık Öfke" },

		// Protected fallback-table labels and tooltips.  These complete-string
		// translations cover the item/trait/equipment text that is not part of
		// the semantic catalogue; placeholders and markup are kept in the source
		// row when a fragment is translated by the table-aware writer.
		{ "Empty Slot", "Boş Yuva" },
		{ "You must slot all of these traits:", "Bu özelliklerin tümünü yuvaya yerleştirmelisiniz:" },
		{ "You must slot at least one of these traits:", "Bu özelliklerden en az birini yuvaya yerleştirmelisiniz:" },
		{ "You must have the following trait slotted:", "Şu özelliği yuvaya yerleştirmelisiniz:" },
		{ "You must have none of these traits slotted:", "Bu özelliklerin hiçbiri yuvaya yerleştirilmemeli:" },
		{ " line slotted.", " satıra yerleştirildi." },
		{ "Worn on shield slot", "Kalkan yuvasında giyilir" },
		{ "Worn on Craft Tool slot", "Zanaat aracı yuvasında giyilir" },
		{ "Worn on weapon slot", "Silah yuvasında giyilir" },
		{ "Worn on wrist", "Bilekte giyilir" }, { "Worn on belt", "Kemerde giyilir" },
		{ "Worn on legs", "Bacakta giyilir" }, { "Worn on feet", "Ayaklarda giyilir" },
		{ "Worn on chest", "Göğüste giyilir" }, { "Worn on hands", "Elde giyilir" },
		{ "Worn on head", "Başta giyilir" }, { "Worn on shoulders", "Omuzlarda giyilir" },
		{ "Worn on neck", "Boyunda giyilir" }, { "Worn on finger", "Parmakta giyilir" },
		{ "Worn on ear", "Kulakta giyilir" }, { "Worn in pocket", "Cep yuvasında giyilir" },
		{ "Worn on:", "Giyildiği yer:" },
		{ "Secondary Weapon Aura Slot", "İkincil Silah Aurası Yuvası" },
		{ "Primary Weapon Aura Slot", "Birincil Silah Aurası Yuvası" },
		{ "Ranged Weapon Aura Slot", "Menzilli Silah Aurası Yuvası" },
		{ "Secondary Weapon Slot", "İkincil silah yuvası" },
		{ "Bonus Active Quest Slots", "Etkin Görev Yuvaları Bonusu" },
		{ "Next bonus slot: (", "Sonraki bonus yuva: (" },
		{ "Slot requires purchase from the LOTRO Store.", "Yuva LOTRO Mağazası'ndan satın alınmalıdır." },
		{ "Can be used to repair the following item types:", "Şu eşya türlerini onarmak için kullanılabilir:" },
		{ "This item can be used for crafting.", "Bu eşya zanaatkârlık için kullanılabilir." },
		{ "This item is broken, and must be repaired before it can be equipped.", "Bu eşya kırılmıştır; kuşanılmadan önce onarılmalıdır." },
		{ "This component can be used as an optional ingredient when creating new items.", "Bu bileşen yeni eşyalar oluşturulurken isteğe bağlı malzeme olarak kullanılabilir." },
		{ "This component can only be applied to items of the following type(s):", "Bu bileşen yalnızca şu eşya türlerine uygulanabilir:" },
		{ "This component can be used with recipes that create the following item type(s):", "Bu bileşen, şu eşya türlerini oluşturan tariflerle kullanılabilir:" },
		{ "Pattern: ", "Kalıp: " }, { "Materials: ", "Malzemeler: " },
		{ "Vital Cost:", "Can Maliyeti:" }, { "Armour Stats:", "Zırh İstatistikleri:" },
		{ "Shield Stats:", "Kalkan İstatistikleri:" }, { "Experience Stats:", "Deneyim İstatistikleri:" },
		{ "Material Components:", "Malzeme Bileşenleri:" }, { "Nothing Selected", "Hiçbir şey seçilmedi" },
		{ "Lock Status: Unlocked", "Kilit Durumu: Açık" }, { "Pending", "Beklemede" },
		{ "Current Instance: ", "Mevcut Örnek: " }, { "Next Skill: ", "Sonraki Yetenek: " },
		{ "Toggle Skill", "Yeteneği Aç/Kapat" }, { "Available XP: ", "Mevcut Deneyim: " },
		{ "Value: ", "Değer: " }, { "Progress: ", "İlerleme: " }, { "Occupancy: ", "Doluluk: " },
		{ "Time Remaining: ", "Kalan Süre: " }, { "Name: ", "İsim: " }, { "Requires: ", "Gerektirir: " },
		{ "Requires no more than ", "Şundan fazla olmamalı: " }, { " players in fellowship", " grup üyesi" },
		{ "Requires: Infamy Rank ", "Gerektirir: Kötülük Rütbesi " },
		{ "Minimum item level for reinforcement: ", "Güçlendirme için minimum eşya seviyesi: " },
		{ "Maximum repaired item level: ", "Onarılabilecek en yüksek eşya seviyesi: " },
		{ "Applied Reinforcements:", "Uygulanan Güçlendirmeler:" }, { "Target Effects:", "Hedef Etkileri:" },
		{ "Barring Effects:", "Engellenen Etkiler:" }, { "Restricted Effect:", "Kısıtlı Etki:" },
		{ "When in Position:", "Konumdayken:" }, { "Single Use Recipe", "Tek Kullanımlık Tarif" },
		{ "Applies a title to a legendary weapon.", "Efsanevi bir silaha unvan uygular." },
		{ "Gloom: ", "Kasvet: " }, { "Dye: ", "Boya: " }, { "Attacker", "Saldıran" },
		{ "This is a monster player!", "Bu bir canavar oyuncusudur!" },
		{ "This house is currently unowned and available for purchase.", "Bu evin şu anda sahibi yok ve satın alınabilir." },
		{ "This house is currently unowned, however it is not yet available for purchase.", "Bu evin şu anda sahibi yok; ancak henüz satın alınabilir değil." },
		{ "Must use within a neighbourhood in which you or your kinship owns a house.", "Sizin veya kardeşliğinizin bir eve sahip olduğu mahallede kullanılmalıdır." },
		{ "Must use within a neighbourhood in which your kinship owns a house.", "Kardeşliğinizin bir eve sahip olduğu mahallede kullanılmalıdır." },
		{ "Can not summon in this area.", "Bu bölgede çağrılamaz." }, { "This can only be used at night", "Yalnızca gece kullanılabilir" },
		{ "This can only be used during the day.", "Yalnızca gündüz kullanılabilir." },
		{ "Successful use will corrupt you.", "Başarılı kullanım sizi yozlaştırır." },
		{ "You need #1:", "Gerekli: #1:" },
		{ " more #1:{ranks | rank[1]} in other traits in the whole tree (", " tüm ağaçtaki diğer özelliklerde #1:{rütbeler | rütbe[1]} daha (" },
		{ " more #1:{ranks | rank[1]} in other traits in the ", " diğer özelliklerde #1:{rütbeler | rütbe[1]} daha, şu " },
		{ " more #1:{points | point[1]} in the ", " şu dalda #1:{puanlar | puan[1]} daha " },
		{ " branch (", " dalı (" }, { "Usage blocked by an active effect or item.", "Kullanım etkin bir etki veya eşya tarafından engellendi." },
		{ "</rgb> extra quest slots for being VIP.\\n", "</rgb> VIP olduğunuz için ek etkin görev yuvası.\\n" },
		{ "</rgb> extra quest slots through pre-order.\\n", "</rgb> ön sipariş yoluyla ek etkin görev yuvası.\\n" },
		{ "</rgb> extra quest slots through mithril purchase.\\n", "</rgb> Mithril satın alımıyla ek etkin görev yuvası.\\n" },
		{ "</rgb> extra quest slots through completed deeds.\\n\\n ", "</rgb> tamamlanan başarımlar yoluyla ek etkin görev yuvası.\\n\\n " },
		{ "You can open an additional active quest slot for every ", "Her biri için bir ek etkin görev yuvası açabilirsiniz: " },
		{ " additional active quest slots from completed deeds.\\n\\n ", " tamamlanan başarımlardan ek etkin görev yuvası.\\n\\n " },

		// Remaining frequently visible menu and tooltip labels.
		{ "Open the Deed Log", "Başarım Günlüğünü Aç" }, { "Monster", "Canavar" }, { "Make Coins", "Para Kazan" },
		{ "Double-click to send a tell to this character.", "Bu karaktere özel mesaj göndermek için çift tıklayın." },
		{ "(Choose One)", "(Birini Seçin)" }, { "Increased Standing with", "Şununla İtibar Artışı:" },
		{ "Elite", "Seçkin" }, { "Raid Invitation", "Baskın Daveti" },
		{ "Lock map to character position: On.", "Haritayı karakter konumuna kilitle: Açık." },
		{ "Lock map to character position: Off.", "Haritayı karakter konumuna kilitle: Kapalı." },
		{ "Revive for Mithril Coins", "Mithril Paraları karşılığında diril" }, { "Completion Time:", "Tamamlanma Süresi:" },
		{ "Increase Scale", "Ölçeği Artır" }, { "Reduce Scale", "Ölçeği Azalt" }, { "Please wait ...", "Lütfen bekleyin ..." },
		{ "Mount Display Panel", "Binek Görüntüleme Paneli" }, { "Tutorial Hints", "Öğretici İpuçları" },
		{ "End Travel", "Seyahati Sonlandır" },
		{ "Click and drag to move the Event Quickslots.", "Etkinlik hızlı yuvalarını taşımak için tıklayıp sürükleyin." },
		{ "Click and drag to move the Event panel.", "Etkinlik panelini taşımak için tıklayıp sürükleyin." },
		{ "Toggles this quickslot between horizontal and vertical orientation", "Bu hızlı yuvayı yatay ve dikey yön arasında değiştirir" },
		{ "All ingredient slots will be cleared.  Close anyway?", "Tüm malzeme yuvaları temizlenecek. Yine de kapatılsın mı?" },
		{ " emote, there were no slots available on the current panel!\\n", " emote için mevcut panelde uygun yuva yok!\\n" },
		{ ", as there are no slots available on the current panel!\\n", ", çünkü mevcut panelde uygun yuva yok!\\n" },
		{ "Unlock Slots", "Yuvaların Kilidini Aç" },
		{ "Dye colour preview for the Back slot. Press Reset button to clear.", "Sırt yuvası boya önizlemesi. Temizlemek için Sıfırla düğmesine basın." },
		{ "Dye colour preview for the Boots slot. Press Reset button to clear.", "Çizme yuvası boya önizlemesi. Temizlemek için Sıfırla düğmesine basın." },
		{ "Dye colour preview for the Legs slot. Press Reset button to clear.", "Bacak yuvası boya önizlemesi. Temizlemek için Sıfırla düğmesine basın." },
		{ "Dye colour preview for the Chest slot. Press Reset button to clear.", "Göğüs yuvası boya önizlemesi. Temizlemek için Sıfırla düğmesine basın." },
		{ "Dye colour preview for the Head slot. Press Reset button to clear.", "Baş yuvası boya önizlemesi. Temizlemek için Sıfırla düğmesine basın." },
		{ "Dye colour preview for the Shoulder slot. Press Reset button to clear.", "Omuz yuvası boya önizlemesi. Temizlemek için Sıfırla düğmesine basın." },
		{ "Dye colour preview for the Gloves slot. Press Reset button to clear.", "Eldiven yuvası boya önizlemesi. Temizlemek için Sıfırla düğmesine basın." },
		{ "Drag a skill into this slot to set it as your default attack.", "Varsayılan saldırınız yapmak için bir yeteneği bu yuvaya sürükleyin." },
		{ "ALERT! The store is not currently available, please try again later.", "UYARI! Mağaza şu anda kullanılamıyor; lütfen daha sonra tekrar deneyin." },
		{ "Shards to Earn:", "Kazanılacak Parçalar:" }, { "Product Types", "Ürün Türleri" },
		{ "Please enter only numbers", "Lütfen yalnızca sayı girin" }, { "Please enter only numbers.", "Lütfen yalnızca sayı girin." },
		{ "Report Spam", "İstenmeyen Mesaj Bildir" }, { "Hide items bound to other characters", "Diğer karakterlere bağlı eşyaları gizle" },
		{ "Looking for players for this instance", "Bu örnek için oyuncu aranıyor" }, { "You don't have anything selected.", "Hiçbir şey seçmediniz." },
		{ "Choose Your Class", "Sınıfınızı Seçin" }, { "Choose Class", "Sınıf Seç" }, { "Strength-based", "Kuvvet tabanlı" },
		{ "Frame Rate", "Kare Hızı" }, { "Deflect", "Saptırma" }, { "Grouping", "Gruplama" }, { "Powers", "Güçler" },
		{ "Other", "Diğer" },
		{ "Maintenance", "Bakım" }, { "Take", "Al" }, { "Cost", "Maliyet" }, { "Willpower", "İrade Gücü" },
		{ "Glove Colour", "Eldiven Rengi" }, { "Player Bio:", "Oyuncu Biyografisi:" }, { "Enter character name:", "Karakter adını girin:" },
		{ "Gold", "Altın" }, { "Delete", "Sil" }, { "Available Skills", "Mevcut Yetenekler" },
		{ "External Container", "Harici Kutu" }, { "Not Roleplaying", "Rol Yapmıyor" }, { "Choose Your Name", "İsminizi Seçin" },
		{ "Give Gift", "Hediye Ver" }, { "Companion Shortcut Bar", "Yoldaş Kısayol Çubuğu" }, { "Send", "Gönder" },
		{ "Legendary Item", "Efsanevi Eşya" }, { "True", "Doğru" }, { "Bio", "Biyografi" }, { "Unknown", "Bilinmiyor" },
		{ "Pick a Colour", "Renk Seç" }, { "Find a fellowship for the selected quest.", "Seçili görev için bir kardeşlik bul." },
		{ "Gambit Panel", "Gambit Paneli" }, { "Housing Management", "Konut Yönetimi" }, { "Rotate Left", "Sola Döndür" },
		{ "Train", "Eğit" }, { "Dismount", "Binekten İn" }, { "Stable-masters", "Ahır Ustaları" },
		{ "Change Chat Font Size", "Sohbet Yazı Boyutunu Değiştir" },
		{ "Refresh", "Yenile" }, { "Storage", "Depo" }, { "Signature", "İmza" }, { "Update", "Güncelle" },
		{ "Open", "Aç" }, { "Lock", "Kilitle" }, { "Buy House", "Ev Satın Al" }, { "Trainer", "Eğitmen" },
		{ "Alias: ", "Takma ad: " }, { "Sort contents by quantity", "İçeriği miktara göre sırala" },
		{ "List", "Liste" }, { "Current Destiny Points: ", "Mevcut Kader Puanı: " }, { "Leave your fellowship.", "Kardeşliğinizden ayrılın." },
		{ "Current location", "Mevcut konum" }, { "Mail", "Posta" }, { "Destiny Wallet", "Kader Cüzdanı" },
		{ "Auto Skill Bar", "Otomatik Yetenek Çubuğu" }, { "Full", "Dolu" }, { "Gathering Information...", "Bilgiler alınıyor..." },
		{ "Link lost", "Bağlantı kesildi" }, { "Fellowship Invitation", "Kardeşlik Daveti" }, { "Vocations", "Meslekler" },
		{ "Maximum Rank Achieved", "Maksimum Rütbeye Ulaşıldı" }, { "Default", "Varsayılan" }, { "Alt", "Alt" },
		{ "Drag items here to make a shortcut.", "Kısayol oluşturmak için eşyaları buraya sürükleyin." },
		{ "Leaderboard update failed.", "Liderlik tablosu güncellenemedi." }, { "Destiny Points", "Kader Puanları" },
		{ "Vault", "Kasa" }, { "Score", "Skor" }, { "Housing Storage", "Konut Deposu" }, { "Get LOTRO Points", "LOTRO Puanları Al" },
		{ "Untraining", "Eğitim Geri Alma" }, { "Dye Options", "Boya Seçenekleri" }, { "Show Only Equipped Items", "Yalnızca Kuşanılmış Eşyaları Göster" },
		{ "Find Items", "Eşya Bul" }, { "Item Powers", "Eşya Güçleri" }, { "Hobby Trainer", "Hobi Eğitmeni" }, { "Barber Shop", "Berber" },
		{ "Immune", "Bağışık" }, { "Alerts", "Uyarılar" }, { "Recruit a new fellowship member.", "Yeni bir kardeşlik üyesi al." }
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
