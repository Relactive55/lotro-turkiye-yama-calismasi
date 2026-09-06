using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace LotroTrGemini;

public sealed class TmStore
{
	private readonly string _path;

	private readonly Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly object _lock = new object();

	private readonly object _saveLock = new object();

	private int _dirty;

	public int Count
	{
		get
		{
			lock (_lock)
			{
				return _map.Count;
			}
		}
	}

	public string PathFile => _path;

	public TmStore(string path)
	{
		_path = path;
		LoadFile(path);
	}

	public void MergeFrom(string otherPath)
	{
		if (!string.IsNullOrEmpty(otherPath) && File.Exists(otherPath) && !string.Equals(otherPath, _path, StringComparison.OrdinalIgnoreCase))
		{
			LoadFile(otherPath, mergeOnly: true);
		}
	}

	private void LoadFile(string path, bool mergeOnly = false)
	{
		if (!File.Exists(path))
		{
			return;
		}
		foreach (string item in File.ReadLines(path, Encoding.UTF8))
		{
			if (string.IsNullOrWhiteSpace(item) || item[0] == '#')
			{
				continue;
			}
			int num = item.IndexOf('\t');
			if (num <= 0)
			{
				continue;
			}
			string text = Unescape(item.Substring(0, num));
			string text2 = Unescape(item.Substring(num + 1));
			// Values are fully validated when they are actually used. Keeping the
			// startup filter structural avoids repeating expensive token/regex
			// checks for hundreds of thousands of unused memory entries.
			if (text.Length == 0 || text2.Length == 0 || string.Equals(text, text2, StringComparison.Ordinal))
			{
				continue;
			}
			lock (_lock)
			{
				if (!mergeOnly || !_map.ContainsKey(text))
				{
					_map[text] = text2;
				}
			}
		}
	}

	public string Lookup(string en)
	{
		if (string.IsNullOrEmpty(en))
		{
			return null;
		}
		string exact = TurTextFix.ExactForEnglish(en);
		if (!string.IsNullOrEmpty(exact))
		{
			return exact;
		}
		string value;
		lock (_lock)
		{
			if (!_map.TryGetValue(en, out value))
			{
				return null;
			}
		}
		if (IsUnsafeSourceMemoryKey(en) || TextGuard.ShouldKeepAsIs(en) || !TranslationQuality.LooksEnglishText(en))
		{
			return null;
		}
		if (TextGuard.IsCorruptTranslation(en, value) || TranslationQuality.LooksCorruptPair(en, value))
		{
			return null;
		}
		return value;
	}

