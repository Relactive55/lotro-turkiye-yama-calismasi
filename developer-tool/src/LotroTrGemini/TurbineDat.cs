using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LotroTrGemini;

public sealed class TurbineDat : IDisposable
{
	public const uint MagicBt = 21570u;

	public const long SuperOff = 320L;

	private FileStream _fs;

	private BinaryReader _br;

	private bool _writable;

	private Dictionary<int, long> _entryPos;

	private static readonly byte[] ZeroPad = new byte[4096];

	private uint _freeHead;

	private uint _freeCount;

	private bool _freeLoaded;

	private bool _freeDirty;

	public string Path { get; private set; }

	public uint BlockSize { get; private set; }

	public uint DirectoryOffset { get; private set; }

	public void Open(string path, bool writable)
	{
		Close();
		Path = path;
		_writable = writable;
		_entryPos = null;
		_fs = new FileStream(path, FileMode.Open, (!writable) ? FileAccess.Read : FileAccess.ReadWrite, writable ? FileShare.Read : FileShare.ReadWrite, 4194304, writable ? FileOptions.RandomAccess : FileOptions.SequentialScan);
		_br = new BinaryReader(_fs, Encoding.Unicode, leaveOpen: true);
		_fs.Position = 320L;
		if (_br.ReadUInt32() != 21570)
		{
			throw new InvalidDataException("DAT superblock BT değil.");
		}
		BlockSize = _br.ReadUInt32();
		_br.ReadUInt32();
		_br.ReadUInt32();
		_br.ReadUInt32();
		_br.ReadUInt32();
		_br.ReadUInt32();
		_br.ReadUInt32();
		DirectoryOffset = _br.ReadUInt32();
		if (BlockSize < 16 || BlockSize > 65536)
		{
			throw new InvalidDataException("block size: " + BlockSize);
		}
		if (DirectoryOffset == 0 || DirectoryOffset >= _fs.Length)
		{
			throw new InvalidDataException("directory offset geçersiz.");
		}
	}

	public void Dispose()
	{
		Close();
	}

	public void Close()
	{
		if (_br != null)
		{
			try
			{
				_br.Dispose();
			}
			catch
			{
			}
			_br = null;
		}
		if (_fs == null)
		{
			return;
		}
		try
		{
			if (_writable)
			{
				_fs.Flush(flushToDisk: true);
			}
		}
		catch
		{
		}
		try
		{
			_fs.Dispose();
		}
		catch
		{
		}
		_fs = null;
	}

	public List<DatEntry> ListLocalization()
	{
		List<DatEntry> list = new List<DatEntry>(4096);
		Walk(DirectoryOffset, list, 0);
		list.Sort((DatEntry a, DatEntry b) => a.Id.CompareTo(b.Id));
		List<DatEntry> list2 = new List<DatEntry>();
		foreach (DatEntry item in list)
		{
			if ((item.Id & -16777216) == 620756992 && item.Size != 0)
			{
				list2.Add(item);
			}
		}
		return list2;
	}

	private void Walk(uint offset, List<DatEntry> list, int depth)
	{
		if (depth > 40 || offset == 0 || offset >= _fs.Length)
		{
			return;
		}
		_fs.Position = offset + 8;
		List<uint> list2 = new List<uint>();
		for (int i = 0; i < 62; i++)
		{
			uint num = _br.ReadUInt32();
			uint num2 = _br.ReadUInt32();
			if (num == 0 && num2 == 0)
			{
				break;
			}
			if (num2 != 0)
			{
				list2.Add(num2);
			}
		}
		_fs.Position = offset + 504;
		uint num3 = _br.ReadUInt32();
		if (num3 > 500000)
		{
			throw new InvalidDataException("directory count");
		}
		for (uint num4 = 0u; num4 < num3; num4++)
		{
			uint flags = _br.ReadUInt32();
			uint id = _br.ReadUInt32();
			uint num5 = _br.ReadUInt32();
			uint num6 = _br.ReadUInt32();
			uint timestamp = _br.ReadUInt32();
			uint version = _br.ReadUInt32();
			uint size = _br.ReadUInt32();
			uint flags2 = _br.ReadUInt32();
			if (num6 != 0 && num5 != 0)
			{
				list.Add(new DatEntry
				{
					Id = (int)id,
					Offset = num5,
					Size = num6,
					Version = (int)version,
					Timestamp = timestamp,
					Size2 = size,
					Flags = flags,
					Flags2 = flags2
				});
			}
		}
		int num7 = Math.Min(list2.Count, (int)(num3 + 1));
		for (int j = 0; j < num7; j++)
		{
			Walk(list2[j], list, depth + 1);
		}
	}

