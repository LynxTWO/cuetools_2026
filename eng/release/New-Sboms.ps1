[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$ToolDirectory,
    [string]$PackageName = "CUETools 2026",
    [string]$PackageVersion = "2026.1.0",
    [string]$CycloneInputPath,
    [string]$CycloneFramework = "net8.0-windows",
    [string]$CycloneRuntime = "win-x64",
    [string]$OutputStem = "cuetools-wpf"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ([string]::IsNullOrWhiteSpace($CycloneInputPath)) {
    $CycloneInputPath = Join-Path $repoRoot "CUETools.Wpf\CUETools.Wpf.csproj"
}
$CycloneInputPath = [IO.Path]::GetFullPath($CycloneInputPath)
if ([string]::IsNullOrWhiteSpace($ToolDirectory)) {
    $ToolDirectory = Join-Path ([IO.Path]::GetTempPath()) "cuetools-sbom-tools"
}
$ToolDirectory = [IO.Path]::GetFullPath($ToolDirectory)

if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "Artifact directory does not exist: $ArtifactDirectory"
}
if (-not (Test-Path -LiteralPath $CycloneInputPath -PathType Leaf)) {
    throw "CycloneDX input does not exist: $CycloneInputPath"
}
if ([string]::IsNullOrWhiteSpace($OutputStem) -or
    $OutputStem.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
    throw "OutputStem must be a safe non-empty file name."
}
foreach ($directory in @($OutputDirectory, $ToolDirectory)) {
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }
}

$cycloneDx = Join-Path $ToolDirectory "dotnet-CycloneDX.exe"
if (-not (Test-Path -LiteralPath $cycloneDx -PathType Leaf)) {
    & dotnet tool install --tool-path $ToolDirectory CycloneDX --version 6.2.0
    if ($LASTEXITCODE -ne 0) { throw "Failed to install CycloneDX 6.2.0." }
}
$cycloneVersion = (& $cycloneDx --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or
    -not ($cycloneVersion -eq "6.2.0" -or $cycloneVersion.StartsWith("6.2.0+"))) {
    throw "Expected CycloneDX 6.2.0, found '$cycloneVersion'. Remove the pinned tool directory and retry."
}
$sbomTool = Join-Path $ToolDirectory "sbom-tool.exe"
if (-not (Test-Path -LiteralPath $sbomTool -PathType Leaf)) {
    & dotnet tool install --tool-path $ToolDirectory Microsoft.Sbom.DotNetTool --version 4.1.5
    if ($LASTEXITCODE -ne 0) { throw "Failed to install Microsoft.Sbom.DotNetTool 4.1.5." }
}
$sbomVersion = (& $sbomTool --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $sbomVersion -ne "4.1.5") {
    throw "Expected Microsoft.Sbom.DotNetTool 4.1.5, found '$sbomVersion'. Remove the pinned tool directory and retry."
}

$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$commitTimeRaw = (& git -C $repoRoot show -s --format=%cI HEAD).Trim()
$commitTime = ([DateTimeOffset]::Parse($commitTimeRaw)).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

$cyclonePath = Join-Path $OutputDirectory "$OutputStem.cdx.json"
if (Test-Path -LiteralPath $cyclonePath) {
    Remove-Item -LiteralPath $cyclonePath -Force
}
$cycloneArguments = @(
    $CycloneInputPath,
    "--framework", $CycloneFramework,
    "--output", $OutputDirectory,
    "--filename", [IO.Path]::GetFileName($cyclonePath),
    "--output-format", "Json",
    "--exclude-dev",
    "--disable-package-restore",
    "--no-serial-number",
    "--set-name", $PackageName,
    "--set-version", $PackageVersion,
    "--spec-version", "1.6"
)
if ([IO.Path]::GetExtension($CycloneInputPath) -ieq ".csproj") {
    $cycloneArguments += @("--recursive", "--include-project-references")
    if (-not [string]::IsNullOrWhiteSpace($CycloneRuntime)) {
        $cycloneArguments += @("--runtime", $CycloneRuntime)
    }
}
& $cycloneDx @cycloneArguments
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $cyclonePath)) {
    throw "CycloneDX generation failed."
}

# CycloneDX emits the wall-clock generation time. Replace only that metadata field with the source
# commit time so repeated generation from the same restored graph is byte-stable.
$cycloneJson = [IO.File]::ReadAllText($cyclonePath)
$timestampPattern = '("timestamp"\s*:\s*")[^"]+(")'
if ([Text.RegularExpressions.Regex]::Matches($cycloneJson, $timestampPattern).Count -ne 1) {
    throw "CycloneDX output did not contain exactly one metadata timestamp."
}
$cycloneJson = [Text.RegularExpressions.Regex]::Replace(
    $cycloneJson,
    $timestampPattern,
    ('${1}' + $commitTime + '${2}'))
[IO.File]::WriteAllText($cyclonePath, $cycloneJson, (New-Object Text.UTF8Encoding($false)))
$null = Get-Content -LiteralPath $cyclonePath -Raw | ConvertFrom-Json

