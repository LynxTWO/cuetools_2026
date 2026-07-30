[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$policyPath = Join-Path $PSScriptRoot "signing-policy.json"
$signingScriptPath = Join-Path $PSScriptRoot "Invoke-ArtifactSigning.ps1"
$workflowPath = Join-Path $repoRoot ".github\workflows\release-windows.yml"

$policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json
$signingScript = Get-Content -LiteralPath $signingScriptPath -Raw
$workflow = Get-Content -LiteralPath $workflowPath -Raw
$checks = 0
function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

Require ([int]$policy.schemaVersion -eq 1) "Unsupported signing policy schema."
Require `
    ([string]$policy.policyId -ceq "cuetools-windows-authenticode-v1") `
    "The signing policy identity drifted."
Require `
    ([string]$policy.fileDigest -ceq "SHA256" -and
        [string]$policy.timestampDigest -ceq "SHA256") `
    "The signing policy must use SHA-256 for file and timestamp digests."
Require `
    ([string]$policy.timestampUrl -ceq "http://timestamp.digicert.com") `
    "The reviewed RFC 3161 timestamp authority drifted."
Require `
    ([string]$policy.codeSigningEku -ceq "1.3.6.1.5.5.7.3.3") `
    "The signing policy does not require the code-signing EKU."
Require `
    ([string]$policy.productionRefPrefix -ceq "refs/tags/v") `
    "Production signing is not bound to tag refs."
Require (@($policy.profiles).Count -eq 2) "Both release artifact profiles are required."

$profileIds = @($policy.profiles.id | Sort-Object -Unique)
Require `
    ($profileIds.Count -eq 2 -and
        $profileIds -contains "classic-win" -and
        $profileIds -contains "wpf-win-x64") `
    "The classic and WPF signing profiles are not both present."

foreach ($profile in @($policy.profiles)) {
    $contractPath = [IO.Path]::GetFullPath(
        (Join-Path $repoRoot ([string]$profile.contractPath)))
    Require `
        (Test-Path -LiteralPath $contractPath -PathType Leaf) `
        "Signing profile '$($profile.id)' has no artifact contract."
    $contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
    $selected = New-Object "Collections.Generic.List[object]"
    foreach ($required in @($contract.requiredFiles)) {
        $relativePath = ([string]$required.path).Replace("\", "/")
        $matches = @(
            $profile.includePathRegexes |
                Where-Object { $relativePath -cmatch [string]$_ })
        if ($matches.Count -eq 0) { continue }
        $sha256Property = $required.PSObject.Properties["sha256"]
        Require `
            ($null -eq $sha256Property -or
                [string]::IsNullOrWhiteSpace(
                    [string]$sha256Property.Value)) `
            "Signing profile '$($profile.id)' selects hash-pinned upstream file $relativePath."
        Require `
            ($relativePath -cmatch "\.(?:exe|dll)$") `
            "Signing profile '$($profile.id)' selects non-PE path $relativePath."
        $selected.Add($required)
    }
    Require `
        ($selected.Count -ge [int]$profile.minimumSelectedFiles) `
        "Signing profile '$($profile.id)' selects only $($selected.Count) files."
    Require `
        ([bool]$profile.regeneratePluginManifest) `
        "Signing profile '$($profile.id)' does not rebuild its plugin hash manifest."
}

foreach ($scriptContract in @(
    "Import-PfxCertificate",
    "/fd ([string]`$policy.fileDigest)",
    "/tr ([string]`$policy.timestampUrl)",
    "/td ([string]`$policy.timestampDigest)",
    "verify /pa /all /tw",
    "TimeStamperCertificate",
    "Write-PluginManifest.ps1",
    "unsigned-evaluation",
    "CUETOOLS_SIGNING_PFX_BASE64 is unavailable",
    "Assert-NoReparsePointInExistingPath")) {
    Require `
        ($signingScript.Contains($scriptContract)) `
        "The signing implementation is missing '$scriptContract'."
}

Require `
    ($workflow.Contains("environment: release-signing") -and
        $workflow.Contains("CUETOOLS_SIGNING_PFX_BASE64") -and
        $workflow.Contains("CUETOOLS_SIGNING_PFX_PASSWORD") -and
        $workflow.Contains("CUETOOLS_SIGNING_SUBJECT_PATTERN")) `
    "The release workflow does not use the protected signing environment contract."
Require `
    ($workflow.Contains("startsWith(github.ref, 'refs/tags/v')") -and
        $workflow.Contains("inputs.sign_release")) `
    "The release workflow does not require signing for tags or explicit release dispatches."
Require `
    ($workflow.Contains("Upload retained release failure evidence") -and
        $workflow.Contains("if: failure()") -and
        $workflow.Contains('release-failure-evidence-${{ github.run_id }}') -and
        $workflow.Contains("bin/Release/evidence/")) `
    "The release workflow does not preserve retained failure evidence."

$signingIndex = $workflow.IndexOf(
    "Invoke-ArtifactSigning.ps1",
    [StringComparison]::Ordinal)
$provenanceIndex = $workflow.IndexOf(
    "New-Provenance.ps1",
    [StringComparison]::Ordinal)
$sbomIndex = $workflow.IndexOf(
    "New-Sboms.ps1",
    [StringComparison]::Ordinal)
Require `
    ($signingIndex -ge 0 -and
        $provenanceIndex -gt $signingIndex -and
        $sbomIndex -gt $signingIndex) `
    "Signing must precede artifact provenance and SBOM generation."

Write-Host "Signing policy checks passed: $checks"
