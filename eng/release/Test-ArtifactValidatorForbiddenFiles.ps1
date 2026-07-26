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
$tempRoot = [IO.Path]::Combine(
    $tempBase,
    "cuetools-artifact-validator-" + [Guid]::NewGuid().ToString("N"))
$artifactDirectory = Join-Path $tempRoot "artifact"
$validatorOutput = Join-Path $tempRoot "validator"
$manifestPath = Join-Path $tempRoot "contract.json"
$forbiddenPath = Join-Path $artifactDirectory "libmp3lame.dll"
$validatorProject = Join-Path $PSScriptRoot "ArtifactValidator\ArtifactValidator.csproj"
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

    function Write-TestManifest([string[]]$ForbiddenFiles) {
        $manifest = [ordered]@{
            schemaVersion = 1
            manifestId = "forbidden-files-focused-test"
            productVersion = $productVersion
            versionAssembly = "managed-root-copy.dll"
            forbiddenFiles = $ForbiddenFiles
            requiredFiles = @(
                [ordered]@{
                    path = "managed-root-copy.dll"
                    minimumBytes = 1
                }
            )
        }
        [IO.File]::WriteAllText(
            $manifestPath,
            (($manifest | ConvertTo-Json -Depth 5) + "`n"),
            $utf8)
    }

    Write-TestManifest @("libmp3lame.dll")
    $cleanResult = Invoke-Validator `
        -ValidatorAssembly $validatorAssembly `
        -ArtifactDirectory $artifactDirectory `
        -ManifestPath $manifestPath
    Assert-True `
        ($cleanResult.ExitCode -eq 0) `
        "A clean artifact was rejected: $($cleanResult.Output)"
    Assert-True `
        ($cleanResult.Output -match "forbiddenFiles=1") `
        "The clean validation result did not report its forbidden-file contract."

    [IO.File]::WriteAllText($forbiddenPath, "native-root-copy")
    $fileResult = Invoke-Validator `
        -ValidatorAssembly $validatorAssembly `
        -ArtifactDirectory $artifactDirectory `
        -ManifestPath $manifestPath
    Assert-True `
        ($fileResult.ExitCode -eq 1 -and
            $fileResult.Output -match "Forbidden artifact path exists: libmp3lame\.dll") `
        "A forbidden root native file was not rejected: $($fileResult.Output)"
    Remove-Item -LiteralPath $forbiddenPath -Force

    New-Item -ItemType Directory -Path $forbiddenPath | Out-Null
    $directoryResult = Invoke-Validator `
        -ValidatorAssembly $validatorAssembly `
        -ArtifactDirectory $artifactDirectory `
        -ManifestPath $manifestPath
    Assert-True `
        ($directoryResult.ExitCode -eq 1 -and
            $directoryResult.Output -match "Forbidden artifact path exists: libmp3lame\.dll") `
        "A forbidden root directory was not rejected: $($directoryResult.Output)"
    Remove-Item -LiteralPath $forbiddenPath -Force

    Write-TestManifest @("../outside.dll")
    $escapeResult = Invoke-Validator `
        -ValidatorAssembly $validatorAssembly `
        -ArtifactDirectory $artifactDirectory `
        -ManifestPath $manifestPath
    Assert-True `
        ($escapeResult.ExitCode -eq 1 -and
            $escapeResult.Output -match "Artifact path escapes the root") `
        "An escaping forbidden path was not rejected: $($escapeResult.Output)"

    $wpfManifest = Get-Content `
        -LiteralPath (Join-Path $PSScriptRoot "wpf-win-x64.manifest.json") `
        -Raw |
        ConvertFrom-Json
    Assert-True `
        (@($wpfManifest.forbiddenFiles) -contains "libmp3lame.dll") `
        "The WPF contract does not forbid the root native libmp3lame.dll copy."
    Assert-True `
        (@($wpfManifest.requiredFiles.path) -contains "CUETools.Codecs.libmp3lame.dll") `
        "The WPF contract no longer requires the managed root libmp3lame assembly."
    Assert-True `
        (@($wpfManifest.requiredFiles.path) -contains "plugins/x64/libmp3lame.dll") `
        "The WPF contract no longer requires the architecture-scoped native libmp3lame DLL."

    Write-Host "ArtifactValidator forbidden-file checks passed: $script:checkCount"
}
finally {
    $tempPrefix = $tempBase.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $tempLeaf = [IO.Path]::GetFileName($tempRoot)
    if (-not $tempRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not $tempLeaf.StartsWith(
            "cuetools-artifact-validator-",
            [StringComparison]::Ordinal)) {
        throw "Refusing to clean an unexpected test path: $tempRoot"
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
