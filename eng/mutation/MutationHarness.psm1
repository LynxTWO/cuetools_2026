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
    $score = if ($eligible -eq 0) { 0.0 } else { 100.0 * $counts.killed / $eligible }
    return [pscustomobject]@{
        Counts = [pscustomobject]$counts
        Eligible = $eligible
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
    if ($Measurement.Eligible -eq 0 -or $Measurement.Counts.killed -eq 0) {
        return "The profile did not execute and kill any eligible mutant."
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
