[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ContractPath,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ArtifactName,
    [string]$NativeInventoryPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$releaseSafetyScript = Join-Path $PSScriptRoot "ReleaseSafety.ps1"
if (-not (Test-Path -LiteralPath $releaseSafetyScript -PathType Leaf)) {
    throw "Release safety helper does not exist: $releaseSafetyScript"
}
. $releaseSafetyScript

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
$ContractPath = [IO.Path]::GetFullPath($ContractPath)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
Assert-SafeArtifactName -Name $ArtifactName
if ([string]::IsNullOrWhiteSpace($NativeInventoryPath)) {
    $NativeInventoryPath = Join-Path $PSScriptRoot "native-dependencies.json"
}
$NativeInventoryPath = [IO.Path]::GetFullPath($NativeInventoryPath)

Assert-NoReparsePointInExistingPath `
    -Path $ArtifactDirectory `
    -Purpose "Artifact directory"
if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "Artifact directory does not exist: $ArtifactDirectory"
}
if (-not (Test-Path -LiteralPath $ContractPath -PathType Leaf)) {
    throw "Artifact contract does not exist: $ContractPath"
}
if (-not (Test-Path -LiteralPath $NativeInventoryPath -PathType Leaf)) {
    throw "Native dependency inventory does not exist: $NativeInventoryPath"
}
$artifactPrefix = $ArtifactDirectory.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
if (Test-SameOrDescendantPath `
    -CandidatePath $OutputDirectory `
    -RootPath $ArtifactDirectory) {
    throw "Provenance output must be outside the artifact directory to avoid self-referential hashes."
}
Assert-NoReparsePointInExistingPath `
    -Path $OutputDirectory `
    -Purpose "Provenance output directory"
if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}
Assert-NoReparsePointInExistingPath `
    -Path $OutputDirectory `
    -Purpose "Provenance output directory"

