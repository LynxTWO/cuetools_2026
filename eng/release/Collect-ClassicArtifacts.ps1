[CmdletBinding()]
param(
    [string]$BuildReceiptPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

. (Join-Path $PSScriptRoot "ReleaseSafety.ps1")
. (Join-Path $PSScriptRoot "New-ClassicBuildReceipt.ps1")

function Resolve-ContainedCollectionPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $normalized = ConvertTo-ClassicRelativePath `
        -RelativePath $RelativePath `
        -Purpose $Purpose
    $segments = $normalized.Split("/")

    $fullRoot = [IO.Path]::GetFullPath($Root)
    $fullPath = [IO.Path]::GetFullPath((Join-Path $fullRoot (
        $segments -join [IO.Path]::DirectorySeparatorChar)))
    if (-not (Test-SameOrDescendantPath `
        -CandidatePath $fullPath `
        -RootPath $fullRoot) -or
        [string]::Equals(
            $fullPath,
            $fullRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose escapes its root: '$RelativePath'."
    }
    return $fullPath
}

function Get-CollectionRelativePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = $fullRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith(
        $prefix,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Artifact file escapes its root: $fullPath"
    }
    return $fullPath.Substring($prefix.Length).Replace("\", "/")
}

function Assert-ExactArtifactFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)]
        [string[]]$ExpectedRelativePaths
    )

    $expected = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($relativePath in $ExpectedRelativePaths) {
        $normalized = $relativePath.Replace("\", "/")
        if (-not $expected.Add($normalized)) {
            throw "Collection plan contains duplicate destination '$normalized'."
        }
    }

    $actual = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @(Get-VerifiedArtifactFiles -Root $ArtifactDirectory)) {
        $relativePath = Get-CollectionRelativePath `
            -Root $ArtifactDirectory `
            -Path $file.FullName
        if (-not $actual.Add($relativePath)) {
            throw "Artifact contains duplicate path '$relativePath'."
        }
    }

    $missing = @($expected | Where-Object { -not $actual.Contains($_) } | Sort-Object)
    $unexpected = @($actual | Where-Object { -not $expected.Contains($_) } | Sort-Object)
    if ($missing.Count -ne 0 -or $unexpected.Count -ne 0) {
        throw "Collected artifact differs from its exact plan: missing=[$($missing -join ', ')], " +
            "unexpected=[$($unexpected -join ', ')]."
    }
}

function Get-CollectionFileSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = $null
    try {
        $stream = [IO.File]::Open(
            $Path,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        return [BitConverter]::ToString(
            $algorithm.ComputeHash($stream)).Replace("-", "")
    }
    finally {
        if ($stream -ne $null) { $stream.Dispose() }
        $algorithm.Dispose()
    }
}

function Get-CollectionTextSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

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

function Get-ClassicArtifactControlPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)]
        [ValidateSet("owner.json", "publication.json")]
        [string]$Suffix
    )

    $artifactFullPath = [IO.Path]::GetFullPath($ArtifactDirectory)
    $releaseRoot = [IO.Path]::GetDirectoryName($artifactFullPath)
    $artifactName = [IO.Path]::GetFileName($artifactFullPath)
    Assert-SafeArtifactName -Name $artifactName
    return Join-Path $releaseRoot ("." + $artifactName + "." + $Suffix)
}

function Write-AtomicCollectionJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [object]$Value,
        [switch]$NoReplace
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetDirectoryName($fullPath)
    if ([string]::IsNullOrWhiteSpace($parent)) {
        throw "Collection control file has no parent directory: $fullPath"
    }
    Assert-NoReparsePointInExistingPath `
        -Path $parent `
        -Purpose "Collection control directory"
    if (Test-Path -LiteralPath $fullPath) {
        Assert-NoReparsePointInExistingPath `
            -Path $fullPath `
            -Purpose "Collection control file"
        $existing = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
        if ($existing.PSIsContainer -or
            -not ($existing -is [IO.FileInfo]) -or
            ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Collection control path is not a regular file: $fullPath"
        }
        if ($NoReplace) {
            throw "Collection control file already exists: $fullPath"
        }
    }

    $temporaryPath = Join-Path $parent (
        "." + [IO.Path]::GetFileName($fullPath) +
        ".tmp-" + [Guid]::NewGuid().ToString("N"))
    $stream = $null
    $writer = $null
    try {
        $stream = New-Object IO.FileStream(
            $temporaryPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        $writer = New-Object IO.StreamWriter(
            $stream,
            (New-Object Text.UTF8Encoding($false)),
            1024,
            $true)
        $writer.Write(($Value | ConvertTo-Json -Depth 12))
        $writer.Write("`n")
        $writer.Flush()
        $stream.Flush($true)
        $writer.Dispose()
        $writer = $null
        $stream.Dispose()
        $stream = $null

        if (Test-Path -LiteralPath $fullPath) {
            [IO.File]::Replace(
                $temporaryPath,
                $fullPath,
                [System.Management.Automation.Language.NullString]::Value)
        }
        else {
            [IO.File]::Move($temporaryPath, $fullPath)
        }
    }
    finally {
        if ($writer -ne $null) { $writer.Dispose() }
        if ($stream -ne $null) { $stream.Dispose() }
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Read-CollectionControlJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    return (Read-ClassicBuildJsonDocument `
        -Path $Path `
        -Purpose $Purpose `
        -MaximumBytes 64KB).value
}

function Remove-CollectionControlFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    if (-not (Test-Path -LiteralPath $Path)) { return }
    [void](Read-CollectionControlJson -Path $Path -Purpose $Purpose)
    [IO.File]::Delete([IO.Path]::GetFullPath($Path))
}

function Get-ClassicArtifactTreeIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $fullRoot = [IO.Path]::GetFullPath($Root)
    $records = New-Object "Collections.Generic.List[object]"
    $totalBytes = [long]0
    foreach ($file in @(
        Get-VerifiedArtifactFiles -Root $fullRoot |
            Sort-Object FullName)) {
        $relativePath = Get-CollectionRelativePath `
            -Root $fullRoot `
            -Path $file.FullName
        $hash = Get-CollectionFileSha256 -Path $file.FullName
        $totalBytes += [long]$file.Length
        $records.Add([pscustomobject]@{
            path = $relativePath
            bytes = [long]$file.Length
            sha256 = $hash
        })
    }

    $canonical = New-Object Text.StringBuilder
    foreach ($record in $records) {
        [void]$canonical.Append($record.path.Length)
        [void]$canonical.Append(":")
        [void]$canonical.Append($record.path)
        [void]$canonical.Append("|")
        [void]$canonical.Append($record.bytes)
        [void]$canonical.Append("|")
        [void]$canonical.Append($record.sha256)
        [void]$canonical.Append("`n")
    }
    return [pscustomobject]@{
        fileCount = $records.Count
        bytes = $totalBytes
        sha256 = Get-CollectionTextSha256 -Text $canonical.ToString()
    }
}

function Assert-ClassicToken {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Token,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    if ($Token -notmatch "^[0-9a-f]{32}$") {
        throw "$Purpose contains an invalid ownership token."
    }
}

function Assert-ActiveClassicArtifactLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Lease,
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)]
        [string]$LeaseToken
    )

    $releaseRoot = [IO.Path]::GetDirectoryName(
        [IO.Path]::GetFullPath($ArtifactDirectory))
    Assert-ActiveClassicReleaseLease `
        -Lease $Lease `
        -ReleaseRoot $releaseRoot `
        -LeaseToken $LeaseToken
}

function New-OwnedClassicArtifactStage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseRoot,
        [Parameter(Mandatory = $true)]
        [string]$ArtifactName,
        [Parameter(Mandatory = $true)]
        [string]$CollectionId
    )

    Assert-SafeArtifactName -Name $ArtifactName
    $releaseFullPath = [IO.Path]::GetFullPath($ReleaseRoot)
    Assert-NoReparsePointInExistingPath `
        -Path $releaseFullPath `
        -Purpose "Classic release root"
    $token = [Guid]::NewGuid().ToString("N")
    $stageName = "." + $ArtifactName + ".stage-" + $token
    $stagePath = Join-Path $releaseFullPath $stageName
    $receiptPath = $stagePath + ".owner.json"
    if ((Test-Path -LiteralPath $stagePath) -or
        (Test-Path -LiteralPath $receiptPath)) {
        throw "Generated classic stage identity already exists: $stageName"
    }

    New-Item -ItemType Directory -Path $stagePath | Out-Null
    try {
        Write-AtomicCollectionJson `
            -Path $receiptPath `
            -NoReplace `
            -Value ([ordered]@{
                schemaVersion = 1
                kind = "cuetools-classic-stage-owner"
                collectionId = $CollectionId
                artifactName = $ArtifactName
                stageName = $stageName
                ownerToken = $token
                sealed = $false
            })
    }
    catch {
        if (Test-Path -LiteralPath $stagePath -PathType Container) {
            [IO.Directory]::Delete($stagePath)
        }
        throw
    }
    return [pscustomobject]@{
        path = $stagePath
        receiptPath = $receiptPath
        token = $token
        collectionId = $CollectionId
        artifactName = $ArtifactName
    }
}

