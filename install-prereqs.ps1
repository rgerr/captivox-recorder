[CmdletBinding()]
param(
    [string]$PythonSeries = '3.10',
    [string]$VenvPath = '',
    [switch]$SkipAppSettings,
    [switch]$OverwriteExistingAppSettings,
    [switch]$RebuildVenv,
    [switch]$PrepareAmdGpuRuntime
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $true
}

$script:Warnings = New-Object System.Collections.Generic.List[string]
$script:Blockers = New-Object System.Collections.Generic.List[string]
$script:Summary = New-Object System.Collections.Generic.List[string]
$script:InstallLogRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'CaptivoxRecorderFreePrereqs'
New-Item -ItemType Directory -Path $script:InstallLogRoot -Force | Out-Null

function Add-WarningLine([string]$Message) {
    Write-Warning $Message
    $script:Warnings.Add($Message) | Out-Null
}

function Add-Blocker([string]$Message) {
    Write-Host $Message -ForegroundColor Yellow
    $script:Blockers.Add($Message) | Out-Null
}

function Add-Summary([string]$Message) {
    $script:Summary.Add($Message) | Out-Null
}

# De gratis editie gebruikt een eigen map, los van een eventuele andere Captivox-installatie.
# CAPTIVOX_APPDATA_ROOT overschrijft die map, net als in de recorder en de worker.
function Get-AppDataRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:CAPTIVOX_APPDATA_ROOT)) {
        return [string]$env:CAPTIVOX_APPDATA_ROOT
    }
    return Join-Path $env:LOCALAPPDATA 'CaptivoxRecorderFree'
}

function Get-DefaultVenvPath {
    return Join-Path (Get-AppDataRoot) 'runtime\.venv'
}

function Get-BundledPythonCandidates([string]$BundleRoot) {
    return @(
        (Join-Path $BundleRoot 'runtime\python\python.exe'),
        (Join-Path $BundleRoot 'runtime\python310\python.exe'),
        (Join-Path $BundleRoot 'python\python.exe'),
        (Join-Path $BundleRoot 'tools\python\python.exe')
    )
}

function Resolve-BundledPythonPath([string]$BundleRoot, [string]$Series) {
    foreach ($candidate in Get-BundledPythonCandidates -BundleRoot $BundleRoot) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        $result = Invoke-NativeLogged -FilePath $candidate -ArgumentList @('--version') -StepName 'bundled_python_version'
        if ($result.ExitCode -ne 0) { continue }
        $versionText = (($result.StdOut + "`n" + $result.StdErr) -split "`r?`n" | Where-Object { $_ -match '^Python\s+\d+\.\d+' } | Select-Object -First 1)
        if (-not [string]::IsNullOrWhiteSpace($versionText) -and $versionText -match "Python\s+$([regex]::Escape($Series))\.") {
            return $candidate
        }
    }
    return $null
}

