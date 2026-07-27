[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$script:checkCount = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checkCount++
}

. (Join-Path $PSScriptRoot "New-ClassicBuildReceipt.ps1")

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase (
    "cuetools-classic-build-receipt-" +
    [Guid]::NewGuid().ToString("N"))
$toolRoot = Join-Path $tempBase (
    "cuetools-classic-build-tools-" +
    [Guid]::NewGuid().ToString("N"))
$planPath = Join-Path $tempRoot "classic.collection.json"
$receiptPath = Join-Path $tempRoot (
    "bin\Release\evidence\classic-build-inputs.v2.json")
$sourcePath = Join-Path $tempRoot "Source.cs"
$solutionPath = Join-Path $tempRoot "CUETools.sln"
$warningBaselinePath = Join-Path $tempRoot (
    "eng\ci\native-warning-baseline.json")
$platforms = @("Any CPU", "x64", "Win32")
$nativePaths = @(
    "ThirdParty/Win32/libFLAC_dynamic.dll",
    "ThirdParty/Win32/MACLibDll.dll",
    "ThirdParty/Win32/wavpackdll.dll",
    "ThirdParty/x64/libFLAC_dynamic.dll",
    "ThirdParty/x64/MACLibDll.dll",
    "ThirdParty/x64/wavpackdll.dll")
$lease = $null

function Write-TestFile([string]$Path, [string]$Value) {
    $parent = [IO.Path]::GetDirectoryName($Path)
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Value)
}

