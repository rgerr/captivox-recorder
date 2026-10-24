[CmdletBinding()]
param(
    [string]$SourceRoot = '',
    # De runtime-map in de gegevensmap van de app (<gegevensmap>\runtime\whispercpp). Verplicht: install-prereqs.ps1 geeft
    # hem mee, zodat dit script nooit in de map van een andere Captivox-installatie schrijft.
    [string]$RuntimeRoot = '',
    [string]$ModelName = 'large-v3-turbo-q5_0',
    [string]$WhisperCppRef = 'v1.7.6',
    [string]$NativeBuildRoot = '',
    [switch]$Clean,
    [switch]$SkipModelDownload
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) {
    throw 'Geef -RuntimeRoot op: de map runtime\whispercpp in de gegevensmap van de app (install-prereqs.ps1 doet dat zelf).'
}
$RuntimeRoot = [System.IO.Path]::GetFullPath($RuntimeRoot)
if ($PSVersionTable.PSVersion.Major -ge 7) { $PSNativeCommandUseErrorActionPreference = $true }

function Require-Command([string]$Name, [string]$Hint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) { throw "Benodigd commando ontbreekt: $Name. $Hint" }
}

function Invoke-NativeChecked {
    param([Parameter(Mandatory = $true)][string]$FilePath,[string[]]$ArgumentList = @(),[string]$StepName = $FilePath)
    Write-Host ("> {0} {1}" -f $FilePath, ($ArgumentList -join ' ')) -ForegroundColor DarkGray
    & $FilePath @ArgumentList
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "$StepName faalde met exitcode $exitCode." }
}

function Test-DirectoryWritable([string]$PathToCheck) {
    try {
        New-Item -ItemType Directory -Force -Path $PathToCheck | Out-Null
        $probe = Join-Path $PathToCheck ([guid]::NewGuid().ToString('N') + '.tmp')
        [IO.File]::WriteAllText($probe, 'ok')
        Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
        return $true
    } catch { return $false }
}

function Get-ProjectRootFromScript {
    $current = Get-Item -LiteralPath $PSScriptRoot
    for ($i = 0; $i -lt 8 -and $null -ne $current; $i++) {
        if ((Test-Path -LiteralPath (Join-Path $current.FullName 'workers')) -and (Test-Path -LiteralPath (Join-Path $current.FullName 'src'))) { return $current.FullName }
        $current = $current.Parent
    }
    return (Split-Path -Parent $PSScriptRoot)
}

# De native build gebeurt in een korte map in de root van een schijf: de Vulkan-subbuild van whisper.cpp maakt diep
# geneste paden en MSBuild loopt vast boven ongeveer 240 tekens. De mapnaam hoort bij de gegevensmap van deze app
# (bijvoorbeeld cvxwc-recorderfree of cvxwc-pro), zodat de build nooit de bouwmap van een andere Captivox-installatie
# opruimt of hergebruikt.
function Get-NativeBuildFolderName {
    $productFolder = Split-Path -Leaf (Split-Path -Parent (Split-Path -Parent $RuntimeRoot))
    $tag = ($productFolder.ToLowerInvariant() -replace '^captivox', '') -replace '[^a-z0-9]', ''
    if ([string]::IsNullOrWhiteSpace($tag)) { $tag = 'app' }
    if ($tag.Length -gt 16) { $tag = $tag.Substring(0, 16) }
    return 'cvxwc-' + $tag
}

