[CmdletBinding()]
param(
    [switch]$CleanupStale
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$releaseRoot = Join-Path $repoRoot "bin\Release"
$testLeaf = "signing-harness-" + [Guid]::NewGuid().ToString("N")
$testRoot = Join-Path $releaseRoot $testLeaf
$artifactRoot = Join-Path $testRoot "artifact"
$contractPath = Join-Path $testRoot "contract.json"
$policyPath = Join-Path $testRoot "policy.json"
$outputPath = Join-Path $testRoot "evidence\signing-status.json"
$pfxPath = Join-Path $testRoot "test.pfx"
$certificate = $null

function Remove-SigningHarnessDirectory([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $expectedPrefix = $releaseRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith(
            $expectedPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($fullPath).StartsWith(
            "signing-harness-",
            [StringComparison]::Ordinal)) {
        throw "Refusing to clean unexpected signing harness path: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

if ($CleanupStale) {
    foreach ($staleDirectory in @(
        Get-ChildItem `
            -LiteralPath $releaseRoot `
            -Directory `
            -Filter "signing-harness-*" `
            -ErrorAction SilentlyContinue)) {
        Remove-SigningHarnessDirectory -Path $staleDirectory.FullName
    }
    Write-Host "Stale signing harness directories removed."
    return
}

New-Item -ItemType Directory -Path $artifactRoot | Out-Null
try {
    $probePath = Join-Path $artifactRoot "CUETools.SigningProbe.exe"
    $validatorProject = Join-Path $PSScriptRoot (
        "ArtifactValidator\ArtifactValidator.csproj")
    $validatorExecutable = Join-Path $PSScriptRoot (
        "ArtifactValidator\bin\Release\net8.0\ArtifactValidator.exe")
    if (-not (Test-Path -LiteralPath $validatorExecutable -PathType Leaf)) {
        & dotnet build $validatorProject -c Release --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Could not build the unsigned PE signing fixture."
        }
    }
    Copy-Item -LiteralPath $validatorExecutable -Destination $probePath
    if (
        (Get-AuthenticodeSignature -LiteralPath $probePath).Status -ne
            [Management.Automation.SignatureStatus]::NotSigned) {
        throw "The signing fixture is unexpectedly signed."
    }

    $contract = [ordered]@{
        schemaVersion = 1
        manifestId = "cuetools-signing-test-v1"
        productVersion = "1.0.0"
        versionAssembly = "CUETools.SigningProbe.exe"
        requiredFiles = @(
            [ordered]@{
                path = "CUETools.SigningProbe.exe"
                minimumBytes = 1
            })
    }
    [IO.File]::WriteAllText(
        $contractPath,
        (($contract | ConvertTo-Json -Depth 5) + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))

    $policy = [ordered]@{
        schemaVersion = 1
        policyId = "cuetools-windows-authenticode-v1"
        fileDigest = "SHA256"
        timestampDigest = "SHA256"
        timestampUrl = "http://timestamp.digicert.com"
        codeSigningEku = "1.3.6.1.5.5.7.3.3"
        productionRefPrefix = "refs/tags/"
        profiles = @(
            [ordered]@{
                id = "signing-test"
                artifactDirectory = "bin/Release/$testLeaf/artifact"
                contractPath = "bin/Release/$testLeaf/contract.json"
                minimumSelectedFiles = 1
                regeneratePluginManifest = $false
                includePathRegexes = @(
                    "^CUETools\.SigningProbe\.exe$")
            })
    }
    [IO.File]::WriteAllText(
        $policyPath,
        (($policy | ConvertTo-Json -Depth 6) + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))

    $passwordText = [Guid]::NewGuid().ToString("N")
    $password = ConvertTo-SecureString $passwordText -AsPlainText -Force
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=CUETools Signing Harness" `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter ([DateTime]::Now.AddDays(2))
    Export-PfxCertificate `
        -Cert $certificate `
        -FilePath $pfxPath `
        -Password $password | Out-Null

    # Exercise the production import/removal path rather than reusing the
    # certificate instance that created the fixture.
    Remove-Item -LiteralPath (
        "Cert:\CurrentUser\My\$($certificate.Thumbprint)") -Force

    $publicTrustRefused = $false
    try {
        & (Join-Path $PSScriptRoot "Invoke-ArtifactSigning.ps1") `
            -RepositoryRoot $repoRoot `
            -PolicyPath $policyPath `
            -OutputPath $outputPath `
            -CertificateBase64 ([Convert]::ToBase64String(
                [IO.File]::ReadAllBytes($pfxPath))) `
            -CertificatePassword $passwordText `
            -ExpectedSubjectPattern "^CN=CUETools Signing Harness$" `
            -RequireSigning
    }
    catch {
        if ($_.Exception.Message -notmatch
            "^SignTool verification failed for CUETools\.SigningProbe\.exe") {
            throw
        }
        $publicTrustRefused = $true
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $probePath
    if ($null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint -or
        $null -eq $signature.TimeStamperCertificate) {
        $actualSigner = if ($null -eq $signature.SignerCertificate) {
            "<none>"
        } else {
            $signature.SignerCertificate.Thumbprint
        }
        $actualTimestamp = if ($null -eq $signature.TimeStamperCertificate) {
            "<none>"
        } else {
            $signature.TimeStamperCertificate.Subject
        }
        throw (
            "The signing harness output is not signed and timestamped. " +
            "Status=$($signature.Status); signer=$actualSigner; " +
            "expected=$($certificate.Thumbprint); timestamp=$actualTimestamp")
    }
    if (-not $publicTrustRefused) {
        $status = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json
        if ([string]$status.mode -ne "authenticode-release" -or
            -not [bool]$status.productionRelease -or
            @($status.files).Count -ne 1) {
            throw "The signing harness did not produce one release signature record."
        }
    }
    if (Test-Path -LiteralPath (
            "Cert:\CurrentUser\My\$($certificate.Thumbprint)")) {
        throw "The signing harness left its imported private certificate behind."
    }
    Write-Host (
        "Artifact signing harness passed: signed, RFC 3161 timestamped, " +
        "and untrusted test publisher refused=$publicTrustRefused")
}
finally {
    $storePaths = New-Object "Collections.Generic.List[string]"
    if ($null -ne $certificate) {
        $storePaths.Add(
            "Cert:\CurrentUser\My\$($certificate.Thumbprint)")
    }
    foreach ($storePath in $storePaths) {
        if (Test-Path -LiteralPath $storePath) {
            Remove-Item -LiteralPath $storePath -Force
        }
    }
    Remove-SigningHarnessDirectory -Path $testRoot
}