function Resolve-BundledFfmpegPath([string]$BundleRoot) {
    $candidates = @(
        (Join-Path $BundleRoot 'runtime\ffmpeg\ffmpeg.exe'),
        (Join-Path $BundleRoot 'runtime\ffmpeg\bin\ffmpeg.exe'),
        (Join-Path $BundleRoot 'ffmpeg\ffmpeg.exe'),
        (Join-Path $BundleRoot 'tools\ffmpeg\bin\ffmpeg.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
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

function Run-PreflightChecks([string]$BundleRoot, [string]$TargetVenv) {
    Write-Host '== Preflight controleren' -ForegroundColor Cyan

    try {
        $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
        Add-Summary "Windows-versie gedetecteerd: $($os.Caption) ($($os.Version))"
    }
    catch {
        Add-WarningLine "Windows-versie kon niet worden gelezen: $($_.Exception.Message)"
    }

    if ([Environment]::Is64BitOperatingSystem) {
        Add-Summary '64-bit Windows bevestigd.'
    }
    else {
        Add-Blocker 'Captivox Recorder verwacht een 64-bit Windows-omgeving.'
    }

    $venvParent = Split-Path -Parent $TargetVenv
    if (-not (Test-PathWritable -PathToCheck $venvParent)) {
        Add-Blocker "Doelpad voor de Python-runtime is niet beschrijfbaar: $venvParent"
    }
    else {
        Add-Summary "Doelpad voor de Python-runtime is beschrijfbaar: $venvParent"
    }

    $settingsParent = Split-Path -Parent (Get-AppSettingsPath)
    if (-not (Test-PathWritable -PathToCheck $settingsParent)) {
        Add-Blocker "Settingspad is niet beschrijfbaar: $settingsParent"
    }
    else {
        Add-Summary "Settingspad is beschrijfbaar: $settingsParent"
    }

    try {
        $driveRoot = [System.IO.Path]::GetPathRoot($TargetVenv)
        if (-not [string]::IsNullOrWhiteSpace($driveRoot)) {
            $driveName = $driveRoot.TrimEnd('\').TrimEnd(':')
            $drive = Get-PSDrive -Name $driveName -ErrorAction Stop
            $freeGb = [math]::Round($drive.Free / 1GB, 2)
            Add-Summary "Vrije schijfruimte bij Python-runtime: ${freeGb} GB"
            if ($freeGb -lt 8) {
                Add-WarningLine 'Vrije schijfruimte is krap voor een volledige Windows 11 bootstrap en toekomstige installerpayload.'
            }
        }
    }
    catch {
        Add-WarningLine "Vrije schijfruimte kon niet worden bepaald: $($_.Exception.Message)"
    }

    try {
        $gpuNames = Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop | ForEach-Object { $_.Name } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        if ($gpuNames) {
            Add-Summary "Video-adapters gedetecteerd: $([string]::Join('; ', $gpuNames))"
        }
    }
    catch {
        Add-WarningLine "GPU-detectie in preflight kon niet worden gelezen: $($_.Exception.Message)"
    }
}

function Confirm-Yes([string]$Question) {
    $answer = Read-Host "$Question [Y/n]"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $true }
    return $answer.Trim().ToLowerInvariant() -in @('y', 'yes', 'j', 'ja')
}

function Resolve-BundleContext {
    $start = Get-Item -LiteralPath (Split-Path -Parent $PSCommandPath)

    $relativeCandidates = @(
        'workers\requirements.txt'
    )

    $current = $start
    for ($i = 0; $i -lt 10 -and $null -ne $current; $i++) {
        foreach ($rel in $relativeCandidates) {
            $candidate = Join-Path $current.FullName $rel
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

function New-StepLogPath([string]$StepName) {
    $safe = ($StepName -replace '[^a-zA-Z0-9\-_]+', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($safe)) {
        $safe = 'step'
    }
    return Join-Path $script:InstallLogRoot ("{0}_{1:yyyyMMdd_HHmmss_fff}.log" -f $safe, [DateTime]::Now)
}

function Format-NativeArgument([string]$Value) {
    if ($null -eq $Value) { return '""' }
    if ($Value -notmatch '[\s\"]') { return $Value }
    return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Join-NativeArguments([string[]]$Values) {
    if ($null -eq $Values -or $Values.Count -eq 0) {
        return ''
    }

    return (($Values | ForEach-Object { Format-NativeArgument $_ }) -join ' ')
}

function Write-LogContent([string]$LogPath, [string]$Content) {
    $normalized = $Content
    if ($null -eq $normalized) {
        $normalized = ''
    }
    [System.IO.File]::WriteAllText($LogPath, $normalized, [System.Text.UTF8Encoding]::new($false))
}

function New-TemporaryProbeFile([string]$StepName, [string]$Content) {
    $probeSafe = ($StepName -replace '[^a-zA-Z0-9\-_]+', '_').Trim('_')
    if ([string]::IsNullOrWhiteSpace($probeSafe)) {
        $probeSafe = 'probe'
    }
    $probePath = Join-Path $script:InstallLogRoot ("{0}_{1:yyyyMMdd_HHmmss_fff}.py" -f $probeSafe, [DateTime]::Now)
    [System.IO.File]::WriteAllText($probePath, $Content, [System.Text.UTF8Encoding]::new($false))
    return $probePath
}

function Invoke-NativeLogged {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory = $true)][string]$StepName
    )

    $logPath = New-StepLogPath -StepName $StepName

    try {
        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = $FilePath
        $psi.Arguments = Join-NativeArguments -Values $ArgumentList
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.WorkingDirectory = $bundleRoot

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $psi
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()

        $combined = New-Object System.Collections.Generic.List[string]
        $combined.Add("COMMAND: $FilePath $(Join-NativeArguments -Values $ArgumentList)") | Out-Null
        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            $combined.Add('--- STDOUT ---') | Out-Null
            $combined.Add($stdout.TrimEnd("`r", "`n")) | Out-Null
        }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            $combined.Add('--- STDERR ---') | Out-Null
            $combined.Add($stderr.TrimEnd("`r", "`n")) | Out-Null
        }
        $combined.Add("EXITCODE: $($process.ExitCode)") | Out-Null
        Write-LogContent -LogPath $logPath -Content (($combined -join [Environment]::NewLine) + [Environment]::NewLine)

        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            $stdout.TrimEnd("`r", "`n") | Out-Host
        }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            $stderr.TrimEnd("`r", "`n") | Out-Host
        }

        return [pscustomobject]@{
            ExitCode = [int]$process.ExitCode
            LogPath = $logPath
            StdOut = $stdout
            StdErr = $stderr
        }
    }
    catch {
        $message = ($_ | Out-String).TrimEnd()
        $combined = @(
            "COMMAND: $FilePath $(Join-NativeArguments -Values $ArgumentList)",
            '--- POWERSHELL EXCEPTION ---',
            $message,
            'EXITCODE: 1'
        )
        Write-LogContent -LogPath $logPath -Content (($combined -join [Environment]::NewLine) + [Environment]::NewLine)
        $message | Out-Host
        return [pscustomobject]@{
            ExitCode = 1
            LogPath = $logPath
            StdOut = ''
            StdErr = $message
        }
    }
}

function Get-PythonLauncherPath {
    $cmd = Get-Command py.exe -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) { return $cmd.Source }

    $fallback = Join-Path $env:LOCALAPPDATA 'Programs\Python\Launcher\py.exe'
    if (Test-Path -LiteralPath $fallback) { return $fallback }

    return $null
}

function Test-PythonSeriesAvailable([string]$Series) {
    $launcher = Get-PythonLauncherPath
    if (-not $launcher) { return $false }

    try {
        & $launcher "-$Series" --version *> $null
        return ($LASTEXITCODE -eq 0)
    }
    catch {
        return $false
    }
}


