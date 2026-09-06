$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$script = Join-Path $root 'scripts\get-lotro-official-version.ps1'
$datacenter = Join-Path $PSScriptRoot 'fixtures\lotro-datacenter-response.xml'
$launcher = Join-Path $PSScriptRoot 'fixtures\lotro-launcher-server-config.xml'

$actual = & $script -DatacenterResponsePath $datacenter -LauncherConfigPath $launcher
if ($actual -ne '3601.0066.7272.4024') {
    throw "Beklenen resmi LOTRO sürümü alınamadı: $actual"
}

$invalid = Join-Path ([IO.Path]::GetTempPath()) ('lotro-invalid-launcher-' + [Guid]::NewGuid().ToString('N') + '.xml')
try {
    Set-Content -LiteralPath $invalid -Encoding UTF8 -Value '<configuration><appSettings /></configuration>'
    $failedClosed = $false
    try {
        & $script -DatacenterResponsePath $datacenter -LauncherConfigPath $invalid 2>$null | Out-Null
    }
    catch {
        $failedClosed = $true
    }
    if (-not $failedClosed) { throw 'Sürümsüz LOTRO yanıtı fail-closed reddedilmedi.' }
}
finally {
    Remove-Item -LiteralPath $invalid -Force -ErrorAction SilentlyContinue
}

Write-Output 'Official LOTRO version detector tests: PASS'
