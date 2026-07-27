[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$DotNetPath,
    [string]$VSTestPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw "Monkey's Audio architecture verification requires Windows."
}

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
}
else {
    [IO.Path]::GetFullPath($RepositoryRoot)
}
if (-not (Test-Path -LiteralPath $repoRoot -PathType Container)) {
    throw "Repository root does not exist: $repoRoot"
}

$testProject = Join-Path $repoRoot (
    "CUETools.Wpf.Tests\CUETools.Wpf.Tests.csproj")
$x64Native = Join-Path $repoRoot "ThirdParty\x64\MACLibDll.dll"
$x86Native = Join-Path $repoRoot "ThirdParty\Win32\MACLibDll.dll"
$testFilter =
    "FullyQualifiedName~CodecImportIntegrationTests." +
    "MonkeyAudio_RoundTripsAndVerifiesRealPcm_OnNet8"

function Assert-RegularFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "$Purpose does not exist: $fullPath"
    }
    $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or
        -not ($item -is [IO.FileInfo]) -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Purpose must be a regular non-reparse file: $fullPath"
    }
    return $fullPath
}

function Get-PeMachine {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = Assert-RegularFile -Path $Path -Purpose $Purpose
    $stream = $null
    $reader = $null
    try {
        $stream = New-Object IO.FileStream(
            $fullPath,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        if ($stream.Length -lt 64) {
            throw "$Purpose is too small to be a PE file: $fullPath"
        }
        $reader = New-Object IO.BinaryReader($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) {
            throw "$Purpose has no DOS PE signature: $fullPath"
        }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or
            ([long]$peOffset + 6) -gt $stream.Length) {
            throw "$Purpose has an invalid PE header offset: $fullPath"
        }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw "$Purpose has no PE signature: $fullPath"
        }
        return $reader.ReadUInt16()
    }
    finally {
        if ($reader -ne $null) {
            $reader.Dispose()
            $reader = $null
            $stream = $null
        }
        elseif ($stream -ne $null) {
            $stream.Dispose()
        }
    }
}

function Assert-PeMachine {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [UInt16]$ExpectedMachine,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $machine = Get-PeMachine -Path $Path -Purpose $Purpose
    if ($machine -ne $ExpectedMachine) {
        throw (
            "$Purpose has PE machine 0x{0:X4}, expected 0x{1:X4}: {2}" -f
            $machine,
            $ExpectedMachine,
            ([IO.Path]::GetFullPath($Path)))
    }
}