function Seal-OwnedClassicArtifactStage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Stage
    )

    Assert-ClassicToken -Token ([string]$Stage.token) -Purpose "Classic stage"
    $receipt = Read-CollectionControlJson `
        -Path $Stage.receiptPath `
        -Purpose "Classic stage ownership receipt"
    if ([int]$receipt.schemaVersion -ne 1 -or
        [string]$receipt.kind -ne "cuetools-classic-stage-owner" -or
        [string]$receipt.collectionId -ne [string]$Stage.collectionId -or
        [string]$receipt.artifactName -ne [string]$Stage.artifactName -or
        [string]$receipt.stageName -ne [IO.Path]::GetFileName($Stage.path) -or
        [string]$receipt.ownerToken -cne [string]$Stage.token) {
        throw "Classic stage ownership receipt does not match its exact stage token."
    }
    $tree = Get-ClassicArtifactTreeIdentity -Root $Stage.path
    Write-AtomicCollectionJson `
        -Path $Stage.receiptPath `
        -Value ([ordered]@{
            schemaVersion = 1
            kind = "cuetools-classic-stage-owner"
            collectionId = [string]$Stage.collectionId
            artifactName = [string]$Stage.artifactName
            stageName = [IO.Path]::GetFileName($Stage.path)
            ownerToken = [string]$Stage.token
            sealed = $true
            treeSha256 = $tree.sha256
            fileCount = $tree.fileCount
            bytes = $tree.bytes
        })
    return $tree
}

function Assert-OwnedClassicArtifactStage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Stage,
        [switch]$RequireSealed,
        [switch]$AllowMissingDirectory
    )

    Assert-ClassicToken -Token ([string]$Stage.token) -Purpose "Classic stage"
    $stageFullPath = [IO.Path]::GetFullPath([string]$Stage.path)
    $receiptFullPath = [IO.Path]::GetFullPath([string]$Stage.receiptPath)
    Assert-SafeArtifactName -Name ([string]$Stage.artifactName)
    $expectedStageName = "." + [string]$Stage.artifactName +
        ".stage-" + [string]$Stage.token
    if ([IO.Path]::GetFileName($stageFullPath) -cne $expectedStageName) {
        throw "Classic stage path is not bound to its exact stage token."
    }
    if (-not [string]::Equals(
        $receiptFullPath,
        $stageFullPath + ".owner.json",
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Classic stage receipt is not the exact sibling for its stage."
    }
    $receipt = Read-CollectionControlJson `
        -Path $receiptFullPath `
        -Purpose "Classic stage ownership receipt"
    if ([int]$receipt.schemaVersion -ne 1 -or
        [string]$receipt.kind -ne "cuetools-classic-stage-owner" -or
        [string]$receipt.collectionId -ne [string]$Stage.collectionId -or
        [string]$receipt.artifactName -ne [string]$Stage.artifactName -or
        [string]$receipt.stageName -ne [IO.Path]::GetFileName($stageFullPath) -or
        [string]$receipt.ownerToken -cne [string]$Stage.token) {
        throw "Classic stage ownership receipt does not match its exact stage token."
    }
    if ($RequireSealed -and -not [bool]$receipt.sealed) {
        throw "Classic stage has not been sealed to an exact tree."
    }
    $stageExists = Test-Path -LiteralPath $stageFullPath -PathType Container
    if (-not $stageExists -and -not $AllowMissingDirectory) {
        throw "Classic stage directory is missing: $stageFullPath"
    }
    if ([bool]$receipt.sealed -and $stageExists) {
        $tree = Get-ClassicArtifactTreeIdentity -Root $stageFullPath
        if ([string]$receipt.treeSha256 -cne [string]$tree.sha256 -or
            [int]$receipt.fileCount -ne [int]$tree.fileCount -or
            [long]$receipt.bytes -ne [long]$tree.bytes) {
            throw "Classic stage changed after its exact tree was sealed."
        }
    }
    elseif ([bool]$receipt.sealed) {
        $tree = [pscustomobject]@{
            sha256 = [string]$receipt.treeSha256
            fileCount = [int]$receipt.fileCount
            bytes = [long]$receipt.bytes
        }
    }
    return [pscustomobject]@{
        receipt = $receipt
        tree = $(if ([bool]$receipt.sealed) { $tree } else { $null })
    }
}

