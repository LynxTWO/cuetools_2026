[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$script:checkCount = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checkCount++
}

$releaseTestRoot = $PSScriptRoot
$collectorPath = Join-Path $releaseTestRoot "Collect-ClassicArtifacts.ps1"
. $collectorPath
$planPath = Join-Path $releaseTestRoot "classic-win.collection.json"
$contractPath = Join-Path $releaseTestRoot "classic-win.manifest.json"
$batchPath = Join-Path $releaseTestRoot "..\..\collect_files.bat"

$batchSource = Get-Content -LiteralPath $batchPath -Raw
Assert-True `
    ($batchSource.Contains("Invoke-ClassicRelease.ps1") -and
        -not $batchSource.Contains(
            "-File ""%~dp0eng\release\Collect-ClassicArtifacts.ps1""")) `
    "collect_files.bat does not delegate only to the classic release orchestrator."
Assert-True `
    ($batchSource.IndexOf(
        "xcopy",
        [StringComparison]::OrdinalIgnoreCase) -lt 0) `
    "collect_files.bat still contains an incremental xcopy path."

$collectorSource = Get-Content -LiteralPath $collectorPath -Raw
Assert-True `
    ($collectorSource.Contains("Assert-ExactArtifactFiles") -and
        $collectorSource.Contains("Get-VerifiedArtifactFiles") -and
        $collectorSource.Contains("[IO.Directory]::Move") -and
        $collectorSource.Contains("ArtifactValidator\ArtifactValidator.csproj") -and
        $collectorSource.Contains("LeaseToken") -and
        $collectorSource.Contains("Direct classic artifact collection is disabled") -and
        -not $collectorSource.Contains("function Enter-ClassicArtifactLease") -and
        $collectorSource.Contains("Repair-ClassicArtifactPublication") -and
        $collectorSource.Contains("Assert-ClassicBuildReceipt")) `
    "The collector no longer retains exact-tree, lease, recovery, build-receipt, and validator gates."

$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
Assert-True ($plan.schemaVersion -eq 2) "Collection plan schema changed unexpectedly."
Assert-True `
    (@($plan.files).Count -eq 98) `
    "Collection plan no longer has the reviewed 98 copied inputs."
$binSources = @($plan.files | Where-Object {
    ([string]$_.source).Replace("\", "/").StartsWith(
        "bin/Release/",
        [StringComparison]::OrdinalIgnoreCase)
})
Assert-True `
    ($binSources.Count -eq 80 -and
        @($binSources.source | Sort-Object -Unique).Count -eq 79) `
    "Collection plan's reviewed 80 compiled entries or 79 source leaves drifted."
Assert-True `
    (@($plan.freshBuildOutputs).Count -eq 6) `
    "Collection plan does not declare exactly six fresh native outputs."
