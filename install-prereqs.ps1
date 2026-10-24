[CmdletBinding()]
param(
    [string]$PythonSeries = '3.10',
    [string]$VenvPath = '',
    [switch]$SkipAppSettings,
    [switch]$OverwriteExistingAppSettings,
    [switch]$RebuildVenv,
    # Geavanceerd en alleen handmatig: whisper.cpp met Vulkan zelf bouwen (vraagt bouwgereedschap). Nooit in -Unattended.
    [switch]$PrepareAmdGpuRuntime,
    # Optioneel een andere gegevensmap (bijvoorbeeld voor een test); zonder deze parameter de vaste map van deze smaak.
    [string]$AppDataRoot = '',
    # Voor het installatieprogramma: geen vragen, geen uitvoer die iemand moet lezen; voortgang gaat naar -StatusFile.
    [switch]$Unattended,
    # Een regel '<procent>|<Nederlands>|<Engels>', telkens in zijn geheel vervangen; aan het eind
    # 'done|<exitcode>|<Nederlands>|<Engels>|<logbestand>'. Naast dit bestand komt '<StatusFile>.pid' met het proces-id.
    [string]$StatusFile = '',
    # De spraakmodellen niet vooraf ophalen (dan haalt de eerste transcriptie ze op).
    [switch]$SkipModelPrefetch,
    # Optioneel een ander Whisper-model om vooraf op te halen; zonder deze parameter: large-v2 bij NVIDIA, anders
    # large-v3-turbo (processor). Bij een werkende videokaartroute (AMD) is alleen het whisper.cpp-model nodig.
    [string]$PrefetchModel = '',
    # Welke torch-build: auto (CUDA bij een NVIDIA-kaart, anders CPU), cpu of cuda.
    [ValidateSet('auto', 'cpu', 'cuda')]
    [string]$TorchVariant = 'auto'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}
# >>> psmodulepath-sanitize
# Windows PowerShell 5.1 die vanuit PowerShell 7 is gestart, erft het modulepad van PowerShell 7; dan laden de eigen
# modules van Windows PowerShell (Get-CimInstance, ...) niet goed. Alleen de paden van Windows PowerShell en eigen
# modulemappen houden.
if ($PSVersionTable.PSEdition -ne 'Core') {
    $windowsPowerShellModules = Join-Path $PSHOME 'Modules'
    $keptModulePaths = @(($env:PSModulePath -split ';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '(?i)(\\PowerShell\\|\\Microsoft\.PowerShell_)' -and $_.TrimEnd('\') -ine $windowsPowerShellModules })
    $env:PSModulePath = (@($windowsPowerShellModules) + $keptModulePaths | Select-Object -Unique) -join ';'
}
# <<< psmodulepath-sanitize

# Kinderprocessen (Python, pip) zonder vragen, in UTF-8 en zonder voortgangsbalken in het logboek.
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONNOUSERSITE = '1'
$env:PIP_NO_INPUT = '1'
$env:PIP_DISABLE_PIP_VERSION_CHECK = '1'
$env:HF_HUB_DISABLE_PROGRESS_BARS = '1'
$env:HF_HUB_DISABLE_TELEMETRY = '1'
# Zonder Xet: gewone downloads met zichtbare voortgang en geen tweede, grote chunk-cache naast de modellen.
$env:HF_HUB_DISABLE_XET = '1'

$script:Warnings = New-Object System.Collections.Generic.List[string]
$script:Blockers = New-Object System.Collections.Generic.List[string]
$script:Summary = New-Object System.Collections.Generic.List[string]
$script:FirstBlockerEnglish = ''
# De gegevensmap van deze smaak. build.ps1 zet hier bij het bouwen de map van de gekozen smaak neer.
$script:ProductFolder = 'CaptivoxRecorderFree'
# Tijdelijke Python-probes; de logboeken staan in <gegevensmap>\logs\install.
$script:ProbeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ($script:ProductFolder + 'Prereqs')
$script:LogWriter = $null
$script:LogPath = ''
$script:LockStream = $null
$script:LastPercent = 0
$script:StopMarker = 'CAPTIVOX_INSTALL_STOP'

# ---------- Vastgepinde downloads ----------
# Elke download heeft een vaste versie en SHA-256; bij een afwijkende controlesom wordt het bestand geweigerd.

# Python 3.10 van python-build-standalone (astral-sh), "install_only_stripped": een losse, private Python zonder
# installatie, register of PATH. SHA-256 uit SHA256SUMS van release 20261003.
$script:PythonDownload = [pscustomobject]@{
    Version = '3.10.22'
    Url = 'https://github.com/astral-sh/python-build-standalone/releases/download/20261003/cpython-3.10.22%2B20261003-x86_64-pc-windows-msvc-install_only_stripped.tar.gz'
    FileName = 'cpython-3.10.22+20261003-x86_64-pc-windows-msvc-install_only_stripped.tar.gz'
    Sha256 = '0ce921091537e76b871333be49fe225940854c17a21be914b792470716abb2a0'
}
# FFmpeg 8.1 "full build" van gyan.dev (GitHub GyanD/codexffmpeg). De full build heeft de whisper-audiofilter met
# Vulkan-GPU (use_gpu/gpu_device), die de AMD-route van de worker gebruikt. GPLv3.
$script:FfmpegDownload = [pscustomobject]@{
    Version = '8.1-full_build'
    Url = 'https://github.com/GyanD/codexffmpeg/releases/download/8.1/ffmpeg-8.1-full_build.zip'
    FileName = 'ffmpeg-8.1-full_build.zip'
    Sha256 = '587b1c37de29c5003d01cf65da10001bac43a58b88e61af0fc77c61daff04761'
}
# Het whisper.cpp-model dat de FFmpeg-whisper-route (AMD en andere Vulkan-kaarten) verwacht, vast op een commit van
# huggingface.co/ggerganov/whisper.cpp.
$script:WhisperCppModelDownload = [pscustomobject]@{
    Url = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo-q5_0.bin'
    FileName = 'ggml-large-v3-turbo-q5_0.bin'
    Sha256 = '394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2'
}
# CUDA-builds van torch/torchaudio voor NVIDIA (dezelfde versie als in requirements.windows.lock.txt, met +cu128).
$script:TorchCudaIndexUrl = 'https://download.pytorch.org/whl/cu128'
$script:TorchCudaSuffix = '+cu128'

# ---------- Logboek en status ----------

# Elke smaak gebruikt een eigen map, los van een eventuele andere Captivox-installatie. Alleen -AppDataRoot kan die map
# vervangen; omgevingsvariabelen spelen geen rol, zodat dit script nooit de map van een andere installatie gebruikt.
function Get-AppDataRoot {
    if (-not [string]::IsNullOrWhiteSpace($AppDataRoot)) {
        return [System.IO.Path]::GetFullPath($AppDataRoot)
    }
    return Join-Path $env:LOCALAPPDATA $script:ProductFolder
}

function Get-RuntimeRoot {
    return Join-Path (Get-AppDataRoot) 'runtime'
}

function Get-DefaultVenvPath {
    return Join-Path (Get-RuntimeRoot) '.venv'
}

function Initialize-InstallLog {
    $logDirectory = Join-Path (Get-AppDataRoot) 'logs\install'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $script:LogPath = Join-Path $logDirectory ('install-{0:yyyyMMdd-HHmmss}.log' -f [DateTime]::Now)
    $stream = [System.IO.File]::Open($script:LogPath, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
    $script:LogWriter = New-Object System.IO.StreamWriter($stream, (New-Object System.Text.UTF8Encoding($false)))
    $script:LogWriter.AutoFlush = $true

    # Alleen de nieuwste tien installatielogboeken bewaren.
    try {
        Get-ChildItem -LiteralPath $logDirectory -Filter 'install-*.log' -File -ErrorAction Stop |
            Sort-Object -Property LastWriteTime -Descending | Select-Object -Skip 10 |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
    }
    catch { }
}

function Write-Log([string]$Message) {
    if ($null -eq $script:LogWriter) { return }
    try {
        $script:LogWriter.WriteLine(('{0:yyyy-MM-dd HH:mm:ss} {1}' -f [DateTime]::Now, $Message))
    }
    catch { }
}

function Write-Step([string]$Message) {
    Write-Host "== $Message" -ForegroundColor Cyan
    Write-Log "== $Message"
}

function Add-WarningLine([string]$Message) {
    Write-Warning $Message
    Write-Log "WAARSCHUWING: $Message"
    $script:Warnings.Add($Message) | Out-Null
}

function Add-Blocker([string]$Message, [string]$English = '') {
    Write-Host $Message -ForegroundColor Yellow
    Write-Log "BLOKKEREND: $Message"
    $script:Blockers.Add($Message) | Out-Null
    if ([string]::IsNullOrWhiteSpace($script:FirstBlockerEnglish) -and -not [string]::IsNullOrWhiteSpace($English)) {
        $script:FirstBlockerEnglish = $English
    }
}

function Add-Summary([string]$Message) {
    Write-Log "OK: $Message"
    $script:Summary.Add($Message) | Out-Null
}

# Stopt de rest van de installatie na een blokkerend punt (de afsluiting schrijft het resultaat).
function Stop-Install {
    throw $script:StopMarker
}

# De statusregel is ASCII zonder '|' of regeleinden, zodat het installatieprogramma hem zonder coderingsgedoe kan lezen.
function ConvertTo-StatusText([string]$Text) {
    if ([string]::IsNullOrEmpty($Text)) { return '' }
    $normalized = $Text.Normalize([System.Text.NormalizationForm]::FormD)
    $builder = New-Object System.Text.StringBuilder
    foreach ($character in $normalized.ToCharArray()) {
        $category = [System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($character)
        if ($category -eq [System.Globalization.UnicodeCategory]::NonSpacingMark) { continue }
        $code = [int]$character
        if ($code -eq 124) { [void]$builder.Append('/') }
        elseif ($code -eq 13 -or $code -eq 10 -or $code -eq 9) { [void]$builder.Append(' ') }
        elseif ($code -ge 32 -and $code -lt 127) { [void]$builder.Append($character) }
        else { [void]$builder.Append('?') }
    }
    $result = $builder.ToString().Trim()
    if ($result.Length -gt 400) { $result = $result.Substring(0, 397) + '...' }
    return $result
}

# Schrijft het statusbestand in een keer: eerst een tijdelijk bestand, dan vervangen. Leest het installatieprogramma op
# dat moment, dan volgt een paar keer een nieuwe poging; mislukt het, dan neemt de volgende statusregel het over.
function Write-StatusLine([string]$Line) {
    if ([string]::IsNullOrWhiteSpace($StatusFile)) { return }
    $temporary = $StatusFile + '.tmp'
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try {
            [System.IO.File]::WriteAllText($temporary, $Line, [System.Text.Encoding]::ASCII)
            if (Test-Path -LiteralPath $StatusFile) {
                # [NullString]::Value: met $null geeft PowerShell een lege tekst door en weigert .NET het pad.
                [System.IO.File]::Replace($temporary, $StatusFile, [NullString]::Value)
            }
            else {
                [System.IO.File]::Move($temporary, $StatusFile)
            }
            return
        }
        catch {
            Start-Sleep -Milliseconds 50
        }
    }
}

function Set-InstallStatus([int]$Percent, [string]$Dutch, [string]$English) {
    $value = [Math]::Max(0, [Math]::Min(99, $Percent))
    if ($value -lt $script:LastPercent) { $value = $script:LastPercent }
    $script:LastPercent = $value
    Write-Log ("STATUS {0}% {1}" -f $value, $Dutch)
    Write-StatusLine ('{0}|{1}|{2}' -f $value, (ConvertTo-StatusText $Dutch), (ConvertTo-StatusText $English))
}

function Write-FinalStatus([int]$ExitCode, [string]$Dutch, [string]$English) {
    Write-Log ("RESULTAAT exitcode {0}: {1}" -f $ExitCode, $Dutch)
    Write-StatusLine ('done|{0}|{1}|{2}|{3}' -f $ExitCode, (ConvertTo-StatusText $Dutch), (ConvertTo-StatusText $English), (ConvertTo-StatusText $script:LogPath))
}

function Write-PidFile {
    if ([string]::IsNullOrWhiteSpace($StatusFile)) { return }
    try {
        $directory = Split-Path -Parent $StatusFile
        if (-not [string]::IsNullOrWhiteSpace($directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
        [System.IO.File]::WriteAllText($StatusFile + '.pid', [string]$PID, [System.Text.Encoding]::ASCII)
    }
    catch { }
}

# Maar een installatie tegelijk per gegevensmap.
function Enter-InstallLock {
    $runtimeRoot = Get-RuntimeRoot
    New-Item -ItemType Directory -Path $runtimeRoot -Force | Out-Null
    $lockPath = Join-Path $runtimeRoot 'install.lock'
    try {
        $script:LockStream = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        return $true
    }
    catch {
        Add-Blocker 'Er loopt al een andere installatie of reparatie van de transcriptie-omgeving. Wacht tot die klaar is en probeer het daarna opnieuw.' 'Another installation or repair of the transcription runtime is already running. Wait until it has finished and try again.'
        return $false
    }
}

function Exit-InstallLock {
    if ($null -ne $script:LockStream) {
        try { $script:LockStream.Dispose() } catch { }
        $script:LockStream = $null
    }
}

# ---------- Processen ----------

function Format-NativeArgument([string]$Value) {
    if ($null -eq $Value) { return '""' }
    if ($Value -eq '') { return '""' }
    if ($Value -notmatch '[\s\"]') { return $Value }
    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Join-NativeArguments([string[]]$Values) {
    if ($null -eq $Values -or $Values.Count -eq 0) {
        return ''
    }

    return (($Values | ForEach-Object { Format-NativeArgument $_ }) -join ' ')
}

function New-TemporaryProbeFile([string]$StepName, [string]$Content) {
    New-Item -ItemType Directory -Path $script:ProbeRoot -Force | Out-Null
    $probeSafe = ($StepName -replace '[^a-zA-Z0-9\-_]+', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($probeSafe)) {
        $probeSafe = 'probe'
    }
    $probePath = Join-Path $script:ProbeRoot ("{0}_{1:yyyyMMdd_HHmmss_fff}.py" -f $probeSafe, [DateTime]::Now)
    [System.IO.File]::WriteAllText($probePath, $Content, [System.Text.UTF8Encoding]::new($false))
    return $probePath
}

function Stop-ProcessTree([int]$ProcessId) {
    try {
        $taskkill = Join-Path $env:SystemRoot 'System32\taskkill.exe'
        & $taskkill /PID $ProcessId /T /F 2>&1 | Out-Null
    }
    catch { }
}

# Start een programma zonder venster, leest stdout en stderr regel voor regel (zonder extra threads), schrijft alles in
# het logboek en geeft elke regel aan -OnLine (bijvoorbeeld voor voortgang). -TimeoutSeconds 0 = geen tijdslimiet.
function Invoke-NativeLogged {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory = $true)][string]$StepName,
        [int]$TimeoutSeconds = 0,
        [scriptblock]$OnLine = $null,
        [string]$WorkingDirectory = ''
    )

    $stdoutBuilder = New-Object System.Text.StringBuilder
    $stderrBuilder = New-Object System.Text.StringBuilder
    $timedOut = $false
    $exitCode = 1
    $commandText = "$FilePath $(Join-NativeArguments -Values $ArgumentList)"
    Write-Log "STAP $StepName -> $commandText"

    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $FilePath
        $psi.Arguments = Join-NativeArguments -Values $ArgumentList
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
        $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
        $psi.WorkingDirectory = $(if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) { $WorkingDirectory } elseif ($bundleRoot) { $bundleRoot } else { (Get-Location).Path })

        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $psi
        $null = $process.Start()
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $outTask = $process.StandardOutput.ReadLineAsync()
        $errTask = $process.StandardError.ReadLineAsync()

        while ($true) {
            $progressed = $false
            foreach ($channel in @('out', 'err')) {
                $task = $(if ($channel -eq 'out') { $outTask } else { $errTask })
                if ($null -eq $task -or -not $task.IsCompleted) { continue }
                $progressed = $true
                $line = $null
                try { $line = $task.Result } catch { $line = $null }
                if ($null -eq $line) {
                    if ($channel -eq 'out') { $outTask = $null } else { $errTask = $null }
                    continue
                }
                if ($channel -eq 'out') {
                    [void]$stdoutBuilder.AppendLine($line)
                    $outTask = $process.StandardOutput.ReadLineAsync()
                }
                else {
                    [void]$stderrBuilder.AppendLine($line)
                    $errTask = $process.StandardError.ReadLineAsync()
                }
                Write-Log "  [$StepName] $line"
                if (-not $Unattended) { Write-Host $line }
                if ($null -ne $OnLine) {
                    try { & $OnLine $line ($channel -eq 'err') } catch { Write-Log "  [$StepName] voortgang niet verwerkt: $($_.Exception.Message)" }
                }
            }

            if ($null -eq $outTask -and $null -eq $errTask) { break }
            if ($TimeoutSeconds -gt 0 -and $stopwatch.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
                $timedOut = $true
                Stop-ProcessTree -ProcessId $process.Id
                [void]$stderrBuilder.AppendLine("TIMEOUT: proces afgebroken na $TimeoutSeconds seconden.")
                break
            }
            if (-not $progressed) { Start-Sleep -Milliseconds 100 }
        }

        if ($timedOut) {
            try { $process.WaitForExit(5000) | Out-Null } catch { }
            $exitCode = -1
        }
        else {
            $process.WaitForExit()
            $exitCode = [int]$process.ExitCode
        }
        Write-Log "STAP $StepName klaar: exitcode $exitCode$(if ($timedOut) { ' (tijdslimiet)' })"

        return [pscustomobject]@{
            ExitCode = [int]$exitCode
            LogPath = $script:LogPath
            StdOut = $stdoutBuilder.ToString()
            StdErr = $stderrBuilder.ToString()
            TimedOut = [bool]$timedOut
        }
    }
    catch {
        $message = ($_ | Out-String).TrimEnd()
        Write-Log "STAP $StepName mislukt: $message"
        return [pscustomobject]@{
            ExitCode = 1
            LogPath = $script:LogPath
            StdOut = $stdoutBuilder.ToString()
            StdErr = $message
            TimedOut = $false
        }
    }
}

function Invoke-NativeLoggedWithTimeout {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory = $true)][string]$StepName,
        [int]$TimeoutSeconds = 60
    )
    return Invoke-NativeLogged -FilePath $FilePath -ArgumentList $ArgumentList -StepName $StepName -TimeoutSeconds $TimeoutSeconds
}

