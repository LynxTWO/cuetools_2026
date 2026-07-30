[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$safetyScript = Join-Path $PSScriptRoot "ReleaseSafety.ps1"
. $safetyScript
$nativeInventoryScript = Join-Path $PSScriptRoot "NativeDependencyInventory.ps1"
. $nativeInventoryScript

$script:checkCount = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checkCount++
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if (-not [object]::Equals($Expected, $Actual)) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
    $script:checkCount++
}

function Assert-Null($Actual, [string]$Message) {
    if ($null -ne $Actual) {
        throw "$Message Expected null, got '$Actual'."
    }
    $script:checkCount++
}

function Assert-Throws([scriptblock]$Action, [string]$MessagePattern, [string]$Message) {
    $exceptionMessage = $null
    try {
        & $Action
    }
    catch {
        $exceptionMessage = $_.Exception.Message
    }
    if ($null -eq $exceptionMessage) {
        throw "$Message Expected an exception."
    }
    if ($exceptionMessage -notmatch $MessagePattern) {
        throw "$Message Unexpected exception: $exceptionMessage"
    }
    $script:checkCount++
}

$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = [IO.Path]::Combine(
    $tempBase,
    "cuetools-release-safety-" + [Guid]::NewGuid().ToString("N"))
$reparseEntries = New-Object "Collections.Generic.List[string]"
New-Item -ItemType Directory -Path $tempRoot | Out-Null

