[CmdletBinding()]
param(
    [switch]$SkipRegression
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repositoryRoot "UpperComInspectionInstrument2022\UpperComInspectionInstrument2022.csproj"
$regressionScript = Join-Path $PSScriptRoot "Invoke-Regression.ps1"
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "Project version is missing." }

$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\release"))
$packageName = "UpperComInspectionInstrument2022-$version-win-x64"
$publishOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot $packageName))
$zipPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "$packageName.zip"))

function Assert-ArtifactPath {
    param([string]$Path)
    $allowedPrefix = $artifactRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $Path.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the release artifact directory: $Path"
    }
}

Assert-ArtifactPath $publishOutput
Assert-ArtifactPath $zipPath

if (-not $SkipRegression) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $regressionScript -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Regression failed. Release publishing was stopped." }
}

New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
if (Test-Path -LiteralPath $publishOutput) {
    Remove-Item -LiteralPath $publishOutput -Recurse -Force
}
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

dotnet publish $projectPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishOutput
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$applicationPath = Join-Path $publishOutput "UpperComInspectionInstrument2022.exe"
$runtimePath = Join-Path $publishOutput "coreclr.dll"
if (-not (Test-Path -LiteralPath $applicationPath) -or -not (Test-Path -LiteralPath $runtimePath)) {
    throw "The self-contained output is incomplete. Application host or runtime is missing."
}

$sourceDocs = Join-Path $repositoryRoot "docs"
if (Test-Path -LiteralPath $sourceDocs) {
    Copy-Item -LiteralPath $sourceDocs -Destination (Join-Path $publishOutput "docs") -Recurse -Force
}

$checksumPath = Join-Path $publishOutput "checksums.csv"
$checksumRows = Get-ChildItem -LiteralPath $publishOutput -File -Recurse |
    Where-Object { $_.FullName -ne $checksumPath } |
    Sort-Object FullName |
    ForEach-Object {
        [pscustomobject]@{
            RelativePath = $_.FullName.Substring($publishOutput.Length + 1)
            SizeBytes = $_.Length
            SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }
$checksumCsv = $checksumRows | ConvertTo-Csv -NoTypeInformation
[System.IO.File]::WriteAllText(
    $checksumPath,
    ([string]::Join([Environment]::NewLine, $checksumCsv) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($true))

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishOutput,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Write-Host "Release directory: $publishOutput"
Write-Host "Release archive:   $zipPath"
Write-Host "Archive SHA256:    $zipHash"
