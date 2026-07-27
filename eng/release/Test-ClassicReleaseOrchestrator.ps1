[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$script:checkCount = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checkCount++
}

. (Join-Path $PSScriptRoot "Invoke-ClassicRelease.ps1")

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase (
    "cuetools-classic-orchestrator-" + [Guid]::NewGuid().ToString("N"))
$toolRoot = Join-Path $tempBase (
    "cuetools-classic-orchestrator-tools-" +
    [Guid]::NewGuid().ToString("N"))
$planPath = Join-Path $tempRoot "classic.collection.json"
$receiptPath = Join-Path $tempRoot (
    "bin\Release\evidence\classic-build-inputs.v2.json")
$warningBaselinePath = Join-Path $tempRoot (
    "eng\ci\native-warning-baseline.json")
$artifactDirectory = Join-Path $tempRoot "bin\Release\CUETools_1.0.0"
$alternateArtifactDirectory = Join-Path $tempRoot (
    "bin\Release\CUETools_9.9.9")
$collectorPath = Join-Path $PSScriptRoot "Collect-ClassicArtifacts.ps1"
$receiptHelperPath = Join-Path $PSScriptRoot "New-ClassicBuildReceipt.ps1"
$releaseSafetyPath = Join-Path $PSScriptRoot "ReleaseSafety.ps1"
$probePath = Join-Path $tempRoot "lease-probe.ps1"
$nativePaths = @(
    "ThirdParty/Win32/libFLAC_dynamic.dll",
    "ThirdParty/Win32/MACLibDll.dll",
    "ThirdParty/Win32/wavpackdll.dll",
    "ThirdParty/x64/libFLAC_dynamic.dll",
    "ThirdParty/x64/MACLibDll.dll",
    "ThirdParty/x64/wavpackdll.dll")
$freshPaths = @("bin/Release/net47/App.dll") + $nativePaths

function Write-TestFile([string]$Path, [string]$Value) {
    $parent = [IO.Path]::GetDirectoryName($Path)
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    [IO.File]::WriteAllText($Path, $Value)
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
Write-TestFile `
    -Path (Join-Path $tempRoot "CUETools.sln") `
    -Value "Microsoft Visual Studio Solution File`n"
Write-TestFile `
    -Path (Join-Path $tempRoot "Source.cs") `
    -Value "class Source { }`n"
Write-TestFile `
    -Path $warningBaselinePath `
    -Value (([ordered]@{
        schemaVersion = 1
        lane = "classic-release-fixture"
        builds = @("Release|x64", "Release|Win32")
        fingerprints = @()
        limitations = @()
    } | ConvertTo-Json -Depth 6) + "`n")
[IO.File]::WriteAllText(
    (Join-Path $tempRoot ".gitignore"),
    "bin/`nThirdParty/Win32/*.dll`nThirdParty/x64/*.dll`n")

$files = New-Object "Collections.Generic.List[object]"
$files.Add([ordered]@{
    source = "bin/Release/net47/App.dll"
    destination = "App.dll"
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
        collectionId = "classic-orchestrator-test"
        productVersion = "1.0.0"
        freshBuildOutputs = $nativePaths
        generatedFiles = @("notice.txt")
        files = $files.ToArray()
    } | ConvertTo-Json -Depth 8))
Write-TestFile `
    -Path $probePath `
    -Value @'
param([string]$ReleaseSafetyPath, [string]$ReleaseRoot)
$ErrorActionPreference = "Stop"
. $ReleaseSafetyPath
try {
    $lease = Enter-ClassicReleaseLease `
        -ReleaseRoot $ReleaseRoot `
        -TimeoutMilliseconds 150
    Exit-ClassicReleaseLease -Lease $lease
    exit 3
}
catch [TimeoutException] {
    exit 0
}
'@

