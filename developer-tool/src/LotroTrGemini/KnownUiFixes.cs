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
		new FlatUiFix("250001BB:801:-1:0", "Mount skills will be shown here after they are acquired.", "Binek becerileri edinildikten sonra burada gösterilecektir."),
		new FlatUiFix("250001BB:846:-1:0", "Deaths: ", "Ölümler: "),
		new FlatUiFix("250001BB:880:-1:0", "Rank: ", "Rütbe: "),
		new FlatUiFix("250001BB:895:-1:0", "Assist Window", "Yardım Penceresi"),
		new FlatUiFix("250001BB:900:-1:0",
			"To equip a hobby, drag it from the Hobbies Panel to an available quickslot at the bottom of the game screen.",
			"Bir hobiyi kuşanmak için, onu Hobi Paneli'nden oyun ekranının altındaki uygun bir hızlı yuvaya sürükleyin."),
		new FlatUiFix("250001BB:917:-1:0", "Traits", "Özellikler")
	};

	public static bool HasAutomaticFix(int did)
	{
		return did == CharacterSelectionDid || did == FellowshipMenuDid || did == FellowshipUiDid;
	}

	public static byte[] ApplyTranslatedPayload(int did, byte[] payload)
	{
		if (payload == null || payload.Length == 0) return payload;
		if (did == CharacterSelectionDid) return ApplyCharacterSelectionFix(payload);
		if (did == FellowshipMenuDid || did == FellowshipUiDid)
			return ApplyFlatUiFixes(did, payload);
		return payload;
	}

	private static byte[] ApplyCharacterSelectionFix(byte[] payload)
	{
		byte[] source = BuildRecord("", " of ", " Character Slots Used");
		byte[] target = BuildRecord("", " / ", " KARAKTER YUVASI KULLANILIYOR");
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
		if (fixes.Count == 0) return payload;
		HashSet<string> changedKeys = new HashSet<string>(StringComparer.Ordinal);
		foreach (FlatUiFix fix in fixes)
		{
			if (!byKey.TryGetValue(fix.Key, out LocRow row))
				throw new InvalidDataException("Doğrulanmış UI satırı bulunamadı: " + fix.Key);
			if (!string.Equals(row.Original, fix.Source, StringComparison.Ordinal)
				&& !string.Equals(row.Original, fix.Target, StringComparison.Ordinal))
				throw new InvalidDataException("UI satırı beklenmeyen kaynakla eşleşti: " + fix.Key);
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