function Get-Sha256Hex([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$nativeInventory = Get-Content -LiteralPath $NativeInventoryPath -Raw | ConvertFrom-Json
if ([int]$nativeInventory.schemaVersion -ne 1 -or
    @($nativeInventory.artifacts).Count -eq 0) {
    throw "Native dependency inventory is empty or has an unsupported schema."
}
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
foreach ($inputFile in @($nativeInventory.pinnedFiles)) {
    $path = [IO.Path]::GetFullPath((Join-Path $repoRoot ([string]$inputFile.path)))
    if (-not $path.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Native inventory file is missing or escapes the repository: $($inputFile.path)"
    }
    $actualHash = Get-Sha256Hex $path
    if (-not [string]::Equals(
        $actualHash,
        [string]$inputFile.sha256,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Native inventory hash mismatch: $($inputFile.path)"
    }
}
foreach ($submodule in @($nativeInventory.pinnedSubmodules)) {
    $path = [IO.Path]::GetFullPath((Join-Path $repoRoot ([string]$submodule.path)))
    if (-not $path.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Native inventory submodule is missing or escapes the repository: $($submodule.path)"
    }
    $actualCommit = (& git -C $path rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or
        -not [string]::Equals(
            $actualCommit,
            [string]$submodule.commit,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Native inventory submodule commit mismatch: $($submodule.path)"
    }
}

function Get-PatchId([string]$WorkingDirectory) {
    Push-Location $WorkingDirectory
    try {
        # Diff from HEAD captures staged and unstaged tracked changes. A plain `git diff` misses
        # staged source, which can produce an artifact while the receipt incorrectly says clean.
        $patchId = (& git diff HEAD --binary --no-ext-diff | & git patch-id --stable)
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to calculate git patch id in $WorkingDirectory"
        }
        if ([string]::IsNullOrWhiteSpace(($patchId -join ""))) {
            return $null
        }
        return (($patchId | Select-Object -First 1) -split "\s+")[0]
    }
    finally { Pop-Location }
}

function Get-UntrackedRecords([string]$WorkingDirectory) {
    $records = @{}
    $excludedClassifications = @{}
    $excludedCount = 0
    $paths = @(& git -C $WorkingDirectory -c core.quotepath=false ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate untracked source in $WorkingDirectory"
    }
    foreach ($relativePath in $paths) {
        if ([string]::IsNullOrWhiteSpace($relativePath)) { continue }
        $fullPath = [IO.Path]::GetFullPath((Join-Path $WorkingDirectory $relativePath))
        Assert-NoReparsePointInExistingPath `
            -Path $fullPath `
            -Purpose "Untracked source"
        $info = New-Object IO.FileInfo($fullPath)
        if (-not $info.Exists) {
            throw "Untracked source disappeared during provenance capture: $relativePath"
        }
        if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Untracked source must not be a reparse point: $relativePath"
        }
        $classification = Get-GeneratedUntrackedClassification -RelativePath $relativePath
        if ($null -ne $classification) {
            if (-not $excludedClassifications.ContainsKey($classification)) {
                $excludedClassifications[$classification] = 0
            }
            $excludedClassifications[$classification]++
            $excludedCount++
            continue
        }
        $normalized = $relativePath.Replace("\", "/")
        $records[$normalized] = [pscustomobject]@{
            path = $normalized
            bytes = [long]$info.Length
            sha256 = Get-Sha256Hex $fullPath
        }
    }
    $orderedPaths = [string[]]$records.Keys
    [Array]::Sort($orderedPaths, [StringComparer]::Ordinal)
    $orderedClassifications = [string[]]$excludedClassifications.Keys
    [Array]::Sort($orderedClassifications, [StringComparer]::Ordinal)
    return [pscustomobject]@{
        sourceFiles = @($orderedPaths | ForEach-Object { $records[$_] })
        excludedGeneratedFiles = [pscustomobject]@{
            count = [int]$excludedCount
            classifications = @(
                $orderedClassifications | ForEach-Object {
                    [pscustomobject]@{
                        classification = $_
                        count = [int]$excludedClassifications[$_]
                    }
                }
            )
        }
    }
}

$fileRecords = @{}
foreach ($artifactFile in @(Get-VerifiedArtifactFiles -Root $ArtifactDirectory)) {
    Assert-NoReparsePointInExistingPath `
        -Path $artifactFile.FullName `
        -Purpose "Artifact file"
    $currentFile = Get-Item -LiteralPath $artifactFile.FullName -Force -ErrorAction Stop
    if ($currentFile.PSIsContainer -or
        ($currentFile.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Artifact file changed or became a reparse point during hashing: $($artifactFile.FullName)"
    }
    $relative = $currentFile.FullName.Substring($artifactPrefix.Length).Replace("\", "/")
    if ($fileRecords.ContainsKey($relative)) {
        throw "Artifact contains a duplicate normalized path: $relative"
    }
    $fileRecords[$relative] = [pscustomobject]@{
        path = $relative
        bytes = [long]$currentFile.Length
        sha256 = Get-Sha256Hex $currentFile.FullName
    }
}
$orderedArtifactPaths = [string[]]$fileRecords.Keys
[Array]::Sort($orderedArtifactPaths, [StringComparer]::Ordinal)
$files = @($orderedArtifactPaths | ForEach-Object { $fileRecords[$_] })
if ($files.Count -eq 0) {
    throw "Artifact contains zero regular files."
}

$hashManifest = [ordered]@{
    schemaVersion = 1
    artifact = $ArtifactName
    algorithm = "SHA-256"
    files = $files
}
$utf8 = New-Object Text.UTF8Encoding($false)
$hashManifestPath = Join-Path $OutputDirectory "$ArtifactName.sha256.json"
Assert-NoReparsePointInExistingPath `
    -Path $hashManifestPath `
    -Purpose "Hash manifest output"
[IO.File]::WriteAllText(
    $hashManifestPath,
    (($hashManifest | ConvertTo-Json -Depth 8) + "`n"),
    $utf8)
$contractEvidencePath = Join-Path $OutputDirectory "$ArtifactName.contract.json"
Assert-NoReparsePointInExistingPath `
    -Path $contractEvidencePath `
    -Purpose "Contract evidence output"
[IO.File]::WriteAllText(
    $contractEvidencePath,
    [IO.File]::ReadAllText($ContractPath),
    $utf8)
$nativeInventoryEvidencePath =
    Join-Path $OutputDirectory "$ArtifactName.native-dependencies.json"
Assert-NoReparsePointInExistingPath `
    -Path $nativeInventoryEvidencePath `
    -Purpose "Native inventory evidence output"
[IO.File]::WriteAllText(
    $nativeInventoryEvidencePath,
    [IO.File]::ReadAllText($NativeInventoryPath),
    $utf8)

Push-Location $repoRoot
try {
    $sourceCommit = (& git rev-parse HEAD).Trim()
    $commitTimeRaw = (& git show -s --format=%cI HEAD).Trim()
    $commitTime = ([DateTimeOffset]::Parse($commitTimeRaw)).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    $rootPatchId = Get-PatchId $repoRoot
    $rootUntrackedResult = Get-UntrackedRecords $repoRoot
    $rootUntracked = @($rootUntrackedResult.sourceFiles)
    $submodules = @()
    if (Test-Path -LiteralPath (Join-Path $repoRoot ".gitmodules")) {
        $moduleLines = @(& git config --file .gitmodules --get-regexp "submodule\..*\.path")
        foreach ($line in $moduleLines) {
            $parts = $line -split "\s+", 2
            if ($parts.Count -ne 2) { continue }
            $relativePath = $parts[1].Trim()
            $fullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $relativePath))
            if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
                $submodules += [pscustomobject]@{
                    path = $relativePath.Replace("\", "/")
                    commit = $null
                    patchId = $null
                    untrackedFiles = @()
                    excludedGeneratedFiles = [pscustomobject]@{
                        count = 0
                        classifications = @()
                    }
                    state = "missing"
                }
                continue
            }
            $subCommit = (& git -C $fullPath rev-parse HEAD).Trim()
            $subPatchId = Get-PatchId $fullPath
            $subUntrackedResult = Get-UntrackedRecords $fullPath
            $subUntracked = @($subUntrackedResult.sourceFiles)
            $submodules += [pscustomobject]@{
                path = $relativePath.Replace("\", "/")
                commit = $subCommit
                patchId = $subPatchId
                untrackedFiles = $subUntracked
                excludedGeneratedFiles = $subUntrackedResult.excludedGeneratedFiles
                state = $(if (
                    $null -eq $subPatchId -and
                    $subUntracked.Count -eq 0 -and
                    $subUntrackedResult.excludedGeneratedFiles.count -eq 0
                ) { "clean" } else { "patched-or-untracked" })
            }
        }
    }
    $submoduleStateIsClean =
        @($submodules | Where-Object { $_.state -ne "clean" }).Count -eq 0

    $msbuildVersion = @(& dotnet msbuild -version -nologo) |
        Where-Object { $_ -match "^\d+\.\d+" } |
        Select-Object -Last 1
    $gitVersion = (& git --version).Trim()
    $receipt = [ordered]@{
        schemaVersion = 1
        artifact = $ArtifactName
        artifactFileCount = $files.Count
        artifactBytes = [long](($files | Measure-Object -Property bytes -Sum).Sum)
        artifactHashManifest = [IO.Path]::GetFileName($hashManifestPath)
        artifactHashManifestSha256 = Get-Sha256Hex $hashManifestPath
        artifactContract = [IO.Path]::GetFileName($contractEvidencePath)
        artifactContractSha256 = Get-Sha256Hex $contractEvidencePath
        nativeDependencyInventory = [IO.Path]::GetFileName($nativeInventoryEvidencePath)
        nativeDependencyInventorySha256 = Get-Sha256Hex $nativeInventoryEvidencePath
        source = [ordered]@{
            commit = $sourceCommit
            commitTimeUtc = $commitTime
            rootPatchId = $rootPatchId
            untrackedFiles = $rootUntracked
            excludedGeneratedFiles = $rootUntrackedResult.excludedGeneratedFiles
            untrackedPolicy = [ordered]@{
                enumeration = "git-ls-files-others-exclude-standard"
                sourceFiles = "path-size-sha256"
                generatedFiles = "count-and-classification-only"
                ignoredFiles = "not-enumerated-or-counted"
            }
            state = $(if (
                $null -eq $rootPatchId -and
                $rootUntracked.Count -eq 0 -and
                $rootUntrackedResult.excludedGeneratedFiles.count -eq 0 -and
                $submoduleStateIsClean
            ) { "clean" } else { "patched-or-untracked" })
            submodules = $submodules
        }
        toolchain = [ordered]@{
            dotnetSdk = (& dotnet --version).Trim()
            msbuild = ([string]$msbuildVersion).Trim()
            git = $gitVersion
            powershell = $PSVersionTable.PSVersion.ToString()
            os = [Environment]::OSVersion.VersionString
        }
        generatedAtUtc = $commitTime
        signature = "unsigned"
        nativeDependencyProvenance = "The attached inventory pins known inputs and records each packaged native build recipe or explicit vendored-binary provenance gap; artifact bytes are independently hashed."
    }
}
finally { Pop-Location }

$receiptPath = Join-Path $OutputDirectory "$ArtifactName.build-receipt.json"
Assert-NoReparsePointInExistingPath `
    -Path $receiptPath `
    -Purpose "Build receipt output"
[IO.File]::WriteAllText(
    $receiptPath,
    (($receipt | ConvertTo-Json -Depth 10) + "`n"),
    $utf8)

Write-Host "Hash manifest: $hashManifestPath ($($files.Count) files)"
Write-Host "Artifact contract: $contractEvidencePath"
Write-Host "Native dependency inventory: $nativeInventoryEvidencePath"
Write-Host "Build receipt: $receiptPath"
