[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$UpdaterAssemblyPath)

$ErrorActionPreference = 'Stop'
$null = Add-Type -AssemblyName System.Web.Extensions
$asm = [Reflection.Assembly]::LoadFile((Resolve-Path -LiteralPath $UpdaterAssemblyPath).Path)
$transport = $asm.GetType('LotroTurkceYama.Setup.FixedGitHubTransport')
$pathValidator = $asm.GetType('LotroTurkceYama.Setup.LotroPathValidator')
$manifestValidator = $asm.GetType('LotroTurkceYama.Setup.ManifestValidator')
$manifestType = $asm.GetType('LotroTurkceYama.Setup.ReleaseManifest')
$releaseType = $asm.GetType('LotroTurkceYama.Setup.StableRelease')
$assetType = $asm.GetType('LotroTurkceYama.Setup.ReleaseAsset')
$updaterType = $asm.GetType('LotroTurkceYama.Setup.LotroReleaseUpdater')
$failureType = $asm.GetType('LotroTurkceYama.Setup.UpdaterFailure')
$passed = 0

function Pass([string]$name) { $script:passed++; Write-Output "PASS $name" }
function Expect-Code([scriptblock]$action, [string]$expected, [string]$name) {
    try { & $action; throw "Beklenen hata oluşmadı: $name" }
    catch {
        $inner = $_.Exception.InnerException
        if ($inner -and $inner.GetType().FullName -eq $failureType.FullName -and $inner.Code -eq $expected) { Pass $name; return }
        if ($_.Exception.GetType().FullName -eq $failureType.FullName -and $_.Exception.Code -eq $expected) { Pass $name; return }
        throw
    }
}

$transport.GetMethod('ValidateUri').Invoke($null, @([Uri]'https://api.github.com/repos/Relactive/lotro-turkiye-yama-calismasi/releases/latest'))
Pass 'allowlisted HTTPS endpoint'
 $transport.GetMethod('ValidateReleaseAssetUri').Invoke($null, @([Uri]'https://github.com/Relactive/lotro-turkiye-yama-calismasi/releases/download/patch-1/manifest.json', 'patch-1', 'manifest.json'))
 Pass 'release asset owner/repository path'
Expect-Code { $transport.GetMethod('ValidateUri').Invoke($null, @([Uri]'http://api.github.com/x')) } 'ENDPOINT_REJECTED' 'HTTP downgrade rejected'
Expect-Code { $transport.GetMethod('ValidateUri').Invoke($null, @([Uri]'https://evil.example/x')) } 'ENDPOINT_REJECTED' 'non-allowlisted host rejected'
Expect-Code { $transport.GetMethod('ValidateReleaseAssetUri').Invoke($null, @([Uri]'https://github.com/other/repo/releases/download/patch-1/manifest.json', 'patch-1', 'manifest.json')) } 'ENDPOINT_REJECTED' 'wrong release repository rejected'

$safe = $updaterType.GetMethod('IsSafeFileName', [Reflection.BindingFlags]'NonPublic,Static')
if (-not $safe.Invoke($null, @('lotro-turkce-yama-1.dat'))) { throw 'Safe file name reddedildi.' }
if ($safe.Invoke($null, @('..\evil.dat'))) { throw 'Traversal file name kabul edildi.' }
Pass 'asset filename traversal guard'

$temp = Join-Path ([IO.Path]::GetTempPath()) ('lotro-contract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    Expect-Code { $pathValidator.GetMethod('Validate').Invoke($null, @([string](Join-Path $temp 'missing'))) } 'INVALID_LOTRO_DIRECTORY' 'invalid LOTRO directory rejected'
    [IO.File]::WriteAllBytes((Join-Path $temp 'client_local_English.dat'), [byte[]](1,2,3))
    [IO.File]::WriteAllBytes((Join-Path $temp 'LotroLauncher.exe'), [byte[]](0))
    $pathValidator.GetMethod('Validate').Invoke($null, @([string]$temp))
    Pass 'fake LOTRO directory accepted'
} finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }

$json = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$manifest = $json.Deserialize((Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'tests\fixtures\manifest-valid.example.json') -Raw), $manifestType)
$release = [Activator]::CreateInstance($releaseType)
$release.id = 1; $release.tag_name = 'patch-2026.09.05.1'; $release.draft = $false; $release.prerelease = $false
$assetManifest = [Activator]::CreateInstance($assetType); $assetManifest.id = 3; $assetManifest.name = 'manifest.json'; $assetManifest.size = 100; $assetManifest.browser_download_url = 'https://github.com/Relactive/lotro-turkiye-yama-calismasi/releases/download/patch-2026.09.05.1/manifest.json'
$assetPatch = [Activator]::CreateInstance($assetType); $assetPatch.id = 2; $assetPatch.name = $manifest.asset_name; $assetPatch.size = $manifest.asset_size; $assetPatch.browser_download_url = 'https://github.com/Relactive/lotro-turkiye-yama-calismasi/releases/download/patch-2026.09.05.1/' + $manifest.asset_name
$release.assets = @($assetManifest, $assetPatch)
$manifestValidator.GetMethod('Validate').Invoke($null, @($manifest, $release, $assetManifest))
Pass 'valid manifest identity'
$manifest.release_id = 999
Expect-Code { $manifestValidator.GetMethod('Validate').Invoke($null, @($manifest, $release, $assetManifest)) } 'MANIFEST_INVALID' 'manifest release mismatch rejected'

Write-Output "contract_tests_passed=$passed"
