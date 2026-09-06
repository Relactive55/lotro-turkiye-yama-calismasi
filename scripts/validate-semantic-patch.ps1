[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
$json = Get-Content -LiteralPath $resolved -Raw -Encoding UTF8 | ConvertFrom-Json
if ($json.schema_version -ne 1 -or $json.patch_kind -ne 'semantic_delta_patch') { throw 'Semantic patch kimliği geçersiz.' }
if ([string]$json.source_dat_sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or [string]$json.source_catalog_sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Semantic patch baseline hash geçersiz.' }
$entries = @($json.entries)
if ([int]$json.counts.safe_translated_count -ne $entries.Count) { throw 'safe_translated_count ile entries sayısı farklı.' }
$seen = @{}
foreach ($entry in $entries) {
    if ([string]::IsNullOrWhiteSpace([string]$entry.entry_identity) -or [string]$entry.source_digest -notmatch '^[A-Fa-f0-9]{64}$' -or [string]$entry.token_signature -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Semantic patch entry kimliği geçersiz.' }
    if ($seen.ContainsKey([string]$entry.entry_identity)) { throw 'Semantic patch duplicate entry_identity.' }
    $seen[[string]$entry.entry_identity] = $true
    if ($null -ne $entry.source) { throw 'Semantic patch raw English source taşıyamaz.' }
}
Write-Output ("SEMANTIC_PATCH_VALID|entries={0}|path={1}" -f $entries.Count, $resolved)
