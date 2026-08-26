[CmdletBinding()]
param(
    [ValidateSet("Quick", "Full")]
    [string]$Mode = "Quick",
    [string[]]$Profile = @(),
    [string]$ResultsDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$mutationRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $mutationRoot "..\.."))
$manifestPath = Join-Path $mutationRoot "profiles.json"
$toolManifestPath = Join-Path $mutationRoot ".config\dotnet-tools.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Import-Module (Join-Path $mutationRoot "MutationHarness.psm1") -Force

& (Join-Path $mutationRoot "Test-MutationHarness.ps1")

$selected = @($manifest.profiles)
if ($Profile.Count -gt 0) {
    $unknown = @($Profile | Where-Object { $_ -notin @($manifest.profiles.id) })
    if ($unknown.Count -gt 0) {
        throw "Unknown mutation profile(s): $($unknown -join ', ')"
    }
    $selected = @($manifest.profiles | Where-Object { $_.id -in $Profile })
}

if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
    $ResultsDirectory = Join-Path $repoRoot "TestResults\Mutation\$Mode-$stamp"
}
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
if (Test-Path -LiteralPath $ResultsDirectory) {
    $existing = @(Get-ChildItem -LiteralPath $ResultsDirectory -Force)
    if ($existing.Count -gt 0) {
        throw "Refusing to reuse non-empty mutation results directory: $ResultsDirectory"
    }
} else {
    New-Item -ItemType Directory -Path $ResultsDirectory | Out-Null
}

& dotnet tool restore --tool-manifest $toolManifestPath --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$level = if ($Mode -ceq "Quick") { "Basic" } else { "Standard" }
$summaries = New-Object "Collections.Generic.List[object]"

foreach ($profileEntry in $selected) {
    $profileId = [string]$profileEntry.id
    $sourceProject = Join-Path $mutationRoot ([string]$profileEntry.sourceProject)
    $configPath = Join-Path $mutationRoot ([string]$profileEntry.config)
    $sourceDirectory = Split-Path -Parent $sourceProject
    $profileRoot = Join-Path $ResultsDirectory $profileId
    $reportRoot = Join-Path $profileRoot "report"
    New-Item -ItemType Directory -Path $profileRoot,$reportRoot | Out-Null
    $logPath = Join-Path $profileRoot "stryker.log"
    $configName = Split-Path -Leaf $configPath
    $minimum = if ($Mode -ceq "Quick") {
        [double]$profileEntry.minimumQuickScore
    } else {
        [double]$profileEntry.minimumFullScore
    }
    $maximumNoCoverage = if ($Mode -ceq "Quick") {
        [int]$profileEntry.maximumQuickNoCoverage
    } else {
        [int]$profileEntry.maximumFullNoCoverage
    }
    $arguments = @(
        "tool", "run", "dotnet-stryker",
        "--config-file", $configName,
        "--output", $reportRoot,
        "--mutation-level", $level,
        "--skip-version-check"
    )
    $displayCommand = "dotnet " + ($arguments -join " ")

    Push-Location $sourceDirectory
    try {
        & dotnet @arguments *> $logPath
        $exitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    $report = Get-ChildItem -LiteralPath $reportRoot -Recurse `
        -Filter "mutation-report.json" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $report) {
        $packet = Join-Path $profileRoot "failure-packet.json"
        Write-MutationFailurePacket $packet $profileId $displayCommand $exitCode `
            $logPath "" $null "Stryker did not produce a JSON mutation report." $Mode
        throw "Mutation profile $profileId produced no report. Failure packet: $packet"
    }

    $measurement = Get-MutationCounts $report.FullName
    $summary = [pscustomobject]@{
        id = $profileId
        risk = [string]$profileEntry.risk
        mode = $Mode
        mutationLevel = $level
        score = $measurement.Score
        minimumScore = $minimum
        maximumNoCoverage = $maximumNoCoverage
        eligible = $measurement.Eligible
        counts = $measurement.Counts
        report = $report.FullName
        log = $logPath
    }
    $summaries.Add($summary)

    $reason = Get-MutationFailureReason `
        $exitCode $measurement $minimum $maximumNoCoverage
    if ($reason.Length -gt 0) {
        $packet = Join-Path $profileRoot "failure-packet.json"
        Write-MutationFailurePacket $packet $profileId $displayCommand $exitCode `
            $logPath $report.FullName $measurement.Counts $reason $Mode
        throw "Mutation profile $profileId failed: $reason Failure packet: $packet"
    }

    Write-Host (("MUTATION PASS: profile={0}, level={1}, score={2:N2}, killed={3}, " +
        "timeout={4}, survived={5}, no-coverage={6}, report={7}") -f
        $profileId,$level,$measurement.Score,$measurement.Counts.killed,
        $measurement.Counts.timeout,$measurement.Counts.survived,
        $measurement.Counts.noCoverage,$report.FullName)
}

$summaryPath = Join-Path $ResultsDirectory "mutation-summary.json"
[ordered]@{
    schema = "cuetools.mutation-summary.v1"
    mode = $Mode
    toolVersion = [string]$manifest.toolVersion
    generatedUtc = [DateTime]::UtcNow.ToString("o")
    profiles = @($summaries.ToArray())
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host "Mutation suite PASS: profiles=$($summaries.Count), mode=$Mode, summary=$summaryPath"
