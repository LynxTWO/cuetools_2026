Set-StrictMode -Version 2.0

function Assert-PinnedSourceArchiveBinding {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Inventory,
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$ArtifactId,
        [Parameter(Mandatory = $true)]
        [string]$PinnedRelativePath
    )

    $repositoryFullPath = [IO.Path]::GetFullPath($RepositoryRoot)
    $repositoryPrefix = $repositoryFullPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $normalizedPinnedPath = $PinnedRelativePath.Replace("\", "/")

    $pins = @(
        @($Inventory.pinnedFiles) |
            Where-Object {
                [string]::Equals(
                    ([string]$_.path).Replace("\", "/"),
                    $normalizedPinnedPath,
                    [StringComparison]::OrdinalIgnoreCase)
            })
    if ($pins.Count -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$pins[0].sha256)) {
        throw (
            "Native dependency inventory must contain exactly one SHA-256 pin for " +
            "$normalizedPinnedPath.")
    }

    $artifacts = @(
        @($Inventory.artifacts) |
            Where-Object {
                [string]::Equals(
                    [string]$_.id,
                    $ArtifactId,
                    [StringComparison]::Ordinal)
            })
    if ($artifacts.Count -ne 1) {
        throw (
            "Native dependency inventory must contain exactly one artifact named " +
            "'$ArtifactId'.")
    }
    $artifact = $artifacts[0]
    $sourceArchiveProperty = $artifact.PSObject.Properties["sourceArchive"]
    if ($null -eq $sourceArchiveProperty -or
        $sourceArchiveProperty.Value -is [string]) {
        throw "Artifact '$ArtifactId' must have structured sourceArchive metadata."
    }
    $sourceArchive = $sourceArchiveProperty.Value
    if ([string]::IsNullOrWhiteSpace([string]$sourceArchive.sha256) -or
        $null -eq $sourceArchive.bytes) {
        throw (
            "Artifact '$ArtifactId' sourceArchive metadata must record SHA-256 " +
            "and byte length.")
    }

    $pinHash = ([string]$pins[0].sha256).ToLowerInvariant()
    $metadataHash = ([string]$sourceArchive.sha256).ToLowerInvariant()
    if ($pinHash -ne $metadataHash) {
        throw (
            "Artifact '$ArtifactId' sourceArchive SHA-256 does not match the " +
            "pinned file entry for $normalizedPinnedPath.")
    }

    $inputReferencesArchive = $false
    foreach ($sourceInput in @($artifact.sourceInputs)) {
        $normalizedInput = ([string]$sourceInput).Replace("\", "/")
        if ([string]::Equals(
                $normalizedInput,
                $normalizedPinnedPath,
                [StringComparison]::OrdinalIgnoreCase) -or
            $normalizedInput.StartsWith(
                "$normalizedPinnedPath ",
                [StringComparison]::OrdinalIgnoreCase)) {
            $inputReferencesArchive = $true
            break
        }
    }
    if (-not $inputReferencesArchive) {
        throw (
            "Artifact '$ArtifactId' sourceInputs do not bind the pinned archive " +
            "$normalizedPinnedPath.")
    }

    $archivePath = [IO.Path]::GetFullPath(
        (Join-Path $repositoryFullPath $PinnedRelativePath))
    if (-not $archivePath.StartsWith(
            $repositoryPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        throw (
            "Pinned source archive is missing or escapes the repository: " +
            $normalizedPinnedPath)
    }
    $archiveInfo = Get-Item -LiteralPath $archivePath -Force
    if (($archiveInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Pinned source archive must not be a reparse point: $normalizedPinnedPath"
    }
    if ([int64]$sourceArchive.bytes -ne [int64]$archiveInfo.Length) {
        throw (
            "Artifact '$ArtifactId' sourceArchive byte length does not match " +
            "the pinned file $normalizedPinnedPath.")
    }
    $actualHash = (
        Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    if ($actualHash -ne $pinHash) {
        throw (
            "Pinned source archive does not match its SHA-256: " +
            $normalizedPinnedPath)
    }
}

function Get-CUEToolsMacSdkSourceClosure {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [object]$Inventory
    )

    $repositoryFullPath = [IO.Path]::GetFullPath($RepositoryRoot)
    $repositoryPrefix = $repositoryFullPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $macRelativePath = "ThirdParty/MAC_SDK"
    $archiveRelativePath = "$macRelativePath/MAC_1320_SDK.zip"
    $macRoot = [IO.Path]::GetFullPath(
        (Join-Path $repositoryFullPath $macRelativePath))
    $archivePath = [IO.Path]::GetFullPath(
        (Join-Path $repositoryFullPath $archiveRelativePath))
    if (-not $macRoot.StartsWith(
            $repositoryPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $macRoot -PathType Container)) {
        throw "Monkey's Audio SDK expansion root is missing or escapes the repository."
    }

    Assert-PinnedSourceArchiveBinding `
        -Inventory $Inventory `
        -RepositoryRoot $repositoryFullPath `
        -ArtifactId "monkeys-audio" `
        -PinnedRelativePath $archiveRelativePath

    function Assert-MacClosureRegularFile(
        [string]$Path,
        [string]$Purpose) {
        if (-not $Path.StartsWith(
                $repositoryPrefix,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
            throw "$Purpose is missing or escapes the repository: $Path"
        }
        $info = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
        if (-not ($info -is [IO.FileInfo]) -or
            ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Purpose must be a regular file: $Path"
        }
        return $info
    }

    function Get-MacClosureStreamSha256([IO.Stream]$Stream) {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString(
                $algorithm.ComputeHash($Stream))).Replace(
                    "-",
                    "").ToLowerInvariant()
        }
        finally {
            $algorithm.Dispose()
        }
    }

    $overridePaths = [string[]]@(
        "Source/MACLibDll/MACLibDll.cpp",
        "Source/MACLibDll/MACLibDll.def",
        "Source/MACLibDll/MACLibDll.h",
        "Source/Projects/VS2022/MACLibDll/MACLibDll.vcxproj")
    $overrideSet = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($relativePath in $overridePaths) {
        [void]$overrideSet.Add($relativePath)
    }

    $pinLookup = @{}
    foreach ($relativePath in $overridePaths) {
        $manifestPath = "$macRelativePath/$relativePath"
        $pins = @(
            @($Inventory.pinnedFiles) |
                Where-Object {
                    [string]::Equals(
                        ([string]$_.path).Replace("\", "/"),
                        $manifestPath,
                        [StringComparison]::OrdinalIgnoreCase)
                })
        if ($pins.Count -ne 1 -or
            [string]$pins[0].sha256 -notmatch "^[0-9A-Fa-f]{64}$") {
            throw (
                "Native dependency inventory must contain exactly one " +
                "SHA-256 pin for $manifestPath.")
        }
        $pinLookup[$manifestPath] =
            ([string]$pins[0].sha256).ToLowerInvariant()
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    $members = [Collections.Generic.Dictionary[string,object]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $macRelativeMembers = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $identityLines = New-Object "Collections.Generic.List[string]"
    try {
        $macPrefix = $macRoot.TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) {
                continue
            }
            $normalized = $entry.FullName.Replace("\", "/").TrimStart("/")
            if ([string]::IsNullOrWhiteSpace($normalized) -or
                [IO.Path]::IsPathRooted($normalized)) {
                throw "Monkey's Audio SDK archive contains an unsafe member path."
            }
            $destination = [IO.Path]::GetFullPath(
                (Join-Path $macRoot $normalized))
            if (-not $destination.StartsWith(
                    $macPrefix,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw (
                    "Monkey's Audio SDK archive member escapes its expansion " +
                    "root: $normalized")
            }
            if (-not $macRelativeMembers.Add($normalized)) {
                throw "Monkey's Audio SDK archive contains a duplicate member: $normalized"
            }
            if ($overrideSet.Contains($normalized)) {
                throw (
                    "Monkey's Audio SDK archive collides with a CUETools " +
                    "override: $normalized")
            }

            $entryStream = $entry.Open()
            try {
                $entryHash = Get-MacClosureStreamSha256 $entryStream
            }
            finally {
                $entryStream.Dispose()
            }
            $expandedInfo = Assert-MacClosureRegularFile `
                -Path $destination `
                -Purpose "Expanded Monkey's Audio SDK member"
            $expandedHash = (
                Get-FileHash -LiteralPath $destination -Algorithm SHA256
            ).Hash.ToLowerInvariant()
            if ([long]$expandedInfo.Length -ne [long]$entry.Length -or
                $expandedHash -ne $entryHash) {
                throw (
                    "Expanded Monkey's Audio SDK member differs from the " +
                    "pinned archive: $normalized")
            }
            $repositoryRelativePath = "$macRelativePath/$normalized"
            $record = [pscustomobject]([ordered]@{
                path = $repositoryRelativePath
                bytes = [long]$entry.Length
                sha256 = $entryHash
            })
            $members.Add($repositoryRelativePath, $record)
            $identityLines.Add(
                "archive`t$normalized`t$($record.bytes)`t$($record.sha256)")
        }
    }
    finally {
        $archive.Dispose()
    }
    if ($members.Count -eq 0) {
        throw "Monkey's Audio SDK archive contains no files."
    }

    foreach ($relativePath in $overridePaths) {
        $manifestPath = "$macRelativePath/$relativePath"
        $overridePath = [IO.Path]::GetFullPath(
            (Join-Path $repositoryFullPath $manifestPath))
        $overrideInfo = Assert-MacClosureRegularFile `
            -Path $overridePath `
            -Purpose "CUETools Monkey's Audio SDK override"
        $overrideHash = (
            Get-FileHash -LiteralPath $overridePath -Algorithm SHA256
        ).Hash.ToLowerInvariant()
        if ($overrideHash -ne [string]$pinLookup[$manifestPath]) {
            throw (
                "CUETools Monkey's Audio SDK override differs from its " +
                "pinned hash: $relativePath")
        }
        $identityLines.Add(
            "override`t$relativePath`t$($overrideInfo.Length)`t$overrideHash")
    }

    $generatedPattern =
        "^Source/Projects/Visual Studio - 2022/MACLib/(?:x64/)?(?:Debug|Release)/"
    $generatedFileCount = 0
    foreach ($item in (Get-ChildItem -LiteralPath $macRoot -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw (
                "Monkey's Audio SDK source closure contains a reparse point: " +
                $item.FullName)
        }
        if ($item.PSIsContainer) {
            continue
        }
        if (-not ($item -is [IO.FileInfo])) {
            throw (
                "Monkey's Audio SDK source closure contains a non-file leaf: " +
                $item.FullName)
        }
        $relativePath = $item.FullName.Substring($macRoot.Length).TrimStart(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar).Replace("\", "/")
        if ($relativePath -eq "MAC_1320_SDK.zip" -or
            $macRelativeMembers.Contains($relativePath) -or
            $overrideSet.Contains($relativePath)) {
            continue
        }
        if ($relativePath -match $generatedPattern) {
            $generatedFileCount++
            continue
        }
        throw (
            "Monkey's Audio SDK source closure contains an unbound file: " +
            $relativePath)
    }

    $orderedIdentityLines = [string[]]@($identityLines)
    [Array]::Sort($orderedIdentityLines, [StringComparer]::Ordinal)
    $identityText = ($orderedIdentityLines -join "`n") + "`n"
    $identityAlgorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $identityHash = ([BitConverter]::ToString(
            $identityAlgorithm.ComputeHash(
                (New-Object Text.UTF8Encoding($false)).GetBytes(
                    $identityText)))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $identityAlgorithm.Dispose()
    }

    $orderedMemberPaths = [string[]]$members.Keys
    [Array]::Sort($orderedMemberPaths, [StringComparer]::Ordinal)
    $archiveInfo = Get-Item -LiteralPath $archivePath -Force
    return [pscustomobject]([ordered]@{
        summary = [pscustomobject]([ordered]@{
            classification = "pinned-native-source-expansion"
            path = $macRelativePath
            state = "validated"
            archive = [pscustomobject]([ordered]@{
                path = $archiveRelativePath
                bytes = [long]$archiveInfo.Length
                sha256 = (
                    Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
                ).Hash.ToLowerInvariant()
            })
            archiveFileCount = [int]$members.Count
            overrideFileCount = [int]$overridePaths.Count
            generatedFileCount = [int]$generatedFileCount
            expandedTreeSha256 = $identityHash
        })
        archiveMembers = @(
            $orderedMemberPaths | ForEach-Object { $members[$_] })
    })
}