function Resolve-NativeBuildRoot([string]$RequestedRoot) {
    if (-not [string]::IsNullOrWhiteSpace($RequestedRoot)) {
        if (Test-DirectoryWritable -PathToCheck $RequestedRoot) { return (Resolve-Path -LiteralPath $RequestedRoot).Path }
        throw "Native buildroot is niet beschrijfbaar: $RequestedRoot"
    }
    $folderName = Get-NativeBuildFolderName
    $candidates = New-Object System.Collections.Generic.List[string]
    $projectRoot = Get-ProjectRootFromScript
    $projectDrive = [IO.Path]::GetPathRoot($projectRoot)
    if (-not [string]::IsNullOrWhiteSpace($projectDrive)) { $candidates.Add((Join-Path $projectDrive $folderName)) | Out-Null }
    if (-not [string]::IsNullOrWhiteSpace($env:SystemDrive)) { $candidates.Add((Join-Path ($env:SystemDrive + '\') $folderName)) | Out-Null }
    if (-not [string]::IsNullOrWhiteSpace($env:TEMP)) { $candidates.Add((Join-Path $env:TEMP $folderName)) | Out-Null }
    foreach ($candidate in $candidates | Select-Object -Unique) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        if (Test-DirectoryWritable -PathToCheck $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    throw "Geen korte, beschrijfbare native buildroot gevonden. Geef er een op met -NativeBuildRoot, bijvoorbeeld D:\$folderName."
}

function Assert-ShortPathBudget([string]$BuildDir) {
    $projected = Join-Path $BuildDir 'ggml\src\ggml-vulkan\vulkan-shaders-gen-prefix\src\vulkan-shaders-gen-build\CMakeFiles\4.3.2\VCTargetsPath.vcxproj'
    if ($projected.Length -ge 240) { throw "Native buildpad is nog te lang voor MSBuild/Vulkan-subbuild ($($projected.Length) tekens). Gebruik een kort pad met -NativeBuildRoot, bijvoorbeeld D:\$(Get-NativeBuildFolderName)." }
}

function Get-VsWherePath {
    $candidate = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $candidate) { return $candidate }
    $cmd = Get-Command vswhere.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

function Resolve-VisualStudioCppInstall {
    $vswhere = Get-VsWherePath
    if (-not $vswhere) { throw "Visual Studio locator ontbreekt. Installeer Visual Studio Build Tools met workload 'Desktop development with C++'." }
    $installPath = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath) | Select-Object -First 1
    $installationVersion = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationVersion) | Select-Object -First 1
    $displayName = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property displayName) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($installPath) -or -not (Test-Path -LiteralPath $installPath)) { throw "Geen Visual Studio C++ toolchain gevonden. Open Visual Studio Installer en installeer workload 'Desktop development with C++' inclusief MSVC x64/x86 build tools." }
    $msbuild = Get-ChildItem -LiteralPath $installPath -Recurse -Filter MSBuild.exe -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match 'MSBuild\\Current\\Bin\\MSBuild\.exe$' } | Select-Object -First 1
    if (-not $msbuild) { throw "Visual Studio C++ toolchain gevonden, maar MSBuild.exe niet. Installatiepad: $installPath" }
    $major = 0
    if (-not [string]::IsNullOrWhiteSpace($installationVersion)) { $null = [int]::TryParse(($installationVersion -split '\.')[0], [ref]$major) }
    return [pscustomobject]@{ InstallPath = $installPath; InstallationVersion = $installationVersion; DisplayName = $displayName; Major = $major; MSBuild = $msbuild.FullName }
}

function Get-CMakeVersionText {
    $lines = (& cmake --version) 2>$null
    return (($lines | Select-Object -First 1) -as [string])
}
function Get-CMakeGeneratorsText {
    return ((& cmake --help) 2>$null) -join "`n"
}

function Resolve-CMakeVisualStudioGenerator($VsInfo) {
    $help = Get-CMakeGeneratorsText
    $candidates = @()
    if ($VsInfo.Major -ge 18) { $candidates += [pscustomobject]@{ Name = 'Visual Studio 18 2026'; Platform = 'x64' } }
    if ($VsInfo.Major -ge 17) { $candidates += [pscustomobject]@{ Name = 'Visual Studio 17 2022'; Platform = 'x64' } }
    if ($VsInfo.Major -eq 0) { $candidates += [pscustomobject]@{ Name = 'Visual Studio 18 2026'; Platform = 'x64' }; $candidates += [pscustomobject]@{ Name = 'Visual Studio 17 2022'; Platform = 'x64' } }
    foreach ($candidate in $candidates) { if ($help -match [regex]::Escape($candidate.Name)) { return $candidate } }
    $cmakeVersion = Get-CMakeVersionText
    throw "Geen passende CMake Visual Studio-generator gevonden voor '$($VsInfo.DisplayName)' ($($VsInfo.InstallationVersion)). CMake meldt: $cmakeVersion."
}

