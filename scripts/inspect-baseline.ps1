[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DatPath,
    [string]$OutputPath,
    [string]$NativeMetadataPath
)

$ErrorActionPreference = 'Stop'
$item = Get-Item -LiteralPath $DatPath -ErrorAction Stop
if (-not $item.PSIsContainer -and $item.Name -eq 'client_local_English.dat') { } else { throw 'DatPath client_local_English.dat olmalıdır.' }

function Get-StreamingSha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose() }
    }
    finally { $sha.Dispose() }
}

$record = [ordered]@{
    source_kind = 'clean_english_dat'
    file_name = $item.Name
    size = [int64]$item.Length
    sha256 = Get-StreamingSha256 $item.FullName
    modified_utc = $item.LastWriteTimeUtc.ToString('o')
    dat_metadata = [ordered]@{
        block_size = $null
        vnum_dat_file = $null
        vnum_game_data = $null
        dat_file_id = $null
        dat_stamp = $null
        first_iteration_guid = $null
    }
    catalog_sha256 = ''
    notes = 'Native metadata is optional input from a separately controlled x86 read-only inspector; proprietary path is intentionally omitted.'
}
if ($NativeMetadataPath) {
    $native = Get-Item -LiteralPath $NativeMetadataPath -ErrorAction Stop
    if ($native.PSIsContainer) { throw 'NativeMetadataPath bir JSON dosyası olmalıdır.' }
    try { $nativeRecord = Get-Content -LiteralPath $native.FullName -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { throw "Native metadata JSON okunamadı: $($native.FullName)" }
    if (-not $nativeRecord.dat_sha256 -or $nativeRecord.dat_sha256.ToLowerInvariant() -ne $record.sha256.ToLowerInvariant()) {
        throw 'Native metadata SHA-256 ile DAT SHA-256 eşleşmiyor.'
    }
    if ($nativeRecord.dat_size -and [int64]$nativeRecord.dat_size -ne $record.size) { throw 'Native metadata DAT boyutu ile eşleşmiyor.' }
    foreach ($name in @('block_size','vnum_dat_file','vnum_game_data','dat_file_id','dat_stamp','first_iteration_guid')) {
        if ($null -ne $nativeRecord.$name) { $record.dat_metadata[$name] = $nativeRecord.$name }
    }
    if ($nativeRecord.catalog_sha256) { $record.catalog_sha256 = $nativeRecord.catalog_sha256.ToLowerInvariant() }
    $record.notes = 'SHA-256, native metadata and catalog identity were joined from the same read-only x86 inspection.'
}
$json = $record | ConvertTo-Json -Depth 6
if ($OutputPath) {
    $resolvedOutput = $OutputPath
    if (Test-Path -LiteralPath $OutputPath) { $resolvedOutput = (Resolve-Path -LiteralPath $OutputPath).Path }
    else {
        $parent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
        if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    }
    [IO.File]::WriteAllText($resolvedOutput, $json, [Text.UTF8Encoding]::new($false))
}
$json
