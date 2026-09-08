using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using LotroTrGemini;
using LotroTurkceYama.Setup;

internal static class PatcherRegressionTests
{
    private const int FellowshipDid = 0x250001AF;
    private static readonly Type UnitType = typeof(ManagedSemanticDatPatcher).GetNestedType("Unit", BindingFlags.NonPublic);
    private static readonly MethodInfo BuildBlob = typeof(ManagedSemanticDatPatcher).GetMethod("BuildVerifiedBlob", BindingFlags.Static | BindingFlags.NonPublic);

    public static int Run()
    {
        int passed = 0;
        byte[] fixture = BuildFellowshipFixture();
        foreach (bool compressed in new[] { false, true })
        {
            object unit = CreateUnit(fixture, compressed);
            List<LocRow> rows = Rows(unit);
            LocRow semanticRow = rows.Single(row => row.Key == "250001AF:0:-1:0");
            semanticRow.Translation = "Pencereyi aç";
            ChangedKeys(unit).Add(semanticRow.Key);
            byte[] blob = InvokeBuild(unit, true);
            List<LocRow> result = LocBin.Parse(TurbineDat.MaybeDecompress(blob), FellowshipDid).GetRows(FellowshipDid);
            Check(TurbineDat.LooksCompressed(blob) == compressed
                && result.Count == rows.Count
                && result.Single(row => row.Key == semanticRow.Key).Original == "Pencereyi aç"
                && result.Single(row => row.Key == "250001AF:272:-1:0").Original == "Oyuncular"
                && result.Single(row => row.Key == "250001AF:3:-1:0").Original == "Bekleme Süresi: "
                && result.Single(row => row.Key == "250001AF:1:-1:0").Original == "Unchanged text",
                "semantic and automatic UI changes in one DID preserve " + (compressed ? "compressed" : "raw") + " storage");
            passed++;
        }

        object automaticOnly = CreateUnit(fixture, false);
        byte[] automaticallyFixed = InvokeBuild(automaticOnly, true);
        Check(LocBin.Parse(automaticallyFixed, FellowshipDid).GetRows(FellowshipDid)
                .Single(row => row.Key == "250001AF:272:-1:0").Original == "Oyuncular",
            "automatic-only UI changes pass semantic identity validation");
        passed++;
        Check(InvokeBuild(CreateUnit(automaticallyFixed, false), true).SequenceEqual(automaticallyFixed),
            "already-applied automatic UI changes remain byte-identical");
        passed++;

        object disabled = CreateUnit(fixture, false);
        Rows(disabled)[0].Translation = "Pencereyi aç";
        ChangedKeys(disabled).Add(Rows(disabled)[0].Key);
        List<LocRow> disabledRows = LocBin.Parse(InvokeBuild(disabled, false), FellowshipDid).GetRows(FellowshipDid);
        Check(disabledRows[0].Original == "Pencereyi aç"
                && disabledRows.Single(row => row.Key == "250001AF:272:-1:0").Original == "Players",
            "recovery mode can apply semantic changes without automatic UI changes");
        passed++;

        // Corrupt only the expected source identity, leaving a valid UI table.
        // The former automatic-only bypass would accept this mismatched unit.
        object wrongIdentity = CreateUnit(fixture, false);
        byte[] wrongPayload = (byte[])fixture.Clone();
        wrongPayload[wrongPayload.Length - 1] ^= 1;
        UnitType.GetField("Payload").SetValue(wrongIdentity, wrongPayload);
        ExpectInvalidData(() => InvokeBuild(wrongIdentity, true), "identity rebuild",
            "automatic-only UI changes reject an incorrect source identity");
        passed++;

        object wrongTarget = CreateUnit(fixture, false);
        Rows(wrongTarget)[0].RecordIndex = int.MaxValue;
        Rows(wrongTarget)[0].Translation = "Pencereyi aç";
        ExpectInvalidData(() => InvokeBuild(wrongTarget, true), "hedef uyuşmazlığı",
            "automatic UI changes do not bypass semantic target validation");
        Check(Rows(wrongTarget)[0].Translation == "Pencereyi aç", "failed validation preserves requested translations");
        passed += 2;

        LocBin driftedBin = LocBin.Parse(fixture, FellowshipDid);
        List<LocRow> driftedRows = driftedBin.GetRows(FellowshipDid);
        driftedRows.Single(row => row.Key == "250001AF:272:-1:0").Translation = "Unexpected source";
        object drifted = CreateUnit(driftedBin.Rebuild(driftedRows), false);
        ExpectInvalidData(() => InvokeBuild(drifted, true), "beklenmeyen kaynak",
            "automatic UI changes reject source text drift");
        passed++;
        passed += RunDirectoryIndexTests();
        passed += RunCloseFailureTests();
        passed += RunCandidatePathTests();
        return passed;
    }

