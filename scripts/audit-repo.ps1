[CmdletBinding()]
param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$RepoRoot = (Get-Item -LiteralPath $RepoRoot -ErrorAction Stop).FullName.TrimEnd([IO.Path]::DirectorySeparatorChar)
$forbiddenExtensions = @('.dat','.datx','.rar','.7z','.zip','.gguf','.part','.tmp','.log','.exe','.dll','.pdb','.jsonl','.building')
$forbiddenNames = @('raw-localization','private-catalog','models','model-cache','gpu_runtime','gpu-cache','venv','bin','obj','out','work','temp','tmp','cache','staging','backup','backups','test-output','installed_patch.json')
$forbiddenNamePatterns = @('^gpu_job_', '^gpu_quality_log\.tsv$', '^gpu_runtime_path\.txt$', '^lotro_gpu_ready\.txt$', '^YAZIM_RAPOR\.txt$', '_fail\.txt$', '\.(jsonl|bin)\.gz$')
$absolutePathPattern = '((?<![A-Za-z0-9_])[A-Za-z]:\\|(?i:/Users/|/home/|BEGIN (RSA|OPENSSH|EC) PRIVATE KEY|ghp_[A-Za-z0-9]|github_pat_[A-Za-z0-9]))'

# Build outputs and local package caches are intentionally outside the public
# source audit. Prune them before descent: a local SDK or DAT workspace can
# contain tens of thousands of files that this audit must never enumerate.
$excludedDirectories = @('.git', 'bin', 'obj', 'work', '__pycache__')
$pendingDirectories = New-Object 'System.Collections.Generic.Stack[string]'
$pendingDirectories.Push($RepoRoot)
$files = New-Object 'System.Collections.Generic.List[System.IO.FileInfo]'
while ($pendingDirectories.Count -gt 0) {
    foreach ($item in Get-ChildItem -LiteralPath $pendingDirectories.Pop() -Force) {
        if ($item.PSIsContainer) {
            if ($item.Name -notin $excludedDirectories -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
                $pendingDirectories.Push($item.FullName)
            }
        } else {
            $files.Add($item)
        }
    }
}
$bad = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    if ($forbiddenExtensions -contains $file.Extension.ToLowerInvariant()) { $bad.Add("forbidden-extension: $($file.FullName)") }
    if ($forbiddenNames -contains $file.Name.ToLowerInvariant() -or $forbiddenNames -contains $file.Directory.Name.ToLowerInvariant()) { $bad.Add("forbidden-directory-or-name: $($file.FullName)") }
    foreach ($pattern in $forbiddenNamePatterns) { if ($file.Name -match $pattern) { $bad.Add("forbidden-local-artifact: $($file.FullName)"); break } }
    if ($file.Length -le 10MB -and $file.Extension -notin @('.exe','.dll','.pdb','.dat','.datx','.rar','.7z','.zip','.gguf','.gz') -and $file.FullName -ne $PSCommandPath) {
        $text = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($text -and $text -match $absolutePathPattern) { $bad.Add("absolute-path-or-secret: $($file.FullName)") }
        # Cache metadata may contain exact-term source text as well. A schema
        # describing cache_kind is allowed; an actual cache document is local.
        if ($file.Extension -eq '.json' -and $text -match '"cache_kind"\s*:\s*"catalog_record_cache"') {
            $bad.Add("forbidden-source-cache-metadata: $($file.FullName)")
        }
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
