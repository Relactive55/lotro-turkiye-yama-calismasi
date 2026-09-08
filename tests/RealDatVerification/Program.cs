using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using LotroTrGemini;
using LotroTurkceYama.Setup;

// Opt-in, offline verification only. Never replaces the input or publishes assets.
internal static class Program
{
    private static int Main(string[] args)
    {
        bool inspectResult = args.Length == 5 && args[4] == "--inspect-result";
        if (args.Length != 4 && !inspectResult)
            throw new ArgumentException("source.dat manifest.json semantic.json NEW-private-candidate.dat [--inspect-result]");
        string source = Path.GetFullPath(args[0]);
        string candidate = Path.GetFullPath(args[3]);
        if (File.Exists(candidate) || Directory.Exists(candidate)
            || string.Equals(source, candidate, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Candidate must be a new, separate file.");
        var clock = Stopwatch.StartNew();
        Action<string> log = message => Console.WriteLine("[" + clock.Elapsed.ToString(@"hh\:mm\:ss") + "] " + message);
        var manifest = new JavaScriptSerializer().Deserialize<ReleaseManifest>(File.ReadAllText(args[1], Encoding.UTF8));
        var sourceBefore = new FileInfo(source);
        long sourceSize = sourceBefore.Length;
        DateTime sourceTime = sourceBefore.LastWriteTimeUtc;
        string sourceHash = Hash(source);
        if (sourceSize != manifest.source_dat_size || !Equal(sourceHash, manifest.source_dat_sha256))
            throw new InvalidDataException("Source does not match manifest.");
        if (new FileInfo(args[2]).Length != manifest.asset_size || !Equal(Hash(args[2]), manifest.asset_sha256))
            throw new InvalidDataException("Asset does not match manifest.");
        log("Verified input hashes; loading semantic document.");
        var patch = SemanticPatchSerializer.ReadFile(args[2]);
        if (patch.patch_version != manifest.patch_version || patch.source_dat_size != manifest.source_dat_size
            || !Equal(patch.source_dat_sha256, manifest.source_dat_sha256)
            || !Equal(patch.source_catalog_sha256, manifest.source_catalog_sha256))
            throw new InvalidDataException("Patch metadata does not match manifest.");
        ManagedSemanticDatPatcher.Result result;
        try
        {
            // Diagnostic mode still verifies every target and the full DAT structure.
            // It retains a private result for comparison, but never reports a
            // release pass if its catalog differs from the supplied manifest.
            result = ManagedSemanticDatPatcher.BuildCandidate(source, candidate, patch,
                inspectResult ? null : manifest.candidate_catalog_sha256, CancellationToken.None, log);
            if (!string.IsNullOrWhiteSpace(manifest.candidate_dat_sha256)
                && (result.DatSize != manifest.candidate_dat_size || !Equal(result.DatSha256, manifest.candidate_dat_sha256)))
                throw new InvalidDataException("Candidate binary does not match manifest.");
        }
        finally
        {
            var after = new FileInfo(source);
            if (after.Length != sourceSize || after.LastWriteTimeUtc != sourceTime || !Equal(Hash(source), sourceHash))
                throw new InvalidDataException("Input DAT changed during verification.");
            log("Input DAT unchanged (SHA-256, length and modification time).");
        }
        bool catalogMatches = Equal(result.CatalogSha256, manifest.candidate_catalog_sha256);
        log((catalogMatches ? "REAL_DAT_PASS" : "RELEASE_CATALOG_MISMATCH") + "|applied=" + result.Applied + "|dids=" + result.TouchedDids
            + "|records=" + result.RecordCount + "|catalog=" + result.CatalogSha256
            + "|dat=" + result.DatSha256 + "|size=" + result.DatSize);
        return catalogMatches ? 0 : 2;
    }

    private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Hash(string path)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
