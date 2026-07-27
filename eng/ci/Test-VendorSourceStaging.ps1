[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$prepareScript = Join-Path $PSScriptRoot "Prepare-VendorSources.ps1"
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase (
    "cuetools-vendor-stage-" + [Guid]::NewGuid().ToString("N"))
$checkCount = 0

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checkCount++
}

function Invoke-TestGit(
    [string]$WorkingDirectory,
    [string[]]$Arguments) {
    $output = @(& git -C $WorkingDirectory @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture git command failed: git $($Arguments -join ' ')`n$($output -join "`n")"
    }
    return $output
}

function Write-TestText([string]$Path, [string]$Text) {
    $parent = [IO.Path]::GetDirectoryName($Path)
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        [void][IO.Directory]::CreateDirectory($parent)
    }
    [IO.File]::WriteAllText(
        $Path,
        $Text,
        (New-Object Text.UTF8Encoding($false)))
}

$vendors = @(
    [pscustomobject]@{
        id = "wavpack"
        path = "ThirdParty/WavPack"
        patch = "ThirdParty/submodule_WavPack_CUETools.patch"
    },
    [pscustomobject]@{
        id = "windows-media-lib"
        path = "ThirdParty/WindowsMediaLib"
        patch = "ThirdParty/submodule_WindowsMediaLib_CUETools.patch"
    },
    [pscustomobject]@{
        id = "flac"
        path = "ThirdParty/flac"
        patch = "ThirdParty/submodule_flac_CUETools.patch"
    },
    [pscustomobject]@{
        id = "taglib-sharp"
        path = "ThirdParty/taglib-sharp"
        patch = "ThirdParty/submodule_taglib-sharp_CUETools.patch"
    })

