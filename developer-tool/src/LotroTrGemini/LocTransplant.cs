using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LotroTrGemini;

public static class LocTransplant
{
	public sealed class Result
	{
		public int Ok;

		public int Same;

		public int Fail;

		public int SkipNoEn;

		public int TokenTableWritten;

		public string DestPath;

		public string FailLogPath;

		public TimeSpan Elapsed;

		public string Mode;

		public long OutSize;
	}

	public const int DidTokenTable = 620757423;

	public static Result Apply(string enDat, string trDat, string outDat, Action<string> progress = null, string failLogPath = null, bool forceSubfile = false)
	{
		if (string.IsNullOrEmpty(enDat) || !File.Exists(enDat))
		{
			throw new FileNotFoundException("EN DAT yok", enDat);
		}
		if (string.IsNullOrEmpty(trDat) || !File.Exists(trDat))
		{
			throw new FileNotFoundException("TR DAT yok", trDat);
		}
		if (string.IsNullOrEmpty(outDat))
		{
			throw new ArgumentException("outDat");
		}
		string directoryName = Path.GetDirectoryName(outDat);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		if (string.IsNullOrEmpty(failLogPath))
		{
			failLogPath = Path.Combine(directoryName ?? ".", "EnTrApply_fail.txt");
		}
		progress = progress ?? ((Action<string>)delegate
		{
		});
		Stopwatch sw = Stopwatch.StartNew();
		if (!forceSubfile)
		{
			return ApplyTrFullCopy(trDat, outDat, failLogPath, progress, sw);
		}
		return ApplyTurbineSubfiles(enDat, trDat, outDat, failLogPath, progress, sw);
	}

	private static Result ApplyTrFullCopy(string trDat, string outDat, string failLogPath, Action<string> progress, Stopwatch sw)
	{
		progress("TR paket tam kopya (LocTransplant-TrFullCopy)…");
		string text = outDat + ".building";
		if (File.Exists(text))
		{
			File.Delete(text);
		}
		if (File.Exists(outDat) && !string.Equals(Path.GetFullPath(outDat), Path.GetFullPath(trDat), StringComparison.OrdinalIgnoreCase))
		{
			File.Delete(outDat);
		}
		if (string.Equals(Path.GetFullPath(outDat), Path.GetFullPath(trDat), StringComparison.OrdinalIgnoreCase))
		{
			progress("Out = TR (zaten hedef) · kopya atlandı");
		}
		else
		{
			File.Copy(trDat, text, overwrite: true);
			if (File.Exists(outDat))
			{
				File.Delete(outDat);
			}
			File.Move(text, outDat);
		}
		int num = 0;
		int num2 = 0;
		try
		{
			using TurbineDat turbineDat = new TurbineDat();
			turbineDat.Open(outDat, writable: false);
			foreach (DatEntry item in turbineDat.ListLocalization())
			{
				num2++;
				if (item.Id == 620757423)
				{
					num = 1;
				}
			}
		}
		catch (Exception ex)
		{
			try
			{
				File.WriteAllText(failLogPath, "verify-list-ex\t" + ex.Message, Encoding.UTF8);
			}
			catch
			{
			}
		}
		try
		{
			File.WriteAllText(failLogPath, "", Encoding.UTF8);
		}
		catch
		{
		}
		long length = new FileInfo(outDat).Length;
		Result result = new Result
		{
			Ok = num2,
			Same = 0,
			Fail = 0,
			SkipNoEn = 0,
			TokenTableWritten = num,
			DestPath = outDat,
			FailLogPath = failLogPath,
			Elapsed = sw.Elapsed,
			Mode = "TrFullCopy",
			OutSize = length
		};
		progress($"Transplant bitti · mode=TrFullCopy loc={num2} 250001AF={num} size={length} · {sw.Elapsed}");
		return result;
	}