function Invoke-NativeLoggedWithTimeout {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory = $true)][string]$StepName,
        [int]$TimeoutSeconds = 60
    )

    $logPath = New-StepLogPath -StepName $StepName
    $stdoutBuilder = [System.Text.StringBuilder]::new()
    $stderrBuilder = [System.Text.StringBuilder]::new()
    $timedOut = $false
    $exitCode = 1

    try {
        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = $FilePath
        $psi.Arguments = Join-NativeArguments -Values $ArgumentList
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.WorkingDirectory = $bundleRoot

        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $psi
        $process.EnableRaisingEvents = $true

        $outputHandler = [System.Diagnostics.DataReceivedEventHandler]{
            param($sender, $eventArgs)
            if ($null -ne $eventArgs.Data) {
                [void]$stdoutBuilder.AppendLine($eventArgs.Data)
            }
        }
        $errorHandler = [System.Diagnostics.DataReceivedEventHandler]{
            param($sender, $eventArgs)
            if ($null -ne $eventArgs.Data) {
                [void]$stderrBuilder.AppendLine($eventArgs.Data)
            }
        }

        $process.add_OutputDataReceived($outputHandler)
        $process.add_ErrorDataReceived($errorHandler)

        $null = $process.Start()
        $process.BeginOutputReadLine()
        $process.BeginErrorReadLine()

        $timeoutMs = [Math]::Max(1, $TimeoutSeconds) * 1000
        if (-not $process.WaitForExit($timeoutMs)) {
            $timedOut = $true
            try {
                if ($env:OS -eq 'Windows_NT') {
                    & taskkill /PID $process.Id /T /F | Out-Null
                }
                else {
                    $process.Kill()
                }
            }
            catch {
                try { $process.Kill() } catch { }
            }
            try { $process.WaitForExit(5000) | Out-Null } catch { }
            $exitCode = -1
            [void]$stderrBuilder.AppendLine("TIMEOUT: proces afgebroken na $TimeoutSeconds seconden.")
        }
        else {
            try { $process.WaitForExit() } catch { }
            $exitCode = [int]$process.ExitCode
        }

        $stdout = $stdoutBuilder.ToString()
        $stderr = $stderrBuilder.ToString()

        $combined = New-Object System.Collections.Generic.List[string]
        $combined.Add("COMMAND: $FilePath $(Join-NativeArguments -Values $ArgumentList)") | Out-Null
        $combined.Add("TIMEOUT_SECONDS: $TimeoutSeconds") | Out-Null
        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            $combined.Add('--- STDOUT ---') | Out-Null
            $combined.Add($stdout.TrimEnd("`r", "`n")) | Out-Null
        }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            $combined.Add('--- STDERR ---') | Out-Null
            $combined.Add($stderr.TrimEnd("`r", "`n")) | Out-Null
        }
        $combined.Add("TIMED_OUT: $timedOut") | Out-Null
        $combined.Add("EXITCODE: $exitCode") | Out-Null
        Write-LogContent -LogPath $logPath -Content (($combined -join [Environment]::NewLine) + [Environment]::NewLine)

        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            $stdout.TrimEnd("`r", "`n") | Out-Host
        }
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            $stderr.TrimEnd("`r", "`n") | Out-Host
        }

        return [pscustomobject]@{
            ExitCode = [int]$exitCode
            LogPath = $logPath
            StdOut = $stdout
            StdErr = $stderr
            TimedOut = [bool]$timedOut
        }
    }
    catch {
        $message = ($_ | Out-String).TrimEnd()
        $combined = @(
            "COMMAND: $FilePath $(Join-NativeArguments -Values $ArgumentList)",
            "TIMEOUT_SECONDS: $TimeoutSeconds",
            '--- POWERSHELL EXCEPTION ---',
            $message,
            'TIMED_OUT: False',
            'EXITCODE: 1'
        )
        Write-LogContent -LogPath $logPath -Content (($combined -join [Environment]::NewLine) + [Environment]::NewLine)
        $message | Out-Host
        return [pscustomobject]@{
            ExitCode = 1
            LogPath = $logPath
            StdOut = ''
            StdErr = $message
            TimedOut = $false
        }
    }
}

function Install-PythonSeries([string]$Series) {
    Write-Host "== Python $Series installeren via winget (Python.Python.$Series)" -ForegroundColor Cyan
    $result = Invoke-NativeLogged -FilePath 'winget' -ArgumentList @('install', '--id', "Python.Python.$Series", '--exact', '--source', 'winget', '--accept-package-agreements', '--accept-source-agreements') -StepName 'winget_python_install'

    if ($result.ExitCode -eq 0) {
        return $true
    }

    if ($result.ExitCode -eq -1978335189) {
        Add-WarningLine "Python $Series winget-resultaat is 'not applicable/al aanwezig'. Hercontrole volgt."
        return $true
    }

    Add-Blocker "Python $Series installatie via winget faalde met exitcode $($result.ExitCode). Zie log: $($result.LogPath)"
    return $false
}

