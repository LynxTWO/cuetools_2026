[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$workflowPath = Join-Path $repoRoot ".github\workflows\build_ffmpeg_dlls.yml"
$projectPath = Join-Path $repoRoot "CUETools.Codecs.ffmpeg\CUETools.Codecs.ffmpeg.csproj"
$lockPath = Join-Path $repoRoot "CUETools.Codecs.ffmpeg\packages.lock.json"
$inventoryPath = Join-Path $repoRoot "eng\release\native-dependencies.json"
$workerPath = Join-Path $repoRoot "eng\ci\FFmpegCodecWorker\Program.cs"

$workflow = Get-Content -LiteralPath $workflowPath -Raw
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json
$worker = Get-Content -LiteralPath $workerPath -Raw

$checks = 0
function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
    $script:checks++
}

$expectedFfmpeg = "8.1.2"
$expectedAutoGen = "8.1.0"
$expectedVcpkg = "9e593bb18ea69cc5095e012465dcd675a822ed0d"

Require `
    ($workflow -match "(?m)^\s+ffmpegVer:\s+'$([regex]::Escape($expectedFfmpeg))'\s*$") `
    "The FFmpeg workflow version is not pinned to $expectedFfmpeg."
Require `
    (([regex]::Matches(
        $workflow,
        "vcpkgCommitId:\s+'$expectedVcpkg'")).Count -eq 2) `
    "Both FFmpeg architectures must use the reviewed vcpkg commit."
Require `
    ($workflow.Contains("FFmpegCodecWorker.csproj") -and
        $workflow.Contains("CUETools.FFmpegCodecWorker.exe")) `
    "The FFmpeg workflow does not build and run the managed/native probe."
Require `
    (([regex]::Matches(
        $workflow,
        "FFmpegCodecWorker\.csproj[^\r\n]*-p:PlatformTarget=\$\{\{ matrix\.configuration \}\}")).Count -eq 2 -and
        $workflow.Contains("probe restore failed") -and
        $workflow.Contains("probe build failed")) `
    "The FFmpeg probe does not bind restore/build architecture or fail closed."
Require `
    ($workflow.Contains("FFmpeg.txt") -and
        $workflow.Contains("FFmpeg-vcpkg.json")) `
    "The FFmpeg workflow does not retain its license and port evidence."
Require `
    ($workflow.Contains("cuetools.ffmpeg-build.v1") -and
        $workflow.Contains("Get-FileHash") -and
        $workflow.Contains("FileVersionInfo")) `
    "The FFmpeg workflow does not emit hash- and version-bound evidence."
Require `
    ($workflow.Contains('ffmpeg_${{ env.ffmpegVer }}_dlls_${{ matrix.pluginDir }}')) `
    "The FFmpeg workflow artifact name drifted."
Require `
    (-not ($workflow -match "(?m)^\s{10}#\s+ffmpeg dlls:")) `
    "A PowerShell-style comment remains executable in a cmd workflow block."

$packageReference = @(
    $project.SelectNodes("//*[local-name()='PackageReference']") |
        Where-Object { $_.Include -eq "FFmpeg.AutoGen" })
Require `
    ($packageReference.Count -eq 1 -and
        [string]$packageReference[0].Version -eq $expectedAutoGen) `
    "The managed FFmpeg binding is not pinned to $expectedAutoGen."

foreach ($framework in @(
    ".NETFramework,Version=v4.7",
    ".NETStandard,Version=v2.0")) {
    $dependency = $lock.dependencies.$framework."FFmpeg.AutoGen"
    Require `
        ($null -ne $dependency -and
            [string]$dependency.requested -eq "[$expectedAutoGen, )" -and
            [string]$dependency.resolved -eq $expectedAutoGen -and
            -not [string]::IsNullOrWhiteSpace([string]$dependency.contentHash)) `
        "The FFmpeg.AutoGen lock entry is invalid for $framework."
}

$entry = @(
    $inventory.artifacts |
        Where-Object { $_.id -eq "ffmpeg-on-demand" })
Require ($entry.Count -eq 1) "The FFmpeg native dependency inventory entry is missing."
Require `
    ([string]$entry[0].version -eq $expectedFfmpeg) `
    "The FFmpeg native dependency inventory version drifted."
Require `
    (@($entry[0].sourceInputs) -contains "vcpkg commit $expectedVcpkg") `
    "The FFmpeg native dependency inventory does not pin the vcpkg commit."
Require `
    ([string]$entry[0].verification -match "FFmpeg\.AutoGen 8\.1\.0" -and
        [string]$entry[0].verification -match "x86" -and
        [string]$entry[0].verification -match "x64") `
    "The FFmpeg native dependency inventory omits the binding or architecture evidence."

foreach ($probeContract in @(
    "path+stream decode",
    "seek replay",
    "callback containment",
    "Disposed")) {
    Require `
        ($worker.Contains($probeContract)) `
        "The FFmpeg worker is missing its '$probeContract' contract."
}

Write-Host "FFmpeg workflow checks passed: $checks"
