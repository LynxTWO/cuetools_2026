[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

. (Join-Path $PSScriptRoot "NativeWarningBaseline.ps1")

$checkCount = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checkCount++
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase (
    "cuetools-native-warning-" + [Guid]::NewGuid().ToString("N"))
$baselinePath = Join-Path $tempRoot "native-warning-baseline.json"
$logPath = Join-Path $tempRoot "native-build.log"
$expected =
    "ThirdParty/flac/src/libFLAC/fixed.c|C4244|fixture conversion warning"

function Assert-NovelWarningRejected(
    [string]$Line,
    [string]$Label) {
    [IO.File]::WriteAllText(
        $logPath,
        $Line + "`r`n",
        [Text.Encoding]::Unicode)
    $rejected = $false
    try {
        [void](Get-NativeWarningBaselineResult `
            -RepositoryRoot $tempRoot `
            -WarningBaselinePath $baselinePath `
            -LogPaths @($logPath) `
            -CoverageIds @("Release|x64"))
    }
    catch {
        $rejected = $_.Exception.Message -match "new fingerprint"
    }
    Assert-True $rejected "$Label warning format bypassed the native baseline."
}

try {
    [void][IO.Directory]::CreateDirectory($tempRoot)
    [IO.File]::WriteAllText(
        $baselinePath,
        ([ordered]@{
            schemaVersion = 1
            lane = "fixture"
            builds = @("fixture")
            fingerprints = @($expected)
            limitations = @()
        } | ConvertTo-Json -Depth 6),
        (New-Object Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText(
        $logPath,
        "4>$tempRoot\ThirdParty\flac\src\libFLAC\fixed.c(7,2): " +
            "warning C4244: fixture conversion warning`r`n" +
            "8>$tempRoot\Managed.cs(3,1): warning CS0219: managed warning`r`n",
        [Text.Encoding]::Unicode)

    $result = Get-NativeWarningBaselineResult `
        -RepositoryRoot $tempRoot `
        -WarningBaselinePath $baselinePath `
        -LogPaths @($logPath) `
        -CoverageIds @("Release|x64")
    Assert-True `
        ((@($result.fingerprints) -join "`n") -ceq $expected) `
        "The native warning parser did not produce the canonical fingerprint."
    Assert-True `
        ($result.emittedWarningLines -eq 1) `
        "The native warning parser did not exclude the managed warning."
    Assert-True `
        (@($result.logs).Count -eq 1 -and
            [string]$result.logs[0].sha256 -match "^[0-9A-F]{64}$") `
        "The native warning gate did not bind its exact input log."

    [IO.File]::AppendAllText(
        $logPath,
        "4>$tempRoot\ThirdParty\WavPack\pack.c(9): " +
            "warning C4999: novel native warning`r`n",
        [Text.Encoding]::Unicode)
    $novelRejected = $false
    try {
        [void](Get-NativeWarningBaselineResult `
            -RepositoryRoot $tempRoot `
            -WarningBaselinePath $baselinePath `
            -LogPaths @($logPath) `
            -CoverageIds @("Release|x64"))
    }
    catch {
        $novelRejected =
            $_.Exception.Message -match "new fingerprint" -and
            $_.Exception.Message -match "ThirdParty/WavPack/pack.c"
    }
    Assert-True `
        $novelRejected `
        "A novel native warning was not rejected."

    Assert-NovelWarningRejected `
        "..\..\ThirdParty\flac\relative.c(4): warning C4999: relative path" `
        "Relative-path"
    $forwardRoot = $tempRoot.Replace("\", "/")
    Assert-NovelWarningRejected `
        "$forwardRoot/ThirdParty/flac/forward.c(4): warning C4999: forward slash" `
        "Forward-slash"
    Assert-NovelWarningRejected `
        "2026-07-26T23:00:00Z 1>$tempRoot\ThirdParty\flac\timed.c(4): warning C4999: timed" `
        "Timestamp-prefixed"
    Assert-NovelWarningRejected `
        "cl : warning D9025: overriding option" `
        "Native-tool"

    $emptyRejected = $false
    try {
        [void](Get-NativeWarningBaselineResult `
            -RepositoryRoot $tempRoot `
            -WarningBaselinePath $baselinePath `
            -CoverageIds @("Release|x64"))
    }
    catch {
        $emptyRejected =
            $_.Exception.Message -match "requires actual log text"
    }
    Assert-True $emptyRejected "Empty warning coverage was silently accepted."

    $duplicateRejected = $false
    try {
        [void](Get-NativeWarningBaselineResult `
            -RepositoryRoot $tempRoot `
            -WarningBaselinePath $baselinePath `
            -LogPaths @($logPath, $logPath) `
            -CoverageIds @("Release|x64", "Release|Win32"))
    }
    catch {
        $duplicateRejected =
            $_.Exception.Message -match "duplicate log path"
    }
    Assert-True $duplicateRejected "Duplicate warning logs were silently accepted."

    $encodingPath = Join-Path $tempRoot "encoding.log"
    [IO.File]::WriteAllBytes(
        $encodingPath,
        [Text.Encoding]::Unicode.GetBytes("bomless utf16"))
    $bomlessRejected = $false
    try {
        [void](Get-NativeWarningFileDocument `
            -Path $encodingPath `
            -Purpose "Test BOM-less UTF-16")
    }
    catch {
        $bomlessRejected = $_.Exception.Message -match "BOM-less UTF-16"
    }
    Assert-True $bomlessRejected "BOM-less UTF-16 was treated as warning-free UTF-8."

    [IO.File]::WriteAllText(
        $encodingPath,
        "utf32",
        [Text.Encoding]::UTF32)
    $utf32Rejected = $false
    try {
        [void](Get-NativeWarningFileDocument `
            -Path $encodingPath `
            -Purpose "Test UTF-32")
    }
    catch {
        $utf32Rejected = $_.Exception.Message -match "UTF-32"
    }
    Assert-True $utf32Rejected "UTF-32 warning logs were silently misdecoded."

    [IO.File]::WriteAllBytes(
        $encodingPath,
        [byte[]]@(0xC3, 0x28))
    $invalidUtf8Rejected = $false
    try {
        [void](Get-NativeWarningFileDocument `
            -Path $encodingPath `
            -Purpose "Test invalid UTF-8")
    }
    catch {
        $invalidUtf8Rejected = $true
    }
    Assert-True $invalidUtf8Rejected "Invalid UTF-8 warning logs were accepted."

    Write-Host "Native warning baseline checks passed: $checkCount"
}
finally {
    $tempPrefix = $tempBase.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $leaf = [IO.Path]::GetFileName($tempRoot)
    if (-not $tempRoot.StartsWith(
            $tempPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not $leaf.StartsWith(
            "cuetools-native-warning-",
            [StringComparison]::Ordinal)) {
        throw "Refusing to clean an unexpected native warning test path: $tempRoot"
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
