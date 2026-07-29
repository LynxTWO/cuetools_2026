[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$script:checkCount = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checkCount++
}

function Invoke-Validator(
    [string]$ValidatorAssembly,
    [string]$ArtifactDirectory,
    [string]$ManifestPath
) {
    $oldErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = @(
            & dotnet $ValidatorAssembly $ArtifactDirectory $ManifestPath 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldErrorActionPreference
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output -join [Environment]::NewLine
    }
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase (
    "cuetools-artifact-exact-" + [Guid]::NewGuid().ToString("N"))
$artifactDirectory = Join-Path $tempRoot "artifact"
$validatorOutput = Join-Path $tempRoot "validator"
$manifestPath = Join-Path $tempRoot "contract.json"
$validatorProject = Join-Path $PSScriptRoot (
    "ArtifactValidator\ArtifactValidator.csproj")
New-Item -ItemType Directory -Path $artifactDirectory | Out-Null
New-Item -ItemType Directory -Path $validatorOutput | Out-Null

try {
    & dotnet build $validatorProject `
        --configuration Release `
        --output $validatorOutput `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "ArtifactValidator build failed with exit code $LASTEXITCODE."
    }

    $validatorAssembly = Join-Path $validatorOutput "ArtifactValidator.dll"
    $versionAssembly = Join-Path $artifactDirectory "managed-root-copy.dll"
    Copy-Item -LiteralPath $validatorAssembly -Destination $versionAssembly
    $productVersion = [Reflection.AssemblyName]::GetAssemblyName(
        $versionAssembly).Version.ToString(3)
    $utf8 = New-Object Text.UTF8Encoding($false)

    function Write-TestManifest(
        [bool]$RequireExactFiles,
        [string]$PeMachine = "",
        [string]$Sha256 = "") {
        $required = [ordered]@{
            path = "managed-root-copy.dll"
            minimumBytes = 1
        }
        if (-not [string]::IsNullOrWhiteSpace($PeMachine)) {
            $required.peMachine = $PeMachine
        }
        if (-not [string]::IsNullOrWhiteSpace($Sha256)) {
            $required.sha256 = $Sha256
        }
        $manifest = [ordered]@{
            schemaVersion = 1
            manifestId = "exact-files-focused-test"
            productVersion = $productVersion
            versionAssembly = "managed-root-copy.dll"
            requireExactFiles = $RequireExactFiles
            requiredFiles = @($required)
            forbiddenFiles = @()
        }
        [IO.File]::WriteAllText(
            $manifestPath,
            (($manifest | ConvertTo-Json -Depth 5) + "`n"),
            $utf8)
    }

    Write-TestManifest $true
    $clean = Invoke-Validator `
        -ValidatorAssembly $validatorAssembly `
        -ArtifactDirectory $artifactDirectory `
        -ManifestPath $manifestPath
    Assert-True `
        ($clean.ExitCode -eq 0 -and $clean.Output -match "exactFiles=1") `
        "An exact clean artifact was rejected: $($clean.Output)"

    $unexpectedPath = Join-Path $artifactDirectory "stale-from-prior-build.dll"
    [IO.File]::WriteAllText($unexpectedPath, "stale")
    $unexpected = Invoke-Validator `
        -ValidatorAssembly $validatorAssembly `
        -ArtifactDirectory $artifactDirectory `
        -ManifestPath $manifestPath
    Assert-True `
        ($unexpected.ExitCode -eq 1 -and
            $unexpected.Output -match
                "unexpected=\[stale-from-prior-build\.dll\]") `
        "Exact validation accepted a stale unexpected file: $($unexpected.Output)"

    Write-TestManifest $false
    $nonExact = Invoke-Validator `
        -ValidatorAssembly $validatorAssembly `
        -ArtifactDirectory $artifactDirectory `
        -ManifestPath $manifestPath
    Assert-True `
        ($nonExact.ExitCode -eq 0 -and $nonExact.Output -match "exactFiles=0") `
        "A manifest that did not request exactness rejected an extra file: $($nonExact.Output)"

    Write-TestManifest $false "x64"
    $wrongMachine = Invoke-Validator `
        -ValidatorAssembly $validatorAssembly `
        -ArtifactDirectory $artifactDirectory `
        -ManifestPath $manifestPath
    Assert-True `
        ($wrongMachine.ExitCode -eq 1 -and
            $wrongMachine.Output -match
                "PE machine is x86; contract requires x64") `
        "Artifact validation accepted the wrong PE machine: $($wrongMachine.Output)"

    Write-TestManifest $false "x86"
    $rightMachine = Invoke-Validator `
        -ValidatorAssembly $validatorAssembly `
        -ArtifactDirectory $artifactDirectory `
        -ManifestPath $manifestPath
    Assert-True `
        ($rightMachine.ExitCode -eq 0) `
        "Artifact validation rejected the contracted PE machine: $($rightMachine.Output)"

    $exactHash = (
        Get-FileHash -LiteralPath $versionAssembly -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    Write-TestManifest $false "" $exactHash
    $rightHash = Invoke-Validator `
        -ValidatorAssembly $validatorAssembly `
        -ArtifactDirectory $artifactDirectory `
        -ManifestPath $manifestPath
    Assert-True `
        ($rightHash.ExitCode -eq 0) `
        "Artifact validation rejected the contracted SHA-256: $($rightHash.Output)"

    Write-TestManifest $false "" ("0" * 64)
    $wrongHash = Invoke-Validator `
        -ValidatorAssembly $validatorAssembly `
        -ArtifactDirectory $artifactDirectory `
        -ManifestPath $manifestPath
    Assert-True `
        ($wrongHash.ExitCode -eq 1 -and
            $wrongHash.Output -match "SHA-256 is .* not 000000") `
        "Artifact validation accepted the wrong SHA-256: $($wrongHash.Output)"

    Write-Host "ArtifactValidator exact-file checks passed: $script:checkCount"
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
            "cuetools-artifact-exact-",
            [StringComparison]::Ordinal)) {
        throw "Refusing to clean unexpected validator-test path: $tempRoot"
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