function Ensure-Python([string]$Series) {
    if ($script:BundledPythonPath) {
        Add-Summary "Bundled Python $Series gevonden in de bundle: $script:BundledPythonPath"
        return $true
    }

    if (Test-PythonSeriesAvailable -Series $Series) {
        Add-Summary "Python $Series gevonden via py launcher."
        return $true
    }

    if (Confirm-Yes "Python $Series.x ontbreekt. Nu via winget installeren?") {
        $null = Install-PythonSeries -Series $Series
    }

    if (Test-PythonSeriesAvailable -Series $Series) {
        Add-Summary "Python $Series gevonden via py launcher na installatie/hercontrole."
        return $true
    }

    Add-Blocker "Python $Series.x ontbreekt of is niet correct gevonden. Herstel: installeer Python $Series.x of lever in een toekomstige installerbundle een private Python-runtime mee."
    return $false
}

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

function New-Venv([string]$Series, [string]$TargetVenv) {
    $parent = Split-Path -Parent $TargetVenv
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $bootstrapPython = $script:BundledPythonPath
    $argumentList = @()
    if ($bootstrapPython) {
        Add-Summary 'Bundled Python-runtime wordt gebruikt als bootstrapbron voor de venv.'
        $argumentList = @('-m', 'venv', $TargetVenv)
    }
    else {
        $launcher = Get-PythonLauncherPath
        if (-not $launcher) {
            Add-Blocker 'Python launcher (py.exe) ontbreekt, waardoor de venv niet kan worden aangemaakt.'
            return $null
        }
        $bootstrapPython = $launcher
        $argumentList = @("-$Series", '-m', 'venv', $TargetVenv)
    }

    Write-Host "== Venv aanmaken: $TargetVenv" -ForegroundColor Cyan
    $result = Invoke-NativeLogged -FilePath $bootstrapPython -ArgumentList $argumentList -StepName 'python_venv_create'
    $venvPython = Join-Path $TargetVenv 'Scripts\python.exe'
    if ($result.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $venvPython)) {
        Add-Blocker "Aanmaken van venv faalde: $TargetVenv. Zie log: $($result.LogPath)"
        return $null
    }

    Add-Summary "Venv aangemaakt: $venvPython"
    return $venvPython
}

function Test-DependencyResolverConflictFromLog([string]$LogPath) {
    if ([string]::IsNullOrWhiteSpace($LogPath) -or -not (Test-Path -LiteralPath $LogPath)) {
        return $false
    }

    try {
        $content = Get-Content -LiteralPath $LogPath -Raw -Encoding UTF8 -ErrorAction Stop
    }
    catch {
        return $false
    }

    $markers = @(
        'ResolutionImpossible',
        'because these package versions have conflicting dependencies.',
        'The conflict is caused by:'
    )

    foreach ($marker in $markers) {
        if ($content -match [regex]::Escape($marker)) {
            return $true
        }
    }

    return $false
}

function Get-DependencyResolverConflictSummary([string]$LogPath) {
    if ([string]::IsNullOrWhiteSpace($LogPath) -or -not (Test-Path -LiteralPath $LogPath)) {
        return $null
    }

    try {
        $lines = Get-Content -LiteralPath $LogPath -Encoding UTF8 -ErrorAction Stop
    }
    catch {
        return $null
    }

    $interesting = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

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

    if ($interesting.Count -eq 0) {
        return $null
    }

    return ($interesting | Select-Object -First 6) -join ' | '
}

function Install-WorkerStack([string]$VenvPython, [string]$InstallSpecPath, [string]$WheelhousePath) {
    Write-Host '== Pip bijwerken in venv' -ForegroundColor Cyan
    $pipUpgrade = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList @('-m', 'pip', 'install', '--upgrade', 'pip', 'setuptools', 'wheel') -StepName 'pip_upgrade'
    if ($pipUpgrade.ExitCode -ne 0) {
        Add-Blocker "pip upgrade in de venv faalde. Zie log: $($pipUpgrade.LogPath)"
        return [pscustomobject]@{
            Success = $false
            ResolverConflict = $false
            LogPath = $pipUpgrade.LogPath
        }
    }

    Write-Host '== Worker requirements installeren' -ForegroundColor Cyan
    $pipInstallArgs = @('-m', 'pip', 'install')
    if (-not [string]::IsNullOrWhiteSpace($WheelhousePath)) {
        Add-Summary "Lokale wheelhouse gebruikt voor worker-installatie: $WheelhousePath"
        $pipInstallArgs += @('--no-index', '--find-links', $WheelhousePath)
    }
    $pipInstallArgs += @('-r', $InstallSpecPath)
    $installResult = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList $pipInstallArgs -StepName 'worker_requirements_install'
    if ($installResult.ExitCode -ne 0) {
        $resolverConflict = Test-DependencyResolverConflictFromLog -LogPath $installResult.LogPath
        if ($resolverConflict) {
            $summary = Get-DependencyResolverConflictSummary -LogPath $installResult.LogPath
            if ([string]::IsNullOrWhiteSpace($summary)) {
                Add-Blocker "Dependency-resolverconflict in $InstallSpecPath. Top-level pins botsen; schone venv-herbouw helpt hier niet. Zie log: $($installResult.LogPath)"
            }
            else {
                Add-Blocker "Dependency-resolverconflict in $InstallSpecPath. Top-level pins botsen; schone venv-herbouw helpt hier niet. Samenvatting: $summary. Zie log: $($installResult.LogPath)"
            }
        }
        else {
            Add-Blocker "Installatie van $InstallSpecPath faalde. Zie log: $($installResult.LogPath)"
        }

        return [pscustomobject]@{
            Success = $false
            ResolverConflict = $resolverConflict
            LogPath = $installResult.LogPath
        }
    }

    Write-Host '== pip check' -ForegroundColor Cyan
    $pipCheck = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList @('-m', 'pip', 'check') -StepName 'pip_check'
    if ($pipCheck.ExitCode -ne 0) {
        Add-Blocker "pip check rapporteerde dependencyproblemen. Zie log: $($pipCheck.LogPath)"
        return [pscustomobject]@{
            Success = $false
            ResolverConflict = $false
            LogPath = $pipCheck.LogPath
        }
    }

    Add-Summary "Worker requirements geinstalleerd uit: $InstallSpecPath"
    return [pscustomobject]@{
        Success = $true
        ResolverConflict = $false
        LogPath = $installResult.LogPath
    }
}