function Resolve-X64DotNet {
    [CmdletBinding()]
    param([string]$RequestedPath)

    $candidates = New-Object "Collections.Generic.List[string]"
    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $candidates.Add([IO.Path]::GetFullPath($RequestedPath))
    }
    else {
        $programFiles64 = if (-not [string]::IsNullOrWhiteSpace(
                [string]${env:ProgramW6432})) {
            [string]${env:ProgramW6432}
        }
        else {
            [string]${env:ProgramFiles}
        }
        if (-not [string]::IsNullOrWhiteSpace($programFiles64)) {
            $candidates.Add((Join-Path $programFiles64 "dotnet\dotnet.exe"))
        }
        $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($command -ne $null) {
            $candidates.Add([string]$command.Source)
        }
    }

    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }
        try {
            Assert-PeMachine `
                -Path $candidate `
                -ExpectedMachine 0x8664 `
                -Purpose "x64 dotnet host"
            return (Assert-RegularFile `
                -Path $candidate `
                -Purpose "x64 dotnet host")
        }
        catch {
            if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
                throw
            }
        }
    }
    throw (
        "An x64 dotnet host was not found. Install the x64 .NET 8 SDK or pass " +
        "-DotNetPath with its exact dotnet.exe path.")
}

function Resolve-X86VSTest {
    [CmdletBinding()]
    param([string]$RequestedPath)

    $candidateSet = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    $candidates = New-Object "Collections.Generic.List[string]"
    function Add-Candidate([string]$Candidate) {
        if ([string]::IsNullOrWhiteSpace($Candidate)) { return }
        $fullPath = [IO.Path]::GetFullPath($Candidate)
        if ($candidateSet.Add($fullPath)) {
            $candidates.Add($fullPath)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        Add-Candidate $RequestedPath
    }
    else {
        $programFilesX86 = [string]${env:ProgramFiles(x86)}
        if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
            Add-Candidate (Join-Path $programFilesX86 (
                "Microsoft Visual Studio\2022\BuildTools\Common7\IDE\" +
                "CommonExtensions\Microsoft\TestWindow\vstest.console.exe"))
            $vswhere = Join-Path $programFilesX86 (
                "Microsoft Visual Studio\Installer\vswhere.exe")
            if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
                foreach ($pattern in @(
                    "Common7\IDE\CommonExtensions\Microsoft\TestWindow\" +
                        "vstest.console.exe",
                    "Common7\IDE\Extensions\TestPlatform\vstest.console.exe")) {
                    $found = @(
                        & $vswhere -all -products * -find $pattern 2>$null)
                    $exitCode = $LASTEXITCODE
                    if ($exitCode -eq 0) {
                        foreach ($path in $found) {
                            Add-Candidate ([string]$path)
                        }
                    }
                }
            }
        }
    }

    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }
        try {
            Assert-PeMachine `
                -Path $candidate `
                -ExpectedMachine 0x014C `
                -Purpose "x86 VSTest console"
            return (Assert-RegularFile `
                -Path $candidate `
                -Purpose "x86 VSTest console")
        }
        catch {
            if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
                throw
            }
        }
    }
    throw (
        "An x86 vstest.console.exe was not found. Install Visual Studio Build Tools " +
        "with the test platform or pass -VSTestPath with its exact x86 path.")
}

function Assert-NoReparseTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $pending = New-Object "Collections.Generic.Stack[string]"
    $pending.Push($fullPath)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Purpose contains a reparse point: $current"
        }
        if (-not $item.PSIsContainer) { continue }
        foreach ($child in (Get-ChildItem -LiteralPath $current -Force)) {
            $pending.Push($child.FullName)
        }
    }
}

