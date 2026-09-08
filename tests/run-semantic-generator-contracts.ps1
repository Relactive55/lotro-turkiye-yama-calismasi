[CmdletBinding()]
param([string]$DotnetPath)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$sdk = if ($DotnetPath) { (Get-Item -LiteralPath $DotnetPath -ErrorAction Stop).FullName } else { (Get-Command dotnet -ErrorAction Stop).Source }
$project = Join-Path $repo 'developer-tool\automation\SemanticPatchGenerator.Contracts\SemanticPatchGenerator.Contracts.csproj'
$previousAppData = $env:APPDATA
$taskAppData = Join-Path $repo 'work\nuget-appdata'
try {
    New-Item -ItemType Directory -Path (Join-Path $taskAppData 'NuGet') -Force | Out-Null
    $env:APPDATA = $taskAppData
    & $sdk build $project -c Release --ignore-failed-sources -p:RestoreIgnoreFailedSources=true
    if ($LASTEXITCODE -ne 0) { throw 'Semantic generator contract build failed.' }
    & (Join-Path (Split-Path -Parent $project) 'bin\Release\net472\SemanticPatchGenerator.Contracts.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Semantic generator contracts failed.' }
}
finally {
    $env:APPDATA = $previousAppData
}
