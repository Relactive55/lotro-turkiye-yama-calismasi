param([Parameter(Mandatory=$true)][string]$Target)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$env:PYTHONUTF8 = '1'
$env:PIP_DISABLE_PIP_VERSION_CHECK = '1'

function Invoke-Checked([string]$File, [string[]]$Arguments, [string]$Status) {
    Write-Output $Status
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Status başarısız oldu (kod $LASTEXITCODE)."
    }
}

New-Item -ItemType Directory -Path $Target -Force | Out-Null
$python = Join-Path $Target 'venv\Scripts\python.exe'
$ready = Join-Path $Target 'lotro_gpu_ready.txt'

if ((Test-Path -LiteralPath $python) -and (Test-Path -LiteralPath $ready)) {
    & $python -c "import os,torch; os.add_dll_directory(os.path.join(os.path.dirname(torch.__file__),'lib')); import llama_cpp,huggingface_hub,transformers,sentencepiece,sacremoses" 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Output 'GPU bileşenleri zaten hazır.'
        exit 0
    }
}

$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($Target))
if ($drive.AvailableFreeSpace -lt 12GB) {
    $free = [Math]::Round($drive.AvailableFreeSpace / 1GB, 1)
    throw "GPU bileşenleri için en az 12 GB boş alan önerilir. Kullanılabilir: $free GB."
}

$uv = Join-Path $Target 'uv.exe'
if (-not (Test-Path -LiteralPath $uv)) {
    Write-Output 'GPU kurulumu: güvenli Python yöneticisi indiriliyor…'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $Target 'uv-download.zip'
    $extract = Join-Path $Target 'uv-download'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip' -OutFile $zip
    if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $extract)
    $downloaded = Get-ChildItem -LiteralPath $extract -Filter 'uv.exe' -Recurse | Select-Object -First 1
    if (-not $downloaded) { throw 'İndirilen UV paketinde uv.exe bulunamadı.' }
    Copy-Item -LiteralPath $downloaded.FullName -Destination $uv -Force
    Remove-Item -LiteralPath $zip -Force
    Remove-Item -LiteralPath $extract -Recurse -Force
}

Invoke-Checked -File $uv -Arguments @('venv', (Join-Path $Target 'venv'), '--python', '3.11', '--seed') -Status 'GPU kurulumu: Python 3.11 hazırlanıyor…'
Invoke-Checked -File $python -Arguments @('-m','pip','install','--disable-pip-version-check','torch==2.5.1','--index-url','https://download.pytorch.org/whl/cu121') -Status 'GPU kurulumu: NVIDIA PyTorch indiriliyor…'
Invoke-Checked -File $uv -Arguments @('pip','install','--python',$python,'huggingface-hub','llama-cpp-python','transformers==4.57.6','sentencepiece','sacremoses','--extra-index-url','https://abetlen.github.io/llama-cpp-python/whl/cu125') -Status 'GPU kurulumu: OPUS ve Qwen kalite bileşenleri indiriliyor…'

Write-Output 'GPU kurulumu doğrulanıyor…'
& $python -c "import os,torch; os.add_dll_directory(os.path.join(os.path.dirname(torch.__file__),'lib')); import llama_cpp,huggingface_hub,transformers,sentencepiece,sacremoses; print('CUDA='+str(torch.cuda.is_available()))"
if ($LASTEXITCODE -ne 0) { throw 'GPU bileşenleri kuruldu ancak Python doğrulaması başarısız oldu.' }

[IO.File]::WriteAllText($ready, ('lotro-opus-qwen-quality-v1' + [Environment]::NewLine + [DateTime]::UtcNow.ToString('o')), [Text.UTF8Encoding]::new($false))
Write-Output 'GPU bileşenleri hazır. OPUS ve Qwen modelleri ilk çeviride otomatik indirilecek.'