    private static int RunCandidatePathTests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "lotro-candidate-path-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "source.dat");
            string backup = Path.Combine(directory, "prior-clean.dat");
            string existing = Path.Combine(directory, "existing-candidate.dat");
            string candidate = Path.Combine(directory, "new-candidate.dat");
            string recovered = Path.Combine(directory, "recovered-candidate.dat");
            const string sourceText = "Synthetic source DAT sentinel";
            Dictionary<string, byte[]> sentinels = new Dictionary<string, byte[]>
            {
                { source, Encoding.UTF8.GetBytes(sourceText) },
                { backup, Encoding.UTF8.GetBytes("Synthetic prior clean backup sentinel") },
                { existing, Encoding.UTF8.GetBytes("Existing caller-owned output sentinel") }
            };
            foreach (KeyValuePair<string, byte[]> item in sentinels) File.WriteAllBytes(item.Key, item.Value);
            Action verifyPreserved = () =>
            {
                foreach (KeyValuePair<string, byte[]> item in sentinels)
                    if (!File.Exists(item.Key) || !File.ReadAllBytes(item.Key).SequenceEqual(item.Value))
                        throw new Exception("Candidate rejection changed an existing input or output: " + item.Key);
                if (File.Exists(candidate) || File.Exists(recovered))
                    throw new Exception("Candidate rejection left a new output file");
            };

