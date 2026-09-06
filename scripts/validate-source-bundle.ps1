[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
$file = Get-Item -LiteralPath $resolved
if ($file.Length -gt 64MB) { throw 'Source bundle 64 MiB sınırını aşıyor.' }

try { $bundle = Get-Content -LiteralPath $resolved -Raw -Encoding UTF8 | ConvertFrom-Json }
catch { throw "Source bundle JSON olarak okunamadı: $($_.Exception.Message)" }

if ($bundle.schema_version -ne 1 -or $bundle.bundle_kind -ne 'client_assisted_source_update') {
    throw 'Source bundle schema_version/bundle_kind geçersiz.'
}
if ($bundle.source_dat_size -lt 1 -or $bundle.source_dat_sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or $bundle.source_catalog_sha256 -notmatch '^[A-Fa-f0-9]{64}$') {
    throw 'Source DAT boyutu veya SHA-256 kimliği geçersiz.'
}

$records = @($bundle.records)
if ($records.Count -gt 100000) { throw 'Source bundle en fazla 100000 kayıt içerebilir.' }
$identities = @{}
$datKeys = @{}
$seen = 0
foreach ($record in $records) {
    if ($null -eq $record.PSObject.Properties['critical_ui']) { throw "Source bundle critical_ui alanı eksik: $($record.entry_identity)" }
    if ([string]::IsNullOrWhiteSpace([string]$record.entry_identity) -or
        [string]::IsNullOrWhiteSpace([string]$record.dat_key) -or
        [int]$record.did -lt 0 -or
        [int]$record.record_index -lt 0 -or
        [int]$record.group_index -lt -1 -or
        [int]$record.index_in_group -lt 0 -or
        [string]$record.source_digest -notmatch '^[A-Fa-f0-9]{64}$' -or
        [string]$record.token_signature -notmatch '^[A-Fa-f0-9]{64}$' -or
        ([string]$record.classification -notin @('NEW', 'MODIFIED'))) {
        throw "Source bundle kaydı geçersiz: $($record.entry_identity)"
    }
    $expectedDatKey = ('{0:X8}:{1}:{2}:{3}' -f [int]$record.did, [int]$record.record_index, [int]$record.group_index, [int]$record.index_in_group)
    if ([string]$record.dat_key -cne $expectedDatKey) { throw "Source bundle dat_key/coordinates mismatch: $($record.entry_identity)" }
    if (([uint32][int]$record.did -ge 0x250001A0) -and ([uint32][int]$record.did -le 0x250001FF) -and (-not [bool]$record.critical_ui)) {
        throw "Critical UI kaydı critical_ui=false olamaz: $($record.entry_identity)"
    }
    $key = [string]$record.entry_identity
    if ($identities.ContainsKey($key)) { throw "Source bundle duplicate entry_identity: $key" }
    $identities[$key] = $true
    $datKey = [string]$record.dat_key
    if ($datKeys.ContainsKey($datKey)) { throw "Source bundle duplicate dat_key: $datKey" }
    $datKeys[$datKey] = $true
    if ($null -ne $record.source -and ([string]$record.source).Length -gt 16000) {
        throw "Source bundle source alanı çok uzun: $key"
    }
    $seen++
}

Write-Output ("SOURCE_BUNDLE_VALID|records={0}|path={1}" -f $seen, $resolved)