$cleanupLeaves = @(
    @($binSources.source) + @($plan.freshBuildOutputs) |
        ForEach-Object {
            ([string]$_).Replace("\", "/").ToLowerInvariant()
        } |
        Sort-Object -Unique)
Assert-True `
    ($cleanupLeaves.Count -eq 85) `
    "Collection plan no longer maps to the reviewed 85 fresh output leaves."
Assert-True `
    (@($plan.generatedFiles) -contains "THIRD-PARTY-NOTICES.txt") `
    "Collection plan omitted generated third-party notices."
Assert-True `
    (@($plan.generatedFiles) -contains "plugins/CUETools.PluginManifest.v1") `
    "Collection plan omitted the generated plugin trust manifest."

$destinations = @($plan.files.destination) + @($plan.generatedFiles)
$uniqueDestinations = @(
    $destinations |
        ForEach-Object { ([string]$_).Replace("\", "/").ToLowerInvariant() } |
        Sort-Object -Unique)
Assert-True `
    ($uniqueDestinations.Count -eq $destinations.Count) `
    "Collection plan has duplicate destinations."
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
$requiredPaths = @(
    $contract.requiredFiles.path |
        ForEach-Object { ([string]$_).Replace("\", "/").ToLowerInvariant() } |
        Sort-Object)
Assert-True `
    ([bool]$contract.requireExactFiles) `
    "Classic artifact contract does not require an exact file set."
Assert-True `
    (($requiredPaths -join "`n") -eq (($uniqueDestinations | Sort-Object) -join "`n")) `
    "Classic artifact contract and collection plan do not name the same exact files."
Assert-True `
    ($requiredPaths.Count -eq 100) `
    "Classic artifact contract no longer has the reviewed 100-file exact tree."
$peContracts = @($contract.requiredFiles | Where-Object {
    $_.PSObject.Properties["peMachine"] -ne $null
})
Assert-True `
    ($peContracts.Count -eq 14 -and
        @($peContracts | Where-Object {
            ([string]$_.path).StartsWith(
                "plugins/win32/",
                [StringComparison]::Ordinal) -and
            [string]$_.peMachine -ceq "x86"
        }).Count -eq 7 -and
        @($peContracts | Where-Object {
            ([string]$_.path).StartsWith(
                "plugins/x64/",
                [StringComparison]::Ordinal) -and
            [string]$_.peMachine -ceq "x64"
        }).Count -eq 7) `
    "Classic artifact contract no longer binds all 14 architecture-specific PE images."

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase (
    "cuetools-classic-collection-" + [Guid]::NewGuid().ToString("N"))
$artifactName = "CUETools_2.2.6"
$collectionId = "classic-collector-test"
$artifactDirectory = Join-Path $tempRoot $artifactName
$outsideDirectory = Join-Path $tempRoot "outside"
$junctionPath = Join-Path $artifactDirectory "linked"
New-Item -ItemType Directory -Path $tempRoot | Out-Null
New-Item -ItemType Directory -Path $outsideDirectory | Out-Null

function New-TestClassicStage(
    [string]$Root,
    [string]$Name,
    [string]$Id,
    [hashtable]$Files) {
    $stage = New-OwnedClassicArtifactStage `
        -ReleaseRoot $Root `
        -ArtifactName $Name `
        -CollectionId $Id
    foreach ($relativePath in @($Files.Keys | Sort-Object)) {
        $destination = Join-Path $stage.path $relativePath
        $parent = [IO.Path]::GetDirectoryName($destination)
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
            New-Item -ItemType Directory -Path $parent | Out-Null
        }
        [IO.File]::WriteAllText($destination, [string]$Files[$relativePath])
    }
    [void](Seal-OwnedClassicArtifactStage -Stage $stage)
    return $stage
}

$lease = $null
try {
    $copySourceRelativePath = "copy-source/source.bin"
    $copySourcePath = Join-Path $tempRoot $copySourceRelativePath
    $copySourceParent = [IO.Path]::GetDirectoryName($copySourcePath)
    $copyStage = Join-Path $tempRoot "copy-stage"
    New-Item -ItemType Directory -Path $copySourceParent | Out-Null
    New-Item -ItemType Directory -Path $copyStage | Out-Null
    [IO.File]::WriteAllText(
        $copySourcePath,
        "receipt-bound copy payload")
    $copySource = Get-ClassicBuildRegularFileRecord `
        -Path $copySourcePath `
        -Purpose "Test collection source"
    $copyRecord = [pscustomobject]([ordered]@{
        path = $copySourceRelativePath
        bytes = [long]$copySource.bytes
        sha256 = [string]$copySource.sha256
        freshBuildOutput = $false
    })
    Copy-CollectionFile `
        -RepositoryRoot $tempRoot `
        -StageDirectory $copyStage `
        -SourceRelativePath $copySourceRelativePath `
        -DestinationRelativePath "nested/copied.bin" `
        -ExpectedInputRecord $copyRecord
    Assert-True `
        ((Get-Content -LiteralPath (
            Join-Path $copyStage "nested/copied.bin") -Raw) -eq
            "receipt-bound copy payload") `
        "Receipt-bound collection copy changed the source bytes."

    $badCopyRecord = [pscustomobject]([ordered]@{
        path = $copySourceRelativePath
        bytes = [long]$copySource.bytes
        sha256 = "0" * 64
        freshBuildOutput = $false
    })
    $tamperedCopyRejected = $false
    try {
        Copy-CollectionFile `
            -RepositoryRoot $tempRoot `
            -StageDirectory $copyStage `
            -SourceRelativePath $copySourceRelativePath `
            -DestinationRelativePath "nested/rejected.bin" `
            -ExpectedInputRecord $badCopyRecord
    }
    catch {
        $tamperedCopyRejected =
            $_.Exception.Message -match "hash differs"
    }
    Assert-True `
        $tamperedCopyRejected `
        "Collection copy accepted bytes that differed from the receipt."
    Assert-True `
        (-not (Test-Path -LiteralPath (
            Join-Path $copyStage "nested/rejected.bin"))) `
        "Rejected receipt-bound collection copy retained a partial destination."

    $lease = Enter-ClassicReleaseLease `
        -ReleaseRoot $tempRoot
    $leaseToken = [string]$lease.token
    Assert-True `
        ([string]$lease.path -ceq
            (Join-Path $tempRoot ".cuetools-classic-release.lock")) `
        "Classic release lease used a product-specific lock path."

    $alternateArtifact = Join-Path $tempRoot "CUETools_9.9.9"
    Assert-ActiveClassicArtifactLease `
        -Lease $lease `
        -ArtifactDirectory $alternateArtifact `
        -LeaseToken $leaseToken
    Assert-True $true "Repo-wide lease did not cover another product version."
    $wrongLeaseTokenRejected = $false
    try {
        Assert-ActiveClassicArtifactLease `
            -Lease $lease `
            -ArtifactDirectory $artifactDirectory `
            -LeaseToken ([Guid]::NewGuid().ToString("N"))
    }
    catch {
        $wrongLeaseTokenRejected =
            $_.Exception.Message -match "exact token"
    }
    Assert-True `
        $wrongLeaseTokenRejected `
        "Classic release boundary accepted the live handle with another token."

    $contended = $false
    try {
        $secondLease = Enter-ClassicReleaseLease `
            -ReleaseRoot ([IO.Path]::GetDirectoryName($alternateArtifact)) `
            -TimeoutMilliseconds 100
        Exit-ClassicReleaseLease -Lease $secondLease
    }
    catch {
        $contended = $_.Exception -is [TimeoutException]
    }
    Assert-True `
        $contended `
        "A second product version acquired the repo-wide classic release lease."

    $firstStage = New-TestClassicStage `
        -Root $tempRoot `
        -Name $artifactName `
        -Id $collectionId `
        -Files @{ "fresh.txt" = "fresh" }
    Publish-ValidatedArtifactStage `
        -Stage $firstStage `
        -ArtifactDirectory $artifactDirectory `
        -CollectionId $collectionId `
        -SourceIdentity "test-build:first" `
        -Lease $lease `
        -LeaseToken $leaseToken
    Remove-OwnedClassicArtifactStage -Stage $firstStage
    Assert-True `
        (Test-Path -LiteralPath (
            Join-Path $artifactDirectory "fresh.txt") -PathType Leaf) `
        "Fresh staged content was not published."
    Assert-True `
        (Test-Path -LiteralPath (
            Get-ClassicArtifactControlPath `
                -ArtifactDirectory $artifactDirectory `
                -Suffix "owner.json") -PathType Leaf) `
        "Published artifact has no ownership receipt."
    [void](Assert-OwnedClassicArtifact `
        -ArtifactDirectory $artifactDirectory `
        -CollectionId $collectionId)

    $replacementStage = New-TestClassicStage `
        -Root $tempRoot `
        -Name $artifactName `
        -Id $collectionId `
        -Files @{ "replacement.txt" = "replacement" }
    Publish-ValidatedArtifactStage `
        -Stage $replacementStage `
        -ArtifactDirectory $artifactDirectory `
        -CollectionId $collectionId `
        -SourceIdentity "test-build:replacement" `
        -Lease $lease `
        -LeaseToken $leaseToken
    Remove-OwnedClassicArtifactStage -Stage $replacementStage
    Assert-True `
        (Test-Path -LiteralPath (
            Join-Path $artifactDirectory "replacement.txt") -PathType Leaf) `
        "Owned artifact replacement did not publish the new tree."
    Assert-True `
        (-not (Test-Path -LiteralPath (
            Join-Path $artifactDirectory "fresh.txt"))) `
        "Owned artifact replacement retained a stale file."

    Assert-ExactArtifactFiles `
        -ArtifactDirectory $artifactDirectory `
        -ExpectedRelativePaths @("replacement.txt")
    [IO.File]::WriteAllText(
        (Join-Path $artifactDirectory "unexpected.txt"),
        "unexpected")
    $unexpectedRejected = $false
    try {
        Assert-ExactArtifactFiles `
            -ArtifactDirectory $artifactDirectory `
            -ExpectedRelativePaths @("replacement.txt")
    }
    catch {
        $unexpectedRejected = $_.Exception.Message -match "unexpected=\[unexpected\.txt\]"
    }
    Assert-True `
        $unexpectedRejected `
        "Exact artifact validation accepted an unexpected stale file."
    Remove-Item -LiteralPath (
        Join-Path $artifactDirectory "unexpected.txt") -Force

    $unownedName = "CUETools_9.9.9"
    $unownedArtifact = Join-Path $tempRoot $unownedName
    New-Item -ItemType Directory -Path $unownedArtifact | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $unownedArtifact "foreign.txt"),
        "foreign")
    $unownedStage = New-TestClassicStage `
        -Root $tempRoot `
        -Name $unownedName `
        -Id $collectionId `
        -Files @{ "new.txt" = "new" }
    $unownedRejected = $false
    try {
        Publish-ValidatedArtifactStage `
            -Stage $unownedStage `
            -ArtifactDirectory $unownedArtifact `
            -CollectionId $collectionId `
            -SourceIdentity "test-build:unowned" `
            -Lease $lease `
            -LeaseToken $leaseToken
    }
    catch {
        $unownedRejected =
            $_.Exception.Message -match "unowned classic artifact"
    }
    Assert-True `
        $unownedRejected `
        "Collector replaced an arbitrary destination without its owner receipt."
    Assert-True `
        ((Get-Content -LiteralPath (
            Join-Path $unownedArtifact "foreign.txt") -Raw) -eq "foreign") `
        "Unowned-target refusal changed the foreign destination."
    Remove-OwnedClassicArtifactStage -Stage $unownedStage

    $rollbackStage = New-TestClassicStage `
        -Root $tempRoot `
        -Name $artifactName `
        -Id $collectionId `
        -Files @{ "rollback.txt" = "must-not-publish" }
    $rollbackInjected = $false
    try {
        Publish-ValidatedArtifactStage `
            -Stage $rollbackStage `
            -ArtifactDirectory $artifactDirectory `
            -CollectionId $collectionId `
            -SourceIdentity "test-build:rollback" `
            -Lease $lease `
            -LeaseToken $leaseToken `
            -InjectFailureAt "AfterPriorMove"
    }
    catch {
        $rollbackInjected =
            $_.Exception.Message -match "Injected failure"
    }
    Assert-True `
        $rollbackInjected `
        "Injected prior-move failure did not surface."
    Assert-True `
        (Test-Path -LiteralPath (
            Join-Path $artifactDirectory "replacement.txt") -PathType Leaf) `
        "Proven rollback did not restore the prior artifact."
    Assert-True `
        (-not (Test-Path -LiteralPath (
            Get-ClassicPublicationJournal `
                -ArtifactDirectory $artifactDirectory))) `
        "Proven rollback retained a stale recovery journal."
    Remove-OwnedClassicArtifactStage -Stage $rollbackStage

    $recoveryStage = New-TestClassicStage `
        -Root $tempRoot `
        -Name $artifactName `
        -Id $collectionId `
        -Files @{ "recovery.txt" = "not-committed" }
    $rollbackFailureRetained = $false
    try {
        Publish-ValidatedArtifactStage `
            -Stage $recoveryStage `
            -ArtifactDirectory $artifactDirectory `
            -CollectionId $collectionId `
            -SourceIdentity "test-build:recovery" `
            -Lease $lease `
            -LeaseToken $leaseToken `
            -InjectFailureAt "RollbackFailure"
    }
    catch {
        $rollbackFailureRetained =
            $_.Exception.Message -match "journal were retained"
    }
    Assert-True `
        $rollbackFailureRetained `
        "Rollback failure did not provide an explicit retained-state recovery instruction."
    Assert-True `
        (-not (Test-Path -LiteralPath $artifactDirectory)) `
        "Injected rollback failure unexpectedly republished a destination."
    Assert-True `
        (Test-Path -LiteralPath (
            Get-ClassicPublicationJournal `
                -ArtifactDirectory $artifactDirectory) -PathType Leaf) `
        "Rollback failure did not retain its recovery journal."
    $rollbackRecovery = Repair-ClassicArtifactPublication `
        -ArtifactDirectory $artifactDirectory `
        -CollectionId $collectionId `
        -Lease $lease `
        -LeaseToken $leaseToken
    Assert-True `
        ($rollbackRecovery -eq "RestoredPriorBackup") `
        "Startup recovery did not restore the token-bound prior backup."
    Assert-True `
        (Test-Path -LiteralPath (
            Join-Path $artifactDirectory "replacement.txt") -PathType Leaf) `
        "Startup rollback recovery restored the wrong artifact tree."

    $forwardStage = New-TestClassicStage `
        -Root $tempRoot `
        -Name $artifactName `
        -Id $collectionId `
        -Files @{ "forward.txt" = "commit-on-restart" }
    $forwardFailureRetained = $false
    try {
        Publish-ValidatedArtifactStage `
            -Stage $forwardStage `
            -ArtifactDirectory $artifactDirectory `
            -CollectionId $collectionId `
            -SourceIdentity "test-build:forward" `
            -Lease $lease `
            -LeaseToken $leaseToken `
            -InjectFailureAt "RollbackFailureAfterStagePublish"
    }
    catch {
        $forwardFailureRetained =
            $_.Exception.Message -match "journal were retained"
    }
    Assert-True `
        $forwardFailureRetained `
        "Published-stage rollback failure did not retain recovery state."
    $forwardRecovery = Repair-ClassicArtifactPublication `
        -ArtifactDirectory $artifactDirectory `
        -CollectionId $collectionId `
        -Lease $lease `
        -LeaseToken $leaseToken
    Assert-True `
        ($forwardRecovery -eq "CommittedPublishedStage") `
        "Startup recovery did not commit the exact published stage."
    Assert-True `
        (Test-Path -LiteralPath (
            Join-Path $artifactDirectory "forward.txt") -PathType Leaf) `
        "Forward recovery did not retain the validated published tree."
    Assert-True `
        (@(Get-ChildItem -LiteralPath $tempRoot -Force |
            Where-Object {
                $_.Name -like ".$artifactName.backup-*"
            }).Count -eq 0) `
        "Recovery left a token-bound backup after destination revalidation."

    $tokenStage = New-TestClassicStage `
        -Root $tempRoot `
        -Name $artifactName `
        -Id $collectionId `
        -Files @{ "token.txt" = "token" }
    $wrongTokenStage = [pscustomobject]@{
        path = $tokenStage.path
        receiptPath = $tokenStage.receiptPath
        token = [Guid]::NewGuid().ToString("N")
        collectionId = $tokenStage.collectionId
        artifactName = $tokenStage.artifactName
    }
    $wrongTokenRejected = $false
    try {
        Remove-OwnedClassicArtifactStage -Stage $wrongTokenStage
    }
    catch {
        $wrongTokenRejected =
            $_.Exception.Message -match "exact stage token"
    }
    Assert-True `
        $wrongTokenRejected `
        "Stage cleanup accepted a prefix match without the exact owner token."
    Assert-True `
        (Test-Path -LiteralPath $tokenStage.path -PathType Container) `
        "Wrong-token cleanup removed the owned stage."
    Remove-OwnedClassicArtifactStage -Stage $tokenStage

    New-Item -ItemType Junction -Path $junctionPath -Target $outsideDirectory |
        Out-Null
    $reparseStage = New-TestClassicStage `
        -Root $tempRoot `
        -Name $artifactName `
        -Id $collectionId `
        -Files @{ "second.txt" = "second" }
    $reparseRejected = $false
    try {
        Publish-ValidatedArtifactStage `
            -Stage $reparseStage `
            -ArtifactDirectory $artifactDirectory `
            -CollectionId $collectionId `
            -SourceIdentity "test-build:reparse" `
            -Lease $lease `
            -LeaseToken $leaseToken
    }
    catch {
        $reparseRejected = $_.Exception.Message -match "reparse point"
    }
    Assert-True `
        $reparseRejected `
        "Publication accepted an existing artifact tree containing a reparse point."
    Assert-True `
        (Test-Path -LiteralPath (
            Join-Path $artifactDirectory "forward.txt") -PathType Leaf) `
        "Rejected publication damaged the prior artifact."
    Remove-OwnedClassicArtifactStage -Stage $reparseStage
}
finally {
    if (Test-Path -LiteralPath $junctionPath) {
        Remove-Item -LiteralPath $junctionPath -Force
    }
    if ($lease -ne $null) {
        Exit-ClassicReleaseLease -Lease $lease
    }
    $tempPrefix = $tempBase.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $tempLeaf = [IO.Path]::GetFileName($tempRoot)
    if (-not $tempRoot.StartsWith(
            $tempPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not $tempLeaf.StartsWith(
            "cuetools-classic-collection-",
            [StringComparison]::Ordinal)) {
        throw "Refusing to clean unexpected collection-test path: $tempRoot"
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

Write-Host "Classic artifact collection checks passed: $script:checkCount"
