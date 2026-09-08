[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceDat,
    [Parameter(Mandatory)][string]$CandidateDat,
    [string]$GeneratorAssembly = (Join-Path $PSScriptRoot '../developer-tool/automation/SemanticPatchGenerator/bin/Release/net472/SemanticPatchGenerator.exe')
)
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::LoadFrom((Get-Item -LiteralPath $GeneratorAssembly).FullName)
# Screenshot regression: this table's final bytes are parameter metadata, not
# translated text. Read physical bytes independently of TurbineDat.ReadRaw.
function Read-NpcBoundary([string]$Path) {
    $dat = [LotroTrGemini.TurbineDat]::new()
    $stream = $null
    $reader = $null
    try {
        $dat.Open((Get-Item -LiteralPath $Path).FullName,$false)
        if (!$dat.UsesModernStorage) { throw 'Expected modern DAT storage.' }
        $entry = [LotroTrGemini.DatEntry]::new()
        if (!$dat.TryGetEntry(0x250047CF,[ref]$entry)) { throw 'Screenshot NPC table missing.' }
        $stream = [IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        $reader = [IO.BinaryReader]::new($stream)
        $stream.Position = $entry.Offset
        if ($reader.ReadUInt32() -ne 0 -or $reader.ReadUInt32() -ne 0) { throw 'Expected contiguous native header.' }
        $prefix = $reader.ReadBytes(8)
        if ([BitConverter]::ToUInt32($prefix,0) -ne 0x250047CF) { throw 'Native payload DID missing.' }
        $stream.Position = [long]$entry.Offset + 8 + $entry.Size - 4
        $tail = $reader.ReadBytes(4)
        if ($tail.Length -ne 4) { throw 'Truncated native payload.' }
        return ([Convert]::ToBase64String($prefix) + ':' + [Convert]::ToBase64String($tail))
    }
    finally {
        if ($reader) { $reader.Dispose() } elseif ($stream) { $stream.Dispose() }
        $dat.Dispose()
    }
}
$original = Read-NpcBoundary $SourceDat
$candidate = Read-NpcBoundary $CandidateDat
if ($original -cne $candidate) { throw 'NPC native prefix/footer changed: four-byte truncation regression.' }
'NATIVE_NPC_BOUNDARY_PASS|DID=250047CF|independent-physical-read=true|read-only=true'
