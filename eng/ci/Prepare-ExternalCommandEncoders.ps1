[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$CacheDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
} else {
    [IO.Path]::GetFullPath($RepositoryRoot)
}
$repoPrefix = $repoRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$outputRoot = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot "ThirdParty\encoders"))
$expectedOutputRoot = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot "ThirdParty\encoders"))
if (-not [string]::Equals(
        $outputRoot,
        $expectedOutputRoot,
        [StringComparison]::OrdinalIgnoreCase) -or
    -not $outputRoot.StartsWith(
        $repoPrefix,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "External encoder output root is unsafe: $outputRoot"
}
if ([string]::IsNullOrWhiteSpace($CacheDirectory)) {
    $CacheDirectory = Join-Path $repoRoot "obj\external-encoder-cache"
}
$cacheRoot = [IO.Path]::GetFullPath($CacheDirectory)

function Assert-SafeDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    if (Test-Path -LiteralPath $Path) {
        $info = Get-Item -LiteralPath $Path -Force
        if (-not $info.PSIsContainer -or
            ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Purpose is not a regular directory: $Path"
        }
        return
    }
    [void][IO.Directory]::CreateDirectory($Path)
}

function Assert-SafeOutputParents {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $outputPrefix = $outputRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith(
            $outputPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "External encoder output escapes its owned root: $Path"
    }

    $relative = $fullPath.Substring($outputPrefix.Length)
    $parts = $relative.Split(
        @(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar),
        [StringSplitOptions]::RemoveEmptyEntries)
    $current = $outputRoot
    for ($index = 0; $index -lt $parts.Length - 1; $index++) {
        $current = Join-Path $current $parts[$index]
        Assert-SafeDirectory -Path $current -Purpose "External encoder output parent"
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-ExactFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][Int64]$Bytes,
        [Parameter(Mandatory = $true)][string]$Sha256,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Purpose is missing: $Path"
    }
    $info = Get-Item -LiteralPath $Path -Force
    if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Purpose must not be a reparse point: $Path"
    }
    if ($info.Length -ne $Bytes) {
        throw "$Purpose has length $($info.Length), expected $Bytes."
    }
    $actualHash = Get-Sha256 $Path
    if ($actualHash -ne $Sha256.ToLowerInvariant()) {
        throw "$Purpose SHA-256 is $actualHash, expected $Sha256."
    }
}

function Resolve-RepositoryFile {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    if ([IO.Path]::IsPathRooted($RelativePath)) {
        throw "$Purpose must use a repository-relative path."
    }
    $fullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $RelativePath))
    if (-not $fullPath.StartsWith(
            $repoPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose escapes the repository: $RelativePath"
    }

    $relative = $fullPath.Substring($repoPrefix.Length)
    $parts = $relative.Split(
        @(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar),
        [StringSplitOptions]::RemoveEmptyEntries)
    $current = $repoRoot
    foreach ($part in $parts) {
        $current = Join-Path $current $part
        if (-not (Test-Path -LiteralPath $current)) {
            throw "$Purpose is missing: $current"
        }
        $info = Get-Item -LiteralPath $current -Force
        if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Purpose traverses a reparse point: $current"
        }
    }
    return $fullPath
}

