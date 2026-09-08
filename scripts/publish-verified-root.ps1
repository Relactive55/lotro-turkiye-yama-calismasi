[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$UpdaterExe,
    [Parameter(Mandatory)][string]$NotesFile,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$CommitSha,
    [string]$GhPath = 'gh',
    [switch]$Publish,
    [switch]$ValidateOnly
)

# Only this allowlist is uploaded, never the directory or its private DAT.
# A failed transfer leaves a draft. Re-running resumes only matching drafts.
$ErrorActionPreference = 'Stop'
$repository = 'Relactive55/lotro-turkiye-yama-calismasi'
$directory = (Get-Item -LiteralPath $PackageDirectory).FullName
$exe = (Get-Item -LiteralPath $UpdaterExe).FullName
$notes = (Get-Item -LiteralPath $NotesFile).FullName
[void][Reflection.Assembly]::LoadFrom($exe)
Add-Type -AssemblyName System.Web.Extensions
$json = [System.Web.Script.Serialization.JavaScriptSerializer]::new()
$json.MaxJsonLength = [int]::MaxValue
$manifest = $json.Deserialize([IO.File]::ReadAllText((Join-Path $directory 'manifest-template.json')), [LotroTurkceYama.Setup.ReleaseManifest])
$proof = Get-Content -LiteralPath (Join-Path $directory 'verification.json') -Raw -Encoding UTF8 | ConvertFrom-Json
# Earlier producers validated a four-byte-shifted view with the same buggy
# reader/writer. Matching hashes alone cannot rehabilitate those candidates.
if ($manifest.patch_generator_version -cne 'semantic-generator-v4-native-framing' -or
    [Version]$manifest.minimum_updater_version -lt [Version]'1.3.0.0') {
    throw 'Obsolete DAT framing producer; rebuild a native-framing root before publication.'
}
if ($manifest.release_id -ne 0 -or $manifest.asset_id -ne 0 -or $manifest.patch_mode -cne 'full' -or
    $manifest.chain_depth -ne 0 -or $proof.status -cne 'VERIFIED_ROOT' -or !$proof.source_unchanged -or
    $proof.applied -ne $manifest.safe_translated_count -or $proof.candidate_sha256 -ine $manifest.candidate_dat_sha256 -or
    $proof.candidate_catalog_sha256 -ine $manifest.candidate_catalog_sha256 -or $proof.candidate_size -ne $manifest.candidate_dat_size) {
    throw 'Verified root evidence does not agree with its unfinished template.'
}
if ($manifest.release_tag -cnotmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$' -or
    $manifest.asset_name -cnotmatch '^lotro-turkce-yama-[A-Za-z0-9][A-Za-z0-9._-]{0,110}$' -or
    !($manifest.asset_name.EndsWith('.semantic.json',[StringComparison]::Ordinal) -or
      $manifest.asset_name.EndsWith('.semantic.json.gz',[StringComparison]::Ordinal))) { throw 'Unsafe semantic asset name.' }
