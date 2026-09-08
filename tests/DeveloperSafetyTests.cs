using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LotroTrGemini;

internal static class DeveloperSafetyTests
{
	public static int Run()
	{
		int passed = 0;
		Check(ProtectedFormat.HasSameProtectedTokens(
			"Use %1$s <b>{0:N2}</b> [fn] 12.5\\n",
			"%1$s kullan <b>{0:N2}</b> [fn] 12,5\\n"), "ordered token/tag/numeric validation");
		passed++;
		Check(!ProtectedFormat.HasSameProtectedTokens(
			"Use %s <b>{0}</b> [n]",
			"<b>%s kullan</b> [n] {0}"), "token order is rejected");
		passed++;
		Check(!ProtectedFormat.HasSameProtectedTokens("<b>Text</b>", "<b>Metin</i>"), "tag stack mismatch is rejected");
		passed++;
		Check(!ProtectedFormat.HasSameProtectedTokens("Value 10.5", "Değer 11.5"), "numeric value mismatch is rejected");
		passed++;
		Check(ProtectedFormat.HasSameProtectedTokens(
			"/plugins load <plugin name> - Opens <name>.",
			"/plugins load <plugin name> - <name> öğesini açar."),
			"angle-bracket placeholders stay opaque");
		passed++;
		Check(ProtectedFormat.HasSameProtectedTokens(
			"</rgb> Shadow <rgb=#00FF00>",
			"</rgb> Gölge <rgb=#00FF00>"),
			"split tag boundaries stay protected");
		passed++;
		byte[] structuredFixture = BuildStructuredFixture();
		LocBin fixtureBin = LocBin.Parse(structuredFixture, 0x25000001);
		Check(!fixtureBin.UsedFlatFallback && fixtureBin.GetRows(0x25000001).Count == 2, "structured fixture parsed");
		passed++;
		List<LocRow> fixtureRows = fixtureBin.GetRows(0x25000001);
		fixtureRows[1].Translation = "Kullan %s";
		LocBin rebuiltBin = LocBin.Parse(fixtureBin.Rebuild(fixtureRows), 0x25000001);
		List<LocRow> rebuiltRows = rebuiltBin.GetRows(0x25000001);
		Check(!rebuiltBin.UsedFlatFallback && rebuiltRows.Count == 2 && rebuiltRows[0].Original == "Welcome" && rebuiltRows[1].Original == "Kullan %s", "structured round-trip fixture");
		passed++;
		byte[] boundaryFixture = Convert.FromBase64String("AAAAAN4CACUBAAAAAAJ1SKQNAAAAAAEAAAAORwBuAGEAdwBpAG4AZwAgAFIAYQB0AFsARQBdAAAAAAAALmM9AwAAAAABAAAAAy4ALgAuAAA=");
		LocBin boundaryBin = LocBin.Parse(boundaryFixture, 0x250002DE);
		List<LocRow> boundaryRows = boundaryBin.GetRows(0x250002DE);
		Check(boundaryRows.Count == 2 && boundaryRows[1].Original == "..."
			&& Convert.ToBase64String(boundaryBin.Rebuild(boundaryRows)) == Convert.ToBase64String(boundaryFixture),
			"fallback keeps final boundary string and exact managed layout");
		passed++;
		byte[] hiddenUiFixture = BuildHiddenUiFixture();
		byte[] fixedUiFixture = KnownUiFixes.ApplyTranslatedPayload(unchecked((int)0x250001BDu), hiddenUiFixture);
		Check(!ContainsBytes(fixedUiFixture, Encoding.Unicode.GetBytes("Character Slots Used"))
			&& ContainsBytes(fixedUiFixture, Encoding.Unicode.GetBytes(" / "))
			&& ContainsBytes(fixedUiFixture, Encoding.Unicode.GetBytes(" KARAKTER YUVASI KULLANILIYOR")),
			"hidden character-slot UI variants are translated");
		passed++;
		Check(object.ReferenceEquals(hiddenUiFixture, KnownUiFixes.ApplyTranslatedPayload(unchecked((int)0x250001BEu), hiddenUiFixture)),
			"known UI fix stays scoped to its verified DID");
		passed++;

		List<CatalogRecord> oldRecords = new List<CatalogRecord>
		{
			Make(0x25000010, 0, 0, 0, "Same", "same-context", 0, "record-a", "shape-a"),
			Make(0x25000011, 0, 0, 0, "Move", "move-context", 1, "record-b", "shape-b"),
			Make(0x25000012, 0, 0, 0, "Old text", "modify-context", 2, "record-c", "shape-c"),
			Make(0x25000013, 0, 0, 0, "Removed", "removed-context", 3, "record-d", "shape-d"),
			Make(0x25000014, 0, 0, 0, "Duplicate", "duplicate-a", 4, "record-e", "shape-e"),
			Make(0x25000015, 0, 0, 0, "Duplicate", "duplicate-b", 5, "record-f", "shape-f")
		};
		List<CatalogRecord> newRecords = new List<CatalogRecord>
		{
			Make(0x25000010, 0, 0, 0, "Same", "same-context", 0, "record-a", "shape-a"),
			Make(0x25000011, 0, 0, 0, "Move", "move-context", 20, "record-b", "shape-b"),
			Make(0x25000012, 0, 0, 0, "New text", "modify-context", 2, "record-c", "shape-c"),
			Make(0x25000016, 0, 0, 0, "Brand new", "new-context", 6, "record-g", "shape-g"),
			Make(0x25000017, 0, 0, 0, "Duplicate", "not-present", 7, "record-h", "shape-h")
		};
		IList<CatalogDiffRecord> diff = CatalogDiff.Compare(oldRecords, newRecords);
		CatalogDiffSummary summary = CatalogDiff.Summarize(diff);
		Check(summary.Unchanged == 1, "diff UNCHANGED");
		passed++;
		Check(summary.Moved == 1, "diff MOVED");
		passed++;
		Check(summary.Modified == 1, "diff MODIFIED");
		passed++;
		Check(summary.New == 1, "diff NEW");
		passed++;
		Check(summary.Ambiguous == 1, "diff AMBIGUOUS fail-closed");
		passed++;
		Check(summary.Removed == 1, "diff REMOVED");
		passed++;
		Check(CatalogIdentity.IsCriticalUiDid(unchecked((int)0x250001A0u)), "critical UI range");
		passed++;

		CatalogRecord sourceA = Make(0x25000020, 0, 0, 0, "Defeat the Orc", "quest-context", 10, "record-q", "shape-q");
		CatalogRecord sourceB = Make(0x25000020, 0, 0, 0, "Defeat the Orc Captain", "quest-context", 10, "record-q", "shape-q");
		CatalogRecord sourceMoved = Make(0x25000020, 0, 0, 0, "Defeat the Orc", "changed-neighbor", 42, "record-q", "shape-q");
		Check(!SourceDigest.Matches(sourceA.SourceDigest, sourceB.SourceDigest), "source digest changes when English changes");
		passed++;
		Check(SourceDigest.Matches(sourceA.SourceDigest, sourceMoved.SourceDigest), "source digest survives a safe move");
		passed++;

		IList<CatalogDiffRecord> sourceDiff = CatalogDiff.Compare(
			new List<CatalogRecord>(), new List<CatalogRecord> { sourceA });
		string syntheticDatHash = CatalogIdentity.Sha256Hex("synthetic-dat-A");
		string syntheticCatalogHash = CatalogIdentity.Sha256Hex("synthetic-catalog-A");
		SemanticPatchDocument semanticPatch = SemanticPatchBuilder.Build(
			"tr-2026.09.05.1",
			syntheticDatHash,
			123,
			syntheticCatalogHash,
			sourceDiff,
			new List<TranslationCandidate>
			{
				new TranslationCandidate
				{
					entry_identity = sourceA.EntryIdentity,
					dat_key = sourceA.Key,
					source_digest = sourceA.SourceDigest,
					token_signature = sourceA.TokenSignature,
					target = "Ork'u Yen",
					translation_status = TranslationStatuses.HumanApproved,
					translation_engine = "human",
					translation_engine_version = "reviewed"
				}
			},
			"catalog-A",
			"generator-test",
			"NONE",
			"none",
			"");
		Check(semanticPatch.entries.Count == 1 && semanticPatch.counts.safe_translated_count == 1, "semantic patch builder emits safe entry");
		passed++;
		SemanticPatchDocument incrementalPatch = SemanticPatchBuilder.BuildIncremental(
			"tr-2026.09.05.2",
			syntheticDatHash,
			123,
			syntheticCatalogHash,
			"tr-2026.09.05.1",
			CatalogIdentity.Sha256Hex("candidate-dat-A"),
			456,
			CatalogIdentity.Sha256Hex("candidate-catalog-A"),
			sourceDiff,
			new List<TranslationCandidate>
			{
				new TranslationCandidate
				{
					entry_identity = sourceA.EntryIdentity,
					dat_key = sourceA.Key,
					source_digest = sourceA.SourceDigest,
					token_signature = sourceA.TokenSignature,
					target = "Ork'u Yen",
					translation_status = TranslationStatuses.HumanApproved
				}
			},
			"catalog-A-incremental",
			"generator-test",
			"NONE",
			"none",
			"");
		Check(incrementalPatch.patch_mode == SemanticPatchBuilder.IncrementalPatchMode
			&& incrementalPatch.base_patch_version == "tr-2026.09.05.1"
			&& incrementalPatch.base_candidate_dat_size == 456
			&& incrementalPatch.entries.Count == 1,
			"incremental semantic patch builder records predecessor identity");
		passed++;
		CatalogRecord sourceASecond = Make(0x25000020, 0, 0, 1, "Defeat the Warg", "quest-context-2", 11, "record-q", "shape-q");
		IList<CatalogDiffRecord> sameRecordDiff = CatalogDiff.Compare(
			new List<CatalogRecord>(), new List<CatalogRecord> { sourceA, sourceASecond });
		SemanticPatchDocument sameRecordPatch = SemanticPatchBuilder.Build(
			"tr-2026.09.05.same-record", syntheticDatHash, 123, syntheticCatalogHash, sameRecordDiff,
			new List<TranslationCandidate>
			{
				new TranslationCandidate { entry_identity = sourceA.EntryIdentity, dat_key = sourceA.Key, source_digest = sourceA.SourceDigest, token_signature = sourceA.TokenSignature, target = "Ork'u Yen", translation_status = TranslationStatuses.HumanApproved },
				new TranslationCandidate { entry_identity = sourceASecond.EntryIdentity, dat_key = sourceASecond.Key, source_digest = sourceASecond.SourceDigest, token_signature = sourceASecond.TokenSignature, target = "Warg'ı Yen", translation_status = TranslationStatuses.HumanApproved }
			},
			"catalog-A", "generator-test", "NONE", "none", "");
		List<LocRow> sameRecordRows = new List<LocRow>
		{
			new LocRow { Did = sourceA.Did, RecordIndex = 0, GroupIndex = 0, IndexInGroup = 0, Original = sourceA.Source, Translation = sourceA.Source },
			new LocRow { Did = sourceASecond.Did, RecordIndex = 0, GroupIndex = 0, IndexInGroup = 1, Original = sourceASecond.Source, Translation = sourceASecond.Source }
		};
		SemanticPatchApplyResult sameRecordApplied = SemanticPatchApplier.ApplyToRows(sameRecordPatch, new List<CatalogRecord> { sourceA, sourceASecond }, sameRecordRows, syntheticDatHash);
		Check(sameRecordPatch.entries.Count == 2 && sameRecordApplied.Applied == 2, "same-record rows use unique DAT keys");
		passed++;
		string serializedPatch = SemanticPatchSerializer.Serialize(semanticPatch);
		Check(serializedPatch == SemanticPatchSerializer.Serialize(SemanticPatchSerializer.Deserialize(serializedPatch)), "semantic patch serialization is deterministic");
		passed++;

		List<LocRow> rowsA = new List<LocRow>
		{
			new LocRow { Did = sourceA.Did, RecordIndex = sourceA.RecordIndex, GroupIndex = sourceA.GroupIndex, IndexInGroup = sourceA.IndexInGroup, Original = sourceA.Source, Translation = sourceA.Source }
		};
		SemanticPatchApplyResult applied = SemanticPatchApplier.ApplyToRows(semanticPatch, new List<CatalogRecord> { sourceA }, rowsA, syntheticDatHash);
		Check(applied.Applied == 1 && rowsA[0].Translation == "Ork'u Yen", "semantic patch applies matching digest");
		passed++;

		List<LocRow> rowsB = new List<LocRow>
		{
			new LocRow { Did = sourceB.Did, RecordIndex = sourceB.RecordIndex, GroupIndex = sourceB.GroupIndex, IndexInGroup = sourceB.IndexInGroup, Original = sourceB.Source, Translation = sourceB.Source }
		};
		SemanticPatchApplyResult stale = SemanticPatchApplier.ApplyToRows(semanticPatch, new List<CatalogRecord> { sourceB }, rowsB, syntheticDatHash);
		Check(stale.SourceChanged == 1 && stale.Applied == 0 && rowsB[0].Translation == sourceB.Source, "stale translation falls back to current English");
		passed++;

		CatalogRecord critical = Make(unchecked((int)0x250001A0u), 0, 0, 0, "Critical Button", "critical", 20, "record-critical", "shape-critical");
		IList<CatalogDiffRecord> partialDiff = CatalogDiff.Compare(
			new List<CatalogRecord>(), new List<CatalogRecord> { sourceA, critical });
		SemanticPatchDocument partialPatch = SemanticPatchBuilder.Build(
			"tr-2026.09.05.2",
			syntheticDatHash,
			123,
			syntheticCatalogHash,
			partialDiff,
			new List<TranslationCandidate>
			{
				new TranslationCandidate
				{
					entry_identity = sourceA.EntryIdentity,
					source_digest = sourceA.SourceDigest,
					token_signature = sourceA.TokenSignature,
					target = "Ork'u Yen",
					translation_status = TranslationStatuses.HumanApproved
				},
				new TranslationCandidate
				{
					entry_identity = critical.EntryIdentity,
					source_digest = critical.SourceDigest,
					token_signature = critical.TokenSignature,
					target = "Kritik Düğme",
					translation_status = TranslationStatuses.MachineTranslated
				}
			},
			"catalog-A",
			"generator-test",
			"OPUS",
			"unverified",
			"");
		Check(partialPatch.entries.Count == 1 && partialPatch.counts.critical_review_required_count == 1, "critical machine entry excluded from partial patch");
		passed++;

		CatalogRecord bundleOldMoved = Make(0x25000021, 0, 0, 0, "Move me", "move-context", 30, "record-move", "shape-move");
		CatalogRecord bundleNewMoved = Make(0x25000021, 0, 0, 0, "Move me", "move-context", 50, "record-move", "shape-move");
		CatalogRecord bundleOldUnchanged = Make(0x25000022, 0, 0, 0, "Stay", "stay-context", 40, "record-stay", "shape-stay");
		CatalogRecord bundleNewUnchanged = Make(0x25000022, 0, 0, 0, "Stay", "stay-context", 40, "record-stay", "shape-stay");
		CatalogRecord bundleNew = Make(0x25000023, 0, 0, 0, "New quest text", "new-context", 60, "record-new", "shape-new");
		IList<CatalogDiffRecord> bundleDiff = CatalogDiff.Compare(
			new List<CatalogRecord> { sourceA, bundleOldMoved, bundleOldUnchanged },
			new List<CatalogRecord> { sourceB, bundleNewMoved, bundleNewUnchanged, bundleNew });
		SourceBundleDocument bundle = SourceBundleBuilder.Build(
			syntheticDatHash,
			123,
			syntheticCatalogHash,
			bundleDiff,
			false,
			new Dictionary<string, string> { { "dat_file_id", "2" }, { "block_size", "256" } });
		Check(bundle.records.Count == 2 && bundle.records[0].classification == "MODIFIED" && bundle.records[1].classification == "NEW", "source bundle keeps only NEW/MODIFIED records");
		passed++;
		string serializedBundle = SourceBundleSerializer.Serialize(bundle);
		Check(serializedBundle.IndexOf("\"source\":", StringComparison.Ordinal) < 0, "source bundle omits raw English by default");
		passed++;
		Check(serializedBundle == SourceBundleSerializer.Serialize(SourceBundleSerializer.Deserialize(serializedBundle)), "source bundle serialization is deterministic");
		passed++;
		SourceBundleDocument bundleWithSource = SourceBundleBuilder.Build(syntheticDatHash, 123, syntheticCatalogHash, bundleDiff, true);
		string serializedBundleWithSource = SourceBundleSerializer.Serialize(bundleWithSource);
		Check(serializedBundleWithSource.IndexOf("\"source\":", StringComparison.Ordinal) >= 0, "source bundle raw English is explicit opt-in");
		passed++;
		SourceBundleDocument tamperedBundle = SourceBundleSerializer.Deserialize(serializedBundle);
		tamperedBundle.records[0].classification = "MOVED";
		try
		{
			SourceBundleValidator.EnsureValid(tamperedBundle);
			throw new Exception("tampered source bundle was accepted");
		}
		catch (InvalidDataException)
		{
			Check(true, "source bundle validator rejects unsafe classification");
			passed++;
		}
		passed += VerifyTranslationScope();
		Console.WriteLine("developer_tests_passed=" + passed);
		return passed;
	}

