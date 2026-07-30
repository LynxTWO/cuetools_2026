[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [Parameter(Mandatory = $true)]
    [ValidateSet("Wpf", "Classic")]
    [string]$Flavor
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$releaseSafetyScript = Join-Path $PSScriptRoot "ReleaseSafety.ps1"
if (-not (Test-Path -LiteralPath $releaseSafetyScript -PathType Leaf)) {
    throw "Release safety helper does not exist: $releaseSafetyScript"
}
. $releaseSafetyScript
$nativeInventoryScript = Join-Path $PSScriptRoot "NativeDependencyInventory.ps1"
if (-not (Test-Path -LiteralPath $nativeInventoryScript -PathType Leaf)) {
    throw "Native dependency inventory helper does not exist: $nativeInventoryScript"
}
. $nativeInventoryScript

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "bin\Release"))
$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFullPath

if (-not (Test-SameOrDescendantPath `
        -CandidatePath $outputFullPath `
        -RootPath $allowedRoot) -or
    -not [string]::Equals(
        [IO.Path]::GetFileName($outputFullPath),
        "THIRD-PARTY-NOTICES.txt",
        [StringComparison]::Ordinal)) {
    throw "Third-party notices must be written as bin\\Release\\...\\THIRD-PARTY-NOTICES.txt."
}
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    throw "Third-party notices output directory does not exist: $outputDirectory"
}
Assert-NoReparsePointInExistingPath `
    -Path $outputDirectory `
    -Purpose "Third-party notices output directory"
if (Test-Path -LiteralPath $outputFullPath) {
    Assert-NoReparsePointInExistingPath `
        -Path $outputFullPath `
        -Purpose "Third-party notices output file"
}

function ConvertTo-NormalizedText {
    param([Parameter(Mandatory = $true)][string]$Text)

    return ($Text -replace "\r\n|\r|\n", "`r`n").TrimEnd("`r", "`n")
}

function Read-TrackedText {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $path = [IO.Path]::GetFullPath((Join-Path $repoRoot $RelativePath))
    if (-not (Test-SameOrDescendantPath -CandidatePath $path -RootPath $repoRoot) -or
        -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Third-party license source is missing or escapes the repository: $RelativePath"
    }
    return ConvertTo-NormalizedText ([IO.File]::ReadAllText($path))
}

function Read-LicenseSection {
    param([Parameter(Mandatory = $true)][string]$Heading)

    $licensePath = Join-Path $repoRoot "License.txt"
    $lines = [IO.File]::ReadAllLines($licensePath)
    $headingLine = "${Heading}:"
    $headingIndex = -1
    for ($index = 0; $index -lt $lines.Length; $index++) {
        if ([string]::Equals(
            $lines[$index].Trim(),
            $headingLine,
            [StringComparison]::Ordinal)) {
            $headingIndex = $index
            break
        }
    }
    if ($headingIndex -lt 0) {
        throw "License.txt does not contain the expected '$headingLine' section."
    }

    $startIndex = $headingIndex - 1
    while ($startIndex -ge 0 -and $lines[$startIndex] -notmatch "^\*{80}$") {
        $startIndex--
    }
    $endIndex = $headingIndex + 1
    while ($endIndex -lt $lines.Length -and $lines[$endIndex] -notmatch "^\*{80}$") {
        $endIndex++
    }
    if ($startIndex -lt 0 -or $endIndex -ge $lines.Length) {
        throw "License.txt '$headingLine' section delimiters are malformed."
    }

    return ($lines[$startIndex..$endIndex] -join "`r`n")
}

