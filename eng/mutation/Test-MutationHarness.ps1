[CmdletBinding()]
param(
    [switch]$Build
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Contains([string]$Text, [string]$Expected, [string]$Message) {
    Assert-True ($Text.Contains($Expected)) $Message
}

$mutationRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $mutationRoot "..\.."))
$manifestPath = Join-Path $mutationRoot "profiles.json"
$toolManifestPath = Join-Path $mutationRoot ".config\dotnet-tools.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$toolManifest = Get-Content -LiteralPath $toolManifestPath -Raw | ConvertFrom-Json
Import-Module (Join-Path $mutationRoot "MutationHarness.psm1") -Force
$mutationBuildPolicy = Get-Content -LiteralPath `
    (Join-Path $mutationRoot "Directory.Build.props") -Raw

Assert-True ($manifest.schema -ceq "cuetools.mutation-profiles.v1") `
    "Unexpected mutation profile schema."
Assert-True (@($manifest.profiles).Count -gt 0) "No mutation profiles are configured."
Assert-True ($toolManifest.tools.'dotnet-stryker'.version -ceq $manifest.toolVersion) `
    "The mutation manifest and local Stryker tool versions differ."
Assert-Contains $mutationBuildPolicy "<RestorePackagesWithLockFile" `
    "Mutation test projects must emit NuGet lock files."
Assert-Contains $mutationBuildPolicy "<RestoreLockedMode" `
    "Mutation CI must restore packages in locked mode."

$ids = @($manifest.profiles | ForEach-Object { [string]$_.id })
Assert-True (($ids | Sort-Object -Unique).Count -eq $ids.Count) `
    "Mutation profile ids must be unique."

