[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$workflowRoot = Join-Path $repoRoot ".github\workflows"
$expected = [ordered]@{
    "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1" = 5
    "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68" = 4
    "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a" = 4
    "lukka/run-vcpkg@b1a0dd252f06b9e25b3c022a9a03bd7a427fb6a2" = 1
}

$uses = New-Object "Collections.Generic.List[string]"
foreach ($workflow in Get-ChildItem -LiteralPath $workflowRoot -File -Filter "*.yml") {
    $source = Get-Content -LiteralPath $workflow.FullName -Raw
    foreach ($match in [regex]::Matches(
        $source,
        "(?m)^\s*(?:-\s*)?uses:\s*([^\s#]+)")) {
        $uses.Add($match.Groups[1].Value)
    }
}

foreach ($use in $uses) {
    if ($use -notmatch "^[^@]+@[0-9a-f]{40}$") {
        throw "Workflow action is not pinned to an immutable commit: $use"
    }
    if (-not $expected.Contains($use)) {
        throw "Workflow action pin is not in the reviewed supported-runtime set: $use"
    }
}

foreach ($entry in $expected.GetEnumerator()) {
    $actual = @($uses | Where-Object { $_ -ceq $entry.Key }).Count
    if ($actual -ne $entry.Value) {
        throw (
            "Workflow action pin count drifted for {0}: expected {1}, found {2}." -f
            $entry.Key,
            $entry.Value,
            $actual)
    }
}

Write-Host "Workflow action pin checks passed: $($uses.Count)"
