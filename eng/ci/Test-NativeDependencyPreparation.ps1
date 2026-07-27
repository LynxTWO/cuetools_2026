[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$tempBase = [IO.Path]::GetTempPath()
$tempRoot = Join-Path $tempBase (
    "cuetools-native-preparation-" + [Guid]::NewGuid().ToString("N"))
$checkCount = 0

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
    $script:checkCount++
}

function Copy-RepoFile([string]$RelativePath) {
    $source = [IO.Path]::GetFullPath((Join-Path $repoRoot $RelativePath))
    $destination = [IO.Path]::GetFullPath((Join-Path $tempRoot $RelativePath))
    $parent = [IO.Path]::GetDirectoryName($destination)
    [void][IO.Directory]::CreateDirectory($parent)
    Copy-Item -LiteralPath $source -Destination $destination
}

try {
    [void][IO.Directory]::CreateDirectory($tempRoot)
    foreach ($relativePath in @(
        "eng/ci/Build-NativeDependencies.ps1",
        "eng/ci/NativeWarningBaseline.ps1",
        "eng/ci/native-warning-baseline.json",
        "eng/release/NativeDependencyInventory.ps1",
        "eng/release/native-dependencies.json",
        "ThirdParty/MAC_SDK/MAC_1320_SDK.zip",
        "ThirdParty/MAC_SDK/Source/MACLibDll/MACLibDll.cpp",
        "ThirdParty/MAC_SDK/Source/MACLibDll/MACLibDll.def",
        "ThirdParty/MAC_SDK/Source/MACLibDll/MACLibDll.h",
        "ThirdParty/MAC_SDK/Source/Projects/VS2022/MACLibDll/MACLibDll.vcxproj")) {
        Copy-RepoFile $relativePath
    }

    $trackedSentinel = Join-Path $tempRoot (
        "ThirdParty\MAC_SDK\Source\Projects\VS2022\MACLibDll\MACLibDll.vcxproj")
    $archiveOnlyProject = Join-Path $tempRoot (
        "ThirdParty\MAC_SDK\Source\Projects\Visual Studio - 2022\MACLib\MACLib.vcxproj")
    Assert-True (Test-Path -LiteralPath $trackedSentinel -PathType Leaf) `
        "The clean-checkout fixture is missing the tracked wrapper project."
    Assert-True (-not (Test-Path -LiteralPath $archiveOnlyProject)) `
        "The clean-checkout fixture unexpectedly contains an expanded SDK project."

    $scriptPath = Join-Path $tempRoot "eng\ci\Build-NativeDependencies.ps1"
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
        -File $scriptPath -RepositoryRoot $tempRoot -ExpandMacSdkOnly
    Assert-True ($LASTEXITCODE -eq 0) `
        "The clean-checkout-shaped Monkey's Audio SDK expansion failed."
    Assert-True (Test-Path -LiteralPath $archiveOnlyProject -PathType Leaf) `
        "The archive-only MACLib project was not expanded."
    $wrapperProjectText = [IO.File]::ReadAllText($trackedSentinel)
    Assert-True ($wrapperProjectText -notmatch "DefaultPlatformToolset") `
        "The Monkey's Audio wrapper still inherits an environment-selected toolset."
    Assert-True (
        ([regex]::Matches(
            $wrapperProjectText,
            "<PlatformToolset>v143</PlatformToolset>")).Count -eq 4) `
        "The Monkey's Audio wrapper does not pin v143 in all four configurations."

    $generatedTarget = Join-Path $tempRoot (
        "ThirdParty\MAC_SDK\Source\Projects\Visual Studio - 2022\" +
        "MACLib\x64\Release\generated.obj")
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($generatedTarget))
    [IO.File]::WriteAllBytes($generatedTarget, [byte[]](1, 2, 3))
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
        -File $scriptPath -RepositoryRoot $tempRoot -ExpandMacSdkOnly
    Assert-True ($LASTEXITCODE -eq 0) `
        "Expected native build residue was rejected by the expanded-tree source closure."

    $unexpectedTarget = Join-Path $tempRoot (
        "ThirdParty\MAC_SDK\Source\UnexpectedOverride.cpp")
    [IO.File]::WriteAllText($unexpectedTarget, "unexpected source")
    $oldErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        $unexpectedOutput = @(
            & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
                -File $scriptPath -RepositoryRoot $tempRoot -ExpandMacSdkOnly 2>&1)
        $unexpectedExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $oldErrorAction }
    Assert-True ($unexpectedExitCode -ne 0) `
        "An unbound source file in the expanded SDK was silently accepted."
    $unexpectedText = $unexpectedOutput | Out-String
    Assert-True (
        ($unexpectedText -match
            "file not bound to the archive or CUETools override manifest") -and
        ($unexpectedText -match "Source/UnexpectedOverride.cpp")) `
        ("The unbound-source refusal did not identify the unexpected SDK file. Output: " +
            $unexpectedText)
    [IO.File]::Delete($unexpectedTarget)

    $repairTarget = Join-Path $tempRoot "ThirdParty\MAC_SDK\Readme.txt"
    [IO.File]::Delete($repairTarget)
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
        -File $scriptPath -RepositoryRoot $tempRoot -ExpandMacSdkOnly
    Assert-True ($LASTEXITCODE -eq 0) `
        "A missing expanded SDK file was not repaired."
    Assert-True (Test-Path -LiteralPath $repairTarget -PathType Leaf) `
        "The repaired SDK file is still missing."

    [IO.File]::AppendAllText($repairTarget, "unexpected mutation")
    $oldErrorAction = $ErrorActionPreference
    try {
        # This child process is expected to fail. Windows PowerShell converts its
        # stderr into ErrorRecords under Stop, so relax only around this probe.
        $ErrorActionPreference = "Continue"
        $output = @(
            & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
                -File $scriptPath -RepositoryRoot $tempRoot -ExpandMacSdkOnly 2>&1)
        $mutationExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $oldErrorAction }
    Assert-True ($mutationExitCode -ne 0) `
        "A mutated expanded SDK file was silently accepted."
    Assert-True (
        (($output | Out-String) -match
            "differs from the pinned archive:\s*Readme.txt")) `
        "The mutated SDK refusal did not identify the drifted archive file."

    [IO.File]::Delete($repairTarget)
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
        -File $scriptPath -RepositoryRoot $tempRoot -ExpandMacSdkOnly
    Assert-True ($LASTEXITCODE -eq 0) `
        "The mutated archive member was not recoverable through a clean repair."

    $fixtureManifestPath = Join-Path $tempRoot (
        "eng\release\native-dependencies.json")
    $fixtureManifestText = [IO.File]::ReadAllText($fixtureManifestPath)
    $fixtureManifest = $fixtureManifestText | ConvertFrom-Json
    $fixtureMacArtifact = @(
        $fixtureManifest.artifacts |
            Where-Object { [string]$_.id -eq "monkeys-audio" })
    Assert-True ($fixtureMacArtifact.Count -eq 1) `
        "The fixture does not contain exactly one Monkey's Audio artifact."
    $fixtureMacArtifact[0].sourceArchive.sha256 = "0" * 64
    [IO.File]::WriteAllText(
        $fixtureManifestPath,
        ($fixtureManifest | ConvertTo-Json -Depth 20))
    try {
        $ErrorActionPreference = "Continue"
        $metadataOutput = @(
            & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
                -File $scriptPath -RepositoryRoot $tempRoot -ExpandMacSdkOnly 2>&1)
        $metadataExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $oldErrorAction }
    Assert-True ($metadataExitCode -ne 0) `
        "Conflicting Monkey's Audio sourceArchive metadata was silently accepted."
    Assert-True (
        (($metadataOutput | Out-String) -match
            "sourceArchive SHA-256 does not match the pinned file entry")) `
        "The sourceArchive metadata refusal did not identify the conflicting hash."

    [IO.File]::WriteAllText($fixtureManifestPath, $fixtureManifestText)
    $fixtureManifest = $fixtureManifestText | ConvertFrom-Json
    $fixtureMacArtifact = @(
        $fixtureManifest.artifacts |
            Where-Object { [string]$_.id -eq "monkeys-audio" })
    $fixtureMacArtifact[0].sourceArchive.bytes =
        [int64]$fixtureMacArtifact[0].sourceArchive.bytes + 1
    [IO.File]::WriteAllText(
        $fixtureManifestPath,
        ($fixtureManifest | ConvertTo-Json -Depth 20))
    try {
        $ErrorActionPreference = "Continue"
        $metadataOutput = @(
            & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
                -File $scriptPath -RepositoryRoot $tempRoot -ExpandMacSdkOnly 2>&1)
        $metadataExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $oldErrorAction }
    Assert-True ($metadataExitCode -ne 0) `
        "Incorrect Monkey's Audio sourceArchive byte length was silently accepted."
    Assert-True (
        (($metadataOutput | Out-String) -match
            "sourceArchive byte length does not match the pinned file")) `
        "The sourceArchive metadata refusal did not identify the byte-length conflict."
    [IO.File]::WriteAllText($fixtureManifestPath, $fixtureManifestText)

    $overrideTarget = Join-Path $tempRoot (
        "ThirdParty\MAC_SDK\Source\MACLibDll\MACLibDll.cpp")
    [IO.File]::AppendAllText($overrideTarget, "unexpected override mutation")
    try {
        $ErrorActionPreference = "Continue"
        $overrideOutput = @(
            & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass `
                -File $scriptPath -RepositoryRoot $tempRoot -ExpandMacSdkOnly 2>&1)
        $overrideExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $oldErrorAction }
    Assert-True ($overrideExitCode -ne 0) `
        "A mutated CUETools Monkey's Audio wrapper file was silently accepted."
    $overrideText = $overrideOutput | Out-String
    Assert-True (
        ($overrideText -match "override hash mismatch") -and
        ($overrideText -match "Source/MACLibDll/MACLibDll.cpp")) `
        "The mutated-wrapper refusal did not identify the drifted override."

    Write-Host "Native dependency preparation checks passed: $checkCount"
}
finally {
    $tempPrefix = $tempBase.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $tempLeaf = [IO.Path]::GetFileName($tempRoot)
    if (-not $tempRoot.StartsWith(
        $tempPrefix,
        [StringComparison]::OrdinalIgnoreCase) -or
        -not $tempLeaf.StartsWith(
            "cuetools-native-preparation-",
            [StringComparison]::Ordinal)) {
        throw "Refusing to clean an unexpected native preparation test path: $tempRoot"
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