	public byte[] ReadRaw(DatEntry e)
	{
		return ReadChain(e.Offset, e.Size);
	}

	private uint ReadU32At(long pos)
	{
		_fs.Position = pos;
		int num = _fs.ReadByte();
		int num2 = _fs.ReadByte();
		int num3 = _fs.ReadByte();
		int num4 = _fs.ReadByte();
		if (num4 < 0)
		{
			throw new EndOfStreamException();
		}
		return (uint)(num | (num2 << 8) | (num3 << 16) | (num4 << 24));
	}

	public byte[] ReadChain(uint dataOffset, uint size)
	{
		byte[] array = new byte[size];
		int num = 0;
		long num2 = size;
		long pos = dataOffset;
		int num3 = 0;
		int num4 = (int)(BlockSize - 4);
		while (num2 > 0)
		{
			if (num3++ > 2000000)
			{
				throw new IOException("block chain loop");
			}
			uint num5 = ReadU32At(pos);
			if (num5 == 0)
			{
				int num6 = (int)num2;
				if (num6 > 0 && _fs.Position + num6 > _fs.Length)
				{
					num6 = (int)(_fs.Length - _fs.Position);
				}
				if (num6 > 0 && _fs.Read(array, num, num6) == num6)
				{
					break;
				}
				throw new EndOfStreamException();
			}
			int num7 = (int)Math.Min(num2, num4);
			if (_fs.Read(array, num, num7) != num7)
			{
				throw new EndOfStreamException();
			}
			num += num7;
			num2 -= num7;
			pos = num5;
		}
		return array;
	}

	public static bool LooksCompressed(byte[] raw)
	{
		if (raw == null || raw.Length < 6)
		{
			return false;
		}
		int num = BitConverter.ToInt32(raw, 0);
		if (num > 0 && num < 134217728)
		{
			return raw[4] == 120;
		}
		return false;
	}

	public static byte[] MaybeDecompress(byte[] raw)
	{
		if (!LooksCompressed(raw))
		{
			return raw;
		}
		int capacity = BitConverter.ToInt32(raw, 0);
		try
		{
			using MemoryStream stream = new MemoryStream(raw, 6, raw.Length - 6);
			using DeflateStream deflateStream = new DeflateStream(stream, CompressionMode.Decompress);
			using MemoryStream memoryStream = new MemoryStream(capacity);
			deflateStream.CopyTo(memoryStream);
			return memoryStream.ToArray();
		}
		catch
		{
			return raw;
		}
	}

