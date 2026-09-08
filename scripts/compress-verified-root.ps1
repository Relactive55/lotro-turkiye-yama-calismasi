[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory,
      [Parameter(Mandatory)][string]$UpdaterExe)
$ErrorActionPreference = 'Stop'
$directory = (Get-Item -LiteralPath $PackageDirectory).FullName
[void][Reflection.Assembly]::LoadFrom((Get-Item -LiteralPath $UpdaterExe).FullName)
Add-Type -AssemblyName System.Web.Extensions
$json = [System.Web.Script.Serialization.JavaScriptSerializer]::new()
$manifestPath = Join-Path $directory 'manifest-template.json'
$manifest = $json.Deserialize([IO.File]::ReadAllText($manifestPath),[LotroTurkceYama.Setup.ReleaseManifest])
if ($manifest.release_id -ne 0 -or $manifest.asset_id -ne 0 -or $manifest.patch_mode -cne 'full' -or
    $manifest.asset_name -cnotmatch '^lotro-turkce-yama-[A-Za-z0-9._-]+\.semantic\.json$') { throw 'Unpublished plain root template required.' }
$source = Join-Path $directory $manifest.asset_name
$destination = $source + '.gz'
$backup = Join-Path $directory 'manifest-uncompressed-template.json'
$next = Join-Path $directory 'manifest-compressed-template.json'
foreach ($path in @($destination,$backup,$next)) { if (Test-Path -LiteralPath $path) { throw "Existing output is never overwritten: $path" } }
if ((Get-Item -LiteralPath $source).Length -ne $manifest.asset_size -or
    (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $manifest.asset_sha256) { throw 'Verified plain semantic asset changed.' }
$inputFile = [IO.File]::OpenRead($source)
try {
    $output = [IO.File]::Open($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try {
        $gzip = [IO.Compression.GZipStream]::new($output,[IO.Compression.CompressionLevel]::Optimal,$true)
        try { $inputFile.CopyTo($gzip,1048576) } finally { $gzip.Dispose() }
        $output.Flush($true)
    } finally { $output.Dispose() }
} finally { $inputFile.Dispose() }
$compressedFile = [IO.File]::OpenRead($destination)
try {
    $gzip = [IO.Compression.GZipStream]::new($compressedFile,[IO.Compression.CompressionMode]::Decompress)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $restoredHash = [BitConverter]::ToString($sha.ComputeHash($gzip)).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose(); $gzip.Dispose() }
} finally { $compressedFile.Dispose() }
if ($restoredHash -cne $manifest.asset_sha256) { throw 'Compression changed the semantic bytes.' }
$patch = [LotroTrGemini.SemanticPatchSerializer]::ReadFile($destination)
if ($patch.entries.Count -ne $manifest.safe_translated_count -or $patch.patch_version -cne $manifest.patch_version) { throw 'Compressed reader contract mismatch.' }
$originalSize = $manifest.asset_size
$manifest.asset_name += '.gz'
$manifest.asset_size = (Get-Item -LiteralPath $destination).Length
$manifest.asset_sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest.minimum_updater_version = [LotroTurkceYama.Setup.LotroReleaseUpdater]::CurrentUpdaterVersion
if ([Version]$manifest.minimum_updater_version -lt [Version]'1.2.0.0') { throw 'Gzip requires updater 1.2.0.0 or newer.' }
[IO.File]::WriteAllText($next,$json.Serialize($manifest),[Text.UTF8Encoding]::new($false))
[IO.File]::Replace($next,$manifestPath,$backup,$true)
"COMPRESSED_ROOT_PASS|original_bytes=$originalSize|compressed_bytes=$($manifest.asset_size)|sha256=$($manifest.asset_sha256)|identical_semantic_bytes=true|DAT_unchanged=true"
