[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
if ($resolved.EndsWith('.gz',[StringComparison]::OrdinalIgnoreCase)) {
    $file = [IO.File]::OpenRead($resolved)
    try {
        $gzip = [IO.Compression.GZipStream]::new($file,[IO.Compression.CompressionMode]::Decompress)
        $reader = [IO.StreamReader]::new($gzip,[Text.UTF8Encoding]::new($false,$true))
        try { $json = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    } finally { $file.Dispose() }
} else { $json = Get-Content -LiteralPath $resolved -Raw -Encoding UTF8 | ConvertFrom-Json }
if ($json.schema_version -ne 1 -or $json.patch_kind -ne 'semantic_delta_patch') { throw 'Semantic patch kimliği geçersiz.' }
if ([string]$json.source_dat_sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or [string]$json.source_catalog_sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Semantic patch baseline hash geçersiz.' }
$mode = if ([string]::IsNullOrWhiteSpace([string]$json.patch_mode)) { 'full' } else { [string]$json.patch_mode }
if ($mode -ne 'full' -and $mode -ne 'incremental') { throw 'Semantic patch modu geçersiz.' }
if ($mode -eq 'incremental') {
    if ([string]::IsNullOrWhiteSpace([string]$json.base_patch_version) -or
        [string]$json.base_candidate_dat_sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
        [int64]$json.base_candidate_dat_size -lt 1 -or
        [string]$json.base_candidate_catalog_sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Incremental semantic predecessor kimliği geçersiz.' }
}
$entries = @($json.entries)
if ([int]$json.counts.safe_translated_count -ne $entries.Count) { throw 'safe_translated_count ile entries sayısı farklı.' }
$seenDatKeys = @{}
$structuralPreserveDids = @('250001A7','250001AF','250001BB','25008A58','2503B6C1') | ForEach-Object { [Convert]::ToInt32($_, 16) }
$mojibakeMarkers = @('Ã¼','Ã¶','Ã§','Ã‡','Ã–','Ãœ','Ä±','Ä°','ÄŸ','Äž','ÅŸ','Åž','ï¿½','�')
foreach ($entry in $entries) {
    if ([string]::IsNullOrWhiteSpace([string]$entry.entry_identity) -or [string]$entry.source_digest -notmatch '^[A-Fa-f0-9]{64}$' -or [string]$entry.token_signature -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Semantic patch entry kimliği geçersiz.' }
    # One logical record may contain multiple independently translated string
    # groups and therefore share entry_identity. DAT keys must remain unique.
    if ($seenDatKeys.ContainsKey([string]$entry.dat_key)) { throw 'Semantic patch duplicate dat_key.' }
    $seenDatKeys[[string]$entry.dat_key] = $true
    if ($null -ne $entry.source) { throw 'Semantic patch raw English source taşıyamaz.' }
    if ($structuralPreserveDids -contains [int]$entry.did) { throw ('Yapısal string tablosu değiştirilemez: ' + $entry.dat_key) }
    $target = ([string]$entry.target).Trim()
    foreach ($marker in $mojibakeMarkers) {
        if ($target.Contains($marker)) { throw ('Bozuk UTF-8 hedefi: ' + $entry.dat_key) }
    }
    if ($target.Length -ge 24) {
        for ($split = [Math]::Max(1, [int]($target.Length / 2) - 2); $split -le [Math]::Min($target.Length - 1, [int]($target.Length / 2) + 2); $split++) {
            $left = $target.Substring(0, $split).Trim()
            $right = $target.Substring($split).Trim()
            if ($left.Length -ge 12 -and $left -ceq $right) { throw ('Yinelenmiş hedef metni: ' + $entry.dat_key) }
        }
    }
}
Write-Output ("SEMANTIC_PATCH_VALID|entries={0}|path={1}" -f $entries.Count, $resolved)