try {
    & git -C $tempRoot init --quiet
    if ($LASTEXITCODE -ne 0) { throw "git init failed" }
    & git -C $tempRoot config user.email "classic-orchestrator@example.invalid"
    & git -C $tempRoot config user.name "Classic Orchestrator Test"
    & git -C $tempRoot add .
    & git -C $tempRoot commit --quiet -m "fixture"
    if ($LASTEXITCODE -ne 0) { throw "git commit failed" }

    foreach ($helperPath in @($receiptHelperPath, $collectorPath)) {
        $directRejected = $false
        try {
            & $helperPath
        }
        catch {
            $directRejected =
                $_.Exception.Message -match "Invoke-ClassicRelease\.ps1"
        }
        Assert-True `
            $directRejected `
            "Release helper allowed direct execution outside the orchestrator: $helperPath"
    }

    foreach ($relativePath in $freshPaths) {
        Write-TestFile `
            -Path (Join-Path $tempRoot $relativePath) `
            -Value "stale $relativePath"
    }
    $neighborPath = Join-Path $tempRoot "bin/Release/net47/neighbor.keep"
    Write-TestFile -Path $neighborPath -Value "neighbor"

    $script:observedCommands =
        New-Object "Collections.Generic.List[object]"
    $script:collectionCalls = 0
    $commandInvoker = {
        param($Command, $ToolPath, $LogPath, $RepositoryRoot)
        $script:observedCommands.Add($Command)
        if ([int]$Command.sequence -eq 1) {
            foreach ($relativePath in $freshPaths) {
                Assert-True `
                    (-not (Test-Path -LiteralPath (
                        Join-Path $RepositoryRoot $relativePath))) `
                    "Orchestrator did not remove a declared output before restore."
            }
            Assert-True `
                (Test-Path -LiteralPath $neighborPath -PathType Leaf) `
                "Exact-leaf cleanup removed a neighboring file."
        }
        if ([int]$Command.sequence -eq 2) {
            $process = Start-Process `
                -FilePath "powershell.exe" `
                -ArgumentList @(
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    $probePath,
                    $releaseSafetyPath,
                    ([IO.Path]::GetDirectoryName(
                        $alternateArtifactDirectory))) `
                -WindowStyle Hidden `
                -Wait `
                -PassThru
            Assert-True `
                ($process.ExitCode -eq 0) `
                "A second PowerShell process acquired the repo-wide lease for another product version."
        }
        if ([int]$Command.sequence -eq 4) {
            foreach ($relativePath in $freshPaths) {
                Write-TestFile `
                    -Path (Join-Path $RepositoryRoot $relativePath) `
                    -Value "fresh $relativePath"
            }
        }
        Write-TestFile `
            -Path $LogPath `
            -Value "fixture command $($Command.sequence)`n"
        return 0
    }
    $collectionInvoker = {
        param($BuildReceiptPath, $Lease, $LeaseToken, $ArtifactPath, $Receipt)
        Assert-ActiveClassicArtifactLease `
            -Lease $Lease `
            -ArtifactDirectory $ArtifactPath `
            -LeaseToken $LeaseToken
        $script:lastValidatedReceipt = Assert-ClassicBuildReceipt `
            -RepositoryRoot $tempRoot `
            -PlanPath $planPath `
            -ReceiptPath $BuildReceiptPath `
            -Configuration "Release" `
            -Platforms @("Any CPU", "x64", "Win32") `
            -TestToolRoot $toolRoot `
            -Lease $Lease `
            -LeaseToken $LeaseToken
        Assert-True `
            ([string]$Receipt.receiptContentSha256 -match "^[0-9A-F]{64}$") `
            "Collector callback did not receive the complete receipt digest."
        Assert-True `
            ([string]$Receipt.leaseToken -ceq $LeaseToken) `
            "Collector callback received a receipt for another release lease."
        $script:collectionCalls++
    }
    $toolchain = New-TestClassicBuildToolchain -ToolRoot $toolRoot
    $result = Invoke-ClassicRelease `
        -RepositoryRoot $tempRoot `
        -PlanPath $planPath `
        -ReceiptPath $receiptPath `
        -LeaseTimeoutMilliseconds 1000 `
        -TestToolchain $toolchain `
        -TestToolRoot $toolRoot `
        -TestCommandInvoker $commandInvoker `
        -TestCollectionInvoker $collectionInvoker

    Assert-True `
        ($script:collectionCalls -eq 1) `
        "Orchestrator did not call collection exactly once."
    Assert-True `
        ($script:observedCommands.Count -eq 4) `
        "Orchestrator did not execute exactly four canonical commands."
    Assert-True `
        ([string]$script:observedCommands[0].role -ceq "restore" -and
            ((@($script:observedCommands |
                Select-Object -Skip 1 |
                ForEach-Object { $_.arguments[1] }) -join "`n") -ceq
                "/Rebuild`n/Rebuild`n/Rebuild")) `
        "Orchestrator command order or /Rebuild arguments drifted."
    Assert-True `
        (Test-Path -LiteralPath $neighborPath -PathType Leaf) `
        "Release cleanup removed a neighboring file."
    foreach ($relativePath in $nativePaths) {
        Assert-True `
            (Test-Path -LiteralPath (
                Join-Path $tempRoot $relativePath) -PathType Leaf) `
            "Orchestrator did not recreate native output '$relativePath'."
    }
    $validated = $script:lastValidatedReceipt
    Assert-True `
        (@($validated.collectionInputs).Count -eq $files.Count) `
        "Orchestrator receipt omitted a collection source."
    Assert-True `
        ([string]$result.receiptContentSha256 -ceq
            [string]$validated.receiptContentSha256) `
        "Orchestrator returned a digest other than the complete receipt digest."

    $postLease = Enter-ClassicReleaseLease `
        -ReleaseRoot ([IO.Path]::GetDirectoryName(
            $alternateArtifactDirectory)) `
        -TimeoutMilliseconds 500
    Exit-ClassicReleaseLease -Lease $postLease
    Assert-True $true "Orchestrator did not release its lease."

    [IO.File]::Delete($receiptPath)
    $script:collectionCallsBeforeFailure = $script:collectionCalls
    $failingInvoker = {
        param($Command, $ToolPath, $LogPath, $RepositoryRoot)
        Write-TestFile `
            -Path $LogPath `
            -Value "fixture command $($Command.sequence)`n"
        if ([int]$Command.sequence -eq 2) { return 9 }
        return 0
    }
    $failureSurfaced = $false
    try {
        [void](Invoke-ClassicRelease `
            -RepositoryRoot $tempRoot `
            -PlanPath $planPath `
            -ReceiptPath $receiptPath `
            -LeaseTimeoutMilliseconds 1000 `
            -TestToolchain $toolchain `
            -TestToolRoot $toolRoot `
            -TestCommandInvoker $failingInvoker `
            -TestCollectionInvoker $collectionInvoker)
    }
    catch {
        $failureSurfaced =
            $_.Exception.Message -match "exit code 9"
    }
    Assert-True `
        $failureSurfaced `
        "Nonzero canonical build command did not fail the orchestrator."
    Assert-True `
        (Test-Path -LiteralPath (
            $receiptPath + ".intent.json") -PathType Leaf) `
        "Failed build did not preserve its intent."
    Assert-True `
        (-not (Test-Path -LiteralPath $receiptPath)) `
        "Failed build created a completed receipt."
    Assert-True `
        ($script:collectionCalls -eq
            $script:collectionCallsBeforeFailure) `
        "Failed build invoked artifact collection."

    $failedIntentDocument = Read-ClassicBuildJsonDocument `
        -Path ($receiptPath + ".intent.json") `
        -Purpose "Failed test build intent"
    $failedBuildId = [string]$failedIntentDocument.value.buildId
    $failedIntentArchive = $receiptPath + ".intent." +
        $failedBuildId + ".abandoned.json"
    [IO.File]::AppendAllText(
        (Join-Path $tempRoot "Source.cs"),
        "// repair after failed build`n")
    $staleIntentRejected = $false
    try {
        [void](Invoke-ClassicRelease `
            -RepositoryRoot $tempRoot `
            -PlanPath $planPath `
            -ReceiptPath $receiptPath `
            -LeaseTimeoutMilliseconds 1000 `
            -TestToolchain $toolchain `
            -TestToolRoot $toolRoot `
            -TestCommandInvoker $commandInvoker `
            -TestCollectionInvoker $collectionInvoker)
    }
    catch {
        $staleIntentRejected =
            $_.Exception.Message -match "stale source fingerprint"
    }
    Assert-True `
        $staleIntentRejected `
        "Retry silently archived a pending intent after its source changed."
    Assert-True `
        (Test-Path -LiteralPath (
            $receiptPath + ".intent.json") -PathType Leaf) `
        "Rejected stale-intent recovery did not retain the pending intent."
    $retryResult = Invoke-ClassicRelease `
        -RepositoryRoot $tempRoot `
        -PlanPath $planPath `
        -ReceiptPath $receiptPath `
        -LeaseTimeoutMilliseconds 1000 `
        -ArchiveStalePendingIntent `
        -TestToolchain $toolchain `
        -TestToolRoot $toolRoot `
        -TestCommandInvoker $commandInvoker `
        -TestCollectionInvoker $collectionInvoker
    Assert-True `
        (Test-Path -LiteralPath $failedIntentArchive -PathType Leaf) `
        "Retry did not archive the validated failed-build intent."
    Assert-True `
        (-not (Test-Path -LiteralPath (
            $receiptPath + ".intent.json"))) `
        "Retry retained a pending build intent."
    $archivedIntentDocument = Read-ClassicBuildJsonDocument `
        -Path $failedIntentArchive `
        -Purpose "Archived failed test build intent"
    Assert-True `
        ([string]$archivedIntentDocument.sha256 -ceq
            [string]$failedIntentDocument.sha256) `
        "Retry archive changed the failed-build intent bytes."
    Assert-True `
        ($script:collectionCalls -eq
            ($script:collectionCallsBeforeFailure + 1)) `
        "Successful retry did not invoke collection exactly once."
    Assert-True `
        ([string]$retryResult.receiptContentSha256 -ceq
            [string]$script:lastValidatedReceipt.receiptContentSha256) `
        "Successful retry returned a different receipt digest than collection validated."

    [IO.File]::Delete($receiptPath)
    $collectionsBeforeWarningFailure = $script:collectionCalls
    $warningInvoker = {
        param($Command, $ToolPath, $LogPath, $RepositoryRoot)
        if ([int]$Command.sequence -eq 4) {
            foreach ($relativePath in $freshPaths) {
                Write-TestFile `
                    -Path (Join-Path $RepositoryRoot $relativePath) `
                    -Value "fresh $relativePath"
            }
        }
        $text = "fixture command $($Command.sequence)`n"
        if ([string]$Command.role -ceq "rebuild" -and
            [string]$Command.tuple -ceq "Release|x64") {
            $text +=
                "$RepositoryRoot\ThirdParty\flac\fixture.c(9): " +
                "warning C4999: novel release warning`n"
        }
        Write-TestFile -Path $LogPath -Value $text
        return 0
    }
    $warningFailureSurfaced = $false
    try {
        [void](Invoke-ClassicRelease `
            -RepositoryRoot $tempRoot `
            -PlanPath $planPath `
            -ReceiptPath $receiptPath `
            -LeaseTimeoutMilliseconds 1000 `
            -TestToolchain $toolchain `
            -TestToolRoot $toolRoot `
            -TestCommandInvoker $warningInvoker `
            -TestCollectionInvoker $collectionInvoker)
    }
    catch {
        $warningFailureSurfaced =
            $_.Exception.Message -match "new fingerprint"
    }
    Assert-True `
        $warningFailureSurfaced `
        "A novel native warning did not fail the release orchestrator."
    Assert-True `
        (Test-Path -LiteralPath (
            $receiptPath + ".intent.json") -PathType Leaf) `
        "Warning-budget failure did not preserve its build intent."
    Assert-True `
        (-not (Test-Path -LiteralPath $receiptPath)) `
        "Warning-budget failure created a completed receipt."
    Assert-True `
        ($script:collectionCalls -eq $collectionsBeforeWarningFailure) `
        "Warning-budget failure invoked artifact collection."

    $warningIntent = Read-ClassicBuildJsonDocument `
        -Path ($receiptPath + ".intent.json") `
        -Purpose "Warning-failed test build intent"
    $warningIntentArchive = $receiptPath + ".intent." +
        [string]$warningIntent.value.buildId + ".abandoned.json"
    [void](Invoke-ClassicRelease `
        -RepositoryRoot $tempRoot `
        -PlanPath $planPath `
        -ReceiptPath $receiptPath `
        -LeaseTimeoutMilliseconds 1000 `
        -TestToolchain $toolchain `
        -TestToolRoot $toolRoot `
        -TestCommandInvoker $commandInvoker `
        -TestCollectionInvoker $collectionInvoker)
    Assert-True `
        (Test-Path -LiteralPath $warningIntentArchive -PathType Leaf) `
        "Retry did not archive the warning-failed build intent."
    Assert-True `
        ($script:collectionCalls -eq
            ($collectionsBeforeWarningFailure + 1)) `
        "Successful warning-gate retry did not collect exactly once."

    $planContext = Get-ClassicCollectionPlan `
        -RepositoryRoot $tempRoot `
        -PlanPath $planPath
    [void](Remove-ClassicFreshBuildOutputLeaves `
        -RepositoryRoot $tempRoot `
        -Plan $planContext)
    $setupLease = Enter-ClassicReleaseLease `
        -ReleaseRoot (Join-Path $tempRoot "bin\Release")
    try {
        [void](Start-ClassicBuildReceipt `
            -RepositoryRoot $tempRoot `
            -PlanPath $planPath `
            -ReceiptPath $receiptPath `
            -Configuration "Release" `
            -Platforms @("Any CPU", "x64", "Win32") `
            -TestToolchain $toolchain `
            -TestToolRoot $toolRoot `
            -Lease $setupLease `
            -LeaseToken ([string]$setupLease.token))
    }
    finally {
        Exit-ClassicReleaseLease -Lease $setupLease
    }
    $corruptIntentPath = $receiptPath + ".intent.json"
    $validPendingIntentText =
        Get-Content -LiteralPath $corruptIntentPath -Raw
    $corruptIntent = ConvertFrom-ClassicBuildJson `
        -Text $validPendingIntentText
    $corruptIntent.leaseToken = [Guid]::NewGuid().ToString("N")
    [IO.File]::WriteAllText(
        $corruptIntentPath,
        ($corruptIntent | ConvertTo-Json -Depth 32))
    foreach ($relativePath in $freshPaths) {
        Write-TestFile `
            -Path (Join-Path $tempRoot $relativePath) `
            -Value "must remain $relativePath"
    }
    $corruptIntentRejected = $false
    try {
        [void](Invoke-ClassicRelease `
            -RepositoryRoot $tempRoot `
            -PlanPath $planPath `
            -ReceiptPath $receiptPath `
            -LeaseTimeoutMilliseconds 1000 `
            -TestToolchain $toolchain `
            -TestToolRoot $toolRoot `
            -TestCommandInvoker $commandInvoker `
            -TestCollectionInvoker $collectionInvoker)
    }
    catch {
        $corruptIntentRejected =
            $_.Exception.Message -match "prior repo-wide release lease"
    }
    Assert-True `
        $corruptIntentRejected `
        "Orchestrator accepted a foreign pending build intent."
    foreach ($relativePath in $freshPaths) {
        Assert-True `
            ((Get-Content -LiteralPath (
                Join-Path $tempRoot $relativePath) -Raw) -ceq
                "must remain $relativePath") `
            "Foreign pending-intent refusal changed output leaf '$relativePath'."
    }

    [IO.File]::WriteAllText(
        $corruptIntentPath,
        '{"schemaVersion":')
    $malformedIntentRejected = $false
    try {
        [void](Invoke-ClassicRelease `
            -RepositoryRoot $tempRoot `
            -PlanPath $planPath `
            -ReceiptPath $receiptPath `
            -LeaseTimeoutMilliseconds 1000 `
            -TestToolchain $toolchain `
            -TestToolRoot $toolRoot `
            -TestCommandInvoker $commandInvoker `
            -TestCollectionInvoker $collectionInvoker)
    }
    catch {
        $malformedIntentRejected = $true
    }
    Assert-True `
        $malformedIntentRejected `
        "Orchestrator accepted malformed pending build intent JSON."
    foreach ($relativePath in $freshPaths) {
        Assert-True `
            ((Get-Content -LiteralPath (
                Join-Path $tempRoot $relativePath) -Raw) -ceq
                "must remain $relativePath") `
            "Malformed pending-intent refusal changed output leaf '$relativePath'."
    }

    foreach ($unsafe in @(
        "../escape.dll",
        "safe/stream:ads",
        "safe/CON",
        "safe/trailing. ",
        "safe//empty")) {
        $rejected = $false
        try {
            [void](ConvertTo-ClassicRelativePath `
                -RelativePath $unsafe `
                -Purpose "Test unsafe path")
        }
        catch { $rejected = $true }
        Assert-True `
            $rejected `
            "Strict release path validation accepted '$unsafe'."
    }
}
finally {
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
                "cuetools-classic-orchestrator-",
                [StringComparison]::Ordinal) -and
             -not $leaf.StartsWith(
                "cuetools-classic-orchestrator-tools-",
                [StringComparison]::Ordinal))) {
            throw "Refusing to clean unexpected orchestrator test path: $path"
        }
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
}

Write-Host "Classic release orchestrator checks passed: $script:checkCount"
