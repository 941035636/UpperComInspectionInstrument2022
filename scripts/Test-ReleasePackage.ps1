[CmdletBinding()]
param(
    [string]$ArchivePath
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repositoryRoot "UpperComInspectionInstrument2022\UpperComInspectionInstrument2022.csproj"
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $ArchivePath = Join-Path $repositoryRoot "artifacts\release\UpperComInspectionInstrument2022-$version-win-x64.zip"
}
$ArchivePath = [System.IO.Path]::GetFullPath($ArchivePath)
if (-not (Test-Path -LiteralPath $ArchivePath)) { throw "Release archive was not found: $ArchivePath" }

$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$extractPath = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "package-smoke"))
$allowedPrefix = $artifactRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $extractPath.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to modify a path outside the artifact directory: $extractPath"
}
if (Test-Path -LiteralPath $extractPath) { Remove-Item -LiteralPath $extractPath -Recurse -Force }
New-Item -ItemType Directory -Path $extractPath -Force | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $extractPath)
$extractPrefix = $extractPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

$requiredFiles = @(
    "UpperComInspectionInstrument2022.exe",
    "coreclr.dll",
    "checksums.csv",
    "docs\UserManual.md",
    "docs\DataDictionary.md",
    "docs\InstallationAndUpgrade.md",
    "docs\BackupAndRestore.md"
)
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $extractPath $relativePath))) {
        throw "Required release file is missing: $relativePath"
    }
}

$checksumPath = Join-Path $extractPath "checksums.csv"
$checksumRows = Import-Csv -LiteralPath $checksumPath -Encoding UTF8
if ($checksumRows.Count -eq 0) { throw "The release checksum list is empty." }
foreach ($row in $checksumRows) {
    $filePath = [System.IO.Path]::GetFullPath((Join-Path $extractPath $row.RelativePath))
    if (-not $filePath.StartsWith($extractPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Checksum entry escapes the extracted package: $($row.RelativePath)"
    }
    if (-not (Test-Path -LiteralPath $filePath)) { throw "Checksum entry is missing: $($row.RelativePath)" }
    $item = Get-Item -LiteralPath $filePath
    if ($item.Length -ne [long]$row.SizeBytes) { throw "Size mismatch: $($row.RelativePath)" }
    $hash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
    if ($hash -ne $row.SHA256) { throw "SHA-256 mismatch: $($row.RelativePath)" }
}

$resultDirectory = Join-Path $repositoryRoot "TestResults\PackageSmoke"
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$resultPath = Join-Path $resultDirectory ("package-smoke-{0}.csv" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
$rows = @(
    [pscustomobject]@{ Time = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff"); Step = "Archive extraction"; Outcome = "Passed"; Details = $ArchivePath },
    [pscustomobject]@{ Time = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff"); Step = "Required files"; Outcome = "Passed"; Details = [string]::Join("; ", $requiredFiles) },
    [pscustomobject]@{ Time = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff"); Step = "Per-file SHA-256"; Outcome = "Passed"; Details = "$($checksumRows.Count) files verified" }
)
$csv = $rows | ConvertTo-Csv -NoTypeInformation
[System.IO.File]::WriteAllText(
    $resultPath,
    ([string]::Join([Environment]::NewLine, $csv) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($true))

Write-Host "Release package smoke test passed: $resultPath"
