[CmdletBinding()]
param(
    [switch]$SkipRegression,
    [switch]$SkipPublish,
    [string]$NsisCompilerPath
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repositoryRoot "UpperComInspectionInstrument2022\UpperComInspectionInstrument2022.csproj"
$publishScript = Join-Path $PSScriptRoot "Publish-Release.ps1"
$installerScript = Join-Path $repositoryRoot "installer\UpperComInspectionInstrument2022.nsi"
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "Project version is missing." }

if (-not $SkipPublish) {
    & $publishScript -SkipRegression:$SkipRegression
    if ($LASTEXITCODE -ne 0) { throw "Release publishing failed with exit code $LASTEXITCODE." }
}

$packageName = "UpperComInspectionInstrument2022-$version-win-x64"
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\release\$packageName"))
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory "UpperComInspectionInstrument2022.exe"))) {
    throw "Published application was not found: $publishDirectory"
}

if ([string]::IsNullOrWhiteSpace($NsisCompilerPath)) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\NSIS-3.12\makensis.exe"),
        "C:\Program Files (x86)\NSIS\makensis.exe",
        "C:\Program Files\NSIS\makensis.exe"
    )
    $NsisCompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($NsisCompilerPath) -or -not (Test-Path -LiteralPath $NsisCompilerPath)) {
    throw "NSIS compiler was not found. Install NSIS 3.12 or pass -NsisCompilerPath."
}

$outputDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\installer"))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$installerPath = Join-Path $outputDirectory "UpperComInspectionInstrument2022-Setup-$version.exe"
if (Test-Path -LiteralPath $installerPath) { Remove-Item -LiteralPath $installerPath -Force }

& $NsisCompilerPath "/INPUTCHARSET" "UTF8" "/DAPP_VERSION=$version" "/DPUBLISH_DIR=$publishDirectory" "/DOUTPUT_DIR=$outputDirectory" $installerScript
if ($LASTEXITCODE -ne 0) { throw "NSIS compilation failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $installerPath)) { throw "Installer output was not created: $installerPath" }

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
$rows = @(
    '"FileName","SizeBytes","SHA256"',
    ('"{0}","{1}","{2}"' -f (Split-Path $installerPath -Leaf), (Get-Item -LiteralPath $installerPath).Length, $hash)
)
[System.IO.File]::WriteAllText(
    (Join-Path $outputDirectory "checksums.csv"),
    ([string]::Join([Environment]::NewLine, $rows) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($true))

Write-Host "Installer: $installerPath"
Write-Host "SHA256:   $hash"
