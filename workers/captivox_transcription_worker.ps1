param(
    [switch]$RunOnce,
    [switch]$SelfTest,
    [string]$SettingsPath,
    [string]$ProcessSessionDir
)

$ErrorActionPreference = 'Stop'
# >>> psmodulepath-sanitize (v8.27.1)
# Windows PowerShell 5.1 die (via de recorder) vanuit PowerShell 7 is gestart, erft het modulepad van PowerShell 7. Dan
# laden Microsoft.PowerShell.Utility-functies als Get-FileHash niet, en werd een correct transcript als "verplichte output
# ontbreekt" afgekeurd (2-10-2026). Alleen de paden van Windows PowerShell en eigen modulemappen houden.
if ($PSVersionTable.PSEdition -ne 'Core') {
    # De eigen modules van Windows PowerShell vooraan ($PSHOME), zodat ze altijd winnen; PowerShell 7-paden (ook de
    # Store-versie onder WindowsApps) eruit.
    $windowsPowerShellModules = Join-Path $PSHOME 'Modules'
    $keptModulePaths = @(($env:PSModulePath -split ';') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '(?i)(\\PowerShell\\|\\Microsoft\.PowerShell_)' -and $_.TrimEnd('\') -ine $windowsPowerShellModules })
    $env:PSModulePath = (@($windowsPowerShellModules) + $keptModulePaths | Select-Object -Unique) -join ';'
}
# <<< psmodulepath-sanitize
$WorkerVersionFallback = 'v7.85.0'
$WorkerRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$WorkerVersionFile = Join-Path $WorkerRoot 'worker.version.txt'
$WorkerVersion = $WorkerVersionFallback
if (Test-Path -LiteralPath $WorkerVersionFile) {
    try {
        $versionFromFile = [string](Get-Content -LiteralPath $WorkerVersionFile -Encoding UTF8 -TotalCount 1)
        if (-not [string]::IsNullOrWhiteSpace($versionFromFile)) {
            $WorkerVersion = $versionFromFile.Trim()
        }
    }
    catch {}
}

$ExpectedWorkerVersion = if (-not [string]::IsNullOrWhiteSpace($env:CAPTIVOX_EXPECTED_VERSION)) { [string]$env:CAPTIVOX_EXPECTED_VERSION } else { $WorkerVersion }
if (-not [string]::IsNullOrWhiteSpace($ExpectedWorkerVersion) -and $WorkerVersion -ne $ExpectedWorkerVersion) {
    Write-Host "[Captivox] Worker-versiemismatch: worker=$WorkerVersion, verwacht=$ExpectedWorkerVersion. Worker stopt om stale verwerking te voorkomen."
    exit 12
}

# De gegevensmap van de gratis Captivox Recorder; de recorder geeft hem door via CAPTIVOX_APPDATA_ROOT.
$AppDataRoot = if (-not [string]::IsNullOrWhiteSpace($env:CAPTIVOX_APPDATA_ROOT)) { [string]$env:CAPTIVOX_APPDATA_ROOT } else { Join-Path $env:LOCALAPPDATA 'CaptivoxRecorderFree' }
$SettingsPath = if ($SettingsPath) { $SettingsPath } else { Join-Path $AppDataRoot 'settings\application_settings.json' }
$HelperScript = Join-Path $WorkerRoot 'transcribe_with_whisperx_recorder.py'
# Logboeken in de gegevensmap, zodat de programmamap alleen-lezen mag zijn.
$WorkerLogRoot = Join-Path $AppDataRoot 'logs\worker'
$MainLogFile = Join-Path $WorkerLogRoot ("ready_worker_{0}.log" -f (Get-Date -Format 'yyyyMMdd'))
$LockFile = Join-Path $WorkerLogRoot 'captivox_transcription_worker.lock'
$WorkerRuntimeRoot = Join-Path $AppDataRoot 'temp\worker_runtime'
$WorkerStateFile = Join-Path $WorkerRuntimeRoot 'worker_status.json'
$RetentionRuntimeRoot = Join-Path $AppDataRoot 'temp\retention_runtime'
$RetentionStateFile = Join-Path $RetentionRuntimeRoot 'retention_status.json'
$CurrentDefaultOutputRoot = Join-Path $env:USERPROFILE 'Captivox\Opnames'
$LegacyDefaultOutputRoots = @()

function Resolve-BundledWorkerPythonPath {
    $candidates = @(
        (Join-Path $WorkerRoot '..\runtime\python\python.exe'),
        (Join-Path $WorkerRoot '..\runtime\python310\python.exe'),
        (Join-Path $WorkerRoot '..\python\python.exe'),
        (Join-Path $AppDataRoot 'runtime\.venv\Scripts\python.exe')
    )
    foreach ($candidate in $candidates) {
        try { $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($candidate) } catch { $resolved = $candidate }
        if (Test-Path -LiteralPath $resolved) {
            return $resolved
        }
    }
    return 'python'
}

function Resolve-BundledWorkerFfmpegPath {
    $candidates = @(
        (Join-Path $WorkerRoot '..\runtime\ffmpeg\ffmpeg.exe'),
        (Join-Path $WorkerRoot '..\runtime\ffmpeg\bin\ffmpeg.exe'),
        (Join-Path $WorkerRoot '..\ffmpeg\ffmpeg.exe'),
        (Join-Path $AppDataRoot 'runtime\ffmpeg\ffmpeg.exe')
    )
    foreach ($candidate in $candidates) {
        try { $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($candidate) } catch { $resolved = $candidate }
        if (Test-Path -LiteralPath $resolved) {
            return $resolved
        }
    }
    return 'ffmpeg'
}
function Resolve-BundledWhisperCppExePath {
    $candidates = @(
        (Join-Path $WorkerRoot '..\runtime\whispercpp\whisper-cli.exe'),
        (Join-Path $WorkerRoot '..\runtime\whispercpp\bin\whisper-cli.exe'),
        (Join-Path $WorkerRoot '..\tools\whispercpp\whisper-cli.exe'),
        (Join-Path $AppDataRoot 'runtime\whispercpp\whisper-cli.exe'),
        (Join-Path $AppDataRoot 'runtime\whispercpp\bin\whisper-cli.exe')
    )
    foreach ($candidate in $candidates) {
        try { $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($candidate) } catch { $resolved = $candidate }
        if (Test-Path -LiteralPath $resolved) { return $resolved }
    }
    return ''
}

function Resolve-BundledWhisperCppModelPath {
    $candidates = @(
        (Join-Path $WorkerRoot '..\runtime\whispercpp\models\ggml-large-v3-turbo-q5_0.bin'),
        (Join-Path $WorkerRoot '..\runtime\whispercpp\models\ggml-large-v3-turbo.bin'),
        (Join-Path $WorkerRoot '..\runtime\whispercpp\models\ggml-large-v3.bin'),
        (Join-Path $WorkerRoot '..\runtime\whispercpp\models\ggml-medium.bin'),
        (Join-Path $AppDataRoot 'runtime\whispercpp\models\ggml-large-v3-turbo-q5_0.bin'),
        (Join-Path $AppDataRoot 'runtime\whispercpp\models\ggml-large-v3-turbo.bin'),
        (Join-Path $AppDataRoot 'runtime\whispercpp\models\ggml-large-v3.bin'),
        (Join-Path $AppDataRoot 'runtime\whispercpp\models\ggml-medium.bin')
    )
    foreach ($candidate in $candidates) {
        try { $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($candidate) } catch { $resolved = $candidate }
        if (Test-Path -LiteralPath $resolved) { return $resolved }
    }
    return ''
}

New-Item -ItemType Directory -Force -Path $WorkerLogRoot | Out-Null
New-Item -ItemType Directory -Force -Path $WorkerRuntimeRoot | Out-Null
New-Item -ItemType Directory -Force -Path $RetentionRuntimeRoot | Out-Null

function Write-Log {
    param([string]$Level, [string]$Message)
    $line = "[{0}] [{1}] {2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level.ToUpper(), $Message
    Write-Host $line
    try { Add-Content -Path $MainLogFile -Value $line } catch {}
}


function ConvertFrom-JsonCompat {
    param([string]$JsonText)
    return ($JsonText | ConvertFrom-Json)
}

function Read-JsonFile {
    param([string]$LiteralPath)
    $jsonText = Get-Content -LiteralPath $LiteralPath -Raw -Encoding UTF8
    return (ConvertFrom-JsonCompat -JsonText $jsonText)
}

function Load-Manifest {
    param([string]$ManifestPath)
    if ([string]::IsNullOrWhiteSpace($ManifestPath) -or -not (Test-Path -LiteralPath $ManifestPath)) {
        return $null
    }
    return (Read-JsonFile -LiteralPath $ManifestPath)
}

function Get-TranscriptOutputFormat {
    $format = [string]$script:CurrentConfig.TranscriptOutputFormat
    if ([string]::IsNullOrWhiteSpace($format)) {
        $format = 'txt+json'
    }
    return $format.ToLowerInvariant()
}

function Get-PowerShellHostInfo {
    $version = if ($PSVersionTable.PSVersion) { $PSVersionTable.PSVersion.ToString() } else { 'unknown' }
    $edition = if ($PSVersionTable.PSEdition) { [string]$PSVersionTable.PSEdition } else { 'Desktop' }
    $exe = if ($PSCommandPath) { $PSCommandPath } elseif ($MyInvocation.MyCommand.Path) { $MyInvocation.MyCommand.Path } else { 'powershell.exe' }
    return [pscustomobject]@{
        version = $version
        edition = $edition
        host_path = $exe
        ps_home = $PSHOME
    }
}

function Load-Settings {
    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        return [pscustomobject]@{
            ui = [pscustomobject]@{ outputRoot = $CurrentDefaultOutputRoot; diagnosticsMode = $false }
            worker = [pscustomobject]@{
                whisperXEnabled = $false
                diarizationEnabled = $true
                useProductionProfile = $true
                transcriptionProfile = 'auto'
                whisperCppExe = (Resolve-BundledWhisperCppExePath)
                whisperCppModelPath = (Resolve-BundledWhisperCppModelPath)
                runtimeMode = 'python'
                pythonExe = (Resolve-BundledWorkerPythonPath)
                whisperXModel = 'large-v2'
                computeType = 'float16'
                language = 'nl'
                batchSize = 4
                minSpeakers = 2
                maxSpeakers = 4
                transcriptOutputFormat = 'txt+json'
                diarizationMode = 'stable'
                diarizationAcceleration = 'auto'
                stableDiarizationModel = 'pyannote/speaker-diarization-3.1'
                experimentalDiarizationModel = 'pyannote/speaker-diarization-community-1'
                ffmpegPath = (Resolve-BundledWorkerFfmpegPath)
                pollIntervalSec = 5
                minReadyAgeSec = 2
                retryFailedSessions = $true
                retryStaleStartedAfterMinutes = 180
                maxRetryAttempts = 3
                retryBackoffMinutes = 15
                knownSessionRoots = @($CurrentDefaultOutputRoot)
                createMarkers = $true
                preserveWorkerOutput = $false
                exportSrt = $false
                exportVtt = $false
                customVocabulary = @()
                retention = [pscustomobject]@{
                    enabled = $false
                    audioRetentionDays = 30
                    transcriptRetentionDays = 90
                    sessionRecordRetentionDays = 90
                }
                import = [pscustomobject]@{
                    watchFolderEnabled = $false
                    watchFolders = @()
                    consumeWatchFiles = $true
                    minimumStableAgeSeconds = 15
                    supportedExtensions = @('.m4a','.mp3','.wav','.mp4','.m4v','.aac')
                }
                enhancement = [pscustomobject]@{
                    enabled = $false
                    importedAudioOnly = $true
                    profile = 'light-clean'
                }
            }
        }
    }

    $loaded = Read-JsonFile -LiteralPath $SettingsPath
    return (Normalize-LegacyOutputRootSettings -Settings $loaded)
}

function Is-LegacyDefaultOutputRoot {
    param([string]$PathValue)
    if ([string]::IsNullOrWhiteSpace($PathValue)) { return $false }
    foreach ($candidate in $LegacyDefaultOutputRoots) {
        if ([string]::Equals((Normalize-SessionRootPath $candidate), (Normalize-SessionRootPath $PathValue), [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Normalize-SessionRootPath {
    param([string]$PathValue)
    if ([string]::IsNullOrWhiteSpace($PathValue)) { return '' }
    try {
        return [System.IO.Path]::GetFullPath($PathValue.Trim()).TrimEnd('\','/')
    }
    catch {
        return $PathValue.Trim().TrimEnd('\','/')
    }
}

function Normalize-LegacyOutputRootSettings {
    param($Settings)
    if ($null -eq $Settings -or $null -eq $Settings.ui -or $null -eq $Settings.worker) { return $Settings }

    $currentOutputRoot = [string]$Settings.ui.outputRoot
    if (Is-LegacyDefaultOutputRoot -PathValue $currentOutputRoot) {
        if ($null -eq $Settings.worker.knownSessionRoots) {
            $Settings.worker | Add-Member -NotePropertyName knownSessionRoots -NotePropertyValue @() -Force
        }

        $trimmed = Normalize-SessionRootPath $currentOutputRoot
        $knownRoots = @($Settings.worker.knownSessionRoots)
        if (-not ($knownRoots | Where-Object { [string]::Equals((Normalize-SessionRootPath ([string]$_)), $trimmed, [System.StringComparison]::OrdinalIgnoreCase) })) {
            $Settings.worker.knownSessionRoots += $trimmed
        }

        $Settings.ui.outputRoot = $CurrentDefaultOutputRoot
    }

    return $Settings
}

function Get-BooleanSettingValue {
    param(
        [object]$Object,
        [string[]]$PropertyNames,
        [bool]$DefaultValue
    )

    if ($null -eq $Object) { return $DefaultValue }
    foreach ($propertyName in $PropertyNames) {
        try {
            $property = $Object.PSObject.Properties[$propertyName]
            if ($null -eq $property) { continue }
            $value = $property.Value
            if ($null -eq $value) { continue }
            if ($value -is [bool]) { return [bool]$value }
            $text = ([string]$value).Trim()
            if ([string]::IsNullOrWhiteSpace($text)) { continue }
            $normalizedText = $text.ToLowerInvariant()
            if (@('true','1','yes','ja','on') -contains $normalizedText) { return $true }
            if (@('false','0','no','nee','off') -contains $normalizedText) { return $false }
        }
        catch {}
    }

    return $DefaultValue
}

function Test-TranscriptFormatIncludes {
    param([string]$Format, [string]$Token)
    if ([string]::IsNullOrWhiteSpace($Format) -or [string]::IsNullOrWhiteSpace($Token)) { return $false }
    $normalizedFormat = ($Format.ToLowerInvariant() -replace '[,; ]', '+')
    $parts = @($normalizedFormat -split '\+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    return $parts -contains $Token.ToLowerInvariant()
}

$script:CurrentConfig = $null

function Load-WorkerConfig {
    $settings = Load-Settings
    $roots = New-Object System.Collections.Generic.List[string]
    if ($settings.ui.outputRoot) { $roots.Add((Normalize-SessionRootPath ([string]$settings.ui.outputRoot))) }
    if ($settings.worker.knownSessionRoots) {
        foreach ($root in $settings.worker.knownSessionRoots) {
            if (-not [string]::IsNullOrWhiteSpace([string]$root)) {
                $roots.Add((Normalize-SessionRootPath ([string]$root)))
            }
        }
    }

    $transcriptOutputFormat = if ($settings.worker.transcriptOutputFormat) { ([string]$settings.worker.transcriptOutputFormat).ToLowerInvariant() } else { 'txt+json' }
    $exportSrt = Get-BooleanSettingValue -Object $settings.worker -PropertyNames @('exportSrt','ExportSrt') -DefaultValue $false
    $exportVtt = Get-BooleanSettingValue -Object $settings.worker -PropertyNames @('exportVtt','ExportVtt') -DefaultValue $false
    if (-not [string]::IsNullOrWhiteSpace($env:CAPTIVOX_EXPORT_SRT)) { $exportSrt = Get-BooleanSettingValue -Object ([pscustomobject]@{ value = $env:CAPTIVOX_EXPORT_SRT }) -PropertyNames @('value') -DefaultValue $exportSrt }
    if (-not [string]::IsNullOrWhiteSpace($env:CAPTIVOX_EXPORT_VTT)) { $exportVtt = Get-BooleanSettingValue -Object ([pscustomobject]@{ value = $env:CAPTIVOX_EXPORT_VTT }) -PropertyNames @('value') -DefaultValue $exportVtt }
    if (Test-TranscriptFormatIncludes -Format $transcriptOutputFormat -Token 'srt') { $exportSrt = $true }
    if (Test-TranscriptFormatIncludes -Format $transcriptOutputFormat -Token 'vtt') { $exportVtt = $true }
    $watchFolderEnabled = Get-BooleanSettingValue -Object $settings.worker.import -PropertyNames @('watchFolderEnabled','WatchFolderEnabled') -DefaultValue $false
    $consumeWatchFiles = Get-BooleanSettingValue -Object $settings.worker.import -PropertyNames @('consumeWatchFiles','ConsumeWatchFiles') -DefaultValue $true

    return [pscustomobject]@{
        Settings = $settings
        SessionRoots = @($roots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
        DiagnosticsMode = [bool]$settings.ui.diagnosticsMode
        WhisperXEnabled = [bool]$settings.worker.whisperXEnabled
        DiarizationEnabled = [bool]$settings.worker.diarizationEnabled
        UseProductionProfile = if ($null -ne $settings.worker.useProductionProfile) { [bool]$settings.worker.useProductionProfile } else { $true }
        TranscriptionProfile = if ($settings.worker.transcriptionProfile) { Normalize-TranscriptionProfile -Profile ([string]$settings.worker.transcriptionProfile) } else { 'auto' }
        WhisperCppExe = if ($settings.worker.whisperCppExe) { [string]$settings.worker.whisperCppExe } else { Resolve-BundledWhisperCppExePath }
        WhisperCppModelPath = if ($settings.worker.whisperCppModelPath) { [string]$settings.worker.whisperCppModelPath } else { Resolve-BundledWhisperCppModelPath }
        RuntimeMode = 'python'
        PythonExe = if ($settings.worker.pythonExe) { [string]$settings.worker.pythonExe } else { Resolve-BundledWorkerPythonPath }
        Language = if ($settings.worker.language) { [string]$settings.worker.language } else { 'nl' }
        Model = if ($settings.worker.whisperXModel) { [string]$settings.worker.whisperXModel } else { 'large-v2' }
        BatchSize = if ($settings.worker.batchSize) { [int]$settings.worker.batchSize } else { 4 }
        ComputeType = if ($settings.worker.computeType) { [string]$settings.worker.computeType } else { 'float16' }
        MinSpeakers = if ($null -ne $settings.worker.minSpeakers) { [int]$settings.worker.minSpeakers } else { 0 }
        MaxSpeakers = if ($null -ne $settings.worker.maxSpeakers) { [int]$settings.worker.maxSpeakers } else { 0 }
        TranscriptOutputFormat = $transcriptOutputFormat
        DiarizationMode = if ($settings.worker.diarizationMode) { [string]$settings.worker.diarizationMode } else { 'stable' }
        DiarizationAcceleration = if ($settings.worker.diarizationAcceleration) { [string]$settings.worker.diarizationAcceleration } else { 'auto' }
        StableDiarizationModel = if ($settings.worker.stableDiarizationModel) { [string]$settings.worker.stableDiarizationModel } else { 'pyannote/speaker-diarization-3.1' }
        ExperimentalDiarizationModel = if ($settings.worker.experimentalDiarizationModel) { [string]$settings.worker.experimentalDiarizationModel } else { 'pyannote/speaker-diarization-community-1' }
        FfmpegPath = if ($settings.worker.ffmpegPath) { [string]$settings.worker.ffmpegPath } else { Resolve-BundledWorkerFfmpegPath }
        PollIntervalSec = if ($settings.worker.pollIntervalSec) { [int]$settings.worker.pollIntervalSec } else { 5 }
        MinReadyAgeSec = if ($null -ne $settings.worker.minReadyAgeSec) { [int]$settings.worker.minReadyAgeSec } else { 2 }
        RetryFailedSessions = if ($null -ne $settings.worker.retryFailedSessions) { [bool]$settings.worker.retryFailedSessions } else { $true }
        RetryStaleStartedAfterMinutes = if ($settings.worker.retryStaleStartedAfterMinutes) { [int]$settings.worker.retryStaleStartedAfterMinutes } else { 180 }
        MaxRetryAttempts = if ($null -ne $settings.worker.maxRetryAttempts) { [int]$settings.worker.maxRetryAttempts } else { 3 }
        RetryBackoffMinutes = if ($null -ne $settings.worker.retryBackoffMinutes) { [int]$settings.worker.retryBackoffMinutes } else { 15 }
        CreateMarkers = if ($null -ne $settings.worker.createMarkers) { [bool]$settings.worker.createMarkers } else { $true }
        PreserveWorkerOutput = if ($null -ne $settings.worker.preserveWorkerOutput) { [bool]$settings.worker.preserveWorkerOutput } else { $true }
        ExportSrt = $exportSrt
        ExportVtt = $exportVtt
        CustomVocabulary = @($settings.worker.customVocabulary)
        ImportWatchFolderEnabled = $watchFolderEnabled
        ImportWatchFolders = @($settings.worker.import.watchFolders | ForEach-Object { [string]$_ })
        ImportConsumeWatchFiles = $consumeWatchFiles
        ImportMinimumStableAgeSeconds = if ($settings.worker.import.minimumStableAgeSeconds) { [int]$settings.worker.import.minimumStableAgeSeconds } else { 15 }
        ImportSupportedExtensions = @($settings.worker.import.supportedExtensions | ForEach-Object { ([string]$_).ToLowerInvariant() })
        EnhancementEnabled = if ($null -ne $settings.worker.enhancement.enabled) { [bool]$settings.worker.enhancement.enabled } else { $false }
        EnhancementImportedAudioOnly = if ($null -ne $settings.worker.enhancement.importedAudioOnly) { [bool]$settings.worker.enhancement.importedAudioOnly } else { $true }
        EnhancementProfile = if ($settings.worker.enhancement.profile) { [string]$settings.worker.enhancement.profile } else { 'light-clean' }
        RetentionEnabled = if ($null -ne $settings.worker.retention.enabled) { [bool]$settings.worker.retention.enabled } else { $false }
        AudioRetentionDays = if ($settings.worker.retention.audioRetentionDays) { [int]$settings.worker.retention.audioRetentionDays } else { 30 }
        TranscriptRetentionDays = if ($settings.worker.retention.transcriptRetentionDays) { [int]$settings.worker.retention.transcriptRetentionDays } else { 90 }
        SessionRecordRetentionDays = if ($settings.worker.retention.sessionRecordRetentionDays) { [int]$settings.worker.retention.sessionRecordRetentionDays } else { 90 }
    }
}

function Refresh-WorkerConfig {
    $script:CurrentConfig = Load-WorkerConfig
    return $script:CurrentConfig
}

function Get-DiarizationModel {
    param([pscustomobject]$Config = $script:CurrentConfig)
    if (-not $Config.DiarizationEnabled) { return '' }
    if ($Config.DiarizationMode -eq 'experimental') { return $Config.ExperimentalDiarizationModel }
    return $Config.StableDiarizationModel
}

function Ensure-SessionSubDirectory {
    param([string]$SessionDir, [string]$Name)
    $path = Join-Path $SessionDir $Name
    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
    return $path
}

function Get-SessionDiagnosticsPath {
    param([string]$SessionDir, [string]$FileName)
    return (Join-Path (Ensure-SessionSubDirectory -SessionDir $SessionDir -Name 'diagnostics') $FileName)
}

function Get-SessionExportPath {
    param([string]$SessionDir, [string]$FileName)
    return (Join-Path (Ensure-SessionSubDirectory -SessionDir $SessionDir -Name 'exports') $FileName)
}

function Get-SessionAiPath {
    param([string]$SessionDir, [string]$FileName)
    return (Join-Path (Ensure-SessionSubDirectory -SessionDir $SessionDir -Name 'ai') $FileName)
}


function Normalize-TranscriptionProfile {
    param([string]$Profile)
    $value = if ([string]::IsNullOrWhiteSpace($Profile)) { 'auto' } else { $Profile.Trim().ToLowerInvariant() }
    switch ($value) {
        'nvidia' { return 'nvidia-cuda' }
        'cuda' { return 'nvidia-cuda' }
        'nvidia-cuda' { return 'nvidia-cuda' }
        'amd' { return 'amd-gpu' }
        'amd-gpu' { return 'amd-gpu' }
        'rocm' { return 'amd-gpu' }
        'hip' { return 'amd-gpu' }
        'cpu' { return 'cpu-fallback' }
        'cpu-fallback' { return 'cpu-fallback' }
        default { return 'auto' }
    }
}

function Get-DetectedGpuProfile {
    $gpuNames = @()
    try {
        $gpuNames = @(Get-CimInstance Win32_VideoController -ErrorAction Stop | ForEach-Object { [string]$_.Name } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    catch {
        try {
            $gpuNames = @(Get-WmiObject Win32_VideoController -ErrorAction Stop | ForEach-Object { [string]$_.Name } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        }
        catch {
            $gpuNames = @()
        }
    }

    foreach ($name in $gpuNames) {
        if ($name -match '(?i)nvidia|geforce|\brtx\b|\bgtx\b') { return 'nvidia-cuda' }
    }
    foreach ($name in $gpuNames) {
        if ($name -match '(?i)amd|radeon|\brx\b') { return 'amd-gpu' }
    }
    return 'cpu-fallback'
}

function Convert-RegistryBinaryToUInt64 {
    param([byte[]]$Value)
    if ($null -eq $Value -or $Value.Length -eq 0) { return [uint64]0 }
    $buffer = New-Object byte[] 8
    [Array]::Copy($Value, 0, $buffer, 0, [Math]::Min(8, $Value.Length))
    try { return [BitConverter]::ToUInt64($buffer, 0) } catch { return [uint64]0 }
}

function Convert-RegistryValueToUInt64 {
    param($Value)
    if ($null -eq $Value) { return [uint64]0 }
    if ($Value -is [byte[]]) { return Convert-RegistryBinaryToUInt64 -Value $Value }
    try { return [uint64]$Value } catch { return [uint64]0 }
}

function Convert-RegistryAdapterString {
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [byte[]]) {
        try { return ([System.Text.Encoding]::Unicode.GetString($Value)).Trim([char[]]@([char]0, [char]32, [char]9, [char]13, [char]10)) } catch { return $null }
    }
    return ([string]$Value).Trim([char[]]@([char]0, [char]32, [char]9, [char]13, [char]10))
}

function Get-WorkerGpuVendorFromName {
    param([string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return 'unknown' }
    if ($Name -match '(?i)nvidia|geforce|\brtx\b|\bgtx\b') { return 'nvidia' }
    if ($Name -match '(?i)amd|radeon|\brx\b') { return 'amd' }
    if ($Name -match '(?i)intel|uhd|iris') { return 'intel' }
    return 'unknown'
}

function Get-WorkerRegistryGpuMemoryItems {
    $items = @()
    try {
        $videoRoot = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Control\Video')
        if ($null -eq $videoRoot) { return @() }
        try {
            foreach ($adapterKeyName in $videoRoot.GetSubKeyNames()) {
                $adapterKey = $videoRoot.OpenSubKey($adapterKeyName)
                if ($null -eq $adapterKey) { continue }
                try {
                    foreach ($subKeyName in $adapterKey.GetSubKeyNames()) {
                        $deviceKey = $adapterKey.OpenSubKey($subKeyName)
                        if ($null -eq $deviceKey) { continue }
                        try {
                            $name = Convert-RegistryAdapterString -Value ($deviceKey.GetValue('HardwareInformation.AdapterString', $null))
                            $memoryBytes = Convert-RegistryValueToUInt64 -Value ($deviceKey.GetValue('HardwareInformation.qwMemorySize', $null))
                            $method = 'registry HardwareInformation.qwMemorySize'
                            if ($memoryBytes -le 0) {
                                $memoryBytes = Convert-RegistryValueToUInt64 -Value ($deviceKey.GetValue('HardwareInformation.MemorySize', $null))
                                $method = 'registry HardwareInformation.MemorySize'
                            }
                            if ($memoryBytes -gt 0) {
                                if ([string]::IsNullOrWhiteSpace($name)) { $name = $adapterKeyName }
                                $items += [pscustomobject]@{
                                    name = [string]$name
                                    vendor = (Get-WorkerGpuVendorFromName -Name $name)
                                    adapter_ram_bytes = [uint64]$memoryBytes
                                    detection_method = $method
                                }
                            }
                        }
                        finally { if ($deviceKey) { $deviceKey.Dispose() } }
                    }
                }
                finally { if ($adapterKey) { $adapterKey.Dispose() } }
            }
        }
        finally { if ($videoRoot) { $videoRoot.Dispose() } }
    }
    catch { return @() }
    return @($items)
}

function Select-WorkerRegistryGpuMemoryMatch {
    param(
        [string]$Name,
        [object[]]$RegistryItems
    )
    if ($null -eq $RegistryItems -or $RegistryItems.Count -eq 0) { return $null }
    $vendor = Get-WorkerGpuVendorFromName -Name $Name
    $candidates = @($RegistryItems | Where-Object { $_.vendor -eq $vendor -and $_.adapter_ram_bytes -gt 0 })
    if ($candidates.Count -eq 0) { $candidates = @($RegistryItems | Where-Object { $_.adapter_ram_bytes -gt 0 }) }
    return $candidates | Sort-Object -Property adapter_ram_bytes -Descending | Select-Object -First 1
}

function Get-WorkerHardwareSnapshot {
    $gpuItems = @()
    $registryGpuItems = @(Get-WorkerRegistryGpuMemoryItems)
    try {
        $gpuItems = @(Get-CimInstance Win32_VideoController -ErrorAction Stop | ForEach-Object {
            $name = [string]$_.Name
            $wmiBytes = $(if ($null -ne $_.AdapterRAM) { [uint64]$_.AdapterRAM } else { [uint64]0 })
            $registryMatch = Select-WorkerRegistryGpuMemoryMatch -Name $name -RegistryItems $registryGpuItems
            $registryBytes = $(if ($null -ne $registryMatch) { [uint64]$registryMatch.adapter_ram_bytes } else { [uint64]0 })
            $effectiveBytes = $wmiBytes
            $method = 'WMI Win32_VideoController.AdapterRAM'
            if ($registryBytes -gt $effectiveBytes) {
                $effectiveBytes = $registryBytes
                $method = [string]$registryMatch.detection_method
            }
            [pscustomobject]@{
                name = $name
                adapter_ram_bytes = [uint64]$effectiveBytes
                adapter_ram_wmi_bytes = [uint64]$wmiBytes
                adapter_ram_registry_bytes = [uint64]$registryBytes
                adapter_ram_detection_method = $method
            }
        })
    }
    catch {
        $gpuItems = @($registryGpuItems | ForEach-Object {
            [pscustomobject]@{
                name = [string]$_.name
                adapter_ram_bytes = [uint64]$_.adapter_ram_bytes
                adapter_ram_wmi_bytes = [uint64]0
                adapter_ram_registry_bytes = [uint64]$_.adapter_ram_bytes
                adapter_ram_detection_method = [string]$_.detection_method
            }
        })
    }

    $profile = Get-DetectedGpuProfile
    $effectiveWhisperCppModelPath = Resolve-EffectiveWhisperCppModelPath -ConfiguredPath $script:CurrentConfig.WhisperCppModelPath -TranscriptionProfile $profile
    return [pscustomobject]@{
        detected_at = [datetimeoffset]::Now.ToString('o')
        gpu_profile = $profile
        gpu_profile_label = (Get-TranscriptionProfileDisplayName -Profile $profile)
        gpus = $gpuItems
        whispercpp_exe = $script:CurrentConfig.WhisperCppExe
        whispercpp_model_path = $effectiveWhisperCppModelPath
        amd_engine = $(if ($profile -eq 'amd-gpu') { 'FFmpeg Whisper GPU' } else { 'not_applicable' })
        note = 'Worker-snapshot is lichtgewicht; runtime-validatie gebeurt in de helper. AMD gebruikt primair FFmpeg Whisper GPU met Vulkan-validatie.'
    }
}

function Resolve-EffectiveTranscriptionProfile {
    param([pscustomobject]$Config = $script:CurrentConfig)
    $configured = Normalize-TranscriptionProfile -Profile $Config.TranscriptionProfile
    if ($configured -eq 'auto') {
        return (Get-DetectedGpuProfile)
    }
    return $configured
}

function Get-TranscriptionProfileDisplayName {
    param([string]$Profile)
    switch (Normalize-TranscriptionProfile -Profile $Profile) {
        'nvidia-cuda' { return 'NVIDIA CUDA' }
        'amd-gpu' { return 'AMD GPU' }
        'cpu-fallback' { return 'CPU fallback' }
        default { return 'Automatisch' }
    }
}

function Resolve-AmdRuntimeManifestModelPath {
    param([string]$ConfiguredPath)

    $roots = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        try {
            $resolvedConfigured = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ConfiguredPath)
        } catch {
            $resolvedConfigured = $ConfiguredPath
        }
        $configuredParent = Split-Path -Parent $resolvedConfigured
        if (-not [string]::IsNullOrWhiteSpace($configuredParent)) {
            if ([string]::Equals((Split-Path -Leaf $configuredParent), 'models', [System.StringComparison]::OrdinalIgnoreCase)) {
                $roots.Add((Split-Path -Parent $configuredParent))
            }
            else {
                $roots.Add($configuredParent)
            }
        }
    }

    $roots.Add((Join-Path $AppDataRoot 'runtime\whispercpp')) | Out-Null
    $roots.Add((Join-Path $WorkerRoot '..\runtime\whispercpp')) | Out-Null

    foreach ($root in @($roots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)) {
        try {
            $resolvedRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($root)
        } catch {
            $resolvedRoot = $root
        }

        $manifestPath = Join-Path $resolvedRoot 'runtime_manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath)) { continue }

        try {
            $runtimeManifest = Read-JsonFile -LiteralPath $manifestPath
            $selectedModel = [string]$runtimeManifest.selected_model_path
            if (-not [string]::IsNullOrWhiteSpace($selectedModel) -and (Test-Path -LiteralPath $selectedModel)) {
                return $selectedModel
            }
        }
        catch {
            continue
        }
    }

    return ''
}

function Resolve-EffectiveWhisperCppModelPath {
    param(
        [string]$ConfiguredPath,
        [string]$TranscriptionProfile = ''
    )

    $resolvedProfile = Normalize-TranscriptionProfile -Profile $TranscriptionProfile
    if ([string]::IsNullOrWhiteSpace($resolvedProfile) -or $resolvedProfile -eq 'auto') {
        $resolvedProfile = Resolve-EffectiveTranscriptionProfile
    }

    if ($resolvedProfile -eq 'amd-gpu') {
        $manifestModel = Resolve-AmdRuntimeManifestModelPath -ConfiguredPath $ConfiguredPath
        if (-not [string]::IsNullOrWhiteSpace($manifestModel)) { return $manifestModel }

        $preferredModels = @(
            (Join-Path $AppDataRoot 'runtime\whispercpp\models\ggml-large-v3-turbo-q5_0.bin'),
            (Join-Path $WorkerRoot '..\runtime\whispercpp\models\ggml-large-v3-turbo-q5_0.bin')
        )
        foreach ($candidate in $preferredModels) {
            try { $resolved = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($candidate) } catch { $resolved = $candidate }
            if (Test-Path -LiteralPath $resolved) { return $resolved }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        try { $resolvedConfigured = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ConfiguredPath) } catch { $resolvedConfigured = $ConfiguredPath }
        if (Test-Path -LiteralPath $resolvedConfigured) { return $resolvedConfigured }
    }

    $bundled = Resolve-BundledWhisperCppModelPath
    if (-not [string]::IsNullOrWhiteSpace($bundled)) { return $bundled }

    return $ConfiguredPath
}

function Get-EffectiveBatchSize {
    param([pscustomobject]$Config = $script:CurrentConfig, [string]$Profile = '')
    $resolvedProfile = if ([string]::IsNullOrWhiteSpace($Profile)) { Resolve-EffectiveTranscriptionProfile -Config $Config } else { Normalize-TranscriptionProfile -Profile $Profile }
    if ($resolvedProfile -eq 'cpu-fallback') { return [Math]::Min([int]$Config.BatchSize, 2) }
    if ($resolvedProfile -eq 'amd-gpu') { return 1 }
    return [int]$Config.BatchSize
}

function Get-EffectiveComputeType {
    param([pscustomobject]$Config = $script:CurrentConfig, [string]$Profile = '')
    $resolvedProfile = if ([string]::IsNullOrWhiteSpace($Profile)) { Resolve-EffectiveTranscriptionProfile -Config $Config } else { Normalize-TranscriptionProfile -Profile $Profile }
    if ($resolvedProfile -eq 'cpu-fallback') { return 'int8' }
    if ($resolvedProfile -eq 'amd-gpu') { return 'ffmpeg-whisper' }
    if ($Config.UseProductionProfile) { return 'float16' }
    return $Config.ComputeType
}

function Normalize-DiarizationAcceleration {
    param([string]$Value)
    $normalized = if ([string]::IsNullOrWhiteSpace($Value)) { 'auto' } else { $Value.Trim().ToLowerInvariant() }
    switch ($normalized) {
        'cpu' { return 'cpu' }
        'auto' { return 'auto' }
        'amd-rocm' { return 'amd-rocm-experimental' }
        'rocm' { return 'amd-rocm-experimental' }
        'amd-rocm-experimental' { return 'amd-rocm-experimental' }
        'nvidia-cuda' { return 'nvidia-cuda-experimental' }
        'cuda' { return 'nvidia-cuda-experimental' }
        'nvidia-cuda-experimental' { return 'nvidia-cuda-experimental' }
        default { return 'auto' }
    }
}

function Get-EffectiveDiarizationAcceleration {
    param([pscustomobject]$Config = $script:CurrentConfig)
    return Normalize-DiarizationAcceleration -Value $Config.DiarizationAcceleration
}

function Get-EffectiveDiarizationMode {
    param([pscustomobject]$Config = $script:CurrentConfig)
    if ($Config.UseProductionProfile) { return 'stable' }
    return $Config.DiarizationMode
}

function Get-EffectiveDiarizationModel {
    param([pscustomobject]$Config = $script:CurrentConfig)
    if (-not $Config.DiarizationEnabled) { return '' }
    $profile = Resolve-EffectiveTranscriptionProfile -Config $Config
    if ($profile -eq 'amd-gpu') {
        # v7.35.0: AMD gebruikt FFmpeg Whisper GPU/Vulkan voor transcriptie
        # en pyannote 3.1 in de bestaande hoofdworker voor CPU-diarization.
        return 'pyannote/speaker-diarization-3.1'
    }
    $mode = Get-EffectiveDiarizationMode -Config $Config
    if ($mode -eq 'experimental') { return $Config.ExperimentalDiarizationModel }
    return $Config.StableDiarizationModel
}

function Get-WorkerProfileSummary {
    param([pscustomobject]$Config = $script:CurrentConfig)
    $resolvedProfile = Resolve-EffectiveTranscriptionProfile -Config $Config
    $profileLabel = Get-TranscriptionProfileDisplayName -Profile $resolvedProfile
    if ($resolvedProfile -eq 'amd-gpu') { return ("production-profile={0};engine:FFmpeg Whisper GPU;compute:ffmpeg-whisper;diarization:pyannote-3.1;acceleration=$(Get-EffectiveDiarizationAcceleration -Config $Config);cpu-fallback" -f $profileLabel) }
    if ($Config.UseProductionProfile) { return ("production-profile={0};compute:{1};diarization:pyannote-3.1;acceleration=$(Get-EffectiveDiarizationAcceleration -Config $Config);cpu-fallback" -f $profileLabel, (Get-EffectiveComputeType -Config $Config -Profile $resolvedProfile)) }
    return ("override-profile={0};compute:{1};mode:{2};model:{3}" -f $profileLabel, (Get-EffectiveComputeType -Config $Config -Profile $resolvedProfile), $Config.DiarizationMode, (Get-DiarizationModel -Config $Config))
}

function Resolve-TokenHelperExe {
    $candidates = @(
        $env:CAPTIVOX_TOKEN_HELPER_EXE,
        $env:CAPTIVOX_IMPORT_HELPER_EXE,
        $env:CAPTIVOX_RECORDER_EXE,
        (Join-Path (Split-Path -Parent $PSScriptRoot) 'CaptivoxRecorder.exe'),
        (Join-Path $PSScriptRoot 'CaptivoxRecorder.exe')
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Resolve-RecoveryHelperExe {
    return (Resolve-ImportHelperExe)
}

function Resolve-ImportHelperExe {
    $candidates = @(
        $env:CAPTIVOX_IMPORT_HELPER_EXE,
        $env:CAPTIVOX_TOKEN_HELPER_EXE,
        $env:CAPTIVOX_RECORDER_EXE,
        (Join-Path (Split-Path -Parent $PSScriptRoot) 'CaptivoxRecorder.exe'),
        (Join-Path $PSScriptRoot 'CaptivoxRecorder.exe')
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    return $null
}

function Get-CustomVocabularyPrompt {
    $entries = @($script:CurrentConfig.CustomVocabulary)
    if ($entries.Count -eq 0) { return $null }

    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $entries) {
        if ($null -eq $entry) { continue }
        $canonical = ([string]$entry.canonical).Trim()
        if ([string]::IsNullOrWhiteSpace($canonical)) { continue }
        $variants = @($entry.variants | ForEach-Object { ([string]$_).Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($variants.Count -gt 0) {
            $lines.Add(($canonical + ' = ' + ($variants -join ', ')))
        }
        else {
            $lines.Add($canonical)
        }
    }

    if ($lines.Count -eq 0) { return $null }
    $prompt = 'Voorkeurswoorden en namen: ' + ($lines -join '; ')
    if ($prompt.Length -gt 1200) {
        $prompt = $prompt.Substring(0, 1200)
    }
    return $prompt
}

function Test-WatchSourceStable {
    param([System.IO.FileInfo]$File, [int]$MinimumAgeSeconds)
    $age = ((Get-Date) - $File.LastWriteTime).TotalSeconds
    return $age -ge [Math]::Max(5, $MinimumAgeSeconds)
}

function Move-WatchFolderImportFailure {
    param(
        [System.IO.FileInfo]$File,
        [string]$Reason
    )

    try {
        if ($null -eq $File -or -not (Test-Path -LiteralPath $File.FullName)) { return }
        $failedDir = Join-Path $File.DirectoryName '_captivox_failed'
        New-Item -ItemType Directory -Path $failedDir -Force | Out-Null
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $targetName = "{0}.{1}.failed{2}" -f $File.BaseName, $stamp, $File.Extension
        $targetPath = Join-Path $failedDir $targetName
        Move-Item -LiteralPath $File.FullName -Destination $targetPath -Force
        $reasonPath = $targetPath + '.txt'
        [System.IO.File]::WriteAllText($reasonPath, $Reason, [System.Text.UTF8Encoding]::new($false))
        Write-Log 'WARN' "Watch folder bestand in quarantaine gezet: $($File.FullName) -> $targetPath"
    }
    catch {
        Write-Log 'WARN' "Watch folder bestand kon niet in quarantaine worden gezet: $($File.FullName) :: $($_.Exception.Message)"
    }
}

function Invoke-WatchFolderImports {
    if (-not $script:CurrentConfig.ImportWatchFolderEnabled) { return }
    $helperExe = Resolve-ImportHelperExe
    if (-not $helperExe) {
        Write-Log 'WARN' 'Import helper niet gevonden; watch folder ingest wordt overgeslagen.'
        return
    }

    foreach ($watchRoot in @($script:CurrentConfig.ImportWatchFolders | Sort-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($watchRoot) -or -not (Test-Path -LiteralPath $watchRoot)) { continue }
        try {
            $files = Get-ChildItem -LiteralPath $watchRoot -File -ErrorAction Stop | Sort-Object LastWriteTime
        }
        catch {
            Write-Log 'WARN' "Watch folder kan niet worden gelezen: $watchRoot :: $($_.Exception.Message)"
            continue
        }

        foreach ($file in $files) {
            try {
                if ($file.DirectoryName -match '(?i)[\\/]_captivox_failed$') { continue }
                $ext = $file.Extension.ToLowerInvariant()
                if (@($script:CurrentConfig.ImportSupportedExtensions).Count -gt 0 -and $script:CurrentConfig.ImportSupportedExtensions -notcontains $ext) { continue }
                if (-not (Test-Path -LiteralPath $file.FullName)) { continue }
                if (-not (Test-WatchSourceStable -File $file -MinimumAgeSeconds $script:CurrentConfig.ImportMinimumStableAgeSeconds)) { continue }

                $arguments = @('--import-audio', $file.FullName, '--watch-folder-source')
                if ($script:CurrentConfig.ImportConsumeWatchFiles) { $arguments += '--consume-source' }
                Write-Log 'INFO' "Watch folder ingest voor: $($file.FullName)"
                $result = Invoke-ExternalProcess -FileName $helperExe -Arguments $arguments -EnvironmentVariables @{} -WorkingDirectory (Split-Path -Parent $helperExe) -StdoutPath $null -StderrPath $null
                if (-not $result.ExitCodeAvailable -or $result.ExitCode -ne 0) {
                    $detail = (($result.Stderr + " `n" + $result.Stdout).Trim())
                    if ([string]::IsNullOrWhiteSpace($detail)) { $detail = "exitcode $($result.ExitCodeText)" }
                    Write-Log 'ERROR' "Watch folder ingest mislukt voor $($file.FullName) :: $detail"
                    Move-WatchFolderImportFailure -File $file -Reason $detail
                    continue
                }

                $sessionDir = @(([string]$result.Stdout) -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)[0]
                if ([string]::IsNullOrWhiteSpace($sessionDir)) {
                    $detail = 'Import helper eindigde succesvol, maar gaf geen sessiemap terug via stdout.'
                    Write-Log 'ERROR' "Watch folder ingest onvolledig voor $($file.FullName) :: $detail"
                    Move-WatchFolderImportFailure -File $file -Reason $detail
                    continue
                }

                Write-Log 'INFO' "Watch folder ingest gereed: $($file.FullName) -> $sessionDir"
            }
            catch {
                $detail = $_.Exception.Message
                Write-Log 'ERROR' "Watch folder ingest overgeslagen na fout voor $($file.FullName) :: $detail"
                Move-WatchFolderImportFailure -File $file -Reason $detail
                continue
            }
        }
    }
}

function Get-HfToken {
    if ($env:HF_TOKEN) {
        return [string]$env:HF_TOKEN
    }

    $helperExe = Resolve-TokenHelperExe
    if (-not $helperExe) {
        throw 'HF-token bridge niet gevonden (CaptivoxRecorder.exe ontbreekt naast de worker).'
    }

    $result = Invoke-ExternalProcess -FileName $helperExe -Arguments @('--get-hf-token') -EnvironmentVariables @{} -WorkingDirectory (Split-Path -Parent $helperExe) -StdoutPath $null -StderrPath $null
    if ($result.ExitCode -ne 0) {
        $detail = ($result.Stderr + " `n" + $result.Stdout).Trim()
        if ([string]::IsNullOrWhiteSpace($detail)) {
            $detail = "exitcode $($result.ExitCode)"
        }

        throw ("HF-token kon niet worden geladen via token bridge: {0}" -f $detail)
    }

    $token = ([string]$result.Stdout).Trim()
    if ([string]::IsNullOrWhiteSpace($token)) {
        throw 'HF-token bridge gaf een lege tokenwaarde terug.'
    }

    return $token
}


$script:LastWorkerActivityUtc = [datetimeoffset]::UtcNow

function Update-WorkerRuntimeStatus {
    param(
        [string]$State,
        [string]$Message,
        [string]$ActiveSessionDir = '',
        [switch]$MarkActivity
    )

    if ($MarkActivity) {
        $script:LastWorkerActivityUtc = [datetimeoffset]::UtcNow
    }
    elseif (-not $script:LastWorkerActivityUtc) {
        $script:LastWorkerActivityUtc = [datetimeoffset]::UtcNow
    }

    $payload = [ordered]@{
        version = $WorkerVersion
        expected_version = $ExpectedWorkerVersion
        worker_root = $WorkerRoot
        worker_script = $MyInvocation.MyCommand.Path
        worker_pid = $PID
        state = $State
        message = $Message
        active_session_dir = $ActiveSessionDir
        last_seen_utc = ([datetimeoffset]::UtcNow.ToString('o'))
        last_activity_utc = $script:LastWorkerActivityUtc.ToString('o')
        session_roots = @($script:CurrentConfig.SessionRoots)
    } | ConvertTo-Json -Depth 8

    $tempPath = "$WorkerStateFile.tmp"
    try {
        [System.IO.File]::WriteAllText($tempPath, $payload, [System.Text.Encoding]::UTF8)
        if (Test-Path -LiteralPath $WorkerStateFile) {
            Remove-Item -LiteralPath $WorkerStateFile -Force -ErrorAction SilentlyContinue
        }
        Move-Item -LiteralPath $tempPath -Destination $WorkerStateFile -Force
    }
    catch {
        try {
            if (Test-Path -LiteralPath $tempPath) {
                Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
            }
        }
        catch {}
    }
}

$script:LastRetentionRunUtc = [datetimeoffset]::MinValue

function Save-RetentionRuntimeStatus {
    param([hashtable]$Snapshot)
    $tempPath = "$RetentionStateFile.tmp"
    try {
        $json = $Snapshot | ConvertTo-Json -Depth 8
        [System.IO.File]::WriteAllText($tempPath, $json, [System.Text.Encoding]::UTF8)
        if (Test-Path -LiteralPath $RetentionStateFile) {
            Remove-Item -LiteralPath $RetentionStateFile -Force -ErrorAction SilentlyContinue
        }
        Move-Item -LiteralPath $tempPath -Destination $RetentionStateFile -Force
    }
    catch {
        try {
            if (Test-Path -LiteralPath $tempPath) {
                Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
            }
        }
        catch {}
    }
}

function Get-RetentionCompletedAt {
    param($Manifest)

    foreach ($candidate in @($Manifest.transcription_finished_at, $Manifest.finished_at, $Manifest.created_at)) {
        if ($null -eq $candidate) { continue }
        try { return [datetimeoffset]::Parse([string]$candidate, [System.Globalization.CultureInfo]::InvariantCulture) } catch {}
    }

    return [datetimeoffset]::UtcNow
}

function Get-RetentionAgeDays {
    param([datetimeoffset]$Reference)
    $days = ([datetimeoffset]::UtcNow - $Reference).TotalDays
    if ($days -lt 0) { return 0 }
    return $days
}

function Add-RetentionCandidatePath {
    param(
        [System.Collections.Generic.List[string]]$List,
        [string]$SessionDir,
        $Candidate
    )

    if ($null -eq $Candidate) { return }
    $trimmed = ([string]$Candidate).Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) { return }
    $fullPath = if ([System.IO.Path]::IsPathRooted($trimmed)) { $trimmed } else { Join-Path $SessionDir $trimmed }
    if ($fullPath.StartsWith($SessionDir, [System.StringComparison]::OrdinalIgnoreCase)) {
        $List.Add($fullPath) | Out-Null
    }
}

function Remove-RetentionFiles {
    param(
        [string[]]$Candidates,
        [string]$SessionDir
    )

    $deleted = 0
    foreach ($path in ($Candidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        if (-not $path.StartsWith($SessionDir, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
        try {
            [System.IO.File]::SetAttributes($path, [System.IO.FileAttributes]::Normal)
            Remove-Item -LiteralPath $path -Force
            $deleted++
            Write-Log 'INFO' "Retentie verwijderde bestand: $path"
        }
        catch {
            Write-Log 'ERROR' "Retentie kon bestand niet verwijderen: $path :: $($_.Exception.Message)"
        }
    }

    return $deleted
}

function Test-EligibleCompletedManifest {
    param($Manifest)
    $recorderStatus = ("$($Manifest.status)").Trim().ToLowerInvariant()
    $transcriptionStatus = ("$($Manifest.transcription_status)").Trim().ToLowerInvariant()
    return $recorderStatus -eq 'completed' -and $transcriptionStatus -eq 'completed'
}

function Recover-InterruptedSessionsNative {
    param([int]$StaleSeconds = 120)
    $recovered = 0
    foreach ($root in @($script:CurrentConfig.SessionRoots | Sort-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root)) { continue }
        foreach ($dir in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
            $manifestPath = Join-Path $dir.FullName 'manifest.json'
            if (-not (Test-Path -LiteralPath $manifestPath)) { continue }
            try {
                $manifest = Load-Manifest -ManifestPath $manifestPath
                if ($null -eq $manifest) { continue }
                $status = ([string]$manifest.status).Trim().ToLowerInvariant()
                if ($status -ne 'recording') { continue }
                $heartbeat = Convert-ToDateTimeOffsetSafe $manifest.last_heartbeat_at
                if ($null -eq $heartbeat) { $heartbeat = Convert-ToDateTimeOffsetSafe $manifest.created_at }
                if ($null -eq $heartbeat) { continue }
                if (([datetimeoffset]::Now - $heartbeat).TotalSeconds -lt $StaleSeconds) { continue }
                Set-Prop -Object $manifest -Name 'status' -Value 'interrupted'
                Set-Prop -Object $manifest -Name 'error_code' -Value 'SESSION_INTERRUPTED'
                Set-Prop -Object $manifest -Name 'error_message' -Value 'Sessie is onderbroken aangetroffen en gemarkeerd voor herstelinspectie.'
                Set-Prop -Object $manifest -Name 'finished_at' -Value ([datetimeoffset]::Now.ToString('o'))
                Set-Prop -Object $manifest -Name 'recovery_status' -Value 'interrupted'
                Set-Prop -Object $manifest -Name 'recovery_notes' -Value 'Herkend bij worker-/app-herstart; live capture kan niet naadloos worden hervat. Reeds opgeslagen artefacten blijven behouden.'
                Set-Prop -Object $manifest -Name 'last_heartbeat_at' -Value ([datetimeoffset]::Now.ToString('o'))
                Save-Manifest -ManifestPath $manifestPath -Manifest $manifest
                $sessionLogPath = Join-Path $dir.FullName 'session.log'
                Add-Content -LiteralPath $sessionLogPath -Value ("[{0}] Sessierecovery: opname als interrupted gemarkeerd tijdens workerstart." -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')) -Encoding UTF8
                $recovered++
            }
            catch {
                Write-Log 'WARN' ("Sessierecovery mislukt voor {0}: {1}" -f $dir.FullName, $_.Exception.Message)
            }
        }
    }
    return $recovered
}

function Invoke-InterruptedSessionRecovery {
    param([int]$StaleSeconds = 120)
    $helperExe = Resolve-RecoveryHelperExe
    if ($helperExe) {
        try {
            $stdout = & $helperExe '--recover-interrupted' '--stale-seconds' $StaleSeconds 2>$null
            $count = 0
            if ($stdout) {
                $joined = ($stdout | Out-String).Trim()
                if ([int]::TryParse($joined, [ref]$count)) {
                    if ($count -gt 0) { Write-Log 'INFO' ("Sessierecovery via helper markeerde {0} onderbroken sessie(s)." -f $count) }
                    return $count
                }
            }
        }
        catch {
            Write-Log 'WARN' ("CLI-sessierecovery via helper mislukte; native fallback wordt gebruikt. Detail: {0}" -f $_.Exception.Message)
        }
    }
    else {
        Write-Log 'WARN' 'Recovery helper niet gevonden; native PowerShell fallback wordt gebruikt.'
    }
    $fallbackCount = Recover-InterruptedSessionsNative -StaleSeconds $StaleSeconds
    if ($fallbackCount -gt 0) {
        Write-Log 'INFO' ("Sessierecovery via native fallback markeerde {0} onderbroken sessie(s)." -f $fallbackCount)
    }
    return $fallbackCount
}

function Invoke-RetentionPolicy {
    $snapshot = [ordered]@{
        enabled = [bool]$script:CurrentConfig.RetentionEnabled
        success = $true
        lastRunAt = ([datetimeoffset]::Now.ToString('o'))
        scannedSessionCount = 0
        eligibleSessionCount = 0
        deletedAudioFileCount = 0
        deletedTranscriptFileCount = 0
        deletedSessionRecordFileCount = 0
        deletedSessionDirectoryCount = 0
        summary = 'Retentie nog niet uitgevoerd'
        lastError = $null
    }

    try {
        if (-not $script:CurrentConfig.RetentionEnabled) {
            $snapshot.summary = 'Retentie uitgeschakeld'
            Save-RetentionRuntimeStatus -Snapshot $snapshot
            return
        }

        foreach ($root in $script:CurrentConfig.SessionRoots) {
            $directories = @()
            try { $directories = @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction Stop | ForEach-Object { $_.FullName }) } catch { continue }
            foreach ($sessionDir in $directories) {
                $snapshot.scannedSessionCount++
                $manifestPath = Join-Path $sessionDir 'manifest.json'
                if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { continue }

                try { $manifest = Read-JsonFile -LiteralPath $manifestPath } catch { continue }
                if ($null -eq $manifest -or -not (Test-EligibleCompletedManifest -Manifest $manifest)) { continue }

                $snapshot.eligibleSessionCount++
                $completedAt = Get-RetentionCompletedAt -Manifest $manifest
                $ageDays = Get-RetentionAgeDays -Reference $completedAt

                if ($ageDays -ge $script:CurrentConfig.AudioRetentionDays) {
                    $audioFiles = New-Object 'System.Collections.Generic.List[string]'
                    $audioFiles.Add((Join-Path $sessionDir 'audio_mix.wav')) | Out-Null
                    Add-RetentionCandidatePath -List $audioFiles -SessionDir $sessionDir -Candidate $manifest.output_file
                    Add-RetentionCandidatePath -List $audioFiles -SessionDir $sessionDir -Candidate $manifest.mic_stem
                    Add-RetentionCandidatePath -List $audioFiles -SessionDir $sessionDir -Candidate $manifest.system_stem
                    Add-RetentionCandidatePath -List $audioFiles -SessionDir $sessionDir -Candidate $manifest.transcription_input_path
                    $snapshot.deletedAudioFileCount += Remove-RetentionFiles -Candidates $audioFiles.ToArray() -SessionDir $sessionDir
                }

                if ($ageDays -ge $script:CurrentConfig.TranscriptRetentionDays) {
                    $transcriptFiles = New-Object 'System.Collections.Generic.List[string]'
                    $transcriptFiles.Add((Join-Path $sessionDir 'transcript.txt')) | Out-Null
                    $transcriptFiles.Add((Join-Path $sessionDir 'transcript.json')) | Out-Null
                    Add-RetentionCandidatePath -List $transcriptFiles -SessionDir $sessionDir -Candidate $manifest.transcript_text_path
                    Add-RetentionCandidatePath -List $transcriptFiles -SessionDir $sessionDir -Candidate $manifest.transcript_json_path
                    $snapshot.deletedTranscriptFileCount += Remove-RetentionFiles -Candidates $transcriptFiles.ToArray() -SessionDir $sessionDir
                }

                if ($ageDays -ge $script:CurrentConfig.SessionRecordRetentionDays) {
                    $sessionFiles = @(
                        (Join-Path $sessionDir 'manifest.json'),
                        (Join-Path $sessionDir 'session.log'),
                        (Join-Path $sessionDir 'diagnostics.log'),
                        (Join-Path $sessionDir 'transcription.log'),
                        (Join-Path $sessionDir 'worker.stdout.log'),
                        (Join-Path $sessionDir 'worker.stderr.log'),
                        (Join-Path $sessionDir 'READY'),
                        (Join-Path $sessionDir 'TRANSCRIBED'),
                        (Join-Path $sessionDir 'TRANSCRIPTION_FAILED'),
                        (Join-Path $sessionDir '.transcription.claim')
                    )
                    $snapshot.deletedSessionRecordFileCount += Remove-RetentionFiles -Candidates $sessionFiles -SessionDir $sessionDir
                }

                try {
                    $remaining = @(Get-ChildItem -LiteralPath $sessionDir -Force -ErrorAction Stop)
                    if ($remaining.Count -eq 0) {
                        Remove-Item -LiteralPath $sessionDir -Force
                        $snapshot.deletedSessionDirectoryCount++
                        Write-Log 'INFO' "Retentie verwijderde lege sessiemap: $sessionDir"
                    }
                }
                catch {
                    Write-Log 'ERROR' "Retentie kon sessiemap niet verwijderen of inspecteren: $sessionDir :: $($_.Exception.Message)"
                }
            }
        }

        $snapshot.summary = "Retentie actief | Audio: -$($snapshot.deletedAudioFileCount) | Transcript: -$($snapshot.deletedTranscriptFileCount) | Sessiegegevens: -$($snapshot.deletedSessionRecordFileCount) | Mappen: -$($snapshot.deletedSessionDirectoryCount)"
        Save-RetentionRuntimeStatus -Snapshot $snapshot
    }
    catch {
        $snapshot.success = $false
        $snapshot.lastError = $_.Exception.Message
        $snapshot.summary = "Retentie fout: $($_.Exception.Message)"
        Write-Log 'ERROR' "Retention run failed :: $($_.Exception.Message)"
        Save-RetentionRuntimeStatus -Snapshot $snapshot
    }
}

function Test-ShouldRunRetention {
    if (-not $script:CurrentConfig.RetentionEnabled) { return $false }
    if ($script:LastRetentionRunUtc -eq [datetimeoffset]::MinValue) { return $true }
    return (([datetimeoffset]::UtcNow - $script:LastRetentionRunUtc).TotalMinutes -ge 30)
}

function Write-SessionLog {
    param([string]$SessionDir, [string]$Message)
    $path = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'transcription.log'
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    try { Add-Content -Path $path -Value $line } catch {}
}

function Write-DiagnosticsLog {
    param([string]$SessionDir, [string]$Message)
    if (-not $script:CurrentConfig.DiagnosticsMode) { return }
    $path = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'diagnostics.log'
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    try { Add-Content -Path $path -Value $line } catch {}
}

function Acquire-RunLock {
    try {
        $script:LockHandle = [System.IO.File]::Open($LockFile, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $script:RunLockAcquired = $true
        return $true
    }
    catch {
        Write-Log 'WARN' 'Andere worker-run actief; deze run wordt overgeslagen.'
        return $false
    }
}

function Release-RunLock {
    try {
        if ($script:LockHandle) {
            $script:LockHandle.Dispose()
            $script:LockHandle = $null
        }
    } catch {}
    try {
        if (Test-Path -LiteralPath $LockFile) { Remove-Item -LiteralPath $LockFile -Force -ErrorAction SilentlyContinue }
    } catch {}
}

function Quote-Arg {
    param([string]$Value)
    # Een lege waarde moet als "" mee, anders valt hij weg en schuift het volgende argument op
    # (bijvoorbeeld --diarization-model zonder sprekerherkenning).
    if ([string]::IsNullOrEmpty($Value)) {
        return '""'
    }
    if ($Value -match '[\s"]') {
        return '"' + ($Value -replace '"', '\"') + '"'
    }
    return $Value
}

function Invoke-ExternalProcess {
    param(
        [string]$FileName,
        [string[]]$Arguments,
        [hashtable]$EnvironmentVariables,
        [string]$WorkingDirectory,
        [string]$StdoutPath,
        [string]$StderrPath,
        [int]$TimeoutSec = 0,
        [scriptblock]$OnTick = $null,
        [scriptblock]$OnStarted = $null
    )

    $resolvedStdoutPath = $StdoutPath
    $resolvedStderrPath = $StderrPath
    $cleanupStdout = $false
    $cleanupStderr = $false

    if ([string]::IsNullOrWhiteSpace($resolvedStdoutPath)) {
        $resolvedStdoutPath = Join-Path $env:TEMP ("captivox_{0}.stdout.log" -f ([guid]::NewGuid().ToString('N')))
        $cleanupStdout = $true
    }
    if ([string]::IsNullOrWhiteSpace($resolvedStderrPath)) {
        $resolvedStderrPath = Join-Path $env:TEMP ("captivox_{0}.stderr.log" -f ([guid]::NewGuid().ToString('N')))
        $cleanupStderr = $true
    }

    $stdoutDir = Split-Path -Parent $resolvedStdoutPath
    if ($stdoutDir) { New-Item -ItemType Directory -Path $stdoutDir -Force | Out-Null }
    $stderrDir = Split-Path -Parent $resolvedStderrPath
    if ($stderrDir) { New-Item -ItemType Directory -Path $stderrDir -Force | Out-Null }
    [System.IO.File]::WriteAllText($resolvedStdoutPath, '', [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText($resolvedStderrPath, '', [System.Text.UTF8Encoding]::new($false))

    $previousEnv = @{}
    try {
        foreach ($key in $EnvironmentVariables.Keys) {
            $previousEnv[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, [string]$EnvironmentVariables[$key], 'Process')
        }

        $argumentLine = (($Arguments | ForEach-Object { Quote-Arg ([string]$_) }) -join ' ')
        $startParams = @{
            FilePath = $FileName
            ArgumentList = $argumentLine
            WorkingDirectory = $WorkingDirectory
            RedirectStandardOutput = $resolvedStdoutPath
            RedirectStandardError = $resolvedStderrPath
            WindowStyle = 'Hidden'
            PassThru = $true
        }
        $process = Start-Process @startParams
        if ($OnStarted) {
            try { & $OnStarted $process } catch {}
        }

        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $timedOut = $false
        while ($true) {
            $process.Refresh()
            if ($process.HasExited) { break }
            if ($OnTick) {
                try { & $OnTick $process $stopwatch.Elapsed } catch {}
            }
            if ($TimeoutSec -gt 0 -and $stopwatch.Elapsed.TotalSeconds -ge $TimeoutSec) {
                $timedOut = $true
                try { if (-not $process.HasExited) { $process.Kill() } } catch {}
                break
            }
            Start-Sleep -Milliseconds 1000
        }

        try { $process.WaitForExit() } catch {}
        $stopwatch.Stop()

        $processHasExited = $false
        try { $processHasExited = [bool]$process.HasExited } catch { $processHasExited = $false }
        $exitCode = $null
        $exitCodeAvailable = $false
        $exitCodeReadError = $null
        if ($processHasExited) {
            try {
                $exitCode = [int]$process.ExitCode
                $exitCodeAvailable = $true
            }
            catch {
                $exitCodeReadError = $_.Exception.Message
            }
        }
        else {
            $exitCodeReadError = 'Proces was nog niet beeindigd toen de exitcode werd uitgelezen.'
        }
        $exitCodeText = if ($exitCodeAvailable) {
            [string]$exitCode
        }
        elseif ([string]::IsNullOrWhiteSpace($exitCodeReadError)) {
            'unavailable'
        }
        else {
            "unavailable: $exitCodeReadError"
        }

        $stdout = if (Test-Path -LiteralPath $resolvedStdoutPath) { Get-Content -LiteralPath $resolvedStdoutPath -Raw -Encoding UTF8 } else { '' }
        $stderr = if (Test-Path -LiteralPath $resolvedStderrPath) { Get-Content -LiteralPath $resolvedStderrPath -Raw -Encoding UTF8 } else { '' }
        return [pscustomobject]@{
            ExitCode = $exitCode
            ExitCodeAvailable = $exitCodeAvailable
            ExitCodeText = $exitCodeText
            ExitCodeReadError = $exitCodeReadError
            ProcessHasExited = $processHasExited
            Stdout = $stdout
            Stderr = $stderr
            TimedOut = $timedOut
            DurationMs = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
            ProcessId = $process.Id
        }
    }
    finally {
        foreach ($key in $previousEnv.Keys) {
            [Environment]::SetEnvironmentVariable($key, $previousEnv[$key], 'Process')
        }
        if ($cleanupStdout -and (Test-Path -LiteralPath $resolvedStdoutPath)) {
            try { Remove-Item -LiteralPath $resolvedStdoutPath -Force -ErrorAction SilentlyContinue } catch {}
        }
        if ($cleanupStderr -and (Test-Path -LiteralPath $resolvedStderrPath)) {
            try { Remove-Item -LiteralPath $resolvedStderrPath -Force -ErrorAction SilentlyContinue } catch {}
        }
    }
}

function Get-SessionDurationSeconds {
    param([psobject]$Manifest)
    if ($null -eq $Manifest) { return 0 }
    $createdAt = Convert-ToDateTimeOffsetSafe -Value $Manifest.created_at
    $finishedAt = Convert-ToDateTimeOffsetSafe -Value $Manifest.finished_at
    if ($null -eq $createdAt -or $null -eq $finishedAt) { return 0 }
    $seconds = ($finishedAt - $createdAt).TotalSeconds
    if ($seconds -lt 0) { return 0 }
    return [math]::Round($seconds, 3)
}

function Test-IsLongFormSession {
    param([psobject]$Manifest)
    return (Get-SessionDurationSeconds -Manifest $Manifest) -ge 2700
}

function Get-HelperTimeoutSeconds {
    param([psobject]$Manifest)

    $minimum = 900
    $fallback = 2700
    if ($null -eq $Manifest) { return $fallback }

    $sessionDuration = Get-SessionDurationSeconds -Manifest $Manifest
    if ($sessionDuration -le 0) { return $fallback }

    $multiplier = if (Test-IsLongFormSession -Manifest $Manifest) { 3.5 } else { 3.0 }
    $buffer = if (Test-IsLongFormSession -Manifest $Manifest) { 600 } else { 300 }
    $calculated = [int][math]::Ceiling(($sessionDuration * $multiplier) + $buffer)
    return [math]::Max($minimum, [math]::Min($calculated, 10800))
}

function Get-HelperEmergencyTimeoutSeconds {
    param([psobject]$Manifest)
    $softLimitSec = Get-HelperTimeoutSeconds -Manifest $Manifest
    $emergencyCeilingSec = [int][math]::Ceiling([math]::Max(($softLimitSec * 2.0), ($softLimitSec + 1800)))
    return [math]::Min($emergencyCeilingSec, 21600)
}

function Get-HelperMeaningfulIdleSeconds {
    param(
        [psobject]$Manifest,
        [string]$PhaseName = ''
    )

    $isLongForm = Test-IsLongFormSession -Manifest $Manifest
    $phaseKey = if ([string]::IsNullOrWhiteSpace($PhaseName)) { '' } else { $PhaseName.ToLowerInvariant() }
    switch ($phaseKey) {
        'diarization' { return $(if ($isLongForm) { 900 } else { 420 }) }
        'align' { return $(if ($isLongForm) { 600 } else { 300 }) }
        'transcribe' { return $(if ($isLongForm) { 600 } else { 300 }) }
        default { return $(if ($isLongForm) { 600 } else { 300 }) }
    }
}

function Get-HelperPhaseBudgetSeconds {
    param(
        [string]$PhaseName,
        [psobject]$Manifest
    )

    $duration = Get-SessionDurationSeconds -Manifest $Manifest
    $phaseKey = if ([string]::IsNullOrWhiteSpace($PhaseName)) { '' } else { $PhaseName.ToLowerInvariant() }
    $isLongForm = Test-IsLongFormSession -Manifest $Manifest
    $budget = switch ($phaseKey) {
        'audio_load' { 90; break }
        'model_load' { 240; break }
        'transcribe' {
            if ($duration -le 0) { 900 } else { [int][math]::Min(1800, [math]::Max(180, [math]::Ceiling(($duration * 0.35) + 120))) }
            break
        }
        'align_load' { 180; break }
        'align' {
            if ($duration -le 0) { 900 } else { [int][math]::Min(1500, [math]::Max(180, [math]::Ceiling(($duration * 0.30) + 90))) }
            break
        }
        'diarization_load' { 240; break }
        'diarization' {
            if ($duration -le 0) { 900 } else { [int][math]::Min(1200, [math]::Max(180, [math]::Ceiling(($duration * 0.25) + 120))) }
            break
        }
        'export' { 120; break }
        default { 300; break }
    }

    if ($isLongForm) {
        switch ($phaseKey) {
            'transcribe' { return [int][math]::Min(2700, [math]::Ceiling($budget * 1.5)) }
            'align' { return [int][math]::Min(2400, [math]::Ceiling($budget * 1.5)) }
            'diarization' { return [int][math]::Min(5400, [math]::Ceiling($budget * 2.0)) }
        }
    }

    return $budget
}

function Get-LatestFileActivityTimestamp {
    param([string[]]$Paths)
    $latest = $null
    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) { continue }
        try {
            $item = Get-Item -LiteralPath $path -ErrorAction Stop
            $timestamp = [datetimeoffset]::new($item.LastWriteTimeUtc, [timespan]::Zero)
            if ($null -eq $latest -or $timestamp -gt $latest) {
                $latest = $timestamp
            }
        }
        catch {}
    }
    return $latest
}

function Test-RequiredPath {
    param([string]$PathToCheck, [string]$Label)
    if (-not (Test-Path -LiteralPath $PathToCheck)) {
        throw "$Label niet gevonden: $PathToCheck"
    }
}

function Test-CommandAvailable {
    param([string]$CommandName, [string]$Label)
    if (-not (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "$Label niet beschikbaar: $CommandName"
    }
}

function Read-Manifest {
    param([string]$ManifestPath)
    return (Read-JsonFile -LiteralPath $ManifestPath)
}

function Convert-ToDateTimeOffsetSafe {
    param($Value)
    if ($null -eq $Value) { return $null }
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    try {
        return [System.DateTimeOffset]::Parse($text, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
    }
    catch {
        return $null
    }
}

function Write-TextFileAtomic {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $fileName = Split-Path -Leaf $Path
    $tempPath = if ([string]::IsNullOrWhiteSpace($directory)) {
        ".$fileName.$([guid]::NewGuid().ToString('N')).tmp"
    } else {
        Join-Path $directory (".$fileName.$([guid]::NewGuid().ToString('N')).tmp")
    }

    try {
        [System.IO.File]::WriteAllText($tempPath, $Value, [System.Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $tempPath -Destination $Path -Force
    }
    finally {
        try {
            if (Test-Path -LiteralPath $tempPath) { Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue }
        } catch {}
    }
}

function Save-Manifest {
    param([string]$ManifestPath, [psobject]$Manifest)
    $json = $Manifest | ConvertTo-Json -Depth 100
    Write-TextFileAtomic -Path $ManifestPath -Value $json
}

function Set-Prop {
    param([psobject]$Object, [string]$Name, $Value)
    if ($Object.PSObject.Properties[$Name]) { $Object.$Name = $Value }
    else { Add-Member -InputObject $Object -NotePropertyName $Name -NotePropertyValue $Value -Force }
}

function Ensure-ObjectProp {
    param([psobject]$Object, [string]$Name)
    if ($null -eq $Object) { throw "Kan property '$Name' niet initialiseren op null-object." }
    if (-not $Object.PSObject.Properties[$Name] -or $null -eq $Object.$Name) {
        Set-Prop -Object $Object -Name $Name -Value ([pscustomobject]@{})
    }
    return $Object.$Name
}

function Get-PropValue {
    param($Object, [string]$Name)
    if ($null -eq $Object -or -not $Object.PSObject.Properties[$Name]) { return $null }
    return $Object.PSObject.Properties[$Name].Value
}

function Resolve-TranscriptionCompletionAuthority {
    param([psobject]$Manifest)
    $phaseTimings = Get-PropValue -Object $Manifest -Name 'phase_timings'
    $workerTimings = Get-PropValue -Object $phaseTimings -Name 'worker'
    $helperProgress = Get-PropValue -Object $workerTimings -Name 'helper_progress'
    $helperPhases = Get-PropValue -Object $helperProgress -Name 'phases'
    $exportPhase = Get-PropValue -Object $helperPhases -Name 'export'
    $transcribePhase = Get-PropValue -Object $helperPhases -Name 'transcribe'

    $candidates = @(
        @{ value = (Get-PropValue -Object $workerTimings -Name 'completed_at'); authority = 'worker_completed_at' },
        @{ value = (Get-PropValue -Object $workerTimings -Name 'helper_finished_at'); authority = 'helper_finished_at' },
        @{ value = (Get-PropValue -Object $helperProgress -Name 'completed_at'); authority = 'helper_progress_completed_at' },
        @{ value = (Get-PropValue -Object $exportPhase -Name 'finished_at'); authority = 'helper_export_finished_at' },
        @{ value = (Get-PropValue -Object $transcribePhase -Name 'finished_at'); authority = 'helper_transcribe_finished_at' }
    )

    foreach ($candidate in $candidates) {
        $timestamp = Convert-ToDateTimeOffsetSafe -Value $candidate.value
        if ($timestamp) {
            return [pscustomobject]@{ Timestamp = $timestamp; Authority = $candidate.authority }
        }
    }

    return [pscustomobject]@{ Timestamp = [datetimeoffset]::Now; Authority = 'worker_complete_now' }
}

function Read-HelperProgressFile {
    param([string]$ProgressPath)
    if ([string]::IsNullOrWhiteSpace($ProgressPath) -or -not (Test-Path -LiteralPath $ProgressPath)) { return $null }
    try {
        return Get-Content -LiteralPath $ProgressPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Sync-HelperProgressToManifest {
    param(
        [psobject]$Manifest,
        [string]$ManifestPath,
        [psobject]$WorkerTimings,
        [string]$ProgressPath,
        [string]$LastHeartbeatSeen = $null
    )

    $progress = Read-HelperProgressFile -ProgressPath $ProgressPath
    if ($null -eq $progress) {
        return [pscustomobject]@{ Progress = $null; LastHeartbeatSeen = $LastHeartbeatSeen; Updated = $false }
    }

    $heartbeatSeen = $LastHeartbeatSeen
    if ($progress.PSObject.Properties['last_heartbeat_at'] -and $progress.last_heartbeat_at) {
        $heartbeatText = [string]$progress.last_heartbeat_at
        $heartbeatSeen = $heartbeatText
        Set-Prop -Object $Manifest -Name 'last_heartbeat_at' -Value $heartbeatText
        Set-Prop -Object $WorkerTimings -Name 'helper_last_heartbeat_at' -Value $heartbeatText
    }

    Set-Prop -Object $WorkerTimings -Name 'helper_progress' -Value $progress
    if ($progress.PSObject.Properties['state']) { Set-Prop -Object $WorkerTimings -Name 'helper_state' -Value $progress.state }
    if ($progress.PSObject.Properties['current_phase']) { Set-Prop -Object $WorkerTimings -Name 'current_phase' -Value $progress.current_phase }
    if ($progress.PSObject.Properties['phase_started_at']) { Set-Prop -Object $WorkerTimings -Name 'current_phase_started_at' -Value $progress.phase_started_at }
    if ($progress.PSObject.Properties['phase_elapsed_sec']) { Set-Prop -Object $WorkerTimings -Name 'current_phase_elapsed_sec' -Value $progress.phase_elapsed_sec }
    if ($progress.PSObject.Properties['last_completed_phase']) { Set-Prop -Object $WorkerTimings -Name 'last_completed_phase' -Value $progress.last_completed_phase }
    if ($progress.PSObject.Properties['last_activity_at']) { Set-Prop -Object $WorkerTimings -Name 'helper_last_activity_at' -Value $progress.last_activity_at }
    if ($progress.PSObject.Properties['last_progress_event_at']) { Set-Prop -Object $WorkerTimings -Name 'helper_last_progress_event_at' -Value $progress.last_progress_event_at }
    if ($progress.PSObject.Properties['activity_seq']) { Set-Prop -Object $WorkerTimings -Name 'helper_activity_seq' -Value $progress.activity_seq }
    if ($progress.PSObject.Properties['phase_progress']) { Set-Prop -Object $WorkerTimings -Name 'helper_phase_progress' -Value $progress.phase_progress }
    if ($progress.PSObject.Properties['error'] -and $progress.error) { Set-Prop -Object $WorkerTimings -Name 'helper_error' -Value ([string]$progress.error) }

    Save-Manifest -ManifestPath $ManifestPath -Manifest $Manifest
    return [pscustomobject]@{ Progress = $progress; LastHeartbeatSeen = $heartbeatSeen; Updated = $true }
}

function Get-ActiveClaimSnapshot {
    foreach ($root in $script:CurrentConfig.SessionRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $claimFiles = Get-ChildItem -LiteralPath $root -Filter '.transcription.claim' -File -Recurse -ErrorAction SilentlyContinue
        foreach ($claimFile in $claimFiles) {
            try {
                $sessionDir = $claimFile.Directory.FullName
                $claim = Read-SessionClaim -SessionDir $sessionDir
                if ($null -eq $claim) { continue }
                $ownerAlive = Test-ProcessAlive -ProcessId $claim.owner_pid
                $helperAlive = Test-ProcessAlive -ProcessId $claim.helper_pid
                if ($ownerAlive -or $helperAlive) {
                    return [pscustomobject]@{
                        session_dir = $sessionDir
                        owner_alive = $ownerAlive
                        helper_alive = $helperAlive
                        owner_pid = $claim.owner_pid
                        helper_pid = $claim.helper_pid
                    }
                }
            }
            catch {}
        }
    }
    return $null
}

function Get-ReadySessionDirectories {
    $dirs = foreach ($root in $script:CurrentConfig.SessionRoots) {
        if (-not (Test-Path -LiteralPath $root)) {
            Write-Log 'WARN' "Sessieroot ontbreekt en wordt overgeslagen: $root"
            continue
        }

        Get-ChildItem -LiteralPath $root -Filter 'READY' -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $_.Directory.FullName }
    }
    return $dirs | Sort-Object -Unique
}

function Test-ReadyAge {
    param([string]$ReadyPath)
    $readyFile = Get-Item -LiteralPath $ReadyPath -ErrorAction SilentlyContinue
    if (-not $readyFile) { return $false }
    $ageSec = ((Get-Date) - $readyFile.LastWriteTime).TotalSeconds
    return $ageSec -ge $script:CurrentConfig.MinReadyAgeSec
}

function Get-PendingReadyDelaySeconds {
    $minimumDelay = $null
    foreach ($root in $script:CurrentConfig.SessionRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Get-ChildItem -LiteralPath $root -Filter 'READY' -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                $ageSec = ((Get-Date) - $_.LastWriteTime).TotalSeconds
                $remaining = [math]::Ceiling([math]::Max(0, $script:CurrentConfig.MinReadyAgeSec - $ageSec))
                if ($remaining -gt 0) {
                    if ($null -eq $minimumDelay -or $remaining -lt $minimumDelay) {
                        $minimumDelay = [int]$remaining
                    }
                }
            }
            catch {}
        }
    }
    return $minimumDelay
}

function Get-ExpectedOutputs {
    param([string]$SessionDir)
    $format = Get-TranscriptOutputFormat
    $txtPath = if ($format -in @('txt', 'txt+json')) { Join-Path $SessionDir 'transcript.txt' } else { $null }
    $jsonPath = if ($format -in @('json', 'txt+json')) { Join-Path $SessionDir 'transcript.json' } else { $null }
    return [pscustomobject]@{
        TxtPath = $txtPath
        JsonPath = $jsonPath
        WantsTxt = $null -ne $txtPath
        WantsJson = $null -ne $jsonPath
    }
}

function Test-ExpectedOutputsExist {
    param([pscustomobject]$Expected)
    # v8.27.1: waarom de controle faalde, voor de foutmelding (voorheen bleef de reden leeg).
    $script:ExpectedOutputsFailureReason = ''
    if (-not $Expected.WantsTxt -and -not $Expected.WantsJson) { $script:ExpectedOutputsFailureReason = 'geen uitvoerformaat'; return $false }
    try {
        $basePath = if ($Expected.WantsTxt) { $Expected.TxtPath } else { $Expected.JsonPath }
        $receiptPath = Join-Path (Split-Path -Parent $basePath) 'transcript.outputs.json'
        $receipt = Get-Content -LiteralPath $receiptPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ($receipt.schema -ne 'captivox-transcript-outputs-v1') { $script:ExpectedOutputsFailureReason = 'ontvangstbewijs heeft een onbekend schema'; return $false }
        foreach ($kind in @('txt', 'json')) {
            $required = if ($kind -eq 'txt') { $Expected.WantsTxt } else { $Expected.WantsJson }
            if (-not $required) { continue }
            $path = if ($kind -eq 'txt') { $Expected.TxtPath } else { $Expected.JsonPath }
            $entry = $receipt.outputs.$kind
            $file = Get-Item -LiteralPath $path -ErrorAction Stop
            if ($file.PSIsContainer -or $null -eq $entry -or $file.Length -ne $entry.bytes) { $script:ExpectedOutputsFailureReason = "$kind wijkt in grootte af van het ontvangstbewijs"; return $false }
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash -ne $entry.sha256) { $script:ExpectedOutputsFailureReason = "$kind wijkt in inhoud (hash) af van het ontvangstbewijs"; return $false }
            if ($kind -eq 'json') {
                $transcript = Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
                if ($null -eq $transcript.segments -or $transcript.segments -isnot [array]) { $script:ExpectedOutputsFailureReason = 'transcript.json heeft geen segmentenlijst'; return $false }
                foreach ($segment in $transcript.segments) { if ($segment.text -isnot [string]) { $script:ExpectedOutputsFailureReason = 'transcript.json heeft een segment zonder tekst'; return $false } }
            }
        }
        return $true
    }
    catch { $script:ExpectedOutputsFailureReason = 'controle mislukt: ' + $_.Exception.Message; return $false }
}

function Get-ClaimFilePath {
    param([string]$SessionDir)
    return (Join-Path $SessionDir '.transcription.claim')
}

function Remove-SessionClaim {
    param([string]$SessionDir)
    $claimPath = Get-ClaimFilePath -SessionDir $SessionDir
    try {
        if (Test-Path -LiteralPath $claimPath) {
            Remove-Item -LiteralPath $claimPath -Force -ErrorAction SilentlyContinue
        }
    } catch {}
}

function Read-SessionClaim {
    param([string]$SessionDir)
    $claimPath = Get-ClaimFilePath -SessionDir $SessionDir
    if (-not (Test-Path -LiteralPath $claimPath)) { return $null }
    try {
        return (Read-JsonFile -LiteralPath $claimPath)
    }
    catch {
        return $null
    }
}

function Test-ProcessAlive {
    param($ProcessId)
    if ($null -eq $ProcessId) { return $false }
    $parsed = 0
    if (-not [int]::TryParse([string]$ProcessId, [ref]$parsed)) { return $false }
    try {
        $proc = Get-Process -Id $parsed -ErrorAction Stop
        return $null -ne $proc
    }
    catch {
        return $false
    }
}

function Stop-ProcessIfAlive {
    param($ProcessId, [string]$Reason = 'stale-worker-version')
    if ($null -eq $ProcessId) { return }
    $parsed = 0
    if (-not [int]::TryParse([string]$ProcessId, [ref]$parsed)) { return }
    try {
        $proc = Get-Process -Id $parsed -ErrorAction Stop
        Stop-Process -Id $parsed -Force -ErrorAction Stop
        Write-Log 'WARN' "Proces $parsed beeindigd vanwege $Reason."
    }
    catch {
        Write-Log 'DEBUG' "Proces $parsed niet beeindigd of niet meer actief: $($_.Exception.Message)"
    }
}

function Test-ClaimVersionMismatch {
    param($Claim)
    if ($null -eq $Claim) { return $false }
    if (-not $Claim.PSObject.Properties['worker_version']) { return $false }
    $claimWorkerVersion = [string]$Claim.worker_version
    return (-not [string]::IsNullOrWhiteSpace($claimWorkerVersion) -and $claimWorkerVersion -ne $WorkerVersion)
}

function Update-SessionClaim {
    param([string]$SessionDir, [hashtable]$Updates)
    $claimPath = Get-ClaimFilePath -SessionDir $SessionDir
    if (-not (Test-Path -LiteralPath $claimPath)) { return $null }
    try {
        $claim = Read-JsonFile -LiteralPath $claimPath
    }
    catch {
        return $null
    }
    if ($null -eq $claim) { return $null }
    foreach ($entry in $Updates.GetEnumerator()) {
        Set-Prop -Object $claim -Name $entry.Key -Value $entry.Value
    }
    $json = $claim | ConvertTo-Json -Depth 20
    Write-TextFileAtomic -Path $claimPath -Value $json
    return $claim
}

function Acquire-SessionClaim {
    param([string]$SessionDir, [psobject]$Manifest)
    $claimPath = Get-ClaimFilePath -SessionDir $SessionDir
    $claim = [pscustomobject]@{
        app_version = $(if ($Manifest.PSObject.Properties['app_version'] -and -not [string]::IsNullOrWhiteSpace([string]$Manifest.app_version)) { [string]$Manifest.app_version } else { $WorkerVersion })
        worker_version = $WorkerVersion
        worker_host = $env:COMPUTERNAME
        worker_pid = $PID
        owner_pid = $PID
        owner_role = $(if ($ProcessSessionDir) { 'session-runner' } else { 'scan-worker' })
        helper_pid = $null
        helper_started_at = $null
        helper_finished_at = $null
        claimed_at = [datetimeoffset]::Now.ToString('o')
        meeting_id = $Manifest.meeting_id
    }
    $json = $claim | ConvertTo-Json -Depth 10
    $tempPath = $null

    try {
        $directory = Split-Path -Parent $claimPath
        $tempPath = Join-Path $directory (".transcription.claim.$([guid]::NewGuid().ToString('N')).tmp")
        [System.IO.File]::WriteAllText($tempPath, $json, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Move($tempPath, $claimPath)
        return $claimPath
    }
    catch {
        try { if ($tempPath -and (Test-Path -LiteralPath $tempPath)) { Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue } } catch {}
        throw 'Sessie kon niet atomair geclaimd worden; mogelijk verwerkt een andere worker deze sessie al.'
    }
}

function Get-SessionDecision {
    param([string]$SessionDir)
    $manifestPath = Join-Path $SessionDir 'manifest.json'
    $readyPath = Join-Path $SessionDir 'READY'
    $audioPath = Join-Path $SessionDir 'audio_mix.wav'
    $expected = Get-ExpectedOutputs -SessionDir $SessionDir
    $claimPath = Get-ClaimFilePath -SessionDir $SessionDir

    if (-not (Test-Path -LiteralPath $readyPath)) { return @{ Process = $false; Reason = 'READY ontbreekt.' } }
    if (-not (Test-ReadyAge -ReadyPath $readyPath)) { return @{ Process = $false; Reason = 'READY nog te recent.' } }
    if (-not (Test-Path -LiteralPath $manifestPath)) { return @{ Process = $false; Reason = 'manifest.json ontbreekt.' } }
    if (-not (Test-Path -LiteralPath $audioPath)) { return @{ Process = $false; Reason = 'audio_mix.wav ontbreekt.' } }

    $manifest = Read-Manifest -ManifestPath $manifestPath

    if ($manifest.status -ne 'completed') { return @{ Process = $false; Reason = "Recorderstatus is '$($manifest.status)' in plaats van 'completed'."; Manifest = $manifest; ManifestPath = $manifestPath } }

    if ($manifest.transcription_status -eq 'completed') {
        return @{ Process = $false; Reason = 'Transcriptie is al voltooid.'; Manifest = $manifest; ManifestPath = $manifestPath }
    }
    $claim = Read-SessionClaim -SessionDir $SessionDir
    if (Test-ClaimVersionMismatch -Claim $claim) {
        $staleClaimWorkerVersion = [string]$claim.worker_version
        Write-DiagnosticsLog -SessionDir $SessionDir -Message "Stale claim-versie gedetecteerd: claim_worker=$staleClaimWorkerVersion, actuele_worker=$WorkerVersion."
        $ownerAlive = Test-ProcessAlive -ProcessId $claim.owner_pid
        $helperAlive = Test-ProcessAlive -ProcessId $claim.helper_pid
        if ($ownerAlive) { Stop-ProcessIfAlive -ProcessId $claim.owner_pid -Reason "stale claim worker version $staleClaimWorkerVersion != $WorkerVersion" }
        if ($helperAlive) { Stop-ProcessIfAlive -ProcessId $claim.helper_pid -Reason "stale helper worker version $staleClaimWorkerVersion != $WorkerVersion" }
        Remove-SessionClaim -SessionDir $SessionDir
        $claim = $null
        if ($manifest.transcription_status -eq 'started' -and -not (Test-ExpectedOutputsExist -Expected $expected)) {
            Set-Prop -Object $manifest -Name 'transcription_status' -Value 'queued'
            Set-Prop -Object $manifest -Name 'transcription_error' -Value "Stale workerclaim $staleClaimWorkerVersion vrijgegeven voor herverwerking door $WorkerVersion."
            Set-Prop -Object $manifest -Name 'transcription_started_at' -Value $null
            Save-Manifest -ManifestPath $manifestPath -Manifest $manifest
            $manifest = Read-Manifest -ManifestPath $manifestPath
        }
    }
    if (Test-ExpectedOutputsExist -Expected $expected) {
        $ownerAlive = $false
        $helperAlive = $false
        if ($claim) {
            $ownerAlive = Test-ProcessAlive -ProcessId $claim.owner_pid
            $helperAlive = Test-ProcessAlive -ProcessId $claim.helper_pid
        }

        if ($manifest.transcription_status -eq 'completed' -or (-not $ownerAlive -and -not $helperAlive)) {
            Complete-Session -Manifest $manifest -ManifestPath $manifestPath -TxtPath $expected.TxtPath -JsonPath $expected.JsonPath
            Remove-SessionClaim -SessionDir $SessionDir
            $manifest = Read-Manifest -ManifestPath $manifestPath
            return @{ Process = $false; Reason = 'Transcriptartefacten waren al aanwezig; sessie is op completed gezet en wordt niet opnieuw verwerkt.'; Manifest = $manifest; ManifestPath = $manifestPath }
        }
    }

    $startedAt = Convert-ToDateTimeOffsetSafe -Value $manifest.transcription_started_at
    if ($manifest.transcription_status -eq 'started' -and $startedAt) {
        $age = (Get-Date) - $startedAt.LocalDateTime
        if ($claim) {
            $ownerAlive = Test-ProcessAlive -ProcessId $claim.owner_pid
            $helperAlive = Test-ProcessAlive -ProcessId $claim.helper_pid
            if ($ownerAlive -or $helperAlive) {
                $liveRole = if ($helperAlive) { "helper PID $($claim.helper_pid)" } else { "$($claim.owner_role) PID $($claim.owner_pid)" }
                return @{ Process = $false; Reason = "Sessie is actief via $liveRole sinds $startedAt."; Manifest = $manifest; ManifestPath = $manifestPath }
            }

            $claimStartedAt = Convert-ToDateTimeOffsetSafe -Value $claim.claimed_at
            $claimAgeSec = if ($claimStartedAt) { [math]::Round(((Get-Date) - $claimStartedAt.LocalDateTime).TotalSeconds, 3) } else { [math]::Round($age.TotalSeconds, 3) }
            if (Test-ExpectedOutputsExist -Expected $expected) {
                Complete-Session -Manifest $manifest -ManifestPath $manifestPath -TxtPath $expected.TxtPath -JsonPath $expected.JsonPath
                Remove-SessionClaim -SessionDir $SessionDir
                return @{ Process = $false; Reason = 'Onderbroken runner hersteld: transcriptartefacten waren al aanwezig en manifest is op completed gezet.'; Manifest = $manifest; ManifestPath = $manifestPath }
            }
            if ($claimAgeSec -lt 30) {
                return @{ Process = $false; Reason = "Claim bestaat nog maar ${claimAgeSec}s en wacht op runner-bootstrap."; Manifest = $manifest; ManifestPath = $manifestPath }
            }

            Fail-Session -Manifest $manifest -ManifestPath $manifestPath -Message 'Transcriptierunner of helper is weggevallen voor voltooiing; claim vrijgegeven voor herverwerking.'
            Remove-SessionClaim -SessionDir $SessionDir
            $manifest = Read-Manifest -ManifestPath $manifestPath
        }
        else {
            if (Test-ExpectedOutputsExist -Expected $expected) {
                Complete-Session -Manifest $manifest -ManifestPath $manifestPath -TxtPath $expected.TxtPath -JsonPath $expected.JsonPath
                return @{ Process = $false; Reason = 'Transcriptartefacten waren aanwezig zonder claimbestand; manifest is op completed gezet.'; Manifest = $manifest; ManifestPath = $manifestPath }
            }
            if ($age.TotalSeconds -lt 30) {
                return @{ Process = $false; Reason = "Sessie staat op started sinds $startedAt maar runner-bootstrap kreeg nog geen claimbestand."; Manifest = $manifest; ManifestPath = $manifestPath }
            }
            Fail-Session -Manifest $manifest -ManifestPath $manifestPath -Message 'Transcriptiestatus stond op started zonder claimbestand; sessie vrijgegeven voor herverwerking.'
            $manifest = Read-Manifest -ManifestPath $manifestPath
        }
    }
    if ($manifest.transcription_status -eq 'failed' -and -not $script:CurrentConfig.RetryFailedSessions) {
        return @{ Process = $false; Reason = 'Transcriptie is eerder mislukt en retries staan uit.'; Manifest = $manifest; ManifestPath = $manifestPath }
    }

    $attempts = 0
    if ($manifest.PSObject.Properties['transcription_attempts'] -and $manifest.transcription_attempts -ne $null) { $attempts = [int]$manifest.transcription_attempts }
    if ($manifest.transcription_status -eq 'failed' -and $script:CurrentConfig.MaxRetryAttempts -gt 0 -and $attempts -ge $script:CurrentConfig.MaxRetryAttempts) {
        return @{ Process = $false; Reason = "Max retries bereikt ($attempts/$($script:CurrentConfig.MaxRetryAttempts))."; Manifest = $manifest; ManifestPath = $manifestPath }
    }

    $finishedAt = Convert-ToDateTimeOffsetSafe -Value $manifest.transcription_finished_at
    if ($manifest.transcription_status -eq 'failed' -and $finishedAt -and $script:CurrentConfig.RetryBackoffMinutes -gt 0) {
        $retryAge = (Get-Date) - $finishedAt.LocalDateTime
        if ($retryAge.TotalMinutes -lt $script:CurrentConfig.RetryBackoffMinutes) {
            return @{ Process = $false; Reason = "Retry-backoff actief tot $($finishedAt.LocalDateTime.AddMinutes($script:CurrentConfig.RetryBackoffMinutes))."; Manifest = $manifest; ManifestPath = $manifestPath }
        }
    }

    if (Test-Path -LiteralPath $claimPath) {
        $claim = if ($claim) { $claim } else { Read-SessionClaim -SessionDir $SessionDir }
        if ($claim) {
            $ownerAlive = Test-ProcessAlive -ProcessId $claim.owner_pid
            $helperAlive = Test-ProcessAlive -ProcessId $claim.helper_pid
            if ($ownerAlive -or $helperAlive) {
                return @{ Process = $false; Reason = 'Er bestaat al een actieve sessieclaim voor deze opname.'; Manifest = $manifest; ManifestPath = $manifestPath }
            }
        }
        try { Remove-Item -LiteralPath $claimPath -Force -ErrorAction SilentlyContinue } catch {}
    }

    return @{
        Process = $true
        Manifest = $manifest
        ManifestPath = $manifestPath
        AudioPath = $audioPath
        TxtPath = $expected.TxtPath
        JsonPath = $expected.JsonPath
        ClaimPath = $claimPath
        Expected = $expected
    }
}

function Claim-Session {
    param([psobject]$Manifest, [string]$ManifestPath, [string]$AudioPath, [string]$TxtPath, [string]$JsonPath)
    $startedAt = [datetimeoffset]::Now
    if (-not $Manifest.PSObject.Properties['phase_timings']) { Set-Prop -Object $Manifest -Name 'phase_timings' -Value ([pscustomobject]@{}) }
    $phaseTimings = Ensure-ObjectProp -Object $Manifest -Name 'phase_timings'
    $transcriptionTimings = Ensure-ObjectProp -Object $phaseTimings -Name 'transcription'
    Set-Prop -Object $Manifest -Name 'app_version' -Value $(if ($Manifest.PSObject.Properties['app_version'] -and -not [string]::IsNullOrWhiteSpace([string]$Manifest.app_version)) { [string]$Manifest.app_version } else { $WorkerVersion })
    Set-Prop -Object $Manifest -Name 'transcription_status' -Value 'started'
    Set-Prop -Object $Manifest -Name 'transcription_started_at' -Value $startedAt.ToString('o')
    Set-Prop -Object $Manifest -Name 'transcription_finished_at' -Value $null
    Set-Prop -Object $transcriptionTimings -Name 'started_at' -Value $startedAt.ToString('o')
    Set-Prop -Object $transcriptionTimings -Name 'finished_at' -Value $null
    Set-Prop -Object $transcriptionTimings -Name 'status' -Value 'started'
    Set-Prop -Object $transcriptionTimings -Name 'authority' -Value 'worker_manifest'
    Set-Prop -Object $transcriptionTimings -Name 'last_writer' -Value $WorkerVersion
    Set-Prop -Object $Manifest -Name 'transcription_error' -Value $null
    Set-Prop -Object $Manifest -Name 'transcript_text_path' -Value $TxtPath
    Set-Prop -Object $Manifest -Name 'transcript_json_path' -Value $JsonPath
    Set-Prop -Object $Manifest -Name 'transcription_input_path' -Value $AudioPath
    $claimProfile = Resolve-EffectiveTranscriptionProfile
    $claimEngine = switch ($claimProfile) {
        'amd-gpu' { 'pending:FFmpeg Whisper GPU' }
        'nvidia-cuda' { ("pending:whisperx:{0}" -f $script:CurrentConfig.Model) }
        'cpu-fallback' { ("pending:whisperx-cpu:{0}" -f $script:CurrentConfig.Model) }
        default { 'pending' }
    }
    Set-Prop -Object $Manifest -Name 'transcription_engine' -Value $claimEngine
    Set-Prop -Object $Manifest -Name 'requested_transcription_profile' -Value (Normalize-TranscriptionProfile -Profile $script:CurrentConfig.TranscriptionProfile)
    Set-Prop -Object $Manifest -Name 'active_transcription_profile' -Value $claimProfile
    Set-Prop -Object $Manifest -Name 'transcript_output_format' -Value $script:CurrentConfig.TranscriptOutputFormat
    Set-Prop -Object $Manifest -Name 'worker_version' -Value $WorkerVersion
    Set-Prop -Object $Manifest -Name 'worker_host' -Value $env:COMPUTERNAME
    $attempts = 0
    if ($Manifest.PSObject.Properties['transcription_attempts'] -and $Manifest.transcription_attempts -ne $null) { $attempts = [int]$Manifest.transcription_attempts }
    Set-Prop -Object $Manifest -Name 'transcription_attempts' -Value ($attempts + 1)
    Save-Manifest -ManifestPath $ManifestPath -Manifest $Manifest
}

function Complete-Session {
    param([psobject]$Manifest, [string]$ManifestPath, [string]$TxtPath, [string]$JsonPath)
    $completionAuthority = Resolve-TranscriptionCompletionAuthority -Manifest $Manifest
    $finishedAt = $completionAuthority.Timestamp
    if (-not $Manifest.PSObject.Properties['phase_timings']) { Set-Prop -Object $Manifest -Name 'phase_timings' -Value ([pscustomobject]@{}) }
    $phaseTimings = Ensure-ObjectProp -Object $Manifest -Name 'phase_timings'
    $transcriptionTimings = Ensure-ObjectProp -Object $phaseTimings -Name 'transcription'
    Set-Prop -Object $Manifest -Name 'transcription_status' -Value 'completed'
    Set-Prop -Object $Manifest -Name 'transcription_finished_at' -Value $finishedAt.ToString('o')
    Set-Prop -Object $transcriptionTimings -Name 'finished_at' -Value $finishedAt.ToString('o')
    Set-Prop -Object $transcriptionTimings -Name 'status' -Value 'completed'
    Set-Prop -Object $transcriptionTimings -Name 'authority' -Value $completionAuthority.Authority
    Set-Prop -Object $transcriptionTimings -Name 'last_completed_by' -Value 'Complete-Session'
    Set-Prop -Object $Manifest -Name 'transcription_finished_at_authority' -Value $completionAuthority.Authority
    Set-Prop -Object $transcriptionTimings -Name 'last_writer' -Value $WorkerVersion
    Set-Prop -Object $Manifest -Name 'transcription_error' -Value $null
    Set-Prop -Object $Manifest -Name 'transcript_text_path' -Value $TxtPath
    Set-Prop -Object $Manifest -Name 'transcript_json_path' -Value $JsonPath
    $outputDir = if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
        Split-Path -Parent $JsonPath
    }
    elseif (-not [string]::IsNullOrWhiteSpace($TxtPath)) {
        Split-Path -Parent $TxtPath
    }
    else {
        Split-Path -Parent $ManifestPath
    }
    if ($script:CurrentConfig.ExportSrt) { Set-Prop -Object $Manifest -Name 'transcript_srt_path' -Value (Join-Path (Join-Path $outputDir 'exports') 'transcript.srt') }
    if ($script:CurrentConfig.ExportVtt) { Set-Prop -Object $Manifest -Name 'transcript_vtt_path' -Value (Join-Path (Join-Path $outputDir 'exports') 'transcript.vtt') }
    Set-Prop -Object $Manifest -Name 'transcript_schema_version' -Value 'captivox-transcript-v1'

    try {
        $sessionDirForMetadata = Split-Path -Parent $ManifestPath
        $helperTimingsPath = Get-SessionDiagnosticsPath -SessionDir $sessionDirForMetadata -FileName 'phase_timings.json'
        if (Test-Path -LiteralPath $helperTimingsPath) {
            $helperMetadata = Read-JsonFile -LiteralPath $helperTimingsPath

            if ($helperMetadata.PSObject.Properties['engine'] -and -not [string]::IsNullOrWhiteSpace([string]$helperMetadata.engine)) {
                Set-Prop -Object $Manifest -Name 'transcription_engine' -Value ([string]$helperMetadata.engine)
                Set-Prop -Object $transcriptionTimings -Name 'engine' -Value ([string]$helperMetadata.engine)
            }
            if ($helperMetadata.PSObject.Properties['active_transcription_profile'] -and -not [string]::IsNullOrWhiteSpace([string]$helperMetadata.active_transcription_profile)) {
                Set-Prop -Object $Manifest -Name 'active_transcription_profile' -Value ([string]$helperMetadata.active_transcription_profile)
                Set-Prop -Object $transcriptionTimings -Name 'active_transcription_profile' -Value ([string]$helperMetadata.active_transcription_profile)
            }
            if ($helperMetadata.PSObject.Properties['requested_transcription_profile'] -and -not [string]::IsNullOrWhiteSpace([string]$helperMetadata.requested_transcription_profile)) {
                Set-Prop -Object $Manifest -Name 'requested_transcription_profile' -Value ([string]$helperMetadata.requested_transcription_profile)
                Set-Prop -Object $transcriptionTimings -Name 'requested_transcription_profile' -Value ([string]$helperMetadata.requested_transcription_profile)
            }
            if ($helperMetadata.PSObject.Properties['device'] -and -not [string]::IsNullOrWhiteSpace([string]$helperMetadata.device)) {
                Set-Prop -Object $Manifest -Name 'transcription_runtime_device' -Value ([string]$helperMetadata.device)
                Set-Prop -Object $transcriptionTimings -Name 'runtime_device' -Value ([string]$helperMetadata.device)
            }
            if ($helperMetadata.PSObject.Properties['compute_type'] -and -not [string]::IsNullOrWhiteSpace([string]$helperMetadata.compute_type)) {
                Set-Prop -Object $Manifest -Name 'transcription_compute_type' -Value ([string]$helperMetadata.compute_type)
                Set-Prop -Object $transcriptionTimings -Name 'compute_type' -Value ([string]$helperMetadata.compute_type)
            }
            if ($helperMetadata.PSObject.Properties['model'] -and -not [string]::IsNullOrWhiteSpace([string]$helperMetadata.model)) {
                Set-Prop -Object $Manifest -Name 'transcription_model' -Value ([string]$helperMetadata.model)
                Set-Prop -Object $transcriptionTimings -Name 'model' -Value ([string]$helperMetadata.model)
            }
            if ($helperMetadata.PSObject.Properties['gpu_fallback_active']) {
                Set-Prop -Object $Manifest -Name 'gpu_fallback_active' -Value ([bool]$helperMetadata.gpu_fallback_active)
                Set-Prop -Object $transcriptionTimings -Name 'gpu_fallback_active' -Value ([bool]$helperMetadata.gpu_fallback_active)
            }
            if ($helperMetadata.PSObject.Properties['gpu_fallback_reason']) {
                Set-Prop -Object $Manifest -Name 'gpu_fallback_reason' -Value $helperMetadata.gpu_fallback_reason
                Set-Prop -Object $transcriptionTimings -Name 'gpu_fallback_reason' -Value $helperMetadata.gpu_fallback_reason
            }
            if ($helperMetadata.PSObject.Properties['vulkan_confirmed']) {
                Set-Prop -Object $Manifest -Name 'vulkan_confirmed' -Value ([bool]$helperMetadata.vulkan_confirmed)
                Set-Prop -Object $transcriptionTimings -Name 'vulkan_confirmed' -Value ([bool]$helperMetadata.vulkan_confirmed)
            }
            if ($helperMetadata.PSObject.Properties['diarization_applied']) {
                Set-Prop -Object $Manifest -Name 'diarization_applied' -Value ([bool]$helperMetadata.diarization_applied)
            }
            if ($helperMetadata.PSObject.Properties['diarization_status']) {
                Set-Prop -Object $Manifest -Name 'diarization_status' -Value $helperMetadata.diarization_status
            }
            if ($helperMetadata.PSObject.Properties['diarization_warning']) {
                Set-Prop -Object $Manifest -Name 'diarization_warning' -Value $helperMetadata.diarization_warning
            }
            if ($helperMetadata.PSObject.Properties['diarization_requested']) {
                Set-Prop -Object $Manifest -Name 'diarization_requested' -Value ([bool]$helperMetadata.diarization_requested)
            }
            if ($helperMetadata.PSObject.Properties['diarization_engine']) {
                Set-Prop -Object $Manifest -Name 'diarization_engine' -Value $helperMetadata.diarization_engine
            }
            if ($helperMetadata.PSObject.Properties['diarization_device']) {
                Set-Prop -Object $Manifest -Name 'diarization_device' -Value $helperMetadata.diarization_device
            }
            if ($helperMetadata.PSObject.Properties['diarization_gpu_backend']) {
                Set-Prop -Object $Manifest -Name 'diarization_gpu_backend' -Value $helperMetadata.diarization_gpu_backend
            }
            if ($helperMetadata.PSObject.Properties['diarization_fallback_active']) {
                Set-Prop -Object $Manifest -Name 'diarization_fallback_active' -Value ([bool]$helperMetadata.diarization_fallback_active)
            }
            if ($helperMetadata.PSObject.Properties['diarization_fallback_reason']) {
                Set-Prop -Object $Manifest -Name 'diarization_fallback_reason' -Value $helperMetadata.diarization_fallback_reason
            }
            if ($helperMetadata.PSObject.Properties['diarization_assigned_segments']) {
                Set-Prop -Object $Manifest -Name 'diarization_assigned_segments' -Value ([int]$helperMetadata.diarization_assigned_segments)
            }
            if ($helperMetadata.PSObject.Properties['diarization_speaker_count']) {
                Set-Prop -Object $Manifest -Name 'diarization_speaker_count' -Value ([int]$helperMetadata.diarization_speaker_count)
            }
            if ($helperMetadata.PSObject.Properties['diarization_model']) {
                Set-Prop -Object $Manifest -Name 'diarization_model' -Value $helperMetadata.diarization_model
            }
            if ($helperMetadata.PSObject.Properties['diarization_requested_model']) {
                Set-Prop -Object $Manifest -Name 'diarization_requested_model' -Value $helperMetadata.diarization_requested_model
            }
            if ($helperMetadata.PSObject.Properties['diarization_effective_model']) {
                Set-Prop -Object $Manifest -Name 'diarization_effective_model' -Value $helperMetadata.diarization_effective_model
            }
            if ($helperMetadata.PSObject.Properties['diarization_model_compatibility_fallback_active']) {
                Set-Prop -Object $Manifest -Name 'diarization_model_compatibility_fallback_active' -Value ([bool]$helperMetadata.diarization_model_compatibility_fallback_active)
            }
            if ($helperMetadata.PSObject.Properties['diarization_model_compatibility_fallback_reason']) {
                Set-Prop -Object $Manifest -Name 'diarization_model_compatibility_fallback_reason' -Value $helperMetadata.diarization_model_compatibility_fallback_reason
            }
            if ($helperMetadata.PSObject.Properties['diarization_execution_mode']) {
                Set-Prop -Object $Manifest -Name 'diarization_execution_mode' -Value $helperMetadata.diarization_execution_mode
            }
            if ($helperMetadata.PSObject.Properties['pyannote_audio_version']) {
                Set-Prop -Object $Manifest -Name 'pyannote_audio_version' -Value $helperMetadata.pyannote_audio_version
            }
            foreach ($fieldName in @('diarization_requested_acceleration','diarization_effective_acceleration','diarization_gpu_available','diarization_gpu_validated','diarization_gpu_device_name','diarization_runtime_python','diarization_runtime_torch','diarization_runtime_torchaudio','diarization_runtime_torch_cuda','diarization_runtime_torch_hip')) {
                if ($helperMetadata.PSObject.Properties[$fieldName]) {
                    Set-Prop -Object $Manifest -Name $fieldName -Value $helperMetadata.PSObject.Properties[$fieldName].Value
                }
            }
        }
    }
    catch {
        Write-DiagnosticsLog -SessionDir (Split-Path -Parent $ManifestPath) -Message "Helpermetadata kon niet naar manifest worden gespiegeld: $($_.Exception.Message)"
    }

    Save-Manifest -ManifestPath $ManifestPath -Manifest $Manifest
}

function Fail-Session {
    param([psobject]$Manifest, [string]$ManifestPath, [string]$Message)
    $finishedAt = [datetimeoffset]::Now
    if (-not $Manifest.PSObject.Properties['phase_timings']) { Set-Prop -Object $Manifest -Name 'phase_timings' -Value ([pscustomobject]@{}) }
    $phaseTimings = Ensure-ObjectProp -Object $Manifest -Name 'phase_timings'
    $transcriptionTimings = Ensure-ObjectProp -Object $phaseTimings -Name 'transcription'
    Set-Prop -Object $Manifest -Name 'transcription_status' -Value 'failed'
    Set-Prop -Object $Manifest -Name 'transcription_finished_at' -Value $finishedAt.ToString('o')
    Set-Prop -Object $transcriptionTimings -Name 'finished_at' -Value $finishedAt.ToString('o')
    Set-Prop -Object $transcriptionTimings -Name 'status' -Value 'failed'
    Set-Prop -Object $transcriptionTimings -Name 'authority' -Value 'worker_manifest'
    Set-Prop -Object $transcriptionTimings -Name 'last_writer' -Value $WorkerVersion
    Set-Prop -Object $Manifest -Name 'transcription_error' -Value $Message
    Save-Manifest -ManifestPath $ManifestPath -Manifest $Manifest
}

function Invoke-PythonWorker {
    param(
        [string]$SessionDir,
        [string]$AudioPath,
        [string]$TxtPath,
        [string]$JsonPath,
        [string]$HfToken,
        [int]$TimeoutSec = 0,
        [string]$ProgressJsonPath = $null,
        [scriptblock]$OnTick = $null,
        [string]$ClaimPath = $null,
        [psobject]$WorkerTimings = $null,
        [psobject]$Manifest = $null,
        [string]$ManifestPath = $null
    )
    Test-RequiredPath -PathToCheck $script:CurrentConfig.PythonExe -Label 'Python executable'
    Test-RequiredPath -PathToCheck $HelperScript -Label 'WhisperX helper script'

    $effectiveTranscriptionProfile = Resolve-EffectiveTranscriptionProfile
    $effectiveComputeType = Get-EffectiveComputeType -Profile $effectiveTranscriptionProfile
    $effectiveBatchSize = Get-EffectiveBatchSize -Profile $effectiveTranscriptionProfile
    $effectiveDiarizationEnabled = [bool]$script:CurrentConfig.DiarizationEnabled
    $diagModel = if ($effectiveDiarizationEnabled) { Get-EffectiveDiarizationModel } else { '' }
    $effectiveWhisperCppModelPath = Resolve-EffectiveWhisperCppModelPath -ConfiguredPath $script:CurrentConfig.WhisperCppModelPath -TranscriptionProfile $effectiveTranscriptionProfile
    $timingsJsonPath = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'phase_timings.json'
    $resolvedTxtPath = if ($TxtPath) { $TxtPath } else { Join-Path $SessionDir 'transcript.txt' }
    $resolvedJsonPath = if ($JsonPath) { $JsonPath } else { Join-Path $SessionDir 'transcript.json' }
    $resolvedSrtPath = if ($script:CurrentConfig.ExportSrt) { Get-SessionExportPath -SessionDir $SessionDir -FileName 'transcript.srt' } else { $null }
    $resolvedVttPath = if ($script:CurrentConfig.ExportVtt) { Get-SessionExportPath -SessionDir $SessionDir -FileName 'transcript.vtt' } else { $null }
    $initialPrompt = Get-CustomVocabularyPrompt
    $hardwareProfilePath = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'hardware_profile.json'
    try {
        $hardwareSnapshot = Get-WorkerHardwareSnapshot
        Set-Prop -Object $hardwareSnapshot -Name 'configured_transcription_profile' -Value $script:CurrentConfig.TranscriptionProfile
        Set-Prop -Object $hardwareSnapshot -Name 'effective_transcription_profile' -Value $effectiveTranscriptionProfile
        Set-Prop -Object $hardwareSnapshot -Name 'compute_type' -Value $effectiveComputeType
        Set-Prop -Object $hardwareSnapshot -Name 'batch_size' -Value $effectiveBatchSize
        $hardwareSnapshot | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $hardwareProfilePath -Encoding UTF8
    } catch { Write-DiagnosticsLog -SessionDir $SessionDir -Message "hardware_profile.json kon niet worden geschreven: $($_.Exception.Message)" }
    Write-DiagnosticsLog -SessionDir $SessionDir -Message "Resolved worker output targets: txt=$resolvedTxtPath json=$resolvedJsonPath srt=$resolvedSrtPath vtt=$resolvedVttPath model=$diagModel transcription_profile=$effectiveTranscriptionProfile compute=$effectiveComputeType batch=$effectiveBatchSize profile=$(Get-WorkerProfileSummary) whispercppExe=$($script:CurrentConfig.WhisperCppExe) whispercppModel=$effectiveWhisperCppModelPath min=$($script:CurrentConfig.MinSpeakers) max=$($script:CurrentConfig.MaxSpeakers)"
    $arguments = @(
        $HelperScript,
        '--input', $AudioPath,
        '--output-txt', $resolvedTxtPath,
        '--output-json', $resolvedJsonPath,
        '--language', $script:CurrentConfig.Language,
        '--model', $(if ($effectiveTranscriptionProfile -eq 'amd-gpu') { 'ffmpeg-whisper-gpu' } else { $script:CurrentConfig.Model }),
        '--batch-size', ([string]$effectiveBatchSize),
        '--compute-type', $effectiveComputeType,
        '--transcription-profile', $effectiveTranscriptionProfile,
        '--whispercpp-exe', $script:CurrentConfig.WhisperCppExe,
        '--whispercpp-model', $effectiveWhisperCppModelPath,
        '--ffmpeg-path', $script:CurrentConfig.FfmpegPath,
        '--enable-diarization', ($effectiveDiarizationEnabled.ToString().ToLowerInvariant()),
        '--diarization-model', $diagModel,
        '--diarization-acceleration', (Get-EffectiveDiarizationAcceleration),
        '--output-format', $script:CurrentConfig.TranscriptOutputFormat,
        '--timings-json', $timingsJsonPath
    )
    if ($ProgressJsonPath) { $arguments += @('--progress-json', $ProgressJsonPath) }
    if ($resolvedSrtPath) { $arguments += @('--output-srt', $resolvedSrtPath) }
    if ($resolvedVttPath) { $arguments += @('--output-vtt', $resolvedVttPath) }
    if ($initialPrompt) { $arguments += @('--initial-prompt', $initialPrompt) }
    if ($effectiveDiarizationEnabled -and $script:CurrentConfig.MinSpeakers -gt 0) { $arguments += @('--min-speakers', ([string]$script:CurrentConfig.MinSpeakers)) }
    if ($effectiveDiarizationEnabled -and $script:CurrentConfig.MaxSpeakers -gt 0) { $arguments += @('--max-speakers', ([string]$script:CurrentConfig.MaxSpeakers)) }

    $envVars = @{ PYANNOTE_METRICS_ENABLED = '0' }
    if ($HfToken) {
        $envVars['HF_TOKEN'] = $HfToken
        $envVars['HUGGINGFACE_TOKEN'] = $HfToken
    }
    if ($script:CurrentConfig.FfmpegPath) { $envVars['FFMPEG_PATH'] = $script:CurrentConfig.FfmpegPath }

    $stdoutPath = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'worker.stdout.log'
    $stderrPath = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'worker.stderr.log'
    $invocationPath = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'helper_invocation.json'
    $invocation = [pscustomobject]@{
        app_version = $(if ($Manifest.PSObject.Properties['app_version'] -and -not [string]::IsNullOrWhiteSpace([string]$Manifest.app_version)) { [string]$Manifest.app_version } else { $WorkerVersion })
        worker_version = $WorkerVersion
        worker_host = $env:COMPUTERNAME
        created_at = [datetimeoffset]::Now.ToString('o')
        python_exe = $script:CurrentConfig.PythonExe
        helper_script = $HelperScript
        arguments = $arguments
        working_directory = $WorkerRoot
        diarization_model = $diagModel
        diarization_acceleration = (Get-EffectiveDiarizationAcceleration)
        diarization_enabled = $effectiveDiarizationEnabled
        diarization_engine = $(if ($effectiveDiarizationEnabled) { 'pyannote' } else { 'none' })
        diarization_execution_model = $(if ($effectiveTranscriptionProfile -eq 'amd-gpu' -and $effectiveDiarizationEnabled) { 'postprocess-after-ffmpeg-whisper-gpu' } elseif ($effectiveDiarizationEnabled) { 'whisperx-pyannote' } else { 'disabled' })
        transcription_engine = $(if ($effectiveTranscriptionProfile -eq 'amd-gpu') { 'FFmpeg Whisper GPU' } else { 'WhisperX' })
        configured_transcription_profile = $script:CurrentConfig.TranscriptionProfile
        active_transcription_profile = $effectiveTranscriptionProfile
        transcription_profile_label = (Get-TranscriptionProfileDisplayName -Profile $effectiveTranscriptionProfile)
        whispercpp_exe = $script:CurrentConfig.WhisperCppExe
        whispercpp_model_path = $effectiveWhisperCppModelPath
        compute_type = $effectiveComputeType
        batch_size = $effectiveBatchSize
        transcript_output_format = $script:CurrentConfig.TranscriptOutputFormat
        hf_token_source = $(if ($HfToken) { 'process-env:HF_TOKEN' } else { 'not-used' })
        secret_arguments_redacted = @()
        transcription_timing_authority = 'manifest.phase_timings.transcription'
    }
    $invocation | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $invocationPath -Encoding UTF8

    $onStarted = {
        param($process)
        if ($ClaimPath) {
            Update-SessionClaim -SessionDir $SessionDir -Updates @{
                helper_pid = $process.Id
                helper_started_at = [datetimeoffset]::Now.ToString('o')
            } | Out-Null
        }
        if ($WorkerTimings) { Set-Prop -Object $WorkerTimings -Name 'helper_pid' -Value $process.Id }
        if ($Manifest -and $ManifestPath) {
            Set-Prop -Object $Manifest -Name 'last_heartbeat_at' -Value $null
            Save-Manifest -ManifestPath $ManifestPath -Manifest $Manifest
        }
        Write-DiagnosticsLog -SessionDir $SessionDir -Message "Python helper gestart met PID $($process.Id)."
    }

    $result = Invoke-ExternalProcess -FileName $script:CurrentConfig.PythonExe -Arguments $arguments -EnvironmentVariables $envVars -WorkingDirectory $WorkerRoot -StdoutPath $stdoutPath -StderrPath $stderrPath -TimeoutSec $TimeoutSec -OnTick $OnTick -OnStarted $onStarted
    if ($ClaimPath) {
        Update-SessionClaim -SessionDir $SessionDir -Updates @{
            helper_finished_at = [datetimeoffset]::Now.ToString('o')
        } | Out-Null
    }
    return $result
}

function Start-TranscriptSessionRunner {
    param([string]$SessionDir)
    $workerScriptPath = if ($PSCommandPath) { $PSCommandPath } elseif ($MyInvocation.MyCommand.Path) { $MyInvocation.MyCommand.Path } else { throw 'Kan worker-scriptpad niet bepalen.' }
    $runnerArgs = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $workerScriptPath,
        '-SettingsPath', $SettingsPath,
        '-ProcessSessionDir', $SessionDir
    )
    $runnerArgumentLine = (($runnerArgs | ForEach-Object { Quote-Arg ([string]$_) }) -join ' ')
    $runner = Start-Process -FilePath 'powershell.exe' -ArgumentList $runnerArgumentLine -WorkingDirectory $WorkerRoot -WindowStyle Hidden -PassThru
    Write-Log 'INFO' "Sessierunner gestart voor $SessionDir (PID $($runner.Id))."
    Write-DiagnosticsLog -SessionDir $SessionDir -Message "Sessierunner gestart (PID $($runner.Id))."
    Update-WorkerRuntimeStatus -State 'processing' -Message 'Sessierunner gestart' -ActiveSessionDir $SessionDir -MarkActivity
}

function Invoke-TranscriptForSession {
    param([string]$SessionDir)
    $decision = Get-SessionDecision -SessionDir $SessionDir
    if (-not $decision.Process) {
        Write-Log 'DEBUG' "Sessie overgeslagen: $SessionDir :: $($decision.Reason)"
        return
    }

    $manifest = $decision.Manifest
    $manifestPath = $decision.ManifestPath
    $audioPath = $decision.AudioPath
    $txtPath = $decision.TxtPath
    $jsonPath = $decision.JsonPath
    $failureMarker = Join-Path $SessionDir 'TRANSCRIPTION_FAILED'
    $successMarker = Join-Path $SessionDir 'TRANSCRIBED'

    $hfToken = $null

    Write-Log 'INFO' "Start transcriptie voor sessie: $SessionDir"
    Update-WorkerRuntimeStatus -State 'processing' -Message 'Sessie claimen' -ActiveSessionDir $SessionDir -MarkActivity
    Write-SessionLog -SessionDir $SessionDir -Message "Transcriptiewerker claimt sessie op basis van READY. Input: $audioPath"
    Write-DiagnosticsLog -SessionDir $SessionDir -Message "Worker-config: runtime=$($script:CurrentConfig.RuntimeMode) model=$($script:CurrentConfig.Model) diarization=$($script:CurrentConfig.DiarizationEnabled) mode=$(Get-EffectiveDiarizationMode) acceleration=$(Get-EffectiveDiarizationAcceleration) compute=$(Get-EffectiveComputeType) profile=$(Get-WorkerProfileSummary) preserveOutput=$($script:CurrentConfig.PreserveWorkerOutput) format=$($script:CurrentConfig.TranscriptOutputFormat)"

    $claimAcquired = $false
    try {
        $readyWrittenAt = $null
        if ($manifest.PSObject.Properties['phase_timings'] -and $manifest.phase_timings -and $manifest.phase_timings.recorder -and $manifest.phase_timings.recorder.ready_written_at) {
            try { $readyWrittenAt = [datetimeoffset]::Parse([string]$manifest.phase_timings.recorder.ready_written_at) } catch { }
        }

        $claimPath = Acquire-SessionClaim -SessionDir $SessionDir -Manifest $manifest
        $claimAcquired = $true
        $claimAcquiredAt = [datetimeoffset]::Now
        if (-not $manifest.PSObject.Properties['phase_timings']) { Set-Prop -Object $manifest -Name 'phase_timings' -Value ([pscustomobject]@{}) }
        $phaseTimings = Ensure-ObjectProp -Object $manifest -Name 'phase_timings'
        $null = Ensure-ObjectProp -Object $phaseTimings -Name 'recorder'
        $workerTimings = Ensure-ObjectProp -Object $phaseTimings -Name 'worker'
        Set-Prop -Object $workerTimings -Name 'claim_acquired_at' -Value $claimAcquiredAt.ToString('o')
        if ($readyWrittenAt) { Set-Prop -Object $workerTimings -Name 'ready_to_claim_ms' -Value ([math]::Round(($claimAcquiredAt - $readyWrittenAt).TotalMilliseconds, 3)) }
        Save-Manifest -ManifestPath $manifestPath -Manifest $manifest
        Claim-Session -Manifest $manifest -ManifestPath $manifestPath -AudioPath $audioPath -TxtPath $txtPath -JsonPath $jsonPath
        $sessionEffectiveTranscriptionProfile = Resolve-EffectiveTranscriptionProfile
        $sessionEffectiveDiarizationEnabled = [bool]$script:CurrentConfig.DiarizationEnabled
        if ($sessionEffectiveDiarizationEnabled) {
            $hfToken = Get-HfToken
            if ([string]::IsNullOrWhiteSpace($hfToken)) {
                throw 'Diarization is ingeschakeld maar er is geen Hugging Face token beschikbaar.'
            }
        }

        $workerProcessingMessage = if ($sessionEffectiveTranscriptionProfile -eq 'amd-gpu') { "Transcriberen via AMD GPU; diarization=$((Get-EffectiveDiarizationAcceleration))" } else { "Transcriberen en diarization uitvoeren; diarization=$((Get-EffectiveDiarizationAcceleration))" }
        Update-WorkerRuntimeStatus -State 'processing' -Message $workerProcessingMessage -ActiveSessionDir $SessionDir -MarkActivity
        if (Test-Path -LiteralPath $failureMarker) { Remove-Item -LiteralPath $failureMarker -Force -ErrorAction SilentlyContinue }

        $helperStartedAt = [datetimeoffset]::Now
        Set-Prop -Object $workerTimings -Name 'helper_started_at' -Value $helperStartedAt.ToString('o')
        $helperSoftTimeoutSec = Get-HelperTimeoutSeconds -Manifest $manifest
        $helperEmergencyTimeoutSec = Get-HelperEmergencyTimeoutSeconds -Manifest $manifest
        Set-Prop -Object $workerTimings -Name 'helper_timeout_sec' -Value $helperSoftTimeoutSec
        Set-Prop -Object $workerTimings -Name 'helper_emergency_timeout_sec' -Value $helperEmergencyTimeoutSec
        Set-Prop -Object $workerTimings -Name 'long_form_supervision' -Value (Test-IsLongFormSession -Manifest $manifest)
        $helperProgressPath = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'helper_progress.json'
        $stdoutPath = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'worker.stdout.log'
        $stderrPath = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'worker.stderr.log'
        try { if (Test-Path -LiteralPath $helperProgressPath) { Remove-Item -LiteralPath $helperProgressPath -Force -ErrorAction SilentlyContinue } } catch {}
        $script:CurrentHelperStallReason = $null
        $script:CurrentHelperHeartbeatSeen = $null
        $script:CurrentHelperPhaseBudgetWarningKey = $null
        $script:CurrentHelperHeartbeatWarningKey = $null
        $helperHeartbeatTimeoutSec = 180
        Set-Prop -Object $workerTimings -Name 'helper_heartbeat_timeout_sec' -Value $helperHeartbeatTimeoutSec
        Set-Prop -Object $workerTimings -Name 'stuck_phase' -Value $null
        Set-Prop -Object $workerTimings -Name 'stuck_after_sec' -Value $null
        Set-Prop -Object $workerTimings -Name 'helper_phase_budget_warning_phase' -Value $null
        Set-Prop -Object $workerTimings -Name 'helper_phase_budget_warning_at' -Value $null
        Save-Manifest -ManifestPath $manifestPath -Manifest $manifest
        $onHelperTick = {
            param($process, $elapsed)
            $now = [datetimeoffset]::Now
            $sync = Sync-HelperProgressToManifest -Manifest $manifest -ManifestPath $manifestPath -WorkerTimings $workerTimings -ProgressPath $helperProgressPath -LastHeartbeatSeen $script:CurrentHelperHeartbeatSeen
            $script:CurrentHelperHeartbeatSeen = $sync.LastHeartbeatSeen
            $progress = $sync.Progress
            $currentPhase = ''
            $phaseElapsedSec = 0.0
            $heartbeat = $null
            $lastActivityAt = $null
            $lastProgressEventAt = $null
            if ($null -ne $progress) {
                $currentPhase = if ($progress.PSObject.Properties['current_phase']) { [string]$progress.current_phase } else { '' }
                if ($progress.PSObject.Properties['phase_elapsed_sec'] -and $null -ne $progress.phase_elapsed_sec) {
                    try { $phaseElapsedSec = [double]$progress.phase_elapsed_sec } catch { $phaseElapsedSec = 0.0 }
                }
                elseif ($progress.PSObject.Properties['phase_started_at'] -and $progress.phase_started_at) {
                    $phaseStartedAt = Convert-ToDateTimeOffsetSafe $progress.phase_started_at
                    if ($phaseStartedAt) {
                        $phaseElapsedSec = [math]::Round((($now - $phaseStartedAt).TotalSeconds), 3)
                    }
                }
                if ($progress.PSObject.Properties['last_heartbeat_at']) { $heartbeat = Convert-ToDateTimeOffsetSafe $progress.last_heartbeat_at }
                if ($progress.PSObject.Properties['last_activity_at']) { $lastActivityAt = Convert-ToDateTimeOffsetSafe $progress.last_activity_at }
                if ($progress.PSObject.Properties['last_progress_event_at']) { $lastProgressEventAt = Convert-ToDateTimeOffsetSafe $progress.last_progress_event_at }
            }

            $latestLogActivityAt = Get-LatestFileActivityTimestamp -Paths @($stdoutPath, $stderrPath)
            if ($latestLogActivityAt) { Set-Prop -Object $workerTimings -Name 'helper_last_log_activity_at' -Value $latestLogActivityAt.ToString('o') }
            if ($lastActivityAt) { Set-Prop -Object $workerTimings -Name 'helper_last_activity_at' -Value $lastActivityAt.ToString('o') }
            if ($lastProgressEventAt) { Set-Prop -Object $workerTimings -Name 'helper_last_progress_event_at' -Value $lastProgressEventAt.ToString('o') }
            Set-Prop -Object $workerTimings -Name 'helper_phase_elapsed_sec' -Value $phaseElapsedSec

            $phaseBudgetSec = if (-not [string]::IsNullOrWhiteSpace($currentPhase)) { Get-HelperPhaseBudgetSeconds -PhaseName $currentPhase -Manifest $manifest } else { 0 }
            if ($phaseBudgetSec -gt 0) { Set-Prop -Object $workerTimings -Name 'helper_phase_budget_sec' -Value $phaseBudgetSec }

            $runtimeInfo = ''
            if ($null -ne $progress) {
                $runtimeDevice = if ($progress.PSObject.Properties['runtime_device']) { [string]$progress.runtime_device } else { '' }
                $runtimeCompute = if ($progress.PSObject.Properties['runtime_compute_type']) { [string]$progress.runtime_compute_type } else { '' }
                if (-not [string]::IsNullOrWhiteSpace($runtimeDevice)) {
                    $runtimeInfo = " - runtime: $runtimeDevice"
                    if (-not [string]::IsNullOrWhiteSpace($runtimeCompute)) { $runtimeInfo += "/$runtimeCompute" }
                    if ($runtimeDevice -eq 'cpu') { $runtimeInfo += " (WhisperX gebruikt CPU)" }
                }
            }

            $message = if (-not [string]::IsNullOrWhiteSpace($currentPhase)) {
                ("Helper actief: {0} ({1}s, soft-budget {2}s){3}" -f $currentPhase, [int][math]::Floor($phaseElapsedSec), $phaseBudgetSec, $runtimeInfo)
            }
            else {
                ("Helper actief: opstarten of fase-overgang ({0}s){1}" -f [int][math]::Floor($elapsed.TotalSeconds), $runtimeInfo)
            }
            Update-WorkerRuntimeStatus -State 'processing' -Message $message -ActiveSessionDir $SessionDir -MarkActivity

            if ($phaseBudgetSec -gt 0 -and $phaseElapsedSec -ge $phaseBudgetSec) {
                $warningKey = "{0}:{1}" -f $currentPhase, $phaseBudgetSec
                if ($script:CurrentHelperPhaseBudgetWarningKey -ne $warningKey) {
                    $script:CurrentHelperPhaseBudgetWarningKey = $warningKey
                    Set-Prop -Object $workerTimings -Name 'helper_phase_budget_warning_phase' -Value $currentPhase
                    Set-Prop -Object $workerTimings -Name 'helper_phase_budget_warning_at' -Value $now.ToString('o')
                    Set-Prop -Object $workerTimings -Name 'helper_phase_budget_warning_sec' -Value ([int][math]::Floor($phaseElapsedSec))
                    Write-DiagnosticsLog -SessionDir $SessionDir -Message ("Helperfase '{0}' overschrijdt soft-budget ({1}s >= {2}s). Doorlopen op warn-first beleid." -f $currentPhase, [int][math]::Floor($phaseElapsedSec), $phaseBudgetSec)
                    Save-Manifest -ManifestPath $manifestPath -Manifest $manifest
                }
            }

            if ($heartbeat) {
                $heartbeatSilentSec = [math]::Round(($now - $heartbeat).TotalSeconds, 3)
                Set-Prop -Object $workerTimings -Name 'helper_heartbeat_silent_sec' -Value $heartbeatSilentSec
                if ($heartbeatSilentSec -ge $helperHeartbeatTimeoutSec) {
                    $heartbeatWarningKey = "{0}:{1}" -f $currentPhase, $helperHeartbeatTimeoutSec
                    if ($script:CurrentHelperHeartbeatWarningKey -ne $heartbeatWarningKey) {
                        $script:CurrentHelperHeartbeatWarningKey = $heartbeatWarningKey
                        Write-DiagnosticsLog -SessionDir $SessionDir -Message ("Helper-heartbeat is stil sinds {0}s in fase '{1}'. Nog geen kill; wacht op extra stall-signalen." -f [int][math]::Floor($heartbeatSilentSec), $(if ([string]::IsNullOrWhiteSpace($currentPhase)) { 'unknown' } else { $currentPhase }))
                    }
                }
            }

            $meaningfulReference = if ($lastActivityAt) { $lastActivityAt } elseif ($lastProgressEventAt) { $lastProgressEventAt } else { $helperStartedAt }
            $meaningfulIdleSec = Get-HelperMeaningfulIdleSeconds -Manifest $manifest -PhaseName $currentPhase
            $noMeaningfulProgressSec = [math]::Round(($now - $meaningfulReference).TotalSeconds, 3)
            $noLogActivitySec = if ($latestLogActivityAt) { [math]::Round(($now - $latestLogActivityAt).TotalSeconds, 3) } else { [math]::Round($elapsed.TotalSeconds, 3) }
            $helperElapsedSec = [math]::Round($elapsed.TotalSeconds, 3)
            Set-Prop -Object $workerTimings -Name 'helper_elapsed_sec' -Value $helperElapsedSec
            Set-Prop -Object $workerTimings -Name 'helper_meaningful_idle_threshold_sec' -Value $meaningfulIdleSec
            Set-Prop -Object $workerTimings -Name 'helper_no_meaningful_progress_sec' -Value $noMeaningfulProgressSec
            Set-Prop -Object $workerTimings -Name 'helper_no_log_activity_sec' -Value $noLogActivitySec

            $softTimeoutExceeded = $helperElapsedSec -ge $helperSoftTimeoutSec
            if ($softTimeoutExceeded) {
                Set-Prop -Object $workerTimings -Name 'helper_soft_timeout_exceeded_at' -Value $now.ToString('o')
            }

            $noMeaningfulProgress = $noMeaningfulProgressSec -ge $meaningfulIdleSec
            $noLogActivity = $noLogActivitySec -ge $meaningfulIdleSec
            if ($softTimeoutExceeded -and $noMeaningfulProgress -and $noLogActivity) {
                $phaseName = if ([string]::IsNullOrWhiteSpace($currentPhase)) { 'unknown' } else { $currentPhase }
                $script:CurrentHelperStallReason = "WhisperX helper lijkt echt stilgevallen in fase '$phaseName': totale looptijd $([int][math]::Floor($helperElapsedSec))s overschrijdt soft-limiet $helperSoftTimeoutSec s, zonder bruikbare voortgang sinds $([int][math]::Floor($noMeaningfulProgressSec))s en zonder stdout/stderr-activiteit sinds $([int][math]::Floor($noLogActivitySec))s."
                Set-Prop -Object $workerTimings -Name 'stuck_phase' -Value $phaseName
                Set-Prop -Object $workerTimings -Name 'stuck_after_sec' -Value ([int][math]::Floor($helperElapsedSec))
                Set-Prop -Object $workerTimings -Name 'helper_state' -Value 'failed_stalled'
                Set-Prop -Object $workerTimings -Name 'helper_stall_evidence' -Value ([pscustomobject]@{
                    no_meaningful_progress_sec = [int][math]::Floor($noMeaningfulProgressSec)
                    no_log_activity_sec = [int][math]::Floor($noLogActivitySec)
                    soft_timeout_sec = $helperSoftTimeoutSec
                })
                Save-Manifest -ManifestPath $manifestPath -Manifest $manifest
                try { if (-not $process.HasExited) { $process.Kill() } } catch {}
                return
            }
        }
        $result = Invoke-PythonWorker -SessionDir $SessionDir -AudioPath $audioPath -TxtPath $txtPath -JsonPath $jsonPath -HfToken $hfToken -TimeoutSec $helperEmergencyTimeoutSec -ProgressJsonPath $helperProgressPath -OnTick $onHelperTick -ClaimPath $claimPath -WorkerTimings $workerTimings -Manifest $manifest -ManifestPath $manifestPath
        $finalSync = Sync-HelperProgressToManifest -Manifest $manifest -ManifestPath $manifestPath -WorkerTimings $workerTimings -ProgressPath $helperProgressPath -LastHeartbeatSeen $script:CurrentHelperHeartbeatSeen
        $script:CurrentHelperHeartbeatSeen = $finalSync.LastHeartbeatSeen
        $helperFinishedAt = [datetimeoffset]::Now
        Set-Prop -Object $workerTimings -Name 'helper_finished_at' -Value $helperFinishedAt.ToString('o')
        Set-Prop -Object $workerTimings -Name 'helper_duration_ms' -Value $result.DurationMs
        Set-Prop -Object $workerTimings -Name 'claim_to_helper_finished_ms' -Value ([math]::Round(($helperFinishedAt - $claimAcquiredAt).TotalMilliseconds, 3))
        Update-WorkerRuntimeStatus -State 'processing' -Message 'Resultaten valideren en exporteren' -ActiveSessionDir $SessionDir -MarkActivity
        if ($result.TimedOut) {
            if ($script:CurrentHelperStallReason) { throw $script:CurrentHelperStallReason }
            throw "WhisperX helper bereikte het noodplafond van $helperEmergencyTimeoutSec seconden. Controleer worker.stdout.log, worker.stderr.log en helper_progress.json."
        }

        $helperReportedCompleted = ($null -ne $finalSync.Progress -and [string]$finalSync.Progress.state -eq 'completed')
        $expectedOutputsPresent = (Test-ExpectedOutputsExist -Expected $decision.Expected)
        for ($outputCheckRetry = 1; -not $expectedOutputsPresent -and $outputCheckRetry -le 2; $outputCheckRetry++) {
            Start-Sleep -Seconds 2
            $expectedOutputsPresent = (Test-ExpectedOutputsExist -Expected $decision.Expected)
        }
        $helperExitCodeAvailable = if ($result.PSObject.Properties['ExitCodeAvailable']) { [bool]$result.ExitCodeAvailable } else { $null -ne $result.ExitCode }
        $helperExitCodeReadError = if ($result.PSObject.Properties['ExitCodeReadError']) { [string]$result.ExitCodeReadError } else { $null }
        $helperProcessHasExited = if ($result.PSObject.Properties['ProcessHasExited']) { [bool]$result.ProcessHasExited } else { $false }
        $helperExitCodeText = if ($result.PSObject.Properties['ExitCodeText'] -and -not [string]::IsNullOrWhiteSpace([string]$result.ExitCodeText)) {
            [string]$result.ExitCodeText
        }
        elseif ($helperExitCodeAvailable) {
            [string]$result.ExitCode
        }
        else {
            'unavailable'
        }
        $helperCompletionDecision = 'completed_direct'
        $helperCompletionReason = 'helper exitcode 0 en verplichte output aanwezig'
        Set-Prop -Object $workerTimings -Name 'helper_exit_code' -Value $result.ExitCode
        Set-Prop -Object $workerTimings -Name 'helper_exit_code_available' -Value $helperExitCodeAvailable
        Set-Prop -Object $workerTimings -Name 'helper_exit_code_text' -Value $helperExitCodeText
        Set-Prop -Object $workerTimings -Name 'helper_exit_code_read_error' -Value $helperExitCodeReadError
        Set-Prop -Object $workerTimings -Name 'helper_process_has_exited' -Value $helperProcessHasExited
        Set-Prop -Object $workerTimings -Name 'helper_reported_completed' -Value $helperReportedCompleted
        Set-Prop -Object $workerTimings -Name 'expected_outputs_present_after_helper' -Value $expectedOutputsPresent
        Set-Prop -Object $workerTimings -Name 'helper_exitcode_override_applied' -Value $false
        Set-Prop -Object $workerTimings -Name 'helper_exitcode_override_reason' -Value $null

        if ((-not $helperExitCodeAvailable) -or ($result.ExitCode -ne 0)) {
            if ($script:CurrentHelperStallReason) {
                $helperCompletionDecision = 'failed_stalled'
                $helperCompletionReason = $script:CurrentHelperStallReason
                Set-Prop -Object $workerTimings -Name 'helper_completion_decision' -Value $helperCompletionDecision
                Set-Prop -Object $workerTimings -Name 'helper_completion_reason' -Value $helperCompletionReason
                Write-DiagnosticsLog -SessionDir $SessionDir -Message ("Helper-evaluatie: exitcode={0}; reported_completed={1}; expected_outputs={2}; override_applied={3}; decision={4}; reason={5}" -f $helperExitCodeText, $helperReportedCompleted, $expectedOutputsPresent, $false, $helperCompletionDecision, $helperCompletionReason)
                throw $script:CurrentHelperStallReason
            }
            if ($helperReportedCompleted -and $expectedOutputsPresent) {
                $helperCompletionDecision = 'completed_override'
                $helperCompletionReason = if ($helperExitCodeAvailable) { 'helper_progress completed + verplichte output aanwezig' } elseif ([string]::IsNullOrWhiteSpace($helperExitCodeReadError)) { 'helper exitcode unavailable + helper_progress completed + verplichte output aanwezig' } else { "helper exitcode unavailable: $helperExitCodeReadError + helper_progress completed + verplichte output aanwezig" }
                Set-Prop -Object $workerTimings -Name 'helper_exitcode_override_applied' -Value $true
                Set-Prop -Object $workerTimings -Name 'helper_exitcode_override_reason' -Value $helperCompletionReason
                Write-DiagnosticsLog -SessionDir $SessionDir -Message ("Helper-evaluatie: exitcode={0}; reported_completed={1}; expected_outputs={2}; override_applied={3}; decision={4}; reason={5}" -f $helperExitCodeText, $helperReportedCompleted, $expectedOutputsPresent, $true, $helperCompletionDecision, $helperCompletionReason)
                Write-Log 'WARN' "Helper exitcode $helperExitCodeText genegeerd voor $SessionDir omdat helper_progress=completed en transcriptartefacten aanwezig zijn."
            }
            else {
                if ($helperExitCodeAvailable) {
                    $helperCompletionDecision = 'failed_exitcode'
                    $helperCompletionReason = "WhisperX helper exitcode $helperExitCodeText"
                }
                else {
                    $helperCompletionDecision = 'failed_exitcode_unavailable'
                    $helperCompletionReason = if ([string]::IsNullOrWhiteSpace($helperExitCodeReadError)) { 'WhisperX helper exitcode niet beschikbaar' } else { "WhisperX helper exitcode niet beschikbaar: $helperExitCodeReadError" }
                }
                Set-Prop -Object $workerTimings -Name 'helper_completion_decision' -Value $helperCompletionDecision
                Set-Prop -Object $workerTimings -Name 'helper_completion_reason' -Value $helperCompletionReason
                Write-DiagnosticsLog -SessionDir $SessionDir -Message ("Helper-evaluatie: exitcode={0}; reported_completed={1}; expected_outputs={2}; override_applied={3}; decision={4}; reason={5}" -f $helperExitCodeText, $helperReportedCompleted, $expectedOutputsPresent, $false, $helperCompletionDecision, $helperCompletionReason)
                $message = ($result.Stderr + " `n" + $result.Stdout).Trim()
                if ([string]::IsNullOrWhiteSpace($message)) { $message = $helperCompletionReason }
                throw $message
            }
        }

        if (-not $expectedOutputsPresent) {
            $missing = @()
            if ($decision.Expected.WantsTxt -and -not (Test-Path -LiteralPath $decision.Expected.TxtPath)) { $missing += $decision.Expected.TxtPath }
            if ($decision.Expected.WantsJson -and -not (Test-Path -LiteralPath $decision.Expected.JsonPath)) { $missing += $decision.Expected.JsonPath }
            $helperCompletionDecision = 'failed_missing_outputs'
            $missingText = if ($missing.Count -gt 0) { $missing -join '; ' } else { [string]$script:ExpectedOutputsFailureReason }
            $helperCompletionReason = ("Transcriptie-helper meldde succes maar verplichte output ontbreekt: {0}" -f $missingText)
            Set-Prop -Object $workerTimings -Name 'helper_completion_decision' -Value $helperCompletionDecision
            Set-Prop -Object $workerTimings -Name 'helper_completion_reason' -Value $helperCompletionReason
            Write-DiagnosticsLog -SessionDir $SessionDir -Message ("Helper-evaluatie: exitcode={0}; reported_completed={1}; expected_outputs={2}; override_applied={3}; decision={4}; reason={5}" -f $helperExitCodeText, $helperReportedCompleted, $expectedOutputsPresent, $false, $helperCompletionDecision, $helperCompletionReason)
            throw $helperCompletionReason
        }

        if ($helperCompletionDecision -eq 'completed_direct') {
            Write-DiagnosticsLog -SessionDir $SessionDir -Message ("Helper-evaluatie: exitcode={0}; reported_completed={1}; expected_outputs={2}; override_applied={3}; decision={4}; reason={5}" -f $helperExitCodeText, $helperReportedCompleted, $expectedOutputsPresent, $false, $helperCompletionDecision, $helperCompletionReason)
        }
        Set-Prop -Object $workerTimings -Name 'helper_completion_decision' -Value $helperCompletionDecision
        Set-Prop -Object $workerTimings -Name 'helper_completion_reason' -Value $helperCompletionReason

        Update-WorkerRuntimeStatus -State 'processing' -Message 'Transcriptbestanden verwerken' -ActiveSessionDir $SessionDir -MarkActivity
        $timingsPath = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'phase_timings.json'
        if (Test-Path -LiteralPath $timingsPath) {
            try {
                $helperTimingRaw = Get-Content -LiteralPath $timingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
                Set-Prop -Object $workerTimings -Name 'helper' -Value $helperTimingRaw
            }
            catch {
                Write-DiagnosticsLog -SessionDir $SessionDir -Message "phase_timings.json kon niet worden ingelezen: $($_.Exception.Message)"
            }
        }
        Set-Prop -Object $workerTimings -Name 'completed_at' -Value ([datetimeoffset]::Now.ToString('o'))
        Save-Manifest -ManifestPath $manifestPath -Manifest $manifest
        Complete-Session -Manifest $manifest -ManifestPath $manifestPath -TxtPath $txtPath -JsonPath $jsonPath
        $completionMessage = switch ($script:CurrentConfig.TranscriptOutputFormat) {
            'txt' { 'Transcriptie voltooid. Alleen transcript.txt is teruggeschreven in de sessiemap.' }
            'json' { 'Transcriptie voltooid. Alleen transcript.json is teruggeschreven in de sessiemap.' }
            default { 'Transcriptie voltooid. transcript.txt en transcript.json zijn teruggeschreven in de sessiemap.' }
        }
        Write-SessionLog -SessionDir $SessionDir -Message $completionMessage
        Write-DiagnosticsLog -SessionDir $SessionDir -Message 'Transcriptie voltooid.'
        if ($script:CurrentConfig.CreateMarkers) {
            $successMarkerValue = if ($manifest.PSObject.Properties['transcription_finished_at'] -and $manifest.transcription_finished_at) { [string]$manifest.transcription_finished_at } else { Get-Date -Format 's' }
            Set-Content -LiteralPath $successMarker -Value $successMarkerValue -Encoding UTF8
        }
        Write-Log 'INFO' "Transcriptie gereed: $SessionDir"
        Update-WorkerRuntimeStatus -State 'idle' -Message 'Laatste transcriptie succesvol afgerond' -MarkActivity
    }
    catch {
        $message = $_.Exception.Message
        if ($message -like 'Sessie kon niet atomair geclaimd worden*') {
            Write-Log 'INFO' "Sessie overgeslagen door gelijktijdige claim: $SessionDir"
            Write-DiagnosticsLog -SessionDir $SessionDir -Message "Sessierace gedetecteerd; huidige worker verwerkt deze sessie niet."
            return
        }

        Fail-Session -Manifest $manifest -ManifestPath $manifestPath -Message $message
        Write-SessionLog -SessionDir $SessionDir -Message "Transcriptie mislukt: $message"
        Write-DiagnosticsLog -SessionDir $SessionDir -Message "Transcriptie mislukt: $message"
        if ($script:CurrentConfig.CreateMarkers) { Set-Content -LiteralPath $failureMarker -Value $message -Encoding UTF8 }
        Write-Log 'ERROR' "Transcriptie mislukt voor $SessionDir :: $message"
        Update-WorkerRuntimeStatus -State 'warning' -Message "Transcriptie mislukt: $message" -ActiveSessionDir $SessionDir -MarkActivity
    }
    finally {
        if ($claimAcquired) {
            Remove-SessionClaim -SessionDir $SessionDir
        }
    }
}

function Stop-ReadyWatchers {
    try {
        foreach ($sourceId in @($script:WatcherSourceIds)) {
            try { Unregister-Event -SourceIdentifier $sourceId -ErrorAction SilentlyContinue } catch {}
        }
        try { Get-Event -ErrorAction SilentlyContinue | Remove-Event -ErrorAction SilentlyContinue } catch {}
        foreach ($watcher in @($script:WatcherList)) {
            try { $watcher.EnableRaisingEvents = $false } catch {}
            try { $watcher.Dispose() } catch {}
        }
    }
    finally {
        $script:WatcherList = @()
        $script:WatcherSourceIds = @()
    }
}

# Register-ObjectEvent zet de meldingen van de watcher in de eigen wachtrij van PowerShell. Een scriptblok als
# .NET-handler draait op een thread zonder PowerShell en liet het hele proces stoppen (exitcode 2) zodra er een
# READY-bestand verscheen terwijl de worker liep.
function Start-ReadyWatchers {
    param([string[]]$Roots)
    Stop-ReadyWatchers
    $index = 0
    foreach ($root in ($Roots | Sort-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root)) { continue }
        try {
            $watcher = New-Object System.IO.FileSystemWatcher
            $watcher.Path = $root
            $watcher.Filter = 'READY'
            $watcher.IncludeSubdirectories = $true
            $watcher.NotifyFilter = [System.IO.NotifyFilters]'FileName, LastWrite, CreationTime'
            foreach ($eventName in 'Created', 'Changed', 'Renamed') {
                $sourceId = "CaptivoxReady.$index.$eventName"
                Register-ObjectEvent -InputObject $watcher -EventName $eventName -SourceIdentifier $sourceId | Out-Null
                $script:WatcherSourceIds += $sourceId
            }
            $watcher.EnableRaisingEvents = $true
            $script:WatcherList += $watcher
            $index++
        }
        catch {
            Write-Log 'WARN' "READY watcher kon niet worden gestart voor $root :: $($_.Exception.Message)"
        }
    }
    Write-Log 'INFO' ("READY watchers actief voor: {0}" -f (($script:WatcherList | ForEach-Object { $_.Path }) -join '; '))
}

function Wait-ForReadySignalOrTimeout {
    param([int]$TimeoutSec)
    if (@($script:WatcherSourceIds).Count -eq 0) {
        Start-Sleep -Seconds $TimeoutSec
        return 'timeout'
    }
    $readyEvent = Wait-Event -Timeout $TimeoutSec
    if ($null -eq $readyEvent) { return 'timeout' }
    # Een scan pakt alle nieuwe opnames tegelijk op; de rest van de opgespaarde meldingen is dan overbodig.
    Get-Event -ErrorAction SilentlyContinue | Remove-Event -ErrorAction SilentlyContinue
    return 'signal'
}

function Emit-SelfTestSummary {
    param([bool]$Success, [hashtable]$Checks, [string[]]$Messages)
    $psInfo = Get-PowerShellHostInfo
    $payload = [ordered]@{
        success = $Success
        worker_version = $WorkerVersion
        runtime_mode = $script:CurrentConfig.RuntimeMode
        powershell_version = $psInfo.version
        powershell_edition = $psInfo.edition
        powershell_host_path = $psInfo.host_path
        checks = $Checks
        messages = $Messages
    } | ConvertTo-Json -Depth 10 -Compress
    Write-Output ("SELFTEST_JSON:{0}" -f $payload)
}

function Invoke-SelfTest {
    Refresh-WorkerConfig | Out-Null
    Write-Log 'INFO' 'Self-test gestart.'
    Write-Log 'INFO' "Settings: $SettingsPath"
    $psInfo = Get-PowerShellHostInfo
    Write-Log 'INFO' "PowerShell: version=$($psInfo.version) edition=$($psInfo.edition) host=$($psInfo.host_path)"
    Write-Log 'INFO' "Runtime mode: $($script:CurrentConfig.RuntimeMode) | model: $($script:CurrentConfig.Model) | diarization: $($script:CurrentConfig.DiarizationEnabled) | diagnostics: $($script:CurrentConfig.DiagnosticsMode)"

    $checks = [ordered]@{
        python_ok = $false
        helper_ok = $false
        ffmpeg_ok = $false
        token_ok = $false
        stable_runtime_ok = $true
        helper_self_test_ok = $false
        token_bridge_ok = $false
    }
    $messages = New-Object System.Collections.Generic.List[string]

    Test-RequiredPath -PathToCheck $script:CurrentConfig.PythonExe -Label 'Python executable'
    $checks.python_ok = $true
    Test-RequiredPath -PathToCheck $HelperScript -Label 'WhisperX helper script'
    $checks.helper_ok = $true
    if ($script:CurrentConfig.FfmpegPath -match '[\\/]') { Test-RequiredPath -PathToCheck $script:CurrentConfig.FfmpegPath -Label 'ffmpeg' } else { Test-CommandAvailable -CommandName $script:CurrentConfig.FfmpegPath -Label 'ffmpeg' }
    $checks.ffmpeg_ok = $true

    $selfTestEffectiveTranscriptionProfile = Resolve-EffectiveTranscriptionProfile
    $selfTestEffectiveDiarizationEnabled = [bool]$script:CurrentConfig.DiarizationEnabled
    if ($selfTestEffectiveDiarizationEnabled) {
        $helperExe = Resolve-TokenHelperExe
        if (-not $helperExe -and -not $env:HF_TOKEN) {
            throw 'Token bridge ontbreekt: CaptivoxRecorder.exe is niet gevonden naast de worker.'
        }

        $checks.token_bridge_ok = $true
        $hfToken = Get-HfToken
        if ([string]::IsNullOrWhiteSpace($hfToken)) {
            throw 'Geen Hugging Face token beschikbaar terwijl diarization aan staat.'
        }
        $checks.token_ok = $true
    }
    else {
        $checks.token_bridge_ok = $true
        $checks.token_ok = $true
        $hfToken = $null
    }

    $effectiveTranscriptionProfile = Resolve-EffectiveTranscriptionProfile
    $effectiveComputeType = Get-EffectiveComputeType -Profile $effectiveTranscriptionProfile
    $effectiveBatchSize = Get-EffectiveBatchSize -Profile $effectiveTranscriptionProfile
    $effectiveDiarizationEnabled = [bool]$script:CurrentConfig.DiarizationEnabled
    $diagModel = if ($effectiveDiarizationEnabled) { Get-EffectiveDiarizationModel } else { '' }
    $effectiveWhisperCppModelPath = Resolve-EffectiveWhisperCppModelPath -ConfiguredPath $script:CurrentConfig.WhisperCppModelPath -TranscriptionProfile $effectiveTranscriptionProfile
    $timingsJsonPath = Get-SessionDiagnosticsPath -SessionDir $SessionDir -FileName 'phase_timings.json'
    $arguments = @(
        $HelperScript,
        '--self-test',
        '--self-test-mode', 'quick',
        '--language', $script:CurrentConfig.Language,
        '--model', $(if ($effectiveTranscriptionProfile -eq 'amd-gpu') { 'ffmpeg-whisper-gpu' } else { $script:CurrentConfig.Model }),
        '--batch-size', ([string]$effectiveBatchSize),
        '--compute-type', $effectiveComputeType,
        '--transcription-profile', $effectiveTranscriptionProfile,
        '--whispercpp-exe', $script:CurrentConfig.WhisperCppExe,
        '--whispercpp-model', $effectiveWhisperCppModelPath,
        '--ffmpeg-path', $script:CurrentConfig.FfmpegPath,
        '--enable-diarization', ($effectiveDiarizationEnabled.ToString().ToLowerInvariant()),
        '--diarization-model', $diagModel,
        '--diarization-acceleration', (Get-EffectiveDiarizationAcceleration)
    )
    if ($effectiveDiarizationEnabled -and $script:CurrentConfig.MinSpeakers -gt 0) { $arguments += @('--min-speakers', ([string]$script:CurrentConfig.MinSpeakers)) }
    if ($effectiveDiarizationEnabled -and $script:CurrentConfig.MaxSpeakers -gt 0) { $arguments += @('--max-speakers', ([string]$script:CurrentConfig.MaxSpeakers)) }

    $envVars = @{ PYANNOTE_METRICS_ENABLED = '0' }
    if ($hfToken) {
        $envVars['HF_TOKEN'] = $hfToken
        $envVars['HUGGINGFACE_TOKEN'] = $hfToken
    }
    if ($script:CurrentConfig.FfmpegPath) { $envVars['FFMPEG_PATH'] = $script:CurrentConfig.FfmpegPath }

    $result = Invoke-ExternalProcess -FileName $script:CurrentConfig.PythonExe -Arguments $arguments -EnvironmentVariables $envVars -WorkingDirectory $WorkerRoot -StdoutPath $null -StderrPath $null
    if (-not [string]::IsNullOrWhiteSpace($result.Stdout)) { Write-Host $result.Stdout }
    if (-not [string]::IsNullOrWhiteSpace($result.Stderr)) { Write-Host $result.Stderr }
    if ($result.ExitCode -ne 0) {
        $messages.Add("Helper self-test faalde met exitcode $($result.ExitCode).") | Out-Null
        Emit-SelfTestSummary -Success $false -Checks $checks -Messages $messages
        throw "Self-test helper exitcode $($result.ExitCode)"
    }
    $checks.helper_self_test_ok = $true
    $messages.Add('Self-test geslaagd.') | Out-Null
    Emit-SelfTestSummary -Success $true -Checks $checks -Messages $messages
    Write-Log 'INFO' 'Self-test geslaagd.'
}

$script:ExitCode = 0
$script:RunLockBusy = $false
$script:RunLockAcquired = $false
try {
    if ($SelfTest) {
        Invoke-SelfTest
        exit 0
    }

    Refresh-WorkerConfig | Out-Null
    if (-not $script:CurrentConfig.WhisperXEnabled) {
        Write-Log 'INFO' 'WhisperX worker staat uit in instellingen. Geen sessies verwerkt.'
        Update-WorkerRuntimeStatus -State 'disabled' -Message 'WhisperX worker staat uit in instellingen.' -MarkActivity
        exit 0
    }

    if ($ProcessSessionDir) {
        Invoke-TranscriptForSession -SessionDir $ProcessSessionDir
        exit $script:ExitCode
    }

    if (-not (Acquire-RunLock)) { $script:RunLockBusy = $true; exit 0 }

    Write-Log 'INFO' 'READY-worker gestart.'
    Update-WorkerRuntimeStatus -State 'starting' -Message 'READY-worker gestart.' -MarkActivity
    Start-ReadyWatchers -Roots $script:CurrentConfig.SessionRoots
    $recoveredAtStartup = Invoke-InterruptedSessionRecovery -StaleSeconds 120
    if ($recoveredAtStartup -gt 0) {
        Update-WorkerRuntimeStatus -State 'starting' -Message ("READY-worker gestart; sessierecovery markeerde {0} onderbroken sessie(s)." -f $recoveredAtStartup) -MarkActivity
    }

    do {
        $previousRoots = @($script:CurrentConfig.SessionRoots)
        Refresh-WorkerConfig | Out-Null
        $previousRootsKey = ((@($previousRoots) | Sort-Object -Unique) -join '|')
        $currentRootsKey = ((@($script:CurrentConfig.SessionRoots) | Sort-Object -Unique) -join '|')
        if ($previousRootsKey -ne $currentRootsKey) {
            Start-ReadyWatchers -Roots $script:CurrentConfig.SessionRoots
        }

        if (-not $script:CurrentConfig.WhisperXEnabled) {
            Write-Log 'INFO' 'WhisperX worker is tijdens runtime uitgeschakeld. Deze cyclus verwerkt geen sessies.'
            Update-WorkerRuntimeStatus -State 'disabled' -Message 'WhisperX worker is tijdens runtime uitgeschakeld.'
            if (-not $RunOnce) {
                Start-Sleep -Seconds $script:CurrentConfig.PollIntervalSec
                continue
            }
            break
        }

        Write-Log 'INFO' ("Actieve sessieroots: {0}" -f ($script:CurrentConfig.SessionRoots -join '; '))
        Write-Log 'DEBUG' ("Worker config: exportSrt={0}; exportVtt={1}; watchFolderEnabled={2}" -f $script:CurrentConfig.ExportSrt, $script:CurrentConfig.ExportVtt, $script:CurrentConfig.ImportWatchFolderEnabled)
        Update-WorkerRuntimeStatus -State 'scanning' -Message 'Backfill-scan gestart.'
        $sessionDirs = Get-ReadySessionDirectories
        $launchedRunner = $false
        $skippedReasons = @{}
        foreach ($sessionDir in $sessionDirs) {
            try {
                $activeClaim = Get-ActiveClaimSnapshot
                if ($activeClaim) {
                    Write-Log 'DEBUG' "Nieuwe sessie niet gestart; actieve claim aanwezig voor $($activeClaim.session_dir)."
                    break
                }
                $decision = Get-SessionDecision -SessionDir $sessionDir
                if (-not $decision.Process) {
                    $reasonKey = if ([string]::IsNullOrWhiteSpace([string]$decision.Reason)) { 'geen reden' } else { [string]$decision.Reason }
                    if (-not $skippedReasons.ContainsKey($reasonKey)) { $skippedReasons[$reasonKey] = 0 }
                    $skippedReasons[$reasonKey] = [int]$skippedReasons[$reasonKey] + 1
                    continue
                }
                Start-TranscriptSessionRunner -SessionDir $sessionDir
                $launchedRunner = $true
                break
            }
            catch {
                $script:ExitCode = 1
                Write-Log 'ERROR' "Onverwachte fout in sessie $sessionDir :: $($_.Exception.Message)"
                Update-WorkerRuntimeStatus -State 'error' -Message $_.Exception.Message -ActiveSessionDir $sessionDir -MarkActivity
            }
        }
        if ($skippedReasons.Count -gt 0) {
            $skipSummary = ($skippedReasons.GetEnumerator() | Sort-Object Name | ForEach-Object { "{0}x {1}" -f $_.Value, $_.Name }) -join '; '
            Write-Log 'DEBUG' "Backfill-scan sloeg $($skippedReasons.Count) reden(en) over: $skipSummary"
        }

        if (-not $launchedRunner) {
            try {
                Invoke-WatchFolderImports
            }
            catch {
                Write-Log 'ERROR' "Watch folder ingest-cyclus overgeslagen na onverwachte fout :: $($_.Exception.Message)"
            }
        }

        if (Test-ShouldRunRetention) {
            Invoke-RetentionPolicy
            $script:LastRetentionRunUtc = [datetimeoffset]::UtcNow
        }

        Update-WorkerRuntimeStatus -State 'idle' -Message ('Scan afgerond. Kandidaatsessies in deze cyclus: ' + $sessionDirs.Count)
        if (-not $RunOnce) {
            $waitSeconds = $script:CurrentConfig.PollIntervalSec
            $pendingReadyDelay = Get-PendingReadyDelaySeconds
            if ($null -ne $pendingReadyDelay) {
                $waitSeconds = 1
            }
            $waitResult = Wait-ForReadySignalOrTimeout -TimeoutSec $waitSeconds
            if ($waitResult -eq 'signal') {
                Write-Log 'DEBUG' 'READY watcher signaleerde nieuwe of gewijzigde sessie; backfill-scan wordt direct opnieuw uitgevoerd.'
                Update-WorkerRuntimeStatus -State 'idle' -Message 'READY watcher signaleerde een wijziging; nieuwe scan volgt.'
            }
            elseif ($null -ne $pendingReadyDelay -and $waitSeconds -lt $script:CurrentConfig.PollIntervalSec) {
                Write-Log 'DEBUG' "READY-bestand nog te recent; versnelde hercontrole na $waitSeconds seconde(n)."
            }
        }
    } while (-not $RunOnce)
}
catch {
    $script:ExitCode = 1
    Write-Log 'ERROR' $_.Exception.Message
    # v8.37.1: zonder vergrendeling (zelftest, tweede monitor) de status van de draaiende worker niet overschrijven.
    if ($ProcessSessionDir -or $script:RunLockAcquired) {
        Update-WorkerRuntimeStatus -State 'error' -Message $_.Exception.Message -MarkActivity
    }
}
finally {
    Stop-ReadyWatchers
    # v8.37.1: alleen wie de vergrendeling had, geeft hem vrij en zet de status op 'gestopt'. Een worker die de vergrendeling niet
    # kreeg (een andere draait al), de zelftest en een uitgeschakelde worker raken de vergrendeling en status van de draaiende
    # worker niet aan (voorheen werd die op 'gestopt' gezet en zijn vergrendelingsbestand weggehaald).
    if ($ProcessSessionDir) {
        Write-Log 'INFO' "Sessierunner gestopt voor $ProcessSessionDir."
    }
    elseif ($script:RunLockAcquired) {
        Release-RunLock
        Update-WorkerRuntimeStatus -State 'stopped' -Message 'READY-worker gestopt.' -MarkActivity
        Write-Log 'INFO' 'READY-worker gestopt.'
    }
    elseif ($script:RunLockBusy) {
        Write-Log 'INFO' 'READY-worker niet gestart: een andere worker draait al.'
    }
    exit $script:ExitCode
}

