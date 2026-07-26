[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$BaselinePath,
    [switch]$NoRestore,
    [switch]$UpdateBaseline
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    $BaselinePath = Join-Path $PSScriptRoot "warning-baseline.json"
}
$BaselinePath = [IO.Path]::GetFullPath($BaselinePath)
$baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
if ([int]$baseline.schemaVersion -ne 1) {
    throw "Unsupported warning baseline schema '$($baseline.schemaVersion)'."
}
if (@($baseline.builds).Count -eq 0) {
    throw "Warning baseline selects zero builds."
}

$warningLines = [Collections.Generic.List[string]]::new()
$buildFailed = $false
foreach ($build in @($baseline.builds)) {
    $project = [IO.Path]::GetFullPath((Join-Path $repoRoot ([string]$build.project)))
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Warning-gated project does not exist: $($build.project)"
    }
    $arguments = @("build", $project, "--configuration", $Configuration, "--nologo", "--no-incremental")
    if ($NoRestore) { $arguments += "--no-restore" }

    Write-Host ""
    Write-Host "=== Warning-gated build: $($build.id) ==="
    $output = @(& dotnet @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    foreach ($item in $output) {
        $line = [string]$item
        Write-Host $line
        if ($line -match ":\s*warning\s+[A-Za-z]+\d+\s*:") {
            $warningLines.Add($line)
        }
    }
    if ($exitCode -ne 0) { $buildFailed = $true }
}
if ($buildFailed) {
    throw "One or more warning-gated builds failed."
}

$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$fingerprints = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$warningPattern = '^(?<source>.*?)(?:\(\d+(?:,\d+){0,3}\))?\s*:\s*warning\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(?:\s+\[[^\]]+\])?$'
foreach ($line in $warningLines) {
    $match = [Text.RegularExpressions.Regex]::Match($line, $warningPattern)
    if (-not $match.Success) {
        throw "A warning line could not be normalized for the checked baseline: $line"
    }
    $source = $match.Groups["source"].Value.Trim()
    if ($source.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        $source = $source.Substring($repoPrefix.Length)
    }
    $source = $source.Replace("\", "/")
    $message = [Text.RegularExpressions.Regex]::Replace(
        $match.Groups["message"].Value.Trim(),
        "\s+",
        " ")
    $message = $message.Replace($repoRoot, "<repo>")
    $null = $fingerprints.Add(
        "$source|$($match.Groups["code"].Value.ToUpperInvariant())|$message")
}

$actual = [string[]]@($fingerprints)
[Array]::Sort($actual, [StringComparer]::Ordinal)

if ($UpdateBaseline) {
    $baseline.fingerprints = $actual
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText(
        $BaselinePath,
        (($baseline | ConvertTo-Json -Depth 8) + "`n"),
        $utf8)
    Write-Host "Updated warning baseline with $($actual.Count) distinct fingerprints from $($warningLines.Count) emitted warning lines."
    exit 0
}

$expected = [string[]]@($baseline.fingerprints)
[Array]::Sort($expected, [StringComparer]::Ordinal)
$newWarnings = @($actual | Where-Object { [Array]::BinarySearch($expected, $_, [StringComparer]::Ordinal) -lt 0 })
$resolvedWarnings = @($expected | Where-Object { [Array]::BinarySearch($actual, $_, [StringComparer]::Ordinal) -lt 0 })

Write-Host ""
Write-Host "=== Warning budget ==="
Write-Host "Emitted warning lines: $($warningLines.Count)"
Write-Host "Distinct current fingerprints: $($actual.Count)"
Write-Host "Checked baseline fingerprints: $($expected.Count)"
if ($resolvedWarnings.Count -gt 0) {
    Write-Host "Resolved since baseline ($($resolvedWarnings.Count)); baseline may be pruned:"
    $resolvedWarnings | ForEach-Object { Write-Host "  - $_" }
}
if ($newWarnings.Count -gt 0) {
    Write-Host "New warnings ($($newWarnings.Count)):"
    $newWarnings | ForEach-Object { Write-Host "  + $_" }
    throw "Warning budget failed: new warning fingerprints were emitted."
}
Write-Host "Warning budget PASS: no new warning fingerprints."
