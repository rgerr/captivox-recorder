[CmdletBinding()]
param(
    [string]$OutputRoot = '',
    [switch]$SkipZip,
    [switch]$SkipInstaller
)

# Bouwt de release van Captivox Recorder: een zelfstandig CaptivoxRecorder.exe (geen .NET-installatie nodig),
# met de transcriptieworker, de hulpscripts en de documenten ernaast, en daarvan een zip en een
# installatieprogramma (Inno Setup 6), elk met een SHA-256.
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repoRoot 'dist' }

$project = Join-Path $repoRoot 'src\CaptivoxRecorder\CaptivoxRecorder.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Versie ontbreekt in CaptivoxRecorder.csproj.' }

$workerVersion = (Get-Content -LiteralPath (Join-Path $repoRoot 'workers\worker.version.txt') -TotalCount 1).Trim()
if ($workerVersion -ne "v$version") { throw "workers\worker.version.txt ($workerVersion) hoort v$version te zijn." }

$packageName = "CaptivoxRecorder-$version-win-x64"
$stage = Join-Path $OutputRoot $packageName
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Host "== Captivox Recorder $version bouwen" -ForegroundColor Cyan
& dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:GenerateDocumentationFile=false -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish mislukt (exitcode $LASTEXITCODE)." }

# Alleen wat de gebruiker nodig heeft; logboeken en Python-cache van een lokale testrun blijven achter.
$workersTarget = Join-Path $stage 'workers'
New-Item -ItemType Directory -Force -Path $workersTarget | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'workers') -File |
    Where-Object { $_.Extension -in '.ps1', '.py', '.txt' -and $_.Name -ne 'requirements.windows.realized.lock.txt' } |
    Copy-Item -Destination $workersTarget
Copy-Item -LiteralPath (Join-Path $repoRoot 'tools') -Destination (Join-Path $stage 'tools') -Recurse
foreach ($file in 'install-prereqs.ps1', 'LICENSE', 'README.md', 'PRIVACY.md', 'SECURITY.md', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md') {
    $source = Join-Path $repoRoot $file
    if (-not (Test-Path -LiteralPath $source)) { throw "Bestand voor de release ontbreekt: $file" }
    Copy-Item -LiteralPath $source -Destination $stage
}

Get-ChildItem -LiteralPath $stage -Recurse -Filter '*.pdb' | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
$exe = Join-Path $stage 'CaptivoxRecorder.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'CaptivoxRecorder.exe ontbreekt na het bouwen.' }
$exeVersion = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
if ($exeVersion -ne $version) { throw "CaptivoxRecorder.exe meldt versie $exeVersion in plaats van $version." }

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
    & $iscc /Q "/DAppVersion=$version" "/DSourceDir=$stage" "/O$OutputRoot" (Join-Path $repoRoot 'installer\CaptivoxRecorder.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup mislukt (exitcode $LASTEXITCODE)." }
    $setup = Join-Path $OutputRoot "CaptivoxRecorder-$version-setup.exe"
    $setupHash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$setup.sha256" -Value "$setupHash  CaptivoxRecorder-$version-setup.exe" -Encoding ascii
    Write-Host "Setup:   $setup" -ForegroundColor Green
    Write-Host "SHA-256: $setupHash" -ForegroundColor Green
}

Write-Host "Map:     $stage" -ForegroundColor Green
