using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LotroTrGemini;

public sealed class LocBin
{
	public int Reserved0;

	public int FileId;

	public int Scheme;

	public byte[] Trailing = new byte[0];

	public bool UsedFlatFallback;

	public byte[] RawFlat;

	public List<FlatEntry> FlatEntries;

	private readonly List<LocRecord> _recs = new List<LocRecord>();

	public static LocBin Parse(byte[] data, int expectedDid)
	{
		if (TryParseStructured(data, out var bin) && bin._recs.Count > 0)
		{
			return bin;
		}
		int fallbackOffset = data != null && data.Length >= 12
			&& BitConverter.ToInt32(data, 0) == 0
			&& BitConverter.ToInt32(data, 4) == expectedDid ? 4 : 0;
		return ParseAnchored(data, expectedDid, fallbackOffset);
	}

	private static bool TryParseStructured(byte[] data, out LocBin bin)
	{
		bin = new LocBin();
		try
		{
			using MemoryStream memoryStream = new MemoryStream(data);
			using BinaryReader binaryReader = new BinaryReader(memoryStream);
			if (data.Length < 13)
			{
				return false;
			}
			bin.Reserved0 = binaryReader.ReadInt32();
			bin.FileId = binaryReader.ReadInt32();
			bin.Scheme = binaryReader.ReadInt32();
			int num = ReadVarLen(binaryReader);
			if (num < 1 || num > 500000)
			{
				return false;
			}
			for (int i = 0; i < num; i++)
			{
				if (!TryReadRecord(binaryReader, memoryStream, out var rec))
				{
					return false;
				}
				bin._recs.Add(rec);
			}
			while (memoryStream.Position + 16 < memoryStream.Length)
			{
				long position = memoryStream.Position;
				if (!TryReadRecord(binaryReader, memoryStream, out var rec2))
				{
					memoryStream.Position = position;
					break;
				}
				bin._recs.Add(rec2);
			}
			int num2 = (int)(memoryStream.Length - memoryStream.Position);
			if (num2 > 0)
			{
				bin.Trailing = binaryReader.ReadBytes(num2);
			}
			int num3 = 0;
			foreach (LocRecord rec3 in bin._recs)
			{
				num3 += rec3.Main.Count;
			}
			return num3 > 0;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryReadRecord(BinaryReader br, MemoryStream ms, out LocRecord rec)
	{
		rec = null;
		long position = ms.Position;
		try
		{
			rec = new LocRecord
			{
				Hash = br.ReadInt64()
			};
			int num = br.ReadInt32();
			if (num < 0 || num > 256)
			{
				ms.Position = position;
				return false;
			}
			for (int i = 0; i < num; i++)
			{
				if (!TryReadVarString(br, ms, out var s))
				{
					ms.Position = position;
					return false;
				}
				rec.Main.Add(s);
			}
			int num2 = br.ReadInt32();
			if (num2 < 0 || num2 > 4096)
			{
				ms.Position = position;
				return false;
			}
			for (int j = 0; j < num2; j++)
			{
				rec.Unk.Add(br.ReadInt32());
			}
			byte b = br.ReadByte();
			if (b > 64)
			{
				ms.Position = position;
				return false;
			}
			for (int k = 0; k < b; k++)
			{
				int num3 = br.ReadInt32();
				if (num3 < 0 || num3 > 256)
				{
					ms.Position = position;
					return false;
				}
				List<string> list = new List<string>(num3);
				for (int l = 0; l < num3; l++)
				{
					if (!TryReadVarString(br, ms, out var s2))
					{
						ms.Position = position;
						return false;
					}
					list.Add(s2);
				}
				rec.Subs.Add(list);
			}
			return true;
		}
		catch
		{
			ms.Position = position;
			return false;
		}
	}

	private static LocBin ParseAnchored(byte[] data, int expectedDid, int fallbackOffset)
	{
		LocBin locBin = new LocBin
		{
			Reserved0 = ((data.Length >= 4) ? BitConverter.ToInt32(data, 0) : 0),
			FileId = ((data.Length >= 8) ? BitConverter.ToInt32(data, 4) : expectedDid),
			Scheme = ((data.Length < 12) ? 1 : BitConverter.ToInt32(data, 8)),
			UsedFlatFallback = true,
			RawFlat = data,
			FlatEntries = new List<FlatEntry>()
		};
		if (data == null || data.Length < 16)
		{
			return locBin;
		}
		byte[] scanData = data;
		if (fallbackOffset == 4)
		{
			// datexport.dll exposes fallback payloads without the leading reserved
			// word and pads the tail to the same size.  Mirror that view only while
			// scanning, then map string offsets back to the untouched DAT payload.
			scanData = new byte[data.Length];
			Buffer.BlockCopy(data, 4, scanData, 0, data.Length - 4);
			// Catalog identities were originally generated from datexport.dll's
			// shifted view. Keep those stable while RawFlat and entry offsets remain
			// mapped to the real managed payload for byte-exact rebuilding.
			locBin.Reserved0 = BitConverter.ToInt32(scanData, 0);
			locBin.FileId = BitConverter.ToInt32(scanData, 4);
			locBin.Scheme = BitConverter.ToInt32(scanData, 8);
		}
		bool[] array = new bool[scanData.Length];
		for (int i = 12; i + 12 < scanData.Length; i++)
		{
			if (array[i])
			{
				continue;
			}
			int num = BitConverter.ToInt32(scanData, i);
			if (num < 1 || num > 16 || i < 8)
			{
				continue;
			}
			long hash = BitConverter.ToInt64(scanData, i - 8);
			int num2 = i + 4;
			List<FlatEntry> list = new List<FlatEntry>(num);
			for (int j = 0; j < num; j++)
			{
				if (num2 >= scanData.Length)
				{
					break;
				}
				if (array[num2])
				{
					break;
				}
				int num3 = 1;
				int num4 = scanData[num2] & 0xFF;
				if ((num4 & 0x80) != 0)
				{
					if (num2 + 1 >= scanData.Length)
					{
						break;
					}
					num4 = ((num4 & 0x7F) << 8) | scanData[num2 + 1];
					num3 = 2;
				}
				if (num4 < 1 || num4 > 4000)
				{
					break;
				}
				int num5 = num2 + num3;
				if (num5 + num4 * 2 > scanData.Length)
				{
					break;
				}
				bool flag = false;
				for (int k = num2; k < num5 + num4 * 2; k++)
				{
					if (array[k])
					{
						flag = true;
						break;
					}
				}
				if (flag || !TryDecodeVarStringBody(scanData, num5, num4, out var text) || !IsPlausibleLocString(text))
				{
					break;
				}
				list.Add(new FlatEntry
				{
					Offset = num2,
					HeaderSize = num3,
					CharLen = num4,
					Text = text,
					Hash = hash,
					IsSafe = true
				});
				num2 = num5 + num4 * 2;
			}
			if (list.Count == 0 || (num == 1 && list.Count != 1))
			{
				continue;
			}
			foreach (FlatEntry item in list)
			{
				for (int l = item.Offset; l < item.Offset + item.HeaderSize + item.CharLen * 2 && l < array.Length; l++)
				{
					array[l] = true;
				}
				locBin.FlatEntries.Add(item);
			}
		}
		if (locBin.FlatEntries.Count > 0)
		{
			List<FlatEntry> list2 = new List<FlatEntry>(locBin.FlatEntries);
			if (fallbackOffset != 0)
			{
				foreach (FlatEntry item in list2) item.Offset += fallbackOffset;
			}
			list2.Sort((FlatEntry a, FlatEntry b) => a.Offset.CompareTo(b.Offset));
			locBin.FlatEntries = list2;
			LocRecord locRecord = null;
			long num6 = long.MinValue;
			int num7 = -1;
			foreach (FlatEntry item2 in list2)
			{
				if (locRecord == null || item2.Hash != num6 || item2.Offset > num7 + 1)
				{
					locRecord = new LocRecord
					{
						Hash = item2.Hash
					};
					locBin._recs.Add(locRecord);
					num6 = item2.Hash;
				}
				locRecord.Main.Add(item2.Text);
				num7 = item2.Offset + item2.HeaderSize + item2.CharLen * 2;
			}
		}
		return locBin;
	}

	private static bool TryDecodeVarStringBody(byte[] data, int payload, int len, out string text)
	{
		text = null;
		for (int i = 0; i < len; i++)
		{
			char c = (char)(data[payload + i * 2] | (data[payload + i * 2 + 1] << 8));
			if (c == '\0' || c == '\ufffe' || c == '\uffff')
			{
				return false;
			}
		}
		text = Encoding.Unicode.GetString(data, payload, len * 2);
		return true;
	}

	public static bool IsPlausibleLocString(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		if (text.Length > 4000)
		{
			return false;
		}
		int num = 0;
		foreach (char c in text)
		{
			if (c < ' ' && c != '\n' && c != '\r' && c != '\t')
			{
				return false;
			}
			if (c == '\ufffe' || c == '\uffff')
			{
				return false;
			}
			if (char.IsLetterOrDigit(c))
			{
				num++;
			}
			else if (char.IsWhiteSpace(c))
			{
				num++;
			}
			else if (".,!?'#%\"-:;()[]{}/\\+_@*&$€£<>|=~^".IndexOf(c) >= 0)
			{
				num++;
			}
		}
		if (text.Length == 1)
		{
			if (num == 1)
			{
				if (!char.IsLetterOrDigit(text[0]))
				{
					return "<-*/+|".IndexOf(text[0]) >= 0;
				}
				return true;
			}
			return false;
		}
		return num >= Math.Max(1, text.Length * 2 / 3);
	}

	public List<LocRow> GetRows(int did)
	{
		List<LocRow> list = new List<LocRow>();
		for (int i = 0; i < _recs.Count; i++)
		{
			LocRecord locRecord = _recs[i];
			for (int j = 0; j < locRecord.Main.Count; j++)
			{
				list.Add(new LocRow
				{
					Did = did,
					RecordIndex = i,
					GroupIndex = -1,
					IndexInGroup = j,
					Original = locRecord.Main[j],
					Translation = locRecord.Main[j]
				});
			}
			for (int k = 0; k < locRecord.Subs.Count; k++)
			{
				for (int l = 0; l < locRecord.Subs[k].Count; l++)
				{
					list.Add(new LocRow
					{
						Did = did,
						RecordIndex = i,
						GroupIndex = k,
						IndexInGroup = l,
						Original = locRecord.Subs[k][l],
						Translation = locRecord.Subs[k][l]
					});
				}
			}
		}
		return list;
	}

	/// <summary>
	/// Builds cross-version catalog identities without modifying the parsed payload.
	/// The caller owns the global position counter; position is only an auxiliary signal.
	/// </summary>
	public List<CatalogRecord> GetCatalogRecords(int did, ref long position)
	{
		List<LocRow> rows = GetRows(did);
		List<CatalogRecord> result = new List<CatalogRecord>(rows.Count);
		Dictionary<int, string> recordFingerprints = new Dictionary<int, string>();
		for (int i = 0; i < _recs.Count; i++)
		{
			LocRecord record = _recs[i];
			StringBuilder shape = new StringBuilder();
			shape.Append("main=").Append(record.Main.Count).Append(";subs=");
			for (int j = 0; j < record.Subs.Count; j++) shape.Append(record.Subs[j].Count).Append(',');
			shape.Append(";unk=");
			for (int j = 0; j < record.Unk.Count; j++) shape.Append(record.Unk[j]).Append(',');
			recordFingerprints[i] = CatalogIdentity.Sha256Hex("record|" + record.Hash.ToString("X16") + "|" + shape);
		}
		for (int i = 0; i < rows.Count; i++)
		{
			LocRow row = rows[i];
			LocRecord record = (row.RecordIndex >= 0 && row.RecordIndex < _recs.Count) ? _recs[row.RecordIndex] : null;
			int groupCount = row.GroupIndex < 0
				? (record == null ? 0 : record.Main.Count)
				: (record == null || row.GroupIndex >= record.Subs.Count ? 0 : record.Subs[row.GroupIndex].Count);
			string structural = CatalogIdentity.Sha256Hex(
				"structural|fallback=" + UsedFlatFallback + "|file=" + FileId + "|scheme=" + Scheme
				+ "|main=" + (record == null ? 0 : record.Main.Count)
				+ "|sub-count=" + (record == null ? 0 : record.Subs.Count)
				+ "|group=" + row.GroupIndex + "|group-count=" + groupCount);
			string previous = i == 0 ? "<BOF>" : rows[i - 1].Original;
			string next = i + 1 >= rows.Count ? "<EOF>" : rows[i + 1].Original;
			string previousRecord = i == 0 || !recordFingerprints.ContainsKey(rows[i - 1].RecordIndex) ? "" : recordFingerprints[rows[i - 1].RecordIndex];
			string nextRecord = i + 1 >= rows.Count || !recordFingerprints.ContainsKey(rows[i + 1].RecordIndex) ? "" : recordFingerprints[rows[i + 1].RecordIndex];
			string context = CatalogIdentity.BuildContextFingerprint(previous, next, previousRecord, nextRecord);
			string recordFingerprint = recordFingerprints.ContainsKey(row.RecordIndex)
				? recordFingerprints[row.RecordIndex]
				: CatalogIdentity.Sha256Hex("record|missing|" + row.RecordIndex);
			result.Add(CatalogIdentity.FromLocRow(row, recordFingerprint, structural, context, position));
			position++;
		}
		return result;
	}

	public byte[] Rebuild(IList<LocRow> rowsForThisDid)
	{
		if (UsedFlatFallback)
		{
			return RebuildFlat(rowsForThisDid);
		}
		foreach (LocRow item in rowsForThisDid)
		{
			string value = item.Translation ?? item.Original ?? "";
			if (item.RecordIndex < 0 || item.RecordIndex >= _recs.Count)
			{
				continue;
			}
			LocRecord locRecord = _recs[item.RecordIndex];
			if (item.GroupIndex < 0)
			{
				if (item.IndexInGroup >= 0 && item.IndexInGroup < locRecord.Main.Count)
				{
					locRecord.Main[item.IndexInGroup] = value;
				}
			}
			else if (item.GroupIndex < locRecord.Subs.Count && item.IndexInGroup >= 0 && item.IndexInGroup < locRecord.Subs[item.GroupIndex].Count)
			{
				locRecord.Subs[item.GroupIndex][item.IndexInGroup] = value;
			}
		}
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write(Reserved0);
		binaryWriter.Write(FileId);
		binaryWriter.Write(Scheme);
		WriteVarLen(binaryWriter, _recs.Count);
		foreach (LocRecord rec in _recs)
		{
			binaryWriter.Write(rec.Hash);
			binaryWriter.Write(rec.Main.Count);
			foreach (string item2 in rec.Main)
			{
				WriteVarString(binaryWriter, item2);
			}
			binaryWriter.Write(rec.Unk.Count);
			foreach (int item3 in rec.Unk)
			{
				binaryWriter.Write(item3);
			}
			binaryWriter.Write((byte)rec.Subs.Count);
			foreach (List<string> sub in rec.Subs)
			{
				binaryWriter.Write(sub.Count);
				foreach (string item4 in sub)
				{
					WriteVarString(binaryWriter, item4);
				}
			}
		}
		if (Trailing != null && Trailing.Length != 0)
		{
			binaryWriter.Write(Trailing);
		}
		return memoryStream.ToArray();
	}

	private byte[] RebuildFlat(IList<LocRow> rowsForThisDid)
	{
		if (RawFlat == null)
		{
			return new byte[0];
		}
		if (FlatEntries == null || FlatEntries.Count == 0)
		{
			return (byte[])RawFlat.Clone();
		}
		if (rowsForThisDid != null)
		{
			foreach (LocRow item in rowsForThisDid)
			{
				string value = item.Translation ?? item.Original ?? "";
				if (item.RecordIndex >= 0 && item.RecordIndex < _recs.Count)
				{
					LocRecord locRecord = _recs[item.RecordIndex];
					if (item.GroupIndex < 0 && item.IndexInGroup >= 0 && item.IndexInGroup < locRecord.Main.Count)
					{
						locRecord.Main[item.IndexInGroup] = value;
					}
				}
			}
		}
		List<string> list = new List<string>(FlatEntries.Count);
		foreach (LocRecord rec in _recs)
		{
			foreach (string item2 in rec.Main)
			{
				list.Add(item2);
			}
		}
		while (list.Count < FlatEntries.Count)
		{
			list.Add(FlatEntries[list.Count].Text);
		}
		if (list.Count > FlatEntries.Count)
		{
			list.RemoveRange(FlatEntries.Count, list.Count - FlatEntries.Count);
		}
		using MemoryStream memoryStream = new MemoryStream();
		int num = 0;
		for (int i = 0; i < FlatEntries.Count; i++)
		{
			FlatEntry flatEntry = FlatEntries[i];
			if (flatEntry.Offset > num)
			{
				memoryStream.Write(RawFlat, num, flatEntry.Offset - num);
			}
			string text = list[i] ?? "";
			if (string.Equals(text, flatEntry.Text, StringComparison.Ordinal))
			{
				memoryStream.Write(RawFlat, flatEntry.Offset, flatEntry.HeaderSize + flatEntry.CharLen * 2);
			}
			else
			{
				WriteVarStringToStream(memoryStream, text);
			}
			num = flatEntry.Offset + flatEntry.HeaderSize + flatEntry.CharLen * 2;
		}
		if (num < RawFlat.Length)
		{
			memoryStream.Write(RawFlat, num, RawFlat.Length - num);
		}
		return memoryStream.ToArray();
	}

	private static void WriteVarStringToStream(MemoryStream ms, string s)
	{
		if (s == null)
		{
			s = "";
		}
		if (s.Length > 32767)
		{
			s = s.Substring(0, 32767);
		}
		if (s.Length < 128)
		{
			ms.WriteByte((byte)s.Length);
		}
		else
		{
			ms.WriteByte((byte)(0x80 | (s.Length >> 8)));
			ms.WriteByte((byte)(s.Length & 0xFF));
		}
		byte[] bytes = Encoding.Unicode.GetBytes(s);
		ms.Write(bytes, 0, bytes.Length);
	}

	private static bool TryReadVarString(BinaryReader br, MemoryStream ms, out string s)
	{
		s = null;
		long position = ms.Position;
		try
		{
			int num = ReadVarLen(br);
			if (num < 0 || num > 8000 || ms.Position + num * 2 > ms.Length)
			{
				return false;
			}
			byte[] array = br.ReadBytes(num * 2);
			if (array.Length != num * 2)
			{
				return false;
			}
			for (int i = 0; i < num; i++)
			{
				if ((ushort)(array[i * 2] | (array[i * 2 + 1] << 8)) == 0)
				{
					return false;
				}
			}
			s = Encoding.Unicode.GetString(array);
			return true;
		}
		catch
		{
			ms.Position = position;
			return false;
		}
	}

	private static int ReadVarLen(BinaryReader br)
	{
		int num = br.ReadByte();
		if ((num & 0x80) != 0)
		{
			return ((num & 0x7F) << 8) | br.ReadByte();
		}
		return num;
	}

	private static void WriteVarLen(BinaryWriter bw, int len)
	{
		if (len < 0 || len > 32767)
		{
			throw new IOException("varlen");
		}
		if (len < 128)
		{
			bw.Write((byte)len);
			return;
		}
		bw.Write((byte)(0x80 | (len >> 8)));
		bw.Write((byte)(len & 0xFF));
	}

	private static void WriteVarString(BinaryWriter bw, string s)
	{
		if (s == null)
		{
			s = "";
		}
		if (s.Length > 32767)
		{
			throw new IOException("string too long");
		}
		WriteVarLen(bw, s.Length);
		bw.Write(Encoding.Unicode.GetBytes(s));
	}
}