function Remove-OwnedClassicArtifactStage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Stage
    )

    $ownership = Assert-OwnedClassicArtifactStage `
        -Stage $Stage `
        -AllowMissingDirectory
    if (Test-Path -LiteralPath $Stage.path) {
        [void](Get-VerifiedArtifactFiles -Root $Stage.path)
        Remove-Item -LiteralPath $Stage.path -Recurse -Force
    }
    Remove-CollectionControlFile `
        -Path $Stage.receiptPath `
        -Purpose "Classic stage ownership receipt"
}

function Assert-OwnedClassicArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)]
        [string]$CollectionId,
        [string]$ExpectedOwnerToken,
        [string]$ExpectedTreeSha256
    )

    $artifactFullPath = [IO.Path]::GetFullPath($ArtifactDirectory)
    $artifactName = [IO.Path]::GetFileName($artifactFullPath)
    $receiptPath = Get-ClassicArtifactControlPath `
        -ArtifactDirectory $artifactFullPath `
        -Suffix "owner.json"
    if (-not (Test-Path -LiteralPath $artifactFullPath -PathType Container)) {
        throw "Classic artifact destination does not exist: $artifactFullPath"
    }
    if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
        throw "Refusing to replace unowned classic artifact destination: $artifactFullPath"
    }
    $receipt = Read-CollectionControlJson `
        -Path $receiptPath `
        -Purpose "Classic artifact ownership receipt"
    Assert-ClassicToken `
        -Token ([string]$receipt.ownerToken) `
        -Purpose "Classic artifact ownership receipt"
    if ([int]$receipt.schemaVersion -ne 1 -or
        [string]$receipt.kind -ne "cuetools-classic-artifact-owner" -or
        [string]$receipt.collectionId -ne $CollectionId -or
        [string]$receipt.artifactName -ne $artifactName) {
        throw "Classic artifact ownership receipt does not authorize this destination."
    }
    $tree = Get-ClassicArtifactTreeIdentity -Root $artifactFullPath
    if ([string]$receipt.treeSha256 -cne [string]$tree.sha256 -or
        [int]$receipt.fileCount -ne [int]$tree.fileCount -or
        [long]$receipt.bytes -ne [long]$tree.bytes) {
        throw "Classic artifact destination changed after its ownership receipt was written."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedOwnerToken) -and
        [string]$receipt.ownerToken -cne $ExpectedOwnerToken) {
        throw "Classic artifact ownership token does not match the expected publication."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedTreeSha256) -and
        [string]$tree.sha256 -cne $ExpectedTreeSha256) {
        throw "Classic artifact tree does not match the expected publication."
    }
    return [pscustomobject]@{
        receiptPath = $receiptPath
        receipt = $receipt
        tree = $tree
    }
}

function Write-ClassicArtifactOwnerReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)]
        [string]$CollectionId,
        [Parameter(Mandatory = $true)]
        [string]$OwnerToken,
        [Parameter(Mandatory = $true)]
        [object]$Tree,
        [Parameter(Mandatory = $true)]
        [string]$SourceIdentity
    )

    Assert-ClassicToken -Token $OwnerToken -Purpose "Classic artifact"
    $receiptPath = Get-ClassicArtifactControlPath `
        -ArtifactDirectory $ArtifactDirectory `
        -Suffix "owner.json"
    Write-AtomicCollectionJson `
        -Path $receiptPath `
        -Value ([ordered]@{
            schemaVersion = 1
            kind = "cuetools-classic-artifact-owner"
            collectionId = $CollectionId
            artifactName = [IO.Path]::GetFileName(
                [IO.Path]::GetFullPath($ArtifactDirectory))
            ownerToken = $OwnerToken
            treeSha256 = [string]$Tree.sha256
            fileCount = [int]$Tree.fileCount
            bytes = [long]$Tree.bytes
            collectionSourceIdentity = $SourceIdentity
        })
    return $receiptPath
}

function Remove-TokenBoundClassicDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedParent,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedLeaf,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedTreeSha256,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not [string]::Equals(
        [IO.Path]::GetDirectoryName($fullPath),
        [IO.Path]::GetFullPath($ExpectedParent),
        [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($fullPath) -cne $ExpectedLeaf) {
        throw "Refusing to remove a $Purpose with an unexpected identity: $fullPath"
    }
    $tree = Get-ClassicArtifactTreeIdentity -Root $fullPath
    if ([string]$tree.sha256 -cne $ExpectedTreeSha256) {
        throw "Refusing to remove a $Purpose whose exact tree changed: $fullPath"
    }
    Remove-Item -LiteralPath $fullPath -Recurse -Force
}

