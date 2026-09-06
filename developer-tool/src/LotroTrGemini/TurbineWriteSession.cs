using System;
using System.Collections.Generic;

namespace LotroTrGemini;

public sealed class TurbineWriteSession : IDisposable
{
	private TurbineDat _dat;
	private Dictionary<int, DatEntry> _entries;

	public void Open(string path, bool writable)
	{
		Close();
		if (!writable)
		{
			throw new InvalidOperationException("Yazılabilir oturum gerekli.");
		}
		_dat = new TurbineDat();
		_dat.Open(path, writable: true);
		_dat.BuildEntryIndex();
		_entries = new Dictionary<int, DatEntry>();
		foreach (DatEntry entry in _dat.ListLocalization())
		{
			_entries[entry.Id] = entry;
		}
	}

	public Dictionary<int, int[]> LoadSizeMap()
	{
		EnsureOpen();
		Dictionary<int, int[]> result = new Dictionary<int, int[]>(_entries.Count);
		foreach (KeyValuePair<int, DatEntry> pair in _entries)
		{
			result[pair.Key] = new int[2] { checked((int)pair.Value.Size), 0 };
		}
		return result;
	}

	public byte[] ReadSubfile(int fileId, int size, out int version)
	{
		EnsureOpen();
		if (!_entries.TryGetValue(fileId, out DatEntry entry))
		{
			throw new KeyNotFoundException("DAT alt dosyası yok: 0x" + fileId.ToString("X8"));
		}
		version = entry.Version;
		return _dat.ReadRaw(entry);
	}

	public int WriteSubfile(int fileId, byte[] data, int version, int iteration)
	{
		EnsureOpen();
		if (data == null)
		{
			data = new byte[0];
		}
		if (!_entries.TryGetValue(fileId, out DatEntry entry) || data.LongLength > uint.MaxValue)
		{
			return -1;
		}
		if (data.LongLength > entry.Size && !_dat.ExpandChain(entry.Offset, data.Length))
		{
			return -1;
		}
		_dat.WriteChain(entry.Offset, data);
		if (entry.Size != (uint)data.Length)
		{
			if (!_dat.UpdateEntrySize(fileId, (uint)data.Length))
			{
				return -1;
			}
			entry.Size = (uint)data.Length;
			entry.Size2 = (uint)data.Length;
		}
		return 0;
	}

	public void Flush()
	{
		// TurbineDat.Close diske zorunlu flush uygular.
	}

	public void Close()
	{
		if (_dat != null)
		{
			_dat.Close();
			_dat = null;
		}
		_entries = null;
	}

	public void Dispose()
	{
		Close();
	}

	private void EnsureOpen()
	{
		if (_dat == null || _entries == null)
		{
			throw new InvalidOperationException("DAT oturumu açık değil.");
		}
	}
}
