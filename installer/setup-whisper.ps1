# Downloads Whisper (local speech recognition) for Simon Flow, once.
# Simon Flow still works without it, using Windows' basic speech recognition.
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$wd = Join-Path $env:LOCALAPPDATA 'SimonFlow\whisper'
New-Item -ItemType Directory -Force -Path $wd | Out-Null

function Get-File($urls, $dest) {
    foreach ($u in $urls) {
        try {
            & curl.exe -L --fail -s -o $dest $u
            if ($LASTEXITCODE -eq 0 -and (Test-Path $dest)) { return $true }
        } catch { }
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $u -OutFile $dest
            if (Test-Path $dest) { return $true }
        } catch { }
    }
    return $false
}

try {
    if (-not (Test-Path (Join-Path $wd 'bin'))) {
        $zip = Join-Path $wd 'whisper-bin.zip'
        if (Get-File @('https://github.com/ggml-org/whisper.cpp/releases/latest/download/whisper-bin-x64.zip',
                       'https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.2/whisper-bin-x64.zip') $zip) {
            Expand-Archive -Force -LiteralPath $zip -DestinationPath (Join-Path $wd 'bin')
            Remove-Item -Force $zip
        }
    }
    $model = Join-Path $wd 'ggml-base.en.bin'
    if (-not (Test-Path $model) -or (Get-Item $model).Length -lt 50000000) {
        $part = "$model.part"
        if (Get-File @('https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin') $part) {
            Move-Item -Force $part $model
        }
    }
} catch { }
exit 0
