<#
.SYNOPSIS
Enrolls one explicitly trusted CUETools plugin package for the current user.

.DESCRIPTION
Copies a strict DLL-only package into the per-user CUETools2026 plugin trust
zone and writes an exact, ordinally sorted SHA-256 manifest. Plugins execute
in-process with the user's privileges. This script records the bytes the user
approved; it does not authenticate the plugin publisher.

.PARAMETER PackageDirectory
A directory containing top-level DLLs and, optionally, mono, win32, or x64
subdirectories that themselves contain only DLLs.

.PARAMETER DestinationRoot
Overrides the default %AppData%\CUETools2026 root. Intended for isolated
validation and managed deployments.

.PARAMETER Replace
Replaces the active per-user set. The prior directory is retained as a
timestamped sibling backup.

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-CUEToolsPlugin.ps1 -PackageDirectory C:\Downloads\MyCodec
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [string]$DestinationRoot,

    [switch]$Replace
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$manifestName = "CUETools.PluginManifest.v1"
$pluginDirectoryName = "plugins"
$architectures = @("mono", "win32", "x64")
$maximumEntries = 128
$maximumManifestBytes = 64 * 1024

function Assert-NoReparsePointInExistingPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrWhiteSpace($pathRoot)) {
        throw "$Purpose is not an absolute path: $Path"
    }

    $currentPath = $pathRoot
    $pathsToInspect = @($pathRoot)
    foreach ($component in @($fullPath.Substring($pathRoot.Length) -split "[\\/]")) {
        if ([string]::IsNullOrWhiteSpace($component)) {
            continue
        }
        $currentPath = Join-Path $currentPath $component
        $pathsToInspect += $currentPath
    }

    foreach ($candidatePath in $pathsToInspect) {
        try {
            $item = Get-Item -LiteralPath $candidatePath -Force -ErrorAction Stop
        }
        catch {
            if ($_.CategoryInfo.Category -eq
                [Management.Automation.ErrorCategory]::ObjectNotFound) {
                continue
            }
            throw "Unable to inspect $Purpose path component '$candidatePath': $($_.Exception.Message)"
        }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Purpose must not contain a reparse point: $candidatePath"
        }
    }
}

function Test-SameOrDescendantPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CandidatePath,
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    $candidateFullPath = [IO.Path]::GetFullPath($CandidatePath)
    $rootFullPath = [IO.Path]::GetFullPath($RootPath)
    if ([string]::Equals(
        $candidateFullPath,
        $rootFullPath,
        [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $rootPrefix = $rootFullPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    return $candidateFullPath.StartsWith(
        $rootPrefix,
        [StringComparison]::OrdinalIgnoreCase)
}

function Assert-RegularDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    Assert-NoReparsePointInExistingPath -Path $Path -Purpose $Purpose
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (-not $item.PSIsContainer -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Purpose must be a regular directory: $Path"
    }
}

function Assert-RegularFile {
    param(
        [Parameter(Mandatory = $true)]
        [IO.FileInfo]$File,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    if (($File.Attributes -band
        ([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint)) -ne 0) {
        throw "$Purpose must be a regular file: $($File.FullName)"
    }
    if ($File.Length -le 0) {
        throw "$Purpose must not be empty: $($File.FullName)"
    }
}

function Assert-PluginFileName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Name) -or
        -not $Name.EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            $Name,
            [IO.Path]::GetFileName($Name),
            [StringComparison]::Ordinal) -or
        $Name.EndsWith(" ", [StringComparison]::Ordinal) -or
        $Name.EndsWith(".", [StringComparison]::Ordinal)) {
        throw "Plugin packages may contain only simply named DLL files."
    }

    foreach ($character in $Name.ToCharArray()) {
        if ([char]::IsControl($character) -or
            [Array]::IndexOf([IO.Path]::GetInvalidFileNameChars(), $character) -ge 0) {
            throw "Plugin DLL names must not contain control or invalid filename characters."
        }
    }
}