try {
    $safeRoot = Join-Path $tempRoot "safe"
    $safeChild = Join-Path $safeRoot "child"
    New-Item -ItemType Directory -Path $safeChild | Out-Null
    Assert-NoReparsePointInExistingPath -Path $safeChild -Purpose "Safe test path"
    $script:checkCount++

    $outside = Join-Path $tempRoot "outside"
    New-Item -ItemType Directory -Path $outside | Out-Null
    [IO.File]::WriteAllText((Join-Path $outside "sentinel.txt"), "outside")

    $junction = Join-Path $tempRoot "junction"
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    $reparseEntries.Add($junction)
    Assert-Throws `
        { Assert-NoReparsePointInExistingPath -Path $junction -Purpose "Target test path" } `
        "reparse point" `
        "A target junction was accepted."
    Assert-Throws `
        { Assert-NoReparsePointInExistingPath -Path (Join-Path $junction "child") -Purpose "Parent test path" } `
        "reparse point" `
        "A junction in an existing parent component was accepted."

    $outsideReceipt = Join-Path $outside "receipt.json"
    [IO.File]::WriteAllText($outsideReceipt, "outside-receipt")
    $receiptLink = Join-Path $tempRoot "receipt.json"
    try {
        New-Item `
            -ItemType SymbolicLink `
            -Path $receiptLink `
            -Target $outsideReceipt `
            -ErrorAction Stop | Out-Null
    }
    catch {
        # Creating file symlinks requires an elevated token when Windows Developer
        # Mode is unavailable. A leaf junction exercises the same ReparsePoint
        # attribute gate without weakening the test on those hosts.
        New-Item -ItemType Junction -Path $receiptLink -Target $outside | Out-Null
    }
    $reparseEntries.Add($receiptLink)
    Assert-Throws `
        { Assert-NoReparsePointInExistingPath -Path $receiptLink -Purpose "Build receipt output" } `
        "reparse point" `
        "An existing receipt leaf reparse point was accepted."

    Assert-True `
        (Test-SameOrDescendantPath -CandidatePath $safeRoot -RootPath $safeRoot) `
        "Path equality was not recognized."
    Assert-True `
        (Test-SameOrDescendantPath -CandidatePath $safeChild -RootPath $safeRoot) `
        "A descendant path was not recognized."
    Assert-True `
        (-not (Test-SameOrDescendantPath `
            -CandidatePath (Join-Path $tempRoot "safe-sibling") `
            -RootPath $safeRoot)) `
        "A prefix sibling was incorrectly recognized as a descendant."

    foreach ($validName in @("CUETools_2.2.6", "CUETools2026-win-x64")) {
        Assert-SafeArtifactName -Name $validName
        $script:checkCount++
    }
    foreach ($invalidName in @(
        "..",
        "../escape",
        "bad/name",
        ".hidden",
        "name.",
        "name with space",
        "NUL",
        ("x" * 129)
    )) {
        Assert-Throws `
            { Assert-SafeArtifactName -Name $invalidName } `
            "simple safe filename" `
            "Unsafe artifact name '$invalidName' was accepted."
    }

    Assert-Null `
        (Get-GeneratedUntrackedClassification "src/libFLAC/libFLAC_dynamic.vcxproj") `
        "A native project source input was classified as generated."
    Assert-Null `
        (Get-GeneratedUntrackedClassification "src/libFLAC/libFLAC_dynamic.vcxproj.filters") `
        "A native project filter source input was classified as generated."
    Assert-Null `
        (Get-GeneratedUntrackedClassification "src/libFLAC/stream_decoder.cpp") `
        "A C++ source input was classified as generated."
    Assert-Equal `
        "native-object" `
        (Get-GeneratedUntrackedClassification "src/libFLAC/x64/Release_dynamic/decode.obj") `
        "An object file was not classified."
    Assert-Equal `
        "native-build-trace" `
        (Get-GeneratedUntrackedClassification "src/libFLAC/x64/Release_dynamic/build.tlog") `
        "A native build trace was not classified."
    Assert-Equal `
        "native-build-log" `
        (Get-GeneratedUntrackedClassification "src/libFLAC/x64/Release_dynamic/libFLAC_dynamic.vcxproj.FileListAbsolute.txt") `
        "A native absolute file list was not classified."
    Assert-Equal `
        "clean" `
        (Get-ProvenanceWorkspaceState `
            -PatchId $null `
            -UntrackedSourceCount 0 `
            -ClassifiedGeneratedFileCount 6 `
            -NestedWorkspacesClean $true) `
        "Classified build residue incorrectly dirtied the source-state verdict."
    Assert-Equal `
        "patched-or-untracked" `
        (Get-ProvenanceWorkspaceState `
            -PatchId $null `
            -UntrackedSourceCount 1 `
            -ClassifiedGeneratedFileCount 0 `
            -NestedWorkspacesClean $true) `
        "Unknown untracked source was accepted as a clean workspace."
    Assert-Equal `
        "patched-or-untracked" `
        (Get-ProvenanceWorkspaceState `
            -PatchId "fixture-patch" `
            -UntrackedSourceCount 0 `
            -ClassifiedGeneratedFileCount 0 `
            -NestedWorkspacesClean $true) `
        "A tracked source patch was accepted as a clean workspace."

    $artifact = Join-Path $tempRoot "artifact"
    $artifactChild = Join-Path $artifact "content"
    New-Item -ItemType Directory -Path $artifactChild | Out-Null
    [IO.File]::WriteAllText((Join-Path $artifact "root.txt"), "root")
    [IO.File]::WriteAllText((Join-Path $artifactChild "child.txt"), "child")
    $verifiedFiles = @(Get-VerifiedArtifactFiles -Root $artifact)
    Assert-Equal 2 $verifiedFiles.Count "The safe artifact walk returned the wrong file count."

    $artifactJunction = Join-Path $artifact "linked-content"
    New-Item -ItemType Junction -Path $artifactJunction -Target $outside | Out-Null
    $reparseEntries.Add($artifactJunction)
    Assert-Throws `
        { Get-VerifiedArtifactFiles -Root $artifact } `
        "reparse point" `
        "An artifact child junction was accepted."
    Assert-Throws `
        { Get-VerifiedArtifactFiles -Root $junction } `
        "reparse point" `
        "An artifact root junction was accepted."

    $publishSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot "Publish-Wpf.ps1") -Raw
    $publishGuardIndex = $publishSource.IndexOf(
        "Assert-NoReparsePointInExistingPath",
        [StringComparison]::Ordinal)
    $publishRemoveIndex = $publishSource.IndexOf(
        "Remove-Item -LiteralPath `$ArtifactDirectory -Recurse -Force",
        [StringComparison]::Ordinal)
    $publishTreeGuardIndex = $publishSource.IndexOf(
        "Get-VerifiedArtifactFiles -Root `$ArtifactDirectory",
        [StringComparison]::Ordinal)
    Assert-True `
        ($publishGuardIndex -ge 0 -and
            $publishTreeGuardIndex -ge 0 -and
            $publishRemoveIndex -ge 0 -and
            $publishGuardIndex -lt $publishRemoveIndex -and
            $publishTreeGuardIndex -lt $publishRemoveIndex) `
        "Publish-Wpf.ps1 does not guard the full artifact tree before recursive cleanup."

    $noticesSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot "New-ThirdPartyNotices.ps1") -Raw
    Assert-True `
        ($noticesSource.Contains("Test-SameOrDescendantPath") -and
            $noticesSource.Contains("Assert-NoReparsePointInExistingPath")) `
        "New-ThirdPartyNotices.ps1 does not constrain and inspect its output path."
    $pluginInstallerSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot "Install-CUEToolsPlugin.ps1") -Raw
    Assert-True `
        ($pluginInstallerSource.Contains("Assert-TreeHasNoReparsePoints") -and
            $pluginInstallerSource.Contains(".plugins-stage-") -and
            $pluginInstallerSource.Contains("plugins-backup-") -and
            $pluginInstallerSource.Contains("[IO.Directory]::Move")) `
        "Install-CUEToolsPlugin.ps1 does not retain the staged, reparse-refusing publication contract."
    Assert-True `
        ($publishSource.Contains("Install-CUEToolsPlugin.ps1")) `
        "Publish-Wpf.ps1 does not copy the supported user plugin installer."
    $externalPrepareIndex = $publishSource.IndexOf(
        "Prepare-ExternalCommandEncoders.ps1",
        [StringComparison]::Ordinal)
    $dotnetPublishIndex = $publishSource.IndexOf(
        "& dotnet publish",
        [StringComparison]::Ordinal)
    Assert-True `
        ($externalPrepareIndex -ge 0 -and
            $dotnetPublishIndex -ge 0 -and
            $externalPrepareIndex -lt $dotnetPublishIndex) `
        "Publish-Wpf.ps1 does not prepare hash-pinned command encoders before publishing."
    Assert-True `
        ($publishSource.Contains(
                "-p:NuGetLockFilePath=obj/release-win-x64/packages.lock.json") -and
            $publishSource.Contains("-p:RestoreLockedMode=false")) `
        "Publish-Wpf.ps1 can rewrite committed lock files with RID-only restore graphs."
    $classicCollectionSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot "..\..\collect_files.bat") -Raw
    Assert-True `
        ($classicCollectionSource.Contains("Invoke-ClassicRelease.ps1") -and
            -not $classicCollectionSource.Contains(
                "Collect-ClassicArtifacts.ps1") -and
            $classicCollectionSource.IndexOf(
                "xcopy",
                [StringComparison]::OrdinalIgnoreCase) -lt 0) `
        "collect_files.bat does not exclusively delegate to the classic release orchestrator."
    $classicCollectionPlan = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot "classic-win.collection.json") -Raw |
        ConvertFrom-Json
    Assert-True `
        (@($classicCollectionPlan.files |
            Where-Object {
                $_.source -eq "eng/release/Install-CUEToolsPlugin.ps1" -and
                $_.destination -eq "Install-CUEToolsPlugin.ps1"
            }).Count -eq 1) `
        "The classic collection plan does not copy the supported user plugin installer."
    foreach ($contractName in @(
        "wpf-win-x64.manifest.json",
        "classic-win.manifest.json")) {
        $contract = Get-Content -LiteralPath (
            Join-Path $PSScriptRoot $contractName) -Raw | ConvertFrom-Json
        Assert-True `
            (@($contract.requiredFiles.path) -contains "THIRD-PARTY-NOTICES.txt") `
            "$contractName does not require the generated third-party notices."
        Assert-True `
            (@($contract.requiredFiles.path) -contains "Install-CUEToolsPlugin.ps1") `
            "$contractName does not require the supported user plugin installer."
    }
    $wpfContract = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot "wpf-win-x64.manifest.json") -Raw |
        ConvertFrom-Json
    $externalEncoderContract = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot "external-command-encoders.json") -Raw |
        ConvertFrom-Json
    $encoderCatalogSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot "..\..\CUETools.Wpf\Services\EncoderCatalog.cs") -Raw
    $wpfProjectSource = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot "..\..\CUETools.Wpf\CUETools.Wpf.csproj") -Raw
    $gitAttributeLines = @(
        Get-Content -LiteralPath (
            Join-Path $PSScriptRoot "..\..\.gitattributes") |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_.Length -gt 0 -and -not $_.StartsWith("#") })
    $nativeInventory = Get-Content -LiteralPath (
        Join-Path $PSScriptRoot "native-dependencies.json") -Raw |
        ConvertFrom-Json
    $macSdkClosure = Get-CUEToolsMacSdkSourceClosure `
        -RepositoryRoot (Join-Path $PSScriptRoot "..\..") `
        -Inventory $nativeInventory
    Assert-True `
        ([string]$macSdkClosure.summary.classification -ceq
            "pinned-native-source-expansion" -and
            [string]$macSdkClosure.summary.state -ceq "validated" -and
            [int]$macSdkClosure.summary.archiveFileCount -eq 423 -and
            [int]$macSdkClosure.summary.overrideFileCount -eq 4 -and
            [string]$macSdkClosure.summary.expandedTreeSha256 -cmatch
                "^[0-9a-f]{64}$" -and
            @($macSdkClosure.archiveMembers).Count -eq 423) `
        "The pinned Monkey's Audio SDK derived-source closure is incomplete."
    foreach ($pinnedFile in @($nativeInventory.pinnedFiles)) {
        $attributePath = ([string]$pinnedFile.path).Replace("\", "/")
        Assert-True `
            ($gitAttributeLines -ccontains "$attributePath binary" -or
                $gitAttributeLines -ccontains "$attributePath -text") `
            "Hash-bound native input lacks an exact checkout-byte contract: $attributePath"
        $inputPath = Join-Path $PSScriptRoot (
            "..\..\" + ([string]$pinnedFile.path).Replace("/", "\"))
        Assert-True `
            ((Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash -ieq
                [string]$pinnedFile.sha256) `
            "Hash-bound native input drifted from its inventory: $attributePath"
    }
    $externalPaths = New-Object "Collections.Generic.List[string]"
    foreach ($encoder in @($externalEncoderContract.encoders)) {
        $externalPaths.Add([string]$encoder.packagePath)
        if (-not [string]::IsNullOrWhiteSpace(
                [string]$encoder.sourceArchive.packagePath)) {
            $externalPaths.Add([string]$encoder.sourceArchive.packagePath)
        }
        $additionalSourceArchivesProperty =
            $encoder.PSObject.Properties["additionalSourceArchives"]
        if ($null -ne $additionalSourceArchivesProperty) {
            foreach ($additionalSource in @(
                    $additionalSourceArchivesProperty.Value)) {
                $externalPaths.Add([string]$additionalSource.packagePath)
            }
        }
        $sourceSupportProperty =
            $encoder.PSObject.Properties["sourceSupport"]
        if ($null -ne $sourceSupportProperty) {
            foreach ($support in @($sourceSupportProperty.Value)) {
                $externalPaths.Add([string]$support.packagePath)
                $expectedAttribute =
                    ([string]$support.path).Replace("\", "/") + " text eol=lf"
                Assert-True `
                    ($gitAttributeLines -ccontains $expectedAttribute) `
                    "Hash-bound source support lacks an exact LF checkout contract: $($support.path)"
            }
        }
    }
    foreach ($externalPath in $externalPaths) {
        $externalEntries = @(
            $wpfContract.requiredFiles |
                Where-Object { $_.path -ceq $externalPath })
        Assert-True `
            ($externalEntries.Count -eq 1 -and
                -not [string]::IsNullOrWhiteSpace(
                    [string]$externalEntries[0].sha256)) `
            "The WPF artifact does not hash-bind $externalPath."
        $externalLeaf = [IO.Path]::GetFileName($externalPath)
        $expectedProjectPath = if (
            $externalPath.StartsWith(
                "encoders/source/",
                [StringComparison]::Ordinal)) {
            "..\ThirdParty\encoders\source\$externalLeaf"
        }
        else {
            "..\ThirdParty\encoders\x64\$externalLeaf"
        }
        Assert-True `
            ($wpfProjectSource.Contains($expectedProjectPath)) `
            "The WPF project does not publish $externalPath."
    }
    foreach ($encoder in @($externalEncoderContract.encoders)) {
        $artifactEntry = @(
            $wpfContract.requiredFiles |
                Where-Object { $_.path -ceq $encoder.packagePath })
        Assert-True `
            ($artifactEntry.Count -eq 1 -and
                [string]$artifactEntry[0].sha256 -ieq
                    [string]$encoder.executableSha256) `
            "The WPF artifact hash for $($encoder.packagePath) drifted from the external encoder contract."
        Assert-True `
            ($encoderCatalogSource.IndexOf(
                    [string]$encoder.executableSha256,
                    [StringComparison]::OrdinalIgnoreCase) -ge 0) `
            "The runtime catalog does not pin $($encoder.packagePath)."

        if (-not [string]::IsNullOrWhiteSpace(
                [string]$encoder.sourceArchive.packagePath)) {
            $sourceEntry = @(
                $wpfContract.requiredFiles |
                    Where-Object {
                        $_.path -ceq $encoder.sourceArchive.packagePath
                    })
            Assert-True `
                ($sourceEntry.Count -eq 1 -and
                    [string]$sourceEntry[0].sha256 -ieq
                        [string]$encoder.sourceArchive.sha256) `
                "The packaged source hash for $($encoder.id) drifted from the external encoder contract."
        }
        $additionalSourceArchivesProperty =
            $encoder.PSObject.Properties["additionalSourceArchives"]
        if ($null -ne $additionalSourceArchivesProperty) {
            foreach ($additionalSource in @(
                    $additionalSourceArchivesProperty.Value)) {
                $additionalSourceEntry = @(
                    $wpfContract.requiredFiles |
                        Where-Object {
                            $_.path -ceq $additionalSource.packagePath
                        })
                Assert-True `
                    ($additionalSourceEntry.Count -eq 1 -and
                        [string]$additionalSourceEntry[0].sha256 -ieq
                            [string]$additionalSource.sha256) `
                    "Additional packaged source for $($encoder.id) drifted from the external encoder contract."
            }
        }
        $sourceSupportProperty =
            $encoder.PSObject.Properties["sourceSupport"]
        if ($null -ne $sourceSupportProperty) {
            foreach ($support in @($sourceSupportProperty.Value)) {
                $supportEntry = @(
                    $wpfContract.requiredFiles |
                        Where-Object {
                            $_.path -ceq $support.packagePath
                        })
                Assert-True `
                    ($supportEntry.Count -eq 1 -and
                        [string]$supportEntry[0].sha256 -ieq
                            [string]$support.sha256) `
                    "Packaged build support for $($encoder.id) drifted from the external encoder contract."
            }
        }
    }

    & (Join-Path $PSScriptRoot "Test-Install-CUEToolsPlugin.ps1")
    & (Join-Path $PSScriptRoot "Test-ClassicArtifactCollection.ps1")
    & (Join-Path $PSScriptRoot "Test-ArtifactValidatorExactFiles.ps1")
    & (Join-Path $PSScriptRoot "..\ci\Test-NuGetLockFiles.ps1")
    & (Join-Path $PSScriptRoot "..\ci\Test-ResxDuplicateNames.ps1")
    & (Join-Path $PSScriptRoot "..\ci\Test-WorkflowActionPins.ps1")
    & (Join-Path $PSScriptRoot "..\ci\Test-FFmpegWorkflow.ps1")
    & (Join-Path $PSScriptRoot "Test-SigningPolicy.ps1")
    & dotnet run `
        --project (Join-Path $PSScriptRoot "SbomGuard\SbomGuard.csproj") `
        --configuration Release `
        -- `
        self-test
    if ($LASTEXITCODE -ne 0) {
        throw "SBOM guard self-test failed."
    }
    $script:checkCount++

    $provenanceSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot "New-Provenance.ps1") -Raw
    Assert-True `
        ($provenanceSource.Contains("Test-SameOrDescendantPath")) `
        "New-Provenance.ps1 does not reject equal and descendant output paths."
    Assert-True `
        ($provenanceSource.Contains("Get-VerifiedArtifactFiles")) `
        "New-Provenance.ps1 does not use refusal-based artifact traversal."
    Assert-True `
        ($provenanceSource.Contains("Get-GeneratedUntrackedClassification")) `
        "New-Provenance.ps1 does not classify generated native outputs."
    $receiptGuardIndex = $provenanceSource.IndexOf(
        '-Purpose "Build receipt output"',
        [StringComparison]::Ordinal)
    $receiptWriteIndex = $provenanceSource.IndexOf(
        "[IO.File]::WriteAllText(`r`n    `$receiptPath",
        [StringComparison]::Ordinal)
    if ($receiptWriteIndex -lt 0) {
        $receiptWriteIndex = $provenanceSource.IndexOf(
            "[IO.File]::WriteAllText(`n    `$receiptPath",
            [StringComparison]::Ordinal)
    }
    Assert-True `
        ($receiptGuardIndex -ge 0 -and
            $receiptWriteIndex -ge 0 -and
            $receiptGuardIndex -lt $receiptWriteIndex) `
        "New-Provenance.ps1 does not reject an existing receipt reparse point before writing."

    Assert-True `
        (Test-Path -LiteralPath (Join-Path $outside "sentinel.txt") -PathType Leaf) `
        "The junction target was modified during refusal testing."

    Write-Host "Release safety checks passed: $script:checkCount"
}
finally {
    foreach ($reparsePath in $reparseEntries) {
        if (Test-Path -LiteralPath $reparsePath) {
            $reparseInfo = Get-Item -LiteralPath $reparsePath -Force
            if (($reparseInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -or
                -not $reparsePath.StartsWith(
                    $tempRoot.TrimEnd(
                        [IO.Path]::DirectorySeparatorChar,
                        [IO.Path]::AltDirectorySeparatorChar) +
                        [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to remove an unexpected test reparse point: $reparsePath"
            }
            # Windows PowerShell 5.1 can throw a NullReferenceException when Remove-Item
            # removes a directory junction. Directory.Delete removes the junction itself,
            # not its target, after the checks above constrain the exact entry.
            if ($reparseInfo.PSIsContainer) {
                [IO.Directory]::Delete($reparsePath)
            }
            else {
                [IO.File]::Delete($reparsePath)
            }
        }
    }

    $tempPrefix = $tempBase.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $tempLeaf = [IO.Path]::GetFileName($tempRoot)
    if (-not $tempRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not $tempLeaf.StartsWith(
            "cuetools-release-safety-",
            [StringComparison]::Ordinal)) {
        throw "Refusing to clean an unexpected test path: $tempRoot"
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
