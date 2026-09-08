[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceDat,
    [Parameter(Mandatory)][string]$Decisions,
    [string]$GeneratorAssembly = (Join-Path $PSScriptRoot '../developer-tool/automation/SemanticPatchGenerator/bin/Release/net472/SemanticPatchGenerator.exe')
)

# Read-only targeted preflight, NOT a replacement for full release verification.
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::LoadFrom((Get-Item -LiteralPath $GeneratorAssembly).FullName)
$items = @(Get-Content -LiteralPath $Decisions -Encoding UTF8 | Where-Object { $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
if (!$items.Count) { throw 'No reviewed decisions.' }
$keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($item in $items) {
    if ($item.dat_key -cnotmatch '^[0-9A-F]{8}:\d+:-?\d+:\d+$' -or !$keys.Add($item.dat_key)) { throw 'Invalid or duplicate reviewed key.' }
    if ($item.action -cne 'translate') { throw 'Only explicit translation decisions are supported by this preflight.' }
}
$timer = [Diagnostics.Stopwatch]::StartNew()
$dat = [LotroTrGemini.TurbineDat]::new()
try {
    $dat.Open((Get-Item -LiteralPath $SourceDat).FullName, $false)
    $dat.BuildEntryIndex()
    foreach ($group in ($items | Group-Object { $_.dat_key.Substring(0,8) })) {
        $did = [Convert]::ToInt32($group.Name,16)
        $entry = [LotroTrGemini.DatEntry]::new()
        if (!$dat.TryGetEntry($did,[ref]$entry)) { throw "Missing DID: $($group.Name)" }
        $payload = [LotroTrGemini.TurbineDat]::MaybeDecompress($dat.ReadRaw($entry))
        $bin = [LotroTrGemini.LocBin]::Parse($payload,$did)
        $position = [long]0
        $records = @{}
        foreach ($record in $bin.GetCatalogRecords($did,[ref]$position)) { $records.Add($record.Key,$record) }
        $rows = $bin.GetRows($did)
        $byKey = @{}
        foreach ($row in $rows) { $byKey.Add($row.Key,$row) }
        if ([Convert]::ToBase64String($bin.Rebuild($rows)) -cne [Convert]::ToBase64String($payload)) { throw 'Source identity round-trip mismatch.' }
        foreach ($item in $group.Group) {
            $record = $records[$item.dat_key]
            if (!$record -or ![LotroTrGemini.SourceDigest]::Matches($record.SourceDigest,$item.source_digest) -or
                $record.TokenSignature -ine $item.token_signature) { throw "Source proof mismatch: $($item.dat_key)" }
            $format = [LotroTrGemini.ProtectedFormat]::Validate($record.Source,$item.target)
            if (!$format.IsValid) { throw "Protected format mismatch: $($item.dat_key): $($format.Reason)" }
            $byKey[$item.dat_key].Translation = $item.target
        }
        $rebuilt = $bin.Rebuild($rows)
        $verified = [LotroTrGemini.LocBin]::Parse($rebuilt,$did).GetRows($did)
        if ($verified.Count -ne $rows.Count) { throw 'Row count changed.' }
        for ($i = 0; $i -lt $rows.Count; $i++) {
            $expected = if ($null -eq $rows[$i].Translation) { $rows[$i].Original } else { $rows[$i].Translation }
            if ($verified[$i].Key -cne $rows[$i].Key -or $verified[$i].Original -cne $expected) { throw 'Rebuilt row mismatch.' }
        }
    }
}
finally { $dat.Dispose() }
"REVIEWED_PREFLIGHT_PASS|decisions=$($items.Count)|seconds=$([Math]::Round($timer.Elapsed.TotalSeconds,3))|read-only=true|release-verified=false"