try {
    [void][IO.Directory]::CreateDirectory($tempRoot)
    [void](Invoke-TestGit $tempRoot @("init", "--quiet"))
    [void](Invoke-TestGit $tempRoot @("config", "user.name", "CUETools Test"))
    [void](Invoke-TestGit $tempRoot @(
        "config",
        "user.email",
        "cuetools-test@example.invalid"))

    $gitmodules = New-Object Text.StringBuilder
    foreach ($vendor in $vendors) {
        $submodulePath = Join-Path $tempRoot (
            ([string]$vendor.path).Replace(
                "/",
                [IO.Path]::DirectorySeparatorChar))
        [void][IO.Directory]::CreateDirectory($submodulePath)
        [void](Invoke-TestGit $submodulePath @("init", "--quiet"))
        [void](Invoke-TestGit $submodulePath @(
            "config",
            "user.name",
            "CUETools Test"))
        [void](Invoke-TestGit $submodulePath @(
            "config",
            "user.email",
            "cuetools-test@example.invalid"))
        $fixturePath = Join-Path $submodulePath "fixture.txt"
        Write-TestText $fixturePath "stock $($vendor.id)`n"
        [void](Invoke-TestGit $submodulePath @("add", "fixture.txt"))
        [void](Invoke-TestGit $submodulePath @(
            "commit",
            "--quiet",
            "-m",
            "fixture pin"))

        Write-TestText $fixturePath "patched $($vendor.id)`n"
        $patchLines = Invoke-TestGit $submodulePath @(
            "diff",
            "--",
            "fixture.txt")
        $patchPath = Join-Path $tempRoot (
            ([string]$vendor.patch).Replace(
                "/",
                [IO.Path]::DirectorySeparatorChar))
        Write-TestText $patchPath (($patchLines -join "`n") + "`n")
        [void](Invoke-TestGit $submodulePath @(
            "restore",
            "--",
            "fixture.txt"))

        [void]$gitmodules.AppendLine(
            "[submodule `"$($vendor.path)`"]")
        [void]$gitmodules.AppendLine("`tpath = $($vendor.path)")
        [void]$gitmodules.AppendLine(
            "`turl = https://example.invalid/$($vendor.id).git")
    }
    Write-TestText `
        -Path (Join-Path $tempRoot ".gitmodules") `
        -Text $gitmodules.ToString()
    [IO.File]::WriteAllText(
        (Join-Path $tempRoot ".gitignore"),
        "obj/`n",
        (New-Object Text.UTF8Encoding($false)))

    $rootPaths = @(".gitmodules", ".gitignore")
    $rootPaths += @($vendors | ForEach-Object { [string]$_.path })
    $rootPaths += @($vendors | ForEach-Object { [string]$_.patch })
    [void](Invoke-TestGit $tempRoot (@("add", "--") + $rootPaths))
    [void](Invoke-TestGit $tempRoot @(
        "commit",
        "--quiet",
        "-m",
        "fixture superproject"))

    $before = @($vendors | ForEach-Object {
        $path = Join-Path $tempRoot (
            ([string]$_.path).Replace(
                "/",
                [IO.Path]::DirectorySeparatorChar))
        (& git -C $path status --porcelain=v2 --untracked-files=all) -join "`n"
    })
    $first = & $prepareScript `
        -RepositoryRoot $tempRoot `
        -PassThru
    Assert-True `
        ([int]$first.sourceFileCount -eq 4) `
        "The staged fixture did not bind exactly four source files."
    Assert-True `
        ([string]$first.identitySha256 -match "^[0-9A-F]{64}$") `
        "The staged fixture did not produce a bounded identity."

    foreach ($vendor in $vendors) {
        $stagedFixture = Join-Path $tempRoot (
            "obj\vendor-sources\current\$($vendor.path)\fixture.txt")
        Assert-True `
            (([IO.File]::ReadAllText($stagedFixture)).Trim() -ceq
                "patched $($vendor.id)") `
            "The staged source did not contain the checked patch for $($vendor.id)."
    }

    $second = & $prepareScript `
        -RepositoryRoot $tempRoot `
        -PassThru
    Assert-True `
        ([string]$second.identitySha256 -ceq
            [string]$first.identitySha256) `
        "Repeated preparation changed the same pinned stage identity."
    Assert-True `
        ([string]$second.sourceManifestSha256 -ceq
            [string]$first.sourceManifestSha256) `
        "Repeated preparation changed the same pinned source manifest."

    $tamperedPath = Join-Path $tempRoot (
        "obj\vendor-sources\current\ThirdParty\flac\fixture.txt")
    [IO.File]::AppendAllText($tamperedPath, "tampered`n")
    $repaired = & $prepareScript `
        -RepositoryRoot $tempRoot `
        -PassThru
    Assert-True `
        (([IO.File]::ReadAllText($tamperedPath)).Trim() -ceq
            "patched flac") `
        "Preparation did not replace a tampered staged source."
    Assert-True `
        (@(
            Get-ChildItem `
                -LiteralPath (Join-Path $tempRoot "obj\vendor-sources") `
                -Directory |
                Where-Object Name -Like "quarantine-*"
        ).Count -eq 1) `
        "Preparation did not retain exactly one tampered stage for recovery."
    Assert-True `
        ([string]$repaired.identitySha256 -ceq
            [string]$first.identitySha256) `
        "Repair changed the pinned stage identity."

    $junctionTarget = Join-Path $tempRoot "junction-target"
    [void][IO.Directory]::CreateDirectory($junctionTarget)
    $junctionPath = Join-Path $tempRoot (
        "obj\vendor-sources\current\unowned-junction")
    [void](New-Item `
        -ItemType Junction `
        -Path $junctionPath `
        -Target $junctionTarget)
    $reparseRepaired = & $prepareScript `
        -RepositoryRoot $tempRoot `
        -PassThru
    Assert-True `
        (-not (Test-Path -LiteralPath $junctionPath)) `
        "Preparation accepted a reparse point inside the staged tree."
    Assert-True `
        ([string]$reparseRepaired.identitySha256 -ceq
            [string]$first.identitySha256) `
        "Reparse-point rejection changed the pinned stage identity."

    $dirtySubmodule = Join-Path $tempRoot "ThirdParty\WavPack"
    [IO.File]::AppendAllText(
        (Join-Path $dirtySubmodule "fixture.txt"),
        "dirty`n")
    $dirtyRejected = $false
    try {
        [void](& $prepareScript -RepositoryRoot $tempRoot -PassThru)
    }
    catch {
        $dirtyRejected = $_.Exception.Message -match
            "requires a clean submodule"
    }
    Assert-True `
        $dirtyRejected `
        "Preparation accepted a dirty pinned submodule."
    [void](Invoke-TestGit $dirtySubmodule @(
        "restore",
        "--",
        "fixture.txt"))

    $after = @($vendors | ForEach-Object {
        $path = Join-Path $tempRoot (
            ([string]$_.path).Replace(
                "/",
                [IO.Path]::DirectorySeparatorChar))
        (& git -C $path status --porcelain=v2 --untracked-files=all) -join "`n"
    })
    Assert-True `
        (($before -join "`n") -ceq ($after -join "`n")) `
        "Vendor source preparation changed a pinned submodule worktree."

    Write-Host "Vendor source staging checks passed: $checkCount"
}
finally {
    $prefix = $tempBase.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $leaf = [IO.Path]::GetFileName($tempRoot)
    if (-not $tempRoot.StartsWith(
        $prefix,
        [StringComparison]::OrdinalIgnoreCase) -or
        -not $leaf.StartsWith(
            "cuetools-vendor-stage-",
            [StringComparison]::Ordinal)) {
        throw "Refusing to clean an unexpected vendor staging test path: $tempRoot"
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