function Initialize-TestMacSdk([string]$Root) {
    $macRoot = Join-Path $Root "ThirdParty\MAC_SDK"
    $archivePath = Join-Path $macRoot "MAC_1320_SDK.zip"
    $archiveFiles = [ordered]@{
        "License.txt" = "fixture license`n"
        "Source/Shared/Fixture.h" = "#define FIXTURE_MAC_SDK 1320`n"
    }
    foreach ($entry in $archiveFiles.GetEnumerator()) {
        Write-TestFile `
            -Path (Join-Path $macRoot (
                ([string]$entry.Key).Replace(
                    "/",
                    [IO.Path]::DirectorySeparatorChar))) `
            -Value ([string]$entry.Value)
    }
    foreach ($relativePath in @(
        "Source/MACLibDll/MACLibDll.cpp",
        "Source/MACLibDll/MACLibDll.def",
        "Source/MACLibDll/MACLibDll.h",
        "Source/Projects/VS2022/MACLibDll/MACLibDll.vcxproj")) {
        Write-TestFile `
            -Path (Join-Path $macRoot (
                $relativePath.Replace(
                    "/",
                    [IO.Path]::DirectorySeparatorChar))) `
            -Value "fixture override $relativePath`n"
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open(
        $archivePath,
        [IO.Compression.ZipArchiveMode]::Create)
    try {
        $utf8 = New-Object Text.UTF8Encoding($false)
        foreach ($entry in $archiveFiles.GetEnumerator()) {
            $zipEntry = $archive.CreateEntry([string]$entry.Key)
            $stream = $zipEntry.Open()
            try {
                $bytes = $utf8.GetBytes([string]$entry.Value)
                $stream.Write($bytes, 0, $bytes.Length)
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $pinnedPaths = @(
        "ThirdParty/MAC_SDK/MAC_1320_SDK.zip",
        "ThirdParty/MAC_SDK/Source/MACLibDll/MACLibDll.cpp",
        "ThirdParty/MAC_SDK/Source/MACLibDll/MACLibDll.def",
        "ThirdParty/MAC_SDK/Source/MACLibDll/MACLibDll.h",
        "ThirdParty/MAC_SDK/Source/Projects/VS2022/MACLibDll/MACLibDll.vcxproj")
    $pins = @($pinnedPaths | ForEach-Object {
        [ordered]@{
            path = $_
            sha256 = (Get-FileHash `
                -LiteralPath (Join-Path $Root (
                    $_.Replace(
                        "/",
                        [IO.Path]::DirectorySeparatorChar))) `
                -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
    Write-TestFile `
        -Path (Join-Path $Root "eng\release\native-dependencies.json") `
        -Value (([ordered]@{
            pinnedFiles = $pins
        } | ConvertTo-Json -Depth 6) + "`n")
}

function New-TestCommandRecords(
    [object[]]$CommandPlan,
    [string]$Root) {
    $records = New-Object "Collections.Generic.List[object]"
    foreach ($command in $CommandPlan) {
        $logPath = Resolve-ClassicRepositoryPath `
            -RepositoryRoot $Root `
            -RelativePath ([string]$command.logPath) `
            -Purpose "Test command log"
        Write-TestFile `
            -Path $logPath `
            -Value "command $($command.sequence): $($command.arguments -join ' ')`n"
        $log = Get-ClassicBuildRegularFileRecord `
            -Path $logPath `
            -Purpose "Test command log"
        $record = [ordered]@{
            sequence = [int]$command.sequence
            role = [string]$command.role
        }
        if ([string]$command.role -eq "rebuild") {
            $record.tuple = [string]$command.tuple
        }
        $record.toolRole = [string]$command.toolRole
        $record.arguments = [string[]]@($command.arguments)
        $record.startedAtUtc = Get-ClassicUtcTimestamp
        $record.completedAtUtc = Get-ClassicUtcTimestamp
        $record.exitCode = 0
        $record.log = [pscustomobject]([ordered]@{
            path = [string]$command.logPath
            bytes = [long]$log.bytes
            sha256 = [string]$log.sha256
        })
        $records.Add([pscustomobject]$record)
    }
    return $records.ToArray()
}

function Assert-ReceiptRejected(
    [string]$ExpectedMessage,
    [string]$Message) {
    $rejected = $false
    try {
        [void](Assert-ClassicBuildReceipt `
            -RepositoryRoot $tempRoot `
            -PlanPath $planPath `
            -ReceiptPath $receiptPath `
            -Configuration "Release" `
            -Platforms $platforms `
            -TestToolRoot $toolRoot `
            -Lease $lease `
            -LeaseToken ([string]$lease.token))
    }
    catch {
        $rejected = $_.Exception.Message -match $ExpectedMessage
    }
    Assert-True $rejected $Message
}

New-Item -ItemType Directory -Path $tempRoot | Out-Null
foreach ($relativePath in @(
    "Common7/IDE/devenv.com",
    "Common7/IDE/devenv.exe",
    "MSBuild/Current/Bin/MSBuild.exe",
    "MSBuild/Current/Bin/Roslyn/csc.exe",
    "VC/Tools/MSVC/14.0/bin/Hostx64/x86/cl.exe",
    "VC/Tools/MSVC/14.0/bin/Hostx64/x86/link.exe",
    "VC/Tools/MSVC/14.0/bin/Hostx64/x64/cl.exe",
    "VC/Tools/MSVC/14.0/bin/Hostx64/x64/link.exe",
    "VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt",
    ".NETFramework/v4.7/mscorlib.dll",
    "Common7/IDE/CommonExtensions/Microsoft/VSI/bin/Microsoft.VisualStudio.InstallerProjects.dll")) {
    Write-TestFile `
        -Path (Join-Path $toolRoot $relativePath) `
        -Value "fixture tool $relativePath"
}
Write-TestFile -Path $sourcePath -Value "class Source { }`n"
Write-TestFile -Path $solutionPath -Value "Microsoft Visual Studio Solution File`n"
[IO.File]::WriteAllText(
    (Join-Path $tempRoot ".gitignore"),
    "bin/`n" +
    "ThirdParty/Win32/*.dll`n" +
    "ThirdParty/x64/*.dll`n" +
    "ThirdParty/MAC_SDK/License.txt`n" +
    "ThirdParty/MAC_SDK/Source/Shared/*`n")
Initialize-TestMacSdk -Root $tempRoot
Write-TestFile `
    -Path $warningBaselinePath `
    -Value (([ordered]@{
        schemaVersion = 1
        lane = "classic-release-fixture"
        builds = @("Release|x64", "Release|Win32")
        fingerprints = @()
        limitations = @()
    } | ConvertTo-Json -Depth 6) + "`n")

$files = New-Object "Collections.Generic.List[object]"
$files.Add([ordered]@{
    source = "bin/Release/net47/App.dll"
    destination = "App.dll"
})
$files.Add([ordered]@{
    source = "bin/Release/net47/App.dll.config"
    destination = "App.dll.config"
})
$files.Add([ordered]@{
    source = "Source.cs"
    destination = "Source.cs"
})
foreach ($relativePath in $nativePaths) {
    $files.Add([ordered]@{
        source = $relativePath
        destination = "native/" + $relativePath.Replace("/", "-")
    })
}
[IO.File]::WriteAllText(
    $planPath,
    ([ordered]@{
        schemaVersion = 2
        collectionId = "classic-build-receipt-test"
        productVersion = "1.0.0"
        freshBuildOutputs = $nativePaths
        generatedFiles = @("notice.txt")
        files = $files.ToArray()
    } | ConvertTo-Json -Depth 8))

try {
    & git -C $tempRoot init --quiet
    if ($LASTEXITCODE -ne 0) { throw "git init failed" }
    & git -C $tempRoot config user.email "classic-receipt@example.invalid"
    & git -C $tempRoot config user.name "Classic Receipt Test"
    & git -C $tempRoot add .
    & git -C $tempRoot commit --quiet -m "fixture"
    if ($LASTEXITCODE -ne 0) { throw "git commit failed" }

    $testToolchain = New-TestClassicBuildToolchain -ToolRoot $toolRoot
    $lease = Enter-ClassicReleaseLease `
        -ReleaseRoot (Join-Path $tempRoot "bin\Release")
    $leaseToken = [string]$lease.token
    $intent = Start-ClassicBuildReceipt `
        -RepositoryRoot $tempRoot `
        -PlanPath $planPath `
        -ReceiptPath $receiptPath `
        -Configuration "Release" `
        -Platforms $platforms `
        -TestToolchain $testToolchain `
        -TestToolRoot $toolRoot `
        -Lease $lease `
        -LeaseToken $leaseToken
    Assert-True `
        ($intent.schemaVersion -eq 2 -and
            $intent.kind -eq "cuetools-classic-build-intent") `
        "Begin did not create a schema-v2 classic build intent."
    Assert-True `
        ([string]$intent.leaseToken -ceq $leaseToken) `
        "Build intent did not bind the active release lease token."
    Assert-True `
        ([string]$intent.source.expandedNativeInputs[0].state -ceq
            "expanded" -and
            [int]$intent.source.expandedNativeInputs[0].archiveFileCount -eq
                2) `
        "Build intent did not explicitly bind ignored expanded native source."
    Assert-True `
        (@($intent.requiredAbsentAtBegin).Count -eq 8) `
        "Begin did not bind both compiled leaves and all six native outputs."
    Assert-True `
        (@($intent.commandPlan).Count -eq 4 -and
            [string]$intent.commandPlan[1].arguments[1] -ceq "/Rebuild") `
        "Intent omitted the exact restore plus three /Rebuild commands."

    $generatedResidue = Join-Path $tempRoot (
        "ThirdParty/flac/src/libFLAC/x64/Release_dynamic/generated.obj")
    Write-TestFile -Path $generatedResidue -Value "old generated bytes"

    Write-TestFile `
        -Path (Join-Path $tempRoot "bin/Release/net47/App.dll") `
        -Value "fresh compiled bytes"
    Write-TestFile `
        -Path (Join-Path $tempRoot "bin/Release/net47/App.dll.config") `
        -Value "fresh copied config"
    foreach ($relativePath in $nativePaths) {
        Write-TestFile `
            -Path (Join-Path $tempRoot $relativePath) `
            -Value "fresh native $relativePath"
    }
    $commands = @(New-TestCommandRecords `
        -CommandPlan @($intent.commandPlan) `
        -Root $tempRoot)
    $nativeWarningGate = Get-ClassicNativeWarningGate `
        -RepositoryRoot $tempRoot `
        -Commands $commands
    Write-TestFile -Path $generatedResidue -Value "new generated bytes"
    $receipt = Complete-ClassicBuildReceipt `
        -RepositoryRoot $tempRoot `
        -PlanPath $planPath `
        -ReceiptPath $receiptPath `
        -Configuration "Release" `
        -Platforms $platforms `
        -CommandRecords $commands `
        -NativeWarningGate $nativeWarningGate `
        -TestToolRoot $toolRoot `
        -Lease $lease `
        -LeaseToken $leaseToken
    Assert-True `
        ($receipt.schemaVersion -eq 2) `
        "Complete did not create a schema-v2 receipt."
    Assert-True `
        ([string]$receipt.leaseToken -ceq $leaseToken) `
        "Complete receipt did not bind the active release lease token."
    Assert-True `
        (@($receipt.collectionInputs).Count -eq $files.Count) `
        "Complete did not bind every collection-plan source."
    Assert-True `
        ((@($receipt.nativeWarningGate.coverageIds) -join "`n") -ceq
            "Release|x64`nRelease|Win32" -and
            @($receipt.nativeWarningGate.logs).Count -eq 2) `
        "Complete did not bind the exact x64 and Win32 warning logs."
    Assert-True `
        (@($receipt.collectionInputs | Where-Object {
            [bool]$_.freshBuildOutput
        }).Count -eq 8) `
        "Receipt did not mark every required fresh output."
    Assert-True `
        ([string]$receipt.receiptContentSha256 -match "^[0-9A-F]{64}$") `
        "Complete did not return the exact on-disk receipt digest."
    [void](Assert-ClassicBuildReceipt `
        -RepositoryRoot $tempRoot `
        -PlanPath $planPath `
        -ReceiptPath $receiptPath `
        -Configuration "Release" `
        -Platforms $platforms `
        -TestToolRoot $toolRoot `
        -Lease $lease `
        -LeaseToken $leaseToken)
    Assert-True $true "Fresh schema-v2 receipt was rejected."

    $fixtureRejectedByProduction = $false
    try {
        [void](Assert-ClassicBuildReceipt `
            -RepositoryRoot $tempRoot `
            -PlanPath $planPath `
            -ReceiptPath $receiptPath `
            -Configuration "Release" `
            -Platforms $platforms `
            -Lease $lease `
            -LeaseToken $leaseToken)
    }
    catch {
        $fixtureRejectedByProduction =
            $_.Exception.Message -match "test fixture"
    }
    Assert-True `
        $fixtureRejectedByProduction `
        "Production validation accepted fake fixture tools."

    $originalReceiptText = [IO.File]::ReadAllText($receiptPath)
    $tampered = ConvertFrom-ClassicBuildJson -Text $originalReceiptText
    $tampered.leaseToken = [Guid]::NewGuid().ToString("N")
    [IO.File]::WriteAllText(
        $receiptPath,
        ($tampered | ConvertTo-Json -Depth 32))
    Assert-ReceiptRejected `
        -ExpectedMessage "does not match the current plan and build tuple" `
        -Message "Receipt accepted another release lease token."
    [IO.File]::WriteAllText($receiptPath, $originalReceiptText)

    $expandedMacSource = Join-Path $tempRoot (
        "ThirdParty\MAC_SDK\Source\Shared\Fixture.h")
    Write-TestFile `
        -Path $expandedMacSource `
        -Value "#define FIXTURE_MAC_SDK 9999`n"
    Assert-ReceiptRejected `
        -ExpectedMessage "differs from its pinned archive" `
        -Message "Receipt accepted drift in ignored expanded native source."
    Write-TestFile `
        -Path $expandedMacSource `
        -Value "#define FIXTURE_MAC_SDK 1320`n"

    $nativeManifestPath = Join-Path $tempRoot (
        "eng\release\native-dependencies.json")
    $nativeManifestText = [IO.File]::ReadAllText($nativeManifestPath)
    $nativeManifest = ConvertFrom-ClassicBuildJson -Text $nativeManifestText
    $nativeManifest.pinnedFiles[0].sha256 = "0" * 64
    [IO.File]::WriteAllText(
        $nativeManifestPath,
        ($nativeManifest | ConvertTo-Json -Depth 8))
    Assert-ReceiptRejected `
        -ExpectedMessage "does not match the native dependency manifest" `
        -Message "Receipt accepted an expanded SDK detached from its source pin."
    [IO.File]::WriteAllText($nativeManifestPath, $nativeManifestText)

    $tampered = ConvertFrom-ClassicBuildJson -Text $originalReceiptText
    $tampered.nativeWarningGate.baseline.sha256 = "0" * 64
    [IO.File]::WriteAllText(
        $receiptPath,
        ($tampered | ConvertTo-Json -Depth 32))
    Assert-ReceiptRejected `
        -ExpectedMessage "native warning gate is stale" `
        -Message "Receipt accepted a tampered native warning baseline digest."
    [IO.File]::WriteAllText($receiptPath, $originalReceiptText)

    $tampered = ConvertFrom-ClassicBuildJson -Text $originalReceiptText
    $tampered.nativeWarningGate.logs[0].path =
        [string]$tampered.commands[0].log.path
    [IO.File]::WriteAllText(
        $receiptPath,
        ($tampered | ConvertTo-Json -Depth 32))
    Assert-ReceiptRejected `
        -ExpectedMessage "native warning gate is stale" `
        -Message "Receipt accepted a warning gate bound to the restore log."
    [IO.File]::WriteAllText($receiptPath, $originalReceiptText)

    $tampered = ConvertFrom-ClassicBuildJson -Text $originalReceiptText
    $tampered.commands[1].arguments[1] = "/Build"
    [IO.File]::WriteAllText(
        $receiptPath,
        ($tampered | ConvertTo-Json -Depth 32))
    Assert-ReceiptRejected `
        -ExpectedMessage "canonical invocation" `
        -Message "Receipt accepted /Build in place of /Rebuild."
    [IO.File]::WriteAllText($receiptPath, $originalReceiptText)

    $tampered = ConvertFrom-ClassicBuildJson -Text $originalReceiptText
    $tampered.toolchain.tools =
        @($tampered.toolchain.tools | Select-Object -Skip 1)
    [IO.File]::WriteAllText(
        $receiptPath,
        ($tampered | ConvertTo-Json -Depth 32))
    Assert-ReceiptRejected `
        -ExpectedMessage "role count" `
        -Message "Receipt accepted a missing tool role."
    [IO.File]::WriteAllText($receiptPath, $originalReceiptText)

    Write-TestFile `
        -Path (Join-Path $tempRoot "bin/Release/net47/App.dll") `
        -Value "tampered compiled bytes"
    Assert-ReceiptRejected `
        -ExpectedMessage "changed after its build receipt" `
        -Message "Receipt accepted collection input tampering."
    Write-TestFile `
        -Path (Join-Path $tempRoot "bin/Release/net47/App.dll") `
        -Value "fresh compiled bytes"

    [IO.File]::WriteAllText(
        $sourcePath,
        "class Source { int Changed; }`n")
    Assert-ReceiptRejected `
        -ExpectedMessage "source fingerprint is stale" `
        -Message "Receipt accepted a changed source fingerprint."
    & git -C $tempRoot checkout --quiet -- Source.cs
    if ($LASTEXITCODE -ne 0) { throw "fixture source restore failed" }

    $untrackedSource = Join-Path $tempRoot "UntrackedSource.cs"
    Write-TestFile `
        -Path $untrackedSource `
        -Value "class UntrackedSource { }`n"
    Assert-ReceiptRejected `
        -ExpectedMessage "source fingerprint is stale" `
        -Message "Receipt accepted a new untracked source file."
    [IO.File]::Delete($untrackedSource)

    Write-TestFile `
        -Path (Join-Path $toolRoot "MSBuild/Current/Bin/MSBuild.exe") `
        -Value "changed msbuild tool"
    Assert-ReceiptRejected `
        -ExpectedMessage "toolchain no longer matches" `
        -Message "Receipt accepted changed toolchain bytes."
}
finally {
    if ($lease -ne $null) {
        Exit-ClassicReleaseLease -Lease $lease
        $lease = $null
    }
    foreach ($path in @($tempRoot, $toolRoot)) {
        $tempPrefix = $tempBase.TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar
        $leaf = [IO.Path]::GetFileName($path)
        if (-not $path.StartsWith(
                $tempPrefix,
                [StringComparison]::OrdinalIgnoreCase) -or
            (-not $leaf.StartsWith(
                "cuetools-classic-build-receipt-",
                [StringComparison]::Ordinal) -and
             -not $leaf.StartsWith(
                "cuetools-classic-build-tools-",
                [StringComparison]::Ordinal))) {
            throw "Refusing to clean unexpected receipt test path: $path"
        }
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
}

Write-Host "Classic build receipt checks passed: $script:checkCount"