foreach ($profile in $manifest.profiles) {
    Assert-True ($profile.risk -in @("medium", "high", "critical")) `
        "Profile $($profile.id) has an unsupported risk label."
    Assert-True ([double]$profile.minimumQuickScore -gt 0 -and `
        [double]$profile.minimumQuickScore -le 100) `
        "Profile $($profile.id) has an invalid quick score floor."
    Assert-True ([double]$profile.minimumFullScore -gt 0 -and `
        [double]$profile.minimumFullScore -le 100) `
        "Profile $($profile.id) has an invalid full score floor."
    Assert-True ([int]$profile.maximumQuickNoCoverage -ge 0) `
        "Profile $($profile.id) has an invalid quick no-coverage ceiling."
    Assert-True ([int]$profile.maximumFullNoCoverage -ge 0) `
        "Profile $($profile.id) has an invalid full no-coverage ceiling."

    $sourceProject = Join-Path $mutationRoot ([string]$profile.sourceProject)
    $testProject = Join-Path $mutationRoot ([string]$profile.testProject)
    $configPath = Join-Path $mutationRoot ([string]$profile.config)
    foreach ($path in @($sourceProject, $testProject, $configPath)) {
        Assert-True (Test-Path -LiteralPath $path -PathType Leaf) `
            "Mutation profile $($profile.id) is missing $path."
    }

    $sourceProjectText = Get-Content -LiteralPath $sourceProject -Raw
    $testProjectText = Get-Content -LiteralPath $testProject -Raw
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $settings = $config.'stryker-config'
    Assert-True ($settings.configuration -ceq "Release") `
        "Profile $($profile.id) must mutate Release builds."
    Assert-True ($settings.'target-framework' -ceq "net8.0") `
        "Profile $($profile.id) must use the isolated net8.0 graph."
    Assert-True (@($settings.reporters) -contains "json") `
        "Profile $($profile.id) must emit a JSON report."
    Assert-True ([int]$settings.thresholds.break -eq 0) `
        "Stryker's built-in break threshold must stay zero; the reviewed per-profile floor is authoritative."

    $mutatedNames = @($settings.mutate | ForEach-Object {
        [IO.Path]::GetFileName(([string]$_).Replace("/", "\"))
    }) | Sort-Object -Unique
    $expectedNames = @($profile.expectedSources | ForEach-Object {
        [IO.Path]::GetFileName(([string]$_).Replace("/", "\"))
    }) | Sort-Object -Unique
    Assert-True (($mutatedNames -join "|") -ceq ($expectedNames -join "|")) `
        "Profile $($profile.id) mutate globs do not match its reviewed source inventory."

    foreach ($source in $profile.expectedSources) {
        $relative = ([string]$source).Replace("/", "\")
        $sourcePath = Join-Path $repoRoot $relative
        Assert-True (Test-Path -LiteralPath $sourcePath -PathType Leaf) `
            "Profile $($profile.id) production source is missing: $source."
        Assert-Contains $sourceProjectText $relative `
            "Profile $($profile.id) does not link production source $source."
    }

    $includedTestNames = @([regex]::Matches(
        $testProjectText,
        '<Compile\s+Include="([^"]+)"') | ForEach-Object {
            [IO.Path]::GetFileName($_.Groups[1].Value.Replace("/", "\"))
        }) | Sort-Object -Unique
    $expectedTestNames = @($profile.expectedTests | ForEach-Object {
        [IO.Path]::GetFileName(([string]$_).Replace("/", "\"))
    }) | Sort-Object -Unique
    Assert-True (($includedTestNames -join "|") -ceq ($expectedTestNames -join "|")) `
        "Profile $($profile.id) linked tests do not match its reviewed test inventory."
    foreach ($test in $profile.expectedTests) {
        $testPath = Join-Path $repoRoot ([string]$test).Replace("/", "\")
        Assert-True (Test-Path -LiteralPath $testPath -PathType Leaf) `
            "Profile $($profile.id) test source is missing: $test"
    }
}

# The dependency seams exist only to keep Stryker out of legacy/shared-output graphs. Check their
# behavior-bearing identities against production before accepting the isolated compilation.
$device = Get-Content -LiteralPath (Join-Path $repoRoot "Bwg.Scsi\Device.cs") -Raw
$deviceMessages = Get-Content -LiteralPath `
    (Join-Path $repoRoot "Bwg.Scsi\Messages.resx") -Raw
foreach ($entry in @(
    "NoSense = 0", "RecoveredError = 1", "NotReady = 2", "MediumError = 3",
    "HardwareError = 4", "IllegalRequest = 5", "UnitAttention = 6",
    "DataProtect = 7", "BlankCheck = 8", "VendorSpecific = 9")) {
    Assert-Contains $device $entry "Bwg.Scsi sense-key contract changed: $entry"
}
Assert-True ($device -match "(?s)enum CommandStatus.*?NotSupported.*?IoctlFailed.*?DeviceFailed.*?Success") `
    "Bwg.Scsi command-status ordering changed."
Assert-Contains $deviceMessages "LOGICAL UNIT COMMUNICATION TIME-OUT" `
    "Bwg.Scsi sense-description contract changed: LOGICAL UNIT COMMUNICATION TIME-OUT"
foreach ($text in @(
    "LOGICAL UNIT COMMUNICATION FAILURE",
    "UNASSIGNED QUALIFIER")) {
    Assert-Contains $device $text "Bwg.Scsi sense-description contract changed: $text"
}
$ripperContract = Get-Content -LiteralPath `
    (Join-Path $repoRoot "CUETools.Ripper\Ripper.cs") -Raw
Assert-Contains $ripperContract "WindowGivenUpSectors = -1" `
    "ReadProgressArgs give-up default changed."
Assert-Contains $ripperContract "PopulationCount(this BitArray bits)" `
    "BitArray population-count contract changed."

$repairEvidence = Get-Content -LiteralPath `
    (Join-Path $repoRoot "CUETools.App.Core\Services\RepairEvidence.cs") -Raw
$albumTransaction = Get-Content -LiteralPath `
    (Join-Path $repoRoot "CUETools.App.Core\Services\AlbumOutputTransaction.cs") -Raw
Assert-Contains $repairEvidence 'ReceiptFileName = "repair.verify"' `
    "Repair receipt filename contract changed."
Assert-Contains $albumTransaction 'CompletionMarkerName = ".cuetools-complete"' `
    "Album completion marker contract changed."

# The TestCopyHistory profile re-declares StoreJsonContext rather than linking production, because
# the production file also registers roots that would drag HistoryStore and CUETools.Wpf.Models into
# the isolated graph. Pin the production declaration and the two roots the linked sources resolve.
$storeJsonContext = Get-Content -LiteralPath `
    (Join-Path $repoRoot "CUETools.App.Core\Services\StoreJsonContext.cs") -Raw
Assert-Contains $storeJsonContext "internal sealed partial class StoreJsonContext : JsonSerializerContext" `
    "StoreJsonContext declaration contract changed."
Assert-Contains $storeJsonContext "JsonSerializable(typeof(Dictionary<string, List<VerifyRecord>>))" `
    "StoreJsonContext verify-history root registration changed."
$verifyHistorySource = Get-Content -LiteralPath `
    (Join-Path $repoRoot "CUETools.App.Core\Accuracy\VerifyHistory.cs") -Raw
Assert-Contains $verifyHistorySource "StoreJsonContext.Default.VerifyRecord" `
    "VerifyHistory serializer-context usage changed."

$cueConfig = Get-Content -LiteralPath (Join-Path $repoRoot "CUETools.Processor\CUEConfig.cs") -Raw
$format = Get-Content -LiteralPath (Join-Path $repoRoot "CUETools.Codecs\CUEToolsFormat.cs") -Raw
Assert-Contains $cueConfig "Dictionary<string, CUEToolsFormat> formats" `
    "Configured-format collection contract changed."
Assert-Contains $format "bool allowLossless" "Lossless format capability contract changed."
Assert-Contains $format "AudioDecoderSettingsViewModel decoder" "Decoder capability contract changed."

$metadata = Get-Content -LiteralPath (Join-Path $repoRoot "CUETools.Processor\CUEMetadata.cs") -Raw
foreach ($member in @("DiscNumber01", "DiscNumberAndTotal", "DiscNumberAndName")) {
    Assert-Contains $metadata $member "CUEMetadata naming contract changed: $member"
}
$advanced = Get-Content -LiteralPath `
    (Join-Path $repoRoot "CUETools.Processor\CUEConfigAdvanced.cs") -Raw
Assert-True ($advanced -match "(?s)enum CTDBCoversSearch.*?None.*?Primary.*?Extensive") `
    "Artwork search-mode ordering changed."

# killed 7 + timeout 1 = 8 detected of 10 eligible: pins that a timeout counts as a
# detection, exactly on the reviewed floor.
$passingMeasurement = [pscustomobject]@{
    Eligible = 10
    Score = 80.0
    Counts = [pscustomobject]@{ killed = 7; timeout = 1; noCoverage = 1 }
}
Assert-True ((Get-MutationFailureReason 0 $passingMeasurement 80.0 1).Length -eq 0) `
    "Mutation gate rejected a measurement exactly on both reviewed boundaries."
Assert-Contains (Get-MutationFailureReason 0 $passingMeasurement 80.01 1) `
    "below the reviewed floor" "Mutation score regression did not fail closed."
Assert-Contains (Get-MutationFailureReason 0 $passingMeasurement 79.0 0) `
    "exceeds the reviewed ceiling" "Mutation coverage regression did not fail closed."
$emptyMeasurement = [pscustomobject]@{
    Eligible = 0
    Score = 0.0
    Counts = [pscustomobject]@{ killed = 0; timeout = 0; noCoverage = 0 }
}
Assert-Contains (Get-MutationFailureReason 0 $emptyMeasurement 1.0 0) `
    "did not execute and detect" "An empty mutation campaign did not fail closed."

$packetPath = Join-Path ([IO.Path]::GetTempPath()) `
    ("cuetools-mutation-packet-" + [Guid]::NewGuid().ToString("N") + ".json")
try {
    Write-MutationFailurePacket $packetPath "self-test" "dotnet test" 9 `
        "test.log" "report.json" $passingMeasurement.Counts "expected failure" "Full"
    $packet = Get-Content -LiteralPath $packetPath -Raw | ConvertFrom-Json
    Assert-True ($packet.schema -ceq "cuetools.mutation-failure.v1") `
        "Mutation failure packet schema drifted."
    Assert-True ($packet.profile -ceq "self-test" -and $packet.exitCode -eq 9) `
        "Mutation failure packet identity drifted."
    Assert-True ($packet.counts.killed -eq 7 -and $packet.counts.timeout -eq 1 -and `
        $packet.reason -ceq "expected failure") `
        "Mutation failure packet evidence drifted."
    Assert-Contains ([string]$packet.replay) "-Mode Full -Profile self-test" `
        "Mutation failure packet replay command drifted."
} finally {
    if (Test-Path -LiteralPath $packetPath -PathType Leaf) {
        Remove-Item -LiteralPath $packetPath -Force
    }
}

if ($Build) {
    foreach ($profile in $manifest.profiles) {
        $testProject = Join-Path $mutationRoot ([string]$profile.testProject)
        & dotnet test $testProject --configuration Release --nologo --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw "Mutation profile $($profile.id) baseline tests failed with exit code $LASTEXITCODE."
        }
    }
}

Write-Host "Mutation harness contract PASS: profiles=$(@($manifest.profiles).Count), tool=$($manifest.toolVersion), linked-sources=$(@($manifest.profiles.expectedSources).Count), linked-tests=$(@($manifest.profiles.expectedTests).Count), build=$Build"
