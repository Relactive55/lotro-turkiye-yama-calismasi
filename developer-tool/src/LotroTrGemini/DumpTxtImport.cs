using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LotroTrGemini;

public static class DumpTxtImport
{
	public struct ExportPairResult
	{
		public int TotalRows;

		public int Translated;

		public int PendingForAi;

		public int SkippedKeepAsIs;

		public string FullPath;

		public string AiPath;
	}

	private static readonly Regex HeadRe = new Regex("^\\d+\\)\\s*-+\\s*$", RegexOptions.Compiled);

	private static readonly Regex KeyRe = new Regex("^[0-9A-Fa-f]+:-?\\d+:-?\\d+:-?\\d+$", RegexOptions.Compiled);

	public static Dictionary<string, string> LoadIdToTr(string path, Action<string> progress)
	{
		Dictionary<string, string> enToTr;
		return LoadMaps(path, progress, out enToTr);
	}

	public static Dictionary<string, string> LoadEnToTr(string path, Action<string> progress)
	{
		LoadMaps(path, progress, out var enToTr);
		return enToTr;
	}

	public static Dictionary<string, string> LoadMaps(string path, Action<string> progress, out Dictionary<string, string> enToTr)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
		enToTr = new Dictionary<string, string>(StringComparer.Ordinal);
		if (string.IsNullOrEmpty(path) || !File.Exists(path))
		{
			throw new FileNotFoundException("TXT bulunamadı", path);
		}
		long num = 0L;
		long withTur = 0L;
		string curId = null;
		string curIng = null;
		string curTur = null;
		int mode = 0;
		using (StreamReader streamReader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
		{
			string text;
			while ((text = streamReader.ReadLine()) != null)
			{
				num++;
				if (num % 200000 == 0L)
				{
					progress?.Invoke($"TXT okunuyor… satır {num:N0} · kayıt {dictionary.Count:N0}");
				}
				if (string.IsNullOrWhiteSpace(text))
				{
					Commit(dictionary, enToTr, ref curId, ref curIng, ref curTur, ref withTur, ref mode);
				}
				else
				{
					if (text.StartsWith("STATUS:", StringComparison.Ordinal) || text.StartsWith("STATUS :", StringComparison.Ordinal))
					{
						continue;
					}
					if (text.StartsWith("====") || text.StartsWith("TOPLAM") || text.StartsWith("CEVRILMIS") || text.StartsWith("AYNI") || text.StartsWith("SADECE") || text.StartsWith("  CEVRIL") || text.StartsWith("Her madde") || text.StartsWith("  Format") || text.StartsWith("  EN:") || text.StartsWith("  TR:") || text.StartsWith("  Tarih") || text.StartsWith("ID\t") || text.StartsWith("---\t") || text.StartsWith("#") || text.StartsWith("ROLE:") || text.StartsWith("TASK:") || text.StartsWith("RULES:") || text.StartsWith("OUTPUT:") || text.StartsWith("AFTER:") || text.StartsWith("BLOCKS ") || text.StartsWith("NOTE:") || text.StartsWith("SYSTEM:") || text.StartsWith("PROMPT:") || text.StartsWith("IMPORTANT:") || text.StartsWith("CONTEXT:") || text.StartsWith("DO NOT") || text.StartsWith("KEEP:") || text.StartsWith("FORMAT:") || text.StartsWith("FILE:") || text.StartsWith("GAME:") || text.StartsWith("LANG:") || text.StartsWith("COUNT:") || text.StartsWith("PART ") || text.StartsWith("-----") || text.StartsWith("==== ") || text.StartsWith("===") || text.StartsWith("  DAT") || text.StartsWith("  Kullan") || text.StartsWith("  AI") || text.StartsWith("AI_") || text.StartsWith("LOTRO ") || text.StartsWith("INSTRUCTION") || text.StartsWith("You are ") || text.StartsWith("Translate ") || text.StartsWith("Return ") || text.StartsWith("Save as") || text.StartsWith("Import ") || text.StartsWith("Placeholders") || text.StartsWith("When TUR") || text.StartsWith("Do not ") || text.StartsWith("Turkish:"))
					{
						Commit(dictionary, enToTr, ref curId, ref curIng, ref curTur, ref withTur, ref mode);
						continue;
					}
					if (HeadRe.IsMatch(text))
					{
						Commit(dictionary, enToTr, ref curId, ref curIng, ref curTur, ref withTur, ref mode);
						mode = 1;
						continue;
					}
					if (text.StartsWith("ID : ", StringComparison.Ordinal) || text.StartsWith("ID:", StringComparison.Ordinal))
					{
						Commit(dictionary, enToTr, ref curId, ref curIng, ref curTur, ref withTur, ref mode);
						curId = ExtractAfter(text, "ID");
						mode = 1;
						continue;
					}
					if (text.StartsWith("ING:", StringComparison.Ordinal) || text.StartsWith("ING :", StringComparison.Ordinal))
					{
						curIng = ExtractAfter(text, "ING");
						mode = 1;
						continue;
					}
					if (text.StartsWith("TUR:", StringComparison.Ordinal) || text.StartsWith("TUR :", StringComparison.Ordinal))
					{
						curTur = ExtractAfter(text, "TUR");
						mode = 1;
						continue;
					}
					int num2 = text.IndexOf('\t');
					if (num2 <= 0)
					{
						continue;
					}
					Commit(dictionary, enToTr, ref curId, ref curIng, ref curTur, ref withTur, ref mode);
					string text2 = text.Substring(0, num2).Trim();
					if (!KeyRe.IsMatch(text2))
					{
						continue;
					}
					string text3 = text.Substring(num2 + 1);
					int num3 = text3.IndexOf('\t');
					string text4 = "";
					string s;
					if (num3 >= 0)
					{
						text4 = Unescape(text3.Substring(0, num3));
						s = text3.Substring(num3 + 1);
					}
					else
					{
						s = text3;
					}
					s = Unescape(s);
					if (!string.IsNullOrEmpty(s))
					{
						dictionary[text2] = s;
						withTur++;
						if (!string.IsNullOrEmpty(text4) && !string.Equals(text4, s, StringComparison.Ordinal) && !enToTr.ContainsKey(text4))
						{
							enToTr[text4] = s;
						}
					}
				}
			}
			Commit(dictionary, enToTr, ref curId, ref curIng, ref curTur, ref withTur, ref mode);
		}
		progress?.Invoke($"TXT bitti · {dictionary.Count:N0} ID→TR · {enToTr.Count:N0} EN→TR");
		return dictionary;
	}

