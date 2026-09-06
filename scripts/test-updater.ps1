[CmdletBinding()]
param([string]$DotnetPath)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
# Avoid inheriting a denied roaming NuGet.Config on managed hosts. The local
# app-data folder is build-only and is excluded from the public source audit.
$previousAppData = $env:APPDATA
$taskAppData = Join-Path $repo 'work\nuget-appdata'
New-Item -ItemType Directory -Path (Join-Path $taskAppData 'NuGet') -Force | Out-Null
$env:APPDATA = $taskAppData
$sdk = if ($DotnetPath) {
    (Get-Item -LiteralPath $DotnetPath -ErrorAction Stop).FullName
} else {
    (Get-Command dotnet -ErrorAction SilentlyContinue).Source
}
if (-not $sdk) { throw 'dotnet SDK not found; tests were not run.' }
$out = Join-Path ([IO.Path]::GetTempPath()) ('lotro-setup-test-' + [Guid]::NewGuid().ToString('N'))
$obj = Join-Path $out 'obj\'
$bin = Join-Path $out 'bin\'
New-Item -ItemType Directory -Path $out -Force | Out-Null
try {
    & $sdk build (Join-Path $repo 'tests\LOTRO.Setup.Tests.csproj') -c Release --ignore-failed-sources -p:RestoreIgnoreFailedSources=true "-p:BaseIntermediateOutputPath=$obj" "-p:OutputPath=$bin"
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    & (Join-Path $bin 'LOTRO.Setup.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Updater/developer behavior tests failed.' }
}
finally {
    $env:APPDATA = $previousAppData
    if (Test-Path -LiteralPath $out) { [IO.Directory]::Delete([IO.Path]::GetFullPath($out), $true) }
}
