using System;
using System.IO;
using System.Text;

namespace LotroTrGemini;

/// <summary>
/// Handles verified official UI variants that the conservative flat fallback
/// catalog intentionally does not expose as ordinary translation rows.
/// </summary>
public static class KnownUiFixes
{
	private const int CharacterSelectionDid = unchecked((int)0x250001BDu);

	public static byte[] ApplyTranslatedPayload(int did, byte[] payload)
	{
		if (did != CharacterSelectionDid || payload == null || payload.Length == 0)
			return payload;

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