function Copy-CollectionFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$StageDirectory,
        [Parameter(Mandatory = $true)]
        [string]$SourceRelativePath,
        [Parameter(Mandatory = $true)]
        [string]$DestinationRelativePath,
        [Parameter(Mandatory = $true)]
        [object]$ExpectedInputRecord
    )

    Assert-ClassicExactProperties `
        -Value $ExpectedInputRecord `
        -Expected @("path", "bytes", "sha256", "freshBuildOutput") `
        -Purpose "Receipted collection input"
    $normalizedSource = ConvertTo-ClassicRelativePath `
        -RelativePath $SourceRelativePath `
        -Purpose "Collection source"
    if ([string]$ExpectedInputRecord.path -cne $normalizedSource) {
        throw "Collection source does not match its receipted path: $SourceRelativePath"
    }
    Assert-ClassicSha256 `
        -Value ([string]$ExpectedInputRecord.sha256) `
        -Purpose "Receipted collection input hash"
    $sourcePath = Resolve-ContainedCollectionPath `
        -Root $RepositoryRoot `
        -RelativePath $normalizedSource `
        -Purpose "Collection source"
    Assert-NoReparsePointInExistingPath `
        -Path $sourcePath `
        -Purpose "Collection source"
    $sourceInfo = Get-Item -LiteralPath $sourcePath -Force -ErrorAction Stop
    if ($sourceInfo.PSIsContainer -or
        -not ($sourceInfo -is [IO.FileInfo]) -or
        ($sourceInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Collection source must be a regular file: $sourcePath"
    }

    $destinationPath = Resolve-ContainedCollectionPath `
        -Root $StageDirectory `
        -RelativePath $DestinationRelativePath `
        -Purpose "Collection destination"
    if (Test-Path -LiteralPath $destinationPath) {
        throw "Collection destination was already populated: $destinationPath"
    }
    $destinationParent = [IO.Path]::GetDirectoryName($destinationPath)
    if (-not (Test-Path -LiteralPath $destinationParent -PathType Container)) {
        New-Item -ItemType Directory -Path $destinationParent | Out-Null
    }
    Assert-NoReparsePointInExistingPath `
        -Path $destinationParent `
        -Purpose "Collection destination"

    $sourceStream = $null
    $destinationStream = $null
    $algorithm = $null
    $destinationCreated = $false
    try {
        # The same deny-write/delete source handle supplies both the staged bytes and
        # their digest. This closes the three-open A-to-B-to-A race and binds the copy
        # directly to the complete build receipt.
        $sourceStream = New-Object IO.FileStream(
            $sourcePath,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        if ([long]$sourceStream.Length -ne
            [long]$ExpectedInputRecord.bytes) {
            throw "Collection source length differs from its build receipt: $SourceRelativePath"
        }
        $destinationStream = New-Object IO.FileStream(
            $destinationPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        $destinationCreated = $true
        $algorithm = [Security.Cryptography.SHA256]::Create()
        $buffer = New-Object byte[] 131072
        while (($read = $sourceStream.Read(
            $buffer,
            0,
            $buffer.Length)) -gt 0) {
            [void]$algorithm.TransformBlock(
                $buffer,
                0,
                $read,
                $buffer,
                0)
            $destinationStream.Write($buffer, 0, $read)
        }
        [void]$algorithm.TransformFinalBlock(
            (New-Object byte[] 0),
            0,
            0)
        $copiedHash = [BitConverter]::ToString(
            $algorithm.Hash).Replace("-", "")
        if ($copiedHash -cne [string]$ExpectedInputRecord.sha256) {
            throw "Collection source hash differs from its build receipt: $SourceRelativePath"
        }
        $destinationStream.Flush($true)
    }
    catch {
        if ($destinationStream -ne $null) {
            $destinationStream.Dispose()
            $destinationStream = $null
        }
        if ($destinationCreated -and
            (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
            [IO.File]::Delete($destinationPath)
        }
        throw
    }
    finally {
        if ($algorithm -ne $null) { $algorithm.Dispose() }
        if ($destinationStream -ne $null) {
            $destinationStream.Dispose()
        }
        if ($sourceStream -ne $null) { $sourceStream.Dispose() }
    }
}

function Get-ClassicPublicationJournal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory
    )

    return Get-ClassicArtifactControlPath `
        -ArtifactDirectory $ArtifactDirectory `
        -Suffix "publication.json"
}

function Assert-ClassicPublicationJournal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Journal,
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)]
        [string]$CollectionId
    )

    $artifactFullPath = [IO.Path]::GetFullPath($ArtifactDirectory)
    $artifactName = [IO.Path]::GetFileName($artifactFullPath)
    Assert-ClassicToken `
        -Token ([string]$Journal.operationToken) `
        -Purpose "Classic publication journal"
    Assert-ClassicToken `
        -Token ([string]$Journal.stageToken) `
        -Purpose "Classic publication journal stage"
    if ([int]$Journal.schemaVersion -ne 1 -or
        [string]$Journal.kind -ne "cuetools-classic-publication-recovery" -or
        [string]$Journal.collectionId -ne $CollectionId -or
        [string]$Journal.artifactName -ne $artifactName -or
        [string]$Journal.stageName -cne (
            "." + $artifactName + ".stage-" + [string]$Journal.stageToken) -or
        [string]$Journal.backupName -cne (
            "." + $artifactName + ".backup-" +
            [string]$Journal.operationToken) -or
        [string]::IsNullOrWhiteSpace([string]$Journal.stageTreeSha256) -or
        [string]::IsNullOrWhiteSpace(
            [string]$Journal.collectionSourceIdentity)) {
        throw "Classic publication recovery journal has an invalid identity."
    }
    if ([bool]$Journal.priorPresent) {
        if ($Journal.PSObject.Properties["priorReceipt"] -eq $null -or
            $Journal.priorReceipt -eq $null -or
            [string]$Journal.priorTreeSha256 -cne
                [string]$Journal.priorReceipt.treeSha256 -or
            [string]$Journal.priorOwnerToken -cne
                [string]$Journal.priorReceipt.ownerToken) {
            throw "Classic publication recovery journal has an invalid prior receipt."
        }
        Assert-ClassicToken `
            -Token ([string]$Journal.priorOwnerToken) `
            -Purpose "Classic publication journal prior artifact"
    }
}

function New-StageObjectFromJournal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Journal,
        [Parameter(Mandatory = $true)]
        [string]$ReleaseRoot
    )

    $stagePath = Join-Path $ReleaseRoot ([string]$Journal.stageName)
    return [pscustomobject]@{
        path = $stagePath
        receiptPath = $stagePath + ".owner.json"
        token = [string]$Journal.stageToken
        collectionId = [string]$Journal.collectionId
        artifactName = [string]$Journal.artifactName
    }
}

function Repair-ClassicArtifactPublication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)]
        [string]$CollectionId,
        [Parameter(Mandatory = $true)]
        [object]$Lease,
        [Parameter(Mandatory = $true)]
        [string]$LeaseToken
    )

    Assert-ActiveClassicArtifactLease `
        -Lease $Lease `
        -ArtifactDirectory $ArtifactDirectory `
        -LeaseToken $LeaseToken
    $artifactFullPath = [IO.Path]::GetFullPath($ArtifactDirectory)
    $releaseRoot = [IO.Path]::GetDirectoryName($artifactFullPath)
    $journalPath = Get-ClassicPublicationJournal `
        -ArtifactDirectory $artifactFullPath
    if (-not (Test-Path -LiteralPath $journalPath)) {
        return "NoRecoveryNeeded"
    }

    $journal = Read-CollectionControlJson `
        -Path $journalPath `
        -Purpose "Classic publication recovery journal"
    Assert-ClassicPublicationJournal `
        -Journal $journal `
        -ArtifactDirectory $artifactFullPath `
        -CollectionId $CollectionId
    $backupPath = Join-Path $releaseRoot ([string]$journal.backupName)
    $stage = New-StageObjectFromJournal `
        -Journal $journal `
        -ReleaseRoot $releaseRoot
    $artifactPathExists = Test-Path -LiteralPath $artifactFullPath
    $backupPathExists = Test-Path -LiteralPath $backupPath
    $stagePathExists = Test-Path -LiteralPath $stage.path
    if ($artifactPathExists -and
        -not (Test-Path -LiteralPath $artifactFullPath -PathType Container)) {
        throw "Recovery found a non-directory at the public artifact path. " +
            "The stage, backup, and journal were retained for manual inspection."
    }
    if ($backupPathExists -and
        -not (Test-Path -LiteralPath $backupPath -PathType Container)) {
        throw "Recovery found a non-directory at the token-bound backup path. " +
            "The artifact, stage, and journal were retained for manual inspection."
    }
    if ($stagePathExists -and
        -not (Test-Path -LiteralPath $stage.path -PathType Container)) {
        throw "Recovery found a non-directory at the token-bound stage path. " +
            "The artifact, backup, and journal were retained for manual inspection."
    }
    if ((Test-Path -LiteralPath $stage.receiptPath) -and
        -not (Test-Path -LiteralPath $stage.receiptPath -PathType Leaf)) {
        throw "Recovery found a non-file at the token-bound stage receipt path. " +
            "The artifact, backup, and journal were retained for manual inspection."
    }
    $artifactExists = $artifactPathExists
    $backupExists = $backupPathExists

    if ($artifactExists) {
        $artifactTree = Get-ClassicArtifactTreeIdentity -Root $artifactFullPath
        if ([string]$artifactTree.sha256 -ceq
            [string]$journal.stageTreeSha256) {
            # The validated stage reached the public name. Complete forward: the owner
            # receipt is the commit marker, then revalidate it before deleting any backup.
            [void](Write-ClassicArtifactOwnerReceipt `
                -ArtifactDirectory $artifactFullPath `
                -CollectionId $CollectionId `
                -OwnerToken ([string]$journal.operationToken) `
                -Tree $artifactTree `
                -SourceIdentity (
                    [string]$journal.collectionSourceIdentity))
            [void](Assert-OwnedClassicArtifact `
                -ArtifactDirectory $artifactFullPath `
                -CollectionId $CollectionId `
                -ExpectedOwnerToken ([string]$journal.operationToken) `
                -ExpectedTreeSha256 ([string]$journal.stageTreeSha256))
            if ($backupExists) {
                if (-not [bool]$journal.priorPresent) {
                    throw "Recovery found an unowned backup for a first publication."
                }
                Remove-TokenBoundClassicDirectory `
                    -Path $backupPath `
                    -ExpectedParent $releaseRoot `
                    -ExpectedLeaf ([string]$journal.backupName) `
                    -ExpectedTreeSha256 (
                        [string]$journal.priorTreeSha256) `
                    -Purpose "classic publication backup"
            }
            if (Test-Path -LiteralPath $stage.receiptPath -PathType Leaf) {
                Remove-OwnedClassicArtifactStage -Stage $stage
            }
            Remove-CollectionControlFile `
                -Path $journalPath `
                -Purpose "Classic publication recovery journal"
            return "CommittedPublishedStage"
        }

        if ([bool]$journal.priorPresent -and
            [string]$artifactTree.sha256 -ceq
                [string]$journal.priorTreeSha256) {
            if ($backupExists) {
                throw "Recovery found both the prior artifact and its backup; " +
                    "manual inspection is required and neither will be deleted."
            }
            Write-AtomicCollectionJson `
                -Path (Get-ClassicArtifactControlPath `
                    -ArtifactDirectory $artifactFullPath `
                    -Suffix "owner.json") `
                -Value $journal.priorReceipt
            [void](Assert-OwnedClassicArtifact `
                -ArtifactDirectory $artifactFullPath `
                -CollectionId $CollectionId `
                -ExpectedOwnerToken ([string]$journal.priorOwnerToken) `
                -ExpectedTreeSha256 ([string]$journal.priorTreeSha256))
            if (Test-Path -LiteralPath $stage.receiptPath -PathType Leaf) {
                Remove-OwnedClassicArtifactStage -Stage $stage
            }
            Remove-CollectionControlFile `
                -Path $journalPath `
                -Purpose "Classic publication recovery journal"
            return "ConfirmedPriorArtifact"
        }
        throw "Recovery cannot identify the published artifact tree; " +
            "the artifact, backup, and journal were retained for manual inspection."
    }

    if ([bool]$journal.priorPresent) {
        if (-not $backupExists) {
            throw "Recovery cannot find the prior token-bound backup. " +
                "The journal was retained for manual inspection."
        }
        $backupTree = Get-ClassicArtifactTreeIdentity -Root $backupPath
        if ([string]$backupTree.sha256 -cne
            [string]$journal.priorTreeSha256) {
            throw "Recovery backup changed after journaling. " +
                "The backup and journal were retained for manual inspection."
        }
        [IO.Directory]::Move($backupPath, $artifactFullPath)
        Write-AtomicCollectionJson `
            -Path (Get-ClassicArtifactControlPath `
                -ArtifactDirectory $artifactFullPath `
                -Suffix "owner.json") `
            -Value $journal.priorReceipt
        [void](Assert-OwnedClassicArtifact `
            -ArtifactDirectory $artifactFullPath `
            -CollectionId $CollectionId `
            -ExpectedOwnerToken ([string]$journal.priorOwnerToken) `
            -ExpectedTreeSha256 ([string]$journal.priorTreeSha256))
        if (Test-Path -LiteralPath $stage.receiptPath -PathType Leaf) {
            Remove-OwnedClassicArtifactStage -Stage $stage
        }
        Remove-CollectionControlFile `
            -Path $journalPath `
            -Purpose "Classic publication recovery journal"
        return "RestoredPriorBackup"
    }

    if ($backupExists) {
        throw "Recovery found an unowned backup for a first publication. " +
            "The backup and journal were retained for manual inspection."
    }
    if (Test-Path -LiteralPath $stage.receiptPath -PathType Leaf) {
        Remove-OwnedClassicArtifactStage -Stage $stage
    }
    Remove-CollectionControlFile `
        -Path $journalPath `
        -Purpose "Classic publication recovery journal"
    return "AbandonedUnpublishedStage"
}

