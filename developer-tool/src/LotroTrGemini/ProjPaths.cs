using System;
using System.Collections.Generic;
using System.IO;

namespace LotroTrGemini;

public static class ProjPaths
{
	public const string ProjectFolderName = "LOTR PROJEM";

	public static readonly string[] ImportTxtNames = new string[7] { "CEVIRILMIS_AI_CEVRILECEK.txt", "CEVIRILMIS_DAT_TAMAMI.txt", "LOTRO_PAKET_BIREBIR.txt", "LOTRO_EN_export_DUZELTILMIS.txt", "LOTRO_EN_export_SIKI.txt", "LOTRO_EN_export.txt", "CEVIRILMIS_AI_DONE.txt" };

	public static string TranslationMemoryTxtName => "CEVIRILMIS_DAT_TAMAMI.txt";

	public static string AiPendingTxtName => "CEVIRILMIS_AI_CEVRILECEK.txt";

	public static string Root
	{
		get
		{
			try
			{
				string environmentVariable = Environment.GetEnvironmentVariable("LOTRO_PROJECT_ROOT");
				if (LooksLikeProject(environmentVariable))
				{
					return Path.GetFullPath(environmentVariable);
				}
			}
			catch
			{
			}
			try
			{
				string basePath = AppDomain.CurrentDomain.BaseDirectory;
				for (int level = 0; level < 8; level++)
				{
					if (string.IsNullOrEmpty(basePath))
					{
						break;
					}
					if (LooksLikeProject(basePath))
					{
						return basePath;
					}
					string child = Path.Combine(basePath, "LOTR PROJEM");
					if (LooksLikeProject(child))
					{
						return child;
					}
					basePath = Path.GetDirectoryName(basePath);
				}
			}
			catch
			{
			}
			return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
		}
	}

	public static string DatRoot
	{
		get
		{
			string text = Path.Combine(Root, "DAT DOSYA");
			if (!Directory.Exists(text))
			{
				return Root;
			}
			return text;
		}
	}

	public static string OutDatDir
	{
		get
		{
			string text = Path.Combine(Root, "CIKTI");
			try
			{
				Directory.CreateDirectory(text);
			}
			catch
			{
			}
			return text;
		}
	}

	public static string DefaultExportTxtName => TranslationMemoryTxtName;

	public static string ExportDirPrimary
	{
		get
		{
			try
			{
				string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
				if (!string.IsNullOrEmpty(baseDirectory) && Directory.Exists(baseDirectory))
				{
					return baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				}
			}
			catch
			{
			}
			return Root;
		}
	}

	public static string FindEnDat()
	{
		string[] array = new string[3]
		{
			Path.Combine(Root, "ORJİNAL DAT", "client_local_English.dat"),
			Path.Combine(Root, "ORJINAL DAT", "client_local_English.dat"),
			Path.Combine(Root, "ORIJINAL DAT", "client_local_English.dat")
		};
		foreach (string text in array)
		{
			if (File.Exists(text))
			{
				return text;
			}
		}
		return FindDatByFolderHint(isEn: true);
	}

	public static string FindTrDat()
	{
		string[] array = new string[5]
		{
			Path.Combine(Root, "ÇEVRİLMİŞ DAT", "client_local_English.dat"),
			Path.Combine(Root, "CEVRILMIS DAT", "client_local_English.dat"),
			Path.Combine(Root, "ÇEVRILMIS DAT", "client_local_English.dat"),
			Path.Combine(Root, "LOTRONLİNE RELACTİVE DAT", "client_local_English.dat"),
			Path.Combine(Root, "LOTRONLINE RELACTIVE DAT", "client_local_English.dat")
		};
		foreach (string text in array)
		{
			if (File.Exists(text))
			{
				return text;
			}
		}
		return FindDatByFolderHint(isEn: false);
	}

	public static string FindImportTxt()
	{
		string[] array = CandidateTxtDirs();
		foreach (string path in array)
		{
			string[] importTxtNames = ImportTxtNames;
			foreach (string path2 in importTxtNames)
			{
				string text = Path.Combine(path, path2);
				if (File.Exists(text))
				{
					return text;
				}
			}
		}
		try
		{
			string root = Root;
			if (Directory.Exists(root))
			{
				array = ImportTxtNames;
				foreach (string searchPattern in array)
				{
					string[] files = Directory.GetFiles(root, searchPattern, SearchOption.TopDirectoryOnly);
					if (files.Length != 0)
					{
						return files[0];
					}
				}
			}
		}
		catch
		{
		}
		return Path.Combine(ExportDirPrimary, TranslationMemoryTxtName);
	}

