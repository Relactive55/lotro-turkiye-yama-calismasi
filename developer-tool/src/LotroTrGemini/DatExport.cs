using System;
using System.Runtime.InteropServices;

namespace LotroTrGemini;

internal static class DatExport
{
	public const uint DcofExpandable = 2u;

	public const uint DcofReadOnly = 4u;

	public const uint DcofLoadIterations = 128u;

	public const uint FlagsReadWrite = 130u;

	public const uint FlagsReadOnly = 6u;

	private const string Dll = "datexport.dll";

	[DllImport("datexport.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void CloseDatFile(int handle);

	[DllImport("datexport.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void Flush(int handle);

	[DllImport("datexport.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern int GetNumSubfiles(int handle);

	[DllImport("datexport.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern byte GetSubfileCompressionFlag(int handle, int id);

	[DllImport("datexport.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void GetSubfileData(int handle, int did, IntPtr buffer, int writeOffset, out int version);

	[DllImport("datexport.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern void GetSubfileSizes(int handle, [Out] int[] dids, [Out] int[] sizes, [Out] int[] iterations, int offset, int count);

	[DllImport("datexport.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern int GetSubfileVersion(int handle, int did);

	[DllImport("datexport.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	public static extern int OpenDatFileEx2(int handle, string fileName, uint flags, out int didMasterMap, out int blockSize, out int vnumDatFile, out int vnumGameData, out uint datFileId, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 64)] byte[] datIdStamp, [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 64)] byte[] firstIterGuid);

	[DllImport("datexport.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern int PurgeSubfileData(int handle, int fileId);

	[DllImport("datexport.dll", CallingConvention = CallingConvention.Cdecl)]
	public static extern int PutSubfileData(int handle, int fileId, IntPtr buffer, int writeOffset, int size, int version, int iteration, byte unknownZero);

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	public static extern bool SetDllDirectory(string lpPathName);
}