function Write-ResolvedWorkerLock([string]$VenvPython, [string]$RealizedLockPath) {
    if ([string]::IsNullOrWhiteSpace($RealizedLockPath)) {
        return $false
    }

    $directory = Split-Path -Parent $RealizedLockPath
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    Write-Host '== Resolved worker lock vastleggen' -ForegroundColor Cyan
    $freezeResult = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList @('-m', 'pip', 'freeze', '--all') -StepName 'pip_freeze_all'
    if ($freezeResult.ExitCode -ne 0) {
        Add-WarningLine "Vastleggen van resolved worker lock faalde. Zie log: $($freezeResult.LogPath)"
        return $false
    }

    try {
        $freezeOutput = $freezeResult.StdOut
        if ([string]::IsNullOrWhiteSpace($freezeOutput)) {
            Add-WarningLine 'Resolved worker lock is leeg; vastleggen wordt overgeslagen.'
            return $false
        }

        $header = @(
            '# Resolved Windows worker lock voor Captivox Recorder'
            '# Gegenereerd door install-prereqs.ps1 na een geslaagde worker-bootstrap.'
            '# Gebruik deze file alleen als gevalideerde installatiespecificatie wanneer de onderliggende Windows-run echt bevestigd is.'
            ''
        ) -join [Environment]::NewLine
        [System.IO.File]::WriteAllText($RealizedLockPath, $header + $freezeOutput.Trim() + [Environment]::NewLine, [System.Text.Encoding]::UTF8)
        Add-Summary "Resolved worker lock bijgewerkt: $RealizedLockPath"
        return $true
    }
    catch {
        Add-WarningLine "Schrijven van resolved worker lock faalde: $($_.Exception.Message)"
        return $false
    }
}

function Test-VenvRuntime([string]$VenvPython, [string]$Series) {
    Write-Host '== Worker runtime valideren' -ForegroundColor Cyan

    $versionResult = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList @('--version') -StepName 'venv_python_version'
    if ($versionResult.ExitCode -ne 0) {
        Add-Blocker "Python-versiecontrole in de venv faalde. Zie log: $($versionResult.LogPath)"
        return $false
    }

    $versionProbePath = New-TemporaryProbeFile -StepName 'venv_python_probe' -Content @'
import sys
print(".".join(map(str, sys.version_info[:3])))
'@
    try {
        $versionProbe = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList @($versionProbePath) -StepName 'venv_python_probe'
    }
    finally {
        if (Test-Path -LiteralPath $versionProbePath) {
            Remove-Item -LiteralPath $versionProbePath -Force -ErrorAction SilentlyContinue
        }
    }
    if ($versionProbe.ExitCode -ne 0) {
        Add-Blocker "Python-runtimeprobe in de venv faalde. Zie log: $($versionProbe.LogPath)"
        return $false
    }

    $reportedVersion = $null
    try {
        $probeLines = ($versionProbe.StdOut -split "`r?`n")
        $reportedVersion = ($probeLines | Where-Object { $_ -match '^\d+\.\d+\.\d+$' } | Select-Object -Last 1)
    }
    catch {
        $reportedVersion = $null
    }
    if ([string]::IsNullOrWhiteSpace($reportedVersion) -or -not $reportedVersion.StartsWith("$Series.")) {
        Add-Blocker "Venv gebruikt niet de verwachte Python-serie $Series.x. Gedetecteerd: $reportedVersion"
        return $false
    }

    $importProbePath = New-TemporaryProbeFile -StepName 'worker_import_probe' -Content @'
import importlib
modules = ["whisperx", "numpy", "pandas", "torch", "torchaudio", "ffmpeg", "pyannote.audio"]
for name in modules:
    importlib.import_module(name)
print("Worker importcheck: OK")
'@
    try {
        $importResult = Invoke-NativeLogged -FilePath $VenvPython -ArgumentList @($importProbePath) -StepName 'worker_import_probe'
    }
    finally {
        if (Test-Path -LiteralPath $importProbePath) {
            Remove-Item -LiteralPath $importProbePath -Force -ErrorAction SilentlyContinue
        }
    }
    if ($importResult.ExitCode -ne 0) {
        Add-Blocker "Worker import-validatie faalde. Zie log: $($importResult.LogPath)"
        return $false
    }

    Add-Summary 'Worker runtime-importvalidatie geslaagd.'
    return $true
}