function Get-Sha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [IO.File]::Open(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
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

function Copy-VerifiedPluginFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    $sourceStream = [IO.File]::Open(
        $SourcePath,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    $sourceSha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $sourceHash = [BitConverter]::ToString(
            $sourceSha256.ComputeHash($sourceStream)).Replace("-", "")
        $sourceStream.Position = 0

        $destinationStream = [IO.File]::Open(
            $DestinationPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        try {
            $sourceStream.CopyTo($destinationStream)
            $destinationStream.Flush($true)
        }
        finally {
            $destinationStream.Dispose()
        }
    }
    finally {
        $sourceSha256.Dispose()
        $sourceStream.Dispose()
    }

    $destinationHash = Get-Sha256 -Path $DestinationPath
    if (-not [string]::Equals(
        $sourceHash,
        $destinationHash,
        [StringComparison]::Ordinal)) {
        throw "A staged plugin copy did not match its source: $SourcePath"
    }
    return $sourceHash
}

function Get-PluginPackageRecords {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    Assert-RegularDirectory -Path $Root -Purpose "Plugin package directory"
    $records = New-Object "Collections.Generic.List[object]"
    $seenPaths = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    $managedEntryPointCount = 0

    foreach ($entry in @(Get-ChildItem -LiteralPath $Root -Force)) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Plugin packages must not contain reparse points: $($entry.FullName)"
        }

        if ($entry.PSIsContainer) {
            $architecture = $null
            foreach ($candidate in $architectures) {
                if ([string]::Equals(
                    $entry.Name,
                    $candidate,
                    [StringComparison]::Ordinal)) {
                    $architecture = $candidate
                    break
                }
            }
            if ($null -eq $architecture) {
                throw "Unknown plugin package directory '$($entry.Name)'. Allowed directories: mono, win32, x64."
            }
            Assert-RegularDirectory `
                -Path $entry.FullName `
                -Purpose "Plugin architecture directory"

            foreach ($architectureEntry in @(
                Get-ChildItem -LiteralPath $entry.FullName -Force)) {
                if ($architectureEntry.PSIsContainer -or
                    -not ($architectureEntry -is [IO.FileInfo])) {
                    throw "Plugin architecture directories may contain DLL files only: $($architectureEntry.FullName)"
                }
                Assert-RegularFile `
                    -File $architectureEntry `
                    -Purpose "Plugin package entry"
                Assert-PluginFileName -Name $architectureEntry.Name
                $relativePath = $architecture + "/" + $architectureEntry.Name
                if (-not $seenPaths.Add($relativePath)) {
                    throw "The plugin package contains a duplicate path: $relativePath"
                }
                if ($architectureEntry.Name.StartsWith(
                    "CUETools.",
                    [StringComparison]::OrdinalIgnoreCase)) {
                    $managedEntryPointCount++
                }
                $records.Add([PSCustomObject]@{
                    RelativePath = $relativePath
                    SourcePath = $architectureEntry.FullName
                })
            }
            continue
        }

        if (-not ($entry -is [IO.FileInfo])) {
            throw "Plugin packages may contain DLL files and architecture directories only: $($entry.FullName)"
        }
        Assert-RegularFile -File $entry -Purpose "Plugin package entry"
        Assert-PluginFileName -Name $entry.Name
        if (-not $seenPaths.Add($entry.Name)) {
            throw "The plugin package contains a duplicate path: $($entry.Name)"
        }
        if ($entry.Name.StartsWith(
            "CUETools.",
            [StringComparison]::OrdinalIgnoreCase)) {
            $managedEntryPointCount++
        }
        $records.Add([PSCustomObject]@{
            RelativePath = $entry.Name
            SourcePath = $entry.FullName
        })
    }

    if ($records.Count -eq 0) {
        throw "The plugin package contains no DLL files."
    }
    if ($records.Count -gt $maximumEntries) {
        throw "The plugin package exceeds the $maximumEntries-file manifest limit."
    }
    if ($managedEntryPointCount -eq 0) {
        throw "The plugin package contains no CUETools.*.dll managed plugin entry point."
    }

    return $records.ToArray()
}

function Assert-TreeHasNoReparsePoints {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    Assert-RegularDirectory -Path $Root -Purpose $Purpose
    $pending = New-Object "Collections.Generic.Stack[string]"
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        foreach ($entry in @(Get-ChildItem -LiteralPath $current -Force)) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Purpose must not contain reparse points: $($entry.FullName)"
            }
            if ($entry.PSIsContainer) {
                $pending.Push($entry.FullName)
            }
            elseif (-not ($entry -is [IO.FileInfo])) {
                throw "$Purpose contains an unsupported filesystem entry: $($entry.FullName)"
            }
        }
    }
}

