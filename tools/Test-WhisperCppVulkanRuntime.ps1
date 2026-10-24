[CmdletBinding()]
param(
    # De runtime-map in de gegevensmap van de app (<gegevensmap>\runtime\whispercpp). Verplicht: install-prereqs.ps1 geeft
    # hem mee, zodat dit script nooit de runtime van een andere Captivox-installatie test of aanpast.
    [string]$RuntimeRoot = '',
    [string]$FfmpegPath = '',
    [string]$ModelPath = '',
    [switch]$NoDownload,
    [switch]$AllowModelFallback
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) {
    throw 'Geef -RuntimeRoot op: de map runtime\whispercpp in de gegevensmap van de app (install-prereqs.ps1 doet dat zelf).'
}
$RuntimeRoot = [System.IO.Path]::GetFullPath($RuntimeRoot)
if ($PSVersionTable.PSVersion.Major -ge 7) { $PSNativeCommandUseErrorActionPreference = $false }

function Resolve-CommandPath([string]$Name, [string]$ExplicitPath = '') {
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath) -and (Test-Path -LiteralPath $ExplicitPath)) { return (Resolve-Path -LiteralPath $ExplicitPath).Path }
    $cmd = Get-Command $Name -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) { return $cmd.Source }
    return ''
}

function ConvertTo-RelativeFfmpegFilterValue([string]$Path, [string]$Root) {
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([char[]]@([char]'\', [char]'/')) + [System.IO.Path]::DirectorySeparatorChar
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    if (-not $pathFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Filterpad ligt niet onder RuntimeRoot en kan niet veilig relatief worden gemaakt: $Path"
    }
    $rel = $pathFull.Substring($rootFull.Length)
    $rel = $rel -replace '\\', '/'
    if ($rel.Contains('\')) { throw "Relatief FFmpeg filterpad bevat nog een backslash en is onveilig: $rel" }
    if ($rel.Contains(':')) { throw "Relatief FFmpeg filterpad bevat onverwacht ':' teken: $rel" }
    if ([string]::IsNullOrWhiteSpace($rel)) { throw "Relatief FFmpeg filterpad is leeg voor: $Path" }
    return $rel.Replace("'", "\'")
}

function New-TestWav([string]$Path) {
    $dir = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $sampleRate = 16000
    $seconds = 3
    $samples = $sampleRate * $seconds
    $bytesPerSample = 2
    $dataSize = $samples * $bytesPerSample
    $riffSize = 36 + $dataSize
    $fs = [System.IO.File]::Create($Path)
    try {
        $bw = New-Object System.IO.BinaryWriter($fs)
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('RIFF'))
        $bw.Write([int]$riffSize)
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('WAVE'))
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('fmt '))
        $bw.Write([int]16)
        $bw.Write([System.Int16]1)
        $bw.Write([System.Int16]1)
        $bw.Write([int]$sampleRate)
        $bw.Write([int]($sampleRate * $bytesPerSample))
        $bw.Write([System.Int16]$bytesPerSample)
        $bw.Write([System.Int16]16)
        $bw.Write([System.Text.Encoding]::ASCII.GetBytes('data'))
        $bw.Write([int]$dataSize)
        for ($i = 0; $i -lt $samples; $i++) {
            # zachte 440Hz toon, geen stilte. Dit dwingt de Whisper-filter door inference heen.
            $sample = [math]::Sin(2.0 * [math]::PI * 440.0 * $i / $sampleRate) * 1200.0
            $bw.Write([System.Int16]$sample)
        }
        $bw.Flush()
    }
    finally { $fs.Dispose() }
}

function Get-HuggingFaceWhisperCppModelUrl([string]$FileName) {
    if ([string]::IsNullOrWhiteSpace($FileName)) { throw 'Modelbestandsnaam ontbreekt.' }
    $encodedFileName = [System.Uri]::EscapeDataString($FileName)
    return "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/${encodedFileName}?download=true"
}

function Ensure-Model([string]$Path, [string]$FileName) {
    if (Test-Path -LiteralPath $Path) { return $true }
    if ($NoDownload) { return $false }
    $dir = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $url = Get-HuggingFaceWhisperCppModelUrl -FileName $FileName
    Write-Host "Model ontbreekt; downloaden: $FileName" -ForegroundColor Yellow
    Write-Host "Bron: $url" -ForegroundColor DarkGray
    $tmp = "$Path.download"
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    try {
        Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing -TimeoutSec 1800 -Headers @{ 'User-Agent' = 'CaptivoxRecorder-AMD-GPU-selftest' }
        if (-not (Test-Path -LiteralPath $tmp)) { return $false }
        $downloaded = Get-Item -LiteralPath $tmp -ErrorAction Stop
        if ($downloaded.Length -lt 1048576) {
            throw "Download is verdacht klein ($($downloaded.Length) bytes); dit is waarschijnlijk geen ggml-model."
        }
        Move-Item -LiteralPath $tmp -Destination $Path -Force
        return $true
    }
    catch {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        Write-Host "Download mislukt voor ${FileName}: $($_.Exception.Message)" -ForegroundColor Yellow
        return $false
    }
}

function Test-OneFfmpegWhisperModel([string]$Ffmpeg, [string]$RuntimeRoot, [string]$Model, [string]$Wav, [string]$TestRoot, [bool]$DisableCoopMat) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($Model)
    $outJson = Join-Path $TestRoot ("ffmpeg_whisper_selftest_$name.json")
    $stdout = Join-Path $TestRoot ("ffmpeg_whisper_stdout_$name.log")
    $stderr = Join-Path $TestRoot ("ffmpeg_whisper_stderr_$name.log")
    Remove-Item -LiteralPath $outJson -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stdout -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stderr -Force -ErrorAction SilentlyContinue

    $modelValue = ConvertTo-RelativeFfmpegFilterValue -Path $Model -Root $RuntimeRoot
    $destValue = ConvertTo-RelativeFfmpegFilterValue -Path $outJson -Root $RuntimeRoot
    $filter = "whisper=model=${modelValue}:language=nl:queue=3:use_gpu=true:gpu_device=0:destination=${destValue}:format=json"
    $args = @('-y','-nostdin','-hide_banner','-loglevel','info','-i',$Wav,'-vn','-af',$filter,'-f','null','-')

    Write-Host ''
    Write-Host "Test model: $([System.IO.Path]::GetFileName($Model)); disable_coopmat=$DisableCoopMat" -ForegroundColor Cyan
    Write-Host ("> $Ffmpeg $($args -join ' ')") -ForegroundColor DarkGray
    Write-Host "Werkmap voor FFmpeg-filterpaden: $RuntimeRoot" -ForegroundColor DarkGray

    $oldCoop = $env:GGML_VK_DISABLE_COOPMAT
    $oldVisible = $env:GGML_VK_VISIBLE_DEVICES
    try {
        if ($DisableCoopMat) { $env:GGML_VK_DISABLE_COOPMAT = '1' } else { Remove-Item Env:GGML_VK_DISABLE_COOPMAT -ErrorAction SilentlyContinue }
        $env:GGML_VK_VISIBLE_DEVICES = '0'
        $p = Start-Process -FilePath $Ffmpeg -ArgumentList $args -WorkingDirectory $RuntimeRoot -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    }
    finally {
        if ($null -ne $oldCoop) { $env:GGML_VK_DISABLE_COOPMAT = $oldCoop } else { Remove-Item Env:GGML_VK_DISABLE_COOPMAT -ErrorAction SilentlyContinue }
        if ($null -ne $oldVisible) { $env:GGML_VK_VISIBLE_DEVICES = $oldVisible } else { Remove-Item Env:GGML_VK_VISIBLE_DEVICES -ErrorAction SilentlyContinue }
    }

    $output = ''
    if (Test-Path -LiteralPath $stdout) { $output += Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $stderr) { $output += "`n" + (Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue) }
    $gpuConfirmed = $output -match '(?i)use gpu\s*=\s*1|ggml_vulkan|vulkan0|using vulkan|gpu_device\s*=\s*0'
    return [ordered]@{
        model = $Model
        model_file = [System.IO.Path]::GetFileName($Model)
        exit_code = [int]$p.ExitCode
        stdout = $stdout
        stderr = $stderr
        output_json = $outJson
        gpu_text_detected = [bool]$gpuConfirmed
        disable_coopmat = [bool]$DisableCoopMat
        output_tail = if ($output.Length -gt 3000) { $output.Substring($output.Length - 3000) } else { $output }
        success = ([int]$p.ExitCode -eq 0 -and [bool]$gpuConfirmed)
    }
}