	private static void Commit(Dictionary<string, string> map, Dictionary<string, string> enToTr, ref string curId, ref string curIng, ref string curTur, ref long withTur, ref int mode)
	{
		if (mode == 0 && curId == null)
		{
			return;
		}
		if (!string.IsNullOrEmpty(curId) && KeyRe.IsMatch(curId) && !string.IsNullOrEmpty(curTur))
		{
			string text = Unescape(curTur);
			string text2 = Unescape(curIng ?? "");
			if (text.Length > 0)
			{
				map[curId] = text;
				withTur++;
				if (text2.Length > 0 && !string.Equals(text2, text, StringComparison.Ordinal) && enToTr != null && !enToTr.ContainsKey(text2))
				{
					enToTr[text2] = text;
				}
			}
		}
		curId = null;
		curIng = null;
		curTur = null;
		mode = 0;
	}

	private static string ExtractAfter(string line, string tag)
	{
		int num = line.IndexOf(':');
		if (num < 0)
		{
			return "";
		}
		return line.Substring(num + 1).TrimStart();
	}

	private static string Unescape(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "";
		}
		return s.Replace("\\n", "\n").Replace("\\r", "\r");
	}

	private static string Escape(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "";
		}
		return s.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
	}

	public static int Export(string pathFull, IList<LocRow> rows, string sourceDat, Action<string> progress)
	{
		return ExportPair(pathFull, null, rows, sourceDat, progress).TotalRows;
	}

	public static ExportPairResult ExportPair(string pathFull, string pathAi, IList<LocRow> rows, string sourceDat, Action<string> progress)
	{
		if (rows == null)
		{
			throw new ArgumentNullException("rows");
		}
		string text = null;
		if (!string.IsNullOrEmpty(pathFull))
		{
			text = Path.GetDirectoryName(pathFull);
		}
		else if (!string.IsNullOrEmpty(pathAi))
		{
			text = Path.GetDirectoryName(pathAi);
		}
		if (!string.IsNullOrEmpty(text))
		{
			Directory.CreateDirectory(text);
		}
		if (string.IsNullOrEmpty(pathAi) && !string.IsNullOrEmpty(pathFull))
		{
			pathAi = Path.Combine(Path.GetDirectoryName(pathFull) ?? "", "CEVIRILMIS_AI_CEVRILECEK.txt");
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		string text2 = pathFull + ".tmp";
		using (StreamWriter streamWriter = new StreamWriter(text2, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
		{
			WriteFullHeader(streamWriter, sourceDat, rows.Count);
			for (int i = 0; i < rows.Count; i++)
			{
				if (i % 50000 == 0)
				{
					progress?.Invoke($"TXT tam dump… {i:N0}/{rows.Count:N0}");
				}
				LocRow locRow = rows[i];
				if (locRow == null)
				{
					continue;
				}
				if (string.IsNullOrEmpty(locRow.Key) || !KeyRe.IsMatch(locRow.Key))
				{
					num6++;
					continue;
				}
				string text3 = locRow.Original ?? "";
				string text4 = locRow.Translation ?? "";
				if (text4.Length == 0)
				{
					text4 = text3;
				}
				num++;
				if (string.Equals(text3, text4, StringComparison.Ordinal))
				{
					num3++;
				}
				else
				{
					num2++;
				}
				WriteBlock(streamWriter, num, locRow.Key, text3, text4);
			}
			streamWriter.WriteLine("========================================");
			streamWriter.WriteLine("TOPLAM SATIR  : " + num);
			streamWriter.WriteLine("CEVRILMIS     : " + num2 + "  (ING != TUR)");
			streamWriter.WriteLine("AYNI          : " + num3 + "  (henüz çevrilmemiş / aynı metin)");
			streamWriter.WriteLine("GRID SATIR    : " + rows.Count);
			if (num6 > 0)
			{
				streamWriter.WriteLine("ATLANAN KEY   : " + num6);
			}
			streamWriter.WriteLine("AI DOSYASI    : CEVIRILMIS_AI_CEVRILECEK.txt  (kalan satırlar)");
			streamWriter.WriteLine("========================================");
		}
		if (File.Exists(pathFull))
		{
			File.Delete(pathFull);
		}
		File.Move(text2, pathFull);
		if (!string.IsNullOrEmpty(pathAi))
		{
			List<LocRow> list = new List<LocRow>();
			for (int j = 0; j < rows.Count; j++)
			{
				LocRow locRow2 = rows[j];
				if (locRow2 == null || string.IsNullOrEmpty(locRow2.Key) || !KeyRe.IsMatch(locRow2.Key))
				{
					continue;
				}
				string text5 = locRow2.Original ?? "";
				string text6 = locRow2.Translation ?? "";
				if (text6.Length == 0)
				{
					text6 = text5;
				}
				if (string.Equals(text5, text6, StringComparison.Ordinal))
				{
					if (TextGuard.ShouldKeepAsIs(text5))
					{
						num5++;
					}
					else if (string.IsNullOrWhiteSpace(text5) || text5.Length < 2)
					{
						num5++;
					}
					else
					{
						list.Add(locRow2);
					}
				}
			}
			num4 = list.Count;
			string text7 = pathAi + ".tmp";
			using (StreamWriter streamWriter2 = new StreamWriter(text7, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
			{
				WriteAiPromptHeader(streamWriter2, sourceDat, num4, num2, num);
				for (int k = 0; k < list.Count; k++)
				{
					if (k % 20000 == 0)
					{
						progress?.Invoke($"AI TXT… {k:N0}/{list.Count:N0}");
					}
					LocRow locRow3 = list[k];
					string text8 = locRow3.Original ?? "";
					WriteBlock(streamWriter2, k + 1, locRow3.Key, text8, text8);
				}
				streamWriter2.WriteLine("========================================");
				streamWriter2.WriteLine("COUNT: NEED_TR blocks = " + num4);
				streamWriter2.WriteLine("END OF AI JOB FILE — return the full file with Turkish in TUR lines");
				streamWriter2.WriteLine("========================================");
			}
			if (File.Exists(pathAi))
			{
				File.Delete(pathAi);
			}
			File.Move(text7, pathAi);
		}
		progress?.Invoke($"TXT · tam {num:N0} · AI kalan {num4:N0} → {pathFull}");
		return new ExportPairResult
		{
			TotalRows = num,
			Translated = num2,
			PendingForAi = num4,
			SkippedKeepAsIs = num5,
			FullPath = pathFull,
			AiPath = pathAi
		};
	}

	private static void WriteFullHeader(StreamWriter sw, string sourceDat, int gridCount)
	{
		sw.WriteLine("==== CEVIRILMIS_DAT_TAMAMI — full memory dump (all DAT rows) ====");
		sw.WriteLine("GAME: Lord of the Rings Online (LOTRO) client_local_English.dat localization");
		sw.WriteLine("LANG: EN (ING) → TR (TUR)");
		sw.WriteLine("FILE: Complete bilingual dump for re-import (Relactive / LOTRÇEVİRİ TXT uygula)");
		sw.WriteLine("  Tarih : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		if (!string.IsNullOrEmpty(sourceDat))
		{
			sw.WriteLine("  DAT   : " + sourceDat);
		}
		sw.WriteLine("COUNT: expected ≈ " + gridCount + " string rows from original DAT");
		sw.WriteLine("FORMAT: numbered blocks with ID / ING / TUR  (NO STATUS — TXT uygula uyumlu)");
		sw.WriteLine("NOTE: ING != TUR = çevirili. ING boş + TUR dolu = TR-DAT paketi (ID ile EN DAT'a uygulanır).");
		sw.WriteLine("NOTE: AI kalanlar → CEVIRILMIS_AI_CEVRILECEK.txt · güvenli ID haritası → CEVIRILMIS_ID_TR.tsv");
		sw.WriteLine("NOTE: LOTRÇEVİRİ: orijinal EN DAT yükle → «TXT uygula» → «DAT yaz»");
		sw.WriteLine();
	}

	private static void WriteAiPromptHeader(StreamWriter sw, string sourceDat, int needCount, int alreadyDone, int totalRows)
	{
		sw.WriteLine("================================================================================");
		sw.WriteLine("LOTRO LOCALIZATION JOB — ENGLISH → TURKISH");
		sw.WriteLine("================================================================================");
		sw.WriteLine("ROLE: You are a professional Turkish localizer for Lord of the Rings Online (LOTRO).");
		sw.WriteLine("GAME: MMORPG UI, quests, combat, items, dialog. Tone: epic fantasy, natural Turkish.");
		sw.WriteLine("LANG: Source ING = English. Target TUR = Turkish (Türkiye, Latin script, modern UI style).");
		if (!string.IsNullOrEmpty(sourceDat))
		{
			sw.WriteLine("SOURCE_DAT: " + sourceDat);
		}
		sw.WriteLine("  Tarih : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		sw.WriteLine("COUNT: blocks_below_need_translation = " + needCount);
		sw.WriteLine("COUNT: already_translated_elsewhere = " + alreadyDone + "  total_dat_rows ≈ " + totalRows);
		sw.WriteLine();
		sw.WriteLine("TASK:");
		sw.WriteLine("Translate every block from English to Turkish. Put the Turkish ONLY on the TUR line.");
		sw.WriteLine("Return the COMPLETE file with the same structure and the same number of blocks.");
		sw.WriteLine();
		sw.WriteLine("RULES:");
		sw.WriteLine("1) NEVER change the ID line. Copy it exactly.");
		sw.WriteLine("2) NEVER change the ING line (keep original English for matching).");
		sw.WriteLine("3) ONLY change the TUR line to proper Turkish.");
		sw.WriteLine("4) Do NOT add STATUS lines. Blocks are only: N) / ID / ING / TUR.");
		sw.WriteLine("5) Preserve ALL placeholders and markup exactly as in ING:");
		sw.WriteLine("   {0} {1} {2} %s %d %1$s \\n \\r <rgb=...> </rgb> [e] [#] and similar tags.");
		sw.WriteLine("6) Preserve numbers, item rarities, shortcuts, and keybind tokens.");
		sw.WriteLine("7) Keep proper names usually untranslated when standard (Frodo, Mordor, Gandalf, Rivendell)");
		sw.WriteLine("   unless a well-known Turkish form exists; then be consistent across the file.");
		sw.WriteLine("8) Keep UI/game command tokens /slash commands and untranslatable codes as-is.");
		sw.WriteLine("9) Do not invent new blocks. Do not delete blocks. Do not merge blocks.");
		sw.WriteLine("10) One block = one string. TUR may be longer/shorter than ING; that's fine.");
		sw.WriteLine("11) If ING is already a symbol/code with no words, leave TUR identical to ING.");
		sw.WriteLine("12) Output encoding: UTF-8 plain text.");
		sw.WriteLine();
		sw.WriteLine("OUTPUT FORMAT (keep every block exactly like this):");
		sw.WriteLine("N) --------------------------------");
		sw.WriteLine("ID : <id>");
		sw.WriteLine("ING: <english_unchanged>");
		sw.WriteLine("TUR: <turkish_translation>");
		sw.WriteLine();
		sw.WriteLine("AFTER:");
		sw.WriteLine("Save as UTF-8 .txt → LOTRÇEVİRİ → load DAT → «TXT uygula» → this file → «DAT yaz».");
		sw.WriteLine("================================================================================");
		sw.WriteLine("BEGIN BLOCKS (translate TUR of each)");
		sw.WriteLine("================================================================================");
		sw.WriteLine();
	}

	private static void WriteBlock(StreamWriter sw, int n, string id, string en, string tr)
	{
		sw.Write(n);
		sw.WriteLine(") --------------------------------");
		sw.Write("ID : ");
		sw.WriteLine(id);
		sw.Write("ING: ");
		sw.WriteLine(Escape(en));
		sw.Write("TUR: ");
		sw.WriteLine(Escape(tr));
		sw.WriteLine();
	}

	public static int ExportIdTrTsv(string path, IList<LocRow> rows, Action<string> progress)
	{
		if (string.IsNullOrEmpty(path) || rows == null)
		{
			return 0;
		}
		string directoryName = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text = path + ".tmp";
		int num = 0;
		using (StreamWriter streamWriter = new StreamWriter(text, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
		{
			streamWriter.WriteLine("ID\tTUR");
			for (int i = 0; i < rows.Count; i++)
			{
				LocRow locRow = rows[i];
				if (locRow == null || string.IsNullOrEmpty(locRow.Key) || !KeyRe.IsMatch(locRow.Key))
				{
					continue;
				}
				string text2 = locRow.Translation ?? "";
				if (text2.Length == 0)
				{
					text2 = locRow.Original ?? "";
				}
				if (text2.Length != 0)
				{
					streamWriter.Write(locRow.Key);
					streamWriter.Write('\t');
					streamWriter.WriteLine(Escape(text2));
					num++;
					if (i % 100000 == 0)
					{
						progress?.Invoke($"ID↔TR TSV… {i:N0}/{rows.Count:N0}");
					}
				}
			}
		}
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		File.Move(text, path);
		progress?.Invoke($"ID→TR TSV {num:N0} → {path}");
		return num;
	}
}