function Remove-OwnedStage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StagePath,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    if (-not (Test-Path -LiteralPath $StagePath)) {
        return
    }
    $stageFullPath = [IO.Path]::GetFullPath($StagePath)
    $stageLeaf = [IO.Path]::GetFileName($stageFullPath)
    if (-not (Test-SameOrDescendantPath -CandidatePath $stageFullPath -RootPath $Root) -or
        [string]::Equals(
            $stageFullPath,
            [IO.Path]::GetFullPath($Root),
            [StringComparison]::OrdinalIgnoreCase) -or
        -not $stageLeaf.StartsWith(
            ".plugins-stage-",
            [StringComparison]::Ordinal)) {
        throw "Refusing to clean an unexpected plugin staging path: $stageFullPath"
    }
    Assert-TreeHasNoReparsePoints `
        -Root $stageFullPath `
        -Purpose "Owned plugin staging directory"
    Remove-Item -LiteralPath $stageFullPath -Recurse -Force
}

$packageRoot = [IO.Path]::GetFullPath($PackageDirectory)
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $appData = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::ApplicationData)
    if ([string]::IsNullOrWhiteSpace($appData)) {
        throw "The current user's application-data directory is unavailable."
    }
    $DestinationRoot = [IO.Path]::Combine($appData, "CUETools2026")
}
$userDataRoot = [IO.Path]::GetFullPath($DestinationRoot)
$destinationPathRoot = [IO.Path]::GetPathRoot($userDataRoot)
if ([string]::Equals(
    $userDataRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar),
    $destinationPathRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar),
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "The plugin destination root must not be a filesystem root."
}
$destinationPath = [IO.Path]::Combine(
    $userDataRoot,
    $pluginDirectoryName)

if ((Test-SameOrDescendantPath `
        -CandidatePath $packageRoot `
        -RootPath $userDataRoot) -or
    (Test-SameOrDescendantPath `
        -CandidatePath $userDataRoot `
        -RootPath $packageRoot)) {
    throw "The plugin package and destination roots must not overlap."
}

$records = @(Get-PluginPackageRecords -Root $packageRoot)
Assert-NoReparsePointInExistingPath `
    -Path $userDataRoot `
    -Purpose "Plugin destination root"
if (-not [IO.Directory]::Exists($userDataRoot)) {
    [IO.Directory]::CreateDirectory($userDataRoot) | Out-Null
}
Assert-RegularDirectory `
    -Path $userDataRoot `
    -Purpose "Plugin destination root"

