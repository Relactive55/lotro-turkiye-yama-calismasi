[CmdletBinding()]
param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$forbiddenExtensions = @('.dat','.datx','.rar','.7z','.zip','.gguf','.part','.tmp','.log','.exe','.dll','.pdb','.jsonl','.building')
$forbiddenNames = @('raw-localization','private-catalog','models','model-cache','gpu_runtime','gpu-cache','venv','bin','obj','out','work','temp','tmp','cache','staging','backup','backups','test-output','installed_patch.json')
$forbiddenNamePatterns = @('^gpu_job_', '^gpu_quality_log\.tsv$', '^gpu_runtime_path\.txt$', '^lotro_gpu_ready\.txt$', '^YAZIM_RAPOR\.txt$', '_fail\.txt$')
$absolutePathPattern = '((?<![A-Za-z0-9_])[A-Za-z]:\\|(?i:/Users/|/home/|BEGIN (RSA|OPENSSH|EC) PRIVATE KEY|ghp_[A-Za-z0-9]|github_pat_[A-Za-z0-9]))'

# Build outputs and local package caches are intentionally outside the public
# source audit. They are reproducible and must never be copied into a release.
$files = @(Get-ChildItem -LiteralPath $RepoRoot -Recurse -Force -File | Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj|work|__pycache__)\\' })
$bad = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    if ($forbiddenExtensions -contains $file.Extension.ToLowerInvariant()) { $bad.Add("forbidden-extension: $($file.FullName)") }
    if ($forbiddenNames -contains $file.Name.ToLowerInvariant() -or $forbiddenNames -contains $file.Directory.Name.ToLowerInvariant()) { $bad.Add("forbidden-directory-or-name: $($file.FullName)") }
    foreach ($pattern in $forbiddenNamePatterns) { if ($file.Name -match $pattern) { $bad.Add("forbidden-local-artifact: $($file.FullName)"); break } }
    if ($file.Length -le 10MB -and $file.Extension -notin @('.exe','.dll','.pdb','.dat','.datx','.rar','.7z','.zip','.gguf') -and $file.FullName -ne $PSCommandPath) {
        $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($text -and $text -match $absolutePathPattern) { $bad.Add("absolute-path-or-secret: $($file.FullName)") }
    }
}
$largest = $files | Sort-Object Length -Descending | Select-Object -First 10 FullName,Length
$result = [ordered]@{
    repo_root = [IO.Path]::GetFullPath($RepoRoot)
    file_count = $files.Count
    total_bytes = [int64](($files | Measure-Object Length -Sum).Sum)
    forbidden_count = $bad.Count
    forbidden = @($bad)
    largest_files = @($largest | ForEach-Object { [ordered]@{ path = $_.FullName.Substring($RepoRoot.Length).TrimStart('\'); bytes = [int64]$_.Length } })
    clean = ($bad.Count -eq 0)
}
$result | ConvertTo-Json -Depth 6
if ($bad.Count -gt 0) { exit 2 }
