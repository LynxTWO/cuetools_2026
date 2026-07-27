[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
} else {
    [IO.Path]::GetFullPath($RepositoryRoot)
}
$moduleLines = @(
    & git -C $root config `
        --file .gitmodules `
        --get-regexp "submodule\..*\.path")
if ($LASTEXITCODE -ne 0 -or $moduleLines.Count -eq 0) {
    throw "Unable to enumerate the repository submodules."
}

$checked = 0
foreach ($line in $moduleLines) {
    $parts = ([string]$line) -split "\s+", 2
    if ($parts.Count -ne 2) { continue }
    $relativePath = $parts[1].Trim()
    $submodulePath = [IO.Path]::GetFullPath(
        (Join-Path $root $relativePath))
    if (-not (Test-Path -LiteralPath $submodulePath -PathType Container)) {
        throw "Initialized submodule is missing: $relativePath"
    }
    $treeLine = @(& git -C $root ls-tree HEAD -- $relativePath)
    if ($LASTEXITCODE -ne 0 -or $treeLine.Count -ne 1 -or
        [string]$treeLine[0] -cnotmatch
            "^160000 commit (?<commit>[0-9a-f]{40})`t") {
        throw "Unable to resolve the pinned gitlink for $relativePath"
    }
    $pinnedCommit = $Matches["commit"]
    $head = (& git -C $submodulePath rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -cne $pinnedCommit) {
        throw "Submodule commit differs from its pinned gitlink: $relativePath"
    }
    $status = @(
        & git -C $submodulePath status `
            --porcelain=v2 `
            --untracked-files=all)
    if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
        throw "Submodule worktree is not clean: $relativePath"
    }
    $checked++
}
if ($checked -ne $moduleLines.Count) {
    throw "One or more submodule configuration rows could not be checked."
}

Write-Host "Vendor submodule cleanliness PASS: $checked pinned worktrees are clean."