	public static byte[] PackBlob(byte[] payload, bool compress, int padToSize)
	{
		byte[] array;
		if (!compress)
		{
			array = payload;
		}
		else
		{
			byte[] array2;
			using (MemoryStream memoryStream = new MemoryStream())
			{
				memoryStream.WriteByte(120);
				memoryStream.WriteByte(156);
				using (DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
				{
					deflateStream.Write(payload, 0, payload.Length);
				}
				uint num = Adler32(payload);
				memoryStream.WriteByte((byte)((num >> 24) & 0xFF));
				memoryStream.WriteByte((byte)((num >> 16) & 0xFF));
				memoryStream.WriteByte((byte)((num >> 8) & 0xFF));
				memoryStream.WriteByte((byte)(num & 0xFF));
				array2 = memoryStream.ToArray();
			}
			array = new byte[4 + array2.Length];
			Buffer.BlockCopy(BitConverter.GetBytes(payload.Length), 0, array, 0, 4);
			Buffer.BlockCopy(array2, 0, array, 4, array2.Length);
		}
		if (padToSize > array.Length)
		{
			byte[] array3 = new byte[padToSize];
			Buffer.BlockCopy(array, 0, array3, 0, array.Length);
			return array3;
		}
		return array;
	}

	private static uint Adler32(byte[] data)
	{
		uint num = 1u;
		uint num2 = 0u;
		for (int i = 0; i < data.Length; i++)
		{
			num = (num + data[i]) % 65521;
			num2 = (num2 + num) % 65521;
		}
		return (num2 << 16) | num;
	}

	public int MeasureCapacity(uint dataOffset)
	{
		int num = 0;
		long pos = dataOffset;
		int num2 = 0;
		while (num2++ < 2000000)
		{
			uint num3 = ReadU32At(pos);
			num += (int)(BlockSize - 4);
			if (num3 == 0)
			{
				break;
			}
			pos = num3;
		}
		return num;
	}

	public void WriteChain(uint dataOffset, byte[] blob)
	{
		if (!_writable)
		{
			throw new InvalidOperationException("read-only");
		}
		if (blob == null)
		{
			blob = new byte[0];
		}
		int num = 0;
		long pos = dataOffset;
		int num2 = 0;
		int val = (int)(BlockSize - 4);
		while (num < blob.Length)
		{
			if (num2++ > 2000000)
			{
				throw new IOException("write chain loop");
			}
			uint num3 = ReadU32At(pos);
			if (num3 == 0)
			{
				int num4 = blob.Length - num;
				_fs.Write(blob, num, num4);
				num += num4;
				long num5 = 4 + num4;
				long num6 = BlockSize - num5;
				if (num6 < 0)
				{
					num6 = 0L;
				}
				while (num6 > 0)
				{
					int num7 = (int)Math.Min(num6, ZeroPad.Length);
					_fs.Write(ZeroPad, 0, num7);
					num6 -= num7;
				}
				break;
			}
			int num8 = Math.Min(val, blob.Length - num);
			_fs.Write(blob, num, num8);
			num += num8;
			pos = num3;
		}
	}

	public static byte[] FitBlob(byte[] payload, byte[] originalRaw, bool wasCompressed)
	{
		if (payload == null)
		{
			payload = new byte[0];
		}
		if (originalRaw == null)
		{
			originalRaw = new byte[0];
		}
		int num = originalRaw.Length;
		if (num <= 0)
		{
			return null;
		}
		byte[] array = PackBlob(payload, wasCompressed, 0);
		if (array.Length <= num)
		{
			if (array.Length != num)
			{
				return PadTo(array, num);
			}
			return array;
		}
		byte[] array2 = PackBlob(payload, !wasCompressed, 0);
		if (array2.Length <= num)
		{
			if (array2.Length != num)
			{
				return PadTo(array2, num);
			}
			return array2;
		}
		return null;
	}

	private static byte[] PadTo(byte[] raw, int size)
	{
		if (raw.Length >= size)
		{
			return raw;
		}
		byte[] array = new byte[size];
		Buffer.BlockCopy(raw, 0, array, 0, raw.Length);
		return array;
	}

	public bool ExpandChain(uint dataOffset, int neededSize)
	{
		if (!_writable)
		{
			throw new InvalidOperationException("read-only");
		}
		int num = (int)(BlockSize - 4);
		int num2 = Math.Max(1, (neededSize + num - 1) / num);
		List<uint> list = new List<uint>(num2 + 4);
		long num3 = dataOffset;
		int num4 = 0;
		while (num4++ < 2000000)
		{
			list.Add((uint)num3);
			_fs.Position = num3;
			uint num5 = _br.ReadUInt32();
			if (num5 == 0)
			{
				break;
			}
			num3 = num5;
		}
		if (list.Count >= num2)
		{
			return true;
		}
		int num6 = num2 - list.Count;
		LoadFreeMeta();
		for (int i = 0; i < num6; i++)
		{
			if (!AllocateBlockFast(out var offset))
			{
				return false;
			}
			WriteUInt32(list[list.Count - 1], offset);
			list.Add(offset);
			WriteUInt32(offset, 0u);
			_fs.Position = offset + 4;
			int num7 = num;
			while (num7 > 0)
			{
				int num8 = Math.Min(num7, ZeroPad.Length);
				_fs.Write(ZeroPad, 0, num8);
				num7 -= num8;
			}
		}
		WriteUInt32(list[list.Count - 1], 0u);
		SaveFreeMeta();
		return true;
	}

	private void LoadFreeMeta()
	{
		if (!_freeLoaded)
		{
			_fs.Position = 340L;
			_freeHead = _br.ReadUInt32();
			_fs.Position = 348L;
			_freeCount = _br.ReadUInt32();
			_freeLoaded = true;
			_freeDirty = false;
		}
	}

	private void SaveFreeMeta()
	{
		if (_freeDirty)
		{
			_fs.Position = 340L;
			_fs.Write(BitConverter.GetBytes(_freeHead), 0, 4);
			_fs.Position = 348L;
			_fs.Write(BitConverter.GetBytes(_freeCount), 0, 4);
			_freeDirty = false;
		}
	}

	private void WriteUInt32(long pos, uint value)
	{
		_fs.Position = pos;
		byte[] bytes = BitConverter.GetBytes(value);
		_fs.Write(bytes, 0, 4);
	}

	private bool AllocateBlockFast(out uint offset)
	{
		offset = 0u;
		LoadFreeMeta();
		if (_freeHead != 0 && _freeCount != 0 && _freeHead + BlockSize <= (uint)_fs.Length)
		{
			offset = _freeHead;
			_fs.Position = _freeHead;
			_freeHead = _br.ReadUInt32();
			_freeCount--;
			_freeDirty = true;
			return true;
		}
		long num = _fs.Length;
		if (num % BlockSize != 0L)
		{
			num += BlockSize - num % BlockSize;
		}
		long num2 = num + BlockSize;
		if (num2 > uint.MaxValue)
		{
			return false;
		}
		_fs.SetLength(num2);
		offset = (uint)num;
		WriteUInt32(num, 0u);
		_fs.Position = 328L;
		byte[] bytes = BitConverter.GetBytes((uint)num2);
		_fs.Write(bytes, 0, 4);
		return true;
	}

	public void BuildEntryIndex()
	{
		_entryPos = new Dictionary<int, long>(300000);
		IndexWalk(DirectoryOffset, 0);
	}

	public bool TryGetEntry(int id, out DatEntry entry)
	{
		entry = null;
		if (_entryPos == null)
		{
			BuildEntryIndex();
		}
		if (!_entryPos.TryGetValue(id, out var value))
		{
			return false;
		}
		_fs.Position = value;
		uint flags = _br.ReadUInt32();
		uint id2 = _br.ReadUInt32();
		uint offset = _br.ReadUInt32();
		uint size = _br.ReadUInt32();
		uint timestamp = _br.ReadUInt32();
		uint version = _br.ReadUInt32();
		uint size2 = _br.ReadUInt32();
		uint flags2 = _br.ReadUInt32();
		entry = new DatEntry
		{
			Id = (int)id2,
			Offset = offset,
			Size = size,
			Version = (int)version,
			Timestamp = timestamp,
			Size2 = size2,
			Flags = flags,
			Flags2 = flags2
		};
		return true;
	}

	private void IndexWalk(uint offset, int depth)
	{
		if (depth > 40 || offset == 0 || offset >= _fs.Length)
		{
			return;
		}
		_fs.Position = offset + 8;
		List<uint> list = new List<uint>();
		for (int i = 0; i < 62; i++)
		{
			uint num = _br.ReadUInt32();
			uint num2 = _br.ReadUInt32();
			if (num == 0 && num2 == 0)
			{
				break;
			}
			if (num2 != 0)
			{
				list.Add(num2);
			}
		}
		_fs.Position = offset + 504;
		uint num3 = _br.ReadUInt32();
		long position = _fs.Position;
		for (uint num4 = 0u; num4 < num3; num4++)
		{
			long num5 = position + num4 * 32;
			_fs.Position = num5 + 4;
			int key = (int)_br.ReadUInt32();
			_entryPos[key] = num5;
		}
		int num6 = Math.Min(list.Count, (int)(num3 + 1));
		for (int j = 0; j < num6; j++)
		{
			IndexWalk(list[j], depth + 1);
		}
	}

	public bool UpdateEntrySize(int id, uint newSize)
	{
		if (!_writable)
		{
			throw new InvalidOperationException("read-only");
		}
		if (_entryPos != null)
		{
			if (_entryPos.TryGetValue(id, out var value))
			{
				WriteUInt32(value + 12, newSize);
				WriteUInt32(value + 24, newSize);
				return true;
			}
			return false;
		}
		return UpdateSizeRec(DirectoryOffset, (uint)id, newSize, 0);
	}

	private bool UpdateSizeRec(uint offset, uint id, uint newSize, int depth)
	{
		if (depth > 40 || offset == 0)
		{
			return false;
		}
		_fs.Position = offset + 8;
		List<uint> list = new List<uint>();
		for (int i = 0; i < 62; i++)
		{
			uint num = _br.ReadUInt32();
			uint num2 = _br.ReadUInt32();
			if (num == 0 && num2 == 0)
			{
				break;
			}
			if (num2 != 0)
			{
				list.Add(num2);
			}
		}
		_fs.Position = offset + 504;
		uint num3 = _br.ReadUInt32();
		long position = _fs.Position;
		for (uint num4 = 0u; num4 < num3; num4++)
		{
			long num5 = position + num4 * 32;
			_fs.Position = num5 + 4;
			if (_br.ReadUInt32() == id)
			{
				WriteUInt32(num5 + 12, newSize);
				WriteUInt32(num5 + 24, newSize);
				return true;
			}
		}
		int num6 = Math.Min(list.Count, (int)(num3 + 1));
		for (int j = 0; j < num6; j++)
		{
			if (UpdateSizeRec(list[j], id, newSize, depth + 1))
			{
				return true;
			}
		}
		return false;
	}
}