function Read-MonkeysAudioLicense {
    $archivePath = Join-Path $repoRoot "ThirdParty\MAC_SDK\MAC_1320_SDK.zip"
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        throw "Monkey's Audio SDK archive is missing: $archivePath"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entry = $archive.Entries |
            Where-Object { $_.FullName -eq "License.txt" } |
            Select-Object -First 1
        if ($null -eq $entry) {
            throw "Monkey's Audio SDK archive does not contain License.txt."
        }
        $stream = $entry.Open()
        try {
            $reader = New-Object IO.StreamReader(
                $stream,
                [Text.Encoding]::UTF8,
                $true)
            try {
                return ConvertTo-NormalizedText $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Add-PropertyLine {
    param(
        [Collections.Generic.List[string]]$Lines,
        [Parameter(Mandatory = $true)]
        [object]$Artifact,
        [Parameter(Mandatory = $true)]
        [string]$PropertyName,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $property = $Artifact.PSObject.Properties[$PropertyName]
    if ($null -ne $property -and
        -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        $Lines.Add("${Label}: $($property.Value)")
    }
}

function Add-LicenseText {
    param(
        [Collections.Generic.List[string]]$Lines,
        [Parameter(Mandatory = $true)]
        [string]$Title,
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    $Lines.Add("")
    $Lines.Add(("=" * 80))
    $Lines.Add($Title)
    $Lines.Add("Exact text source: $Source")
    $Lines.Add(("-" * 80))
    $Lines.Add((ConvertTo-NormalizedText $Text))
}

$inventoryPath = Join-Path $PSScriptRoot "native-dependencies.json"
$inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
Assert-PinnedSourceArchiveBinding `
    -Inventory $inventory `
    -RepositoryRoot $repoRoot `
    -ArtifactId "monkeys-audio" `
    -PinnedRelativePath "ThirdParty/MAC_SDK/MAC_1320_SDK.zip"
$artifactById = @{}
foreach ($artifact in @($inventory.artifacts)) {
    $artifactById[[string]$artifact.id] = $artifact
}

$artifactIds = @(
    "libflac-dynamic",
    "wavpack",
    "monkeys-audio",
    "hdcd",
    "libmp3lame"
)
if ($Flavor -eq "Classic") {
    $artifactIds += @("tta-cpp-cli", "unrar")
}

$lines = New-Object "Collections.Generic.List[string]"
$lines.Add("CUETools third-party notices")
$lines.Add("")
$lines.Add("GENERATED FILE - DO NOT EDIT.")
$lines.Add("Generator: eng/release/New-ThirdPartyNotices.ps1")
$lines.Add("Artifact flavor: $Flavor")
$lines.Add("Inventory: eng/release/native-dependencies.json")
$lines.Add("")
$lines.Add(
    "Scope: native, mixed-mode, and command-line codec components shipped by this artifact. " +
    "Managed-package attribution remains represented by the release SBOM. " +
    "CUETools' own license is in License.txt.")
$lines.Add("")
$lines.Add("Component inventory")
$lines.Add(("-" * 80))

foreach ($artifactId in $artifactIds) {
    if (-not $artifactById.ContainsKey($artifactId)) {
        throw "Native dependency inventory is missing notices component: $artifactId"
    }
    $artifact = $artifactById[$artifactId]
    $lines.Add("Component: $($artifact.id)")
    $lines.Add("Version: $($artifact.version)")
    $lines.Add("Source status: $($artifact.sourceStatus)")
    Add-PropertyLine -Lines $lines -Artifact $artifact -PropertyName "upstream" -Label "Upstream"
    $sourceArchiveProperty = $artifact.PSObject.Properties["sourceArchive"]
    if ($null -ne $sourceArchiveProperty) {
        $sourceArchive = $sourceArchiveProperty.Value
        if ($sourceArchive -is [string]) {
            $lines.Add("Source archive: $sourceArchive")
        }
        else {
            $lines.Add(
                "Source archive: $($sourceArchive.url) " +
                "(SHA-256 $($sourceArchive.sha256))")
        }
    }
    Add-PropertyLine -Lines $lines -Artifact $artifact -PropertyName "binaryDistributor" -Label "Binary distributor"
    Add-PropertyLine -Lines $lines -Artifact $artifact -PropertyName "binaryOrigin" -Label "Binary origin"
    Add-PropertyLine -Lines $lines -Artifact $artifact -PropertyName "binaryOriginAtImport" -Label "Binary origin at import"
    Add-PropertyLine -Lines $lines -Artifact $artifact -PropertyName "binaryOriginStatus" -Label "Binary origin status"
    Add-PropertyLine -Lines $lines -Artifact $artifact -PropertyName "license" -Label "License"
    Add-PropertyLine -Lines $lines -Artifact $artifact -PropertyName "licenseSource" -Label "License source"
    $binaryArchivesProperty = $artifact.PSObject.Properties["binaryArchives"]
    if ($null -ne $binaryArchivesProperty) {
        foreach ($binaryArchive in @($binaryArchivesProperty.Value)) {
            $lines.Add(
                "Binary archive ($($binaryArchive.architecture)): " +
                "$($binaryArchive.url) (SHA-256 $($binaryArchive.sha256))")
        }
    }
    $lines.Add("Package paths: $(@($artifact.packagePaths) -join ', ')")
    $lines.Add("")
}

if ($Flavor -eq "Wpf") {
    $externalManifestPath = Join-Path $PSScriptRoot "external-command-encoders.json"
    $externalManifest =
        Get-Content -LiteralPath $externalManifestPath -Raw |
            ConvertFrom-Json
    if ($externalManifest.schemaVersion -ne 1 -or
        @($externalManifest.encoders).Count -eq 0) {
        throw "External command encoder manifest is empty or unsupported."
    }
    foreach ($encoder in @($externalManifest.encoders)) {
        $lines.Add("Component: $($encoder.id)")
        $lines.Add("Name: $($encoder.displayName)")
        $lines.Add("Version: $($encoder.version)")
        $lines.Add("Upstream: $($encoder.upstream)")
        $lines.Add("License: $($encoder.license)")
        $binaryPathProperty = $encoder.PSObject.Properties["binaryPath"]
        if ($null -ne $binaryPathProperty) {
            $lines.Add(
                "Source-built executable: $($binaryPathProperty.Value) " +
                "(SHA-256 $($encoder.executableSha256))")
        }
        else {
            $lines.Add(
                "Binary archive: $($encoder.binaryArchive.url) " +
                "(SHA-256 $($encoder.binaryArchive.sha256))")
        }
        $lines.Add(
            "Executable SHA-256: $($encoder.executableSha256)")
        $lines.Add(
            "Source archive: $($encoder.sourceArchive.url) " +
            "(SHA-256 $($encoder.sourceArchive.sha256))")
        $linkedLibrarySourceProperty =
            $encoder.PSObject.Properties["linkedLibrarySource"]
        if ($null -ne $linkedLibrarySourceProperty) {
            $linkedLibrarySource = $linkedLibrarySourceProperty.Value
            $lines.Add(
                "Linked library source: $($linkedLibrarySource.name) " +
                "$($linkedLibrarySource.version), $($linkedLibrarySource.url) " +
                "(SHA-256 $($linkedLibrarySource.sha256))")
        }
        $lines.Add("Package path: $($encoder.packagePath)")
        if (-not [string]::IsNullOrWhiteSpace(
                [string]$encoder.sourceArchive.packagePath)) {
            $lines.Add(
                "Packaged corresponding source: " +
                $encoder.sourceArchive.packagePath)
        }
        $sourceSupportProperty =
            $encoder.PSObject.Properties["sourceSupport"]
        if ($null -ne $sourceSupportProperty) {
            foreach ($support in @($sourceSupportProperty.Value)) {
                $lines.Add(
                    "Packaged build support: $($support.packagePath) " +
                    "(SHA-256 $($support.sha256))")
            }
        }
        $lines.Add("Provenance: $($encoder.provenanceNote)")
        $lines.Add("")
    }
}

Add-LicenseText `
    -Lines $lines `
    -Title "libFLAC 1.5.0 - BSD-3-Clause" `
    -Source "ThirdParty/flac/COPYING.Xiph" `
    -Text (Read-TrackedText "ThirdParty\flac\COPYING.Xiph")
Add-LicenseText `
    -Lines $lines `
    -Title "WavPack 5.9.0 - BSD-3-Clause" `
    -Source "ThirdParty/WavPack/license.txt" `
    -Text (Read-TrackedText "ThirdParty\WavPack\license.txt")
Add-LicenseText `
    -Lines $lines `
    -Title "Monkey's Audio 13.20 - BSD-3-Clause" `
    -Source "License.txt inside ThirdParty/MAC_SDK/MAC_1320_SDK.zip" `
    -Text (Read-MonkeysAudioLicense)
Add-LicenseText `
    -Lines $lines `
    -Title "Christopher Key HDCD decoder - custom redistribution license" `
    -Source "License.txt, hdcd.dll section" `
    -Text (Read-LicenseSection "hdcd.dll")
Add-LicenseText `
    -Lines $lines `
    -Title "LAME 3.100 - upstream use and attribution notice" `
    -Source "eng/release/licenses/LAME-3.100-LICENSE.txt" `
    -Text (Read-TrackedText "eng\release\licenses\LAME-3.100-LICENSE.txt")
Add-LicenseText `
    -Lines $lines `
    -Title "LAME 3.100 - GNU Library General Public License 2.0 or later" `
    -Source "eng/release/licenses/GNU-LGPL-2.0.txt" `
    -Text (Read-TrackedText "eng\release\licenses\GNU-LGPL-2.0.txt")

if ($Flavor -eq "Wpf") {
    Add-LicenseText `
        -Lines $lines `
        -Title "Opus Tools 0.2 opusenc - BSD-2-Clause" `
        -Source "eng/release/licenses/OpusTools-opusenc-BSD-2-Clause.txt" `
        -Text (Read-TrackedText "eng\release\licenses\OpusTools-opusenc-BSD-2-Clause.txt")
    Add-LicenseText `
        -Lines $lines `
        -Title "libopus 1.3 - BSD-3-Clause" `
        -Source "eng/release/licenses/libopus-1.3-BSD-3-Clause.txt" `
        -Text (Read-TrackedText "eng\release\licenses\libopus-1.3-BSD-3-Clause.txt")
    Add-LicenseText `
        -Lines $lines `
        -Title "oggenc2 2.88 - GNU General Public License 2.0" `
        -Source "ttalib-1.1/COPYING (standard GPL-2.0 text)" `
        -Text (Read-TrackedText "ttalib-1.1\COPYING")
    Add-LicenseText `
        -Lines $lines `
        -Title "Musepack SV8 r495 compiled files - GNU Lesser General Public License 2.1 or later" `
        -Source "ThirdParty/flac/COPYING.LGPL (standard LGPL-2.1 text)" `
        -Text (Read-TrackedText "ThirdParty\flac\COPYING.LGPL")
}

if ($Flavor -eq "Classic") {
    Add-LicenseText `
        -Lines $lines `
        -Title "TTA library 1.1 - GNU General Public License 2.0 or later" `
        -Source "ttalib-1.1/COPYING" `
        -Text (Read-TrackedText "ttalib-1.1\COPYING")
    Add-LicenseText `
        -Lines $lines `
        -Title "UnRAR DLL 7.23 - freeware redistribution notice" `
        -Source "eng/release/licenses/UnRAR-DLL-7.23-LICENSE.txt" `
        -Text (Read-TrackedText "eng\release\licenses\UnRAR-DLL-7.23-LICENSE.txt")
}

$outputText = ($lines -join "`r`n") + "`r`n"
[IO.File]::WriteAllText(
    $outputFullPath,
    $outputText,
    (New-Object Text.UTF8Encoding($false)))
Write-Host "Third-party notices generated: $outputFullPath"
