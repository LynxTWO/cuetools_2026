function Get-CUEToolsSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).
        Hash.ToUpperInvariant()
}

function Get-CUEToolsTextSha256([string]$Text) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($Text)
        return [BitConverter]::ToString(
            $algorithm.ComputeHash($bytes)).Replace("-", "")
    }
    finally {
        $algorithm.Dispose()
    }
}

function Assert-CUEToolsPathWithin(
    [string]$Candidate,
    [string]$Parent,
    [string]$Purpose) {
    $fullCandidate = [IO.Path]::GetFullPath($Candidate)
    $fullParent = [IO.Path]::GetFullPath($Parent).
        TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $fullParent + [IO.Path]::DirectorySeparatorChar
    if (-not $fullCandidate.StartsWith(
        $prefix,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose escapes its owned root: $fullCandidate"
    }
    return $fullCandidate
}

function Assert-CUEToolsNoReparsePoint(
    [string]$Path,
    [string]$StopAt,
    [string]$Purpose) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $fullStop = [IO.Path]::GetFullPath($StopAt).
        TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
    $current = $fullPath
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
            if (($item.Attributes -band
                [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Purpose crosses a reparse point: $current"
            }
        }
        if ([string]::Equals(
            $current,
            $fullStop,
            [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrWhiteSpace($parent) -or
            [string]::Equals(
                $parent,
                $current,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Purpose does not descend from its expected root: $fullPath"
        }
        $current = $parent
    }
}

function Get-CUEToolsVendorDefinitions([string]$Root) {
    return @(
        [pscustomobject]([ordered]@{
            id = "wavpack"
            path = "ThirdParty/WavPack"
            patch = "ThirdParty/submodule_WavPack_CUETools.patch"
        }),
        [pscustomobject]([ordered]@{
            id = "windows-media-lib"
            path = "ThirdParty/WindowsMediaLib"
            patch = "ThirdParty/submodule_WindowsMediaLib_CUETools.patch"
        }),
        [pscustomobject]([ordered]@{
            id = "flac"
            path = "ThirdParty/flac"
            patch = "ThirdParty/submodule_flac_CUETools.patch"
        }),
        [pscustomobject]([ordered]@{
            id = "taglib-sharp"
            path = "ThirdParty/taglib-sharp"
            patch = "ThirdParty/submodule_taglib-sharp_CUETools.patch"
        }))
}

function Get-CUEToolsPinnedVendorRecords(
    [string]$Root,
    [object[]]$Definitions) {
    $records = New-Object "Collections.Generic.List[object]"
    foreach ($definition in $Definitions) {
        $relativePath = [string]$definition.path
        $submodulePath = [IO.Path]::GetFullPath(
            (Join-Path $Root $relativePath))
        if (-not (Test-Path -LiteralPath $submodulePath -PathType Container)) {
            throw "Pinned vendor submodule is missing: $relativePath"
        }
        Assert-CUEToolsNoReparsePoint `
            -Path $submodulePath `
            -StopAt $Root `
            -Purpose "Pinned vendor submodule"

        $treeLine = @(& git -C $Root ls-tree HEAD -- $relativePath)
        if ($LASTEXITCODE -ne 0 -or $treeLine.Count -ne 1 -or
            [string]$treeLine[0] -cnotmatch
                "^160000 commit (?<commit>[0-9a-f]{40})`t") {
            throw "Unable to resolve the pinned gitlink for $relativePath"
        }
        $pinnedCommit = $Matches["commit"]
        $head = (& git -C $submodulePath rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or
            $head -cnotmatch "^[0-9a-f]{40}$") {
            throw "Unable to resolve the checked-out commit for $relativePath"
        }
        if ($head -cne $pinnedCommit) {
            throw "Vendor submodule commit mismatch: $relativePath is $head, expected $pinnedCommit"
        }
        $status = @(
            & git -C $submodulePath status `
                --porcelain=v2 `
                --untracked-files=all)
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to inspect vendor submodule status: $relativePath"
        }
        if ($status.Count -ne 0) {
            throw "Vendor source staging requires a clean submodule: $relativePath"
        }

        $patchRelativePath = [string]$definition.patch
        $patchPath = [IO.Path]::GetFullPath(
            (Join-Path $Root $patchRelativePath))
        if (-not (Test-Path -LiteralPath $patchPath -PathType Leaf)) {
            throw "Checked vendor patch is missing: $patchRelativePath"
        }
        Assert-CUEToolsNoReparsePoint `
            -Path $patchPath `
            -StopAt $Root `
            -Purpose "Checked vendor patch"
        & git -C $Root ls-files --error-unmatch -- $patchRelativePath *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Vendor patch is not tracked: $patchRelativePath"
        }
        & git -C $Root apply `
            --check `
            --whitespace=nowarn `
            "--directory=$relativePath" `
            $patchPath
        if ($LASTEXITCODE -ne 0) {
            throw "Vendor patch does not apply to its clean pinned source: $patchRelativePath"
        }

        $records.Add([pscustomobject]([ordered]@{
            id = [string]$definition.id
            path = $relativePath.Replace("\", "/")
            commit = $pinnedCommit
            patch = $patchRelativePath.Replace("\", "/")
            patchSha256 = Get-CUEToolsSha256 $patchPath
            sourcePath = $submodulePath
            patchPath = $patchPath
        }))
    }
    return $records.ToArray()
}

function Expand-CUEToolsGitArchive(
    [string]$ArchivePath,
    [string]$DestinationPath) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [void][IO.Directory]::CreateDirectory($DestinationPath)
    $destination = [IO.Path]::GetFullPath($DestinationPath)
    $prefix = $destination.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $entryName = ([string]$entry.FullName).Replace(
                "/",
                [IO.Path]::DirectorySeparatorChar)
            if ([string]::IsNullOrWhiteSpace($entryName) -or
                [IO.Path]::IsPathRooted($entryName)) {
                throw "Pinned vendor archive contains an invalid path."
            }
            $target = [IO.Path]::GetFullPath(
                (Join-Path $destination $entryName))
            if (-not $target.StartsWith(
                $prefix,
                [StringComparison]::OrdinalIgnoreCase)) {
                throw "Pinned vendor archive entry escapes its stage: $($entry.FullName)"
            }
            if ([string]::IsNullOrEmpty([string]$entry.Name)) {
                [void][IO.Directory]::CreateDirectory($target)
                continue
            }
            $parent = [IO.Path]::GetDirectoryName($target)
            [void][IO.Directory]::CreateDirectory($parent)
            if (Test-Path -LiteralPath $target) {
                throw "Pinned vendor archive contains a duplicate path: $($entry.FullName)"
            }
            $input = $entry.Open()
            $output = New-Object IO.FileStream(
                $target,
                [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write,
                [IO.FileShare]::None)
            try {
                $input.CopyTo($output)
            }
            finally {
                $output.Dispose()
                $input.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-CUEToolsOwnedTreeFiles(
    [string]$Root,
    [string]$Purpose) {
    $fullRoot = [IO.Path]::GetFullPath($Root)
    $rootInfo = Get-Item -LiteralPath $fullRoot -Force -ErrorAction Stop
    if (-not $rootInfo.PSIsContainer -or
        ($rootInfo.Attributes -band
            [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Purpose root must be a regular directory: $fullRoot"
    }

    $directories =
        New-Object "Collections.Generic.Stack[IO.DirectoryInfo]"
    $files = New-Object "Collections.Generic.List[IO.FileInfo]"
    $directories.Push([IO.DirectoryInfo]$rootInfo)
    while ($directories.Count -gt 0) {
        $directory = $directories.Pop()
        foreach ($entry in $directory.EnumerateFileSystemInfos()) {
            if (($entry.Attributes -band
                [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Purpose contains a reparse point: $($entry.FullName)"
            }
            if (($entry.Attributes -band
                [IO.FileAttributes]::Directory) -ne 0) {
                $directories.Push([IO.DirectoryInfo]$entry)
            }
            else {
                $files.Add([IO.FileInfo]$entry)
            }
        }
    }
    return $files.ToArray()
}

function Get-CUEToolsVendorSourceFileRecords([string]$StageRoot) {
    $records = New-Object "Collections.Generic.List[object]"
    $prefix = [IO.Path]::GetFullPath($StageRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    foreach ($file in @(
        Get-CUEToolsOwnedTreeFiles `
            -Root $StageRoot `
            -Purpose "Vendor stage" |
            Sort-Object FullName)) {
        if (-not $file.FullName.StartsWith(
            $prefix,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Vendor stage enumeration escaped its root: $($file.FullName)"
        }
        $relativePath = $file.FullName.Substring($prefix.Length).
            Replace("\", "/")
        if ($relativePath -ceq ".cuetools-vendor-stage.json") {
            continue
        }
        $records.Add([pscustomobject]([ordered]@{
            path = $relativePath
            bytes = [long]$file.Length
            sha256 = Get-CUEToolsSha256 $file.FullName
        }))
    }
    return $records.ToArray()
}

function Get-CUEToolsVendorManifestDigest([object[]]$Files) {
    $lines = @($Files | ForEach-Object {
        "$([string]$_.path)`t$([long]$_.bytes)`t$([string]$_.sha256)"
    })
    return Get-CUEToolsTextSha256 (($lines -join "`n") + "`n")
}

function Test-CUEToolsGeneratedVendorPath([string]$RelativePath) {
    $path = $RelativePath.Replace("\", "/")
    return (
        $path -match "(^|/)(bin|obj)/" -or
        $path -match
            "^ThirdParty/flac/src/libFLAC/(Win32|x64|ThirdParty)/" -or
        $path -match
            "^ThirdParty/WavPack/(src|wavpackdll)/(Win32|x64|Debug|Release)/")
}

function Read-CUEToolsVendorStageManifest([string]$StageRoot) {
    $manifestPath = Join-Path $StageRoot ".cuetools-vendor-stage.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Vendor stage ownership manifest is missing: $manifestPath"
    }
    $info = Get-Item -LiteralPath $manifestPath -Force -ErrorAction Stop
    if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $info.Length -lt 2 -or $info.Length -gt 16MB) {
        throw "Vendor stage ownership manifest is not a bounded regular file."
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    if ([int]$manifest.schemaVersion -ne 1 -or
        [string]$manifest.kind -cne "cuetools-vendor-source-stage" -or
        [string]$manifest.identitySha256 -cnotmatch "^[0-9A-F]{64}$" -or
        [string]$manifest.sourceManifestSha256 -cnotmatch
            "^[0-9A-F]{64}$" -or
        [int]$manifest.sourceFileCount -ne @($manifest.files).Count -or
        @($manifest.vendors).Count -ne 4) {
        throw "Vendor stage ownership manifest has an invalid shape."
    }
    return $manifest
}

function Assert-CUEToolsVendorStage(
    [string]$StageRoot,
    [object[]]$VendorRecords) {
    if (-not (Test-Path -LiteralPath $StageRoot -PathType Container)) {
        throw "Vendor source stage is missing: $StageRoot"
    }
    $stageParent = [IO.Path]::GetDirectoryName(
        [IO.Path]::GetFullPath($StageRoot))
    Assert-CUEToolsNoReparsePoint `
        -Path $StageRoot `
        -StopAt $stageParent `
        -Purpose "Vendor source stage"
    $manifest = Read-CUEToolsVendorStageManifest $StageRoot
    $identityLines = @($VendorRecords | ForEach-Object {
        "$([string]$_.id)`t$([string]$_.path)`t$([string]$_.commit)`t" +
            "$([string]$_.patch)`t$([string]$_.patchSha256)"
    })
    $expectedIdentity = Get-CUEToolsTextSha256 (
        "schema=1`n" + ($identityLines -join "`n") + "`n")
    if ([string]$manifest.identitySha256 -cne $expectedIdentity) {
        throw "Vendor source stage identity does not match the pinned inputs."
    }
    $manifestVendors = @($manifest.vendors | ForEach-Object {
        "$([string]$_.id)`t$([string]$_.path)`t$([string]$_.commit)`t" +
            "$([string]$_.patch)`t$([string]$_.patchSha256)"
    })
    if (($manifestVendors -join "`n") -cne ($identityLines -join "`n")) {
        throw "Vendor source stage input records do not match the pinned inputs."
    }

    $stagePrefix = [IO.Path]::GetFullPath($StageRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $knownPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $verified = New-Object "Collections.Generic.List[object]"
    foreach ($record in @($manifest.files)) {
        $relativePath = [string]$record.path
        if ([string]::IsNullOrWhiteSpace($relativePath) -or
            $relativePath.Contains("\") -or
            [IO.Path]::IsPathRooted($relativePath) -or
            -not $knownPaths.Add($relativePath)) {
            throw "Vendor stage manifest contains an invalid or duplicate source path."
        }
        $fullPath = [IO.Path]::GetFullPath(
            (Join-Path $StageRoot $relativePath))
        if (-not $fullPath.StartsWith(
            $stagePrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Vendor stage source is missing or escapes its root: $relativePath"
        }
        $info = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
        if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            [long]$info.Length -ne [long]$record.bytes -or
            (Get-CUEToolsSha256 $fullPath) -cne [string]$record.sha256) {
            throw "Vendor stage source differs from its ownership manifest: $relativePath"
        }
        $verified.Add([pscustomobject]([ordered]@{
            path = $relativePath
            bytes = [long]$record.bytes
            sha256 = [string]$record.sha256
        }))
    }
    $digest = Get-CUEToolsVendorManifestDigest $verified.ToArray()
    if ($digest -cne [string]$manifest.sourceManifestSha256) {
        throw "Vendor stage source manifest digest is stale."
    }

    foreach ($file in @(
        Get-CUEToolsOwnedTreeFiles `
            -Root $StageRoot `
            -Purpose "Vendor source stage")) {
        $relativePath = $file.FullName.Substring($stagePrefix.Length).
            Replace("\", "/")
        if ($relativePath -ceq ".cuetools-vendor-stage.json" -or
            $knownPaths.Contains($relativePath)) {
            continue
        }
        if (-not (Test-CUEToolsGeneratedVendorPath $relativePath)) {
            throw "Vendor stage contains unowned source-shaped data: $relativePath"
        }
    }

    return [pscustomobject]([ordered]@{
        schemaVersion = 1
        path = "obj/vendor-sources/current"
        identitySha256 = [string]$manifest.identitySha256
        sourceFileCount = [int]$manifest.sourceFileCount
        sourceManifestSha256 = [string]$manifest.sourceManifestSha256
        vendors = @($manifest.vendors)
    })
}

function Enter-CUEToolsVendorStageLease(
    [string]$StageParent,
    [int]$TimeoutMilliseconds) {
    if ($TimeoutMilliseconds -lt 1) {
        throw "Vendor stage lease timeout must be positive."
    }
    [void][IO.Directory]::CreateDirectory($StageParent)
    $leasePath = Join-Path $StageParent ".prepare.lock"
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    while ($true) {
        try {
            return New-Object IO.FileStream(
                $leasePath,
                [IO.FileMode]::OpenOrCreate,
                [IO.FileAccess]::ReadWrite,
                [IO.FileShare]::None)
        }
        catch [IO.IOException] {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "Timed out waiting for the vendor source staging lease."
            }
            Start-Sleep -Milliseconds 100
        }
    }
}

function Initialize-CUEToolsVendorSources {
    [CmdletBinding()]
    param(
        [string]$RepositoryRoot,
        [string]$StagingRoot,
        [int]$LeaseTimeoutMilliseconds = 30000
    )

    $root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
    } else {
        [IO.Path]::GetFullPath($RepositoryRoot)
    }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Repository root does not exist: $root"
    }
    $ownedParent = [IO.Path]::GetFullPath(
        (Join-Path $root "obj\vendor-sources"))
    $stage = if ([string]::IsNullOrWhiteSpace($StagingRoot)) {
        Join-Path $ownedParent "current"
    } else {
        Assert-CUEToolsPathWithin `
            -Candidate $StagingRoot `
            -Parent $ownedParent `
            -Purpose "Vendor source stage"
    }
    $stage = [IO.Path]::GetFullPath($stage)
    if (-not [string]::Equals(
        $stage,
        (Join-Path $ownedParent "current"),
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Vendor source staging currently supports only its owned current path."
    }
    Assert-CUEToolsNoReparsePoint `
        -Path $ownedParent `
        -StopAt $root `
        -Purpose "Vendor source staging root"

    $definitions = @(Get-CUEToolsVendorDefinitions $root)
    $vendorRecords = @(
        Get-CUEToolsPinnedVendorRecords `
            -Root $root `
            -Definitions $definitions)
    $lease = Enter-CUEToolsVendorStageLease `
        -StageParent $ownedParent `
        -TimeoutMilliseconds $LeaseTimeoutMilliseconds
    $candidate = $null
    try {
        if (Test-Path -LiteralPath $stage -PathType Container) {
            try {
                $existing = Assert-CUEToolsVendorStage `
                    -StageRoot $stage `
                    -VendorRecords $vendorRecords
                Write-Host (
                    "Vendor source stage already current: " +
                    "$($existing.sourceFileCount) files, " +
                    "$($existing.identitySha256)")
                return $existing
            }
            catch {
                Write-Host (
                    "Vendor source stage will be replaced because validation failed: " +
                    $_.Exception.Message)
            }
        }

        $candidate = Join-Path $ownedParent (
            "candidate-" + [Guid]::NewGuid().ToString("N"))
        [void][IO.Directory]::CreateDirectory(
            (Join-Path $candidate "ThirdParty"))
        foreach ($vendor in $vendorRecords) {
            $archivePath = Join-Path $candidate (
                [string]$vendor.id + ".zip")
            & git -C ([string]$vendor.sourcePath) archive `
                --format=zip `
                "--output=$archivePath" `
                ([string]$vendor.commit)
            if ($LASTEXITCODE -ne 0 -or
                -not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
                throw "Unable to archive pinned vendor source: $($vendor.path)"
            }
            $destination = Join-Path $candidate (
                ([string]$vendor.path).Replace(
                    "/",
                    [IO.Path]::DirectorySeparatorChar))
            Expand-CUEToolsGitArchive `
                -ArchivePath $archivePath `
                -DestinationPath $destination
            [IO.File]::Delete($archivePath)

            $candidateRelative = $candidate.Substring(
                $root.TrimEnd(
                    [IO.Path]::DirectorySeparatorChar,
                    [IO.Path]::AltDirectorySeparatorChar).Length + 1).
                Replace("\", "/")
            $patchDirectory = "$candidateRelative/$($vendor.path)"
            & git -C $root apply `
                --whitespace=nowarn `
                "--directory=$patchDirectory" `
                ([string]$vendor.patchPath)
            if ($LASTEXITCODE -ne 0) {
                throw "Unable to apply checked vendor patch in the source stage: $($vendor.patch)"
            }
        }

        $files = @(Get-CUEToolsVendorSourceFileRecords $candidate)
        $identityLines = @($vendorRecords | ForEach-Object {
            "$([string]$_.id)`t$([string]$_.path)`t$([string]$_.commit)`t" +
                "$([string]$_.patch)`t$([string]$_.patchSha256)"
        })
        $identity = Get-CUEToolsTextSha256 (
            "schema=1`n" + ($identityLines -join "`n") + "`n")
        $manifest = [ordered]@{
            schemaVersion = 1
            kind = "cuetools-vendor-source-stage"
            identitySha256 = $identity
            sourceFileCount = $files.Count
            sourceManifestSha256 =
                Get-CUEToolsVendorManifestDigest $files
            vendors = @($vendorRecords | ForEach-Object {
                [ordered]@{
                    id = [string]$_.id
                    path = [string]$_.path
                    commit = [string]$_.commit
                    patch = [string]$_.patch
                    patchSha256 = [string]$_.patchSha256
                }
            })
            files = $files
        }
        $manifestPath = Join-Path $candidate (
            ".cuetools-vendor-stage.json")
        $utf8 = New-Object Text.UTF8Encoding($false)
        [IO.File]::WriteAllText(
            $manifestPath,
            (($manifest | ConvertTo-Json -Depth 8) + "`n"),
            $utf8)
        $candidateRecord = Assert-CUEToolsVendorStage `
            -StageRoot $candidate `
            -VendorRecords $vendorRecords

        $quarantine = $null
        if (Test-Path -LiteralPath $stage) {
            Assert-CUEToolsNoReparsePoint `
                -Path $stage `
                -StopAt $ownedParent `
                -Purpose "Prior vendor source stage"
            $quarantine = Join-Path $ownedParent (
                "quarantine-" + [Guid]::NewGuid().ToString("N"))
            Move-Item -LiteralPath $stage -Destination $quarantine
        }
        try {
            Move-Item -LiteralPath $candidate -Destination $stage
            $candidate = $null
        }
        catch {
            if ($quarantine -ne $null -and
                -not (Test-Path -LiteralPath $stage) -and
                (Test-Path -LiteralPath $quarantine)) {
                Move-Item -LiteralPath $quarantine -Destination $stage
            }
            throw
        }

        $published = Assert-CUEToolsVendorStage `
            -StageRoot $stage `
            -VendorRecords $vendorRecords
        if ($quarantine -ne $null) {
            Write-Host "Retained the replaced vendor stage for recovery: $quarantine"
        }
        Write-Host (
            "Prepared vendor source stage: " +
            "$($published.sourceFileCount) files, " +
            "$($published.identitySha256)")

        foreach ($vendor in $vendorRecords) {
            $status = @(
                & git -C ([string]$vendor.sourcePath) status `
                    --porcelain=v2 `
                    --untracked-files=all)
            if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
                throw "Vendor source staging changed a pinned submodule: $($vendor.path)"
            }
        }
        return $published
    }
    finally {
        if ($candidate -ne $null -and
            (Test-Path -LiteralPath $candidate)) {
            Assert-CUEToolsPathWithin `
                -Candidate $candidate `
                -Parent $ownedParent `
                -Purpose "Failed vendor stage candidate" | Out-Null
            Assert-CUEToolsNoReparsePoint `
                -Path $candidate `
                -StopAt $ownedParent `
                -Purpose "Failed vendor stage candidate"
            Remove-Item -LiteralPath $candidate -Recurse -Force
        }
        $lease.Dispose()
    }
}

function Get-CUEToolsVendorSourceIdentity {
    [CmdletBinding()]
    param(
        [string]$RepositoryRoot,
        [string]$StagingRoot
    )
    $root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
    } else {
        [IO.Path]::GetFullPath($RepositoryRoot)
    }
    $ownedParent = [IO.Path]::GetFullPath(
        (Join-Path $root "obj\vendor-sources"))
    $stage = if ([string]::IsNullOrWhiteSpace($StagingRoot)) {
        Join-Path $ownedParent "current"
    } else {
        Assert-CUEToolsPathWithin `
            -Candidate $StagingRoot `
            -Parent $ownedParent `
            -Purpose "Vendor source stage"
    }
    $definitions = @(Get-CUEToolsVendorDefinitions $root)
    $records = @(
        Get-CUEToolsPinnedVendorRecords `
            -Root $root `
            -Definitions $definitions)
    return Assert-CUEToolsVendorStage `
        -StageRoot $stage `
        -VendorRecords $records
}
