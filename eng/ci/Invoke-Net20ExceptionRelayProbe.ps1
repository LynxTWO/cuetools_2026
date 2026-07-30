[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$projectPath = Join-Path $repoRoot "CUETools.Codecs\CUETools.Codecs.csproj"
$assemblyPath =
    Join-Path $repoRoot "bin\$Configuration\net20\CUETools.Codecs.dll"
$projectExtensionsPath = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot "CUETools.Codecs\obj\net20-probe\$Configuration"))
$projectExtensionsProperty =
    "-p:MSBuildProjectExtensionsPath=" +
    $projectExtensionsPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar

if (-not $NoBuild) {
    # The classic solution is restored by full MSBuild, where the net20
    # reference package is deliberately restore-only. This compatibility probe
    # is a Core-MSBuild lane and therefore owns a separate locked assets graph
    # in which the package supplies the missing hosted reference assemblies.
    $restoreArguments = @(
        "restore",
        $projectPath,
        "--locked-mode",
        "--force-evaluate",
        "--nologo",
        $projectExtensionsProperty
    )
    & dotnet @restoreArguments
    if ($LASTEXITCODE -ne 0) {
        throw "The net20 codec restore failed before the compatibility probe."
    }

    $buildArguments = @(
        "build",
        $projectPath,
        "--configuration", $Configuration,
        "--framework", "net20",
        "--no-restore",
        "--nologo",
        $projectExtensionsProperty
    )
    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "The net20 codec build failed before the compatibility probe."
    }
}

if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "The net20 compatibility assembly is missing: $assemblyPath"
}

$frameworkRoot = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319"
$compilerPath = Join-Path $frameworkRoot "csc.exe"
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw "The .NET Framework C# compiler is missing: $compilerPath"
}

$systemTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$probeDirectory = [IO.Path]::GetFullPath(
    (Join-Path $systemTempRoot (
        "cuetools-net20-relay-" + [Guid]::NewGuid().ToString("N"))))
$tempPrefix =
    $systemTempRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar
if (-not $probeDirectory.StartsWith(
    $tempPrefix,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "The compatibility probe directory escaped the system temp directory."
}

New-Item -ItemType Directory -Path $probeDirectory | Out-Null
try {
    $probeSource = Join-Path $PSScriptRoot "Net20ExceptionRelayProbe.cs"
    $probeExecutable = Join-Path $probeDirectory "Net20ExceptionRelayProbe.exe"
    & $compilerPath /nologo /target:exe "/out:$probeExecutable" $probeSource
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath $probeExecutable -PathType Leaf)) {
        throw "The net20 compatibility probe did not compile."
    }

    & $probeExecutable $assemblyPath
    if ($LASTEXITCODE -ne 0) {
        throw "The net20 exception relay compatibility probe failed."
    }
}
finally {
    if (Test-Path -LiteralPath $probeDirectory) {
        Remove-Item -LiteralPath $probeDirectory -Recurse -Force
    }
}