$spdxRoot = Join-Path $OutputDirectory "spdx"
if (-not (Test-Path -LiteralPath $spdxRoot)) {
    New-Item -ItemType Directory -Path $spdxRoot | Out-Null
}
# Keep both the file inventory and component detector scoped to the release artifact. Pointing
# BuildComponentPath at the repository makes prior evidence under bin/Release an input to the next
# run; the project dependency graph is already captured independently by CycloneDX above.
& $sbomTool generate `
    -b $ArtifactDirectory `
    -bc $ArtifactDirectory `
    -m $spdxRoot `
    -pn $PackageName `
    -pv $PackageVersion `
    -ps "CUETools contributors" `
    -nsu $commit `
    -gt $commitTime `
    -D true `
    -F false `
    -P 2 `
    -mi SPDX:2.2 `
    -V Error
if ($LASTEXITCODE -ne 0) {
    throw "SPDX generation failed."
}
$spdxPath = Get-ChildItem -LiteralPath $spdxRoot -Filter "manifest.spdx.json" -File -Recurse |
    Select-Object -ExpandProperty FullName -First 1
if ([string]::IsNullOrWhiteSpace($spdxPath)) {
    throw "SPDX tool succeeded without producing manifest.spdx.json."
}

function Get-StableGuid([string]$Seed) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Seed)
        $hex = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace("-", "").ToLowerInvariant()
        return $hex.Substring(0, 8) + "-" +
            $hex.Substring(8, 4) + "-" +
            $hex.Substring(12, 4) + "-" +
            $hex.Substring(16, 4) + "-" +
            $hex.Substring(20, 12)
    }
    finally { $sha.Dispose() }
}

# sbom-tool creates random document/SWID UUIDs even when its namespace suffix and timestamp are
# supplied. Normalize only those two UUID fields to hashes of immutable build identity. Parallelism
# is fixed at the tool's minimum above, then a package-free .NET guard canonicalizes unordered
# SPDX collections without PowerShell 5.1 collapsing or wrapping one-element JSON arrays.
$spdxJson = [IO.File]::ReadAllText($spdxPath)
$guidPattern = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'
$documentGuid = Get-StableGuid "$commit`n$PackageName`n$PackageVersion`ndocument"
$swidGuid = Get-StableGuid "$commit`n$PackageName`n$PackageVersion`nswid"
$documentPattern = '(sbom-tool-4\.1\.5-)' + $guidPattern
$swidPattern = '(tag_id=)' + $guidPattern
if ([Text.RegularExpressions.Regex]::Matches($spdxJson, $documentPattern).Count -ne 1 -or
    [Text.RegularExpressions.Regex]::Matches($spdxJson, $swidPattern).Count -ne 1) {
    throw "SPDX output did not contain exactly one document UUID and one SWID tag UUID."
}
$spdxJson = [Text.RegularExpressions.Regex]::Replace(
    $spdxJson,
    $documentPattern,
    ('${1}' + $documentGuid))
$spdxJson = [Text.RegularExpressions.Regex]::Replace(
    $spdxJson,
    $swidPattern,
    ('${1}' + $swidGuid))
[IO.File]::WriteAllText(
    $spdxPath,
    $spdxJson,
    (New-Object Text.UTF8Encoding($false)))

$sbomGuardProject = Join-Path $PSScriptRoot "SbomGuard\SbomGuard.csproj"
& dotnet run `
    --project $sbomGuardProject `
    --configuration Release `
    -- `
    canonicalize `
    $spdxPath
if ($LASTEXITCODE -ne 0) {
    throw "SPDX canonicalization failed."
}
$spdxSidecarPath = "$spdxPath.sha256"
$spdxHash = (
    Get-FileHash -LiteralPath $spdxPath -Algorithm SHA256
).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
    $spdxSidecarPath,
    $spdxHash,
    (New-Object Text.UTF8Encoding($false)))

& dotnet run `
    --project $sbomGuardProject `
    --configuration Release `
    --no-build `
    -- `
    validate-spdx `
    $ArtifactDirectory `
    $spdxPath `
    $PackageName `
    $PackageVersion
if ($LASTEXITCODE -ne 0) {
    throw "SPDX semantic validation failed."
}
& dotnet run `
    --project $sbomGuardProject `
    --configuration Release `
    --no-build `
    -- `
    validate-cyclonedx `
    $cyclonePath `
    $PackageName `
    $PackageVersion
if ($LASTEXITCODE -ne 0) {
    throw "CycloneDX semantic validation failed."
}

$spdxValidationPath = Join-Path $ToolDirectory (
    "$OutputStem-spdx-validation-" + [Guid]::NewGuid().ToString("N") + ".json")
& $sbomTool validate `
    -b $ArtifactDirectory `
    -m (Join-Path $spdxRoot "_manifest") `
    -o $spdxValidationPath `
    -F false `
    -P 2 `
    -mi SPDX:2.2 `
    -V Error
if ($LASTEXITCODE -ne 0 -or
    -not (Test-Path -LiteralPath $spdxValidationPath -PathType Leaf)) {
    throw "Microsoft SPDX artifact validation failed."
}
$spdxValidation = Get-Content -LiteralPath $spdxValidationPath -Raw |
    ConvertFrom-Json
if ([string]$spdxValidation.Result -ne "Success" -or
    [int]$spdxValidation.ValidationErrors.Count -ne 0 -or
    [int]$spdxValidation.Summary.ValidationTelemetery.FilesFailedCount -ne 0 -or
    [int]$spdxValidation.Summary.ValidationTelemetery.FilesSuccessfulCount -ne
        [int]$spdxValidation.Summary.ValidationTelemetery.TotalFilesInManifest) {
    throw "Microsoft SPDX validator did not report an exact successful artifact closure."
}

Write-Host "CycloneDX SBOM: $cyclonePath"
Write-Host "SPDX SBOM: $spdxPath"
