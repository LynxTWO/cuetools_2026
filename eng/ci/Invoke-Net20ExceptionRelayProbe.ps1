[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$NoRestore,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$projectPath = Join-Path $repoRoot "CUETools.Codecs\CUETools.Codecs.csproj"
$assemblyPath =
    Join-Path $repoRoot "bin\$Configuration\net20\CUETools.Codecs.dll"

if (-not $NoBuild) {
    $arguments = @(
        "build",
        $projectPath,
        "--configuration", $Configuration,
        "--framework", "net20",
        "--nologo"
    )
    if ($NoRestore) { $arguments += "--no-restore" }
    & dotnet @arguments
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
