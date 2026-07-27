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