function Get-OutputTail([object]$Result, [int]$Lines = 6) {
    $text = ([string]$Result.StdErr + "`n" + [string]$Result.StdOut)
    $kept = @($text -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last $Lines)
    return ($kept -join ' | ')
}

# ---------- Downloads ----------

function Get-FileSha256([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $bytes = $sha.ComputeHash($stream)
    }
    finally {
        $stream.Dispose()
        $sha.Dispose()
    }
    return (-join ($bytes | ForEach-Object { $_.ToString('x2') }))
}

function Format-Gigabytes([double]$Bytes) {
    return ([Math]::Round($Bytes / 1GB, 2)).ToString('0.00', [System.Globalization.CultureInfo]::InvariantCulture)
}

function Format-Megabytes([double]$Bytes) {
    return ([Math]::Round($Bytes / 1MB, 0)).ToString('0', [System.Globalization.CultureInfo]::InvariantCulture)
}

# Haalt een vastgepind bestand op (met voortgang) en controleert de SHA-256. Staat het bestand er al met de juiste
# controlesom, dan wordt niets opnieuw gedownload. Geeft $true bij succes.
function Invoke-PinnedDownload {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Sha256,
        [Parameter(Mandatory = $true)][string]$Destination,
        [int]$PercentFrom = 0,
        [int]$PercentTo = 0,
        [string]$Dutch = 'Downloaden',
        [string]$English = 'Downloading'
    )

    $expected = $Sha256.ToLowerInvariant()
    $directory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Path $directory -Force | Out-Null

    if (Test-Path -LiteralPath $Destination) {
        if ((Get-FileSha256 -Path $Destination) -eq $expected) {
            Write-Log "Download al aanwezig met juiste controlesom: $Destination"
            return $true
        }
        Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
    }

    try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }
    Add-Type -AssemblyName System.Net.Http

    $partial = $Destination + '.part'
    $lastError = ''
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $client = $null
        $response = $null
        $source = $null
        $target = $null
        try {
            Write-Log "Download (poging $attempt): $Url"
            $handler = New-Object System.Net.Http.HttpClientHandler
            $client = New-Object System.Net.Http.HttpClient($handler)
            $client.Timeout = [System.Threading.Timeout]::InfiniteTimeSpan
            $client.DefaultRequestHeaders.UserAgent.ParseAdd('CaptivoxSetup/1.0')
            $response = $client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            if (-not $response.IsSuccessStatusCode) {
                throw "HTTP $([int]$response.StatusCode) $($response.ReasonPhrase)"
            }
            $total = $response.Content.Headers.ContentLength
            $source = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
            $target = [System.IO.File]::Open($partial, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            $buffer = New-Object byte[] (1024 * 1024)
            $done = [long]0
            $lastReport = [DateTime]::MinValue
            while ($true) {
                $readTask = $source.ReadAsync($buffer, 0, $buffer.Length)
                # Valt de verbinding stil, dan na twee minuten zonder gegevens afbreken en opnieuw proberen.
                if (-not $readTask.Wait(120000)) { throw 'De download staat al twee minuten stil.' }
                $read = $readTask.Result
                if ($read -le 0) { break }
                $target.Write($buffer, 0, $read)
                $done += $read
                if (([DateTime]::Now - $lastReport).TotalMilliseconds -ge 1000) {
                    if ($total -and $total -gt 0) {
                        $fraction = [Math]::Min(1.0, $done / [double]$total)
                        $percent = [int]($PercentFrom + ($PercentTo - $PercentFrom) * $fraction)
                        Set-InstallStatus $percent ("{0} ({1} van {2} MB)" -f $Dutch, (Format-Megabytes $done), (Format-Megabytes $total)) ("{0} ({1} of {2} MB)" -f $English, (Format-Megabytes $done), (Format-Megabytes $total))
                    }
                    else {
                        Set-InstallStatus $PercentFrom ("{0} ({1} MB)" -f $Dutch, (Format-Megabytes $done)) ("{0} ({1} MB)" -f $English, (Format-Megabytes $done))
                    }
                    # Pas na het schrijven van de status, zodat een trage statusregel de download niet afremt.
                    $lastReport = [DateTime]::Now
                }
            }
            $target.Dispose(); $target = $null
            $source.Dispose(); $source = $null

            $actual = Get-FileSha256 -Path $partial
            if ($actual -ne $expected) {
                Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
                throw "Controlesom klopt niet (verwacht $expected, gekregen $actual)."
            }
            Move-Item -LiteralPath $partial -Destination $Destination -Force
            Write-Log "Download klaar en gecontroleerd: $Destination ($done bytes)"
            return $true
        }
        catch {
            $lastError = $_.Exception.Message
            Write-Log "Download mislukt (poging $attempt): $lastError"
            if ($null -ne $target) { try { $target.Dispose() } catch { } }
            Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
            if ($attempt -lt 3) { Start-Sleep -Seconds (5 * $attempt) }
        }
        finally {
            if ($null -ne $source) { try { $source.Dispose() } catch { } }
            if ($null -ne $response) { try { $response.Dispose() } catch { } }
            if ($null -ne $client) { try { $client.Dispose() } catch { } }
        }
    }

    Add-Blocker "Downloaden mislukt: $Url. Controleer de internetverbinding en start de installatie opnieuw. Fout: $lastError" "A download failed. Check the internet connection and run setup again."
    return $false
}

