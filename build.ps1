[CmdletBinding()]
param(
    [ValidateSet('Free')]
    [string]$Edition = 'Free',
    [string]$OutputRoot = '',
    [switch]$SkipZip,
    [switch]$SkipInstaller
)

# Bouwt een release van Captivox Recorder: een zelfstandig programma (geen .NET-installatie nodig), de transcriptieworker,
# de hulpscripts en de documenten, en daarvan een zip en een installatieprogramma (Inno Setup 6), elk met een SHA-256.
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repoRoot 'dist' }
$isPro = $Edition -eq 'Pro'

$identity = @{ Exe = 'CaptivoxRecorder.exe'; AppData = 'CaptivoxRecorderFree'; Package = 'CaptivoxRecorder'; Worker = 'captivox_transcription_worker.ps1'; Installer = 'CaptivoxRecorder.iss'; Docs = 'free' }
function Get-SharedScriptText([string]$Path) { return [System.IO.File]::ReadAllText($Path) }

$project = Join-Path $repoRoot 'src\CaptivoxRecorder\CaptivoxRecorder.csproj'
$version = (& dotnet msbuild $project -nologo "-p:Edition=$Edition" -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($version)) { throw 'Versie kon niet uit CaptivoxRecorder.csproj worden gelezen.' }
# De gratis smaak: workers\worker.version.txt in de repository hoort bij de versie in het projectbestand, zodat ook een
# recorder die vanuit de broncode draait zijn worker herkent.
if (-not $isPro) {
    $workerVersion = ([string](Get-Content -LiteralPath (Join-Path $repoRoot 'workers\worker.version.txt') -TotalCount 1)).Trim()
    if ($workerVersion -ne "v$version") { throw "workers\worker.version.txt ($workerVersion) hoort v$version te zijn." }
}

$packageName = "$($identity.Package)-$version-win-x64"
$stage = Join-Path $OutputRoot $packageName
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Host "== $Edition $version bouwen" -ForegroundColor Cyan
& dotnet publish $project -c Release -r win-x64 --self-contained true "-p:Edition=$Edition" `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:GenerateDocumentationFile=false -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish mislukt (exitcode $LASTEXITCODE)." }

# Worker: eigen scriptnaam per smaak, zodat de recorder alleen zijn eigen worker herkent en opruimt.
$workersSource = Join-Path $repoRoot 'workers'
$workersTarget = Join-Path $stage 'workers'
New-Item -ItemType Directory -Force -Path $workersTarget | Out-Null
[System.IO.File]::WriteAllText((Join-Path $workersTarget $identity.Worker), (Get-SharedScriptText (Join-Path $workersSource 'captivox_transcription_worker.ps1')), (New-Object System.Text.UTF8Encoding($true)))
[System.IO.File]::WriteAllText((Join-Path $workersTarget 'transcribe_with_whisperx_recorder.py'), (Get-SharedScriptText (Join-Path $workersSource 'transcribe_with_whisperx_recorder.py')), (New-Object System.Text.UTF8Encoding($false)))
$workerFiles = @('requirements.txt', 'requirements.windows.lock.txt')
foreach ($file in $workerFiles) {
    Copy-Item -LiteralPath (Join-Path $workersSource $file) -Destination $workersTarget
}
Set-Content -LiteralPath (Join-Path $workersTarget 'worker.version.txt') -Value "v$version" -Encoding ascii

$toolsTarget = Join-Path $stage 'tools'
New-Item -ItemType Directory -Force -Path $toolsTarget | Out-Null
$tools = @('Build-WhisperCppVulkanRuntime.ps1', 'Test-WhisperCppVulkanRuntime.ps1')
foreach ($file in $tools) { Copy-Item -LiteralPath (Join-Path $repoRoot "tools\$file") -Destination $toolsTarget }

# install-prereqs.ps1 krijgt de gegevensmap van deze smaak ingevuld: precies een regel '$script:ProductFolder = '...''.
$prereqs = Get-SharedScriptText (Join-Path $repoRoot 'install-prereqs.ps1')
$productFolderPattern = '(?m)^\$script:ProductFolder = ''[A-Za-z0-9]*''(?=\r?$)'
if ([regex]::Matches($prereqs, $productFolderPattern).Count -ne 1) { throw 'install-prereqs.ps1 hoort precies een regel $script:ProductFolder = ''...'' te hebben.' }
$prereqs = [regex]::Replace($prereqs, $productFolderPattern, ('$$script:ProductFolder = ''' + $identity.AppData + ''''))
if ($prereqs -notmatch ([regex]::Escape("`$script:ProductFolder = '$($identity.AppData)'"))) { throw 'De gegevensmap kon niet in install-prereqs.ps1 worden ingevuld.' }
[System.IO.File]::WriteAllText((Join-Path $stage 'install-prereqs.ps1'), $prereqs, (New-Object System.Text.UTF8Encoding($true)))

# De documenten van deze smaak (README, LICENSE, PRIVACY, ...).
Get-ChildItem -LiteralPath $repoRoot -File | Where-Object { $_.Extension -in '.md', '' } | Copy-Item -Destination $stage

Get-ChildItem -LiteralPath $stage -Recurse -Filter '*.pdb' | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
$exe = Join-Path $stage $identity.Exe
if (-not (Test-Path -LiteralPath $exe)) { throw "$($identity.Exe) ontbreekt na het bouwen." }
$exeVersion = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
if ($exeVersion -ne $version) { throw "$($identity.Exe) meldt versie $exeVersion in plaats van $version." }
if (-not $isPro -and (Get-ChildItem -LiteralPath $workersTarget -Filter '*pyannote*')) { throw 'De gratis versie hoort geen sprekerherkenning mee te krijgen.' }

if (-not $SkipZip) {
    $zip = Join-Path $OutputRoot "$packageName.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -LiteralPath $stage -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $packageName.zip" -Encoding ascii
    Write-Host "Zip:     $zip" -ForegroundColor Green
    Write-Host "SHA-256: $hash" -ForegroundColor Green
}

if (-not $SkipInstaller) {
    $iscc = @(
        (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue).Source,
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) niet gevonden. Installeer het, of bouw zonder installatieprogramma met -SkipInstaller.' }

    Write-Host "== Installatieprogramma bouwen" -ForegroundColor Cyan
    & $iscc /Q "/DAppVersion=$version" "/DSourceDir=$stage" "/O$OutputRoot" (Join-Path $repoRoot "installer\$($identity.Installer)")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup mislukt (exitcode $LASTEXITCODE)." }
    $setupName = "$($identity.Package)-$version-setup.exe"
    $setup = Join-Path $OutputRoot $setupName
    $setupHash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$setup.sha256" -Value "$setupHash  $setupName" -Encoding ascii
    Write-Host "Setup:   $setup" -ForegroundColor Green
    Write-Host "SHA-256: $setupHash" -ForegroundColor Green
}

Write-Host "Map:     $stage" -ForegroundColor Green
