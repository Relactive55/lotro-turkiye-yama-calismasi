using System.Collections.Generic;

namespace LotroTrGemini;

/// <summary>
/// Read-only DAT and catalog metadata passed between extraction and downstream
/// diff/bundle generation. The snapshot contains no writable DAT handle.
/// </summary>
public sealed class CatalogSnapshot
{
	public long DatSize { get; internal set; }
	public string DatSha256 { get; internal set; }
	public int BlockSize { get; internal set; }
	public int VnumDatFile { get; internal set; }
	public int VnumGameData { get; internal set; }
	public uint DatFileId { get; internal set; }
	public string DatIdStamp { get; internal set; }
	public string FirstIterationGuid { get; internal set; }
	public int LocalizationDidCount { get; internal set; }
	public int StructuredPayloadCount { get; internal set; }
	public int FallbackPayloadCount { get; internal set; }
	public int EmptyPayloadCount { get; internal set; }
	public int ParseErrorCount { get; internal set; }
	public string CatalogSha256 { get; internal set; }
	public List<int> FailedDids { get; } = new List<int>();
	public List<int> ReviewRequiredDids { get; } = new List<int>();
	public List<CatalogRecord> Records { get; } = new List<CatalogRecord>();
}
