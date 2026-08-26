Set-StrictMode -Version 2.0

function Get-MutationCounts([string]$ReportPath) {
    $report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    $statuses = foreach ($file in $report.files.PSObject.Properties) {
        foreach ($mutant in $file.Value.mutants) { [string]$mutant.status }
    }
    $counts = [ordered]@{
        total = @($statuses).Count
        killed = @($statuses | Where-Object { $_ -ceq "Killed" }).Count
        survived = @($statuses | Where-Object { $_ -ceq "Survived" }).Count
        noCoverage = @($statuses | Where-Object { $_ -ceq "NoCoverage" }).Count
        timeout = @($statuses | Where-Object { $_ -ceq "Timeout" }).Count
        compileError = @($statuses | Where-Object { $_ -ceq "CompileError" }).Count
        ignored = @($statuses | Where-Object { $_ -ceq "Ignored" }).Count
    }
    $eligible = $counts.killed + $counts.survived + $counts.noCoverage + $counts.timeout
    # A timeout IS a detection: the mutant made the program hang, which the tests observed.
    # Stryker itself scores timeouts as killed. Counting them only in the denominator made the
    # gate timing-flaky - the same mutant flips Killed/Timeout run to run on a loaded machine,
    # and on 2026-08-26 that flipped verification-discovery between 91.09 and 81.19 with
    # identical code. Detected = killed + timeout keeps the score identical across the flip.
    $detected = $counts.killed + $counts.timeout
    $score = if ($eligible -eq 0) { 0.0 } else { 100.0 * $detected / $eligible }
    return [pscustomobject]@{
        Counts = [pscustomobject]$counts
        Eligible = $eligible
        Detected = $detected
        Score = [Math]::Round($score, 2)
    }
}

function Get-MutationFailureReason(
    [int]$ExitCode,
    [object]$Measurement,
    [double]$MinimumScore,
    [int]$MaximumNoCoverage) {
    if ($ExitCode -ne 0) {
        return "Stryker exited with code $ExitCode."
    }
    if ($Measurement.Eligible -eq 0 -or
        ($Measurement.Counts.killed + $Measurement.Counts.timeout) -eq 0) {
        return "The profile did not execute and detect any eligible mutant."
    }
    if ($Measurement.Score -lt $MinimumScore) {
        return "Mutation score $($Measurement.Score) is below the reviewed floor $MinimumScore."
    }
    if ($Measurement.Counts.noCoverage -gt $MaximumNoCoverage) {
        return "No-coverage mutant count $($Measurement.Counts.noCoverage) exceeds the reviewed ceiling $MaximumNoCoverage."
    }
    return ""
}

function Write-MutationFailurePacket(
    [string]$Path,
    [string]$ProfileId,
    [string]$Command,
    [int]$ExitCode,
    [string]$LogPath,
    [string]$ReportPath,
    [object]$Counts,
    [string]$Reason,
    [string]$Mode) {
    $packet = [ordered]@{
        schema = "cuetools.mutation-failure.v1"
        profile = $ProfileId
        reason = $Reason
        exitCode = $ExitCode
        command = $Command
        log = $LogPath
        report = $ReportPath
        counts = $Counts
        replay = ".\eng\mutation\Invoke-MutationTests.ps1 -Mode $Mode -Profile $ProfileId"
    }
    $packet | ConvertTo-Json -Depth 6 |
        Set-Content -LiteralPath $Path -Encoding UTF8
}

Export-ModuleMember -Function `
    Get-MutationCounts,Get-MutationFailureReason,Write-MutationFailurePacket
