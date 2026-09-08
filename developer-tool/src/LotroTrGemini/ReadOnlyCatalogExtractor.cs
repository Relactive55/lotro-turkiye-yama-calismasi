using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace LotroTrGemini;

/// <summary>
/// Read-only native DAT catalog extraction. It never calls WriteSubfile/PurgeSubfileData.
/// </summary>
public sealed class ReadOnlyCatalogExtractor
{
	public const int MaxPayloadBytes = 256 * 1024 * 1024;

	public CatalogSnapshot Extract(string datPath, CancellationToken cancellationToken)
	{
		if (!DatExportSession.IsProcessX86) throw new InvalidOperationException("Read-only catalog extraction requires the x86 native reader.");
		if (string.IsNullOrWhiteSpace(datPath) || !File.Exists(datPath)) throw new FileNotFoundException("DAT not found", datPath);
		FileInfo before = new FileInfo(datPath);
		CatalogSnapshot snapshot = new CatalogSnapshot { DatSize = before.Length };
		long position = 0;
		using (DatExportSession session = new DatExportSession())
		{
			session.Open(datPath, writable: false);
			snapshot.BlockSize = session.BlockSize;
			snapshot.VnumDatFile = session.VnumDatFile;
			snapshot.VnumGameData = session.VnumGameData;
			snapshot.DatFileId = session.DatFileId;
			snapshot.DatIdStamp = session.DatIdStamp;
			snapshot.FirstIterationGuid = session.FirstIterationGuid;
			Dictionary<int, int[]> sizes = session.LoadSizeMap();
			List<int> dids = new List<int>();
			foreach (int did in sizes.Keys) if (unchecked((uint)did) >> 24 == 37u) dids.Add(did);
			dids.Sort((left, right) => unchecked((uint)left).CompareTo(unchecked((uint)right)));
			snapshot.LocalizationDidCount = dids.Count;
			foreach (int did in dids)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					int[] info = sizes[did];
					if (info == null || info.Length < 1 || info[0] <= 0)
					{
						snapshot.EmptyPayloadCount++;
						snapshot.ReviewRequiredDids.Add(did);
						continue;
					}
					if (info[0] > MaxPayloadBytes) throw new InvalidDataException("localization payload exceeds safety bound");
					byte[] payload = session.ReadSubfile(did, info[0], out int unusedVersion);
					if (payload == null || payload.Length == 0)
					{
						snapshot.EmptyPayloadCount++;
						snapshot.ReviewRequiredDids.Add(did);
						continue;
					}
					LocBin bin = LocBin.Parse(payload, did);
					List<LocRow> rows = bin.GetRows(did);
					if (rows.Count == 0)
					{
						snapshot.EmptyPayloadCount++;
						snapshot.ReviewRequiredDids.Add(did);
						continue;
					}
					if (bin.UsedFlatFallback)
					{
						snapshot.FallbackPayloadCount++;
						snapshot.ReviewRequiredDids.Add(did);
					}
					else snapshot.StructuredPayloadCount++;
					snapshot.Records.AddRange(bin.GetCatalogRecords(did, ref position));
				}
				catch (OperationCanceledException) { throw; }
				catch
				{
					snapshot.ParseErrorCount++;
					snapshot.FailedDids.Add(did);
					snapshot.ReviewRequiredDids.Add(did);
				}
			}
		}
		FileInfo after = new FileInfo(datPath);
		if (after.Length != before.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
			throw new IOException("DAT changed during read-only catalog extraction; discard the snapshot and retry.");
		snapshot.DatSha256 = HashFile(datPath);
		snapshot.RecordCount = snapshot.Records.Count;
		snapshot.CatalogSha256 = CatalogIdentity.ComputeCatalogHash(snapshot.Records);
		return snapshot;
	}

	private static string HashFile(string path)
	{
		using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
		using (SHA256 sha = SHA256.Create())
		{
			byte[] hash = sha.ComputeHash(stream);
			StringBuilder text = new StringBuilder(hash.Length * 2);
			foreach (byte value in hash) text.Append(value.ToString("x2"));
			return text.ToString();
		}
	}
}
