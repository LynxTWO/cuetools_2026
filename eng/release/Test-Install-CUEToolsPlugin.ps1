[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$script:checkCount = 0
function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )
    if (-not $Condition) {
        throw $Message
    }
    $script:checkCount++
}

function Assert-Equal {
    param(
        $Expected,
        $Actual,
        [string]$Message
    )
    if (-not [object]::Equals($Expected, $Actual)) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
    $script:checkCount++
}

function Assert-Throws {
    param(
        [scriptblock]$Action,
        [string]$MessagePattern,
        [string]$Message
    )

    $exceptionMessage = $null
    try {
        & $Action
    }
    catch {
        $exceptionMessage = $_.Exception.Message
    }
    if ($null -eq $exceptionMessage) {
        throw "$Message Expected an exception."
    }
    if ($exceptionMessage -notmatch $MessagePattern) {
        throw "$Message Unexpected exception: $exceptionMessage"
    }
    $script:checkCount++
}

function Write-TestBytes {
    param(
        [string]$Path,
        [byte[]]$Bytes
    )
    [IO.File]::WriteAllBytes($Path, $Bytes)
}

function Get-Sha256 {
    param([string]$Path)
    $stream = [IO.File]::OpenRead($Path)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString(
            $sha256.ComputeHash($stream)).Replace("-", "")
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

$installer = Join-Path $PSScriptRoot "Install-CUEToolsPlugin.ps1"
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = [IO.Path]::Combine(
    $tempBase,
    "cuetools-plugin-install-" + [Guid]::NewGuid().ToString("N"))
$reparseEntries = New-Object "Collections.Generic.List[string]"
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null

try {
    $packageOne = Join-Path $tempRoot "package-one"
    $destinationRoot = Join-Path $tempRoot "user-data"
    $packageX64 = Join-Path $packageOne "x64"
    [IO.Directory]::CreateDirectory($packageX64) | Out-Null
    Write-TestBytes `
        -Path (Join-Path $packageOne "CUETools.Sample.dll") `
        -Bytes ([byte[]](1, 2, 3, 4))
    Write-TestBytes `
        -Path (Join-Path $packageOne "Dependency.dll") `
        -Bytes ([byte[]](5, 6, 7))
    Write-TestBytes `
        -Path (Join-Path $packageX64 "native.dll") `
        -Bytes ([byte[]](8, 9))

    $firstResult = & $installer `
        -PackageDirectory $packageOne `
        -DestinationRoot $destinationRoot
    $installed = Join-Path $destinationRoot "plugins"
    Assert-Equal `
        ([IO.Path]::GetFullPath($installed)) `
        $firstResult.PluginDirectory `
        "The installer reported the wrong destination."
    Assert-True `
        $firstResult.RestartRequired `
        "The installer did not report the restart requirement."
    Assert-True `
        ($null -eq $firstResult.BackupDirectory) `
        "A first install unexpectedly reported a backup."

    $manifestPath = Join-Path $installed "CUETools.PluginManifest.v1"
    $manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
    Assert-True `
        (-not ($manifestBytes.Length -ge 3 -and
            $manifestBytes[0] -eq 0xef -and
            $manifestBytes[1] -eq 0xbb -and
            $manifestBytes[2] -eq 0xbf)) `
        "The plugin manifest contains a UTF-8 BOM."
    $manifestLines = [IO.File]::ReadAllLines($manifestPath)
    Assert-Equal 3 $manifestLines.Length "The manifest entry count is wrong."
    $manifestPaths = @(
        $manifestLines |
            ForEach-Object { ($_ -split "`t", 2)[1] })
    $expectedPaths = @(
        "CUETools.Sample.dll",
        "Dependency.dll",
        "x64/native.dll")
    Assert-Equal `
        ($expectedPaths -join "|") `
        ($manifestPaths -join "|") `
        "The manifest paths are not in strict ordinal order."
    foreach ($line in $manifestLines) {
        $fields = $line -split "`t", 2
        $filePath = Join-Path $installed (
            $fields[1].Replace("/", [IO.Path]::DirectorySeparatorChar))
        Assert-Equal `
            (Get-Sha256 -Path $filePath) `
            $fields[0] `
            "A plugin manifest hash is incorrect."
    }

    $installedHashBeforeRefusal = Get-Sha256 `
        -Path (Join-Path $installed "CUETools.Sample.dll")
    Assert-Throws `
        {
            & $installer `
                -PackageDirectory $packageOne `
                -DestinationRoot $destinationRoot
        } `
        "already exists" `
        "An existing user plugin set was replaced without -Replace."
    Assert-Equal `
        $installedHashBeforeRefusal `
        (Get-Sha256 -Path (Join-Path $installed "CUETools.Sample.dll")) `
        "Refusing replacement changed the installed plugin."

    $badFilePackage = Join-Path $tempRoot "bad-file-package"
    [IO.Directory]::CreateDirectory($badFilePackage) | Out-Null
    Write-TestBytes `
        -Path (Join-Path $badFilePackage "CUETools.Sample.dll") `
        -Bytes ([byte[]](1))
    [IO.File]::WriteAllText((Join-Path $badFilePackage "readme.txt"), "not allowed")
    Assert-Throws `
        {
            & $installer `
                -PackageDirectory $badFilePackage `
                -DestinationRoot (Join-Path $tempRoot "bad-file-destination")
        } `
        "DLL" `
        "A non-DLL package file was accepted."

    $badDirectoryPackage = Join-Path $tempRoot "bad-directory-package"
    [IO.Directory]::CreateDirectory(
        (Join-Path $badDirectoryPackage "assets")) | Out-Null
    Write-TestBytes `
        -Path (Join-Path $badDirectoryPackage "CUETools.Sample.dll") `
        -Bytes ([byte[]](1))
    Assert-Throws `
        {
            & $installer `
                -PackageDirectory $badDirectoryPackage `
                -DestinationRoot (Join-Path $tempRoot "bad-directory-destination")
        } `
        "Unknown plugin package directory" `
        "An unknown package directory was accepted."

    $noEntryPointPackage = Join-Path $tempRoot "no-entry-point-package"
    [IO.Directory]::CreateDirectory($noEntryPointPackage) | Out-Null
    Write-TestBytes `
        -Path (Join-Path $noEntryPointPackage "Dependency.dll") `
        -Bytes ([byte[]](1))
    Assert-Throws `
        {
            & $installer `
                -PackageDirectory $noEntryPointPackage `
                -DestinationRoot (Join-Path $tempRoot "no-entry-point-destination")
        } `
        "no CUETools" `
        "A dependency-only package was accepted."

    $emptyPackage = Join-Path $tempRoot "empty-dll-package"
    [IO.Directory]::CreateDirectory($emptyPackage) | Out-Null
    Write-TestBytes `
        -Path (Join-Path $emptyPackage "CUETools.Empty.dll") `
        -Bytes ([byte[]]@())
    Assert-Throws `
        {
            & $installer `
                -PackageDirectory $emptyPackage `
                -DestinationRoot (Join-Path $tempRoot "empty-dll-destination")
        } `
        "must not be empty" `
        "An empty plugin DLL was accepted."

    Assert-Throws `
        {
            & $installer `
                -PackageDirectory $packageOne `
                -DestinationRoot ([IO.Path]::GetPathRoot($tempRoot))
        } `
        "must not be a filesystem root" `
        "A filesystem root was accepted as the destination root."

    $outside = Join-Path $tempRoot "outside"
    [IO.Directory]::CreateDirectory($outside) | Out-Null
    Write-TestBytes `
        -Path (Join-Path $outside "native.dll") `
        -Bytes ([byte[]](1))
    $reparsePackage = Join-Path $tempRoot "reparse-package"
    [IO.Directory]::CreateDirectory($reparsePackage) | Out-Null
    Write-TestBytes `
        -Path (Join-Path $reparsePackage "CUETools.Sample.dll") `
        -Bytes ([byte[]](1))
    $linkedX64 = Join-Path $reparsePackage "x64"
    New-Item -ItemType Junction -Path $linkedX64 -Target $outside | Out-Null
    $reparseEntries.Add($linkedX64)
    Assert-Throws `
        {
            & $installer `
                -PackageDirectory $reparsePackage `
                -DestinationRoot (Join-Path $tempRoot "reparse-destination")
        } `
        "reparse point" `
        "A package architecture junction was accepted."

    $packageTwo = Join-Path $tempRoot "package-two"
    $packageMono = Join-Path $packageTwo "mono"
    [IO.Directory]::CreateDirectory($packageMono) | Out-Null
    Write-TestBytes `
        -Path (Join-Path $packageTwo "CUETools.Sample.dll") `
        -Bytes ([byte[]](10, 11, 12))
    Write-TestBytes `
        -Path (Join-Path $packageMono "helper.dll") `
        -Bytes ([byte[]](13, 14))

    $replaceResult = & $installer `
        -PackageDirectory $packageTwo `
        -DestinationRoot $destinationRoot `
        -Replace
    Assert-True `
        (-not [string]::IsNullOrWhiteSpace($replaceResult.BackupDirectory)) `
        "Replacement did not report the preserved backup."
    Assert-True `
        ([IO.Directory]::Exists($replaceResult.BackupDirectory)) `
        "Replacement did not preserve the prior plugin set."
    Assert-True `
        ([IO.Path]::GetFileName($replaceResult.BackupDirectory).StartsWith(
            "plugins-backup-",
            [StringComparison]::Ordinal)) `
        "The preserved backup does not have a timestamped backup name."
    Assert-Equal `
        $installedHashBeforeRefusal `
        (Get-Sha256 -Path (
            Join-Path $replaceResult.BackupDirectory "CUETools.Sample.dll")) `
        "The preserved backup does not contain the prior plugin bytes."
    Assert-Equal `
        (Get-Sha256 -Path (Join-Path $packageTwo "CUETools.Sample.dll")) `
        (Get-Sha256 -Path (Join-Path $installed "CUETools.Sample.dll")) `
        "Replacement did not publish the new plugin bytes."
    Assert-True `
        ([IO.File]::Exists((Join-Path $installed "mono\helper.dll"))) `
        "Replacement did not publish an architecture dependency."
    Assert-True `
        (-not [IO.File]::Exists((Join-Path $installed "Dependency.dll"))) `
        "Replacement merged with the prior set instead of publishing an exact set."
    Assert-Equal `
        0 `
        @(
            Get-ChildItem `
                -LiteralPath $destinationRoot `
                -Force `
                -Filter ".plugins-stage-*").Count `
        "The installer left an owned staging directory behind."

    Write-Host "Plugin installer checks passed: $script:checkCount"
}
finally {
    foreach ($reparsePath in $reparseEntries) {
        if (Test-Path -LiteralPath $reparsePath) {
            $reparseInfo = Get-Item -LiteralPath $reparsePath -Force
            if (($reparseInfo.Attributes -band
                [IO.FileAttributes]::ReparsePoint) -eq 0 -or
                -not $reparsePath.StartsWith(
                    $tempRoot.TrimEnd(
                        [IO.Path]::DirectorySeparatorChar,
                        [IO.Path]::AltDirectorySeparatorChar) +
                        [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove an unexpected test reparse point: $reparsePath"
            }
            if ($reparseInfo.PSIsContainer) {
                [IO.Directory]::Delete($reparsePath)
            }
            else {
                [IO.File]::Delete($reparsePath)
            }
        }
    }

    $tempPrefix = $tempBase.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $tempLeaf = [IO.Path]::GetFileName($tempRoot)
    if (-not $tempRoot.StartsWith(
        $tempPrefix,
        [StringComparison]::OrdinalIgnoreCase) -or
        -not $tempLeaf.StartsWith(
            "cuetools-plugin-install-",
            [StringComparison]::Ordinal)) {
        throw "Refusing to clean an unexpected plugin installer test path: $tempRoot"
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