function Get-VerifiedDownload {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Kind,
        [Parameter(Mandatory = $true)][object]$Archive
    )

    $uri = [Uri]([string]$Archive.url)
    if ($uri.Scheme -ne "https") {
        throw "$Id $Kind URL is not HTTPS."
    }
    $archiveLeaf = [IO.Path]::GetFileName($uri.AbsolutePath)
    if ([string]::IsNullOrWhiteSpace($archiveLeaf)) {
        throw "$Id $Kind URL has no file name."
    }
    $cachePath = Join-Path $cacheRoot ("$Id-$Kind-$archiveLeaf")
    if (Test-Path -LiteralPath $cachePath) {
        try {
            Assert-ExactFile `
                -Path $cachePath `
                -Bytes ([Int64]$Archive.bytes) `
                -Sha256 ([string]$Archive.sha256) `
                -Purpose "$Id cached $Kind archive"
            return $cachePath
        }
        catch {
            # Delete only the one explicitly resolved cache file after its parent
            # and full path have been fixed above.
            Remove-Item -LiteralPath $cachePath -Force
        }
    }

    $stagePath = Join-Path $cacheRoot (
        ".$Id-$Kind-$([Guid]::NewGuid().ToString('N')).downloading")
    try {
        Invoke-WebRequest `
            -Uri $uri.AbsoluteUri `
            -OutFile $stagePath `
            -UseBasicParsing
        Assert-ExactFile `
            -Path $stagePath `
            -Bytes ([Int64]$Archive.bytes) `
            -Sha256 ([string]$Archive.sha256) `
            -Purpose "$Id downloaded $Kind archive"
        [IO.File]::Move($stagePath, $cachePath)
        return $cachePath
    }
    finally {
        if (Test-Path -LiteralPath $stagePath -PathType Leaf) {
            Remove-Item -LiteralPath $stagePath -Force
        }
    }
}

function Publish-File {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256
    )

    Assert-SafeOutputParents $DestinationPath
    if (Test-Path -LiteralPath $DestinationPath) {
        $destinationInfo = Get-Item -LiteralPath $DestinationPath -Force
        if ($destinationInfo.PSIsContainer -or
            ($destinationInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "External encoder destination is unsafe: $DestinationPath"
        }
        if ((Get-Sha256 $DestinationPath) -eq $ExpectedSha256.ToLowerInvariant()) {
            return
        }
    }

    $directory = Split-Path -Parent $DestinationPath
    $stagePath = Join-Path $directory (
        "." + [IO.Path]::GetFileName($DestinationPath) + "." +
        [Guid]::NewGuid().ToString("N") + ".staging")
    $backupPath = Join-Path $directory (
        "." + [IO.Path]::GetFileName($DestinationPath) + "." +
        [Guid]::NewGuid().ToString("N") + ".backup")
    try {
        $sourceStream = [IO.File]::Open(
            $SourcePath,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        try {
            $stageStream = [IO.File]::Open(
                $stagePath,
                [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write,
                [IO.FileShare]::None)
            try {
                $sourceStream.CopyTo($stageStream)
                $stageStream.Flush($true)
            }
            finally { $stageStream.Dispose() }
        }
        finally { $sourceStream.Dispose() }

        if ((Get-Sha256 $stagePath) -ne $ExpectedSha256.ToLowerInvariant()) {
            throw "External encoder staging hash changed before publication."
        }
        if (Test-Path -LiteralPath $DestinationPath) {
            # File.Replace is atomic on the destination volume. Use a concrete
            # backup path because Windows PowerShell can bind a null third
            # argument as an empty path and reject an otherwise valid update.
            [IO.File]::Replace($stagePath, $DestinationPath, $backupPath)
            Remove-Item -LiteralPath $backupPath -Force
        }
        else {
            [IO.File]::Move($stagePath, $DestinationPath)
        }
    }
    finally {
        if (Test-Path -LiteralPath $stagePath -PathType Leaf) {
            Remove-Item -LiteralPath $stagePath -Force
        }
        if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }
}

function Expand-VerifiedZipEntry {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$EntryName,
        [Parameter(Mandatory = $true)][string]$DestinationPath,
        [Parameter(Mandatory = $true)][string]$ExpectedSha256
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $matches = @(
            $archive.Entries |
                Where-Object {
                    [string]::Equals(
                        $_.FullName.Replace("\", "/"),
                        $EntryName.Replace("\", "/"),
                        [StringComparison]::Ordinal)
                })
        if ($matches.Count -ne 1 -or
            [string]::IsNullOrEmpty($matches[0].Name)) {
            throw "Archive must contain exactly one regular '$EntryName' entry."
        }

        Assert-SafeOutputParents $DestinationPath
        $directory = Split-Path -Parent $DestinationPath
        $stagePath = Join-Path $directory (
            "." + [IO.Path]::GetFileName($DestinationPath) + "." +
            [Guid]::NewGuid().ToString("N") + ".extracting")
        try {
            $entryStream = $matches[0].Open()
            try {
                $fileStream = [IO.File]::Open(
                    $stagePath,
                    [IO.FileMode]::CreateNew,
                    [IO.FileAccess]::Write,
                    [IO.FileShare]::None)
                try {
                    $entryStream.CopyTo($fileStream)
                    $fileStream.Flush($true)
                }
                finally { $fileStream.Dispose() }
            }
            finally { $entryStream.Dispose() }

            if ((Get-Sha256 $stagePath) -ne $ExpectedSha256.ToLowerInvariant()) {
                throw "Extracted '$EntryName' does not match its pinned SHA-256."
            }
            Publish-File `
                -SourcePath $stagePath `
                -DestinationPath $DestinationPath `
                -ExpectedSha256 $ExpectedSha256
        }
        finally {
            if (Test-Path -LiteralPath $stagePath -PathType Leaf) {
                Remove-Item -LiteralPath $stagePath -Force
            }
        }
    }
    finally { $archive.Dispose() }
}