function Ensure-Venv([string]$Series, [string]$TargetVenv, [string]$RequirementsPath, [string]$InstallSpecPath, [string]$RealizedLockPath) {
    if (-not (Test-Path -LiteralPath $RequirementsPath)) {
        Add-WarningLine "requirements.txt niet gevonden: $RequirementsPath"
    }

    if (-not (Test-Path -LiteralPath $InstallSpecPath)) {
        Add-Blocker "Installatiespecificatie ontbreekt: $InstallSpecPath"
        return $null
    }

    $attempt = 0
    do {
        $attempt++
        $forceRebuild = $RebuildVenv.IsPresent -or ($attempt -gt 1)
        if ($forceRebuild) {
            Remove-VenvDirectory -TargetVenv $TargetVenv
        }

        $venvPython = Join-Path $TargetVenv 'Scripts\python.exe'
        if (Test-Path -LiteralPath $venvPython) {
            Add-Summary "Bestaande venv gevonden: $venvPython"
        }
        else {
            $venvPython = New-Venv -Series $Series -TargetVenv $TargetVenv
            if (-not $venvPython) {
                return $null
            }
        }

        $installState = Install-WorkerStack -VenvPython $venvPython -InstallSpecPath $InstallSpecPath -WheelhousePath $script:WheelhousePath
        if ($installState.Success) {
            $runtimeOk = Test-VenvRuntime -VenvPython $venvPython -Series $Series
            if ($runtimeOk) {
                $null = Write-ResolvedWorkerLock -VenvPython $venvPython -RealizedLockPath $RealizedLockPath
                return $venvPython
            }

            Add-WarningLine 'Worker runtimeprobe faalde na geslaagde install en pip check. Automatische schone venv-herbouw wordt overgeslagen omdat de dependencylaag al bevestigd is.'
            break
        }
        elseif ($installState.ResolverConflict) {
            Add-WarningLine 'Schone venv-herbouw wordt overgeslagen omdat een echte dependency-resolverconflict is gedetecteerd.'
            break
        }

        if ($attempt -lt 2) {
            Add-WarningLine 'Workerstack-validatie faalde voor een bevestigde runtimeprobe. Venv wordt eenmalig schoon herbouwd en opnieuw geprobeerd.'
        }
    } while ($attempt -lt 2)

    Add-Blocker 'Venv-bootstrap kon niet robuust worden voltooid.'
    return $null
}

function Resolve-FFmpegPath {
    if ($script:BundledFfmpegPath -and (Test-Path -LiteralPath $script:BundledFfmpegPath)) {
        return $script:BundledFfmpegPath
    }

    $cmd = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) {
        return $cmd.Source
    }

    $roots = @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'),
        (Join-Path $env:ProgramFiles 'WinGet\Packages')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    foreach ($root in $roots) {
        $match = Get-ChildItem -Path $root -Filter ffmpeg.exe -Recurse -File -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
        if ($match) { return $match }
    }

    return $null
}

function Test-FFmpeg([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }

    Write-Host '== FFmpeg valideren' -ForegroundColor Cyan
    $result = Invoke-NativeLogged -FilePath $Path -ArgumentList @('-version') -StepName 'ffmpeg_version'
    if ($result.ExitCode -ne 0) {
        Add-Blocker "FFmpeg validatie faalde. Zie log: $($result.LogPath)"
        return $false
    }

    Add-Summary "FFmpeg validatie geslaagd: $Path"
    return $true
}

function Ensure-FFmpeg {
    $ffmpeg = Resolve-FFmpegPath
    if ($ffmpeg) {
        Add-Summary "FFmpeg gevonden: $ffmpeg"
        if (Test-FFmpeg -Path $ffmpeg) {
            return $ffmpeg
        }
        $ffmpeg = $null
    }

    if (Confirm-Yes 'FFmpeg ontbreekt. Nu via winget installeren?') {
        Write-Host '== FFmpeg installeren via winget (Gyan.FFmpeg)' -ForegroundColor Cyan
        $installResult = Invoke-NativeLogged -FilePath 'winget' -ArgumentList @('install', '--id', 'Gyan.FFmpeg', '--exact', '--source', 'winget', '--accept-package-agreements', '--accept-source-agreements') -StepName 'winget_ffmpeg_install'
        if ($installResult.ExitCode -ne 0) {
            Add-Blocker "FFmpeg installatie via winget faalde met exitcode $($installResult.ExitCode). Zie log: $($installResult.LogPath)"
        }
    }

    $ffmpeg = Resolve-FFmpegPath
    if ($ffmpeg -and (Test-FFmpeg -Path $ffmpeg)) {
        Add-Summary "FFmpeg gevonden na installatie/hercontrole: $ffmpeg"
        return $ffmpeg
    }

    Add-Blocker 'FFmpeg ontbreekt of is niet correct gevonden. Voor een toekomstige installerbundle hoort een lokale ffmpeg-runtime meegeleverd te kunnen worden.'
    return $null
}

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