Write-Host 'Captivox AMD/GPU runtime self-test' -ForegroundColor Cyan
Write-Host "RuntimeRoot: $RuntimeRoot"
New-Item -ItemType Directory -Force -Path $RuntimeRoot | Out-Null

$ffmpeg = Resolve-CommandPath -Name 'ffmpeg.exe' -ExplicitPath $FfmpegPath
if ([string]::IsNullOrWhiteSpace($ffmpeg)) { $ffmpeg = Resolve-CommandPath -Name 'ffmpeg' -ExplicitPath $FfmpegPath }
if ([string]::IsNullOrWhiteSpace($ffmpeg)) { throw 'ffmpeg ontbreekt of staat niet op PATH.' }
Write-Host "ffmpeg gevonden: $ffmpeg"

$help = (& $ffmpeg -hide_banner -h filter=whisper 2>&1) -join "`n"
if ($help -notmatch '(?i)whisper' -or $help -notmatch '(?i)use_gpu') {
    throw 'Deze FFmpeg-build heeft geen whisper-filter met use_gpu-optie. Installeer/gebruik een FFmpeg-build met --enable-whisper.'
}
Write-Host 'FFmpeg whisper-filter met use_gpu gevonden.' -ForegroundColor Green

$modelsDir = Join-Path $RuntimeRoot 'models'
New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null
$modelCandidates = @()
if (-not [string]::IsNullOrWhiteSpace($ModelPath)) {
    $modelCandidates += [pscustomobject]@{ File = [System.IO.Path]::GetFileName($ModelPath); Path = $ModelPath; Required = $true; Role = 'explicit' }
}
else {
    $modelCandidates += [pscustomobject]@{ File = 'ggml-large-v3-turbo-q5_0.bin'; Path = (Join-Path $modelsDir 'ggml-large-v3-turbo-q5_0.bin'); Required = $true; Role = 'preferred-production' }
    if ($AllowModelFallback) {
        $modelCandidates += [pscustomobject]@{ File = 'ggml-medium-q5_0.bin'; Path = (Join-Path $modelsDir 'ggml-medium-q5_0.bin'); Required = $false; Role = 'fallback-validation' }
        $modelCandidates += [pscustomobject]@{ File = 'ggml-small-q5_1.bin'; Path = (Join-Path $modelsDir 'ggml-small-q5_1.bin'); Required = $false; Role = 'fallback-validation' }
        $modelCandidates += [pscustomobject]@{ File = 'ggml-base-q5_1.bin'; Path = (Join-Path $modelsDir 'ggml-base-q5_1.bin'); Required = $false; Role = 'fallback-validation' }
    }
}

