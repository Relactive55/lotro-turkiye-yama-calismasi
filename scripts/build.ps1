[CmdletBinding()]
param(
    [ValidateSet('DeveloperTool','SourceSender','SemanticGenerator','Updater','Tests','All')][string]$Project = 'All',
    [string]$DotnetPath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

function Set-LargeAddressAware {
    param([Parameter(Mandatory = $true)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        throw "Geçersiz PE dosyası: $Path"
    }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 24 -gt $bytes.Length -or $bytes[$peOffset] -ne 0x50 -or $bytes[$peOffset + 1] -ne 0x45 -or $bytes[$peOffset + 2] -ne 0 -or $bytes[$peOffset + 3] -ne 0) {
        throw "PE başlığı bulunamadı: $Path"
    }
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    if ($machine -ne 0x014C) { return }
    $characteristicsOffset = $peOffset + 22
    $characteristics = [BitConverter]::ToUInt16($bytes, $characteristicsOffset)
    if (($characteristics -band 0x20) -ne 0) { return }
    $updated = [uint16]($characteristics -bor 0x20)
    [Buffer]::BlockCopy([BitConverter]::GetBytes($updated), 0, $bytes, $characteristicsOffset, 2)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

# Some managed hosts deny the user's roaming NuGet.Config. Keep build-only
# configuration in the ignored work area so source builds remain repeatable;
# no credentials or source assets are written to the repository.
$previousAppData = $env:APPDATA
$taskAppData = Join-Path $repo 'work\nuget-appdata'
try {
    New-Item -ItemType Directory -Path (Join-Path $taskAppData 'NuGet') -Force | Out-Null
    $env:APPDATA = $taskAppData
    $dotnet = if ($DotnetPath) {
        (Get-Item -LiteralPath $DotnetPath -ErrorAction Stop).FullName
    } else {
        (Get-Command dotnet -ErrorAction SilentlyContinue).Source
    }
    if (-not $dotnet) { throw 'dotnet SDK bulunamadı; build çalıştırılmadı.' }
    $projects = @()
    if ($Project -in @('DeveloperTool','All')) { $projects += Join-Path $repo 'developer-tool\LotroTrGemini.csproj' }
    if ($Project -in @('SourceSender','All')) { $projects += Join-Path $repo 'tools\SourceUpdateSender\SourceUpdateSender.csproj' }
    if ($Project -in @('SemanticGenerator','All')) { $projects += Join-Path $repo 'developer-tool\automation\SemanticPatchGenerator\SemanticPatchGenerator.csproj' }
    if ($Project -in @('Updater','All')) { $projects += Join-Path $repo 'updater\LOTRO.TurkceYama.Setup.csproj' }
    if ($Project -in @('Tests','All')) { $projects += Join-Path $repo 'tests\LOTRO.Setup.Tests.csproj' }
    foreach ($projectPath in $projects) {
        & $dotnet build $projectPath -c Release --ignore-failed-sources -p:RestoreIgnoreFailedSources=true
        if ($LASTEXITCODE -ne 0) { throw "Build başarısız: $projectPath" }
    }

    # The native LOTRO reader is x86. These processes also hold the full
    # localization catalog, so allow them to use the 4-GB address space on
    # 64-bit Windows instead of failing near the default 2-GB ceiling.
    foreach ($largeAddressAwarePath in @(
        (Join-Path $repo 'developer-tool\bin\x86\Release\LOTRCEVIRI.exe'),
        (Join-Path $repo 'developer-tool\bin\x86\Release\LOTRKaynakGonder.exe')
    )) {
        if (Test-Path -LiteralPath $largeAddressAwarePath) {
            Set-LargeAddressAware -Path $largeAddressAwarePath
        }
    }
}
finally {
    $env:APPDATA = $previousAppData
}
