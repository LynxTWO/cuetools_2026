[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$PolicyPath,
    [string]$OutputPath,
    [string]$CertificateBase64,
    [string]$CertificatePassword,
    [string]$ExpectedSubjectPattern,
    [string]$SignToolPath,
    [switch]$RequireSigning,
    [switch]$PlanOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

. (Join-Path $PSScriptRoot "ReleaseSafety.ps1")

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
}
else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}
if ([string]::IsNullOrWhiteSpace($PolicyPath)) {
    $PolicyPath = Join-Path $PSScriptRoot "signing-policy.json"
}
$PolicyPath = [IO.Path]::GetFullPath($PolicyPath)
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $RepositoryRoot (
        "bin\Release\evidence\signing\signing-status.json")
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)

function Resolve-SigningRepositoryPath {
    param([string]$RelativePath, [string]$Purpose)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath.Contains(":")) {
        throw "$Purpose is not a safe repository-relative path: $RelativePath"
    }
    $candidate = [IO.Path]::GetFullPath(
        (Join-Path $RepositoryRoot ($RelativePath.Replace("/", "\"))))
    if (-not (Test-SameOrDescendantPath `
            -CandidatePath $candidate `
            -RootPath $RepositoryRoot) -or
        $candidate -eq $RepositoryRoot) {
        throw "$Purpose escapes the repository: $RelativePath"
    }
    return $candidate
}

function Get-SigningProfileFiles {
    param([object]$Profile)

    $artifactRoot = Resolve-SigningRepositoryPath `
        -RelativePath ([string]$Profile.artifactDirectory) `
        -Purpose "Signing artifact directory"
    $contractPath = Resolve-SigningRepositoryPath `
        -RelativePath ([string]$Profile.contractPath) `
        -Purpose "Signing artifact contract"
    Assert-NoReparsePointInExistingPath `
        -Path $artifactRoot `
        -Purpose "Signing artifact directory"
    if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) {
        throw "Signing artifact does not exist: $artifactRoot"
    }
    if (-not (Test-Path -LiteralPath $contractPath -PathType Leaf)) {
        throw "Signing artifact contract does not exist: $contractPath"
    }

    $contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
    $seen = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    $selected = New-Object "Collections.Generic.List[object]"
    foreach ($required in @($contract.requiredFiles)) {
        $relativePath = ([string]$required.path).Replace("\", "/")
        $matches = $false
        foreach ($pattern in @($Profile.includePathRegexes)) {
            if ($relativePath -cmatch [string]$pattern) {
                $matches = $true
                break
            }
        }
        if (-not $matches) { continue }
        if (-not $seen.Add($relativePath)) {
            throw "Signing profile '$($Profile.id)' selects duplicate path $relativePath."
        }
        $sha256Property = $required.PSObject.Properties["sha256"]
        if ($null -ne $sha256Property -and
            -not [string]::IsNullOrWhiteSpace(
                [string]$sha256Property.Value)) {
            throw "Signing profile '$($Profile.id)' selects hash-pinned upstream file $relativePath."
        }
        $fullPath = [IO.Path]::GetFullPath(
            (Join-Path $artifactRoot ($relativePath.Replace("/", "\"))))
        if (-not (Test-SameOrDescendantPath `
                -CandidatePath $fullPath `
                -RootPath $artifactRoot) -or
            -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Signing target is absent or escapes its artifact: $relativePath"
        }
        Assert-NoReparsePointInExistingPath `
            -Path $fullPath `
            -Purpose "Signing target"
        $file = Get-Item -LiteralPath $fullPath -Force
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Signing target is a reparse point: $relativePath"
        }
        $selected.Add([pscustomobject]@{
            profile = [string]$Profile.id
            artifactRoot = $artifactRoot
            relativePath = $relativePath
            fullPath = $fullPath
        })
    }
    if ($selected.Count -lt [int]$Profile.minimumSelectedFiles) {
        throw (
            "Signing profile '{0}' selected {1} files; policy requires at least {2}." -f
            $Profile.id,
            $selected.Count,
            $Profile.minimumSelectedFiles)
    }
    return $selected.ToArray()
}

function Find-SignTool {
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
        $candidate = [IO.Path]::GetFullPath($SignToolPath)
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "SignTool does not exist: $candidate"
        }
        return $candidate
    }
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return [string]$command.Source
    }
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $candidate = @(
        Get-ChildItem -LiteralPath $kitsRoot -Filter signtool.exe -File -Recurse |
            Where-Object { $_.Directory.Name -eq "x64" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
    )
    if ($candidate.Count -ne 1) {
        throw "SignTool.exe was not found in PATH or the Windows SDK."
    }
    return $candidate[0].FullName
}

function Write-SigningStatus([object]$Status) {
    $outputRoot = [IO.Path]::GetDirectoryName($OutputPath)
    if (-not (Test-SameOrDescendantPath `
            -CandidatePath $outputRoot `
            -RootPath (Join-Path $RepositoryRoot "bin\Release")) -or
        $outputRoot -eq (Join-Path $RepositoryRoot "bin\Release")) {
        throw "Signing evidence output must be below bin\\Release: $OutputPath"
    }
    Assert-NoReparsePointInExistingPath `
        -Path $outputRoot `
        -Purpose "Signing evidence directory"
    New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
    Assert-NoReparsePointInExistingPath `
        -Path $OutputPath `
        -Purpose "Signing evidence output"
    [IO.File]::WriteAllText(
        $OutputPath,
        (($Status | ConvertTo-Json -Depth 8) + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
}

$policy = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json
if ([int]$policy.schemaVersion -ne 1 -or
    [string]$policy.fileDigest -cne "SHA256" -or
    [string]$policy.timestampDigest -cne "SHA256" -or
    [string]$policy.codeSigningEku -cne "1.3.6.1.5.5.7.3.3") {
    throw "Unsupported or weakened signing policy."
}
$targets = New-Object "Collections.Generic.List[object]"
foreach ($profile in @($policy.profiles)) {
    foreach ($target in @(Get-SigningProfileFiles -Profile $profile)) {
        $targets.Add($target)
    }
}
if ($PlanOnly) {
    $targets |
        Sort-Object profile, relativePath |
        ForEach-Object { Write-Host "$($_.profile): $($_.relativePath)" }
    Write-Host "Signing plan passed: $($targets.Count) files"
    return
}

if ([string]::IsNullOrWhiteSpace($CertificateBase64)) {
    if ($RequireSigning) {
        throw "Production signing is required, but CUETOOLS_SIGNING_PFX_BASE64 is unavailable."
    }
    Write-SigningStatus ([ordered]@{
        schema = "cuetools.signing-status.v1"
        policyId = [string]$policy.policyId
        mode = "unsigned-evaluation"
        productionRelease = $false
        reason = "A manually dispatched evidence build did not receive release-signing credentials."
        selectedFileCount = $targets.Count
    })
    Write-Host "Unsigned evaluation recorded; this artifact is not eligible for release."
    return
}
if ([string]::IsNullOrWhiteSpace($CertificatePassword) -or
    [string]::IsNullOrWhiteSpace($ExpectedSubjectPattern)) {
    throw "The signing certificate password and expected subject pattern are required."
}

$signTool = Find-SignTool
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "cuetools-signing-" + [Guid]::NewGuid().ToString("N"))
$pfxPath = Join-Path $temporaryRoot "certificate.pfx"
$importedCertificate = $null
$importedCertificates = @()
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $pfxBytes = [Convert]::FromBase64String($CertificateBase64)
    [IO.File]::WriteAllBytes($pfxPath, $pfxBytes)
    $password = ConvertTo-SecureString $CertificatePassword -AsPlainText -Force
    $importedCertificates = @(
        Import-PfxCertificate `
            -FilePath $pfxPath `
            -CertStoreLocation Cert:\CurrentUser\My `
            -Password $password `
            -Exportable:$false)
    [IO.File]::Delete($pfxPath)

    $signingCertificates = @(
        $importedCertificates |
            Where-Object {
                if (-not $_.HasPrivateKey) { return $false }
                $eku = @(
                    $_.Extensions |
                        Where-Object { $_.Oid.Value -eq "2.5.29.37" } |
                        ForEach-Object {
                            $_.EnhancedKeyUsages |
                                ForEach-Object { $_.Value }
                        })
                return @(
                    $eku |
                        Where-Object {
                            $_ -eq [string]$policy.codeSigningEku
                        }).Count -eq 1
            })
    if ($signingCertificates.Count -ne 1) {
        throw "The PFX must contain exactly one private-key code-signing certificate."
    }
    $importedCertificate = $signingCertificates[0]
    if ($importedCertificate.Subject -notmatch $ExpectedSubjectPattern) {
        throw "The signing certificate subject does not match the protected policy pattern."
    }
    if ($importedCertificate.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow -or
        $importedCertificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) {
        throw "The signing certificate is not currently valid."
    }

    $records = New-Object "Collections.Generic.List[object]"
    foreach ($target in @($targets | Sort-Object profile, relativePath)) {
        $before = (Get-FileHash -LiteralPath $target.fullPath -Algorithm SHA256).Hash
        & $signTool sign `
            /sha1 $importedCertificate.Thumbprint `
            /s My `
            /fd ([string]$policy.fileDigest) `
            /tr ([string]$policy.timestampUrl) `
            /td ([string]$policy.timestampDigest) `
            /d "CUETools" `
            $target.fullPath
        if ($LASTEXITCODE -ne 0) {
            throw "SignTool failed for $($target.relativePath) with exit code $LASTEXITCODE."
        }
        & $signTool verify /pa /all /tw $target.fullPath
        if ($LASTEXITCODE -ne 0) {
            throw "SignTool verification failed for $($target.relativePath)."
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $target.fullPath
        if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid -or
            $null -eq $signature.SignerCertificate -or
            $signature.SignerCertificate.Thumbprint -ne
                $importedCertificate.Thumbprint -or
            $null -eq $signature.TimeStamperCertificate) {
            throw "Authenticode or RFC 3161 timestamp verification failed for $($target.relativePath)."
        }
        $records.Add([ordered]@{
            profile = [string]$target.profile
            path = [string]$target.relativePath
            beforeSha256 = $before
            signedSha256 = (Get-FileHash `
                -LiteralPath $target.fullPath `
                -Algorithm SHA256).Hash
        })
    }

    foreach ($profile in @($policy.profiles)) {
        if (-not [bool]$profile.regeneratePluginManifest) { continue }
        $artifactRoot = Resolve-SigningRepositoryPath `
            -RelativePath ([string]$profile.artifactDirectory) `
            -Purpose "Signing artifact directory"
        & (Join-Path $RepositoryRoot "tools\Write-PluginManifest.ps1") `
            -PluginDirectory (Join-Path $artifactRoot "plugins")
    }

    Write-SigningStatus ([ordered]@{
        schema = "cuetools.signing-status.v1"
        policyId = [string]$policy.policyId
        mode = "authenticode-release"
        productionRelease = $true
        fileDigest = [string]$policy.fileDigest
        timestampDigest = [string]$policy.timestampDigest
        timestampUrl = [string]$policy.timestampUrl
        certificate = [ordered]@{
            subject = $importedCertificate.Subject
            thumbprint = $importedCertificate.Thumbprint
            notAfterUtc = $importedCertificate.NotAfter.ToUniversalTime().ToString("o")
        }
        files = $records.ToArray()
    })
    Write-Host "Authenticode signing passed: $($records.Count) files"
}
finally {
    foreach ($certificate in @($importedCertificates)) {
        $certificatePath = "Cert:\CurrentUser\My\$($certificate.Thumbprint)"
        if (Test-Path -LiteralPath $certificatePath) {
            Remove-Item -LiteralPath $certificatePath -Force
        }
    }
    if (Test-Path -LiteralPath $temporaryRoot) {
        $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $tempPrefix = $tempBase.TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar
        if (-not $temporaryRoot.StartsWith(
                $tempPrefix,
                [StringComparison]::OrdinalIgnoreCase) -or
            -not [IO.Path]::GetFileName($temporaryRoot).StartsWith(
                "cuetools-signing-",
                [StringComparison]::Ordinal)) {
            throw "Refusing to clean unexpected signing path: $temporaryRoot"
        }
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
