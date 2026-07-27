[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$MSBuildPath,
    [string]$WarningBaselinePath,
    [switch]$UpdateWarningBaseline,
    [switch]$ApplyPatchesOnly,
    [string]$RepositoryRoot,
    [switch]$ExpandMacSdkOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
} else {
    [IO.Path]::GetFullPath($RepositoryRoot)
}
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
if ([string]::IsNullOrWhiteSpace($WarningBaselinePath)) {
    $WarningBaselinePath = Join-Path $PSScriptRoot "native-warning-baseline.json"
}
$WarningBaselinePath = [IO.Path]::GetFullPath($WarningBaselinePath)
. (Join-Path $PSScriptRoot "NativeWarningBaseline.ps1")
$nativeInventoryScript = Join-Path $PSScriptRoot "..\release\NativeDependencyInventory.ps1"
if (-not (Test-Path -LiteralPath $nativeInventoryScript -PathType Leaf)) {
    throw "Native dependency inventory helper does not exist: $nativeInventoryScript"
}
. $nativeInventoryScript

function Resolve-RepoPath([string]$RelativePath) {
    $path = [IO.Path]::GetFullPath((Join-Path $repoRoot $RelativePath))
    if (-not $path.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes the repository: $RelativePath"
    }
    return $path
}

function Invoke-IdempotentPatch(
    [string]$TargetDirectory,
    [string]$PatchRelativePath) {
    $patchPath = Resolve-RepoPath $PatchRelativePath
    if (-not (Test-Path -LiteralPath $patchPath -PathType Leaf)) {
        throw "Required patch does not exist: $PatchRelativePath"
    }

    $oldErrorAction = $ErrorActionPreference
    try {
        # A failed --check is expected when testing the already-applied branch. Windows PowerShell
        # turns native stderr into an ErrorRecord under Stop, so lower it only around these probes.
        $ErrorActionPreference = "Continue"
        & git -C $repoRoot apply --check "--directory=$TargetDirectory" --whitespace=nowarn $patchPath 2>$null
        $canApply = $LASTEXITCODE -eq 0
    }
    finally { $ErrorActionPreference = $oldErrorAction }
    if ($canApply) {
        & git -C $repoRoot apply "--directory=$TargetDirectory" --whitespace=nowarn $patchPath
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to apply $PatchRelativePath"
        }
        Write-Host "Applied native dependency patch: $PatchRelativePath"
        return
    }

    try {
        $ErrorActionPreference = "Continue"
        & git -C $repoRoot apply --reverse --check "--directory=$TargetDirectory" --whitespace=nowarn $patchPath 2>$null
        $isApplied = $LASTEXITCODE -eq 0
    }
    finally { $ErrorActionPreference = $oldErrorAction }
    if ($isApplied) {
        Write-Host "Native dependency patch already applied: $PatchRelativePath"
        return
    }
    throw "Patch is neither cleanly applicable nor cleanly applied: $PatchRelativePath"
}