$asset = Join-Path $directory $manifest.asset_name
$candidate = Join-Path $directory 'private-candidate.dat'
if ((Get-Item -LiteralPath $asset).Length -ne $manifest.asset_size -or
    (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash -ine $manifest.asset_sha256 -or
    (Get-Item -LiteralPath $candidate).Length -ne $manifest.candidate_dat_size -or
    (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ine $manifest.candidate_dat_sha256) { throw 'Local verified output changed.' }
if ([Version][Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -lt [Version]$manifest.minimum_updater_version) {
    throw 'Updater executable is older than the manifest requirement.'
}
# Validate all non-binding runtime fields before any remote write. These local
# fixture IDs are never saved or used to publish; actual IDs are bound below.
$contract = $json.Deserialize($json.Serialize($manifest),[LotroTurkceYama.Setup.ReleaseManifest])
$contract.release_id = 1
$contract.asset_id = 2
$contractRelease = [LotroTurkceYama.Setup.StableRelease]::new()
$contractRelease.id = 1
$contractRelease.tag_name = $manifest.release_tag
$contractAsset = [LotroTurkceYama.Setup.ReleaseAsset]::new()
$contractAsset.id = 3
[LotroTurkceYama.Setup.ManifestValidator]::Validate($contract,$contractRelease,$contractAsset)
if ($ValidateOnly) { 'LOCAL_RELEASE_PREFLIGHT_PASS|network=false|published=false'; return }

function Invoke-GhJson([string[]]$Arguments) {
    $response = & $GhPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'GitHub operation failed; release remains unpublished.' }
    return ($response | ConvertFrom-Json)
}
$checks = @(Invoke-GhJson @('run','list','--repo',$repository,'--commit',$CommitSha,'--workflow','validate.yml','--limit','1','--json','status,conclusion'))
if ($checks.Count -ne 1 -or $checks[0].status -ne 'completed' -or $checks[0].conclusion -ne 'success') {
    throw 'Successful repository validation for this exact commit is required before upload.'
}
$tag = $manifest.release_tag
$releases = @(Invoke-GhJson @('api',"repos/$repository/releases?per_page=100"))
$release = @($releases | Where-Object { $_.tag_name -ceq $tag })
if ($release.Count -gt 1) { throw 'Ambiguous release.' }
if ($release.Count -eq 0) {
    & $GhPath release create $tag --repo $repository --draft --target $CommitSha --title "LOTR TÜRKÇE YAMA $($manifest.patch_version)" --notes-file $notes
    if ($LASTEXITCODE -ne 0) { throw 'Draft creation failed.' }
    # The tag endpoint returns 404 for unpublished drafts. Resolve the
    # authenticated release list, then use the returned numeric ID.
    $created = @(Invoke-GhJson @('api',"repos/$repository/releases?per_page=100") | Where-Object { $_.tag_name -ceq $tag })
    if ($created.Count -ne 1) { throw 'Created draft could not be uniquely resolved.' }
    $release = $created[0]
} else { $release = $release[0] }
if (!$release.draft -or $release.target_commitish -ine $CommitSha) { throw 'Only a matching draft may be resumed; public releases are never overwritten.' }

function Ensure-Asset([string]$Path, [string]$Name, [string]$ExpectedSha, [long]$Size) {
    $current = Invoke-GhJson @('api',"repos/$repository/releases/$($release.id)")
    $matches = @($current.assets | Where-Object { $_.name -ceq $Name })
    if (!$matches.Count) {
        & $GhPath release upload $tag "$Path#$Name" --repo $repository
        if ($LASTEXITCODE -ne 0) { throw 'Asset upload failed; draft retained for safe retry.' }
        $current = Invoke-GhJson @('api',"repos/$repository/releases/$($release.id)")
        $matches = @($current.assets | Where-Object { $_.name -ceq $Name })
    }
    if ($matches.Count -ne 1 -or $matches[0].size -ne $Size -or $matches[0].digest -ine "sha256:$ExpectedSha") {
        throw "GitHub asset verification failed: $Name"
    }
    return $matches[0]
}
$semantic = Ensure-Asset $asset $manifest.asset_name $manifest.asset_sha256 $manifest.asset_size
$updaterName = 'LOTRO_Turkce_Yama_Setup.exe'
[void](Ensure-Asset $exe $updaterName (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash (Get-Item -LiteralPath $exe).Length)
$manifest.release_id = $release.id
$manifest.asset_id = $semantic.id
$releaseContract = [LotroTurkceYama.Setup.StableRelease]::new()
$releaseContract.id = $release.id
$releaseContract.tag_name = $tag
$manifestAsset = [LotroTurkceYama.Setup.ReleaseAsset]::new()
$manifestAsset.id = -1
[LotroTurkceYama.Setup.ManifestValidator]::Validate($manifest,$releaseContract,$manifestAsset)
$manifestPath = Join-Path $directory 'manifest.json'
[IO.File]::WriteAllText($manifestPath,$json.Serialize($manifest),[Text.UTF8Encoding]::new($false))
[void](Ensure-Asset $manifestPath 'manifest.json' (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash (Get-Item -LiteralPath $manifestPath).Length)
$final = Invoke-GhJson @('api',"repos/$repository/releases/$($release.id)")
$allowed = @($manifest.asset_name,$updaterName,'manifest.json')
if (@($final.assets).Count -ne 3 -or @($final.assets | Where-Object { $_.name -cnotin $allowed }).Count) {
    throw 'Unexpected release assets; refuse publication.'
}
if ($Publish) {
    & $GhPath release edit $tag --repo $repository --draft=false --latest
    if ($LASTEXITCODE -ne 0) { throw 'Publication failed; inspect draft state before retry.' }
}
$confirmed = Invoke-GhJson @('api',"repos/$repository/releases/$($release.id)")
if ($Publish -and $confirmed.draft) { throw 'Publication was not confirmed.' }
"VERIFIED_RELEASE|url=$($confirmed.html_url)|draft=$($confirmed.draft)|assets=3"
