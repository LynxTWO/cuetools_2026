[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$MSBuildPath,
    [string]$WarningBaselinePath,
    [switch]$UpdateWarningBaseline
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
if ([string]::IsNullOrWhiteSpace($WarningBaselinePath)) {
    $WarningBaselinePath = Join-Path $PSScriptRoot "native-warning-baseline.json"
}
$WarningBaselinePath = [IO.Path]::GetFullPath($WarningBaselinePath)
$warningBaseline = Get-Content -LiteralPath $WarningBaselinePath -Raw | ConvertFrom-Json
if ([int]$warningBaseline.schemaVersion -ne 1) {
    throw "Unsupported native warning baseline schema '$($warningBaseline.schemaVersion)'."
}

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
        "ThirdParty\MAC_SDK\Source\Projects\VS2022\MACLibDll\MACLibDll.vcxproj")
    if (Test-Path -LiteralPath $macProject -PathType Leaf) {
        Write-Host "Monkey's Audio SDK is already expanded."
        return
    }

    $sourceRoot = Join-Path $macRoot "Source"
    if (Test-Path -LiteralPath $sourceRoot) {
        throw "Monkey's Audio SDK Source exists but its VS2022 project is missing; refusing to overwrite a partial tree."
    }
    $zipPath = Join-Path $macRoot "MAC_1086_SDK.zip"
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        throw "Monkey's Audio SDK archive is missing: $zipPath"
    }
    if ((Get-Item -LiteralPath $macRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Monkey's Audio SDK destination must not be a reparse point."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $destinationPrefix = $macRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar
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
        }
    }
    finally { $archive.Dispose() }

    Expand-Archive -LiteralPath $zipPath -DestinationPath $macRoot
    if (-not (Test-Path -LiteralPath $macProject -PathType Leaf)) {
        throw "Monkey's Audio SDK expansion did not produce the expected VS2022 project."
    }
    Write-Host "Expanded pinned Monkey's Audio SDK."
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

function Assert-X64Pe([string]$RelativePath) {
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
        if ($machine -ne 0x8664) {
            throw ("Native dependency output is machine 0x{0:X4}, not AMD64: {1}" -f
                $machine, $RelativePath)
        }
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
    Write-Host "Native output PASS: $RelativePath ($($info.Length) bytes, AMD64 PE)"
}

Expand-MacSdkIfRequired
Invoke-IdempotentPatch `
    "ThirdParty/flac" `
    "ThirdParty/submodule_flac_CUETools.patch"
Invoke-IdempotentPatch `
    "ThirdParty/WavPack" `
    "ThirdParty/submodule_WavPack_CUETools.patch"
Invoke-IdempotentPatch `
    "ThirdParty/MAC_SDK" `
    "ThirdParty/ThirdParty_MAC_SDK_CUETools.patch"

$msbuild = Resolve-MSBuild
$solutionDirectory = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
$builds = @(
    [pscustomobject]@{
        project = "ThirdParty\flac\src\libFLAC\libFLAC_dynamic.vcxproj"
        output = "ThirdParty\x64\libFLAC_dynamic.dll"
    },
    [pscustomobject]@{
        project = "ThirdParty\WavPack\wavpackdll\wavpackdll.vcxproj"
        output = "ThirdParty\x64\wavpackdll.dll"
    },
    [pscustomobject]@{
        project = "ThirdParty\MAC_SDK\Source\Projects\VS2022\MACLibDll\MACLibDll.vcxproj"
        output = "ThirdParty\x64\MACLibDll.dll"
    }
)

$warningLines = [Collections.Generic.List[string]]::new()
foreach ($build in $builds) {
    $project = Resolve-RepoPath $build.project
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Native dependency project is missing: $($build.project)"
    }
    $output = @(
        & $msbuild $project /t:Rebuild "/p:Configuration=$Configuration" /p:Platform=x64 `
            "/p:SolutionDir=$solutionDirectory" /nologo 2>&1
    )
    $exitCode = $LASTEXITCODE
    foreach ($item in $output) {
        $line = [string]$item
        Write-Host $line
        if ($line -match ":\s*warning\s+[A-Za-z]+\d+\s*:") {
            $warningLines.Add($line)
        }
    }
    if ($exitCode -ne 0) {
        throw "Native dependency build failed: $($build.project)"
    }
    Assert-X64Pe $build.output
}

$fingerprints = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$warningPattern = '^(?<source>.*?)(?:\(\d+(?:,\d+){0,3}\))?\s*:\s*warning\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(?:\s+\[[^\]]+\])?$'
foreach ($line in $warningLines) {
    $match = [Text.RegularExpressions.Regex]::Match($line, $warningPattern)
    if (-not $match.Success) {
        throw "A native warning line could not be normalized for the checked baseline: $line"
    }
    $source = $match.Groups["source"].Value.Trim()
    if ($source.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        $source = $source.Substring($repoPrefix.Length)
    }
    $source = $source.Replace("\", "/")
    $message = [Text.RegularExpressions.Regex]::Replace(
        $match.Groups["message"].Value.Trim(),
        "\s+",
        " ")
    $message = $message.Replace($repoRoot, "<repo>")
    $null = $fingerprints.Add(
        "$source|$($match.Groups["code"].Value.ToUpperInvariant())|$message")
}

$actual = [string[]]@($fingerprints)
[Array]::Sort($actual, [StringComparer]::Ordinal)
if ($UpdateWarningBaseline) {
    $warningBaseline.fingerprints = $actual
    $utf8 = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText(
        $WarningBaselinePath,
        (($warningBaseline | ConvertTo-Json -Depth 8) + "`n"),
        $utf8)
    Write-Host (
        "Updated native warning baseline with $($actual.Count) distinct fingerprints " +
        "from $($warningLines.Count) emitted warning lines.")
    exit 0
}

$expected = [string[]]@($warningBaseline.fingerprints)
[Array]::Sort($expected, [StringComparer]::Ordinal)
$newWarnings = @(
    $actual |
        Where-Object {
            [Array]::BinarySearch($expected, $_, [StringComparer]::Ordinal) -lt 0
        })
$resolvedWarnings = @(
    $expected |
        Where-Object {
            [Array]::BinarySearch($actual, $_, [StringComparer]::Ordinal) -lt 0
        })

Write-Host ""
Write-Host "=== Native x64 warning budget ==="
Write-Host "Emitted warning lines: $($warningLines.Count)"
Write-Host "Distinct current fingerprints: $($actual.Count)"
Write-Host "Checked baseline fingerprints: $($expected.Count)"
if ($resolvedWarnings.Count -gt 0) {
    Write-Host "Resolved since baseline ($($resolvedWarnings.Count)); baseline may be pruned:"
    $resolvedWarnings | ForEach-Object { Write-Host "  - $_" }
}
if ($newWarnings.Count -gt 0) {
    Write-Host "New warnings ($($newWarnings.Count)):"
    $newWarnings | ForEach-Object { Write-Host "  + $_" }
    throw "Native x64 warning budget failed: new warning fingerprints were emitted."
}

Write-Host "Native x64 warning budget PASS: no new warning fingerprints."
Write-Host "All required x64 native dependencies were rebuilt from pinned, patched source."