function Expand-MacSdkIfRequired {
    $macRoot = Resolve-RepoPath "ThirdParty\MAC_SDK"
    $macProject = Resolve-RepoPath (
        "ThirdParty\MAC_SDK\Source\Projects\Visual Studio - 2022\MACLib\MACLib.vcxproj")
    $archiveRelativePath = "ThirdParty/MAC_SDK/MAC_1320_SDK.zip"
    $zipPath = Resolve-RepoPath $archiveRelativePath
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        throw "Monkey's Audio SDK archive is missing: $zipPath"
    }
    $macRootInfo = Get-Item -LiteralPath $macRoot -Force
    if (($macRootInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Monkey's Audio SDK destination must not be a reparse point."
    }

    $nativeManifestPath = Resolve-RepoPath "eng\release\native-dependencies.json"
    $nativeManifest = Get-Content -LiteralPath $nativeManifestPath -Raw |
        ConvertFrom-Json
    Assert-PinnedSourceArchiveBinding `
        -Inventory $nativeManifest `
        -RepositoryRoot $repoRoot `
        -ArtifactId "monkeys-audio" `
        -PinnedRelativePath $archiveRelativePath
    $archivePins = @(
        $nativeManifest.pinnedFiles |
            Where-Object {
                ([string]$_.path).Replace("\", "/") -eq
                    $archiveRelativePath
            })
    if ($archivePins.Count -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$archivePins[0].sha256)) {
        throw "The native dependency manifest must contain exactly one Monkey's Audio SDK archive hash."
    }
    $actualArchiveHash = (
        Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualArchiveHash -ne
        ([string]$archivePins[0].sha256).ToLowerInvariant()) {
        throw "The Monkey's Audio SDK archive does not match its pinned SHA-256."
    }

    # These stream-wrapper files are maintained directly by CUETools and do not occur in
    # the upstream archive. Every archive member and every override is hash-bound below.
    $localOverridePaths = @(
        "Source/MACLibDll/MACLibDll.cpp",
        "Source/MACLibDll/MACLibDll.def",
        "Source/MACLibDll/MACLibDll.h",
        "Source/Projects/VS2022/MACLibDll/MACLibDll.vcxproj")
    $localOverrides = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($relativePath in $localOverridePaths) {
        [void]$localOverrides.Add($relativePath)
    }

    function Assert-MacDestinationParents([string]$DestinationPath) {
        $relative = $DestinationPath.Substring($macRoot.Length).TrimStart(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
        $parts = $relative.Split(
            @([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
            [StringSplitOptions]::RemoveEmptyEntries)
        $current = $macRoot
        for ($index = 0; $index -lt $parts.Length - 1; $index++) {
            $current = Join-Path $current $parts[$index]
            if (Test-Path -LiteralPath $current) {
                $info = Get-Item -LiteralPath $current -Force
                if (-not $info.PSIsContainer -or
                    ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Monkey's Audio SDK archive destination has an unsafe parent: $current"
                }
            }
            else {
                [void][IO.Directory]::CreateDirectory($current)
            }
        }
    }

    function Get-StreamSha256([IO.Stream]$Stream) {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString(
                $sha.ComputeHash($Stream))).Replace("-", "").ToLowerInvariant()
        }
        finally { $sha.Dispose() }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    $repairedCount = 0
    $archiveFiles = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $archiveHashes = [Collections.Generic.Dictionary[string,string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    try {
        $destinationPrefix = $macRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar
        $destinations = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            $entryPath = $entry.FullName.Replace("/", [IO.Path]::DirectorySeparatorChar)
            if ([IO.Path]::IsPathRooted($entryPath)) {
                throw "Monkey's Audio SDK archive contains a rooted entry."
            }
            $destination = [IO.Path]::GetFullPath((Join-Path $macRoot $entryPath))
            if (-not $destination.StartsWith(
                $destinationPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
                throw "Monkey's Audio SDK archive contains a path traversal entry."
            }
            if (-not $destinations.Add($destination)) {
                throw "Monkey's Audio SDK archive contains duplicate destination paths."
            }
            if ([string]::IsNullOrEmpty($entry.Name)) {
                continue
            }

            $normalizedEntryPath = $entry.FullName.Replace("\", "/").TrimStart("/")
            if (-not $archiveFiles.Add($normalizedEntryPath)) {
                throw "Monkey's Audio SDK archive contains a duplicate file path."
            }
            if ($localOverrides.Contains($normalizedEntryPath)) {
                throw (
                    "Monkey's Audio SDK archive now collides with a CUETools override: " +
                    $normalizedEntryPath)
            }
            $entryStream = $entry.Open()
            try { $entryHash = Get-StreamSha256 $entryStream }
            finally { $entryStream.Dispose() }
            $archiveHashes.Add($normalizedEntryPath, $entryHash)

            Assert-MacDestinationParents $destination
            if (Test-Path -LiteralPath $destination) {
                $destinationInfo = Get-Item -LiteralPath $destination -Force
                if ($destinationInfo.PSIsContainer -or
                    ($destinationInfo.Attributes -band
                        [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Monkey's Audio SDK archive destination is unsafe: $destination"
                }
                $fileStream = [IO.File]::Open(
                    $destination,
                    [IO.FileMode]::Open,
                    [IO.FileAccess]::Read,
                    [IO.FileShare]::Read)
                try {
                    $fileHash = Get-StreamSha256 $fileStream
                }
                finally {
                    $fileStream.Dispose()
                }
                if ($entryHash -ne $fileHash) {
                    throw "Expanded Monkey's Audio SDK file differs from the pinned archive: $normalizedEntryPath"
                }
                continue
            }

            $entryStream = $entry.Open()
            $fileStream = [IO.File]::Open(
                $destination,
                [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write,
                [IO.FileShare]::None)
            try { $entryStream.CopyTo($fileStream) }
            finally {
                $fileStream.Dispose()
                $entryStream.Dispose()
            }
            $fileStream = [IO.File]::Open(
                $destination,
                [IO.FileMode]::Open,
                [IO.FileAccess]::Read,
                [IO.FileShare]::Read)
            try { $fileHash = Get-StreamSha256 $fileStream }
            finally { $fileStream.Dispose() }
            if ($entryHash -ne $fileHash) {
                throw "Expanded Monkey's Audio SDK file differs after extraction: $normalizedEntryPath"
            }
            $repairedCount++
        }
    }
    finally { $archive.Dispose() }

    foreach ($relativePath in $localOverridePaths) {
        $manifestPath = "ThirdParty/MAC_SDK/$relativePath"
        $overridePath = Resolve-RepoPath $manifestPath
        if (-not (Test-Path -LiteralPath $overridePath -PathType Leaf)) {
            throw "Required CUETools Monkey's Audio SDK override is missing: $relativePath"
        }
        $overrideInfo = Get-Item -LiteralPath $overridePath -Force
        if (($overrideInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "CUETools Monkey's Audio SDK override must not be a reparse point: $relativePath"
        }
        $overridePins = @(
            $nativeManifest.pinnedFiles |
                Where-Object {
                    ([string]$_.path).Replace("\", "/") -eq $manifestPath
                })
        if ($overridePins.Count -ne 1 -or
            [string]::IsNullOrWhiteSpace([string]$overridePins[0].sha256)) {
            throw (
                "The native dependency manifest must contain exactly one hash for " +
                "the CUETools Monkey's Audio SDK override: $relativePath")
        }
        $overrideHash = (
            Get-FileHash -LiteralPath $overridePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($overrideHash -ne
            ([string]$overridePins[0].sha256).ToLowerInvariant()) {
            throw "CUETools Monkey's Audio SDK override hash mismatch: $relativePath"
        }
    }

    $generatedPrefixPattern =
        "^Source/Projects/Visual Studio - 2022/MACLib/(?:x64/)?(?:Debug|Release)/"
    $generatedCount = 0
    foreach ($item in (Get-ChildItem -LiteralPath $macRoot -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Monkey's Audio SDK tree must not contain reparse points: $($item.FullName)"
        }
        if ($item.PSIsContainer) {
            continue
        }
        $relativePath = $item.FullName.Substring($macRoot.Length).TrimStart(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar).Replace("\", "/")
        if ($archiveFiles.Contains($relativePath) -or
            $localOverrides.Contains($relativePath) -or
            $relativePath -eq "MAC_1320_SDK.zip") {
            continue
        }
        if ($relativePath -match $generatedPrefixPattern) {
            $generatedCount++
            continue
        }
        throw (
            "Monkey's Audio SDK tree contains a file not bound to the archive or " +
            "CUETools override manifest: $relativePath")
    }

    if (-not (Test-Path -LiteralPath $macProject -PathType Leaf)) {
        throw "Monkey's Audio SDK expansion did not produce the expected VS2022 project."
    }
    if ($repairedCount -gt 0) {
        Write-Host "Expanded or repaired $repairedCount pinned Monkey's Audio SDK files."
    }
    else {
        Write-Host "Pinned Monkey's Audio SDK expansion is complete and byte-validated."
    }
    Write-Host (
        "Monkey's Audio SDK source closure PASS: $($archiveFiles.Count) archive files, " +
        "$($localOverridePaths.Count) CUETools overrides, $generatedCount generated files.")
}

function Resolve-MSBuild {
    if (-not [string]::IsNullOrWhiteSpace($MSBuildPath)) {
        $candidate = [IO.Path]::GetFullPath($MSBuildPath)
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Specified MSBuild does not exist: $candidate"
        }
        return $candidate
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw "vswhere.exe was not found; pass -MSBuildPath explicitly."
    }
    $candidate = @(
        & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find "MSBuild\**\Bin\MSBuild.exe"
    ) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($candidate) -or
        -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Visual Studio MSBuild was not found."
    }
    return [IO.Path]::GetFullPath($candidate)
}

function Assert-NativePe(
    [string]$RelativePath,
    [string]$Platform) {
    $path = Resolve-RepoPath $RelativePath
    $info = New-Object IO.FileInfo($path)
    if (-not $info.Exists -or $info.Length -le 0) {
        throw "Native dependency output is missing or empty: $RelativePath"
    }
    if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Native dependency output must not be a reparse point: $RelativePath"
    }

    $stream = [IO.File]::Open(
        $path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($stream.Length -lt 64 -or
            $reader.ReadUInt16() -ne 0x5A4D) {
            throw "Native dependency output is not a PE image: $RelativePath"
        }
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or $peOffset + 6 -gt $stream.Length) {
            throw "Native dependency output has an invalid PE header: $RelativePath"
        }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw "Native dependency output has no PE signature: $RelativePath"
        }
        $machine = $reader.ReadUInt16()
        $expectedMachine = $(if ($Platform -eq "x64") {
            0x8664
        } else {
            0x014c
        })
        if ($machine -ne $expectedMachine) {
            throw ("Native dependency output is machine 0x{0:X4}, not {1}: {2}" -f
                $machine, $Platform, $RelativePath)
        }
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
    Write-Host "Native output PASS: $RelativePath ($($info.Length) bytes, $Platform PE)"
}

Expand-MacSdkIfRequired
if ($ExpandMacSdkOnly) {
    Write-Host "Pinned Monkey's Audio SDK expansion check passed. No patch or build was requested."
    exit 0
}
Invoke-IdempotentPatch `
    "ThirdParty/flac" `
    "ThirdParty/submodule_flac_CUETools.patch"
Invoke-IdempotentPatch `
    "ThirdParty/WavPack" `
    "ThirdParty/submodule_WavPack_CUETools.patch"
if ($ApplyPatchesOnly) {
    Write-Host "Pinned native sources are expanded and patched. No build was requested."
    exit 0
}

$msbuild = Resolve-MSBuild
$solutionDirectory = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$builds = New-Object "Collections.Generic.List[object]"
foreach ($platform in @("Win32", "x64")) {
    foreach ($definition in @(
        @(
            "ThirdParty\flac\src\libFLAC\libFLAC_dynamic.vcxproj",
            "libFLAC_dynamic.dll"),
        @(
            "ThirdParty\WavPack\wavpackdll\wavpackdll.vcxproj",
            "wavpackdll.dll"),
        @(
            "ThirdParty\MAC_SDK\Source\Projects\VS2022\MACLibDll\MACLibDll.vcxproj",
            "MACLibDll.dll"))) {
        $builds.Add([pscustomobject]@{
            project = $definition[0]
            platform = $platform
            output = "ThirdParty\$platform\$($definition[1])"
        })
    }
}

$buildOutputLines = [Collections.Generic.List[string]]::new()
foreach ($build in $builds) {
    $project = Resolve-RepoPath $build.project
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Native dependency project is missing: $($build.project)"
    }
    $output = @(
        & $msbuild $project /t:Rebuild "/p:Configuration=$Configuration" `
            "/p:Platform=$($build.platform)" `
            "/p:SolutionDir=$solutionDirectory" /nologo 2>&1
    )
    $exitCode = $LASTEXITCODE
    foreach ($item in $output) {
        $line = [string]$item
        Write-Host $line
        $buildOutputLines.Add($line)
    }
    if ($exitCode -ne 0) {
        throw "Native dependency build failed: $($build.project)"
    }
    Assert-NativePe $build.output $build.platform
}

$warningResult = Get-NativeWarningBaselineResult `
    -RepositoryRoot $repoRoot `
    -WarningBaselinePath $WarningBaselinePath `
    -WarningLines $buildOutputLines.ToArray() `
    -CoverageIds @($builds | ForEach-Object {
        "$($_.platform)|$($_.project.Replace('\', '/'))"
    }) `
    -UpdateWarningBaseline:$UpdateWarningBaseline
Write-NativeWarningBaselineSummary -Result $warningResult
Write-Host "All six required native dependencies were rebuilt from pinned source."