            int passed = 0;
            foreach (bool incremental in new[] { false, true })
            {
                string mode = incremental ? "incremental" : "root";
                Action<string, SemanticPatchDocument, CancellationToken> build = (output, patch, token) =>
                {
                    if (incremental) ManagedSemanticDatPatcher.BuildIncrementalCandidate(source, output, patch, null, token);
                    else ManagedSemanticDatPatcher.BuildCandidate(source, output, patch, null, token);
                };
                // A null patch makes these public-API tests prove path admission
                // occurs before patch validation or any attempt to parse the DAT.
                ExpectPathRejected(() => build(Path.Combine(directory, ".", "SOURCE.DAT"), null, CancellationToken.None),
                    verifyPreserved, mode + " candidate rejects a canonical source alias and preserves source bytes");
                ExpectPathRejected(() => build(existing, null, CancellationToken.None),
                    verifyPreserved, mode + " candidate rejects an existing output without deleting its sentinel");
                passed += 2;

                string hash = CatalogIdentity.Sha256Hex(sourceText);
                SemanticPatchDocument cancellationPatch = new SemanticPatchDocument
                {
                    schema_version = 1,
                    patch_kind = SemanticPatchBuilder.PatchKind,
                    patch_mode = incremental ? SemanticPatchBuilder.IncrementalPatchMode : SemanticPatchBuilder.FullPatchMode,
                    patch_version = "candidate-path-test",
                    source_dat_sha256 = hash,
                    source_dat_size = sentinels[source].Length,
                    source_catalog_sha256 = CatalogIdentity.Sha256Hex("synthetic catalog"),
                    base_patch_version = incremental ? "candidate-path-base" : null,
                    base_candidate_dat_sha256 = incremental ? hash : null,
                    base_candidate_dat_size = incremental ? sentinels[source].Length : 0,
                    base_candidate_catalog_sha256 = incremental ? CatalogIdentity.Sha256Hex("synthetic predecessor catalog") : null,
                    counts = new SemanticPatchCounts(),
                    entries = new List<SemanticPatchEntry>()
                };
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    bool canceled = false;
                    try { build(candidate, cancellationPatch, cancellation.Token); }
                    catch (OperationCanceledException) { canceled = true; }
                    verifyPreserved();
                    Check(canceled, mode + " candidate initial cancellation preserves inputs and creates no output");
                    passed++;
                }
            }

            ExpectPathRejected(() => ManagedSemanticDatPatcher.RecoverUpdatedPatchedDat(source, backup,
                Path.Combine(directory, ".", "source.dat"), recovered, null, null, CancellationToken.None),
                verifyPreserved, "recovery rejects a clean output alias of the current DAT and preserves both inputs");
            ExpectPathRejected(() => ManagedSemanticDatPatcher.RecoverUpdatedPatchedDat(source, backup,
                candidate, Path.Combine(directory, ".", "PRIOR-CLEAN.DAT"), null, null, CancellationToken.None),
                verifyPreserved, "recovery rejects a translated output alias of the prior backup before creating the clean output");
            ExpectPathRejected(() => ManagedSemanticDatPatcher.RecoverUpdatedPatchedDat(source, backup,
                candidate, Path.Combine(directory, ".", "NEW-CANDIDATE.DAT"), null, null, CancellationToken.None),
                verifyPreserved, "recovery rejects two output paths that resolve to the same new file");
            return passed + 3;
        }
        finally
        {
            string resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("lotro-candidate-path-test-", StringComparison.Ordinal))
                throw new IOException("Unexpected candidate path fixture cleanup target");
            Directory.Delete(resolved, true);
        }
    }

    private static void ExpectPathRejected(Action action, Action verifyPreserved, string name)
    {
        bool rejected = false;
        try { action(); }
        // InvalidDataException also inherits IOException. Accept only the path
        // guard's IOException, so a null-patch validation error cannot pass.
        catch (IOException ex) when (ex.GetType() == typeof(IOException)) { rejected = true; }
        verifyPreserved();
        Check(rejected, name);
    }

    private static int RunCloseFailureTests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "lotro-close-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckCloseFailure(Path.Combine(directory, "flush.dat"), true, false,
                "DAT close propagates durable flush failure and releases the file");
            CheckCloseFailure(Path.Combine(directory, "dispose.dat"), false, true,
                "DAT close propagates disposal failure and releases the file");
            CheckCloseFailure(Path.Combine(directory, "both.dat"), true, true,
                "DAT close preserves the first error when flush and disposal both fail");
            string readOnlyPath = Path.Combine(directory, "readonly.dat");
            FailingFileStream readOnly = new FailingFileStream(readOnlyPath, true, false);
            TurbineDat readOnlyDat = AttachStream(readOnly, false);
            readOnlyDat.Close();
            Check(!readOnly.DurableFlushAttempted && readOnly.DisposalAttempted,
                "read-only DAT close releases the stream without a durable flush");
            using (File.Open(readOnlyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            return 4;
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void CheckCloseFailure(string path, bool failFlush, bool failDispose, string name)
    {
        FailingFileStream stream = new FailingFileStream(path, failFlush, failDispose);
        TurbineDat dat = AttachStream(stream, true);
        Exception observed = null;
        try { dat.Close(); }
        catch (IOException ex) { observed = ex; }
        Exception expected = failFlush ? stream.FlushFailure : stream.DisposeFailure;
        Check(ReferenceEquals(observed, expected) && stream.DisposalAttempted
                && observed.StackTrace.IndexOf(nameof(FailingFileStream), StringComparison.Ordinal) >= 0,
            name);
        // Fields must be cleared even after a failed close, and the OS handle
        // must be released so candidate cleanup can remove the failed file.
        dat.Close();
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    private static TurbineDat AttachStream(FileStream stream, bool writable)
    {
        TurbineDat dat = new TurbineDat();
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(TurbineDat).GetField("_fs", flags).SetValue(dat, stream);
        typeof(TurbineDat).GetField("_br", flags).SetValue(dat, new BinaryReader(stream, Encoding.Unicode, true));
        typeof(TurbineDat).GetField("_writable", flags).SetValue(dat, writable);
        return dat;
    }

    private sealed class FailingFileStream : FileStream
    {
        private readonly bool _failFlush;
        private readonly bool _failDispose;
        public readonly IOException FlushFailure = new IOException("simulated durable flush failure");
        public readonly IOException DisposeFailure = new IOException("simulated disposal failure");
        public bool DurableFlushAttempted;
        public bool DisposalAttempted;

        public FailingFileStream(string path, bool failFlush, bool failDispose)
            : base(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None)
        {
            _failFlush = failFlush;
            _failDispose = failDispose;
        }

        public override void Flush(bool flushToDisk)
        {
            DurableFlushAttempted |= flushToDisk;
            if (flushToDisk && _failFlush) throw FlushFailure;
            base.Flush(flushToDisk);
        }

        protected override void Dispose(bool disposing)
        {
            DisposalAttempted = true;
            base.Dispose(disposing);
            if (disposing && _failDispose) throw DisposeFailure;
        }
    }

    private static int RunDirectoryIndexTests()
    {
        string directory = Path.Combine(Path.GetTempPath(), "lotro-index-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] fixture = new byte[4096];
            PutUInt32(fixture, 320, 21570);
            PutUInt32(fixture, 324, 1024);
            PutUInt32(fixture, 328, (uint)fixture.Length);
            PutUInt32(fixture, 352, 1024);
            PutUInt32(fixture, 1024 + 504, 2);
            int firstEntry = 1024 + 508;
            PutUInt32(fixture, firstEntry, 77);
            PutUInt32(fixture, firstEntry + 4, 0x25000001);
            PutUInt32(fixture, firstEntry + 8, 2048);
            PutUInt32(fixture, firstEntry + 12, 24);
            PutUInt32(fixture, firstEntry + 24, 32);
            PutUInt32(fixture, firstEntry + 32 + 4, 0x25000002);
            string validPath = Path.Combine(directory, "valid.dat");
            File.WriteAllBytes(validPath, fixture);
            using (TurbineDat dat = new TurbineDat())
            {
                dat.Open(validPath, false);
                dat.BuildEntryIndex();
                Check(dat.TryGetEntry(0x25000001, out DatEntry entry)
                        && entry.Id == 0x25000001 && entry.Offset == 2048
                        && entry.Size == 24 && entry.Size2 == 32 && entry.Flags == 77
                        && dat.TryGetEntry(0x25000002, out DatEntry second) && second.Id == 0x25000002,
                    "batched DAT directory indexing preserves entry positions and metadata");
            }

            byte[] duplicate = (byte[])fixture.Clone();
            PutUInt32(duplicate, firstEntry + 32 + 4, 0x25000001);
            ExpectBadIndex(directory, "duplicate.dat", duplicate, "duplicate entry ID", "duplicate DAT entry IDs are rejected");
            byte[] cycle = (byte[])fixture.Clone();
            PutUInt32(cycle, 1024 + 8, 1);
            PutUInt32(cycle, 1024 + 12, 1024);
            ExpectBadIndex(directory, "cycle.dat", cycle, "cycle", "cyclic DAT directory nodes are rejected");
            byte[] badCount = (byte[])fixture.Clone();
            PutUInt32(badCount, 1024 + 504, 500000);
            ExpectBadIndex(directory, "count.dat", badCount, "entry count", "DAT directory table beyond file bounds is rejected");
            byte[] badChild = (byte[])fixture.Clone();
            PutUInt32(badChild, 1024 + 8, 1);
            PutUInt32(badChild, 1024 + 12, 4080);
            ExpectBadIndex(directory, "child.dat", badChild, "valid bounds", "truncated DAT directory child header is rejected");
            return 5;
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void ExpectBadIndex(string directory, string name, byte[] bytes, string message, string description)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, bytes);
        using (TurbineDat dat = new TurbineDat())
        {
            dat.Open(path, false);
            if (name != "duplicate.dat")
            {
                bool rejected = false;
                try { dat.ListLocalization(); }
                catch (InvalidDataException) { rejected = true; }
                if (!rejected) throw new Exception("Invalid DAT directory accepted during catalog enumeration");
            }
            ExpectInvalidData(() => dat.BuildEntryIndex(), message, description);
            // An invalid traversal must not expose the partial dictionary on
            // a later lookup, even if its first node contained a valid DID.
            try { dat.TryGetEntry(0x25000001, out DatEntry ignored); }
            catch (InvalidDataException) { return; }
            throw new Exception("Invalid DAT directory published a partial index");
        }
    }

    private static void PutUInt32(byte[] bytes, int offset, uint value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
    }

    private static object CreateUnit(byte[] payload, bool compressed)
    {
        LocBin bin = LocBin.Parse(payload, FellowshipDid);
        if (!bin.UsedFlatFallback) throw new Exception("UI regression fixture must use flat fallback");
        object unit = Activator.CreateInstance(UnitType, true);
        UnitType.GetField("Entry").SetValue(unit, new DatEntry { Id = FellowshipDid });
        UnitType.GetField("RawLength").SetValue(unit, TurbineDat.PackBlob(payload, compressed, 0).LongLength);
        UnitType.GetField("Payload").SetValue(unit, payload);
        UnitType.GetField("WasCompressed").SetValue(unit, compressed);
        UnitType.GetField("Bin").SetValue(unit, bin);
        UnitType.GetField("Rows").SetValue(unit, bin.GetRows(FellowshipDid));
        return unit;
    }

    private static List<LocRow> Rows(object unit) => (List<LocRow>)UnitType.GetField("Rows").GetValue(unit);
    private static HashSet<string> ChangedKeys(object unit) => (HashSet<string>)UnitType.GetField("ChangedKeys").GetValue(unit);

    private static byte[] InvokeBuild(object unit, bool applyKnownUiFixes)
    {
        try { return (byte[])BuildBlob.Invoke(null, new[] { unit, (object)applyKnownUiFixes }); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static byte[] BuildFellowshipFixture()
    {
        // Include every registered source row so additions to the protected
        // table keep exercising its fail-closed writer. Output assertions above
        // use explicit target text independent of this fixture construction.
        Dictionary<int, Dictionary<int, string>> records = new Dictionary<int, Dictionary<int, string>>();
        Array fixes = (Array)typeof(KnownUiFixes).GetField("FlatFixes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        foreach (object fix in fixes)
        {
            Type fixType = fix.GetType();
            string key = (string)fixType.GetField("Key").GetValue(fix);
            if (!key.StartsWith("250001AF:", StringComparison.Ordinal)) continue;
            string[] parts = key.Split(':');
            int recordIndex = int.Parse(parts[1]);
            int variantIndex = int.Parse(parts[3]);
            if (!records.TryGetValue(recordIndex, out Dictionary<int, string> variants))
                records.Add(recordIndex, variants = new Dictionary<int, string>());
            variants.Add(variantIndex, (string)fixType.GetField("Source").GetValue(fix));
        }
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.Unicode, true))
        {
            writer.Write(0);
            writer.Write(FellowshipDid);
            writer.Write(1);
            writer.Write((byte)0); // Force the conservative flat fallback.
            for (int recordIndex = 0; recordIndex <= records.Keys.Max(); recordIndex++)
            {
                records.TryGetValue(recordIndex, out Dictionary<int, string> variants);
                int count = variants == null ? 1 : variants.Keys.Max() + 1;
                writer.Write(0x5151515151510000L + recordIndex);
                writer.Write(count);
                for (int variantIndex = 0; variantIndex < count; variantIndex++)
                {
                    string value = "Unchanged text";
                    if (variants != null && variants.TryGetValue(variantIndex, out string source)) value = source;
                    if (recordIndex == 0) value = "Open window";
                    if (value.Length < 128) writer.Write((byte)value.Length);
                    else { writer.Write((byte)(0x80 | (value.Length >> 8))); writer.Write((byte)(value.Length & 0xFF)); }
                    writer.Write(Encoding.Unicode.GetBytes(value));
                }
                writer.Write(0xFEFEFEFEu);
            }
            var hidden = (Tuple<byte[], byte[]>[])typeof(KnownUiFixes)
                .GetField("HiddenTooltipFixes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            foreach (var fix in hidden) writer.Write(fix.Item1);
            return stream.ToArray();
        }
    }

    private static void ExpectInvalidData(Action action, string message, string name)
    {
        try { action(); }
        catch (InvalidDataException ex)
        {
            if (ex.Message.IndexOf(message, StringComparison.Ordinal) < 0) throw;
            Console.WriteLine("PASS " + name);
            return;
        }
        throw new Exception("FAIL " + name + ": expected InvalidDataException");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL " + name);
        Console.WriteLine("PASS " + name);
    }
}