function Resolve-VulkanSdk {
    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:VULKAN_SDK)) { $candidates.Add($env:VULKAN_SDK) | Out-Null }
    if (Test-Path -LiteralPath 'C:\VulkanSDK') { Get-ChildItem -LiteralPath 'C:\VulkanSDK' -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | ForEach-Object { $candidates.Add($_.FullName) | Out-Null } }
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $glslc = Join-Path $candidate 'Bin\glslc.exe'; $header = Join-Path $candidate 'Include\vulkan\vulkan.h'; $lib = Join-Path $candidate 'Lib\vulkan-1.lib'
        if ((Test-Path -LiteralPath $glslc) -and (Test-Path -LiteralPath $header) -and (Test-Path -LiteralPath $lib)) { $env:VULKAN_SDK = $candidate; $env:Path = (Join-Path $candidate 'Bin') + ';' + $env:Path; return [pscustomobject]@{ Root = $candidate; Glslc = $glslc; Header = $header; Lib = $lib } }
    }
    throw "Vulkan SDK ontbreekt of is niet volledig gevonden. Installeer: winget install -e --id KhronosGroup.VulkanSDK --source winget. Sluit daarna PowerShell volledig en open opnieuw."
}

function Get-ModelUrl([string]$Name) { $fileName = "ggml-${Name}.bin"; $encodedFileName = [System.Uri]::EscapeDataString($fileName); return "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/${encodedFileName}?download=true" }

Write-Host '== Captivox whisper.cpp Vulkan runtime build ==' -ForegroundColor Cyan
Write-Host "Doelruntime: $RuntimeRoot"
Write-Host "Model: $ModelName"
Require-Command git 'Installeer Git voor Windows: winget install --id Git.Git -e --source winget'
Require-Command cmake 'Installeer CMake: winget install --id Kitware.CMake -e --source winget'

$nativeRoot = Resolve-NativeBuildRoot -RequestedRoot $NativeBuildRoot
if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = Join-Path $nativeRoot 'src' }
$buildDir = Join-Path $nativeRoot 'b'
Assert-ShortPathBudget -BuildDir $buildDir
Write-Host "Native buildroot: $nativeRoot" -ForegroundColor Green
Write-Host "SourceRoot: $SourceRoot"
Write-Host "BuildDir: $buildDir"

$vs = Resolve-VisualStudioCppInstall
Write-Host "Visual Studio C++ toolchain: $($vs.InstallPath)" -ForegroundColor Green
Write-Host "Visual Studio versie: $($vs.DisplayName) $($vs.InstallationVersion)"
Write-Host "MSBuild: $($vs.MSBuild)"
$generator = Resolve-CMakeVisualStudioGenerator -VsInfo $vs
Write-Host "CMake-generator: $($generator.Name) $($generator.Platform)" -ForegroundColor Green
$vulkan = Resolve-VulkanSdk
Write-Host "Vulkan SDK: $($vulkan.Root)" -ForegroundColor Green

$parent = Split-Path -Parent $SourceRoot
New-Item -ItemType Directory -Force -Path $parent | Out-Null
if (-not (Test-Path -LiteralPath $SourceRoot)) {
    Invoke-NativeChecked -FilePath 'git' -ArgumentList @('clone', 'https://github.com/ggml-org/whisper.cpp.git', $SourceRoot) -StepName 'git clone whisper.cpp'
}
Push-Location $SourceRoot
try {
    Invoke-NativeChecked -FilePath 'git' -ArgumentList @('fetch', '--tags', '--force') -StepName 'git fetch whisper.cpp tags'
    Invoke-NativeChecked -FilePath 'git' -ArgumentList @('checkout', '--force', $WhisperCppRef) -StepName ("git checkout whisper.cpp " + $WhisperCppRef)
    Invoke-NativeChecked -FilePath 'git' -ArgumentList @('clean', '-xdf') -StepName 'git clean whisper.cpp'
}
finally { Pop-Location }

