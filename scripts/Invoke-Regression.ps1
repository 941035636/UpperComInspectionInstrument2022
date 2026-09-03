[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$resultDirectory = Join-Path $repositoryRoot "TestResults\$runId"
$solutionPath = Join-Path $repositoryRoot "UpperComInspectionInstrument2022.sln"
$ruleProject = Join-Path $repositoryRoot "tmp\RuleCheck\RuleCheck.csproj"
$uiProject = Join-Path $repositoryRoot "tmp\UiFlowCheck\UiFlowCheck.csproj"
$records = [System.Collections.Generic.List[object]]::new()
$failed = $false

New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null

function Invoke-RegressionStep {
    param(
        [string]$Name,
        [string]$LogFileName,
        [scriptblock]$Action
    )

    $startedAt = Get-Date
    $outputPath = Join-Path $resultDirectory $LogFileName
    $exitCode = -1
    $status = "Failed"
    $message = ""
    try {
        $output = & $Action 2>&1
        $exitCode = $LASTEXITCODE
        $output | Set-Content -LiteralPath $outputPath -Encoding utf8
        $output | ForEach-Object { Write-Host $_ }
        if ($exitCode -eq 0) {
            $status = "Passed"
        }
        else {
            $message = "Process exit code: $exitCode"
        }
    }
    catch {
        $message = $_.Exception.Message
        $_ | Out-String | Set-Content -LiteralPath $outputPath -Encoding utf8
    }

    $finishedAt = Get-Date
    $records.Add([pscustomobject]@{
        Step = $Name
        Status = $status
        StartedAt = $startedAt.ToString("yyyy-MM-dd HH:mm:ss.fff")
        FinishedAt = $finishedAt.ToString("yyyy-MM-dd HH:mm:ss.fff")
        DurationSeconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
        ExitCode = $exitCode
        Message = $message
        OutputFile = $outputPath
    })
    return $status -eq "Passed"
}

Push-Location $repositoryRoot
try {
    if (-not (Invoke-RegressionStep "Solution build" "01-build.log" {
        dotnet build $solutionPath -c $Configuration --disable-build-servers
    })) { $failed = $true }

    if (-not $failed -and -not (Invoke-RegressionStep "Rule and integration checks" "02-rule-check.log" {
        dotnet run --project $ruleProject -c $Configuration --no-restore --no-build
    })) { $failed = $true }

    if (-not $failed -and -not (Invoke-RegressionStep "WPF UI flow checks" "03-ui-flow-check.log" {
        dotnet run --project $uiProject -c $Configuration --no-restore --no-build
    })) { $failed = $true }
}
finally {
    Pop-Location
    $summaryPath = Join-Path $resultDirectory "regression-results.csv"
    $csvLines = $records | ConvertTo-Csv -NoTypeInformation
    [System.IO.File]::WriteAllText(
        $summaryPath,
        ([string]::Join([Environment]::NewLine, $csvLines) + [Environment]::NewLine),
        [System.Text.UTF8Encoding]::new($true))
    Write-Host "Regression result: $summaryPath"
}

if ($failed) { exit 1 }
exit 0