Assert-SafeDirectory -Path $cacheRoot -Purpose "External encoder cache"
Assert-SafeDirectory -Path $outputRoot -Purpose "External encoder output root"
$manifestPath = Join-Path $repoRoot "eng\release\external-command-encoders.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or @($manifest.encoders).Count -eq 0) {
    throw "External command encoder manifest is empty or unsupported."
}

$prepared = 0
foreach ($encoder in @($manifest.encoders)) {
    $id = [string]$encoder.id
    if ($id -notmatch "^[a-z0-9-]+$") {
        throw "External encoder id is invalid: $id"
    }
    $executablePath = Join-Path $outputRoot (
        "x64\" + [IO.Path]::GetFileName([string]$encoder.packagePath))
    $binaryPathProperty = $encoder.PSObject.Properties["binaryPath"]
    if ($null -ne $binaryPathProperty) {
        $binaryPath = Resolve-RepositoryFile `
            -RelativePath ([string]$binaryPathProperty.Value) `
            -Purpose "$id source-built executable"
        Assert-ExactFile `
            -Path $binaryPath `
            -Bytes ([Int64]$encoder.executableBytes) `
            -Sha256 ([string]$encoder.executableSha256) `
            -Purpose "$id source-built executable"
        Publish-File `
            -SourcePath $binaryPath `
            -DestinationPath $executablePath `
            -ExpectedSha256 ([string]$encoder.executableSha256)
    }
    else {
        $binaryArchive = Get-VerifiedDownload `
            -Id $id `
            -Kind "binary" `
            -Archive $encoder.binaryArchive
        Expand-VerifiedZipEntry `
            -ArchivePath $binaryArchive `
            -EntryName ([string]$encoder.archiveEntry) `
            -DestinationPath $executablePath `
            -ExpectedSha256 ([string]$encoder.executableSha256)
    }

    # Source provenance is part of the preparation gate even when a permissive
    # license does not require us to copy the archive into the product. This
    # keeps a stale URL or mistyped source hash from surviving behind an empty
    # packagePath.
    $sourceArchive = Get-VerifiedDownload `
        -Id $id `
        -Kind "source" `
        -Archive $encoder.sourceArchive
    $sourcePackagePath = [string]$encoder.sourceArchive.packagePath
    if (-not [string]::IsNullOrWhiteSpace($sourcePackagePath)) {
        $sourceDestination = Join-Path $outputRoot (
            "source\" + [IO.Path]::GetFileName($sourcePackagePath))
        Publish-File `
            -SourcePath $sourceArchive `
            -DestinationPath $sourceDestination `
            -ExpectedSha256 ([string]$encoder.sourceArchive.sha256)
    }
    $linkedLibrarySourceProperty =
        $encoder.PSObject.Properties["linkedLibrarySource"]
    if ($null -ne $linkedLibrarySourceProperty) {
        [void](Get-VerifiedDownload `
            -Id $id `
            -Kind "linked-source" `
            -Archive $linkedLibrarySourceProperty.Value)
    }
    $sourceSupportProperty = $encoder.PSObject.Properties["sourceSupport"]
    if ($null -ne $sourceSupportProperty) {
        foreach ($support in @($sourceSupportProperty.Value)) {
            $supportPath = Resolve-RepositoryFile `
                -RelativePath ([string]$support.path) `
                -Purpose "$id source support"
            Assert-ExactFile `
                -Path $supportPath `
                -Bytes ([Int64]$support.bytes) `
                -Sha256 ([string]$support.sha256) `
                -Purpose "$id source support"
            $supportDestination = Join-Path $outputRoot (
                "source\" +
                [IO.Path]::GetFileName([string]$support.packagePath))
            Publish-File `
                -SourcePath $supportPath `
                -DestinationPath $supportDestination `
                -ExpectedSha256 ([string]$support.sha256)
        }
    }
    $prepared++
}

Write-Host "External command encoders prepared: $prepared"