	public static string[] ExportTxtDirs()
	{
		List<string> list = new List<string>();
		Action<string> action = delegate(string d)
		{
			if (!string.IsNullOrEmpty(d))
			{
				try
				{
					d = Path.GetFullPath(d);
				}
				catch
				{
					return;
				}
				for (int i = 0; i < list.Count; i++)
				{
					if (string.Equals(list[i], d, StringComparison.OrdinalIgnoreCase))
					{
						return;
					}
				}
				if (!Directory.Exists(d))
				{
					try
					{
						Directory.CreateDirectory(d);
					}
					catch
					{
						return;
					}
				}
				list.Add(d);
			}
		};
		action(ExportDirPrimary);
		try
		{
			string root = Root;
			if (Directory.Exists(root))
			{
				action(root);
			}
		}
		catch
		{
		}
		return list.ToArray();
	}

	private static string[] CandidateTxtDirs()
	{
		List<string> list = new List<string>();
		Action<string> action = delegate(string d)
		{
			if (!string.IsNullOrEmpty(d) && Directory.Exists(d))
			{
				try
				{
					d = Path.GetFullPath(d);
				}
				catch
				{
					return;
				}
				for (int i = 0; i < list.Count; i++)
				{
					if (string.Equals(list[i], d, StringComparison.OrdinalIgnoreCase))
					{
						return;
					}
				}
				list.Add(d);
			}
		};
		try
		{
			action(AppDomain.CurrentDomain.BaseDirectory);
		}
		catch
		{
		}
		action(Root);
		return list.ToArray();
	}

	private static bool LooksLikeProject(string dir)
	{
		if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
		{
			return false;
		}
		if (Directory.Exists(Path.Combine(dir, "DAT DOSYA")))
		{
			return true;
		}
		if (Directory.Exists(Path.Combine(dir, "ORJİNAL DAT")) || Directory.Exists(Path.Combine(dir, "ORJINAL DAT")) || Directory.Exists(Path.Combine(dir, "ORIJINAL DAT")))
		{
			return true;
		}
		if (Directory.Exists(Path.Combine(dir, "ÇEVRİLMİŞ DAT")) || Directory.Exists(Path.Combine(dir, "CEVRILMIS DAT")) || Directory.Exists(Path.Combine(dir, "LOTRONLİNE RELACTİVE DAT")))
		{
			return true;
		}
		if (File.Exists(Path.Combine(dir, "CEVIRILMIS_DAT_TAMAMI.txt")))
		{
			return true;
		}
		if (File.Exists(Path.Combine(dir, "LOTRO_EN_export_DUZELTILMIS.txt")))
		{
			return true;
		}
		if (Directory.Exists(Path.Combine(dir, "LotroTr_Gemini")) && Directory.Exists(Path.Combine(dir, "LotroDatExport")))
		{
			return true;
		}
		return false;
	}

	private static string FindDatByFolderHint(bool isEn)
	{
		string datRoot = DatRoot;
		if (!Directory.Exists(datRoot))
		{
			return null;
		}
		try
		{
			string[] directories = Directory.GetDirectories(datRoot);
			foreach (string text in directories)
			{
				string text2 = Path.Combine(text, "client_local_English.dat");
				if (!File.Exists(text2))
				{
					continue;
				}
				string text3 = Path.GetFileName(text) ?? "";
				bool flag = text3.IndexOf("TURK", StringComparison.OrdinalIgnoreCase) >= 0 || text3.IndexOf("TÜRK", StringComparison.OrdinalIgnoreCase) >= 0 || text3.IndexOf("EVIR", StringComparison.OrdinalIgnoreCase) >= 0 || text3.IndexOf("ÇEV", StringComparison.OrdinalIgnoreCase) >= 0;
				bool flag2 = text3.IndexOf("ORJ", StringComparison.OrdinalIgnoreCase) >= 0 || text3.IndexOf("ORIG", StringComparison.OrdinalIgnoreCase) >= 0 || text3.IndexOf("ENGL", StringComparison.OrdinalIgnoreCase) >= 0;
				if (isEn)
				{
					if (flag2 && !flag)
					{
						return text2;
					}
				}
				else if (flag)
				{
					return text2;
				}
			}
		}
		catch
		{
		}
		return null;
	}
}