	private static Result ApplyTurbineSubfiles(string enDat, string trDat, string outDat, string failLogPath, Action<string> progress, Stopwatch sw)
	{
		string text = outDat + ".building";
		if (File.Exists(text))
		{
			File.Delete(text);
		}
		if (File.Exists(outDat))
		{
			File.Delete(outDat);
		}
		progress("EN kopyalanıyor (force-subfile)…");
		File.Copy(enDat, text, overwrite: true);
		int ok = 0;
		int same = 0;
		int fail = 0;
		int skipNoEn = 0;
		int num = 0;
		List<string> list = new List<string>();
		string statusPath = Path.Combine(Path.GetDirectoryName(outDat) ?? ".", "EnTrApply_status.txt");
		using (TurbineDat turbineDat = new TurbineDat())
		{
			using TurbineDat turbineDat2 = new TurbineDat();
			turbineDat.Open(trDat, writable: false);
			progress("TR loc listeleniyor…");
			List<DatEntry> list2 = turbineDat.ListLocalization();
			progress($"TR loc: {list2.Count}");
			turbineDat2.Open(text, writable: true);
			progress("EN loc listeleniyor…");
			List<DatEntry> list3 = turbineDat2.ListLocalization();
			Dictionary<int, DatEntry> dictionary = new Dictionary<int, DatEntry>(list3.Count);
			foreach (DatEntry item in list3)
			{
				dictionary[item.Id] = item;
			}
			progress($"EN loc: {dictionary.Count} · yazım…");
			int n = 0;
			int total = list2.Count;
			foreach (DatEntry item2 in list2)
			{
				n++;
				int id = item2.Id;
				byte[] array;
				try
				{
					array = turbineDat.ReadRaw(item2);
				}
				catch (Exception ex)
				{
					fail++;
					list.Add($"0x{id:X8}\ttr-read-ex\t{ex.Message}");
					if (n % 2000 == 0 || n == total)
					{
						Report();
					}
					continue;
				}
				if (array == null || array.Length == 0)
				{
					same++;
					if (n % 2000 == 0 || n == total)
					{
						Report();
					}
					continue;
				}
				if (!dictionary.TryGetValue(id, out var value) || value == null || value.Offset == 0)
				{
					skipNoEn++;
					if (skipNoEn <= 50)
					{
						list.Add($"0x{id:X8}\tno-en-entry\tsize={array.Length}");
					}
					if (n % 2000 == 0 || n == total)
					{
						Report();
					}
					continue;
				}
				byte[] array2;
				try
				{
					array2 = turbineDat2.ReadRaw(value);
				}
				catch (Exception ex2)
				{
					fail++;
					list.Add($"0x{id:X8}\ten-read-ex\t{ex2.Message}");
					if (n % 2000 == 0 || n == total)
					{
						Report();
					}
					continue;
				}
				if (array2 != null && array2.Length == array.Length && BytesEqual(array2, array))
				{
					same++;
					if (n % 2000 == 0 || n == total)
					{
						Report();
					}
					continue;
				}
				try
				{
					uint offset = value.Offset;
					if (array.Length > (int)value.Size && !turbineDat2.ExpandChain(offset, array.Length))
					{
						fail++;
						list.Add($"0x{id:X8}\texpand-fail\tneed={array.Length}\tenSize={value.Size}");
						if (n % 2000 == 0 || n == total)
						{
							Report();
						}
						continue;
					}
					turbineDat2.WriteChain(offset, array);
					if (value.Size != (uint)array.Length)
					{
						turbineDat2.UpdateEntrySize(id, (uint)array.Length);
						value.Size = (uint)array.Length;
					}
					ok++;
					if (id == 620757423)
					{
						num = 1;
					}
				}
				catch (Exception ex3)
				{
					fail++;
					list.Add($"0x{id:X8}\twrite-ex\t{ex3.Message}");
				}
				if (n % 2000 == 0 || n == total)
				{
					Report();
				}
			}
			progress("Flush…");
			turbineDat2.Close();
			void Report()
			{
				string text2 = $"Transplant {n}/{total} · OK={ok} same={same} fail={fail} skipNoEn={skipNoEn}";
				progress(text2);
				try
				{
					File.WriteAllText(statusPath, text2 + "\r\n" + DateTime.Now.ToString("o") + "\r\n", Encoding.UTF8);
				}
				catch
				{
				}
				Console.Write("\r" + text2 + "   ");
			}
		}
		if (File.Exists(outDat))
		{
			File.Delete(outDat);
		}
		File.Move(text, outDat);
		try
		{
			File.WriteAllLines(failLogPath, list, Encoding.UTF8);
		}
		catch
		{
		}
		long length = new FileInfo(outDat).Length;
		Result result = new Result
		{
			Ok = ok,
			Same = same,
			Fail = fail,
			SkipNoEn = skipNoEn,
			TokenTableWritten = num,
			DestPath = outDat,
			FailLogPath = failLogPath,
			Elapsed = sw.Elapsed,
			Mode = "TurbineSubfile",
			OutSize = length
		};
		progress($"Transplant bitti · mode=TurbineSubfile OK={ok} same={same} fail={fail} skipNoEn={skipNoEn} 250001AF={num} · {sw.Elapsed}");
		Console.WriteLine();
		return result;
	}

	private static bool BytesEqual(byte[] a, byte[] b)
	{
		if (a == null || b == null || a.Length != b.Length)
		{
			return false;
		}
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i])
			{
				return false;
			}
		}
		return true;
	}
}
