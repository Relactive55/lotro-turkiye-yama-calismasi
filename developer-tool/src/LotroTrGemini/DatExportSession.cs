using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LotroTrGemini;

public sealed class DatExportSession : IDisposable
{
	public const int DefaultHandle = 0;

	private bool _open;

	private bool _writable;

	private int _handle = DefaultHandle;

	public int BlockSize { get; private set; }
	public int VnumDatFile { get; private set; }
	public int VnumGameData { get; private set; }
	public uint DatFileId { get; private set; }
	public string DatIdStamp { get; private set; }
	public string FirstIterationGuid { get; private set; }

	public DatExportSession(int handle = DefaultHandle)
	{
		_handle = handle;
	}

	public bool IsOpen => _open;

	public static bool IsProcessX86 => IntPtr.Size == 4;

	public int NumSubfiles
	{
		get
		{
			EnsureOpen();
			return DatExport.GetNumSubfiles(_handle);
		}
	}

	public static string FindNativeDir()
	{
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		string[] array = new string[5]
		{
			Path.Combine(baseDirectory, "Native"),
			baseDirectory,
			Path.Combine(baseDirectory, "..\\..\\Native"),
			Path.Combine(baseDirectory, "..\\..\\..\\Native"),
			Path.GetFullPath(Path.Combine(baseDirectory, ".."))
		};
		for (int i = 0; i < array.Length; i++)
		{
			try
			{
				string fullPath = Path.GetFullPath(array[i]);
				if (File.Exists(Path.Combine(fullPath, "datexport.dll")))
				{
					return fullPath;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	public static void EnsureDllSearchPath()
	{
		string text = FindNativeDir();
		if (!string.IsNullOrEmpty(text))
		{
			DatExport.SetDllDirectory(text);
		}
	}

	public void Open(string path, bool writable)
	{
		Close();
		BlockSize = 0;
		VnumDatFile = 0;
		VnumGameData = 0;
		DatFileId = 0;
		DatIdStamp = null;
		FirstIterationGuid = null;
		if (!IsProcessX86)
		{
			throw new InvalidOperationException("datexport.dll 32-bit. Freedom uygulaması x86 olarak çalışmalı.");
		}
		if (string.IsNullOrEmpty(path) || !File.Exists(path))
		{
			throw new FileNotFoundException("DAT yok", path);
		}
		EnsureDllSearchPath();
		uint flags = (writable ? DatExport.FlagsReadWrite : DatExport.FlagsReadOnly);
		byte[] datIdStamp = new byte[64];
		byte[] firstIterGuid = new byte[64];
		int didMasterMap;
		int blockSize;
		int vnumDatFile;
		int vnumGameData;
		uint datFileId;
		int num = DatExport.OpenDatFileEx2(_handle, path, flags, out didMasterMap, out blockSize, out vnumDatFile, out vnumGameData, out datFileId, datIdStamp, firstIterGuid);
		if (num != _handle)
		{
			throw new InvalidOperationException("OpenDatFileEx2 rc=" + num);
		}
		BlockSize = blockSize;
		VnumDatFile = vnumDatFile;
		VnumGameData = vnumGameData;
		DatFileId = datFileId;
		DatIdStamp = DecodeNativeText(datIdStamp);
		FirstIterationGuid = DecodeNativeText(firstIterGuid);
		_open = true;
		_writable = writable;
	}

	public Dictionary<int, int[]> LoadSizeMap()
	{
		EnsureOpen();
		int numSubfiles = NumSubfiles;
		Dictionary<int, int[]> dictionary = new Dictionary<int, int[]>(numSubfiles);
		for (int i = 0; i < numSubfiles; i += 500)
		{
			int num = Math.Min(500, numSubfiles - i);
			int[] array = new int[num];
			int[] array2 = new int[num];
			int[] array3 = new int[num];
			DatExport.GetSubfileSizes(_handle, array, array2, array3, i, num);
			for (int j = 0; j < num; j++)
			{
				dictionary[array[j]] = new int[2]
				{
					array2[j],
					array3[j]
				};
			}
		}
		return dictionary;
	}

	public byte[] ReadSubfile(int fileId, int size, out int version)
	{
		EnsureOpen();
		if (size <= 0)
		{
			version = DatExport.GetSubfileVersion(_handle, fileId);
			return new byte[0];
		}
		IntPtr intPtr = Marshal.AllocHGlobal(size);
		try
		{
			DatExport.GetSubfileData(_handle, fileId, intPtr, 0, out version);
			byte[] array = new byte[size];
			Marshal.Copy(intPtr, array, 0, size);
			return array;
		}
		finally
		{
			Marshal.FreeHGlobal(intPtr);
		}
	}

	public byte GetCompressionFlag(int fileId)
	{
		EnsureOpen();
		return DatExport.GetSubfileCompressionFlag(_handle, fileId);
	}

	public int GetSubfileVersion(int fileId)
	{
		EnsureOpen();
		return DatExport.GetSubfileVersion(_handle, fileId);
	}

	public int WriteSubfile(int fileId, byte[] data, int version, int iteration)
	{
		EnsureOpen();
		if (!_writable)
		{
			throw new InvalidOperationException("read-only");
		}
		if (data == null)
		{
			data = new byte[0];
		}
		IntPtr intPtr = Marshal.AllocHGlobal(Math.Max(1, data.Length));
		try
		{
			if (data.Length != 0)
			{
				Marshal.Copy(data, 0, intPtr, data.Length);
			}
			int purgeResult = DatExport.PurgeSubfileData(_handle, fileId);
			if (purgeResult < 0)
			{
				return -1;
			}
			int putResult = DatExport.PutSubfileData(_handle, fileId, intPtr, 0, data.Length, version, iteration, 0);
			return putResult == 0 ? -1 : 0;
		}
		finally
		{
			Marshal.FreeHGlobal(intPtr);
		}
	}

	public void Flush()
	{
		if (_open)
		{
			DatExport.Flush(_handle);
		}
	}

	public void Close()
	{
		if (_open)
		{
			try
			{
				DatExport.CloseDatFile(_handle);
			}
			catch
			{
			}
			_open = false;
		}
	}

	public void Dispose()
	{
		Close();
	}

	private void EnsureOpen()
	{
		if (!_open)
		{
			throw new InvalidOperationException("DAT açık değil");
		}
	}

	private static string DecodeNativeText(byte[] bytes)
	{
		if (bytes == null || bytes.Length == 0) return null;
		int length = Array.IndexOf(bytes, (byte)0);
		if (length < 0) length = bytes.Length;
		return Encoding.ASCII.GetString(bytes, 0, length).Trim();
	}
}
