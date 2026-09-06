[CmdletBinding()]
param(
    [ValidateSet('DeveloperTool','Updater','Tests','All')][string]$Project = 'All',
    [string]$DotnetPath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
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
    if ($Project -in @('Updater','All')) { $projects += Join-Path $repo 'updater\LOTRO.TurkceYama.Setup.csproj' }
    if ($Project -in @('Tests','All')) { $projects += Join-Path $repo 'tests\LOTRO.Setup.Tests.csproj' }
    foreach ($projectPath in $projects) {
        & $dotnet build $projectPath -c Release --ignore-failed-sources -p:RestoreIgnoreFailedSources=true
        if ($LASTEXITCODE -ne 0) { throw "Build başarısız: $projectPath" }
    }
}
finally {
    $env:APPDATA = $previousAppData
}
