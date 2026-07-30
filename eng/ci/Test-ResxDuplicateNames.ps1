[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$duplicateFiles = New-Object "Collections.Generic.List[string]"
$checkedFiles = 0

Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter "*.resx" |
    Where-Object {
        $_.FullName -notmatch "[\\/](bin|obj|packages|ThirdParty)[\\/]"
    } |
    ForEach-Object {
        $checkedFiles++
        [xml]$document = Get-Content -LiteralPath $_.FullName
        $names = @(
            $document.root.ChildNodes |
                Where-Object {
                    ($_.LocalName -eq "data" -or $_.LocalName -eq "metadata") -and
                    $_.GetAttribute("name")
                } |
                ForEach-Object { $_.GetAttribute("name") }
        )
        $duplicates = @(
            $names |
                Group-Object |
                Where-Object { $_.Count -gt 1 } |
                Select-Object -ExpandProperty Name
        )
        if ($duplicates.Count -gt 0) {
            $relativePath = $_.FullName.Substring($repoRoot.Length).TrimStart(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar)
            $duplicateFiles.Add(
                "$relativePath => " + [string]::Join(", ", $duplicates))
        }
    }

if ($duplicateFiles.Count -gt 0) {
    throw "Duplicate .resx names are rejected because resource compilers silently keep only one value:`n$([string]::Join("`n", $duplicateFiles))"
}

Write-Host "RESX duplicate-name checks passed: $checkedFiles"
