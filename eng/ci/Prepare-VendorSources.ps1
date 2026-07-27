[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$StagingRoot,
    [int]$LeaseTimeoutMilliseconds = 30000,
    [switch]$PassThru
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$vendorStagingLibrary = Join-Path $PSScriptRoot "VendorSourceStaging.ps1"
if (-not (Test-Path -LiteralPath $vendorStagingLibrary -PathType Leaf)) {
    throw "Vendor source staging library does not exist: $vendorStagingLibrary"
}
. $vendorStagingLibrary

$result = Initialize-CUEToolsVendorSources `
    -RepositoryRoot $RepositoryRoot `
    -StagingRoot $StagingRoot `
    -LeaseTimeoutMilliseconds $LeaseTimeoutMilliseconds
if ($PassThru) {
    Write-Output $result
}