function Configure-CaptivoxSettings([string]$VenvPython, [string]$FFmpegPath, [bool]$OverwriteExisting) {
    $settings = Read-ExistingApplicationSettings
    $worker = Ensure-ChildObject -Target $settings -Name 'worker'

    $changed = $false
    if (Set-SettingsPathValue -Target $worker -Name 'pythonExe' -Value $VenvPython -OverwriteExisting $OverwriteExisting -SummaryName 'Python/venv-pad') {
        $changed = $true
    }
    if (Set-SettingsPathValue -Target $worker -Name 'ffmpegPath' -Value $FFmpegPath -OverwriteExisting $OverwriteExisting -SummaryName 'FFmpeg-pad') {
        $changed = $true
    }

    $stableDiarizationModel = if ($worker.PSObject.Properties['stableDiarizationModel']) { [string]$worker.stableDiarizationModel } else { '' }
    if ([string]::IsNullOrWhiteSpace($stableDiarizationModel) -or $stableDiarizationModel -eq 'pyannote/speaker-diarization-community-1') {
        Set-ObjectProperty -Target $worker -Name 'stableDiarizationModel' -Value 'pyannote/speaker-diarization-3.1'
        Add-Summary 'Standaard diarizationmodel gemigreerd naar pyannote/speaker-diarization-3.1.'
        $changed = $true
    }
    $experimentalDiarizationModel = if ($worker.PSObject.Properties['experimentalDiarizationModel']) { [string]$worker.experimentalDiarizationModel } else { '' }
    if ([string]::IsNullOrWhiteSpace($experimentalDiarizationModel)) {
        Set-ObjectProperty -Target $worker -Name 'experimentalDiarizationModel' -Value 'pyannote/speaker-diarization-community-1'
        Add-Summary 'Experimenteel diarizationmodel vastgelegd als pyannote/speaker-diarization-community-1.'
        $changed = $true
    }

    $diarizationAcceleration = if ($worker.PSObject.Properties['diarizationAcceleration']) { [string]$worker.diarizationAcceleration } else { '' }
    if ([string]::IsNullOrWhiteSpace($diarizationAcceleration)) {
        Set-ObjectProperty -Target $worker -Name 'diarizationAcceleration' -Value 'auto'
        Add-Summary 'Diarizationversnelling ingesteld op auto: GPU-sidecar wordt alleen gebruikt wanneer die expliciet beschikbaar en gevalideerd is; anders blijft CPU de veilige route.'
        $changed = $true
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
    [System.IO.File]::WriteAllText($settingsPath, $json, [System.Text.Encoding]::UTF8)

    Add-Summary "App-instellingen bijgewerkt: $settingsPath"
    return $settingsPath
}


function Test-AmdGpuPresent {
    try {
        $gpuNames = Get-CimInstance -ClassName Win32_VideoController -ErrorAction Stop | ForEach-Object { $_.Name } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        foreach ($name in $gpuNames) {
            if ($name -match '(?i)amd|radeon|rx\s*7|rx\s*6|rx\s*5') { return $true }
        }
    }
    catch {
        Add-WarningLine "AMD GPU-detectie voor Vulkan-runtime kon niet worden uitgevoerd: $($_.Exception.Message)"
    }
    return $false
}

function Test-WhisperCppRuntimePresent {
    $runtimeRoot = Join-Path (Get-AppDataRoot) 'runtime\whispercpp'
    $exe = Join-Path $runtimeRoot 'whisper-cli.exe'
    $model = Join-Path $runtimeRoot 'models\ggml-large-v3-turbo-q5_0.bin'
    return ((Test-Path -LiteralPath $exe) -and (Test-Path -LiteralPath $model))
}

function Resolve-FfmpegForAmdWhisper {
    $settingsPath = Get-AppSettingsPath
    try {
        if (Test-Path -LiteralPath $settingsPath) {
            $json = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
            if ($json.worker -and $json.worker.ffmpegPath -and (Test-Path -LiteralPath ([string]$json.worker.ffmpegPath))) { return [string]$json.worker.ffmpegPath }
        }
    } catch { }
    $cmd = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) { return $cmd.Source }
    $cmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Source) { return $cmd.Source }
    return ''
}

function Test-FfmpegWhisperGpuRoutePresent {
    $runtimeRoot = Join-Path (Get-AppDataRoot) 'runtime\whispercpp'
    $model = Join-Path $runtimeRoot 'models\ggml-large-v3-turbo-q5_0.bin'
    if (-not (Test-Path -LiteralPath $model)) { return $false }
    $ffmpeg = Resolve-FfmpegForAmdWhisper
    if ([string]::IsNullOrWhiteSpace($ffmpeg)) { return $false }
    try {
        $help = (& $ffmpeg -hide_banner -h filter=whisper 2>&1) -join "`n"
        return ($help -match '(?i)whisper' -and $help -match '(?i)use_gpu')
    } catch { return $false }
}

