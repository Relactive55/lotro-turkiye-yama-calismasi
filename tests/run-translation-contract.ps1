[CmdletBinding()]
param(
    [string]$PythonPath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$pythonExecutable = $null
$pythonPrefix = @()
$pythonVersion = $null

function Test-PythonCandidate {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [string[]]$PrefixArguments = @()
    )
    try {
        $versionText = (& $Executable @PrefixArguments '--version' 2>&1 | Out-String).Trim()
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0 -or $versionText -notmatch 'Python\s+(\d+\.\d+\.\d+)') { return $false }
        $candidateVersion = [Version]$Matches[1]
        if ($candidateVersion -lt [Version]'3.11.0') { return $false }
        $script:pythonExecutable = $Executable
        $script:pythonPrefix = @($PrefixArguments)
        $script:pythonVersion = $candidateVersion.ToString()
        return $true
    }
    catch {
        return $false
    }
}

if ($PythonPath) {
    $explicit = Get-Command $PythonPath -ErrorAction SilentlyContinue
    if (-not $explicit -and (Test-Path -LiteralPath $PythonPath -PathType Leaf)) {
        $explicit = Get-Item -LiteralPath $PythonPath
    }
    if (-not $explicit) { throw "Belirtilen Python yolu/komutu bulunamadı: $PythonPath" }
    $explicitExecutable = if ($explicit.PSObject.Properties['Source'] -and $explicit.Source) { $explicit.Source } else { $explicit.FullName }
    [void](Test-PythonCandidate -Executable $explicitExecutable)
} else {
    $repoLocal = Join-Path $repo '.venv\Scripts\python.exe'
    if (Test-Path -LiteralPath $repoLocal -PathType Leaf) {
        [void](Test-PythonCandidate -Executable $repoLocal)
    }
    if (-not $pythonExecutable) {
        $lotroRoot = Split-Path -Parent (Split-Path -Parent $repo)
        $lotroLocal = Join-Path $lotroRoot '.venv\Scripts\python.exe'
        if (Test-Path -LiteralPath $lotroLocal -PathType Leaf) {
            [void](Test-PythonCandidate -Executable $lotroLocal)
        }
    }
    if (-not $pythonExecutable) {
        $py = Get-Command 'py' -ErrorAction SilentlyContinue
        if ($py) { [void](Test-PythonCandidate -Executable $py.Source -PrefixArguments @('-3.11')) }
    }
    if (-not $pythonExecutable) {
        foreach ($commandName in @('python', 'python3')) {
            $command = Get-Command $commandName -ErrorAction SilentlyContinue
            if ($command -and (Test-PythonCandidate -Executable $command.Source)) { break }
        }
    }
}
if (-not $pythonExecutable) {
    throw 'Doğrulanmış Python 3.11+ bulunamadı. Sıra: repo .venv, py -3.11, python, python3; gerekirse -PythonPath verin.'
}
$runtimeSuffix = if ($pythonPrefix.Count -gt 0) { "|args=" + ($pythonPrefix -join ',') } else { '' }
Write-Output ("PYTHON_RUNTIME|version=" + $pythonVersion + "|path=" + $pythonExecutable + $runtimeSuffix)
$fixture = Join-Path $repo 'tests\fixtures\translation-input.example.json'
$context = Join-Path $repo 'tests\fixtures\translation-context.example.json'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('lotro-translation-contract-' + [guid]::NewGuid().ToString('N'))
$output = Join-Path $tempRoot 'output.jsonl'
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    & $pythonExecutable @pythonPrefix (Join-Path $repo 'developer-tool\automation\translation_pipeline.py') --provider noop --input $fixture --context $context --output $output
    if ($LASTEXITCODE -ne 0) { throw 'Translation pipeline contract failed.' }
    $lines = @(Get-Content -LiteralPath $output -Encoding UTF8 | Where-Object { $_.Trim() })
    if ($lines.Count -ne 1) { throw 'Translation pipeline fixture count mismatch.' }
    $row = $lines[0] | ConvertFrom-Json
    if ($row.translation_status -ne 'UNTRANSLATED' -or $row.target -ne '') { throw 'Noop provider must fail closed to UNTRANSLATED.' }
    if ($null -ne $row.english -or $null -ne $row.source) { throw 'Candidate output must not contain raw English source.' }
    Write-Output 'TRANSLATION_CONTRACT_PASS'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