if ($Clean -and (Test-Path -LiteralPath $buildDir)) { Remove-Item -LiteralPath $buildDir -Recurse -Force }
if (Test-Path -LiteralPath $buildDir) { Remove-Item -LiteralPath $buildDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

Write-Host ("Configureer whisper.cpp met {0} {1} en GGML_VULKAN=ON..." -f $generator.Name, $generator.Platform) -ForegroundColor Cyan
$configureArgs = @('-S', $SourceRoot, '-B', $buildDir, '-G', $generator.Name, '-A', $generator.Platform, "-DCMAKE_GENERATOR_INSTANCE=$($vs.InstallPath)", '-DGGML_VULKAN=ON', '-DWHISPER_BUILD_TESTS=OFF')
Invoke-NativeChecked -FilePath 'cmake' -ArgumentList $configureArgs -StepName 'cmake configure whisper.cpp Vulkan'

Write-Host 'Bouw whisper-cli...' -ForegroundColor Cyan
try { Invoke-NativeChecked -FilePath 'cmake' -ArgumentList @('--build', $buildDir, '--config', 'Release', '--target', 'whisper-cli', '--parallel') -StepName 'cmake build whisper-cli' }
catch { Write-Warning "Build van target whisper-cli faalde. Volledige build wordt geprobeerd. Originele fout: $($_.Exception.Message)"; Invoke-NativeChecked -FilePath 'cmake' -ArgumentList @('--build', $buildDir, '--config', 'Release', '--parallel') -StepName 'cmake build whisper.cpp full' }

$exe = $null
foreach ($binaryName in @('whisper-cli.exe', 'whisper-main.exe', 'main.exe')) {
    $exe = Get-ChildItem -LiteralPath $buildDir -Recurse -File -Filter $binaryName -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match '\\Release\\|\\bin\\' } | Sort-Object FullName | Select-Object -First 1
    if ($exe) { break }
}
if (-not $exe) {
    $allCandidates = Get-ChildItem -LiteralPath $buildDir -Recurse -File -Include 'whisper-cli.exe', 'whisper-main.exe', 'main.exe' -ErrorAction SilentlyContinue
    $candidateList = ($allCandidates | ForEach-Object { $_.FullName }) -join "`n"
    throw "Geen whisper.cpp CLI-exe gevonden na build. Gevonden kandidaten:`n$candidateList"
}
New-Item -ItemType Directory -Force -Path $RuntimeRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $RuntimeRoot 'models') | Out-Null
Copy-Item -LiteralPath $exe.FullName -Destination (Join-Path $RuntimeRoot 'whisper-cli.exe') -Force
$dllCandidates = Get-ChildItem -LiteralPath $buildDir -Recurse -File -Include '*.dll' -ErrorAction SilentlyContinue
foreach ($dll in $dllCandidates) { Copy-Item -LiteralPath $dll.FullName -Destination $RuntimeRoot -Force -ErrorAction SilentlyContinue }
$modelPath = Join-Path $RuntimeRoot "models\ggml-$ModelName.bin"
if (-not $SkipModelDownload -and -not (Test-Path -LiteralPath $modelPath)) { $modelUrl = Get-ModelUrl -Name $ModelName; Write-Host "Download model: $modelUrl" -ForegroundColor Cyan; Invoke-WebRequest -Uri $modelUrl -OutFile $modelPath -UseBasicParsing }
elseif ($SkipModelDownload) { Write-Warning "Modeldownload overgeslagen. Controleer handmatig dat dit bestand bestaat: $modelPath" }
$manifest = [ordered]@{ version = 1;engine = 'whisper.cpp Vulkan'; source_ref = $WhisperCppRef; source_root = $SourceRoot; build_dir = $buildDir; native_build_root = $nativeRoot; runtime_root = $RuntimeRoot; whisper_cli = (Join-Path $RuntimeRoot 'whisper-cli.exe'); model_path = $modelPath; selected_model_path = $modelPath; model_name = $ModelName; generator = ("{0} {1}" -f $generator.Name, $generator.Platform); visual_studio = $vs.InstallPath; vulkan_sdk = $vulkan.Root; safe_vulkan_flags = @('--no-flash-attn','--beam-size','1','--best-of','1','--device','0'); created_at = (Get-Date).ToString('o') }
$manifestPath = Join-Path $RuntimeRoot 'runtime_manifest.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host ''
Write-Host 'whisper.cpp Vulkan-runtime staat klaar:' -ForegroundColor Green
Write-Host (Join-Path $RuntimeRoot 'whisper-cli.exe')
Write-Host $modelPath
Write-Host $manifestPath
Write-Host ''
Write-Host 'Voer nu uit vanuit de Captivox-projectroot:' -ForegroundColor Green
Write-Host ".\tools\Test-WhisperCppVulkanRuntime.ps1 -RuntimeRoot `"$RuntimeRoot`""
