using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace LotroTrGemini;

public static class DatPatcher
{
	public static void WriteNewDat(string sourceDat, string destDat, List<PatchItem> items, Action<string> progress)
	{
		if (items == null || items.Count == 0)
		{
			throw new InvalidOperationException("Yazılacak değişen öğe yok (hepsi orijinal).");
		}
		progress = progress ?? ((Action<string>)delegate
		{
		});
		progress("Kopyalanıyor (1.8 GB, bir kez)…");
		CopyFileFast(sourceDat, destDat);
		progress($"Yama: {items.Count:N0} değişen alt dosya yazılıyor…");
		using TurbineDat turbineDat = new TurbineDat();
		turbineDat.Open(destDat, writable: true);
		turbineDat.BuildEntryIndex();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int count = items.Count;
		Stopwatch stopwatch = Stopwatch.StartNew();
		for (int num4 = 0; num4 < items.Count; num4++)
		{
			PatchItem patchItem = items[num4];
			try
			{
				if (patchItem.NewRaw == null)
				{
					num2++;
					continue;
				}
				uint offset = patchItem.Offset;
				uint num5 = ((patchItem.OriginalRaw != null) ? ((uint)patchItem.OriginalRaw.Length) : 0u);
				if (turbineDat.TryGetEntry(patchItem.Did, out var entry) && entry != null)
				{
					if (entry.Offset != 0)
					{
						offset = entry.Offset;
					}
					if (entry.Size != 0)
					{
						num5 = entry.Size;
					}
				}
				if (offset == 0)
				{
					num2++;
					continue;
				}
				int num6 = turbineDat.MeasureCapacity(offset);
				if (num6 <= 0)
				{
					num3++;
					continue;
				}
				int num7 = patchItem.NewRaw.Length;
				num7 = ((num5 != 0 && num5 <= (uint)num6) ? ((int)num5) : ((patchItem.OriginalRaw == null || patchItem.OriginalRaw.Length > num6) ? Math.Min(patchItem.NewRaw.Length, num6) : patchItem.OriginalRaw.Length));
				if (num7 <= 0 || (patchItem.NewRaw.Length > num6 && num7 > num6))
				{
					num3++;
					if (num3 <= 15)
					{
						progress($"  skip cap 0x{patchItem.Did:X8} new={patchItem.NewRaw.Length} cap={num6}");
					}
					continue;
				}
				byte[] array = patchItem.NewRaw;
				if (array.Length == num7)
				{
					goto IL_0215;
				}
				if (array.Length > num7)
				{
					num3++;
					continue;
				}
				byte[] array2 = new byte[num7];
				Buffer.BlockCopy(array, 0, array2, 0, array.Length);
				array = array2;
				goto IL_0215;
				IL_0215:
				if (array.Length > num6)
				{
					num3++;
					continue;
				}
				turbineDat.WriteChain(offset, array);
				num++;
				if (num % 400 == 0 || num + num2 + num3 == count)
				{
					double num8 = (double)num / Math.Max(0.5, stopwatch.Elapsed.TotalSeconds);
					int val = count - num - num2 - num3;
					string text = ((num8 > 0.1) ? TimeSpan.FromSeconds((double)Math.Max(0, val) / num8).ToString("mm\\:ss") : "—");
					progress($"  yazı {num:N0}/{count:N0}  ·  fail={num2}  ·  skip={num3}  ·  ETA {text}");
				}
			}
			catch (Exception ex)
			{
				if (num2 < 25)
				{
					progress("! 0x" + patchItem.Did.ToString("X8") + " " + ex.Message);
				}
				num2++;
			}
		}
		progress($"Bitti OK={num:N0} FAIL={num2} SKIP_CAP={num3} ({stopwatch.Elapsed:mm\\:ss})");
		if (num == 0 && count > 0)
		{
			throw new IOException($"Hiç alt dosya yazılamadı (FAIL={num2} SKIP={num3}).");
		}
	}

	private static void CopyFileFast(string src, string dest)
	{
		using FileStream fileStream = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 4194304, FileOptions.SequentialScan);
		using FileStream fileStream2 = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 4194304, FileOptions.SequentialScan);
		byte[] array = new byte[4194304];
		int count;
		while ((count = fileStream.Read(array, 0, array.Length)) > 0)
		{
			fileStream2.Write(array, 0, count);
		}
	}
}
