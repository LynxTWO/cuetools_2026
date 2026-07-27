[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$PlanPath,
    [string]$ReceiptPath,
    [string]$DevenvPath,
    [string]$MSBuildPath,
    [int]$LeaseTimeoutMilliseconds = 30000,
    [switch]$ArchiveStalePendingIntent
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

. (Join-Path $PSScriptRoot "Collect-ClassicArtifacts.ps1")

function Remove-ClassicFreshBuildOutputLeaves {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [object]$Plan
    )

    $removed = 0
    foreach ($relativePath in @($Plan.requiredAbsentAtBegin)) {
        $fullPath = Resolve-ClassicRepositoryPath `
            -RepositoryRoot $RepositoryRoot `
            -RelativePath ([string]$relativePath) `
            -Purpose "Classic fresh build output"
        if (-not (Test-Path -LiteralPath $fullPath)) { continue }
        Assert-NoReparsePointInExistingPath `
            -Path $fullPath `
            -Purpose "Classic fresh build output"
        $item = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
        if ($item.PSIsContainer -or
            -not ($item -is [IO.FileInfo]) -or
            ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Fresh build cleanup only removes declared regular-file leaves: $fullPath"
        }
        [IO.File]::Delete($fullPath)
        if (Test-Path -LiteralPath $fullPath) {
            throw "Fresh build output remained after exact-leaf cleanup: $fullPath"
        }
        $removed++
    }
    return $removed
}

function Invoke-ClassicCanonicalBuildCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [object]$Toolchain,
        [Parameter(Mandatory = $true)]
        [object]$Command,
        [string]$TestToolRoot,
        [scriptblock]$TestCommandInvoker
    )

    $toolPath = Resolve-ClassicBuildToolPath `
        -Toolchain $Toolchain `
        -Role ([string]$Command.toolRole) `
        -TestToolRoot $TestToolRoot
    $logPath = Resolve-ClassicRepositoryPath `
        -RepositoryRoot $RepositoryRoot `
        -RelativePath ([string]$Command.logPath) `
        -Purpose "Classic build command log"
    $logParent = [IO.Path]::GetDirectoryName($logPath)
    if (-not (Test-Path -LiteralPath $logParent -PathType Container)) {
        New-Item -ItemType Directory -Path $logParent | Out-Null
    }
    Assert-NoReparsePointInExistingPath `
        -Path $logParent `
        -Purpose "Classic build command log directory"
    if (Test-Path -LiteralPath $logPath) {
        throw "Classic build command log already exists: $logPath"
    }

    $startedAt = Get-ClassicUtcTimestamp
    $exitCode = -1
    if ($TestCommandInvoker -ne $null) {
        $exitCode = [int](& $TestCommandInvoker `
            $Command `
            $toolPath `
            $logPath `
            $RepositoryRoot)
    }
    else {
        $arguments = [string[]]@($Command.arguments)
        $oldErrorAction = $ErrorActionPreference
        Push-Location -LiteralPath $RepositoryRoot
        try {
            # The call operator receives the canonical argument vector directly.
            # No command preview is reparsed as a shell string.
            $ErrorActionPreference = "Continue"
            & $toolPath @arguments *> $logPath
            $exitCode = [int]$LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $oldErrorAction
            Pop-Location
        }
    }
    $completedAt = Get-ClassicUtcTimestamp
    $log = Get-ClassicBuildRegularFileRecord `
        -Path $logPath `
        -Purpose "Classic build command log"

    $record = [ordered]@{
        sequence = [int]$Command.sequence
        role = [string]$Command.role
    }
    if ([string]$Command.role -ceq "rebuild") {
        $record.tuple = [string]$Command.tuple
    }
    $record.toolRole = [string]$Command.toolRole
    $record.arguments = [string[]]@($Command.arguments)
    $record.startedAtUtc = $startedAt
    $record.completedAtUtc = $completedAt
    $record.exitCode = $exitCode
    $record.log = [pscustomobject]([ordered]@{
        path = [string]$Command.logPath
        bytes = [long]$log.bytes
        sha256 = [string]$log.sha256
    })
    return [pscustomobject]$record
}

function Invoke-ClassicRelease {
    [CmdletBinding()]
    param(
        [string]$RepositoryRoot,
        [string]$PlanPath,
        [string]$ReceiptPath,
        [string]$DevenvPath,
        [string]$MSBuildPath,
        [int]$LeaseTimeoutMilliseconds = 30000,
        [switch]$ArchiveStalePendingIntent,
        [object]$TestToolchain,
        [string]$TestToolRoot,
        [scriptblock]$TestCommandInvoker,
        [scriptblock]$TestCollectionInvoker
    )

    if ($LeaseTimeoutMilliseconds -lt 1) {
        throw "Classic release lease timeout must be positive."
    }
    if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        $RepositoryRoot = [IO.Path]::GetFullPath(
            (Join-Path $PSScriptRoot "..\.."))
    }
    else {
        $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
    }
    if ([string]::IsNullOrWhiteSpace($PlanPath)) {
        $PlanPath = Join-Path $PSScriptRoot "classic-win.collection.json"
    }
    if ([string]::IsNullOrWhiteSpace($ReceiptPath)) {
        $ReceiptPath = Join-Path $RepositoryRoot (
            "bin\Release\evidence\classic-build-inputs.v2.json")
    }
    $PlanPath = [IO.Path]::GetFullPath($PlanPath)
    $ReceiptPath = [IO.Path]::GetFullPath($ReceiptPath)

    $plan = Get-ClassicCollectionPlan `
        -RepositoryRoot $RepositoryRoot `
        -PlanPath $PlanPath
    $releaseRoot = Join-Path $RepositoryRoot "bin\Release"
    if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container)) {
        New-Item -ItemType Directory -Path $releaseRoot | Out-Null
    }
    Assert-NoReparsePointInExistingPath `
        -Path $releaseRoot `
        -Purpose "Classic release root"
    $artifactName = "CUETools_" + [string]$plan.value.productVersion
    Assert-SafeArtifactName -Name $artifactName
    $artifactDirectory = Join-Path $releaseRoot $artifactName

    $lease = $null
    try {
        # This one lease spans restart repair, cleanup, intent, every build
        # command, receipt completion, collection, and publication.
        $lease = Enter-ClassicReleaseLease `
            -ReleaseRoot $releaseRoot `
            -TimeoutMilliseconds $LeaseTimeoutMilliseconds
        $leaseToken = [string]$lease.token
        $vendorPreparationPath = Join-Path $RepositoryRoot (
            "eng\ci\Prepare-VendorSources.ps1")
        if (Test-Path -LiteralPath $vendorPreparationPath -PathType Leaf) {
            [void](Initialize-CUEToolsVendorSources `
                -RepositoryRoot $RepositoryRoot)
        }
        [void](Archive-ValidatedPendingClassicBuildIntent `
            -RepositoryRoot $RepositoryRoot `
            -PlanPath $PlanPath `
            -ReceiptPath $ReceiptPath `
            -Configuration "Release" `
            -Platforms @("Any CPU", "x64", "Win32") `
            -Lease $lease `
            -LeaseToken $leaseToken `
            -AllowStaleSource:$ArchiveStalePendingIntent `
            -TestToolRoot $TestToolRoot)
        [void](Repair-ClassicArtifactPublication `
            -ArtifactDirectory $artifactDirectory `
            -CollectionId ([string]$plan.value.collectionId) `
            -Lease $lease `
            -LeaseToken $leaseToken)

        $removed = Remove-ClassicFreshBuildOutputLeaves `
            -RepositoryRoot $RepositoryRoot `
            -Plan $plan
        Write-Host "Removed $removed declared classic build output leaves."

        $startArguments = @{
            RepositoryRoot = $RepositoryRoot
            PlanPath = $PlanPath
            ReceiptPath = $ReceiptPath
            Configuration = "Release"
            Platforms = @("Any CPU", "x64", "Win32")
            DevenvPath = $DevenvPath
            MSBuildPath = $MSBuildPath
            Lease = $lease
            LeaseToken = $leaseToken
        }
        if ($TestToolchain -ne $null) {
            $startArguments.TestToolchain = $TestToolchain
            $startArguments.TestToolRoot = $TestToolRoot
        }
        $intent = Start-ClassicBuildReceipt @startArguments
        $commands = New-Object "Collections.Generic.List[object]"
        foreach ($command in @($intent.commandPlan)) {
            $record = Invoke-ClassicCanonicalBuildCommand `
                -RepositoryRoot $RepositoryRoot `
                -Toolchain $intent.toolchain `
                -Command $command `
                -TestToolRoot $TestToolRoot `
                -TestCommandInvoker $TestCommandInvoker
            $commands.Add($record)
            if ([int]$record.exitCode -ne 0) {
                throw "Classic build command '$($record.role)' failed with exit code $($record.exitCode). The intent and logs were retained."
            }
        }

        # Evaluate the exact x64 and Win32 logs before completing the receipt.
        # A warning-budget failure retains the intent and logs and prevents
        # collection or publication.
        $nativeWarningGate = Get-ClassicNativeWarningGate `
            -RepositoryRoot $RepositoryRoot `
            -Commands $commands.ToArray()
        Write-NativeWarningBaselineSummary -Result $nativeWarningGate

        $receipt = Complete-ClassicBuildReceipt `
            -RepositoryRoot $RepositoryRoot `
            -PlanPath $PlanPath `
            -ReceiptPath $ReceiptPath `
            -Configuration "Release" `
            -Platforms @("Any CPU", "x64", "Win32") `
            -CommandRecords $commands.ToArray() `
            -NativeWarningGate $nativeWarningGate `
            -TestToolRoot $TestToolRoot `
            -Lease $lease `
            -LeaseToken $leaseToken
        [void](Assert-ClassicBuildReceipt `
            -RepositoryRoot $RepositoryRoot `
            -PlanPath $PlanPath `
            -ReceiptPath $ReceiptPath `
            -Configuration "Release" `
            -Platforms @("Any CPU", "x64", "Win32") `
            -TestToolRoot $TestToolRoot `
            -Lease $lease `
            -LeaseToken $leaseToken)

        if ($TestCollectionInvoker -ne $null) {
            & $TestCollectionInvoker `
                $ReceiptPath `
                $lease `
                $leaseToken `
                $artifactDirectory `
                $receipt
        }
        else {
            Invoke-ClassicArtifactCollection `
                -BuildReceiptPath $ReceiptPath `
                -Lease $lease `
                -LeaseToken $leaseToken `
                -RepositoryRootOverride $RepositoryRoot `
                -PlanPathOverride $PlanPath
        }
        Assert-ActiveClassicArtifactLease `
            -Lease $lease `
            -ArtifactDirectory $artifactDirectory `
            -LeaseToken $leaseToken
        return [pscustomobject]@{
            artifactDirectory = $artifactDirectory
            receiptPath = $ReceiptPath
            receiptContentSha256 =
                [string]$receipt.receiptContentSha256
            buildId = [string]$receipt.buildId
        }
    }
    finally {
        if ($lease -ne $null) {
            Exit-ClassicReleaseLease -Lease $lease
        }
    }
}

if ($MyInvocation.InvocationName -ne ".") {
    [void](Invoke-ClassicRelease `
        -RepositoryRoot $RepositoryRoot `
        -PlanPath $PlanPath `
        -ReceiptPath $ReceiptPath `
        -DevenvPath $DevenvPath `
        -MSBuildPath $MSBuildPath `
        -LeaseTimeoutMilliseconds $LeaseTimeoutMilliseconds `
        -ArchiveStalePendingIntent:$ArchiveStalePendingIntent)
}