$testRoot = Join-Path $RuntimeRoot 'selftest'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$wav = Join-Path $testRoot 'captivox_gpu_selftest_16k.wav'
$resultPath = Join-Path $testRoot 'runtime_selftest_result.json'
$manifestPath = Join-Path $RuntimeRoot 'runtime_manifest.json'
New-TestWav -Path $wav

$attempts = @()
$success = $null
foreach ($candidate in $modelCandidates) {
    $path = [string]$candidate.Path
    $file = [string]$candidate.File
    if (-not (Ensure-Model -Path $path -FileName $file)) { continue }
    foreach ($disable in @($true, $false)) {
        $attempt = Test-OneFfmpegWhisperModel -Ffmpeg $ffmpeg -RuntimeRoot $RuntimeRoot -Model $path -Wav $wav -TestRoot $testRoot -DisableCoopMat $disable
        $attempt['role'] = [string]$candidate.Role
        $attempts += [pscustomobject]$attempt
        if ($attempt.success) { $success = $attempt; break }
        Write-Host "Modeltest faalde: $($attempt.model_file), exit=$($attempt.exit_code), gpu=$($attempt.gpu_text_detected)" -ForegroundColor Yellow
    }
    if ($null -ne $success) { break }
}

$result = [ordered]@{
    version = 1
    engine = 'FFmpeg Whisper GPU'
    runtime_root = $RuntimeRoot
    ffmpeg = $ffmpeg
    selected_model = if ($success) { $success.model } else { $null }
    selected_model_file = if ($success) { $success.model_file } else { $null }
    disable_coopmat = if ($success) { $success.disable_coopmat } else { $null }
    env = if ($success) { @{ GGML_VK_VISIBLE_DEVICES = '0'; GGML_VK_DISABLE_COOPMAT = if ($success.disable_coopmat) { '1' } else { '' } } } else { @{} }
    attempts = @($attempts)
    model_fallback_allowed = [bool]$AllowModelFallback
    created_at = (Get-Date).ToString('o')
}
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resultPath -Encoding UTF8

if ($null -eq $success) {
    Write-Host ''
    Write-Host 'Alle AMD/GPU self-test pogingen zijn mislukt.' -ForegroundColor Red
    if (-not $AllowModelFallback -and [string]::IsNullOrWhiteSpace($ModelPath)) { Write-Host 'Let op: standaard valideert deze test bewust alleen het productiemodel ggml-large-v3-turbo-q5_0.bin. Gebruik -AllowModelFallback alleen voor route-diagnose, niet als kwaliteitsbaseline.' -ForegroundColor Yellow }
    Write-Host "Details: $resultPath" -ForegroundColor Yellow
    $last = if ($attempts.Count -gt 0) { $attempts[$attempts.Count - 1] } else { $null }
    if ($last) { Write-Host $last.output_tail }
    throw "FFmpeg Whisper GPU self-test faalde voor alle gevalideerde modelvarianten. Zie: $resultPath"
}

$manifest = [ordered]@{
    version = 1
    engine = 'FFmpeg Whisper GPU'
    ffmpeg = $ffmpeg
    selected_model_path = $success.model
    selected_model_file = $success.model_file
    selected_model_role = if ($success.Contains('role')) { [string]$success['role'] } else { $null }
    disable_coopmat = [bool]$success.disable_coopmat
    ggml_vk_visible_devices = '0'
    gpu_confirmed = $true
    validated_at = (Get-Date).ToString('o')
    selftest_result = $resultPath
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host 'Self-test afgerond.' -ForegroundColor Green
Write-Host "Geselecteerd model: $($success.model_file)" -ForegroundColor Green
Write-Host "CoopMat uitgeschakeld: $($success.disable_coopmat)" -ForegroundColor Green
Write-Host "Resultaat: $resultPath"
Write-Host "Runtime manifest: $manifestPath"
Write-Host 'FFmpeg Whisper GPU-route is bruikbaar als primaire AMD-transcriptieroute.' -ForegroundColor Green
