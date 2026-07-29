[CmdletBinding()]
param(
    [string]$ArtifactDirectory,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$ContractPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$releaseSafetyScript = Join-Path $PSScriptRoot "ReleaseSafety.ps1"
if (-not (Test-Path -LiteralPath $releaseSafetyScript -PathType Leaf)) {
    throw "Release safety helper does not exist: $releaseSafetyScript"
}
. $releaseSafetyScript

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "bin\Release"))
if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $allowedRoot "CUETools2026-win-x64"
}
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
if ([string]::IsNullOrWhiteSpace($ContractPath)) {
    $ContractPath = Join-Path $PSScriptRoot "wpf-win-x64.manifest.json"
}
$ContractPath = [IO.Path]::GetFullPath($ContractPath)

$allowedPrefix = $allowedRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$artifactLeaf = Split-Path -Leaf $ArtifactDirectory
if (-not $ArtifactDirectory.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not $artifactLeaf.StartsWith("CUETools2026-", [StringComparison]::Ordinal)) {
    throw "Refusing to clean publish path outside bin\\Release\\CUETools2026-*: $ArtifactDirectory"
}

Assert-NoReparsePointInExistingPath `
    -Path $ArtifactDirectory `
    -Purpose "Publish artifact directory"
if (Test-Path -LiteralPath $ArtifactDirectory) {
    Assert-NoReparsePointInExistingPath `
        -Path $ArtifactDirectory `
        -Purpose "Publish artifact directory"
    # Validate every descendant before recursive cleanup. Windows PowerShell 5.1
    # Remove-Item behavior around directory junctions is not safe to rely on.
    $null = @(Get-VerifiedArtifactFiles -Root $ArtifactDirectory)
    Remove-Item -LiteralPath $ArtifactDirectory -Recurse -Force
}
Assert-NoReparsePointInExistingPath `
    -Path $ArtifactDirectory `
    -Purpose "Publish artifact directory"
New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
Assert-NoReparsePointInExistingPath `
    -Path $ArtifactDirectory `
    -Purpose "Publish artifact directory"

$externalEncoderScript = Join-Path $repoRoot "eng\ci\Prepare-ExternalCommandEncoders.ps1"
if (-not (Test-Path -LiteralPath $externalEncoderScript -PathType Leaf)) {
    throw "External command encoder preparation script is missing: $externalEncoderScript"
}
& $externalEncoderScript -RepositoryRoot $repoRoot

& dotnet publish (Join-Path $repoRoot "CUETools.Wpf\CUETools.Wpf.csproj") `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $ArtifactDirectory `
    --nologo `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) {
    throw "The clean WPF publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $repoRoot "License.txt") -Destination $ArtifactDirectory
$pluginInstaller = Join-Path $PSScriptRoot "Install-CUEToolsPlugin.ps1"
Copy-Item -LiteralPath $pluginInstaller -Destination $ArtifactDirectory
$noticesScript = Join-Path $PSScriptRoot "New-ThirdPartyNotices.ps1"
& $noticesScript `
    -OutputPath (Join-Path $ArtifactDirectory "THIRD-PARTY-NOTICES.txt") `
    -Flavor Wpf

$validatorProject = Join-Path $PSScriptRoot "ArtifactValidator\ArtifactValidator.csproj"
& dotnet run --project $validatorProject --configuration Release -- $ArtifactDirectory $ContractPath
if ($LASTEXITCODE -ne 0) {
    throw "The WPF artifact contract or production plugin-load probe failed."
}

Write-Host "Clean WPF publish validated: $ArtifactDirectory"
