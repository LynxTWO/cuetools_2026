[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$policyPath = Join-Path $repoRoot "Directory.Build.props"
[xml]$policy = Get-Content -LiteralPath $policyPath -Raw
$projectListNode = $policy.SelectSingleNode(
    "/Project/PropertyGroup/CUEToolsPackageLockProjects")
if ($null -eq $projectListNode) {
    throw "The first-party NuGet lock policy is missing."
}
$declared = @(
    ([string]$projectListNode.InnerText).Split(
        [char[]]@(";"),
        [StringSplitOptions]::RemoveEmptyEntries) |
        Sort-Object -Unique
)
if ($declared.Count -eq 0) {
    throw "The first-party NuGet lock policy is empty."
}

$projects = @(
    Get-ChildItem -LiteralPath $repoRoot -Filter *.csproj -File -Recurse |
        Where-Object {
            $_.FullName -notlike (Join-Path $repoRoot "ThirdParty\*") -and
            $_.FullName -notlike (Join-Path $repoRoot "obj\*") -and
            $_.FullName -notlike (Join-Path $repoRoot "bin\*")
        } |
        Where-Object {
            [xml]$projectXml = Get-Content -LiteralPath $_.FullName -Raw
            @($projectXml.SelectNodes(
                "//*[local-name()='PackageReference']")).Count -gt 0
        }
)

$observed = @($projects.BaseName | Sort-Object -Unique)
$missingPolicy = @($observed | Where-Object { $_ -notin $declared })
$stalePolicy = @($declared | Where-Object { $_ -notin $observed })
if ($missingPolicy.Count -gt 0 -or $stalePolicy.Count -gt 0) {
    throw (
        "NuGet lock policy drift. Missing policy: [{0}]. Stale policy: [{1}]." -f
        ($missingPolicy -join ", "),
        ($stalePolicy -join ", "))
}

foreach ($project in $projects) {
    $lockPath = Join-Path $project.DirectoryName "packages.lock.json"
    if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
        throw "Missing NuGet lock file for $($project.FullName)."
    }
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    if ([int]$lock.version -lt 1 -or $null -eq $lock.dependencies) {
        throw "Invalid NuGet lock file: $lockPath"
    }
}

$vendorLocks = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "ThirdParty") `
        -Filter packages.lock.json -File -Recurse -ErrorAction SilentlyContinue
)
if ($vendorLocks.Count -gt 0) {
    throw "First-party restore wrote a lock file into ThirdParty."
}

$targetsPath = Join-Path $repoRoot "Directory.Build.targets"
[xml]$targets = Get-Content -LiteralPath $targetsPath -Raw
$eligibilityNode = $targets.SelectSingleNode(
    "/Project/PropertyGroup/_AdcNet47ResxEligible")
if ($null -eq $eligibilityNode -or
    $eligibilityNode.Condition -notlike "*TargetFramework*==*net47*" -or
    $eligibilityNode.Condition -like "*TargetFrameworkVersion*") {
    throw "Core resource support must be limited to SDK target-framework evaluation."
}
$testHelperProjectPath = Join-Path $repoRoot `
    "CUETools.TestHelpers\CUETools.TestHelpers.csproj"
[xml]$testHelperProject = Get-Content -LiteralPath `
    $testHelperProjectPath -Raw
$testHelperOptOut = $testHelperProject.SelectSingleNode(
    "/*[local-name()='Project']/*[local-name()='PropertyGroup']/" +
    "*[local-name()='CUEToolsDisableCoreResxExtension']")
if ($null -eq $testHelperOptOut -or
    [string]$testHelperOptOut.InnerText -cne "true") {
    throw "The resource-free test helper must explicitly opt out of the Core resource package."
}
$packageNodes = @($targets.SelectNodes(
    "/Project/ItemGroup/PackageReference"))
$coreResourcePackage = @(
    $packageNodes | Where-Object {
        $_.Include -eq "System.Resources.Extensions" -and
        $_.ParentNode.Condition -like "*_AdcCoreNet47Resx*"
    }
)
$fullResourcePackage = @(
    $packageNodes | Where-Object {
        $_.Include -eq "System.Resources.Extensions" -and
        $_.ParentNode.Condition -like "*MSBuildRuntimeType*!=*Core*" -and
        $_.ExcludeAssets -eq "all" -and
        [string]::IsNullOrEmpty($_.GetAttribute("PrivateAssets"))
    }
)
$fullNet20ReferencePackage = @(
    $packageNodes | Where-Object {
        $_.Include -eq "Microsoft.NETFramework.ReferenceAssemblies" -and
        $_.ParentNode.Condition -like "*TargetFramework*net20*" -and
        $_.ParentNode.Condition -like "*MSBuildRuntimeType*!=*Core*" -and
        $_.ExcludeAssets -eq "all" -and
        $_.PrivateAssets -eq "all"
    }
)
if ($coreResourcePackage.Count -ne 1 -or
    $fullResourcePackage.Count -ne 1 -or
    $fullNet20ReferencePackage.Count -ne 1) {
    throw (
        "Core/full restore-graph parity is incomplete. Core resource={0}, " +
        "full resource={1}, full net20 reference assemblies={2}." -f
        $coreResourcePackage.Count,
        $fullResourcePackage.Count,
        $fullNet20ReferencePackage.Count)
}

Write-Host "NuGet lock-file checks passed: $($projects.Count)"
