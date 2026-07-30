[CmdletBinding()]
param(
    [ValidateSet("all", "legacy", "modern")]
    [string]$Lane = "all",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ManifestPath,
    [string]$ResultsDirectory,
    [string]$FrameworkPathOverride,
    [switch]$NoRestore,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$vendorPreparation = Join-Path $PSScriptRoot "Prepare-VendorSources.ps1"
& $vendorPreparation -RepositoryRoot $repoRoot
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $PSScriptRoot "test-suites.json"
}
$ManifestPath = [IO.Path]::GetFullPath($ManifestPath)
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) "cuetools-test-results"
}
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)

if (-not (Test-Path -LiteralPath $ResultsDirectory)) {
    New-Item -ItemType Directory -Path $ResultsDirectory | Out-Null
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ([int]$manifest.schemaVersion -ne 1) {
    throw "Unsupported test manifest schema version '$($manifest.schemaVersion)'."
}

$suites = @($manifest.suites)
if ($Lane -ne "all") {
    $suites = @($suites | Where-Object { $_.lane -eq $Lane })
}
if ($suites.Count -eq 0) {
    throw "The test manifest selected zero suites for lane '$Lane'."
}

if ([string]::IsNullOrWhiteSpace($FrameworkPathOverride)) {
    $referenceRoot = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.netframework.referenceassemblies.net47"
    if (Test-Path -LiteralPath $referenceRoot) {
        $candidate = Get-ChildItem -LiteralPath $referenceRoot -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "build\.NETFramework\v4.7" } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) {
            $FrameworkPathOverride = $candidate
        }
    }
}

$summary = @()
$gateFailed = $false
foreach ($suite in $suites) {
    $projectPath = [IO.Path]::GetFullPath((Join-Path $repoRoot ([string]$suite.project)))
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Test project '$($suite.project)' does not exist."
    }

    $trxName = "$($suite.id).trx"
    $trxPath = Join-Path $ResultsDirectory $trxName
    if (Test-Path -LiteralPath $trxPath) {
        Remove-Item -LiteralPath $trxPath -Force
    }
    $arguments = @(
        "test",
        $projectPath,
        "--configuration", $Configuration,
        "--nologo",
        "--results-directory", $ResultsDirectory,
        "--logger", "trx;LogFileName=$trxName"
    )
    if ($NoRestore) { $arguments += "--no-restore" }
    if ($NoBuild) { $arguments += "--no-build" }
    if ($suite.framework -eq "net47" -and -not [string]::IsNullOrWhiteSpace($FrameworkPathOverride)) {
        $arguments += "-p:FrameworkPathOverride=$FrameworkPathOverride"
    }

    Write-Host ""
    Write-Host "=== $($suite.id): $($suite.project) ==="
    & dotnet @arguments
    $dotnetExit = $LASTEXITCODE

    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        throw "Suite '$($suite.id)' did not produce its required TRX result '$trxPath' (dotnet exit $dotnetExit)."
    }

    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ($null -eq $counters) {
        throw "Suite '$($suite.id)' produced a TRX file without result counters."
    }

    $total = [int]$counters.total
    $executed = [int]$counters.executed
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    # Current MSTest adapters include ignored tests in total but leave the TRX notExecuted
    # attribute at zero. Derive non-executed results from the stable total/executed counters so
    # expected skips are visible and cannot masquerade as a counter mismatch.
    $skipped = $total - $executed
    $otherExecuted = $executed - $passed - $failed
    $minimum = [int]$suite.minimumDiscovered
    $maximumSkipped = [int]$suite.maximumSkipped

    $reasons = @()
    if ($dotnetExit -ne 0) { $reasons += "dotnet test exit=$dotnetExit" }
    if ($total -lt $minimum) { $reasons += "discovered=$total below minimum=$minimum" }
    if ($failed -ne 0) { $reasons += "failed=$failed" }
    if ($otherExecuted -ne 0) { $reasons += "otherExecuted=$otherExecuted" }
    if ($skipped -lt 0) { $reasons += "executed=$executed exceeds total=$total" }
    if ($skipped -gt $maximumSkipped) { $reasons += "skipped=$skipped above maximum=$maximumSkipped" }

    $ok = $reasons.Count -eq 0
    if (-not $ok) { $gateFailed = $true }
    $summary += [pscustomobject]@{
        Suite = [string]$suite.id
        Discovered = $total
        Passed = $passed
        Failed = $failed
        Skipped = $skipped
        Gate = $(if ($ok) { "PASS" } else { "FAIL: " + ($reasons -join "; ") })
    }
}

if ($Lane -eq "all" -or $Lane -eq "legacy") {
    $probeArguments = @{
        Configuration = $Configuration
    }
    if ($NoBuild) { $probeArguments["NoBuild"] = $true }
    & (Join-Path $PSScriptRoot "Invoke-Net20ExceptionRelayProbe.ps1") @probeArguments
}

Write-Host ""
Write-Host "=== Test discovery gate ==="
$summary | Format-Table -AutoSize | Out-String | Write-Host
if ($gateFailed) {
    throw "One or more test suites failed the manifest count gate."
}
Write-Host "All $($summary.Count) selected suites passed explicit discovery and result-count assertions."
