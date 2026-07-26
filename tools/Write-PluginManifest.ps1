[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PluginDirectory
)

$ErrorActionPreference = "Stop"
$manifestName = "CUETools.PluginManifest.v1"
$architectureDirectories = @("mono", "win32", "x64")
$pluginRoot = [System.IO.Path]::GetFullPath($PluginDirectory)

if (-not [System.IO.Directory]::Exists($pluginRoot)) {
    throw "Plugin directory does not exist."
}

$rootAttributes = [System.IO.File]::GetAttributes($pluginRoot)
if (($rootAttributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Plugin directory must not be a reparse point."
}

$records = @{}

function Add-PluginRecord {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File,
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    if (($File.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Plugin candidates must not be reparse points."
    }
    if ($records.ContainsKey($RelativePath)) {
        throw "Duplicate plugin manifest path."
    }

    $stream = [System.IO.File]::Open(
        $File.FullName,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($stream)
        $hash = [System.BitConverter]::ToString($hashBytes).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
    $records.Add($RelativePath, $hash)
}

Get-ChildItem -LiteralPath $pluginRoot -File -Filter "*.dll" |
    ForEach-Object {
        Add-PluginRecord -File $_ -RelativePath $_.Name
    }

foreach ($architecture in $architectureDirectories) {
    $architecturePath = [System.IO.Path]::Combine($pluginRoot, $architecture)
    if (-not [System.IO.Directory]::Exists($architecturePath)) {
        continue
    }

    $attributes = [System.IO.File]::GetAttributes($architecturePath)
    if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Plugin architecture directories must not be reparse points."
    }

    Get-ChildItem -LiteralPath $architecturePath -File -Filter "*.dll" |
        ForEach-Object {
            Add-PluginRecord -File $_ -RelativePath ($architecture + "/" + $_.Name)
        }
}

if ($records.Count -eq 0) {
    throw "No runtime DLL candidates were found."
}

$relativePaths = [string[]]$records.Keys
[System.Array]::Sort($relativePaths, [System.StringComparer]::Ordinal)
$lines = New-Object System.Collections.Generic.List[string]
foreach ($relativePath in $relativePaths) {
    $lines.Add($records[$relativePath] + "`t" + $relativePath)
}

$manifestPath = [System.IO.Path]::Combine($pluginRoot, $manifestName)
$stagePath = [System.IO.Path]::Combine(
    $pluginRoot,
    "." + $manifestName + "." + [System.Guid]::NewGuid().ToString("N") + ".tmp")
$backupPath = [System.IO.Path]::Combine(
    $pluginRoot,
    "." + $manifestName + "." + [System.Guid]::NewGuid().ToString("N") + ".bak")

try {
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllLines($stagePath, $lines, $utf8WithoutBom)

    if ([System.IO.File]::Exists($manifestPath)) {
        $manifestAttributes = [System.IO.File]::GetAttributes($manifestPath)
        if (($manifestAttributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Existing plugin manifest must not be a reparse point."
        }
        [System.IO.File]::Replace($stagePath, $manifestPath, $backupPath)
        [System.IO.File]::Delete($backupPath)
    }
    else {
        [System.IO.File]::Move($stagePath, $manifestPath)
    }
}
finally {
    if ([System.IO.File]::Exists($stagePath)) {
        [System.IO.File]::Delete($stagePath)
    }
    if ([System.IO.File]::Exists($backupPath)) {
        [System.IO.File]::Delete($backupPath)
    }
}

Write-Output $manifestPath