function Publish-ValidatedArtifactStage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Stage,
        [Parameter(Mandatory = $true)]
        [string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)]
        [string]$CollectionId,
        [Parameter(Mandatory = $true)]
        [string]$SourceIdentity,
        [Parameter(Mandatory = $true)]
        [object]$Lease,
        [Parameter(Mandatory = $true)]
        [string]$LeaseToken,
        [ValidateSet(
            "None",
            "AfterPriorMove",
            "AfterStagePublish",
            "RollbackFailure",
            "RollbackFailureAfterStagePublish")]
        [string]$InjectFailureAt = "None"
    )

    Assert-ActiveClassicArtifactLease `
        -Lease $Lease `
        -ArtifactDirectory $ArtifactDirectory `
        -LeaseToken $LeaseToken
    $stageOwnership = Assert-OwnedClassicArtifactStage `
        -Stage $Stage `
        -RequireSealed
    $stageFullPath = [IO.Path]::GetFullPath([string]$Stage.path)
    $artifactFullPath = [IO.Path]::GetFullPath($ArtifactDirectory)
    $releaseRoot = [IO.Path]::GetDirectoryName($artifactFullPath)
    $artifactName = [IO.Path]::GetFileName($artifactFullPath)
    Assert-SafeArtifactName -Name $artifactName
    if ([string]$Stage.artifactName -ne $artifactName -or
        [string]$Stage.collectionId -ne $CollectionId -or
        -not [string]::Equals(
            [IO.Path]::GetDirectoryName($stageFullPath),
            $releaseRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "The validated stage is not the exact owned sibling for $artifactFullPath."
    }
    Assert-NoReparsePointInExistingPath `
        -Path $releaseRoot `
        -Purpose "Classic release root"

    $journalPath = Get-ClassicPublicationJournal `
        -ArtifactDirectory $artifactFullPath
    if (Test-Path -LiteralPath $journalPath) {
        throw "A classic publication recovery journal is still pending. " +
            "Run Repair-ClassicArtifactPublication under the collection lease first."
    }
    $ownerReceiptPath = Get-ClassicArtifactControlPath `
        -ArtifactDirectory $artifactFullPath `
        -Suffix "owner.json"
    $prior = $null
    if (Test-Path -LiteralPath $artifactFullPath) {
        $prior = Assert-OwnedClassicArtifact `
            -ArtifactDirectory $artifactFullPath `
            -CollectionId $CollectionId
    }
    elseif (Test-Path -LiteralPath $ownerReceiptPath) {
        throw "Refusing publication because an orphaned ownership receipt exists: " +
            $ownerReceiptPath
    }

    $operationToken = [Guid]::NewGuid().ToString("N")
    $backupName = "." + $artifactName + ".backup-" + $operationToken
    $backupPath = Join-Path $releaseRoot $backupName
    if (Test-Path -LiteralPath $backupPath) {
        throw "Generated classic backup identity already exists: $backupPath"
    }
    $journal = [ordered]@{
        schemaVersion = 1
        kind = "cuetools-classic-publication-recovery"
        collectionId = $CollectionId
        artifactName = $artifactName
        operationToken = $operationToken
        phase = "prepared"
        stageName = [IO.Path]::GetFileName($stageFullPath)
        stageToken = [string]$Stage.token
        stageTreeSha256 = [string]$stageOwnership.tree.sha256
        stageFileCount = [int]$stageOwnership.tree.fileCount
        stageBytes = [long]$stageOwnership.tree.bytes
        collectionSourceIdentity = $SourceIdentity
        backupName = $backupName
        priorPresent = $prior -ne $null
        priorOwnerToken = $(if ($prior -ne $null) {
            [string]$prior.receipt.ownerToken
        } else { $null })
        priorTreeSha256 = $(if ($prior -ne $null) {
            [string]$prior.tree.sha256
        } else { $null })
        priorReceipt = $(if ($prior -ne $null) {
            $prior.receipt
        } else { $null })
    }

    # Flush the journal before the first rename. This supports deterministic
    # process-crash recovery on the supported Windows filesystems. It does not claim
    # power-loss durability because Windows exposes no parent-directory fsync here.
    Write-AtomicCollectionJson `
        -Path $journalPath `
        -NoReplace `
        -Value $journal
    $committed = $false
    try {
        if ($prior -ne $null) {
            [IO.Directory]::Move($artifactFullPath, $backupPath)
        }
        if ($InjectFailureAt -eq "AfterPriorMove" -or
            $InjectFailureAt -eq "RollbackFailure") {
            throw "Injected failure after moving the prior classic artifact."
        }

        $journal.phase = "prior-moved"
        Write-AtomicCollectionJson -Path $journalPath -Value $journal
        [IO.Directory]::Move($stageFullPath, $artifactFullPath)
        if ($InjectFailureAt -eq "AfterStagePublish" -or
            $InjectFailureAt -eq "RollbackFailureAfterStagePublish") {
            throw "Injected failure after publishing the classic stage."
        }

        $journal.phase = "stage-published"
        Write-AtomicCollectionJson -Path $journalPath -Value $journal
        [void](Write-ClassicArtifactOwnerReceipt `
            -ArtifactDirectory $artifactFullPath `
            -CollectionId $CollectionId `
            -OwnerToken $operationToken `
            -Tree $stageOwnership.tree `
            -SourceIdentity $SourceIdentity)
        $committed = $true
    }
    catch {
        $publicationFailure = $_
        if ($committed) { throw }
        try {
            if ($InjectFailureAt -eq "RollbackFailure" -or
                $InjectFailureAt -eq "RollbackFailureAfterStagePublish") {
                throw "Injected rollback failure."
            }
            if (Test-Path -LiteralPath $artifactFullPath -PathType Container) {
                $publishedTree = Get-ClassicArtifactTreeIdentity `
                    -Root $artifactFullPath
                if ([string]$publishedTree.sha256 -cne
                    [string]$stageOwnership.tree.sha256) {
                    throw "Published destination changed before rollback."
                }
                if (Test-Path -LiteralPath $stageFullPath) {
                    throw "Owned stage path became occupied before rollback."
                }
                [IO.Directory]::Move($artifactFullPath, $stageFullPath)
            }
            if ($prior -ne $null) {
                if (-not (Test-Path -LiteralPath $backupPath -PathType Container) -or
                    (Test-Path -LiteralPath $artifactFullPath)) {
                    throw "Prior backup cannot be restored without clobbering."
                }
                $backupTree = Get-ClassicArtifactTreeIdentity -Root $backupPath
                if ([string]$backupTree.sha256 -cne
                    [string]$prior.tree.sha256) {
                    throw "Prior backup changed before rollback."
                }
                [IO.Directory]::Move($backupPath, $artifactFullPath)
                [void](Assert-OwnedClassicArtifact `
                    -ArtifactDirectory $artifactFullPath `
                    -CollectionId $CollectionId `
                    -ExpectedOwnerToken (
                        [string]$prior.receipt.ownerToken) `
                    -ExpectedTreeSha256 ([string]$prior.tree.sha256))
            }
            Remove-CollectionControlFile `
                -Path $journalPath `
                -Purpose "Classic publication recovery journal"
        }
        catch {
            throw "Classic artifact publication failed and rollback could not be proven. " +
                "The token-bound backup and publication journal were retained. " +
                "Re-run the collector to perform deterministic startup recovery before " +
                "manually changing either path. Publication error: " +
                $publicationFailure.Exception.Message + " Rollback error: " +
                $_.Exception.Message
        }
        throw $publicationFailure
    }

    # The owner receipt is the publication commit marker. Re-open and hash the public
    # destination before deleting the prior token-bound backup.
    [void](Assert-OwnedClassicArtifact `
        -ArtifactDirectory $artifactFullPath `
        -CollectionId $CollectionId `
        -ExpectedOwnerToken $operationToken `
        -ExpectedTreeSha256 ([string]$stageOwnership.tree.sha256))
    if ($prior -ne $null -and
        (Test-Path -LiteralPath $backupPath -PathType Container)) {
        Remove-TokenBoundClassicDirectory `
            -Path $backupPath `
            -ExpectedParent $releaseRoot `
            -ExpectedLeaf $backupName `
            -ExpectedTreeSha256 ([string]$prior.tree.sha256) `
            -Purpose "classic publication backup"
    }
    Remove-CollectionControlFile `
        -Path $journalPath `
        -Purpose "Classic publication recovery journal"
}

function Invoke-ClassicArtifactCollection {
    [CmdletBinding()]
    param(
        [string]$BuildReceiptPath,
        [Parameter(Mandatory = $true)]
        [object]$Lease,
        [Parameter(Mandatory = $true)]
        [string]$LeaseToken,
        [string]$TestToolRoot,
        [string]$RepositoryRootOverride,
        [string]$PlanPathOverride
    )

    if ([string]::IsNullOrWhiteSpace($RepositoryRootOverride)) {
        $repositoryRoot = [IO.Path]::GetFullPath(
            (Join-Path $PSScriptRoot "..\.."))
    }
    else {
        $repositoryRoot = [IO.Path]::GetFullPath($RepositoryRootOverride)
    }
    $releaseRoot = Join-Path $repositoryRoot "bin\Release"
    if ([string]::IsNullOrWhiteSpace($PlanPathOverride)) {
        $planPath = Join-Path $PSScriptRoot "classic-win.collection.json"
    }
    else {
        $planPath = [IO.Path]::GetFullPath($PlanPathOverride)
    }
    $contractPath = Join-Path $PSScriptRoot "classic-win.manifest.json"
    if ([string]::IsNullOrWhiteSpace($BuildReceiptPath)) {
        $BuildReceiptPath = Join-Path $repositoryRoot (
            "bin\Release\evidence\classic-build-inputs.v2.json")
    }
    $BuildReceiptPath = [IO.Path]::GetFullPath($BuildReceiptPath)
    $planContext = Get-ClassicCollectionPlan `
        -RepositoryRoot $repositoryRoot `
        -PlanPath $planPath
    $plan = $planContext.value
    $contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json

    if ($plan.schemaVersion -ne 2 -or
        [string]::IsNullOrWhiteSpace($plan.collectionId) -or
        [string]::IsNullOrWhiteSpace($plan.productVersion) -or
        @($plan.files).Count -eq 0 -or
        @($plan.generatedFiles).Count -eq 0) {
        throw "The classic collection plan is incomplete or unsupported."
    }
    if ($plan.productVersion -ne $contract.productVersion) {
        throw "Collection plan version '$($plan.productVersion)' does not match " +
            "artifact contract version '$($contract.productVersion)'."
    }
    $cueSheetSource = Get-Content -LiteralPath (
        Join-Path $repositoryRoot "CUETools.Processor\CUESheet.cs") -Raw
    $versionMatch = [regex]::Match(
        $cueSheetSource,
        'CUEToolsVersion\s*=\s*"(?<version>[^"]+)"')
    if (-not $versionMatch.Success -or
        $versionMatch.Groups["version"].Value -ne $plan.productVersion) {
        throw "Collection plan version does not match CUESheet.CUEToolsVersion."
    }

    Assert-NoReparsePointInExistingPath `
        -Path $repositoryRoot `
        -Purpose "Repository root"
    if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $releaseRoot | Out-Null
    }
    Assert-NoReparsePointInExistingPath `
        -Path $releaseRoot `
        -Purpose "Classic release root"

    $artifactName = "CUETools_" + $plan.productVersion
    Assert-SafeArtifactName -Name $artifactName
    $artifactDirectory = Join-Path $releaseRoot $artifactName
    $stage = $null
    try {
        Assert-ActiveClassicArtifactLease `
            -Lease $Lease `
            -ArtifactDirectory $artifactDirectory `
            -LeaseToken $LeaseToken
        [void](Repair-ClassicArtifactPublication `
            -ArtifactDirectory $artifactDirectory `
            -CollectionId ([string]$plan.collectionId) `
            -Lease $Lease `
            -LeaseToken $LeaseToken)

        $ownerReceiptPath = Get-ClassicArtifactControlPath `
            -ArtifactDirectory $artifactDirectory `
            -Suffix "owner.json"
        if (Test-Path -LiteralPath $artifactDirectory) {
            [void](Assert-OwnedClassicArtifact `
                -ArtifactDirectory $artifactDirectory `
                -CollectionId ([string]$plan.collectionId))
        }
        elseif (Test-Path -LiteralPath $ownerReceiptPath) {
            throw "Refusing collection because an orphaned classic artifact " +
                "ownership receipt exists: $ownerReceiptPath"
        }

        $buildReceipt = Assert-ClassicBuildReceipt `
            -RepositoryRoot $repositoryRoot `
            -PlanPath $planPath `
            -ReceiptPath $BuildReceiptPath `
            -Configuration "Release" `
            -Platforms @("Any CPU", "x64", "Win32") `
            -TestToolRoot $TestToolRoot `
            -Lease $Lease `
            -LeaseToken $LeaseToken
        $sourceIdentity = [string]$buildReceipt.buildId + ":" +
            [string]$buildReceipt.receiptContentSha256
        $receiptedInputs =
            New-Object "Collections.Generic.Dictionary[string,object]" (
                [StringComparer]::OrdinalIgnoreCase)
        foreach ($record in @($buildReceipt.collectionInputs)) {
            $path = [string]$record.path
            if ($receiptedInputs.ContainsKey($path)) {
                if (-not (Test-ClassicJsonEquivalent `
                    -Left $receiptedInputs[$path] `
                    -Right $record)) {
                    throw "Classic build receipt has conflicting duplicate input records: $path"
                }
                continue
            }
            $receiptedInputs.Add($path, $record)
        }

        $stage = New-OwnedClassicArtifactStage `
            -ReleaseRoot $releaseRoot `
            -ArtifactName $artifactName `
            -CollectionId ([string]$plan.collectionId)
        $stageDirectory = [string]$stage.path
        $expectedPaths = New-Object "Collections.Generic.List[string]"
        foreach ($entry in @($plan.files)) {
            $expectedPaths.Add([string]$entry.destination)
            $sourceRelativePath = ConvertTo-ClassicRelativePath `
                -RelativePath ([string]$entry.source) `
                -Purpose "Collection source"
            if (-not $receiptedInputs.ContainsKey($sourceRelativePath)) {
                throw "Classic build receipt omitted collection input '$sourceRelativePath'."
            }
            Copy-CollectionFile `
                -RepositoryRoot $repositoryRoot `
                -StageDirectory $stageDirectory `
                -SourceRelativePath $sourceRelativePath `
                -DestinationRelativePath ([string]$entry.destination) `
                -ExpectedInputRecord $receiptedInputs[$sourceRelativePath]
        }
        foreach ($generatedPath in @($plan.generatedFiles)) {
            $expectedPaths.Add([string]$generatedPath)
        }

        & (Join-Path $PSScriptRoot "New-ThirdPartyNotices.ps1") `
            -OutputPath (Join-Path $stageDirectory "THIRD-PARTY-NOTICES.txt") `
            -Flavor Classic

        & (Join-Path $repositoryRoot "tools\Write-PluginManifest.ps1") `
            -PluginDirectory (Join-Path $stageDirectory "plugins")

        Assert-ExactArtifactFiles `
            -ArtifactDirectory $stageDirectory `
            -ExpectedRelativePaths $expectedPaths.ToArray()

        $validatorProject = Join-Path $PSScriptRoot (
            "ArtifactValidator\ArtifactValidator.csproj")
        & dotnet run `
            --project $validatorProject `
            --configuration Release `
            -- $stageDirectory $contractPath
        if ($LASTEXITCODE -ne 0) {
            throw "Classic artifact validation failed with exit code $LASTEXITCODE."
        }

        # Recheck the exact set at the publication boundary. Validation is intentionally not
        # treated as a lease that prevents another process from changing the stage afterward.
        Assert-ExactArtifactFiles `
            -ArtifactDirectory $stageDirectory `
            -ExpectedRelativePaths $expectedPaths.ToArray()
        [void](Seal-OwnedClassicArtifactStage -Stage $stage)

        # The first receipt check prevented known-stale inputs before collection. Repeat it at
        # the publication boundary so no compiled input or source state can change while the
        # stage is copied and validated.
        $boundaryReceipt = Assert-ClassicBuildReceipt `
            -RepositoryRoot $repositoryRoot `
            -PlanPath $planPath `
            -ReceiptPath $BuildReceiptPath `
            -Configuration "Release" `
            -Platforms @("Any CPU", "x64", "Win32") `
            -TestToolRoot $TestToolRoot `
            -Lease $Lease `
            -LeaseToken $LeaseToken
        $boundaryIdentity = [string]$boundaryReceipt.buildId + ":" +
            [string]$boundaryReceipt.receiptContentSha256
        if ($boundaryIdentity -cne $sourceIdentity) {
            throw "Complete classic build receipt digest changed during collection."
        }

        Publish-ValidatedArtifactStage `
            -Stage $stage `
            -ArtifactDirectory $artifactDirectory `
            -CollectionId ([string]$plan.collectionId) `
            -SourceIdentity $sourceIdentity `
            -Lease $Lease `
            -LeaseToken $LeaseToken
        Write-Host "Classic artifact collection PASS: $artifactDirectory"
    }
    finally {
        if ($stage -ne $null -and
            (Test-Path -LiteralPath $stage.receiptPath -PathType Leaf)) {
            Remove-OwnedClassicArtifactStage -Stage $stage
        }
    }
}

if ($MyInvocation.InvocationName -ne ".") {
    throw "Direct classic artifact collection is disabled. Run Invoke-ClassicRelease.ps1 so build, receipt, collection, and publication share one release lease."
}
