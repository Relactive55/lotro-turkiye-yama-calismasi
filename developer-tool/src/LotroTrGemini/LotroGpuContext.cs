using System.Collections.Generic;

namespace LotroTrGemini;

internal static class LotroGpuContext
{
	internal static readonly string[] ProtectedNames = new string[48]
	{
		"Aragorn", "Arwen", "Bilbo", "Boromir", "Celeborn", "Denethor", "Elrond", "Éomer",
		"Éowyn", "Faramir", "Frodo", "Galadriel", "Gandalf", "Gimli", "Glorfindel", "Gollum",
		"Isildur", "Legolas", "Merry", "Pippin", "Radagast", "Samwise", "Saruman", "Sauron",
		"Shelob", "Théoden", "Treebeard", "Bree", "Erebor", "Eriador", "Gondor", "Lórien",
		"Mirkwood", "Mordor", "Moria", "Rivendell", "Rohan", "Shire", "Angmar", "Beleriand",
		"Middle-earth", "Minas Tirith", "Osgiliath", "Barad-dûr", "Mount Doom", "The Prancing Pony",
		"LOTRO", "The Lord of the Rings Online"
	};

	internal static readonly Dictionary<string, string> Glossary = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
	{
		{ "Middle-earth", "Orta Dünya" },
		{ "Fellowship", "Kardeşlik" },
		{ "War of the Ring", "Yüzük Savaşı" },
		{ "One Ring", "Tek Yüzük" },
		{ "Ring-bearer", "Yüzük Taşıyıcısı" },
		{ "quest", "görev" },
		{ "objective", "hedef" },
		{ "deed", "marifet" },
		{ "trait", "özellik" },
		{ "skill", "yetenek" },
		{ "damage", "hasar" },
		{ "armour", "zırh" },
		{ "armor", "zırh" },
		{ "morale", "moral" },
		{ "power", "güç" },
		{ "might", "kudret" },
		{ "agility", "çeviklik" },
		{ "vitality", "zindelik" },
		{ "will", "irade" },
		{ "fate", "kader" },
		{ "critical rating", "kritik vuruş oranı" },
		{ "physical mastery", "fiziksel ustalık" },
		{ "tactical mastery", "taktiksel ustalık" },
		{ "resistance", "direnç" },
		{ "mitigation", "azaltma" },
		{ "cooldown", "bekleme süresi" },
		{ "raid", "akın" },
		{ "instance", "zindan" },
		{ "crafting", "zanaatkârlık" },
		{ "recipe", "tarif" },
		{ "reward", "ödül" },
		{ "legendary item", "efsanevi eşya" },
		{ "legendary", "efsanevi" },
		{ "common", "yaygın" },
		{ "rare", "nadir" },
		{ "unique", "benzersiz" },
		{ "account", "hesap" },
		{ "character", "karakter" },
		{ "kinship", "soydaşlık" },
		{ "auction house", "müzayede evi" },
		{ "bind on acquire", "alındığında bağlanır" },
		{ "bind on equip", "kuşanıldığında bağlanır" },
		{ "requires", "gerektirir" },
		{ "defeat", "yen" },
		{ "travel", "seyahat" },
		{ "fast travel", "hızlı seyahat" },
		{ "player versus player", "oyuncuya karşı oyuncu" }
	};
}