function Copy-DirectoryContents {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    Assert-NoReparseTree `
        -Path $Source `
        -Purpose "Managed test output"
    [void][IO.Directory]::CreateDirectory($Destination)
    foreach ($item in (Get-ChildItem -LiteralPath $Source -Force)) {
        Copy-Item `
            -LiteralPath $item.FullName `
            -Destination $Destination `
            -Recurse
    }
}

function Get-SingleTestOutputDirectory {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$ArtifactsRoot)

    $matches = @(
        Get-ChildItem `
            -LiteralPath $ArtifactsRoot `
            -Recurse `
            -Filter "CUETools.Wpf.Tests.dll" `
            -File |
            Where-Object {
                (Test-Path -LiteralPath (
                    Join-Path $_.DirectoryName "CUETools.Wpf.Tests.deps.json") `
                    -PathType Leaf) -and
                (Test-Path -LiteralPath (
                    Join-Path $_.DirectoryName "testhost.dll") `
                    -PathType Leaf)
            } |
            ForEach-Object { $_.DirectoryName } |
            Sort-Object -Unique)
    if ($matches.Count -ne 1) {
        throw (
            "Expected one complete CUETools.Wpf.Tests output under '$ArtifactsRoot'; " +
            "found $($matches.Count).")
    }
    return [IO.Path]::GetFullPath($matches[0])
}

function Assert-MacTrxResults {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Architecture
    )

    $trxPath = Assert-RegularFile `
        -Path $Path `
        -Purpose "$Architecture Monkey's Audio TRX result"
    [xml]$document = [IO.File]::ReadAllText($trxPath)
    $namespace = New-Object Xml.XmlNamespaceManager($document.NameTable)
    $namespace.AddNamespace(
        "trx",
        [string]$document.DocumentElement.NamespaceURI)
    $results = @($document.SelectNodes(
        "//trx:UnitTestResult",
        $namespace))
    if ($results.Count -ne 2) {
        throw (
            "$Architecture Monkey's Audio test run produced $($results.Count) " +
            "results; expected the 16-bit and 24-bit cases.")
    }
    foreach ($result in $results) {
        if ([string]$result.outcome -cne "Passed" -or
            ([string]$result.testName).IndexOf(
                "MonkeyAudio_RoundTripsAndVerifiesRealPcm_OnNet8",
                [StringComparison]::Ordinal) -lt 0) {
            throw (
                "$Architecture Monkey's Audio TRX contains an unexpected result: " +
                "$($result.testName) [$($result.outcome)].")
        }
    }
}

function Remove-ValidatedTempTree {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedParent
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetFullPath(
        [IO.Path]::GetDirectoryName($fullPath))
    $expectedParentPath = [IO.Path]::GetFullPath($ExpectedParent)
    $leaf = [IO.Path]::GetFileName($fullPath)
    if (-not [string]::Equals(
            $parent,
            $expectedParentPath,
            [StringComparison]::OrdinalIgnoreCase) -or
        $leaf -cnotmatch "^cuetools-mac-architectures-[0-9a-f]{32}$") {
        throw "Refusing to clean an unexpected MAC architecture test path: $fullPath"
    }
    if (-not (Test-Path -LiteralPath $fullPath)) { return }
    $rootItem = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
    if (-not $rootItem.PSIsContainer -or
        ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to recursively clean a non-directory or reparse root: $fullPath"
    }
    Remove-Item -LiteralPath $fullPath -Recurse -Force
    if (Test-Path -LiteralPath $fullPath) {
        throw "MAC architecture test temporary directory remained after cleanup: $fullPath"
    }
}

$testProject = Assert-RegularFile `
    -Path $testProject `
    -Purpose "WPF integration test project"
$x64Native = Assert-RegularFile `
    -Path $x64Native `
    -Purpose "x64 Monkey's Audio native wrapper"
$x86Native = Assert-RegularFile `
    -Path $x86Native `
    -Purpose "x86 Monkey's Audio native wrapper"
Assert-PeMachine `
    -Path $x64Native `
    -ExpectedMachine 0x8664 `
    -Purpose "x64 Monkey's Audio native wrapper"
Assert-PeMachine `
    -Path $x86Native `
    -ExpectedMachine 0x014C `
    -Purpose "x86 Monkey's Audio native wrapper"

$dotnet = Resolve-X64DotNet -RequestedPath $DotNetPath
$vstest = Resolve-X86VSTest -RequestedPath $VSTestPath

$sdkVersionText = @(& $dotnet --version 2>&1)
$sdkExitCode = $LASTEXITCODE
if ($sdkExitCode -ne 0) {
    throw "The x64 dotnet host failed with exit code $sdkExitCode."
}
$sdkVersion = $null
if (-not [Version]::TryParse(
        (($sdkVersionText | Select-Object -First 1) -as [string]),
        [ref]$sdkVersion) -or
    $sdkVersion.Major -lt 8) {
    throw "Monkey's Audio architecture verification requires the .NET 8 SDK or newer."
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$tempRoot = Join-Path $tempBase (
    "cuetools-mac-architectures-" + [Guid]::NewGuid().ToString("N"))
[void][IO.Directory]::CreateDirectory($tempRoot)

try {
    $artifactsRoot = Join-Path $tempRoot "artifacts"
    $x64Results = Join-Path $tempRoot "results-x64"
    [void][IO.Directory]::CreateDirectory($x64Results)

    Write-Host "Running x64 Monkey's Audio 16/24-bit verified round trips."
    & $dotnet test $testProject `
        --configuration $Configuration `
        --artifacts-path $artifactsRoot `
        --filter $testFilter `
        --results-directory $x64Results `
        --logger "trx;LogFileName=mac-x64.trx" `
        --logger "console;verbosity=normal" `
        --nologo
    $x64ExitCode = $LASTEXITCODE
    if ($x64ExitCode -ne 0) {
        throw "x64 Monkey's Audio tests failed with exit code $x64ExitCode."
    }

    $testOutput = Get-SingleTestOutputDirectory `
        -ArtifactsRoot $artifactsRoot
    $testedX64Native = Join-Path $testOutput "x64\MACLibDll.dll"
    Assert-PeMachine `
        -Path $testedX64Native `
        -ExpectedMachine 0x8664 `
        -Purpose "staged x64 Monkey's Audio native wrapper"
    $sourceX64Hash = (
        Get-FileHash -LiteralPath $x64Native -Algorithm SHA256).Hash
    $testedX64Hash = (
        Get-FileHash -LiteralPath $testedX64Native -Algorithm SHA256).Hash
    if ($sourceX64Hash -cne $testedX64Hash) {
        throw "The x64 test output did not contain the requested native wrapper bytes."
    }
    Assert-MacTrxResults `
        -Path (Join-Path $x64Results "mac-x64.trx") `
        -Architecture "x64"

    $x86Stage = Join-Path $tempRoot "stage-x86"
    Copy-DirectoryContents `
        -Source $testOutput `
        -Destination $x86Stage

    # A successful run must prove the x86 host was selected. Remove only the
    # copied x64 MAC DLL from the isolated stage so an accidental x64 testhost
    # cannot pass by loading it.
    $stagedX64Native = Join-Path $x86Stage "x64\MACLibDll.dll"
    if (Test-Path -LiteralPath $stagedX64Native -PathType Leaf) {
        [IO.File]::Delete($stagedX64Native)
    }
    if (Test-Path -LiteralPath $stagedX64Native) {
        throw "The isolated x86 stage retained its x64 Monkey's Audio DLL."
    }

    $x86NativeDirectory = Join-Path $x86Stage "win32"
    if (-not (Test-Path -LiteralPath $x86NativeDirectory -PathType Container)) {
        [void][IO.Directory]::CreateDirectory($x86NativeDirectory)
    }
    $stagedX86Native = Join-Path $x86NativeDirectory "MACLibDll.dll"
    if (Test-Path -LiteralPath $stagedX86Native) {
        throw "The isolated x86 stage already contains MACLibDll.dll."
    }
    [IO.File]::Copy($x86Native, $stagedX86Native, $false)
    Assert-PeMachine `
        -Path $stagedX86Native `
        -ExpectedMachine 0x014C `
        -Purpose "staged x86 Monkey's Audio native wrapper"
    $sourceX86Hash = (
        Get-FileHash -LiteralPath $x86Native -Algorithm SHA256).Hash
    $stagedX86Hash = (
        Get-FileHash -LiteralPath $stagedX86Native -Algorithm SHA256).Hash
    if ($sourceX86Hash -cne $stagedX86Hash) {
        throw "The staged x86 Monkey's Audio native wrapper bytes changed during copy."
    }

    $x86Assembly = Join-Path $x86Stage "CUETools.Wpf.Tests.dll"
    $x86Results = Join-Path $tempRoot "results-x86"
    [void][IO.Directory]::CreateDirectory($x86Results)
    Write-Host "Running x86 Monkey's Audio 16/24-bit verified round trips."
    & $vstest $x86Assembly `
        "/Platform:x86" `
        "/TestCaseFilter:$testFilter" `
        "/ResultsDirectory:$x86Results" `
        "/Logger:trx;LogFileName=mac-x86.trx" `
        "/Logger:console;Verbosity=normal"
    $x86ExitCode = $LASTEXITCODE
    if ($x86ExitCode -ne 0) {
        throw "x86 Monkey's Audio tests failed with exit code $x86ExitCode."
    }
    Assert-MacTrxResults `
        -Path (Join-Path $x86Results "mac-x86.trx") `
        -Architecture "x86"

    Write-Host (
        "Monkey's Audio architecture verification PASS: " +
        "x64 exit=$x64ExitCode, x86 exit=$x86ExitCode, " +
        "two real 16/24-bit verified round trips per architecture.")
}
finally {
    Remove-ValidatedTempTree `
        -Path $tempRoot `
        -ExpectedParent $tempBase
}