# ---------- Bundel en voorcontrole ----------

function Get-BundledPythonCandidates([string]$BundleRoot) {
    return @(
        (Join-Path $BundleRoot 'runtime\python\python.exe'),
        (Join-Path $BundleRoot 'runtime\python310\python.exe'),
        (Join-Path $BundleRoot 'python\python.exe'),
        (Join-Path $BundleRoot 'tools\python\python.exe')
    )
}

function Test-PythonExecutable([string]$Candidate, [string]$Series) {
    if ([string]::IsNullOrWhiteSpace($Candidate) -or -not (Test-Path -LiteralPath $Candidate)) { return $false }
    $result = Invoke-NativeLogged -FilePath $Candidate -ArgumentList @('--version') -StepName 'python_version' -TimeoutSeconds 60
    if ($result.ExitCode -ne 0) { return $false }
    $versionText = (($result.StdOut + "`n" + $result.StdErr) -split "`r?`n" | Where-Object { $_ -match '^Python\s+\d+\.\d+' } | Select-Object -First 1)
    return (-not [string]::IsNullOrWhiteSpace($versionText) -and $versionText -match "Python\s+$([regex]::Escape($Series))\.")
}

function Resolve-BundledPythonPath([string]$BundleRoot, [string]$Series) {
    foreach ($candidate in Get-BundledPythonCandidates -BundleRoot $BundleRoot) {
        if (Test-PythonExecutable -Candidate $candidate -Series $Series) {
            return $candidate
        }
    }
    return $null
}

function Resolve-WheelhousePath([string]$BundleRoot) {
    $candidate = Join-Path $BundleRoot 'workers\wheelhouse'
    if (-not (Test-Path -LiteralPath $candidate)) {
        return $null
    }

    $wheelFiles = Get-ChildItem -LiteralPath $candidate -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in @('.whl', '.zip', '.gz') }
    if ($wheelFiles -and $wheelFiles.Count -gt 0) {
        return $candidate
    }

    return $null
}

function Test-PathWritable([string]$PathToCheck) {
    try {
        if (-not (Test-Path -LiteralPath $PathToCheck)) {
            New-Item -ItemType Directory -Path $PathToCheck -Force | Out-Null
        }
        $probeFile = Join-Path $PathToCheck ([System.Guid]::NewGuid().ToString('N') + '.tmp')
        [System.IO.File]::WriteAllText($probeFile, 'ok', [System.Text.UTF8Encoding]::new($false))
        Remove-Item -LiteralPath $probeFile -Force -ErrorAction SilentlyContinue
        return $true
    }
    catch {
        return $false
    }
}

function Get-FreeBytes([string]$PathOnDrive) {
    try {
        $root = [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($PathOnDrive))
        if ([string]::IsNullOrWhiteSpace($root)) { return $null }
        return [long](New-Object System.IO.DriveInfo($root)).AvailableFreeSpace
    }
    catch {
        return $null
    }
}