function Ensure-AmdGpuRuntimeIfRequested([string]$BundleRoot) {
    $amdPresent = Test-AmdGpuPresent
    $ffmpegGpuPresent = Test-FfmpegWhisperGpuRoutePresent
    $runtimePresent = Test-WhisperCppRuntimePresent

    if ($amdPresent -and $ffmpegGpuPresent -and -not $PrepareAmdGpuRuntime) {
        Add-Summary 'AMD GPU-runtime beschikbaar via FFmpeg Whisper-filter met use_gpu=true. Zelfbouw whisper.cpp Vulkan is niet nodig.'
        return
    }

    if ($amdPresent -and $runtimePresent -and -not $PrepareAmdGpuRuntime) {
        Add-Summary 'Secundaire AMD GPU-runtime voor whisper.cpp Vulkan is aanwezig.'
        return
    }

    if ($amdPresent -and -not $ffmpegGpuPresent -and -not $runtimePresent -and -not $PrepareAmdGpuRuntime) {
        Add-WarningLine 'AMD/Radeon GPU gedetecteerd, maar geen FFmpeg Whisper GPU-route en geen whisper.cpp Vulkan-runtime gevonden. Voor AMD GPU-transcriptie: gebruik een FFmpeg-build met --enable-whisper/use_gpu of draai .\install-prereqs.ps1 -PrepareAmdGpuRuntime.'
        return
    }

    if (-not $PrepareAmdGpuRuntime) { return }

    $testScript = Join-Path $BundleRoot 'tools\Test-WhisperCppVulkanRuntime.ps1'
    if (-not (Test-Path -LiteralPath $testScript)) {
        Add-Blocker "AMD GPU-runtime testscript ontbreekt: $testScript"
        return
    }

    try {
        Write-Host '== AMD GPU-route voor transcriptie valideren' -ForegroundColor Cyan
        & $testScript
        Add-Summary 'AMD GPU-route voor transcriptie is gevalideerd via FFmpeg Whisper GPU.'
        return
    }
    catch {
        Add-WarningLine "FFmpeg Whisper GPU-route kon niet worden gevalideerd: $($_.Exception.Message)"
    }

    $buildScript = Join-Path $BundleRoot 'tools\Build-WhisperCppVulkanRuntime.ps1'
    if (-not (Test-Path -LiteralPath $buildScript)) {
        Add-Blocker "AMD GPU-runtime buildscript ontbreekt: $buildScript"
        return
    }

    try {
        Write-Host '== Secundaire AMD GPU-runtime via whisper.cpp Vulkan voorbereiden' -ForegroundColor Cyan
        & $buildScript -Clean
        & $testScript
        Add-Summary 'Secundaire AMD GPU-runtime via whisper.cpp Vulkan is gebouwd en getest.'
    }
    catch {
        Add-Blocker "AMD GPU-runtime kon niet worden voorbereid: $($_.Exception.Message)"
    }
}

$bundle = Resolve-BundleContext
$bundleRoot = $bundle.Root
$requirementsPath = $bundle.Requirements
$installSpecPath = $bundle.InstallSpec
if ([string]::IsNullOrWhiteSpace($VenvPath)) {
    $VenvPath = Get-DefaultVenvPath
}
$script:BundledPythonPath = Resolve-BundledPythonPath -BundleRoot $bundleRoot -Series $PythonSeries
$script:BundledFfmpegPath = Resolve-BundledFfmpegPath -BundleRoot $bundleRoot
$script:WheelhousePath = Resolve-WheelhousePath -BundleRoot $bundleRoot

Write-Host '== Captivox Recorder prerequisite bootstrap' -ForegroundColor Green
Write-Host "Bundle root: $bundleRoot"
Write-Host "Requirements: $requirementsPath"
Write-Host "Installatiespecificatie: $installSpecPath"
Write-Host "App-datamap: $(Get-AppDataRoot)"
Run-PreflightChecks -BundleRoot $bundleRoot -TargetVenv $VenvPath

$pythonOk = Ensure-Python -Series $PythonSeries
$venvPython = $null
if ($pythonOk) {
    $venvPython = Ensure-Venv -Series $PythonSeries -TargetVenv $VenvPath -RequirementsPath $requirementsPath -InstallSpecPath $installSpecPath -RealizedLockPath $bundle.RealizedLockSpec
}

$ffmpegPath = Ensure-FFmpeg

$appSettingsPath = $null
if (-not $SkipAppSettings -and $venvPython -and $ffmpegPath) {
    Write-Host '== Captivox app-instellingen bijwerken' -ForegroundColor Cyan
    $appSettingsPath = Configure-CaptivoxSettings -VenvPython $venvPython -FFmpegPath $ffmpegPath -OverwriteExisting ([bool]$OverwriteExistingAppSettings)
}
elseif (-not $SkipAppSettings) {
    Add-WarningLine 'App-instellingen zijn niet automatisch bijgewerkt omdat Python/venv of FFmpeg nog niet geldig bevestigd zijn.'
}

Ensure-AmdGpuRuntimeIfRequested -BundleRoot $bundleRoot

Write-Host ''
Write-Host 'Aanbevolen Captivox-instellingen:' -ForegroundColor Cyan
if ($venvPython) {
    Write-Host "- Python/venv-pad: $venvPython"
}
else {
    Write-Host '- Python/venv-pad: nog niet bevestigd'
}
if ($ffmpegPath) {
    Write-Host "- ffmpeg-pad: $ffmpegPath"
}
else {
    Write-Host '- ffmpeg-pad: nog niet bevestigd'
}
if ($script:BundledPythonPath) { Write-Host "- Bundled Python-bron: $script:BundledPythonPath" }
if ($script:BundledFfmpegPath) { Write-Host "- Bundled FFmpeg-bron: $script:BundledFfmpegPath" }
if ($script:WheelhousePath) { Write-Host "- Lokale wheelhouse: $script:WheelhousePath" }
if ($appSettingsPath) {
    Write-Host "- application_settings.json: $appSettingsPath"
}

Write-Host ''
Write-Host 'Samenvatting' -ForegroundColor Cyan
Write-Host "- Bundle root: $bundleRoot"
Write-Host "- Requirements: $requirementsPath"
Write-Host "- Installatiespecificatie: $installSpecPath"
foreach ($line in $script:Summary) {
    Write-Host "- $line"
}
Write-Host "- Installatielogs: $script:InstallLogRoot"

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
    exit 1
}

Write-Host ''
Write-Host 'Resultaat: prerequisites gecontroleerd, gevalideerd en bootstrap voltooid.' -ForegroundColor Green
exit 0
