[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Owner = 'Relactive55',
    [string]$Repository = 'lotro-turkiye-yama-calismasi'
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Get-Item -LiteralPath $RepoRoot -ErrorAction Stop).FullName.TrimEnd([IO.Path]::DirectorySeparatorChar)
$binaryExtensions = @('.zip', '.exe', '.msi', '.7z', '.rar', '.bat', '.cmd', '.ps1', '.sh', '.jar', '.dll', '.dat', '.datx')
$redirectHosts = @('bit.ly', 'tinyurl.com', 't.co', 'goo.gl', 'is.gd', 'cutt.ly', 'lnkd.in', 'shorturl.at', 'drive.google.com', 'dropbox.com', 'mega.nz', 'mediafire.com', 'discord.gg', 'pastebin.com')
$urlPattern = [regex]'https?://[^\s<>"''`()\[\]]+'
$findings = New-Object System.Collections.Generic.List[object]
$gitCommand = (Get-Command git.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($gitCommand)) {
    $gitCandidate = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Git\cmd\git.exe'
    if (Test-Path -LiteralPath $gitCandidate) { $gitCommand = $gitCandidate }
}
if ([string]::IsNullOrWhiteSpace($gitCommand)) { throw 'Git bulunamadı; public indirme bağlantısı denetimi çalıştırılamadı.' }

function Add-Finding([string]$File, [int]$Line, [string]$Url, [string]$Reason) {
    $findings.Add([ordered]@{ file = $File; line = $Line; url = $Url; reason = $Reason })
}

function Is-GitHubReleaseUrl([Uri]$Uri) {
    if ($Uri.Scheme -ne 'https') { return $false }
    if ($Uri.Host -ieq 'objects.githubusercontent.com') { return $true }
    if ($Uri.Host -ine 'github.com') { return $false }
    return $Uri.AbsolutePath -match '^/[^/]+/[^/]+/releases(?:/|$)'
}

# Disable Git's octal path quoting so folders such as `ORJİNAL DAT` are
# passed to PowerShell as their real Unicode paths.
$tracked = @(& $gitCommand -c core.quotePath=false -C $RepoRoot ls-files)
foreach ($relative in $tracked) {
    if ([string]::IsNullOrWhiteSpace($relative)) { continue }
    $path = Join-Path $RepoRoot $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    $info = Get-Item -LiteralPath $path
    if ($info.Length -gt 2MB) { continue }

    $bytes = [IO.File]::ReadAllBytes($path)
    if ($bytes -contains 0) { continue }
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    $lines = $text -split "`r?`n"
    for ($index = 0; $index -lt $lines.Count; $index++) {
        foreach ($match in $urlPattern.Matches($lines[$index])) {
            $raw = $match.Value.TrimEnd('.', ',', ';', ':', '!', '?')
            try { $uri = [Uri]$raw } catch { continue }
            $extension = [IO.Path]::GetExtension($uri.AbsolutePath).ToLowerInvariant()
            $urlHost = $uri.Host.ToLowerInvariant()
            $relativeName = $relative -replace '\\', '/'
            if ($redirectHosts -contains $urlHost) {
                Add-Finding $relativeName ($index + 1) $raw 'redirect-or-file-host-not-allowed'
                continue
            }
            if ($binaryExtensions -contains $extension -and -not (Is-GitHubReleaseUrl $uri)) {
                Add-Finding $relativeName ($index + 1) $raw 'binary-download-must-use-github-release-asset'
            }
        }
    }
}

$findingArray = @($findings | ForEach-Object { $_ })
$result = [ordered]@{
    repository = "$Owner/$Repository"
    scanned_files = $tracked.Count
    finding_count = $findings.Count
    findings = $findingArray
    clean = ($findings.Count -eq 0)
}
$result | ConvertTo-Json -Depth 8
if ($findings.Count -gt 0) { exit 2 }