$script:GpuNames = $null
function Get-GpuNames {
    if ($null -eq $script:GpuNames) {
        try {
            $script:GpuNames = @(Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop | ForEach-Object { [string]$_.Name } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        }
        catch {
            Add-WarningLine "Videokaarten konden niet worden gelezen: $($_.Exception.Message)"
            $script:GpuNames = @()
        }
    }
    return $script:GpuNames
}

# Dezelfde herkenning als Get-DetectedGpuProfile in de worker.
function Test-NvidiaGpuPresent {
    foreach ($name in Get-GpuNames) {
        if ($name -match '(?i)nvidia|geforce|\brtx\b|\bgtx\b') { return $true }
    }
    return $false
}

function Test-AmdGpuPresent {
    foreach ($name in Get-GpuNames) {
        if ($name -match '(?i)amd|radeon|\brx\b') { return $true }
    }
    return $false
}

function Run-PreflightChecks([string]$BundleRoot, [string]$TargetVenv) {
    Write-Step 'Voorcontrole'

    try {
        $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
        Add-Summary "Windows-versie gedetecteerd: $($os.Caption) ($($os.Version))"
    }
    catch {
        Add-WarningLine "Windows-versie kon niet worden gelezen: $($_.Exception.Message)"
    }

    if (-not [Environment]::Is64BitOperatingSystem) {
        Add-Blocker 'Captivox verwacht een 64-bit Windows.' 'Captivox needs 64-bit Windows.'
    }

    $tar = Join-Path $env:SystemRoot 'System32\tar.exe'
    if (-not (Test-Path -LiteralPath $tar)) {
        Add-Blocker "tar.exe ontbreekt ($tar). Captivox heeft Windows 10 versie 1803 of nieuwer nodig." 'tar.exe is missing. Captivox needs Windows 10 version 1803 or later.'
    }

    $venvParent = Split-Path -Parent $TargetVenv
    if (-not (Test-PathWritable -PathToCheck $venvParent)) {
        Add-Blocker "De map voor de transcriptie-omgeving is niet beschrijfbaar: $venvParent" 'The folder for the transcription runtime is not writable.'
    }

    $settingsParent = Split-Path -Parent (Get-AppSettingsPath)
    if (-not (Test-PathWritable -PathToCheck $settingsParent)) {
        Add-Blocker "De map voor de instellingen is niet beschrijfbaar: $settingsParent" 'The settings folder is not writable.'
    }

    # Pakketten als torch en setuptools hebben diepe mappen; zonder lange-padondersteuning gaat dat mis bij een lange gegevensmap.
    if ($TargetVenv.Length -gt 100) {
        $longPaths = 0
        try { $longPaths = [int](Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' -Name 'LongPathsEnabled' -ErrorAction Stop).LongPathsEnabled } catch { $longPaths = 0 }
        if ($longPaths -ne 1) {
            Add-WarningLine "Het pad van de Python-omgeving is lang ($($TargetVenv.Length) tekens) en Windows staat geen lange paden toe; de installatie van pakketten kan daardoor mislukken."
        }
    }

    $freeBytes = Get-FreeBytes -PathOnDrive $TargetVenv
    if ($null -ne $freeBytes) {
        Add-Summary "Vrije schijfruimte: $(Format-Gigabytes $freeBytes) GB"
        if ($freeBytes -lt 3GB) {
            Add-Blocker "Te weinig vrije schijfruimte: $(Format-Gigabytes $freeBytes) GB. De transcriptie-omgeving heeft ongeveer 10 GB nodig." 'Not enough free disk space. The transcription runtime needs about 10 GB.'
        }
        elseif ($freeBytes -lt 10GB) {
            Add-WarningLine "Vrije schijfruimte is krap ($(Format-Gigabytes $freeBytes) GB); de volledige transcriptie-omgeving met spraakmodellen gebruikt ongeveer 10 GB."
        }
    }

    $gpuNames = Get-GpuNames
    if ($gpuNames.Count -gt 0) {
        Add-Summary "Videokaarten: $([string]::Join('; ', $gpuNames))"
    }
}

function Resolve-BundleContext {
    $start = Get-Item -LiteralPath (Split-Path -Parent $PSCommandPath)

    $current = $start
    for ($i = 0; $i -lt 10 -and $null -ne $current; $i++) {
        $candidate = Join-Path $current.FullName 'workers\requirements.txt'
        if (Test-Path -LiteralPath $candidate) {
            $workerRoot = Split-Path -Parent $candidate
            $realizedLockPath = Join-Path $workerRoot 'requirements.windows.realized.lock.txt'
            $lockPath = Join-Path $workerRoot 'requirements.windows.lock.txt'
            return [pscustomobject]@{
                Root = $current.FullName
                Requirements = $candidate
                InstallSpec = $(if (Test-Path -LiteralPath $realizedLockPath) { $realizedLockPath } elseif (Test-Path -LiteralPath $lockPath) { $lockPath } else { $candidate })
                LockSpec = $lockPath
                RealizedLockSpec = $realizedLockPath
            }
        }
        $current = $current.Parent
    }

    $fallbackRoot = $start.FullName
    $fallbackRequirements = Join-Path $fallbackRoot 'workers\requirements.txt'
    $fallbackLock = Join-Path $fallbackRoot 'workers\requirements.windows.lock.txt'
    $fallbackRealizedLock = Join-Path $fallbackRoot 'workers\requirements.windows.realized.lock.txt'
    return [pscustomobject]@{
        Root = $fallbackRoot
        Requirements = $fallbackRequirements
        InstallSpec = $(if (Test-Path -LiteralPath $fallbackRealizedLock) { $fallbackRealizedLock } elseif (Test-Path -LiteralPath $fallbackLock) { $fallbackLock } else { $fallbackRequirements })
        LockSpec = $fallbackLock
        RealizedLockSpec = $fallbackRealizedLock
    }
}

# ---------- Private Python ----------

# Een eigen Python 3.10 in <gegevensmap>\runtime\python: geen installatie, geen register, geen PATH. Een meegeleverde
# Python in de bundel (draagbare zip) gaat voor; anders de vastgepinde python-build-standalone.
function Ensure-PrivatePython([string]$Series) {
    Write-Step "Python $Series"
    Set-InstallStatus 2 'Python controleren' 'Checking Python'

    if ($script:BundledPythonPath) {
        Add-Summary "Meegeleverde Python in de bundel gebruikt: $script:BundledPythonPath"
        return $script:BundledPythonPath
    }

    $runtimeRoot = Get-RuntimeRoot
    $pythonRoot = Join-Path $runtimeRoot 'python'
    $pythonExe = Join-Path $pythonRoot 'python.exe'
    if (Test-PythonExecutable -Candidate $pythonExe -Series $Series) {
        Add-Summary "Eigen Python $Series aanwezig: $pythonExe"
        return $pythonExe
    }

    if ($Series -ne '3.10') {
        Add-Blocker "Alleen Python 3.10 kan automatisch worden opgehaald (gevraagd: $Series)." 'Only Python 3.10 can be downloaded automatically.'
        return $null
    }

    $archive = Join-Path $runtimeRoot ('downloads\' + $script:PythonDownload.FileName)
    if (-not (Invoke-PinnedDownload -Url $script:PythonDownload.Url -Sha256 $script:PythonDownload.Sha256 -Destination $archive -PercentFrom 2 -PercentTo 7 -Dutch 'Python downloaden' -English 'Downloading Python')) {
        return $null
    }

    Set-InstallStatus 7 'Python uitpakken' 'Extracting Python'
    $staging = Join-Path $runtimeRoot 'python.partial'
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    $tar = Join-Path $env:SystemRoot 'System32\tar.exe'
    $extract = Invoke-NativeLogged -FilePath $tar -ArgumentList @('-xzf', $archive, '-C', $staging) -StepName 'python_extract' -TimeoutSeconds 900
    $extractedExe = Join-Path $staging 'python\python.exe'
    if ($extract.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $extractedExe)) {
        Add-Blocker "Python kon niet worden uitgepakt. Zie logboek: $script:LogPath" 'Python could not be extracted.'
        return $null
    }

    if (Test-Path -LiteralPath $pythonRoot) {
        try {
            Remove-Item -LiteralPath $pythonRoot -Recurse -Force -ErrorAction Stop
        }
        catch {
            Add-Blocker "De oude Python-map kon niet worden vervangen (is Captivox nog bezig?): $pythonRoot" 'The old Python folder could not be replaced. Close Captivox and run setup again.'
            return $null
        }
    }
    Move-Item -LiteralPath (Join-Path $staging 'python') -Destination $pythonRoot
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue

    if (-not (Test-PythonExecutable -Candidate $pythonExe -Series $Series)) {
        Add-Blocker "De gedownloade Python start niet. Zie logboek: $script:LogPath" 'The downloaded Python does not start.'
        return $null
    }
    Add-Summary "Eigen Python $($script:PythonDownload.Version) geinstalleerd: $pythonExe"
    return $pythonExe
}

# ---------- FFmpeg ----------

function Test-FfmpegRuns([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return $false }
    $result = Invoke-NativeLogged -FilePath $Path -ArgumentList @('-hide_banner', '-version') -StepName 'ffmpeg_version' -TimeoutSeconds 60
    return ($result.ExitCode -eq 0)
}

# Heeft deze FFmpeg de whisper-audiofilter met GPU-keuze (nodig voor de AMD-route van de worker)?
function Test-FfmpegWhisperGpuFilter([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return $false }
    $result = Invoke-NativeLogged -FilePath $Path -ArgumentList @('-hide_banner', '-h', 'filter=whisper') -StepName 'ffmpeg_whisper_filter' -TimeoutSeconds 60
    $text = ($result.StdOut + "`n" + $result.StdErr).ToLowerInvariant()
    return ($result.ExitCode -eq 0 -and $text.Contains('use_gpu') -and $text.Contains('gpu_device') -and $text.Contains('model'))
}

# Een eigen FFmpeg in <gegevensmap>\runtime\ffmpeg (ffmpeg.exe, ffprobe.exe en de licentie), los van een FFmpeg in PATH.
function Ensure-PrivateFfmpeg {
    Write-Step 'FFmpeg'
    Set-InstallStatus 8 'FFmpeg controleren' 'Checking FFmpeg'

    $runtimeRoot = Get-RuntimeRoot
    $ffmpegRoot = Join-Path $runtimeRoot 'ffmpeg'
    $ffmpegExe = Join-Path $ffmpegRoot 'ffmpeg.exe'
    $marker = Join-Path $ffmpegRoot 'captivox-ffmpeg.txt'
    $expectedMarker = "$($script:FfmpegDownload.Version) $($script:FfmpegDownload.Sha256)"

    $markerOk = $false
    if (Test-Path -LiteralPath $marker) {
        try { $markerOk = (([string](Get-Content -LiteralPath $marker -TotalCount 1)).Trim() -eq $expectedMarker) } catch { $markerOk = $false }
    }
    if ($markerOk -and (Test-FfmpegRuns -Path $ffmpegExe) -and (Test-FfmpegWhisperGpuFilter -Path $ffmpegExe)) {
        Add-Summary "Eigen FFmpeg $($script:FfmpegDownload.Version) aanwezig: $ffmpegExe"
        return $ffmpegExe
    }

    $archive = Join-Path $runtimeRoot ('downloads\' + $script:FfmpegDownload.FileName)
    if (-not (Invoke-PinnedDownload -Url $script:FfmpegDownload.Url -Sha256 $script:FfmpegDownload.Sha256 -Destination $archive -PercentFrom 8 -PercentTo 13 -Dutch 'FFmpeg downloaden' -English 'Downloading FFmpeg')) {
        return $null
    }

    Set-InstallStatus 13 'FFmpeg uitpakken' 'Extracting FFmpeg'
    $staging = Join-Path $runtimeRoot 'ffmpeg.partial'
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    try {
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $wanted = @{ 'bin/ffmpeg.exe' = 'ffmpeg.exe'; 'bin/ffprobe.exe' = 'ffprobe.exe'; 'LICENSE' = 'LICENSE.txt' }
            foreach ($entry in $zip.Entries) {
                $name = $entry.FullName -replace '\\', '/'
                $slash = $name.IndexOf('/')
                if ($slash -lt 0) { continue }
                $relative = $name.Substring($slash + 1)
                if ($wanted.ContainsKey($relative)) {
                    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $staging $wanted[$relative]), $true)
                }
            }
        }
        finally {
            $zip.Dispose()
        }
    }
    catch {
        Add-Blocker "FFmpeg kon niet worden uitgepakt: $($_.Exception.Message)" 'FFmpeg could not be extracted.'
        return $null
    }

    if (-not (Test-FfmpegRuns -Path (Join-Path $staging 'ffmpeg.exe'))) {
        Add-Blocker "De gedownloade FFmpeg start niet. Zie logboek: $script:LogPath" 'The downloaded FFmpeg does not start.'
        return $null
    }
    [System.IO.File]::WriteAllText((Join-Path $staging 'captivox-ffmpeg.txt'), $expectedMarker, [System.Text.Encoding]::ASCII)

    if (Test-Path -LiteralPath $ffmpegRoot) {
        try {
            Remove-Item -LiteralPath $ffmpegRoot -Recurse -Force -ErrorAction Stop
        }
        catch {
            Add-Blocker "De oude FFmpeg-map kon niet worden vervangen (is Captivox nog bezig?): $ffmpegRoot" 'The old FFmpeg folder could not be replaced. Close Captivox and run setup again.'
            return $null
        }
    }
    Move-Item -LiteralPath $staging -Destination $ffmpegRoot
    Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue

    if (-not (Test-FfmpegWhisperGpuFilter -Path $ffmpegExe)) {
        Add-WarningLine 'De eigen FFmpeg heeft geen whisper-filter met GPU; de AMD-route valt dan terug op de processor.'
    }
    Add-Summary "Eigen FFmpeg $($script:FfmpegDownload.Version) geinstalleerd: $ffmpegExe"
    return $ffmpegExe
}

# ---------- Venv en worker-pakketten ----------

function Remove-VenvDirectory([string]$TargetVenv) {
    if (Test-Path -LiteralPath $TargetVenv) {
        try {
            Remove-Item -LiteralPath $TargetVenv -Recurse -Force -ErrorAction Stop
            Add-Summary "Venv verwijderd voor schone herbouw: $TargetVenv"
        }
        catch {
            Add-WarningLine "Bestaande venv kon niet volledig worden verwijderd: $TargetVenv. Fout: $($_.Exception.Message)"
        }
    }
}

# Hoort deze venv bij de gekozen Python? (regel 'home = ...' in pyvenv.cfg)
function Test-VenvBelongsToPython([string]$TargetVenv, [string]$BasePython) {
    $config = Join-Path $TargetVenv 'pyvenv.cfg'
    if (-not (Test-Path -LiteralPath $config)) { return $false }
    try {
        $homeLine = Get-Content -LiteralPath $config -ErrorAction Stop | Where-Object { $_ -match '^\s*home\s*=' } | Select-Object -First 1
        if (-not $homeLine) { return $false }
        $venvHome = ($homeLine -replace '^\s*home\s*=\s*', '').Trim().TrimEnd('\')
        $expected = (Split-Path -Parent ([System.IO.Path]::GetFullPath($BasePython))).TrimEnd('\')
        return [string]::Equals([System.IO.Path]::GetFullPath($venvHome).TrimEnd('\'), $expected, [System.StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        return $false
    }
}

function New-Venv([string]$BasePython, [string]$TargetVenv) {
    $parent = Split-Path -Parent $TargetVenv
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    Write-Step "Venv aanmaken: $TargetVenv"
    $result = Invoke-NativeLogged -FilePath $BasePython -ArgumentList @('-m', 'venv', $TargetVenv) -StepName 'python_venv_create' -TimeoutSeconds 600
    $venvPython = Join-Path $TargetVenv 'Scripts\python.exe'
    if ($result.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $venvPython)) {
        Add-Blocker "Aanmaken van de venv faalde: $TargetVenv. Zie logboek: $script:LogPath" 'The Python environment (venv) could not be created.'
        return $null
    }

    Add-Summary "Venv aangemaakt: $venvPython"
    return $venvPython
}

function Test-DependencyResolverConflict([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $false }
    foreach ($marker in @('ResolutionImpossible', 'because these package versions have conflicting dependencies.', 'The conflict is caused by:')) {
        if ($Text.Contains($marker)) { return $true }
    }
    return $false
}

function Get-DependencyResolverConflictSummary([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $interesting = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($Text -split "`r?`n")) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
        if (
            $trimmed.StartsWith('ERROR: Cannot install') -or
            $trimmed.StartsWith('The conflict is caused by:') -or
            $trimmed.StartsWith('The user requested ') -or
            $trimmed.StartsWith('whisperx ') -or
            $trimmed.StartsWith('pyannote.audio ') -or
            $trimmed.StartsWith('torch ') -or
            $trimmed.StartsWith('torchaudio ')
        ) {
            $interesting.Add($trimmed) | Out-Null
        }
    }
    if ($interesting.Count -eq 0) { return $null }
    return ($interesting | Select-Object -First 6) -join ' | '
}

function Get-LockPinnedVersion([string]$LockPath, [string]$Package) {
    if ([string]::IsNullOrWhiteSpace($LockPath) -or -not (Test-Path -LiteralPath $LockPath)) { return $null }
    $pattern = '^\s*' + [regex]::Escape($Package) + '\s*==\s*([0-9][0-9A-Za-z\.]*)'
    foreach ($line in Get-Content -LiteralPath $LockPath) {
        if ($line -match $pattern) { return $Matches[1] }
    }
    return $null
}

function Get-EffectiveTorchVariant {
    if ($TorchVariant -ne 'auto') { return $TorchVariant }
    if (Test-NvidiaGpuPresent) { return 'cuda' }
    return 'cpu'
}

# Voortgang tijdens pip: elke 'Collecting/Downloading/Installing'-regel schuift de balk een stukje op. De toestand staat
# in het scriptbereik (geen GetNewClosure: een closure ziet de functies en $script:-variabelen van dit script niet).
$script:PipProgress = @{ From = 0; To = 0; Expected = 1; Count = 0; Dutch = ''; English = ''; Installing = $false }
$script:PipProgressHandler = {
    param($line, $isError)
    $progress = $script:PipProgress
    if ($line -match '^\s*Installing collected packages') {
        # Het uitpakken en installeren zelf geeft minutenlang geen uitvoer meer.
        $progress.Installing = $true
        Set-InstallStatus ([int]($progress.From + ($progress.To - $progress.From) * 0.8)) ($progress.Dutch + ': uitpakken en installeren (dit duurt enkele minuten)') ($progress.English + ': unpacking and installing (this takes a few minutes)')
    }
    elseif (-not $progress.Installing -and $line -match '^\s*(Collecting|Downloading|Requirement already satisfied|Using cached|Building wheel)') {
        $progress.Count++
        $fraction = [Math]::Min(0.78, 0.8 * $progress.Count / [double]$progress.Expected)
        $detail = ''
        if ($line -match '^\s*(Collecting|Downloading)\s+(\S+)') { $detail = ': ' + ($Matches[2] -replace '^.*/', '') }
        Set-InstallStatus ([int]($progress.From + ($progress.To - $progress.From) * $fraction)) ($progress.Dutch + $detail) ($progress.English + $detail)
    }
}

function New-PipProgressHandler([int]$From, [int]$To, [int]$ExpectedLines, [string]$Dutch, [string]$English) {
    $script:PipProgress = @{ From = $From; To = $To; Expected = [Math]::Max(1, $ExpectedLines); Count = 0; Dutch = $Dutch; English = $English; Installing = $false }
    return $script:PipProgressHandler
}

function Install-WorkerStack([string]$VenvPython, [string]$InstallSpecPath, [string]$WheelhousePath, [string]$Variant, [string]$LockPath) {
    $commonArgs = @('--no-input', '--disable-pip-version-check', '--no-cache-dir')

    Write-Step 'Pip bijwerken in venv'
    Set-InstallStatus 16 'Pakketbeheer bijwerken' 'Updating the package manager'
    $pipUpgradeArgs = @('-m', 'pip', 'install') + $commonArgs + @('--upgrade', 'pip', 'setuptools', 'wheel')
    if (-not [string]::IsNullOrWhiteSpace($WheelhousePath)) {
        $pipUpgradeArgs = @('-m', 'pip', 'install') + $commonArgs + @('--no-index', '--find-links', $WheelhousePath, '--upgrade', 'pip', 'setuptools', 'wheel')
    }
    $pipUpgrade = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList $pipUpgradeArgs -StepName 'pip_upgrade'
    if ($pipUpgrade.ExitCode -ne 0) {
        Add-Blocker "pip bijwerken in de venv faalde. Zie logboek: $script:LogPath" 'Updating pip in the Python environment failed.'
        return [pscustomobject]@{ Success = $false; ResolverConflict = $false }
    }

    $requirementsFrom = 18
    if ($Variant -eq 'cuda' -and [string]::IsNullOrWhiteSpace($WheelhousePath)) {
        $torchVersion = Get-LockPinnedVersion -LockPath $LockPath -Package 'torch'
        $torchaudioVersion = Get-LockPinnedVersion -LockPath $LockPath -Package 'torchaudio'
        if ($torchVersion -and $torchaudioVersion) {
            Write-Step "CUDA-build van torch $torchVersion installeren (NVIDIA)"
            $handler = New-PipProgressHandler -From 18 -To 32 -ExpectedLines 30 -Dutch 'Rekenpakketten voor de NVIDIA-videokaart installeren' -English 'Installing compute packages for the NVIDIA graphics card'
            Set-InstallStatus 18 'Rekenpakketten voor de NVIDIA-videokaart installeren (ongeveer 3 GB)' 'Installing compute packages for the NVIDIA graphics card (about 3 GB)'
            $cudaArgs = @('-m', 'pip', 'install') + $commonArgs + @('--index-url', $script:TorchCudaIndexUrl, ("torch==" + $torchVersion + $script:TorchCudaSuffix), ("torchaudio==" + $torchaudioVersion + $script:TorchCudaSuffix))
            $cudaResult = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList $cudaArgs -StepName 'torch_cuda_install' -OnLine $handler
            if ($cudaResult.ExitCode -ne 0) {
                Add-WarningLine 'De CUDA-build van torch kon niet worden geinstalleerd; Captivox gebruikt dan de processor in plaats van de NVIDIA-videokaart.'
            }
            else {
                Add-Summary "CUDA-build van torch geinstalleerd ($torchVersion$($script:TorchCudaSuffix))."
            }
            $requirementsFrom = 32
        }
        else {
            Add-WarningLine 'torch-versie niet gevonden in de lockfile; de CUDA-build wordt overgeslagen.'
        }
    }

    Write-Step 'Worker-pakketten installeren'
    $pipInstallArgs = @('-m', 'pip', 'install') + $commonArgs
    if (-not [string]::IsNullOrWhiteSpace($WheelhousePath)) {
        Add-Summary "Lokale wheelhouse gebruikt voor worker-installatie: $WheelhousePath"
        $pipInstallArgs += @('--no-index', '--find-links', $WheelhousePath)
    }
    $pipInstallArgs += @('-r', $InstallSpecPath)
    $handler = New-PipProgressHandler -From $requirementsFrom -To 46 -ExpectedLines 230 -Dutch 'Transcriptiepakketten installeren' -English 'Installing transcription packages'
    Set-InstallStatus $requirementsFrom 'Transcriptiepakketten installeren (ongeveer 1 GB)' 'Installing transcription packages (about 1 GB)'
    $installResult = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList $pipInstallArgs -StepName 'worker_requirements_install' -OnLine $handler
    if ($installResult.ExitCode -ne 0) {
        $allOutput = $installResult.StdOut + "`n" + $installResult.StdErr
        $resolverConflict = Test-DependencyResolverConflict -Text $allOutput
        if ($resolverConflict) {
            $summary = Get-DependencyResolverConflictSummary -Text $allOutput
            Add-Blocker "Pakketconflict in $InstallSpecPath; een schone venv helpt hier niet. $summary Zie logboek: $script:LogPath" 'The transcription packages conflict with each other.'
        }
        else {
            Add-Blocker "Installatie van de transcriptiepakketten faalde. Controleer de internetverbinding. Zie logboek: $script:LogPath" 'Installing the transcription packages failed. Check the internet connection.'
        }
        return [pscustomobject]@{ Success = $false; ResolverConflict = $resolverConflict }
    }

    Write-Step 'pip check'
    Set-InstallStatus 46 'Pakketten controleren' 'Checking packages'
    $pipCheck = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList @('-m', 'pip', 'check') -StepName 'pip_check' -TimeoutSeconds 600
    if ($pipCheck.ExitCode -ne 0) {
        Add-Blocker "pip check meldt problemen met de pakketten. Zie logboek: $script:LogPath" 'The installed packages are inconsistent.'
        return [pscustomobject]@{ Success = $false; ResolverConflict = $false }
    }

    Add-Summary "Worker-pakketten geinstalleerd uit: $InstallSpecPath"
    return [pscustomobject]@{ Success = $true; ResolverConflict = $false }
}





function Write-ResolvedWorkerLock([string]$VenvPython, [string]$RealizedLockPath) {
    if ([string]::IsNullOrWhiteSpace($RealizedLockPath)) {
        return $false
    }

    $directory = Split-Path -Parent $RealizedLockPath
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $freezeResult = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList @('-m', 'pip', 'freeze', '--all') -StepName 'pip_freeze_all' -TimeoutSeconds 300
    if ($freezeResult.ExitCode -ne 0) {
        Add-WarningLine "Vastleggen van de gebruikte pakketversies faalde. Logboek: $script:LogPath"
        return $false
    }

    try {
        # Een CUDA-build (bijvoorbeeld torch==2.8.0+cu128) staat niet op PyPI; in de vastgelegde lijst daarom de gewone versie.
        $freezeOutput = ($freezeResult.StdOut -split "`r?`n" | ForEach-Object { $_ -replace '^((?:torch|torchaudio|torchvision)==[0-9\.]+)\+[A-Za-z0-9\.]+$', '$1' }) -join [Environment]::NewLine
        if ([string]::IsNullOrWhiteSpace($freezeOutput)) {
            Add-WarningLine 'De lijst met pakketversies is leeg; vastleggen wordt overgeslagen.'
            return $false
        }

        $header = @(
            '# Resolved Windows worker lock voor Captivox Recorder'
            '# Gegenereerd door install-prereqs.ps1 na een geslaagde worker-bootstrap.'
            '# Gebruik deze file alleen als gevalideerde installatiespecificatie wanneer de onderliggende Windows-run echt bevestigd is.'
            ''
        ) -join [Environment]::NewLine
        [System.IO.File]::WriteAllText($RealizedLockPath, $header + $freezeOutput.Trim() + [Environment]::NewLine, [System.Text.Encoding]::UTF8)
        Add-Summary "Gebruikte pakketversies vastgelegd: $RealizedLockPath"
        return $true
    }
    catch {
        Add-WarningLine "Schrijven van de gebruikte pakketversies faalde: $($_.Exception.Message)"
        return $false
    }
}

function Test-VenvRuntime([string]$VenvPython, [string]$Series) {
    Write-Step 'Worker-omgeving valideren'

    $probePath = New-TemporaryProbeFile -StepName 'worker_import_probe' -Content @'
import importlib
import sys
print("PYTHON_VERSION=" + ".".join(map(str, sys.version_info[:3])))
modules = ["whisperx", "numpy", "pandas", "torch", "torchaudio", "ffmpeg", "pyannote.audio", "faster_whisper", "ctranslate2"]
for name in modules:
    importlib.import_module(name)
import torch
print("TORCH=" + torch.__version__ + " CUDA=" + str(bool(torch.cuda.is_available())))
print("Worker importcheck: OK")
'@
    try {
        $importResult = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList @($probePath) -StepName 'worker_import_probe' -TimeoutSeconds 900
    }
    finally {
        if (Test-Path -LiteralPath $probePath) {
            Remove-Item -LiteralPath $probePath -Force -ErrorAction SilentlyContinue
        }
    }

    $reportedVersion = $null
    $versionLine = ($importResult.StdOut -split "`r?`n" | Where-Object { $_ -match '^PYTHON_VERSION=' } | Select-Object -First 1)
    if ($versionLine) { $reportedVersion = ($versionLine -replace '^PYTHON_VERSION=', '').Trim() }
    if ([string]::IsNullOrWhiteSpace($reportedVersion) -or -not $reportedVersion.StartsWith("$Series.")) {
        Write-Log "Venv gebruikt niet de verwachte Python-serie $Series.x. Gedetecteerd: $reportedVersion"
        return $false
    }

    if ($importResult.ExitCode -ne 0) {
        Write-Log "Worker import-validatie faalde: $(Get-OutputTail $importResult)"
        return $false
    }

    $torchLine = ($importResult.StdOut -split "`r?`n" | Where-Object { $_ -match '^TORCH=' } | Select-Object -First 1)
    if ($torchLine) { Add-Summary "Worker-omgeving geladen ($torchLine)." }
    return $true
}

function Get-VenvMarkerPath([string]$TargetVenv) {
    return Join-Path $TargetVenv 'captivox-runtime.json'
}

function Read-VenvMarker([string]$TargetVenv) {
    $path = Get-VenvMarkerPath -TargetVenv $TargetVenv
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try { return (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { return $null }
}

function Write-VenvMarker([string]$TargetVenv, [string]$LockHash, [string]$Variant) {
    $marker = [pscustomobject]@{
        lock_sha256 = $LockHash
        torch_variant = $Variant
        written_at = [DateTimeOffset]::Now.ToString('o')
    }
    [System.IO.File]::WriteAllText((Get-VenvMarkerPath -TargetVenv $TargetVenv), ($marker | ConvertTo-Json), (New-Object System.Text.UTF8Encoding($false)))
}

function Ensure-Venv([string]$Series, [string]$TargetVenv, [string]$RequirementsPath, [string]$InstallSpecPath, [string]$LockPath, [string]$RealizedLockPath, [string]$BasePython) {
    Set-InstallStatus 14 'Python-omgeving controleren' 'Checking the Python environment'
    if (-not (Test-Path -LiteralPath $RequirementsPath)) {
        Add-WarningLine "requirements.txt niet gevonden: $RequirementsPath"
    }

    if (-not (Test-Path -LiteralPath $InstallSpecPath)) {
        Add-Blocker "Installatiespecificatie ontbreekt: $InstallSpecPath" 'The package list of the transcription runtime is missing. Reinstall Captivox.'
        return $null
    }

    $variant = Get-EffectiveTorchVariant
    $hashSource = $(if (Test-Path -LiteralPath $LockPath) { $LockPath } else { $InstallSpecPath })
    $lockHash = Get-FileSha256 -Path $hashSource
    Add-Summary "torch-build: $variant"

    $attempt = 0
    do {
        $attempt++
        $forceRebuild = $RebuildVenv.IsPresent -or ($attempt -gt 1)
        $venvPython = Join-Path $TargetVenv 'Scripts\python.exe'
        if (-not $forceRebuild -and (Test-Path -LiteralPath $TargetVenv) -and -not (Test-VenvBelongsToPython -TargetVenv $TargetVenv -BasePython $BasePython)) {
            Add-Summary 'De bestaande venv hoort bij een andere Python; hij wordt opnieuw opgebouwd met de eigen Python van Captivox.'
            $forceRebuild = $true
        }
        if ($forceRebuild) {
            Remove-VenvDirectory -TargetVenv $TargetVenv
        }

        if (Test-Path -LiteralPath $venvPython) {
            $marker = Read-VenvMarker -TargetVenv $TargetVenv
            if ($null -ne $marker -and [string]$marker.lock_sha256 -eq $lockHash -and [string]$marker.torch_variant -eq $variant) {
                Set-InstallStatus 40 'Bestaande Python-omgeving controleren' 'Checking the existing Python environment'
                if (Test-VenvRuntime -VenvPython $venvPython -Series $Series) {
                    Add-Summary "Bestaande venv is compleet en geldig: $venvPython"
                    return $venvPython
                }
                Add-Summary 'De bestaande venv laadt niet goed; de pakketten worden opnieuw geinstalleerd.'
            }
        }
        else {
            Set-InstallStatus 15 'Python-omgeving aanmaken' 'Creating the Python environment'
            $venvPython = New-Venv -BasePython $BasePython -TargetVenv $TargetVenv
            if (-not $venvPython) {
                return $null
            }
        }

        $installState = Install-WorkerStack -VenvPython $venvPython -InstallSpecPath $InstallSpecPath -WheelhousePath $script:WheelhousePath -Variant $variant -LockPath $LockPath
        if ($installState.Success) {
            Set-InstallStatus 47 'Python-omgeving testen' 'Testing the Python environment'
            if (Test-VenvRuntime -VenvPython $venvPython -Series $Series) {
                Write-VenvMarker -TargetVenv $TargetVenv -LockHash $lockHash -Variant $variant
                $null = Write-ResolvedWorkerLock -VenvPython $venvPython -RealizedLockPath $RealizedLockPath
                return $venvPython
            }

            if ($attempt -lt 2) {
                Add-WarningLine 'De worker-omgeving laadt niet na de installatie. De venv wordt eenmalig schoon opnieuw opgebouwd.'
                # De vorige fout is na een schone herbouw niet meer blokkerend.
                continue
            }
            Add-Blocker "De transcriptiepakketten laden niet. Zie logboek: $script:LogPath" 'The transcription packages do not load.'
            break
        }
        elseif ($installState.ResolverConflict) {
            break
        }

        if ($attempt -lt 2) {
            Add-WarningLine 'Installatie van de worker-pakketten faalde. De venv wordt eenmalig schoon opnieuw opgebouwd.'
            # Een geslaagde tweede poging maakt de eerste fout ongedaan.
            if ($script:Blockers.Count -gt 0) { $script:Blockers.RemoveAt($script:Blockers.Count - 1) }
        }
    } while ($attempt -lt 2)

    if ($script:Blockers.Count -eq 0) {
        Add-Blocker "De Python-omgeving kon niet worden voltooid. Zie logboek: $script:LogPath" 'The Python environment could not be completed.'
    }
    return $null
}

# ---------- Spraakmodellen ----------

# Welke spraakmodellen de installatie ophaalt: een vast profiel uit de instellingen gaat voor, anders NVIDIA, dan AMD,
# anders de processor. Met 'amd' komt ook het whisper.cpp-model en een proef van de snelle videokaartroute; het
# processormodel wordt altijd opgehaald, want ook met een AMD-kaart schrijft de worker standaard op de processor uit.
function Get-TranscriptionRoute([object]$Settings) {
    $configured = ''
    if ($null -ne $Settings -and $Settings.PSObject.Properties['worker'] -and $null -ne $Settings.worker -and $Settings.worker.PSObject.Properties['transcriptionProfile']) {
        $configured = ([string]$Settings.worker.transcriptionProfile).Trim().ToLowerInvariant()
    }
    switch ($configured) {
        { $_ -in @('nvidia', 'cuda', 'nvidia-cuda') } { return 'nvidia' }
        { $_ -in @('amd', 'amd-gpu', 'rocm', 'hip') } { return 'amd' }
        { $_ -in @('cpu', 'cpu-fallback') } {
            # Alleen een eerder door de installatie gekozen processorroute opnieuw beoordelen.
            if (-not (Test-Path -LiteralPath (Get-GpuRouteMarkerPath))) { return 'cpu' }
        }
    }
    if (Test-NvidiaGpuPresent) { return 'nvidia' }
    if (Test-AmdGpuPresent) { return 'amd' }
    return 'cpu'
}

function Get-WorkerSettingValue([object]$Settings, [string]$Name, [string]$Default) {
    if ($null -ne $Settings -and $Settings.PSObject.Properties['worker'] -and $null -ne $Settings.worker -and $Settings.worker.PSObject.Properties[$Name]) {
        $value = [string]$Settings.worker.$Name
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value.Trim() }
    }
    return $Default
}

function Get-GpuRouteMarkerPath {
    return Join-Path (Get-RuntimeRoot) 'captivox-gpu-route.json'
}

function Ensure-WhisperCppModel {
    Write-Step 'whisper.cpp-model voor de videokaart'
    $target = Join-Path (Get-RuntimeRoot) ('whispercpp\models\' + $script:WhisperCppModelDownload.FileName)
    $ok = Invoke-PinnedDownload -Url $script:WhisperCppModelDownload.Url -Sha256 $script:WhisperCppModelDownload.Sha256 -Destination $target -PercentFrom 50 -PercentTo 58 -Dutch 'Spraakmodel voor de videokaart downloaden' -English 'Downloading the speech model for the graphics card'
    if (-not $ok) { return $null }
    Add-Summary "whisper.cpp-model aanwezig: $target"
    return $target
}

$script:ModelPrefetchPython = @'
# Captivox: spraakmodellen vooraf ophalen en een proeftranscriptie doen, met dezelfde functies als de worker.
import argparse
import gc
import importlib.util
import json
import os
import shutil
import sys
import tempfile
import threading
import traceback
import types
import uuid

STATE = {"phase": "start", "repo_dir": None, "total": 0, "base": 0.0, "span": 0.0, "start_size": 0}
STATE_LOCK = threading.Lock()


def emit(kind, **data):
    print("SETUP_" + kind + " " + json.dumps(data, ensure_ascii=True), flush=True)


def tree_size(path):
    total = 0
    if not path or not os.path.isdir(path):
        return 0
    for root, _dirs, files in os.walk(path):
        for name in files:
            full = os.path.join(root, name)
            try:
                if os.path.islink(full):
                    continue
                total += os.lstat(full).st_size
            except OSError:
                pass
    return total


def monitor(stop_event):
    while not stop_event.wait(2.0):
        with STATE_LOCK:
            snapshot = dict(STATE)
        done = 0
        if snapshot["repo_dir"]:
            done = max(0, tree_size(snapshot["repo_dir"]) - snapshot["start_size"])
        total = snapshot["total"]
        fraction = min(0.99, done / total) if total > 0 else 0.0
        emit("PROGRESS", phase=snapshot["phase"], fraction=round(snapshot["base"] + snapshot["span"] * fraction, 4), done=done, total=total)


def begin_phase(phase, base, span, repo_dir=None, expected=0):
    start_size = tree_size(repo_dir) if repo_dir else 0
    total = max(0, int(expected) - start_size) if expected else 0
    with STATE_LOCK:
        STATE.update(phase=phase, base=base, span=span, repo_dir=repo_dir, total=total, start_size=start_size)
    emit("PROGRESS", phase=phase, fraction=round(base, 4), done=0, total=total)


def repo_cache_dir(hub_cache, repo):
    if not repo:
        return None
    return os.path.join(hub_cache, "models--" + repo.replace("/", "--"))


def expected_size(repo, choose):
    if not repo:
        return 0
    try:
        from huggingface_hub import HfApi
        info = HfApi().model_info(repo, files_metadata=True)
        sizes = {s.rfilename: int(s.size or 0) for s in (info.siblings or [])}
        return choose(sizes)
    except Exception as exc:
        emit("WARN", message="grootte van %s onbekend: %s" % (repo, exc))
        return 0


def whisper_files(sizes):
    keep = ("config.json", "preprocessor_config.json", "model.bin", "tokenizer.json")
    return sum(size for name, size in sizes.items() if name in keep or name.startswith("vocabulary."))


def align_files(sizes):
    weights = sizes.get("model.safetensors") or sizes.get("pytorch_model.bin") or 0
    small = sum(size for name, size in sizes.items() if name.endswith(".json") or name.endswith(".txt"))
    return weights + small


def whisper_repo(model):
    if os.path.isdir(model):
        return None
    if "/" in model:
        return model
    try:
        from faster_whisper.utils import _MODELS
        return _MODELS.get(model)
    except Exception:
        return None


def load_worker(path):
    spec = importlib.util.spec_from_file_location("captivox_worker_helper", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def amd_gpu_test(worker, args, wav):
    ns = types.SimpleNamespace(ffmpeg_path=args.ffmpeg, whispercpp_model=args.whispercpp_model, language=args.language)
    probe = worker.build_ffmpeg_whisper_probe(ns)
    if not probe.get("available"):
        return False, str(probe.get("reason"))
    model = worker.resolve_whispercpp_model(ns)
    runtime_root = worker.resolve_whispercpp_runtime_root_from_model(model)
    run_dir = os.path.join(runtime_root, "runs", "captivox_setup_" + uuid.uuid4().hex)
    os.makedirs(run_dir, exist_ok=True)
    try:
        output_json = os.path.join(run_dir, "setup_test.json")
        command, cwd = worker.build_ffmpeg_whisper_command(ns, wav, output_json)
        result = worker.run_process_capture(command, timeout_seconds=900, cwd=cwd, env=worker.build_amd_gpu_process_env(ns))
        log = ((result.get("stdout") or "") + "\n" + (result.get("stderr") or "")).lower()
        confirmed = "ggml_vulkan: found" in log and "vulkan0" in log
        if int(result.get("exit_code") or 0) != 0 or result.get("error"):
            tail = " | ".join([line for line in log.splitlines() if line.strip()][-4:])
            return False, "exitcode %s %s: %s" % (result.get("exit_code"), result.get("error") or "", tail)
        if not confirmed:
            return False, "FFmpeg Whisper draaide, maar gebruik van de videokaart is niet bevestigd."
        return True, "FFmpeg Whisper met Vulkan werkt."
    finally:
        shutil.rmtree(run_dir, ignore_errors=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--worker-script", required=True)
    parser.add_argument("--ffmpeg", required=True)
    parser.add_argument("--data-root", required=True)
    parser.add_argument("--mode", choices=["cpu", "nvidia", "amd"], default="cpu")
    parser.add_argument("--model", default="")
    parser.add_argument("--language", default="nl")
    parser.add_argument("--whispercpp-model", default="")
    args = parser.parse_args()

    os.environ["CAPTIVOX_WORKER_DATA_ROOT"] = args.data_root
    result = {"mode": args.mode, "language": args.language, "ok": False}

    # Andere FFmpeg-mappen uit PATH: de proef moet laten zien dat de worker met alleen de eigen FFmpeg werkt.
    own_dir = os.path.normcase(os.path.dirname(os.path.abspath(args.ffmpeg)))
    kept = []
    for entry in os.environ.get("PATH", "").split(os.pathsep):
        if not entry:
            continue
        try:
            has_ffmpeg = os.path.isfile(os.path.join(entry, "ffmpeg.exe"))
        except OSError:
            has_ffmpeg = False
        if has_ffmpeg and os.path.normcase(os.path.abspath(entry)) != own_dir:
            continue
        kept.append(entry)
    os.environ["PATH"] = os.pathsep.join(kept)

    stop_event = threading.Event()
    thread = threading.Thread(target=monitor, args=(stop_event,), daemon=True)
    thread.start()
    test_dir = tempfile.mkdtemp(prefix="captivox_setup_")
    try:
        begin_phase("import", 0.0, 0.0)
        worker = load_worker(args.worker_script)
        worker.ensure_ffmpeg(args.ffmpeg)
        import torch
        worker.configure_torch_checkpoint_compatibility(torch)
        import whisperx
        from huggingface_hub import constants as hf_constants
        hub_cache = hf_constants.HF_HUB_CACHE
        result["hf_hub_cache"] = hub_cache
        result["torch"] = torch.__version__
        result["cuda_available"] = bool(torch.cuda.is_available())
        emit("INFO", hf_hub_cache=hub_cache, torch=torch.__version__, cuda_available=result["cuda_available"])

        wav = os.path.join(test_dir, "captivox_setup_test.wav")
        worker.write_test_wav(wav, seconds=2.0)

        # Het processormodel is er altijd: ook met een AMD-kaart schrijft Captivox standaard op de processor uit; de
        # videokaartroute (FFmpeg Whisper met Vulkan) is alleen de snelle keuze die de gebruiker zelf aanzet.
        need_cpu_model = True
        whisper_base = 0.0
        if args.mode == "amd":
            begin_phase("gpu_test", 0.0, 0.1)
            gpu_ok, detail = amd_gpu_test(worker, args, wav)
            result["amd_gpu_route_ok"] = bool(gpu_ok)
            result["amd_gpu_detail"] = detail
            emit("INFO", amd_gpu_route_ok=bool(gpu_ok), detail=detail)
            whisper_base = 0.1

        if need_cpu_model:
            model = args.model or ("large-v2" if args.mode == "nvidia" else "large-v3-turbo")
            result["model"] = model
            repo = whisper_repo(model)
            result["model_repo"] = repo
            repo_dir = repo_cache_dir(hub_cache, repo)
            begin_phase("whisper_model", whisper_base, 0.6 - whisper_base, repo_dir, expected_size(repo, whisper_files))
            asr = whisperx.load_model(model, "cpu", compute_type="int8", language=args.language)
            begin_phase("transcribe_test", 0.6, 0.05)
            audio = whisperx.load_audio(wav)
            output = asr.transcribe(audio, batch_size=1, language=args.language)
            result["cpu_transcribe_ok"] = True
            result["cpu_test_segments"] = len(output.get("segments", []))
            del asr
            gc.collect()
            if args.mode == "nvidia" and result["cuda_available"]:
                begin_phase("cuda_test", 0.65, 0.05)
                try:
                    gpu_asr = whisperx.load_model(model, "cuda", compute_type="float16", language=args.language)
                    gpu_asr.transcribe(audio, batch_size=1, language=args.language)
                    result["cuda_transcribe_ok"] = True
                    del gpu_asr
                except Exception as exc:
                    result["cuda_transcribe_ok"] = False
                    result["cuda_detail"] = str(exc)[:500]
                    emit("WARN", message="Proef op de NVIDIA-videokaart mislukt: %s" % exc)
                finally:
                    gc.collect()
                    try:
                        torch.cuda.empty_cache()
                    except Exception:
                        pass

        align_repo = None
        try:
            from whisperx.alignment import DEFAULT_ALIGN_MODELS_HF
            align_repo = DEFAULT_ALIGN_MODELS_HF.get(args.language)
        except Exception:
            align_repo = None
        result["align_model"] = align_repo
        begin_phase("align_model", 0.7, 0.28, repo_cache_dir(hub_cache, align_repo), expected_size(align_repo, align_files))
        align_model, _metadata = whisperx.load_align_model(language_code=args.language, device="cpu")
        del align_model
        gc.collect()

        # transformers haalt bij dit model op de achtergrond nog een safetensors-versie op (ruim 1 GB); wachten tot die
        # klaar is, zodat de eerste echte transcriptie niets meer hoeft te downloaden.
        begin_phase("align_finish", 0.98, 0.0)
        for other in threading.enumerate():
            if other is not threading.current_thread() and other is not thread and not other.daemon and other.is_alive():
                other.join(timeout=3600)

        begin_phase("finish", 1.0, 0.0)
        result["ok"] = True
        emit("RESULT", **result)
        return 0
    except Exception as exc:
        traceback.print_exc()
        result["error"] = ("%s: %s" % (type(exc).__name__, exc))[:800]
        emit("RESULT", **result)
        return 1
    finally:
        stop_event.set()
        shutil.rmtree(test_dir, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
'@

function Get-PrefetchPhaseText([string]$Phase, [long]$Done, [long]$Total) {
    $sizeNl = ''
    $sizeEn = ''
    if ($Total -gt 0 -and $Done -gt 0) {
        $sizeNl = " ($(Format-Gigabytes $Done) van $(Format-Gigabytes $Total) GB)"
        $sizeEn = " ($(Format-Gigabytes $Done) of $(Format-Gigabytes $Total) GB)"
    }
    switch ($Phase) {
        'import' { return @('Transcriptie-omgeving laden', 'Loading the transcription runtime') }
        'gpu_test' { return @('Videokaart testen', 'Testing the graphics card') }
        'whisper_model' { return @("Spraakmodel downloaden en laden$sizeNl", "Downloading and loading the speech model$sizeEn") }
        'transcribe_test' { return @('Proeftranscriptie', 'Test transcription') }
        'cuda_test' { return @('Proeftranscriptie op de NVIDIA-videokaart', 'Test transcription on the NVIDIA graphics card') }
        'align_model' { return @("Taalmodel voor tijdcodes downloaden en laden$sizeNl", "Downloading and loading the alignment model$sizeEn") }
        'align_finish' { return @('Taalmodel voor tijdcodes afronden', 'Finishing the alignment model') }
        'finish' { return @('Spraakmodellen klaar', 'Speech models ready') }
        default { return @('Spraakmodellen voorbereiden', 'Preparing the speech models') }
    }
}

# Haalt de spraakmodellen op in de gewone Hugging Face-cache (dezelfde plek die de worker later gebruikt) en doet een
# proeftranscriptie. Geeft het resultaat van de proef terug, of $null bij een fout.
function Invoke-ModelPrefetch([string]$VenvPython, [string]$FfmpegPath, [string]$Route, [string]$Model, [string]$Language, [string]$WhisperCppModel, [int]$From, [int]$To) {
    Write-Step "Spraakmodellen voorbereiden (route: $Route)"
    $workerScript = Join-Path $bundleRoot 'workers\transcribe_with_whisperx_recorder.py'
    if (-not (Test-Path -LiteralPath $workerScript)) {
        Add-Blocker "Het transcriptiescript ontbreekt: $workerScript" 'The transcription script is missing. Reinstall Captivox.'
        return $null
    }

    $script:PrefetchResult = $null
    $probePath = New-TemporaryProbeFile -StepName 'captivox_model_prefetch' -Content $script:ModelPrefetchPython
    $arguments = @('-X', 'utf8', $probePath, '--worker-script', $workerScript, '--ffmpeg', $FfmpegPath, '--data-root', (Get-AppDataRoot), '--mode', $Route, '--language', $Language)
    if (-not [string]::IsNullOrWhiteSpace($Model)) { $arguments += @('--model', $Model) }
    if (-not [string]::IsNullOrWhiteSpace($WhisperCppModel)) { $arguments += @('--whispercpp-model', $WhisperCppModel) }

    $script:PrefetchRange = @{ From = $From; To = $To }
    $onLine = {
        param($line, $isError)
        if ($line.StartsWith('SETUP_PROGRESS ')) {
            $data = $line.Substring(15) | ConvertFrom-Json
            $texts = Get-PrefetchPhaseText -Phase ([string]$data.phase) -Done ([long]$data.done) -Total ([long]$data.total)
            $range = $script:PrefetchRange
            Set-InstallStatus ([int]($range.From + ($range.To - $range.From) * [double]$data.fraction)) $texts[0] $texts[1]
        }
        elseif ($line.StartsWith('SETUP_RESULT ')) {
            $script:PrefetchResult = $line.Substring(13) | ConvertFrom-Json
        }
    }

    try {
        $result = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList $arguments -StepName 'model_prefetch' -OnLine $onLine
    }
    finally {
        Remove-Item -LiteralPath $probePath -Force -ErrorAction SilentlyContinue
    }

    $prefetch = $script:PrefetchResult
    if ($result.ExitCode -ne 0 -or $null -eq $prefetch -or -not $prefetch.ok) {
        $detail = $(if ($null -ne $prefetch -and $prefetch.PSObject.Properties['error']) { [string]$prefetch.error } else { Get-OutputTail $result })
        Add-Blocker "De spraakmodellen konden niet worden opgehaald of de proeftranscriptie mislukte: $detail Zie logboek: $script:LogPath" 'The speech models could not be downloaded, or the test transcription failed. Check the internet connection.'
        return $null
    }

    if ($prefetch.PSObject.Properties['model'] -and $prefetch.model) { Add-Summary "Whisper-model klaar voor de processor: $($prefetch.model) (proeftranscriptie geslaagd)." }
    if ($prefetch.PSObject.Properties['align_model'] -and $prefetch.align_model) { Add-Summary "Taalmodel voor tijdcodes klaar: $($prefetch.align_model)" }
    if ($prefetch.PSObject.Properties['hf_hub_cache']) { Add-Summary "Spraakmodellen staan in: $($prefetch.hf_hub_cache)" }
    if ($prefetch.PSObject.Properties['amd_gpu_route_ok']) {
        if ($prefetch.amd_gpu_route_ok) { Add-Summary 'Snelle route via de AMD-videokaart (FFmpeg Whisper met Vulkan) getest en bruikbaar; standaard schrijft Captivox uit op de processor.' }
        else { Add-Summary "De snelle route via de AMD-videokaart werkt op deze pc niet ($($prefetch.amd_gpu_detail)); Captivox schrijft uit op de processor." }
    }
    if ($prefetch.PSObject.Properties['cuda_transcribe_ok'] -and -not $prefetch.cuda_transcribe_ok) {
        Add-WarningLine 'De proef op de NVIDIA-videokaart mislukte; de worker valt dan terug op de processor.'
    }
    return $prefetch
}

# ---------- App-instellingen ----------

function Get-AppSettingsPath {
    return Join-Path (Get-AppDataRoot) 'settings\application_settings.json'
}

function Set-ObjectProperty([object]$Target, [string]$Name, [object]$Value) {
    $property = $Target.PSObject.Properties[$Name]
    if ($null -ne $property) {
        $Target.$Name = $Value
    }
    else {
        $Target | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
    }
}

function Ensure-ChildObject([object]$Target, [string]$Name) {
    $property = $Target.PSObject.Properties[$Name]
    if ($null -ne $property -and $null -ne $Target.$Name) {
        return $Target.$Name
    }

    $child = [pscustomobject]@{}
    Set-ObjectProperty -Target $Target -Name $Name -Value $child
    return $child
}


function Test-ExistingPathValue([object]$Target, [string]$Name) {
    $property = $Target.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $false
    }

    $value = [string]$Target.$Name
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $false
    }

    return (Test-Path -LiteralPath $value)
}

function Set-SettingsPathValue([object]$Target, [string]$Name, [string]$Value, [bool]$OverwriteExisting, [string]$SummaryName) {
    $existingIsValid = Test-ExistingPathValue -Target $Target -Name $Name
    if ($existingIsValid -and -not $OverwriteExisting) {
        Add-Summary "Bestaande $SummaryName behouden in application_settings.json"
        return $false
    }

    Set-ObjectProperty -Target $Target -Name $Name -Value $Value
    Add-Summary "$SummaryName toegepast in application_settings.json"
    return $true
}

# Leest een bestaand application_settings.json zodat alleen worker-sleutels worden bijgewerkt
# en alle andere instellingen blijven staan. Zonder bestand begint het met een leeg object;
# de recorder vult ontbrekende waarden zelf met zijn standaardwaarden.
function Read-ExistingApplicationSettings {
    $settingsPath = Get-AppSettingsPath
    if (Test-Path -LiteralPath $settingsPath) {
        try {
            $existing = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($null -ne $existing) {
                return $existing
            }
        }
        catch {
            Add-WarningLine "Bestaande application_settings.json kon niet worden gelezen. Een nieuwe configuratie wordt opgebouwd. Fout: $($_.Exception.Message)"
        }
    }

    return [pscustomobject]@{}
}

# $GpuRoute: 'gpu-ok' (videokaartroute getest), 'gpu-failed' (videokaart gevonden, route werkt niet) of '' (niets te doen).
function Configure-CaptivoxSettings([string]$VenvPython, [string]$FFmpegPath, [bool]$OverwriteExisting, [string]$WhisperCppModelPath = '', [string]$GpuRoute = '') {
    $settings = Read-ExistingApplicationSettings
    $worker = Ensure-ChildObject -Target $settings -Name 'worker'

    $changed = $false
    if (Set-SettingsPathValue -Target $worker -Name 'pythonExe' -Value $VenvPython -OverwriteExisting $OverwriteExisting -SummaryName 'Python/venv-pad') {
        $changed = $true
    }

    # Een andere, geldige FFmpeg blijft staan, behalve als die de whisper-GPU-filter mist en de videokaartroute nodig is.
    $overwriteFfmpeg = $OverwriteExisting
    if (-not $overwriteFfmpeg -and $GpuRoute -eq 'gpu-ok' -and (Test-ExistingPathValue -Target $worker -Name 'ffmpegPath')) {
        $existingFfmpeg = [string]$worker.ffmpegPath
        if (-not [string]::Equals([System.IO.Path]::GetFullPath($existingFfmpeg), [System.IO.Path]::GetFullPath($FFmpegPath), [System.StringComparison]::OrdinalIgnoreCase) -and -not (Test-FfmpegWhisperGpuFilter -Path $existingFfmpeg)) {
            Add-Summary "De ingestelde FFmpeg ($existingFfmpeg) mist de whisper-GPU-filter; de eigen FFmpeg wordt ingesteld."
            $overwriteFfmpeg = $true
        }
    }
    if (Set-SettingsPathValue -Target $worker -Name 'ffmpegPath' -Value $FFmpegPath -OverwriteExisting $overwriteFfmpeg -SummaryName 'FFmpeg-pad') {
        $changed = $true
    }

    if (-not [string]::IsNullOrWhiteSpace($WhisperCppModelPath)) {
        if (Set-SettingsPathValue -Target $worker -Name 'whisperCppModelPath' -Value $WhisperCppModelPath -OverwriteExisting $OverwriteExisting -SummaryName 'whisper.cpp-modelpad') {
            $changed = $true
        }
    }

    # Het transcriptieprofiel blijft van de gebruiker: automatisch kiest NVIDIA via CUDA en anders de processor, ook met
    # een AMD-kaart; de snelle AMD-route zet de gebruiker zelf aan. Eerdere installaties zetten bij een mislukte
    # videokaartproef het profiel op de processor en onthielden dat in een markeerbestand: dat gaat terug naar automatisch.
    $markerPath = Get-GpuRouteMarkerPath
    $profile = $(if ($worker.PSObject.Properties['transcriptionProfile']) { ([string]$worker.transcriptionProfile).Trim().ToLowerInvariant() } else { '' })
    if (Test-Path -LiteralPath $markerPath) {
        if ($profile -in @('cpu', 'cpu-fallback')) {
            Set-ObjectProperty -Target $worker -Name 'transcriptionProfile' -Value 'auto'
            Add-Summary 'Transcriptieprofiel terug op automatisch (een eerdere installatie had het op de processor gezet).'
            $changed = $true
        }
        Remove-Item -LiteralPath $markerPath -Force -ErrorAction SilentlyContinue
    }


    $settingsPath = Get-AppSettingsPath
    $settingsDirectory = Split-Path -Parent $settingsPath
    if (-not (Test-Path -LiteralPath $settingsDirectory)) {
        New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    }

    if (-not $changed) {
        Add-Summary "App-instellingen niet overschreven; bestaande geldige waarden blijven staan: $settingsPath"
        return $settingsPath
    }

    $json = $settings | ConvertTo-Json -Depth 20
    $temporary = $settingsPath + '.tmp'
    [System.IO.File]::WriteAllText($temporary, $json, [System.Text.Encoding]::UTF8)
    Move-Item -LiteralPath $temporary -Destination $settingsPath -Force

    Add-Summary "App-instellingen bijgewerkt: $settingsPath"
    return $settingsPath
}

# ---------- Handmatig: whisper.cpp met Vulkan zelf bouwen (alleen met -PrepareAmdGpuRuntime, nooit onbewaakt) ----------

function Invoke-ManualAmdGpuRuntimeBuild([string]$BundleRoot) {
    $runtimeRoot = Join-Path (Get-RuntimeRoot) 'whispercpp'
    $testScript = Join-Path $BundleRoot 'tools\Test-WhisperCppVulkanRuntime.ps1'
    $buildScript = Join-Path $BundleRoot 'tools\Build-WhisperCppVulkanRuntime.ps1'
    if (-not (Test-Path -LiteralPath $buildScript) -or -not (Test-Path -LiteralPath $testScript)) {
        Add-WarningLine "Bouw- of testscript voor de AMD-runtime ontbreekt in $BundleRoot\tools."
        return
    }

    try {
        Write-Step 'whisper.cpp met Vulkan bouwen (handmatige optie)'
        & $buildScript -Clean -RuntimeRoot $runtimeRoot
        & $testScript -RuntimeRoot $runtimeRoot
        Add-Summary 'whisper.cpp met Vulkan is gebouwd en getest.'
    }
    catch {
        Add-WarningLine "whisper.cpp met Vulkan kon niet worden gebouwd: $($_.Exception.Message)"
    }
}

# ---------- Hoofdlijn ----------

$bundleRoot = $null
$script:ExitCode = 1
$venvPython = $null
$ffmpegPath = $null
$appSettingsPath = $null
try {
    Initialize-InstallLog
    Write-PidFile
    Set-InstallStatus 1 'Voorbereiden' 'Preparing'

    $bundle = Resolve-BundleContext
    $bundleRoot = $bundle.Root
    $requirementsPath = $bundle.Requirements
    $installSpecPath = $bundle.InstallSpec
    if ([string]::IsNullOrWhiteSpace($VenvPath)) {
        $VenvPath = Get-DefaultVenvPath
    }

    Write-Host '== Captivox transcriptie-omgeving' -ForegroundColor Green
    Write-Log "Captivox transcriptie-omgeving; onbewaakt=$([bool]$Unattended); PowerShell $($PSVersionTable.PSVersion)"
    Write-Log "Bundel: $bundleRoot"
    Write-Log "Installatiespecificatie: $installSpecPath"
    Write-Log "Gegevensmap: $(Get-AppDataRoot)"

    if (-not (Enter-InstallLock)) { Stop-Install }

    $script:BundledPythonPath = Resolve-BundledPythonPath -BundleRoot $bundleRoot -Series $PythonSeries
    $script:WheelhousePath = Resolve-WheelhousePath -BundleRoot $bundleRoot

    Run-PreflightChecks -BundleRoot $bundleRoot -TargetVenv $VenvPath
    if ($script:Blockers.Count -gt 0) { Stop-Install }

    $basePython = Ensure-PrivatePython -Series $PythonSeries
    if (-not $basePython) { Stop-Install }

    $ffmpegPath = Ensure-PrivateFfmpeg
    if (-not $ffmpegPath) { Stop-Install }

    $venvPython = Ensure-Venv -Series $PythonSeries -TargetVenv $VenvPath -RequirementsPath $requirementsPath -InstallSpecPath $installSpecPath -LockPath $bundle.LockSpec -RealizedLockPath $bundle.RealizedLockSpec -BasePython $basePython
    if (-not $venvPython) { Stop-Install }

    $existingSettings = Read-ExistingApplicationSettings
    $route = Get-TranscriptionRoute -Settings $existingSettings
    $language = Get-WorkerSettingValue -Settings $existingSettings -Name 'language' -Default 'nl'
    if ($language -eq 'auto') { $language = 'nl' }
    Add-Summary "Transcriptieroute: $route; taal: $language"

    $whisperCppModelPath = ''
    $gpuRoute = ''
    if ($SkipModelPrefetch) {
        Add-Summary 'Spraakmodellen niet vooraf opgehaald (-SkipModelPrefetch); de eerste transcriptie haalt ze op.'
    }
    else {
        if ($route -eq 'amd') {
            $whisperCppModelPath = Ensure-WhisperCppModel
            if (-not $whisperCppModelPath) { Stop-Install }
        }
        # Zonder -PrefetchModel: het model dat de worker op deze route gebruikt (videokaartmodel bij NVIDIA, anders het
        # lichtere processormodel uit de instellingen).
        $prefetchModelName = $PrefetchModel
        if ([string]::IsNullOrWhiteSpace($prefetchModelName)) {
            $prefetchModelName = $(if ($route -eq 'nvidia') { Get-WorkerSettingValue -Settings $existingSettings -Name 'whisperXModel' -Default 'large-v2' } else { Get-WorkerSettingValue -Settings $existingSettings -Name 'cpuWhisperXModel' -Default 'large-v3-turbo' })
        }
        $prefetch = Invoke-ModelPrefetch -VenvPython $venvPython -FfmpegPath $ffmpegPath -Route $route -Model $prefetchModelName -Language $language -WhisperCppModel $whisperCppModelPath -From 58 -To 96
        if ($null -eq $prefetch) { Stop-Install }
        if ($route -eq 'amd') {
            $gpuRoute = $(if ($prefetch.PSObject.Properties['amd_gpu_route_ok'] -and $prefetch.amd_gpu_route_ok) { 'gpu-ok' } else { 'gpu-failed' })
        }
    }

    if (-not $SkipAppSettings) {
        Write-Step 'App-instellingen bijwerken'
        Set-InstallStatus 97 'Instellingen opslaan' 'Saving settings'
        $appSettingsPath = Configure-CaptivoxSettings -VenvPython $venvPython -FFmpegPath $ffmpegPath -OverwriteExisting ([bool]$OverwriteExistingAppSettings) -WhisperCppModelPath $whisperCppModelPath -GpuRoute $gpuRoute
    }

    if ($PrepareAmdGpuRuntime) {
        if ($Unattended) {
            Add-WarningLine '-PrepareAmdGpuRuntime wordt in de onbewaakte installatie overgeslagen (vraagt bouwgereedschap); de FFmpeg-whisper-route is al ingericht.'
        }
        else {
            Invoke-ManualAmdGpuRuntimeBuild -BundleRoot $bundleRoot
        }
    }
}
catch {
    if ([string]$_.Exception.Message -ne $script:StopMarker) {
        Add-Blocker "Onverwachte fout: $($_.Exception.Message)" 'An unexpected error occurred.'
        Write-Log ($_ | Out-String)
    }
}
finally {
    Write-Host ''
    Write-Host 'Overzicht' -ForegroundColor Cyan
    if ($venvPython) { Write-Host "- Python/venv-pad: $venvPython" }
    if ($ffmpegPath) { Write-Host "- ffmpeg-pad: $ffmpegPath" }
    if ($appSettingsPath) { Write-Host "- application_settings.json: $appSettingsPath" }
    foreach ($line in $script:Summary) {
        Write-Host "- $line"
    }
    Write-Host "- Logboek: $script:LogPath"

    if ($script:Warnings.Count -gt 0) {
        Write-Host ''
        Write-Host 'Waarschuwingen' -ForegroundColor Yellow
        foreach ($line in $script:Warnings) {
            Write-Host "- $line"
        }
    }

    if ($script:Blockers.Count -gt 0) {
        Write-Host ''
        Write-Host 'Blokkerende punten' -ForegroundColor Yellow
        foreach ($line in $script:Blockers) {
            Write-Host "- $line"
        }
        $script:ExitCode = 1
        $english = $(if ([string]::IsNullOrWhiteSpace($script:FirstBlockerEnglish)) { 'The transcription runtime could not be completed.' } else { $script:FirstBlockerEnglish })
        Write-FinalStatus -ExitCode 1 -Dutch $script:Blockers[0] -English $english
    }
    else {
        $script:ExitCode = 0
        Write-Host ''
        Write-Host 'Resultaat: de transcriptie-omgeving is klaar.' -ForegroundColor Green
        Write-FinalStatus -ExitCode 0 -Dutch 'De transcriptie-omgeving is klaar.' -English 'The transcription runtime is ready.'
    }

    Exit-InstallLock
    if ($null -ne $script:LogWriter) {
        try { $script:LogWriter.Dispose() } catch { }
        $script:LogWriter = $null
    }
}
exit $script:ExitCode