$mutexSha256 = [Security.Cryptography.SHA256]::Create()
try {
    $mutexBytes = [Text.Encoding]::UTF8.GetBytes(
        $userDataRoot.ToUpperInvariant())
    $mutexHash = [BitConverter]::ToString(
        $mutexSha256.ComputeHash($mutexBytes)).Replace("-", "")
}
finally {
    $mutexSha256.Dispose()
}
$mutexName = "Local\CUEToolsPluginInstall-" + $mutexHash
$mutex = [Threading.Mutex]::new($false, $mutexName)
$mutexAcquired = $false
$stagePath = $null
$backupPath = $null
try {
    try {
        $mutexAcquired = $mutex.WaitOne(0)
    }
    catch [Threading.AbandonedMutexException] {
        $mutexAcquired = $true
    }
    if (-not $mutexAcquired) {
        throw "Another plugin installation is already using this destination."
    }

    if ([IO.Directory]::Exists($destinationPath) -and -not $Replace) {
        throw "The user plugin directory already exists. Re-run with -Replace to preserve it as a backup and publish the new set."
    }
    if (Test-Path -LiteralPath $destinationPath) {
        Assert-TreeHasNoReparsePoints `
            -Root $destinationPath `
            -Purpose "Existing user plugin directory"
    }

    $stageLeaf = ".plugins-stage-" + [Guid]::NewGuid().ToString("N")
    $stagePath = [IO.Path]::Combine($userDataRoot, $stageLeaf)
    [IO.Directory]::CreateDirectory($stagePath) | Out-Null
    Assert-RegularDirectory `
        -Path $stagePath `
        -Purpose "Plugin staging directory"

    $manifestRecords = @{}
    foreach ($record in $records) {
        $relativePlatformPath = $record.RelativePath.Replace(
            "/",
            [IO.Path]::DirectorySeparatorChar)
        $stagedPath = [IO.Path]::Combine($stagePath, $relativePlatformPath)
        $stagedParent = [IO.Path]::GetDirectoryName($stagedPath)
        if (-not [IO.Directory]::Exists($stagedParent)) {
            [IO.Directory]::CreateDirectory($stagedParent) | Out-Null
        }
        Assert-RegularDirectory `
            -Path $stagedParent `
            -Purpose "Plugin staging directory"
        $hash = Copy-VerifiedPluginFile `
            -SourcePath $record.SourcePath `
            -DestinationPath $stagedPath
        $manifestRecords.Add($record.RelativePath, $hash)
    }

    $relativePaths = [string[]]$manifestRecords.Keys
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)
    $manifestPath = [IO.Path]::Combine($stagePath, $manifestName)
    $manifestStream = [IO.File]::Open(
        $manifestPath,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None)
    $manifestWriter = $null
    try {
        $manifestWriter = New-Object IO.StreamWriter(
            $manifestStream,
            (New-Object Text.UTF8Encoding($false)))
        $manifestWriter.NewLine = "`r`n"
        foreach ($relativePath in $relativePaths) {
            $manifestWriter.WriteLine(
                $manifestRecords[$relativePath] + "`t" + $relativePath)
        }
        $manifestWriter.Flush()
        $manifestStream.Flush($true)
    }
    finally {
        if ($null -ne $manifestWriter) {
            $manifestWriter.Dispose()
        }
        $manifestStream.Dispose()
    }

    if ((Get-Item -LiteralPath $manifestPath -Force).Length -gt
        $maximumManifestBytes) {
        throw "The generated plugin manifest exceeds the $maximumManifestBytes-byte runtime limit."
    }
    $manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
    if ($manifestBytes.Length -ge 3 -and
        $manifestBytes[0] -eq 0xef -and
        $manifestBytes[1] -eq 0xbb -and
        $manifestBytes[2] -eq 0xbf) {
        throw "The generated plugin manifest unexpectedly contains a UTF-8 byte-order mark."
    }
    foreach ($relativePath in $relativePaths) {
        $stagedPath = [IO.Path]::Combine(
            $stagePath,
            $relativePath.Replace("/", [IO.Path]::DirectorySeparatorChar))
        $stagedHash = Get-Sha256 -Path $stagedPath
        if (-not [string]::Equals(
            $stagedHash,
            $manifestRecords[$relativePath],
            [StringComparison]::Ordinal)) {
            throw "A staged plugin changed before publication: $relativePath"
        }
    }
    Assert-TreeHasNoReparsePoints `
        -Root $stagePath `
        -Purpose "Plugin staging directory"

    $oldSetMoved = $false
    if ([IO.Directory]::Exists($destinationPath)) {
        $backupLeaf = "plugins-backup-" +
            [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssfffZ") +
            "-" +
            [Guid]::NewGuid().ToString("N")
        $backupPath = [IO.Path]::Combine($userDataRoot, $backupLeaf)
        [IO.Directory]::Move($destinationPath, $backupPath)
        $oldSetMoved = $true
    }

    try {
        [IO.Directory]::Move($stagePath, $destinationPath)
        $stagePath = $null
    }
    catch {
        $publishError = $_.Exception
        if ($oldSetMoved -and
            -not (Test-Path -LiteralPath $destinationPath) -and
            [IO.Directory]::Exists($backupPath)) {
            try {
                [IO.Directory]::Move($backupPath, $destinationPath)
                $backupPath = $null
            }
            catch {
                throw "Plugin publication failed, and the prior set could not be restored. The preserved backup is '$backupPath'. Publish error: $($publishError.Message) Restore error: $($_.Exception.Message)"
            }
        }
        throw $publishError
    }

    [PSCustomObject]@{
        PluginDirectory = $destinationPath
        ManifestPath = [IO.Path]::Combine($destinationPath, $manifestName)
        BackupDirectory = $backupPath
        RestartRequired = $true
    }
}
finally {
    try {
        if ($null -ne $stagePath -and
            (Test-Path -LiteralPath $stagePath)) {
            Remove-OwnedStage -StagePath $stagePath -Root $userDataRoot
        }
    }
    finally {
        if ($mutexAcquired) {
            $mutex.ReleaseMutex()
        }
        $mutex.Dispose()
    }
}
