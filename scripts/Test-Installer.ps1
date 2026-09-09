[CmdletBinding()]
param(
    [string]$InstallerPath
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repositoryRoot "UpperComInspectionInstrument2022\UpperComInspectionInstrument2022.csproj"
$projectXml = [xml](Get-Content -LiteralPath $projectPath -Raw)
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $repositoryRoot "artifacts\installer\UpperComInspectionInstrument2022-Setup-$version.exe"
}
$InstallerPath = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $InstallerPath)) { throw "Installer was not found: $InstallerPath" }

$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$smokeRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot "install-smoke"))
$installPath = [System.IO.Path]::GetFullPath((Join-Path $smokeRoot "application"))
$allowedPrefix = $artifactRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
foreach ($path in @($smokeRoot, $installPath)) {
    if (-not $path.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the artifact directory: $path"
    }
}

if (Test-Path -LiteralPath $smokeRoot) { Remove-Item -LiteralPath $smokeRoot -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $smokeRoot "protected-user-data") -Force | Out-Null
$sentinelPath = Join-Path $smokeRoot "protected-user-data\sentinel.txt"
[System.IO.File]::WriteAllText($sentinelPath, "must survive uninstall", [System.Text.UTF8Encoding]::new($true))

$results = [System.Collections.Generic.List[object]]::new()
function Add-Result([string]$Step, [string]$Outcome, [string]$Details) {
    $results.Add([pscustomobject]@{ Time = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff"); Step = $Step; Outcome = $Outcome; Details = $Details })
}
function Invoke-Setup {
    $process = Start-Process -FilePath $InstallerPath -ArgumentList @("/S", "/D=$installPath") -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw "Installer exited with code $($process.ExitCode)." }
}

Invoke-Setup
$applicationPath = Join-Path $installPath "UpperComInspectionInstrument2022.exe"
$runtimePath = Join-Path $installPath "coreclr.dll"
$manualPath = Join-Path $installPath "docs\UserManual.md"
if (-not (Test-Path -LiteralPath $applicationPath) -or -not (Test-Path -LiteralPath $runtimePath) -or -not (Test-Path -LiteralPath $manualPath)) {
    throw "Installed output is incomplete."
}
Add-Result "Fresh install" "Passed" "Application, self-contained runtime and user manual were installed."

$uninstallRegistryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\UpperComInspectionInstrument2022"
if (-not (Test-Path -LiteralPath $uninstallRegistryPath)) { throw "Uninstall registration is missing." }
Add-Result "Uninstall registration" "Passed" "Current-user uninstall entry exists."

Invoke-Setup
if (-not (Test-Path -LiteralPath $applicationPath)) { throw "Upgrade installation removed the application." }
Add-Result "In-place upgrade" "Passed" "A second install over the same directory completed successfully."

$uninstallerPath = Join-Path $installPath "uninstall.exe"
if (-not (Test-Path -LiteralPath $uninstallerPath)) { throw "Uninstaller was not found." }
$uninstallProcess = Start-Process -FilePath $uninstallerPath -ArgumentList "/S" -Wait -PassThru -WindowStyle Hidden
if ($uninstallProcess.ExitCode -ne 0) { throw "Uninstaller exited with code $($uninstallProcess.ExitCode)." }
$deadline = [DateTime]::UtcNow.AddSeconds(30)
while ((Test-Path -LiteralPath $installPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
if (Test-Path -LiteralPath $installPath) { throw "Install directory still exists after uninstall: $installPath" }
if (Test-Path -LiteralPath $uninstallRegistryPath) { throw "Uninstall registration still exists after uninstall." }
if (-not (Test-Path -LiteralPath $sentinelPath)) { throw "Uninstall removed data outside the program directory." }
Add-Result "Uninstall and data preservation" "Passed" "Program and uninstall entry were removed; external data sentinel was preserved."

$resultDirectory = Join-Path $repositoryRoot "TestResults\InstallerSmoke"
New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
$resultPath = Join-Path $resultDirectory ("installer-smoke-{0}.csv" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
$csv = $results | ConvertTo-Csv -NoTypeInformation
[System.IO.File]::WriteAllText(
    $resultPath,
    ([string]::Join([Environment]::NewLine, $csv) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($true))

Write-Host "Installer smoke test passed: $resultPath"
