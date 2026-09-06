using System;
using System.IO;
using System.Text;

namespace LotroTrGemini;

public sealed class AppSettings
{
	private readonly string _path;

	public string LastDatFolder { get; set; }

	public string LastDatPath { get; set; }

	public string LastTxtPath { get; set; }

	public string LastTrDatPath { get; set; }

	public string LastOutFolder { get; set; }

	public int Workers { get; set; }

	public AppSettings(string path)
	{
		_path = path;
		Workers = 2;
		Load();
		ApplyProjectDefaults();
	}

	public void ApplyProjectDefaults()
	{
		_ = ProjPaths.Root;
		string text = ProjPaths.FindEnDat();
		if ((string.IsNullOrEmpty(LastDatPath) || !File.Exists(LastDatPath)) && !string.IsNullOrEmpty(text) && File.Exists(text))
		{
			LastDatPath = text;
		}
		if (string.IsNullOrEmpty(LastDatFolder) || !Directory.Exists(LastDatFolder))
		{
			if (!string.IsNullOrEmpty(LastDatPath))
			{
				LastDatFolder = Path.GetDirectoryName(LastDatPath);
			}
			else if (Directory.Exists(ProjPaths.DatRoot))
			{
				LastDatFolder = ProjPaths.DatRoot;
			}
		}
		if (string.IsNullOrEmpty(LastTxtPath) || !File.Exists(LastTxtPath))
		{
			string text2 = ProjPaths.FindImportTxt();
			if (File.Exists(text2))
			{
				LastTxtPath = text2;
			}
		}
		if (string.IsNullOrEmpty(LastOutFolder) || !Directory.Exists(LastOutFolder))
		{
			LastOutFolder = ProjPaths.OutDatDir;
		}
		if (!string.IsNullOrEmpty(LastOutFolder))
		{
			string text3 = LastOutFolder.ToLowerInvariant();
			if (text3.Contains("\\standingstonegames\\") || text3.Contains("\\program files"))
			{
				LastOutFolder = ProjPaths.OutDatDir;
			}
		}
	}

	public void Load()
	{
		if (!File.Exists(_path))
		{
			return;
		}
		foreach (string item in File.ReadLines(_path, Encoding.UTF8))
		{
			if (string.IsNullOrWhiteSpace(item) || item[0] == '#')
			{
				continue;
			}
			int num = item.IndexOf('=');
			if (num <= 0)
			{
				continue;
			}
			string text = item.Substring(0, num).Trim();
			string text2 = item.Substring(num + 1).Trim();
			switch (text)
			{
			case "last_dat_folder":
				LastDatFolder = text2;
				break;
			case "last_dat_path":
				LastDatPath = text2;
				break;
			case "last_txt_path":
				LastTxtPath = text2;
				break;
			case "last_tr_dat_path":
				LastTrDatPath = text2;
				break;
			case "last_out_folder":
				LastOutFolder = text2;
				break;
			case "workers":
			{
				if (int.TryParse(text2, out var result))
				{
					Workers = Math.Max(1, Math.Min(16, result));
				}
				break;
			}
			}
		}
	}

	public void Save()
	{
		string directoryName = Path.GetDirectoryName(_path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		using StreamWriter streamWriter = new StreamWriter(_path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("# LotroTr settings");
		streamWriter.WriteLine("workers=" + Math.Max(1, Math.Min(16, Workers)));
		if (!string.IsNullOrEmpty(LastDatFolder))
		{
			streamWriter.WriteLine("last_dat_folder=" + LastDatFolder);
		}
		if (!string.IsNullOrEmpty(LastDatPath))
		{
			streamWriter.WriteLine("last_dat_path=" + LastDatPath);
		}
		if (!string.IsNullOrEmpty(LastTxtPath))
		{
			streamWriter.WriteLine("last_txt_path=" + LastTxtPath);
		}
		if (!string.IsNullOrEmpty(LastTrDatPath))
		{
			streamWriter.WriteLine("last_tr_dat_path=" + LastTrDatPath);
		}
		if (!string.IsNullOrEmpty(LastOutFolder))
		{
			streamWriter.WriteLine("last_out_folder=" + LastOutFolder);
		}
	}
}
