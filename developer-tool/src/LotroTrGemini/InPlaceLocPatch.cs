using System;
using System.Collections.Generic;
using System.Text;

namespace LotroTrGemini;

public static class InPlaceLocPatch
{
	public sealed class Stats
	{
		public int Applied;

		public int SkippedLong;

		public int SkippedNotFound;

		public int MultiHit;
	}

	public static byte[] Apply(byte[] payload, LocBin bin, IList<KeyValuePair<string, string>> replacements, out Stats stats)
	{
		stats = new Stats();
		if (payload == null || payload.Length == 0 || replacements == null || replacements.Count == 0)
		{
			return null;
		}
		byte[] array = (byte[])payload.Clone();
		bool flag = false;
		if (bin != null && bin.FlatEntries != null && bin.FlatEntries.Count > 0)
		{
			Dictionary<string, List<FlatEntry>> dictionary = new Dictionary<string, List<FlatEntry>>(StringComparer.Ordinal);
			foreach (FlatEntry flatEntry in bin.FlatEntries)
			{
				if (flatEntry != null && !string.IsNullOrEmpty(flatEntry.Text))
				{
					if (!dictionary.TryGetValue(flatEntry.Text, out var value))
					{
						value = new List<FlatEntry>();
						dictionary[flatEntry.Text] = value;
					}
					value.Add(flatEntry);
				}
			}
			foreach (KeyValuePair<string, string> replacement in replacements)
			{
				string text = replacement.Key ?? "";
				string text2 = replacement.Value ?? "";
				if (text.Length == 0 || string.Equals(text, text2, StringComparison.Ordinal))
				{
					continue;
				}
				if (!dictionary.TryGetValue(text, out var value2) || value2.Count == 0)
				{
					stats.SkippedNotFound++;
					continue;
				}
				if (value2.Count > 1)
				{
					stats.MultiHit++;
				}
				foreach (FlatEntry item in value2)
				{
					if (text2.Length > item.CharLen)
					{
						stats.SkippedLong++;
					}
					else if (WritePadded(array, item.Offset + item.HeaderSize, item.CharLen, text2))
					{
						stats.Applied++;
						flag = true;
					}
				}
			}
			if (!flag)
			{
				return null;
			}
			return array;
		}
		foreach (KeyValuePair<string, string> replacement2 in replacements)
		{
			string text3 = replacement2.Key ?? "";
			string text4 = replacement2.Value ?? "";
			if (text3.Length == 0 || string.Equals(text3, text4, StringComparison.Ordinal))
			{
				continue;
			}
			if (text4.Length > text3.Length)
			{
				stats.SkippedLong++;
				continue;
			}
			int found;
			int num = ReplaceAllVarUtf16(array, text3, text4, out found);
			if (found == 0)
			{
				stats.SkippedNotFound++;
			}
			else if (found > 1)
			{
				stats.MultiHit++;
			}
			if (num > 0)
			{
				stats.Applied += num;
				flag = true;
			}
		}
		if (!flag)
		{
			return null;
		}
		return array;
	}

	private static bool WritePadded(byte[] buf, int bodyOff, int charSlots, string neu)
	{
		if (bodyOff < 0 || charSlots <= 0)
		{
			return false;
		}
		if (bodyOff + charSlots * 2 > buf.Length)
		{
			return false;
		}
		if (neu == null)
		{
			neu = "";
		}
		if (neu.Length > charSlots)
		{
			return false;
		}
		for (int i = 0; i < charSlots; i++)
		{
			char c = ((i < neu.Length) ? neu[i] : ' ');
			buf[bodyOff + i * 2] = (byte)(c & 0xFF);
			buf[bodyOff + i * 2 + 1] = (byte)(((int)c >> 8) & 0xFF);
		}
		return true;
	}

	private static int ReplaceAllVarUtf16(byte[] buf, string oldT, string neu, out int found)
	{
		found = 0;
		byte[] bytes = Encoding.Unicode.GetBytes(oldT);
		if (bytes.Length == 0)
		{
			return 0;
		}
		int num = 0;
		int num2 = 0;
		while (num2 <= buf.Length - bytes.Length)
		{
			if (!BytesEqualAt(buf, num2, bytes))
			{
				num2++;
				continue;
			}
			int num3 = num2;
			int length = oldT.Length;
			if ((num3 < 1 || buf[num3 - 1] != (byte)length || length >= 128) && (num3 < 2 || length < 128 || length > 32767 || buf[num3 - 2] != (byte)(0x80 | (length >> 8)) || buf[num3 - 1] != (byte)(length & 0xFF)))
			{
				num2++;
				continue;
			}
			found++;
			if (WritePadded(buf, num3, length, neu))
			{
				num++;
			}
			num2 = num3 + bytes.Length;
		}
		return num;
	}

	private static bool BytesEqualAt(byte[] buf, int off, byte[] needle)
	{
		for (int i = 0; i < needle.Length; i++)
		{
			if (buf[off + i] != needle[i])
			{
				return false;
			}
		}
		return true;
	}
}