	public void Upsert(string en, string tr)
	{
		if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(tr) || IsUnsafeSourceMemoryKey(en) || TextGuard.ShouldKeepAsIs(en) || TextGuard.IsCorruptTranslation(en, tr) || TranslationQuality.LooksCorruptPair(en, tr) || string.Equals(en, tr, StringComparison.Ordinal))
		{
			return;
		}
		lock (_lock)
		{
			if (!_map.TryGetValue(en, out var value) || !string.Equals(value, tr, StringComparison.Ordinal))
			{
				_map[en] = tr;
				Interlocked.Exchange(ref _dirty, 1);
			}
		}
	}

	public int CaptureFromRowsAndSave(IList<LocRow> rows)
	{
		if (rows == null)
		{
			return 0;
		}
		int num = 0;
		for (int i = 0; i < rows.Count; i++)
		{
			LocRow locRow = rows[i];
			string text = locRow.Original ?? "";
			string text2 = locRow.Translation ?? "";
			if (LocWriteGuard.IsCriticalUiDid(locRow.Did) && !LocWriteGuard.IsSafeCriticalTranslation(text, text2))
			{
				continue;
			}
			if (text.Length != 0 && text2.Length != 0 && !string.Equals(text, text2, StringComparison.Ordinal))
			{
				string value;
				lock (_lock)
				{
					_map.TryGetValue(text, out value);
				}
				if (string.Equals(value, text2, StringComparison.Ordinal))
				{
					continue;
				}
				if (IsUnsafeSourceMemoryKey(text) || TextGuard.ShouldKeepAsIs(text) || TextGuard.IsCorruptTranslation(text, text2) || TranslationQuality.LooksCorruptPair(text, text2))
				{
					continue;
				}
				Upsert(text, text2);
				if (!string.Equals(value, text2, StringComparison.Ordinal))
				{
					num++;
				}
			}
		}
		if (num > 0 || !File.Exists(_path))
		{
			Save();
		}
		return num;
	}

	public int ApplyToRows(IList<LocRow> rows)
	{
		if (rows == null || rows.Count == 0)
		{
			return 0;
		}
		int num = 0;
		for (int i = 0; i < rows.Count; i++)
		{
			LocRow locRow = rows[i];
			string text = locRow.Original ?? "";
			if (text.Length == 0 || !string.Equals(locRow.Translation ?? "", text, StringComparison.Ordinal))
			{
				continue;
			}
			string text2 = Lookup(text);
			if (!string.IsNullOrEmpty(text2))
			{
				if (LocWriteGuard.IsCriticalUiDid(locRow.Did))
				{
					text2 = LocWriteGuard.AlignCriticalWhitespace(text, text2);
					if (!LocWriteGuard.IsSafeCriticalTranslation(text, text2))
					{
						continue;
					}
				}
				text2 = TextGuard.Sanitize(text, text2, allowCompact: false);
				if (!string.IsNullOrEmpty(text2) && !string.Equals(text2, text, StringComparison.Ordinal))
				{
					locRow.Translation = text2;
					num++;
				}
			}
		}
		return num;
	}

	private static bool IsUnsafeSourceMemoryKey(string source)
	{
		if (string.IsNullOrWhiteSpace(source))
		{
			return true;
		}
		string text = source.Trim();
		// Source-text-only memory is ambiguous for small labels ("Ok", "bonus",
		// "say"...) and must never be used for grammar/sentinel records. Exact
		// curated fixes are resolved before this check in Lookup().
		if (text.Length < 8 || text.IndexOf("{{", StringComparison.Ordinal) >= 0 || text.IndexOf("}}", StringComparison.Ordinal) >= 0 || (text[0] == '#' && text.IndexOf('{') >= 0) || text.IndexOf("[!", StringComparison.Ordinal) >= 0)
		{
			return true;
		}
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
			{
				return true;
			}
			// CJK/Hangul characters in the English localization source are binary
			// grammar data decoded as text, not a sentence to translate.
			if (c >= '\u2E80' && c <= '\uD7AF')
			{
				return true;
			}
		}
		return false;
	}

	public void ClearAll()
	{
		try
		{
			lock (_lock)
			{
				_map.Clear();
				Interlocked.Exchange(ref _dirty, 0);
			}
			if (File.Exists(_path))
			{
				try
				{
					File.Delete(_path);
				}
				catch
				{
				}
			}
			string path = _path + ".tmp";
			if (File.Exists(path))
			{
				try
				{
					File.Delete(path);
					return;
				}
				catch
				{
					return;
				}
			}
		}
		catch
		{
		}
	}

	public void ForceSave()
	{
		Interlocked.Exchange(ref _dirty, 1);
		Save();
	}

	public void Save()
	{
		lock (_saveLock)
		{
			SaveCore();
		}
	}

	private void SaveCore()
	{
		Dictionary<string, string> dictionary;
		lock (_lock)
		{
			if (_dirty == 0 && File.Exists(_path))
			{
				return;
			}
			dictionary = new Dictionary<string, string>(_map);
			Interlocked.Exchange(ref _dirty, 0);
		}
		string directoryName = Path.GetDirectoryName(_path);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text = _path + ".tmp";
		using (StreamWriter streamWriter = new StreamWriter(text, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
		{
			streamWriter.WriteLine("# EN\\tTR  (\\n ve \\t kaçışlı) — yeni DAT yüklemede aynı İngilizceye uygulanır");
			foreach (KeyValuePair<string, string> item in dictionary)
			{
				streamWriter.WriteLine(Escape(item.Key) + "\t" + Escape(item.Value));
			}
		}
		if (File.Exists(_path))
		{
			try
			{
				File.Replace(text, _path, null);
				return;
			}
			catch
			{
				File.Copy(text, _path, overwrite: true);
				try
				{
					File.Delete(text);
					return;
				}
				catch
				{
					return;
				}
			}
		}
		File.Move(text, _path);
	}

	private static string Escape(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "";
		}
		return s.Replace("\\", "\\\\").Replace("\r\n", "\\n").Replace("\n", "\\n")
			.Replace("\r", "\\n")
			.Replace("\t", "\\t");
	}

	private static string Unescape(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder(s.Length);
		for (int i = 0; i < s.Length; i++)
		{
			if (s[i] == '\\' && i + 1 < s.Length)
			{
				switch (s[i + 1])
				{
				case 'n':
					stringBuilder.Append('\n');
					i++;
					continue;
				case 't':
					stringBuilder.Append('\t');
					i++;
					continue;
				case '\\':
					stringBuilder.Append('\\');
					i++;
					continue;
				}
			}
			stringBuilder.Append(s[i]);
		}
		return stringBuilder.ToString();
	}
}