	private static int VerifyTranslationScope()
	{
		CatalogRecord first = Make(0x25000020, 0, 0, 0, "Open", "context", 0, "record", "shape");
		CatalogRecord second = Make(0x25000020, 0, 0, 1, "Open", "context", 1, "record", "shape");
		TranslationCandidate candidate = new TranslationCandidate
		{
			entry_identity = first.EntryIdentity, dat_key = first.Key, source_digest = first.SourceDigest,
			token_signature = first.TokenSignature, target = "Aç", translation_status = TranslationStatuses.HumanApproved
		};
		Func<TranslationCandidate[], SemanticPatchDocument> build = candidates => SemanticPatchBuilder.Build(
			"scope-test", CatalogIdentity.Sha256Hex("dat"), 123, CatalogIdentity.Sha256Hex("catalog"),
			CatalogDiff.Compare(new List<CatalogRecord>(), new List<CatalogRecord> { first, second }), candidates,
			"catalog", "test", "NONE", "none", "");
		Check(first.EntryIdentity == second.EntryIdentity && first.SourceDigest == second.SourceDigest,
			"scope fixture shares identity and text across two distinct DAT keys");
		SemanticPatchDocument patch = build(new[] { candidate });
		Check(patch.entries.Count == 1 && patch.entries[0].dat_key == first.Key,
			"explicit candidate key never leaks to a neighboring identical string");
		TranslationCandidate other = new TranslationCandidate
		{
			entry_identity = second.EntryIdentity, dat_key = second.Key, source_digest = second.SourceDigest,
			token_signature = second.TokenSignature, target = "Açık", translation_status = TranslationStatuses.HumanApproved
		};
		Check(build(new[] { candidate, other }).entries.Count == 2, "explicit per-key translations survive shared record identity");
		Check(build(new[] { candidate, candidate }).entries.Count == 0, "duplicate candidate keys are rejected without identity fallback");
		candidate.source_digest = null;
		Check(build(new[] { candidate }).entries.Count == 0, "missing source proof cannot be restamped");
		candidate.source_digest = first.SourceDigest;
		candidate.token_signature = null;
		Check(build(new[] { candidate }).entries.Count == 0, "missing token proof cannot be restamped");
		candidate.token_signature = first.TokenSignature;
		patch = build(new[] { candidate });
		patch.entries[0].index_in_group++;
		Check(!SemanticPatchValidator.TryValidate(patch, out _), "semantic coordinates must agree with DAT key");
		patch = build(new[] { candidate });
		patch.entries[0].critical_ui = !patch.entries[0].critical_ui;
		Check(!SemanticPatchValidator.TryValidate(patch, out _), "critical UI flag cannot contradict actual DID");
		patch = build(new[] { candidate });
		LocRow remainingRow = new LocRow { Did = second.Did, RecordIndex = second.RecordIndex,
			GroupIndex = second.GroupIndex, IndexInGroup = second.IndexInGroup, Original = second.Source, Translation = second.Source };
		SemanticPatchApplyResult result = SemanticPatchApplier.ApplyToRows(patch, new[] { second }, new[] { remainingRow });
		Check(result.Applied == 0 && result.Missing == 1 && remainingRow.Translation == second.Source,
			"removed key never redirects patch to surviving identical string");
		try
		{
			SemanticPatchApplier.ApplyToRows(patch, new[] { first, first }, new[] { remainingRow });
			throw new Exception("duplicate catalog accepted");
		}
		catch (InvalidDataException) { Check(true, "duplicate catalog rejected before row mutation"); }
		try
		{
			SemanticPatchApplier.ApplyToRows(patch, new[] { first }, new[] { remainingRow, remainingRow });
			throw new Exception("duplicate writable rows accepted");
		}
		catch (InvalidDataException) { Check(true, "duplicate writable rows rejected before mutation"); }
		return 11;
	}

	private static CatalogRecord Make(int did, int record, int group, int index, string source, string context, long position, string recordFingerprint, string structuralFingerprint)
	{
		return CatalogIdentity.FromLocRow(new LocRow
		{
			Did = did,
			RecordIndex = record,
			GroupIndex = group,
			IndexInGroup = index,
			Original = source
		}, recordFingerprint, structuralFingerprint, context, position);
	}

	private static void Check(bool condition, string name)
	{
		if (!condition) throw new Exception("FAIL " + name);
		Console.WriteLine("PASS " + name);
	}

	private static byte[] BuildStructuredFixture()
	{
		using (MemoryStream stream = new MemoryStream())
		using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode, true))
		{
			writer.Write(0);
			writer.Write(0x25000001);
			writer.Write(1);
			writer.Write((byte)1);
			writer.Write((long)123456);
			writer.Write(2);
			WriteVarString(writer, "Welcome");
			WriteVarString(writer, "Use %s");
			writer.Write(0);
			writer.Write((byte)0);
			writer.Flush();
			return stream.ToArray();
		}
	}

	private static void WriteVarString(BinaryWriter writer, string value)
	{
		writer.Write((byte)value.Length);
		writer.Write(Encoding.Unicode.GetBytes(value));
	}

	private static byte[] BuildHiddenUiFixture()
	{
		using (MemoryStream stream = new MemoryStream())
		using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode, true))
		{
			writer.Write(new byte[] { 1, 2 });
			writer.Write(3);
			WriteVarString(writer, "");
			WriteVarString(writer, " of ");
			WriteVarString(writer, " Character Slots Used");
			writer.Write(new byte[] { 4, 5 });
			return stream.ToArray();
		}
	}

	private static bool ContainsBytes(byte[] haystack, byte[] needle)
	{
		for (int i = 0; i <= haystack.Length - needle.Length; i++)
		{
			int j = 0;
			while (j < needle.Length && haystack[i + j] == needle[j]) j++;
			if (j == needle.Length) return true;
		}
		return false;
	}
}
