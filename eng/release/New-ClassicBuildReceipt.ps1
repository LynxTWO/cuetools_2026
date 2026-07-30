[CmdletBinding()]
param(
    [ValidateSet("None", "Begin", "Complete")]
    [string]$Phase = "None",
    [string]$RepositoryRoot,
    [string]$PlanPath,
    [string]$ReceiptPath,
    [string]$Configuration = "Release",
    [string[]]$Platforms = @("Any CPU", "x64", "Win32"),
    [string]$DevenvPath,
    [string]$MSBuildPath,
    [string]$CommandRecordsPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$releaseSafetyScript = Join-Path $PSScriptRoot "ReleaseSafety.ps1"
if (-not (Test-Path -LiteralPath $releaseSafetyScript -PathType Leaf)) {
    throw "Release safety helper does not exist: $releaseSafetyScript"
}
. $releaseSafetyScript

$nativeWarningScript = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..\ci\NativeWarningBaseline.ps1"))
if (-not (Test-Path -LiteralPath $nativeWarningScript -PathType Leaf)) {
    throw "Native warning helper does not exist: $nativeWarningScript"
}
. $nativeWarningScript

$vendorStagingScript = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..\ci\VendorSourceStaging.ps1"))
if (-not (Test-Path -LiteralPath $vendorStagingScript -PathType Leaf)) {
    throw "Vendor source staging helper does not exist: $vendorStagingScript"
}
. $vendorStagingScript

function Get-ClassicBuildTextSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Text
    )

    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($Text)
        return [BitConverter]::ToString(
            $algorithm.ComputeHash($bytes)).Replace("-", "")
    }
    finally {
        $algorithm.Dispose()
    }
}

function ConvertFrom-ClassicBuildJson([string]$Text) {
    # PowerShell 7.6 converts ISO-looking JSON strings to DateTime by default.
    # Receipt timestamps are canonical strings and must survive parsing byte-for-byte.
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey("DateKind")) {
        return $Text | ConvertFrom-Json -DateKind String
    }
    return $Text | ConvertFrom-Json
}

function Write-AtomicClassicBuildJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [object]$Value,
        [switch]$NoReplace
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetDirectoryName($fullPath)
    if ([string]::IsNullOrWhiteSpace($parent)) {
        throw "Classic build receipt has no parent directory: $fullPath"
    }
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    Assert-NoReparsePointInExistingPath `
        -Path $parent `
        -Purpose "Classic build receipt directory"
    if (Test-Path -LiteralPath $fullPath) {
        Assert-NoReparsePointInExistingPath `
            -Path $fullPath `
            -Purpose "Classic build receipt"
        $existing = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
        if ($existing.PSIsContainer -or
            -not ($existing -is [IO.FileInfo]) -or
            ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Classic build receipt path is not a regular file: $fullPath"
        }
        if ($NoReplace) {
            throw "Classic build receipt already exists: $fullPath"
        }
    }

    $temporaryPath = Join-Path $parent (
        "." + [IO.Path]::GetFileName($fullPath) +
        ".tmp-" + [Guid]::NewGuid().ToString("N"))
    $stream = $null
    $writer = $null
    try {
        $stream = New-Object IO.FileStream(
            $temporaryPath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        $writer = New-Object IO.StreamWriter(
            $stream,
            (New-Object Text.UTF8Encoding($false)),
            1024,
            $true)
        $writer.Write(($Value | ConvertTo-Json -Depth 16))
        $writer.Write("`n")
        $writer.Flush()
        $stream.Flush($true)
        $writer.Dispose()
        $writer = $null
        $stream.Dispose()
        $stream = $null
        if (Test-Path -LiteralPath $fullPath) {
            [IO.File]::Replace(
                $temporaryPath,
                $fullPath,
                [System.Management.Automation.Language.NullString]::Value)
        }
        else {
            [IO.File]::Move($temporaryPath, $fullPath)
        }
    }
    finally {
        if ($writer -ne $null) { $writer.Dispose() }
        if ($stream -ne $null) { $stream.Dispose() }
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Read-ClassicBuildJsonDocument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose,
        [long]$MaximumBytes = 1MB
    )

    if ($MaximumBytes -lt 1) {
        throw "$Purpose maximum size must be positive."
    }
    $fullPath = [IO.Path]::GetFullPath($Path)
    Assert-NoReparsePointInExistingPath -Path $fullPath -Purpose $Purpose
    $stream = $null
    $algorithm = $null
    try {
        # One deny-write/delete handle binds the length, digest, and parsed bytes to
        # the same file generation. A separate FileInfo/Get-Content pair leaves a
        # replacement window between the bound and the read.
        $stream = New-Object IO.FileStream(
            $fullPath,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        if ($stream.Length -le 0 -or $stream.Length -gt $MaximumBytes) {
            throw "$Purpose is not a bounded regular JSON file: $fullPath"
        }
        if ($stream.Length -gt [int]::MaxValue) {
            throw "$Purpose is too large to parse safely: $fullPath"
        }
        $bytes = New-Object byte[] ([int]$stream.Length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes, $offset, $bytes.Length - $offset)
            if ($read -le 0) {
                throw "$Purpose ended before its declared length: $fullPath"
            }
            $offset += $read
        }
        $algorithm = [Security.Cryptography.SHA256]::Create()
        $digest = [BitConverter]::ToString(
            $algorithm.ComputeHash($bytes)).Replace("-", "")
        $start = 0
        if ($bytes.Length -ge 3 -and
            $bytes[0] -eq 0xEF -and
            $bytes[1] -eq 0xBB -and
            $bytes[2] -eq 0xBF) {
            $start = 3
        }
        $encoding = New-Object Text.UTF8Encoding($false, $true)
        $text = $encoding.GetString($bytes, $start, $bytes.Length - $start)
        return [pscustomobject]@{
            path = $fullPath
            bytes = [long]$bytes.Length
            sha256 = $digest
            value = ConvertFrom-ClassicBuildJson $text
        }
    }
    finally {
        if ($algorithm -ne $null) { $algorithm.Dispose() }
        if ($stream -ne $null) { $stream.Dispose() }
    }
}

function Read-ClassicBuildJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose,
        [long]$MaximumBytes = 1MB
    )

    return (Read-ClassicBuildJsonDocument `
        -Path $Path `
        -Purpose $Purpose `
        -MaximumBytes $MaximumBytes).value
}

function Assert-ClassicExactProperties {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,
        [Parameter(Mandatory = $true)]
        [string[]]$Expected,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    if ($Value -eq $null) {
        throw "$Purpose is missing."
    }
    $actual = [string[]]@($Value.PSObject.Properties.Name)
    $wanted = [string[]]@($Expected)
    [Array]::Sort($actual, [StringComparer]::Ordinal)
    [Array]::Sort($wanted, [StringComparer]::Ordinal)
    if (($actual -join "`n") -cne ($wanted -join "`n")) {
        throw "$Purpose has an unexpected schema. Expected=[$($wanted -join ', ')], actual=[$($actual -join ', ')]."
    }
}

function Assert-ClassicSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    if ($Value -cnotmatch "^[0-9A-F]{64}$") {
        throw "$Purpose is not an uppercase SHA-256 digest."
    }
}

function ConvertTo-ClassicRelativePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or
        [IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath.IndexOf(":") -ge 0 -or
        $RelativePath.IndexOfAny([IO.Path]::GetInvalidPathChars()) -ge 0) {
        throw "$Purpose must be a safe relative path: '$RelativePath'."
    }
    $segments = $RelativePath.Replace("\", "/").Split("/")
    $reserved = "^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?$"
    foreach ($segment in $segments) {
        if ([string]::IsNullOrWhiteSpace($segment) -or
            $segment -eq "." -or
            $segment -eq ".." -or
            $segment.EndsWith(".", [StringComparison]::Ordinal) -or
            $segment.EndsWith(" ", [StringComparison]::Ordinal) -or
            $segment.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
            $segment -match $reserved) {
            throw "$Purpose contains an unsafe path segment: '$RelativePath'."
        }
    }
    return $segments -join "/"
}

function Resolve-ClassicRepositoryPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $normalized = ConvertTo-ClassicRelativePath `
        -RelativePath $RelativePath `
        -Purpose $Purpose
    $fullPath = [IO.Path]::GetFullPath(
        (Join-Path $root (
            $normalized.Replace(
                "/",
                [IO.Path]::DirectorySeparatorChar))))
    if (-not (Test-SameOrDescendantPath `
        -CandidatePath $fullPath `
        -RootPath $root) -or
        [string]::Equals(
            $fullPath,
            $root,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose escapes the repository: '$RelativePath'."
    }
    return $fullPath
}

function Get-ClassicBuildUntrackedRecords {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory
    )

    $records = New-Object "Collections.Generic.List[object]"
    $paths = @(& git -C $WorkingDirectory -c core.quotepath=false `
        ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate untracked source in $WorkingDirectory"
    }
    foreach ($relativePath in @($paths | Sort-Object)) {
        if ([string]::IsNullOrWhiteSpace([string]$relativePath)) { continue }
        $normalizedPath = ([string]$relativePath).Replace("\", "/")
        if ($null -ne (Get-GeneratedUntrackedClassification `
                -RelativePath $normalizedPath)) {
            # Native projects in old submodules do not consistently ignore their obj/lib/pdb/tlog
            # trees. Those files are build products, so /Build must be allowed to create or
            # replace them without changing source identity. Source-shaped untracked files such as
            # project definitions and patches remain hash-bound below.
            continue
        }
        $fullPath = [IO.Path]::GetFullPath(
            (Join-Path $WorkingDirectory ([string]$relativePath)))
        Assert-NoReparsePointInExistingPath `
            -Path $fullPath `
            -Purpose "Classic build untracked source"
        $info = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
        if ($info.PSIsContainer -or
            -not ($info -is [IO.FileInfo]) -or
            ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Classic build untracked source must be a regular file: $fullPath"
        }
        $file = Get-ClassicBuildRegularFileRecord `
            -Path $fullPath `
            -Purpose "Classic build untracked source"
        $records.Add([pscustomobject]([ordered]@{
            path = $normalizedPath
            bytes = [long]$file.bytes
            sha256 = [string]$file.sha256
        }))
    }
    return $records.ToArray()
}

function Get-ClassicGitWorkspaceRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,
        [Parameter(Mandatory = $true)]
        [string]$DisplayPath
    )

    $commit = (& git -C $WorkingDirectory rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
        throw "Unable to resolve Git commit in $WorkingDirectory"
    }
    $diffLines = @(& git -C $WorkingDirectory diff HEAD --binary `
        --no-ext-diff --submodule=diff)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to fingerprint tracked changes in $WorkingDirectory"
    }
    $diffText = ($diffLines -join "`n")
    if ($diffLines.Count -gt 0) { $diffText += "`n" }
    $untracked = @(Get-ClassicBuildUntrackedRecords `
        -WorkingDirectory $WorkingDirectory)

    $submodules = New-Object "Collections.Generic.List[object]"
    $gitmodules = Join-Path $WorkingDirectory ".gitmodules"
    if (Test-Path -LiteralPath $gitmodules -PathType Leaf) {
        $moduleLines = @(& git -C $WorkingDirectory config `
            --file .gitmodules --get-regexp "submodule\..*\.path")
        if ($LASTEXITCODE -ne 0 -and $moduleLines.Count -ne 0) {
            throw "Unable to enumerate submodules in $WorkingDirectory"
        }
        foreach ($line in @($moduleLines | Sort-Object)) {
            $parts = ([string]$line) -split "\s+", 2
            if ($parts.Count -ne 2) { continue }
            $relativePath = ConvertTo-ClassicRelativePath `
                -RelativePath $parts[1].Trim() `
                -Purpose "Git submodule path"
            $fullPath = Resolve-ClassicRepositoryPath `
                -RepositoryRoot $WorkingDirectory `
                -RelativePath $relativePath `
                -Purpose "Git submodule path"
            $subDisplayPath = $(if ($DisplayPath -eq ".") {
                $relativePath.Replace("\", "/")
            }
            else {
                $DisplayPath.TrimEnd("/") + "/" +
                    $relativePath.Replace("\", "/")
            })
            if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
                $submodules.Add([pscustomobject]([ordered]@{
                    path = $subDisplayPath
                    state = "missing"
                }))
                continue
            }
            Assert-NoReparsePointInExistingPath `
                -Path $fullPath `
                -Purpose "Git submodule workspace"
            $submodules.Add(
                (Get-ClassicGitWorkspaceRecord `
                    -WorkingDirectory $fullPath `
                    -DisplayPath $subDisplayPath))
        }
    }

    return [pscustomobject]([ordered]@{
        path = $DisplayPath
        commit = $commit
        trackedDiffSha256 = Get-ClassicBuildTextSha256 -Text $diffText
        untrackedFiles = $untracked
        submodules = $submodules.ToArray()
    })
}

function Get-ClassicBuildStreamSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [IO.Stream]$Stream
    )

    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString(
            $algorithm.ComputeHash($Stream)).Replace("-", "")
    }
    finally {
        $algorithm.Dispose()
    }
}

function Get-ClassicMacSdkExpandedSourceIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $macRelativePath = "ThirdParty/MAC_SDK"
    $archiveRelativePath = "$macRelativePath/MAC_1320_SDK.zip"
    $macRoot = Resolve-ClassicRepositoryPath `
        -RepositoryRoot $root `
        -RelativePath $macRelativePath `
        -Purpose "Monkey's Audio SDK root"
    $archivePath = Resolve-ClassicRepositoryPath `
        -RepositoryRoot $root `
        -RelativePath $archiveRelativePath `
        -Purpose "Monkey's Audio SDK archive"
    $hasRoot = Test-Path -LiteralPath $macRoot -PathType Container
    $hasArchive = Test-Path -LiteralPath $archivePath -PathType Leaf
    if (-not $hasRoot -and -not $hasArchive) {
        return [pscustomobject]([ordered]@{
            path = $macRelativePath
            state = "absent"
        })
    }
    if (-not $hasRoot -or -not $hasArchive) {
        throw "Monkey's Audio SDK source closure is partial."
    }
    Assert-NoReparsePointInExistingPath `
        -Path $macRoot `
        -Purpose "Monkey's Audio SDK root"
    Assert-NoReparsePointInExistingPath `
        -Path $archivePath `
        -Purpose "Monkey's Audio SDK archive"
    $manifestPath = Resolve-ClassicRepositoryPath `
        -RepositoryRoot $root `
        -RelativePath "eng/release/native-dependencies.json" `
        -Purpose "Native dependency manifest"
    $manifest = (Read-ClassicBuildJsonDocument `
        -Path $manifestPath `
        -Purpose "Native dependency manifest").value
    if ($manifest.PSObject.Properties["pinnedFiles"] -eq $null) {
        throw "Native dependency manifest does not declare pinned files."
    }

    $overridePaths = [string[]]@(
        "Source/MACLibDll/MACLibDll.cpp",
        "Source/MACLibDll/MACLibDll.def",
        "Source/MACLibDll/MACLibDll.h",
        "Source/Projects/VS2022/MACLibDll/MACLibDll.vcxproj")
    $overrideSet = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($relativePath in $overridePaths) {
        [void]$overrideSet.Add($relativePath)
    }
    $pinnedPaths = [string[]]@($archiveRelativePath) +
        [string[]]@($overridePaths | ForEach-Object {
            "$macRelativePath/$_"
        })
    $pinLookup = @{}
    foreach ($pinnedPath in $pinnedPaths) {
        $pins = @($manifest.pinnedFiles | Where-Object {
            [string]$_.path -ceq $pinnedPath
        })
        if ($pins.Count -ne 1) {
            throw "Native dependency manifest must contain exactly one pin for $pinnedPath."
        }
        Assert-ClassicExactProperties `
            -Value $pins[0] `
            -Expected @("path", "sha256") `
            -Purpose "Monkey's Audio SDK source pin"
        if ([string]$pins[0].sha256 -cnotmatch "^[0-9A-Fa-f]{64}$") {
            throw "Monkey's Audio SDK source pin is not a SHA-256 digest."
        }
        $pinLookup[$pinnedPath] =
            ([string]$pins[0].sha256).ToUpperInvariant()
    }

    $archiveRecord = Get-ClassicBuildRegularFileRecord `
        -Path $archivePath `
        -Purpose "Monkey's Audio SDK archive"
    if ([string]$archiveRecord.sha256 -cne
        [string]$pinLookup[$archiveRelativePath]) {
        throw "Monkey's Audio SDK archive does not match the native dependency manifest."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    $archiveFiles = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    $identityLines = New-Object "Collections.Generic.List[string]"
    try {
        foreach ($entry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) { continue }
            $normalized = ConvertTo-ClassicRelativePath `
                -RelativePath (
                    $entry.FullName.Replace("\", "/").TrimStart("/")) `
                -Purpose "Monkey's Audio SDK archive member"
            if (-not $archiveFiles.Add($normalized)) {
                throw "Monkey's Audio SDK archive contains a duplicate file member: $normalized"
            }
            if ($overrideSet.Contains($normalized)) {
                throw "Monkey's Audio SDK archive collides with a CUETools override: $normalized"
            }

            $entryStream = $entry.Open()
            try {
                $entryHash = Get-ClassicBuildStreamSha256 `
                    -Stream $entryStream
            }
            finally {
                $entryStream.Dispose()
            }
            $expandedPath = Resolve-ClassicRepositoryPath `
                -RepositoryRoot $root `
                -RelativePath "$macRelativePath/$normalized" `
                -Purpose "Expanded Monkey's Audio SDK source"
            $expanded = Get-ClassicBuildRegularFileRecord `
                -Path $expandedPath `
                -Purpose "Expanded Monkey's Audio SDK source"
            if ([long]$expanded.bytes -ne [long]$entry.Length -or
                [string]$expanded.sha256 -cne $entryHash) {
                throw "Expanded Monkey's Audio SDK source differs from its pinned archive: $normalized"
            }
            $identityLines.Add(
                "archive`t$normalized`t$($expanded.bytes)`t$($expanded.sha256)")
        }
    }
    finally {
        $archive.Dispose()
    }
    if ($archiveFiles.Count -eq 0) {
        throw "Monkey's Audio SDK archive contains no source files."
    }

    foreach ($relativePath in $overridePaths) {
        $manifestRelativePath = "$macRelativePath/$relativePath"
        $override = Get-ClassicBuildRegularFileRecord `
            -Path (Resolve-ClassicRepositoryPath `
                -RepositoryRoot $root `
                -RelativePath $manifestRelativePath `
                -Purpose "CUETools Monkey's Audio SDK override") `
            -Purpose "CUETools Monkey's Audio SDK override"
        if ([string]$override.sha256 -cne
            [string]$pinLookup[$manifestRelativePath]) {
            throw "CUETools Monkey's Audio SDK override does not match the native dependency manifest: $relativePath"
        }
        $identityLines.Add(
            "override`t$relativePath`t$($override.bytes)`t$($override.sha256)")
    }

    $generatedPattern =
        "^Source/Projects/Visual Studio - 2022/MACLib/(?:x64/)?(?:Debug|Release)/"
    $directories = New-Object "Collections.Generic.Stack[string]"
    $directories.Push($macRoot)
    while ($directories.Count -gt 0) {
        $directory = $directories.Pop()
        foreach ($item in (Get-ChildItem -LiteralPath $directory -Force)) {
            if (($item.Attributes -band
                    [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Monkey's Audio SDK source closure contains a reparse point: $($item.FullName)"
            }
            if ($item.PSIsContainer) {
                $directories.Push($item.FullName)
                continue
            }
            if (-not ($item -is [IO.FileInfo])) {
                throw "Monkey's Audio SDK source closure contains a non-file leaf: $($item.FullName)"
            }
            $relativePath = $item.FullName.Substring(
                $macRoot.Length).TrimStart(
                    [IO.Path]::DirectorySeparatorChar,
                    [IO.Path]::AltDirectorySeparatorChar).Replace("\", "/")
            if ($relativePath -ceq "MAC_1320_SDK.zip" -or
                $archiveFiles.Contains($relativePath) -or
                $overrideSet.Contains($relativePath) -or
                $relativePath -match $generatedPattern) {
                continue
            }
            throw "Monkey's Audio SDK source closure contains an unbound file: $relativePath"
        }
    }

    $lines = [string[]]@($identityLines)
    [Array]::Sort($lines, [StringComparer]::Ordinal)
    return [pscustomobject]([ordered]@{
        path = $macRelativePath
        state = "expanded"
        archive = [pscustomobject]([ordered]@{
            path = $archiveRelativePath
            bytes = [long]$archiveRecord.bytes
            sha256 = [string]$archiveRecord.sha256
        })
        archiveFileCount = [int]$archiveFiles.Count
        overrideFileCount = [int]$overridePaths.Count
        expandedTreeSha256 = Get-ClassicBuildTextSha256 `
            -Text (($lines -join "`n") + "`n")
    })
}

function Get-ClassicBuildSourceIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $record = Get-ClassicGitWorkspaceRecord `
        -WorkingDirectory $root `
        -DisplayPath "."
    $expandedNativeInputs = @(
        Get-ClassicMacSdkExpandedSourceIdentity -RepositoryRoot $root)
    $vendorSourceStage = $null
    if (Test-Path -LiteralPath (
        Join-Path $root "eng\ci\Prepare-VendorSources.ps1") -PathType Leaf) {
        $vendorSourceStage = Get-CUEToolsVendorSourceIdentity `
            -RepositoryRoot $root
    }
    $canonicalRecord = [pscustomobject]([ordered]@{
        workspace = $record
        expandedNativeInputs = $expandedNativeInputs
        vendorSourceStage = $vendorSourceStage
    })
    $canonical = $canonicalRecord | ConvertTo-Json -Depth 32 -Compress
    return [pscustomobject]([ordered]@{
        commit = [string]$record.commit
        fingerprintSha256 = Get-ClassicBuildTextSha256 -Text $canonical
        fingerprintPolicy = "HEAD plus exact tracked binary diff, non-ignored untracked source, recursive initialized-submodule state, explicitly hashed expanded native inputs, and the manifest-verified staged vendor source closure; classified build residue is excluded"
        workspace = $record
        expandedNativeInputs = $expandedNativeInputs
        vendorSourceStage = $vendorSourceStage
    })
}

function Sort-ClassicOrdinalStrings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Values
    )

    $copy = [string[]]@($Values)
    [Array]::Sort($copy, [StringComparer]::OrdinalIgnoreCase)
    return $copy
}

function Get-ClassicCollectionPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$PlanPath
    )

    $document = Read-ClassicBuildJsonDocument `
        -Path $PlanPath `
        -Purpose "Classic collection plan"
    $plan = $document.value
    Assert-ClassicExactProperties `
        -Value $plan `
        -Expected @(
            "schemaVersion",
            "collectionId",
            "productVersion",
            "freshBuildOutputs",
            "generatedFiles",
            "files") `
        -Purpose "Classic collection plan"
    if ([int]$plan.schemaVersion -ne 2 -or
        [string]::IsNullOrWhiteSpace([string]$plan.collectionId) -or
        [string]::IsNullOrWhiteSpace([string]$plan.productVersion) -or
        @($plan.files).Count -eq 0) {
        throw "Classic collection plan is incomplete or unsupported."
    }

    $fileRecords = New-Object "Collections.Generic.List[object]"
    $sourcePaths = New-Object "Collections.Generic.List[string]"
    $destinationSeen = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    $index = 0
    foreach ($entry in @($plan.files)) {
        Assert-ClassicExactProperties `
            -Value $entry `
            -Expected @("source", "destination") `
            -Purpose "Classic collection file entry"
        $source = ConvertTo-ClassicRelativePath `
            -RelativePath ([string]$entry.source) `
            -Purpose "Classic collection source"
        $destination = ConvertTo-ClassicRelativePath `
            -RelativePath ([string]$entry.destination) `
            -Purpose "Classic collection destination"
        if (-not $destinationSeen.Add($destination)) {
            throw "Classic collection plan repeats destination '$destination'."
        }
        $sourcePaths.Add($source)
        $fileRecords.Add([pscustomobject]@{
            index = $index
            path = $source
            fullPath = Resolve-ClassicRepositoryPath `
                -RepositoryRoot $RepositoryRoot `
                -RelativePath $source `
                -Purpose "Classic collection source"
        })
        $index++
    }
    if (@($plan.generatedFiles).Count -eq 0) {
        throw "Classic collection plan has no generated-file declarations."
    }
    foreach ($generatedPath in @($plan.generatedFiles)) {
        $normalized = ConvertTo-ClassicRelativePath `
            -RelativePath ([string]$generatedPath) `
            -Purpose "Classic generated destination"
        if (-not $destinationSeen.Add($normalized)) {
            throw "Classic collection plan repeats destination '$normalized'."
        }
    }

    $expectedNative = [string[]]@(
        "ThirdParty/Win32/libFLAC_dynamic.dll",
        "ThirdParty/Win32/MACLibDll.dll",
        "ThirdParty/Win32/wavpackdll.dll",
        "ThirdParty/x64/libFLAC_dynamic.dll",
        "ThirdParty/x64/MACLibDll.dll",
        "ThirdParty/x64/wavpackdll.dll")
    $declaredNative = New-Object "Collections.Generic.List[string]"
    $nativeSeen = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($path in @($plan.freshBuildOutputs)) {
        $normalized = ConvertTo-ClassicRelativePath `
            -RelativePath ([string]$path) `
            -Purpose "Classic explicit fresh build output"
        if (-not $nativeSeen.Add($normalized)) {
            throw "Classic collection plan repeats explicit fresh output '$normalized'."
        }
        if (@($sourcePaths | Where-Object {
            [string]::Equals(
                [string]$_,
                $normalized,
                [StringComparison]::OrdinalIgnoreCase)
        }).Count -ne 1) {
            throw "Explicit fresh output must name exactly one collection source: $normalized"
        }
        $declaredNative.Add($normalized)
    }
    $sortedExpectedNative = @(
        Sort-ClassicOrdinalStrings -Values $expectedNative)
    $sortedDeclaredNative = @(
        Sort-ClassicOrdinalStrings -Values $declaredNative.ToArray())
    if (($sortedDeclaredNative -join "`n") -cne
        ($sortedExpectedNative -join "`n")) {
        throw "Classic collection plan must declare the exact six Win32/x64 native build outputs."
    }

    $freshSeen = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($record in $fileRecords) {
        if (([string]$record.path).StartsWith(
                "bin/Release/",
                [StringComparison]::OrdinalIgnoreCase)) {
            [void]$freshSeen.Add([string]$record.path)
        }
    }
    foreach ($path in $declaredNative) {
        [void]$freshSeen.Add([string]$path)
    }
    $freshPaths = @(
        Sort-ClassicOrdinalStrings -Values ([string[]]@($freshSeen)))
    $freshLookup = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $freshPaths) { [void]$freshLookup.Add($path) }
    foreach ($record in $fileRecords) {
        $record | Add-Member -NotePropertyName freshBuildOutput `
            -NotePropertyValue $freshLookup.Contains([string]$record.path)
    }

    return [pscustomobject]@{
        repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
        document = $document
        value = $plan
        files = $fileRecords.ToArray()
        requiredAbsentAtBegin = $freshPaths
    }
}

function Get-ClassicCompiledCollectionSources {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$PlanPath
    )

    $plan = Get-ClassicCollectionPlan `
        -RepositoryRoot $RepositoryRoot `
        -PlanPath $PlanPath
    $records = New-Object "Collections.Generic.List[object]"
    foreach ($relativePath in @($plan.requiredAbsentAtBegin)) {
        $records.Add([pscustomobject]@{
            path = [string]$relativePath
            fullPath = Resolve-ClassicRepositoryPath `
                -RepositoryRoot $RepositoryRoot `
                -RelativePath ([string]$relativePath) `
                -Purpose "Classic fresh build output"
        })
    }
    return $records.ToArray()
}

function Get-ClassicBuildRegularFileRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    Assert-NoReparsePointInExistingPath -Path $fullPath -Purpose $Purpose
    $info = Get-Item -LiteralPath $fullPath -Force -ErrorAction Stop
    if ($info.PSIsContainer -or
        -not ($info -is [IO.FileInfo]) -or
        ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Purpose must be a regular file: $fullPath"
    }
    $stream = $null
    $algorithm = $null
    try {
        $stream = New-Object IO.FileStream(
            $fullPath,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        $length = [long]$stream.Length
        $algorithm = [Security.Cryptography.SHA256]::Create()
        $hash = [BitConverter]::ToString(
            $algorithm.ComputeHash($stream)).Replace("-", "")
        return [pscustomobject]@{
            bytes = $length
            sha256 = $hash
        }
    }
    finally {
        if ($algorithm -ne $null) { $algorithm.Dispose() }
        if ($stream -ne $null) { $stream.Dispose() }
    }
}

function Get-ClassicRequiredToolRoles {
    return [string[]]@(
        "devenv-com",
        "devenv-exe",
        "msbuild",
        "roslyn-csc",
        "v143-cl-x86",
        "v143-link-x86",
        "v143-cl-x64",
        "v143-link-x64",
        "v143-toolset-version",
        "net47-reference-assemblies",
        "installer-projects-extension")
}

function Get-ClassicVsWherePath {
    $path = Join-Path ${env:ProgramFiles(x86)} (
        "Microsoft Visual Studio\Installer\vswhere.exe")
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "vswhere.exe was not found."
    }
    return [IO.Path]::GetFullPath($path)
}

function Get-ClassicVisualStudioInstances {
    $vswhere = Get-ClassicVsWherePath
    $json = (& $vswhere -all -products * -format json -utf8) -join "`n"
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
        throw "Visual Studio instance discovery failed."
    }
    $instances = ConvertFrom-ClassicBuildJson $json
    foreach ($instance in @($instances)) {
        Write-Output $instance
    }
}

function Get-ClassicVisualStudioInstanceRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$VisualStudio
    )

    $matches = @(Get-ClassicVisualStudioInstances | Where-Object {
        [string]$_.instanceId -ceq [string]$VisualStudio.instanceId
    })
    if ($matches.Count -ne 1) {
        throw "The receipted Visual Studio instance is not uniquely installed."
    }
    $instance = $matches[0]
    if ([string]$instance.installationVersion -cne
            [string]$VisualStudio.installationVersion -or
        [string]$instance.productId -cne [string]$VisualStudio.productId -or
        -not [bool]$instance.isComplete -or
        -not [bool]$instance.isLaunchable) {
        throw "The receipted Visual Studio instance identity changed."
    }
    return [IO.Path]::GetFullPath([string]$instance.installationPath)
}

function New-ClassicBuildToolRecord {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Role,
        [Parameter(Mandatory = $true)]
        [ValidateSet("visualStudio", "referenceAssemblies")]
        [string]$PathKind,
        [Parameter(Mandatory = $true)]
        [string]$RelativePath,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $normalized = ConvertTo-ClassicRelativePath `
        -RelativePath $RelativePath `
        -Purpose "Classic build tool path"
    $fullPath = Resolve-ClassicRepositoryPath `
        -RepositoryRoot $Root `
        -RelativePath $normalized `
        -Purpose "Classic build tool path"
    $file = Get-ClassicBuildRegularFileRecord `
        -Path $fullPath `
        -Purpose "Classic build tool '$Role'"
    return [pscustomobject]([ordered]@{
        role = $Role
        pathKind = $PathKind
        path = $normalized
        fileVersion = [string](
            [Diagnostics.FileVersionInfo]::GetVersionInfo(
                $fullPath).FileVersion)
        bytes = [long]$file.bytes
        sha256 = [string]$file.sha256
    })
}

function Get-ClassicBuildToolchain {
    [CmdletBinding()]
    param(
        [string]$DevenvPath,
        [string]$MSBuildPath
    )

    if ([string]::IsNullOrWhiteSpace($DevenvPath) -xor
        [string]::IsNullOrWhiteSpace($MSBuildPath)) {
        throw "DevenvPath and MSBuildPath must be supplied together."
    }
    $instances = @(Get-ClassicVisualStudioInstances)
    $candidates = New-Object "Collections.Generic.List[object]"
    foreach ($instance in $instances) {
        if (-not [bool]$instance.isComplete -or
            -not [bool]$instance.isLaunchable) {
            continue
        }
        $root = [IO.Path]::GetFullPath([string]$instance.installationPath)
        $expectedDevenv = Join-Path $root "Common7\IDE\devenv.com"
        $expectedMSBuild = Join-Path $root "MSBuild\Current\Bin\MSBuild.exe"
        $extension = Join-Path $root (
            "Common7\IDE\CommonExtensions\Microsoft\VSI\bin\" +
            "Microsoft.VisualStudio.InstallerProjects.dll")
        if (-not (Test-Path -LiteralPath $expectedDevenv -PathType Leaf) -or
            -not (Test-Path -LiteralPath $expectedMSBuild -PathType Leaf) -or
            -not (Test-Path -LiteralPath $extension -PathType Leaf)) {
            continue
        }
        if (-not [string]::IsNullOrWhiteSpace($DevenvPath) -and
            (-not [string]::Equals(
                [IO.Path]::GetFullPath($DevenvPath),
                [IO.Path]::GetFullPath($expectedDevenv),
                [StringComparison]::OrdinalIgnoreCase) -or
             -not [string]::Equals(
                [IO.Path]::GetFullPath($MSBuildPath),
                [IO.Path]::GetFullPath($expectedMSBuild),
                [StringComparison]::OrdinalIgnoreCase))) {
            continue
        }
        $candidates.Add([pscustomobject]@{
            instance = $instance
            root = $root
        })
    }
    if ($candidates.Count -eq 0) {
        throw "No complete Visual Studio instance matches devenv.com, MSBuild, and Installer Projects."
    }
    if ([string]::IsNullOrWhiteSpace($DevenvPath)) {
        $candidate = @($candidates | Sort-Object {
            [version]$_.instance.installationVersion
        } -Descending)[0]
    }
    elseif ($candidates.Count -eq 1) {
        $candidate = $candidates[0]
    }
    else {
        throw "The requested Visual Studio tool paths matched more than one instance."
    }
    $instanceRoot = [string]$candidate.root
    Assert-NoReparsePointInExistingPath `
        -Path $instanceRoot `
        -Purpose "Visual Studio installation"

    $toolsetVersionRelative =
        "VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt"
    $toolsetVersionPath = Resolve-ClassicRepositoryPath `
        -RepositoryRoot $instanceRoot `
        -RelativePath $toolsetVersionRelative `
        -Purpose "Visual C++ toolset version"
    $toolsetVersion = (
        [IO.File]::ReadAllText($toolsetVersionPath)).Trim()
    if ($toolsetVersion -notmatch "^\d+\.\d+\.\d+$") {
        throw "Visual C++ default toolset version is invalid."
    }

    $referenceRoot = Join-Path ${env:ProgramFiles(x86)} (
        "Reference Assemblies\Microsoft\Framework")
    $definitions = @(
        @("devenv-com", "visualStudio", "Common7/IDE/devenv.com"),
        @("devenv-exe", "visualStudio", "Common7/IDE/devenv.exe"),
        @("msbuild", "visualStudio", "MSBuild/Current/Bin/MSBuild.exe"),
        @("roslyn-csc", "visualStudio", "MSBuild/Current/Bin/Roslyn/csc.exe"),
        @("v143-cl-x86", "visualStudio",
            "VC/Tools/MSVC/$toolsetVersion/bin/Hostx64/x86/cl.exe"),
        @("v143-link-x86", "visualStudio",
            "VC/Tools/MSVC/$toolsetVersion/bin/Hostx64/x86/link.exe"),
        @("v143-cl-x64", "visualStudio",
            "VC/Tools/MSVC/$toolsetVersion/bin/Hostx64/x64/cl.exe"),
        @("v143-link-x64", "visualStudio",
            "VC/Tools/MSVC/$toolsetVersion/bin/Hostx64/x64/link.exe"),
        @("v143-toolset-version", "visualStudio", $toolsetVersionRelative),
        @("net47-reference-assemblies", "referenceAssemblies",
            ".NETFramework/v4.7/mscorlib.dll"),
        @("installer-projects-extension", "visualStudio",
            ("Common7/IDE/CommonExtensions/Microsoft/VSI/bin/" +
             "Microsoft.VisualStudio.InstallerProjects.dll")))
    $tools = New-Object "Collections.Generic.List[object]"
    foreach ($definition in $definitions) {
        $root = $(if ($definition[1] -eq "visualStudio") {
            $instanceRoot
        } else {
            $referenceRoot
        })
        $tools.Add((New-ClassicBuildToolRecord `
            -Role $definition[0] `
            -PathKind $definition[1] `
            -RelativePath $definition[2] `
            -Root $root))
    }
    return [pscustomobject]([ordered]@{
        visualStudio = [pscustomobject]([ordered]@{
            instanceId = [string]$candidate.instance.instanceId
            installationVersion =
                [string]$candidate.instance.installationVersion
            productId = [string]$candidate.instance.productId
        })
        tools = $tools.ToArray()
    })
}

function New-TestClassicBuildToolchain {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ToolRoot
    )

    $definitions = @(
        @("devenv-com", "visualStudio", "Common7/IDE/devenv.com"),
        @("devenv-exe", "visualStudio", "Common7/IDE/devenv.exe"),
        @("msbuild", "visualStudio", "MSBuild/Current/Bin/MSBuild.exe"),
        @("roslyn-csc", "visualStudio", "MSBuild/Current/Bin/Roslyn/csc.exe"),
        @("v143-cl-x86", "visualStudio", "VC/Tools/MSVC/14.0/bin/Hostx64/x86/cl.exe"),
        @("v143-link-x86", "visualStudio", "VC/Tools/MSVC/14.0/bin/Hostx64/x86/link.exe"),
        @("v143-cl-x64", "visualStudio", "VC/Tools/MSVC/14.0/bin/Hostx64/x64/cl.exe"),
        @("v143-link-x64", "visualStudio", "VC/Tools/MSVC/14.0/bin/Hostx64/x64/link.exe"),
        @("v143-toolset-version", "visualStudio", "VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt"),
        @("net47-reference-assemblies", "referenceAssemblies", ".NETFramework/v4.7/mscorlib.dll"),
        @("installer-projects-extension", "visualStudio", "Common7/IDE/CommonExtensions/Microsoft/VSI/bin/Microsoft.VisualStudio.InstallerProjects.dll"))
    $tools = New-Object "Collections.Generic.List[object]"
    foreach ($definition in $definitions) {
        $tools.Add((New-ClassicBuildToolRecord `
            -Role $definition[0] `
            -PathKind $definition[1] `
            -RelativePath $definition[2] `
            -Root $ToolRoot))
    }
    return [pscustomobject]([ordered]@{
        visualStudio = [pscustomobject]([ordered]@{
            instanceId = "classic-release-test"
            installationVersion = "0.0.0.0"
            productId = "CUETools.TestFixture"
        })
        tools = $tools.ToArray()
    })
}

function Assert-ClassicBuildToolchainShape {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Toolchain,
        [switch]$AllowTestFixture
    )

    Assert-ClassicExactProperties `
        -Value $Toolchain `
        -Expected @("visualStudio", "tools") `
        -Purpose "Classic build toolchain"
    Assert-ClassicExactProperties `
        -Value $Toolchain.visualStudio `
        -Expected @("instanceId", "installationVersion", "productId") `
        -Purpose "Classic Visual Studio identity"
    if ([string]::IsNullOrWhiteSpace(
            [string]$Toolchain.visualStudio.instanceId) -or
        [string]::IsNullOrWhiteSpace(
            [string]$Toolchain.visualStudio.installationVersion) -or
        [string]::IsNullOrWhiteSpace(
            [string]$Toolchain.visualStudio.productId)) {
        throw "Classic Visual Studio identity is incomplete."
    }
    $isTest = [string]$Toolchain.visualStudio.productId -ceq
        "CUETools.TestFixture"
    if ($isTest -and -not $AllowTestFixture) {
        throw "A test fixture cannot satisfy a production classic build receipt."
    }
    if (-not $isTest -and
        -not ([string]$Toolchain.visualStudio.productId).StartsWith(
            "Microsoft.VisualStudio.Product.",
            [StringComparison]::Ordinal)) {
        throw "Classic build receipt does not identify a Visual Studio product."
    }
    $required = @(Get-ClassicRequiredToolRoles)
    $tools = @($Toolchain.tools)
    if ($tools.Count -ne $required.Count) {
        throw "Classic build toolchain has an invalid role count."
    }
    $seen = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::Ordinal)
    for ($i = 0; $i -lt $required.Count; $i++) {
        $tool = $tools[$i]
        Assert-ClassicExactProperties `
            -Value $tool `
            -Expected @(
                "role",
                "pathKind",
                "path",
                "fileVersion",
                "bytes",
                "sha256") `
            -Purpose "Classic build tool record"
        if ([string]$tool.role -cne $required[$i] -or
            -not $seen.Add([string]$tool.role)) {
            throw "Classic build tool roles are missing, duplicated, or out of order."
        }
        if ([string]$tool.pathKind -cne "visualStudio" -and
            [string]$tool.pathKind -cne "referenceAssemblies") {
            throw "Classic build tool has an invalid path root."
        }
        [void](ConvertTo-ClassicRelativePath `
            -RelativePath ([string]$tool.path) `
            -Purpose "Classic build tool path")
        if ([long]$tool.bytes -le 0) {
            throw "Classic build tool has an invalid length."
        }
        Assert-ClassicSha256 `
            -Value ([string]$tool.sha256) `
            -Purpose "Classic build tool hash"
    }
}

function Assert-CurrentClassicBuildToolchain {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Toolchain,
        [string]$TestToolRoot
    )

    $allowTest = -not [string]::IsNullOrWhiteSpace($TestToolRoot)
    Assert-ClassicBuildToolchainShape `
        -Toolchain $Toolchain `
        -AllowTestFixture:$allowTest
    if ($allowTest) {
        $current = New-TestClassicBuildToolchain -ToolRoot $TestToolRoot
    }
    else {
        $root = Get-ClassicVisualStudioInstanceRoot `
            -VisualStudio $Toolchain.visualStudio
        $current = Get-ClassicBuildToolchain `
            -DevenvPath (Join-Path $root "Common7\IDE\devenv.com") `
            -MSBuildPath (
                Join-Path $root "MSBuild\Current\Bin\MSBuild.exe")
    }
    if (-not (Test-ClassicJsonEquivalent `
        -Left $current `
        -Right $Toolchain)) {
        throw "Classic build receipt toolchain no longer matches the installed bytes."
    }
    return $current
}

function Resolve-ClassicBuildToolPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Toolchain,
        [Parameter(Mandatory = $true)]
        [string]$Role,
        [string]$TestToolRoot
    )

    [void](Assert-CurrentClassicBuildToolchain `
        -Toolchain $Toolchain `
        -TestToolRoot $TestToolRoot)
    $matches = @($Toolchain.tools | Where-Object {
        [string]$_.role -ceq $Role
    })
    if ($matches.Count -ne 1) {
        throw "Classic build tool role is not unique: $Role"
    }
    if (-not [string]::IsNullOrWhiteSpace($TestToolRoot)) {
        $root = [IO.Path]::GetFullPath($TestToolRoot)
    }
    elseif ([string]$matches[0].pathKind -ceq "visualStudio") {
        $root = Get-ClassicVisualStudioInstanceRoot `
            -VisualStudio $Toolchain.visualStudio
    }
    else {
        $root = Join-Path ${env:ProgramFiles(x86)} (
            "Reference Assemblies\Microsoft\Framework")
    }
    return Resolve-ClassicRepositoryPath `
        -RepositoryRoot $root `
        -RelativePath ([string]$matches[0].path) `
        -Purpose "Classic build tool '$Role'"
}

function Assert-ClassicBuildTuple {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Configuration,
        [Parameter(Mandatory = $true)]
        [string[]]$Platforms
    )

    $expected = @("Any CPU", "x64", "Win32")
    if ($Configuration -cne "Release" -or
        $Platforms.Count -ne $expected.Count -or
        (($Platforms -join "`n") -cne ($expected -join "`n"))) {
        throw "Classic release build receipt requires exactly " +
            "Release|Any CPU, Release|x64, and Release|Win32 in that order."
    }
}

function Test-ClassicJsonEquivalent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Left,
        [Parameter(Mandatory = $true)]
        [object]$Right
    )

    return (($Left | ConvertTo-Json -Depth 32 -Compress) -ceq
        ($Right | ConvertTo-Json -Depth 32 -Compress))
}

function Get-ClassicUtcTimestamp {
    return [DateTime]::UtcNow.ToString(
        "yyyy-MM-ddTHH:mm:ss.fffffffZ",
        [Globalization.CultureInfo]::InvariantCulture)
}

function ConvertFrom-ClassicUtcTimestamp {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    if ($Value -cnotmatch (
        "^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\." +
        "\d{7}Z$")) {
        throw "$Purpose is not a canonical UTC timestamp."
    }
    try {
        return [DateTimeOffset]::ParseExact(
            $Value,
            "yyyy-MM-ddTHH:mm:ss.fffffffZ",
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal)
    }
    catch {
        throw "$Purpose is not a valid UTC timestamp."
    }
}

function New-ClassicBuildCommandPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BuildId
    )

    if ($BuildId -cnotmatch "^[0-9a-f]{32}$") {
        throw "Classic build command plan has an invalid build ID."
    }
    $logRoot = "bin/Release/evidence/classic-build-logs/$BuildId"
    return [object[]]@(
        [pscustomobject]([ordered]@{
            sequence = 1
            role = "restore"
            toolRole = "msbuild"
            arguments = [string[]]@(
                "CUETools.sln",
                "/t:Restore",
                "/p:Configuration=Release",
                "/nologo")
            logPath = "$logRoot/01-restore.log"
        }),
        [pscustomobject]([ordered]@{
            sequence = 2
            role = "build"
            tuple = "Release|Any CPU"
            toolRole = "devenv-com"
            arguments = [string[]]@(
                "CUETools.sln",
                "/Build",
                "Release|Any CPU")
            logPath = "$logRoot/02-build-any-cpu.log"
        }),
        [pscustomobject]([ordered]@{
            sequence = 3
            role = "build"
            tuple = "Release|x64"
            toolRole = "devenv-com"
            arguments = [string[]]@(
                "CUETools.sln",
                "/Build",
                "Release|x64")
            logPath = "$logRoot/03-build-x64.log"
        }),
        [pscustomobject]([ordered]@{
            sequence = 4
            role = "build"
            tuple = "Release|Win32"
            toolRole = "devenv-com"
            arguments = [string[]]@(
                "CUETools.sln",
                "/Build",
                "Release|Win32")
            logPath = "$logRoot/04-build-win32.log"
        }))
}

function Assert-ClassicBuildCommandPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$CommandPlan,
        [Parameter(Mandatory = $true)]
        [string]$BuildId
    )

    $expected = @(New-ClassicBuildCommandPlan -BuildId $BuildId)
    if (-not (Test-ClassicJsonEquivalent `
        -Left @($CommandPlan) `
        -Right $expected)) {
        throw "Classic build command plan is not the canonical restore plus three builds."
    }
}

function Assert-ClassicBuildCommandRecords {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Commands,
        [Parameter(Mandatory = $true)]
        [object[]]$CommandPlan,
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$IntentStartedAt
    )

    if ($Commands.Count -ne $CommandPlan.Count) {
        throw "Classic build receipt has an invalid command count."
    }
    $priorCompleted = $IntentStartedAt
    for ($i = 0; $i -lt $CommandPlan.Count; $i++) {
        $planned = $CommandPlan[$i]
        $actual = $Commands[$i]
        $expectedProperties = @(
            "sequence",
            "role",
            "toolRole",
            "arguments",
            "startedAtUtc",
            "completedAtUtc",
            "exitCode",
            "log")
        if ([string]$planned.role -ceq "build") {
            $expectedProperties += "tuple"
        }
        Assert-ClassicExactProperties `
            -Value $actual `
            -Expected $expectedProperties `
            -Purpose "Classic build command record"
        if ([int]$actual.sequence -ne [int]$planned.sequence -or
            [string]$actual.role -cne [string]$planned.role -or
            [string]$actual.toolRole -cne [string]$planned.toolRole -or
            ((@($actual.arguments) -join "`n") -cne
                (@($planned.arguments) -join "`n")) -or
            [int]$actual.exitCode -ne 0) {
            throw "Classic build command record does not match its canonical invocation."
        }
        if ([string]$planned.role -ceq "build" -and
            [string]$actual.tuple -cne [string]$planned.tuple) {
            throw "Classic build command tuple is invalid."
        }
        Assert-ClassicExactProperties `
            -Value $actual.log `
            -Expected @("path", "bytes", "sha256") `
            -Purpose "Classic build command log"
        if ([string]$actual.log.path -cne [string]$planned.logPath -or
            [long]$actual.log.bytes -le 0) {
            throw "Classic build command log identity is invalid."
        }
        Assert-ClassicSha256 `
            -Value ([string]$actual.log.sha256) `
            -Purpose "Classic build command log hash"
        $logPath = Resolve-ClassicRepositoryPath `
            -RepositoryRoot $RepositoryRoot `
            -RelativePath ([string]$actual.log.path) `
            -Purpose "Classic build command log"
        $logFile = Get-ClassicBuildRegularFileRecord `
            -Path $logPath `
            -Purpose "Classic build command log"
        if ([long]$logFile.bytes -ne [long]$actual.log.bytes -or
            [string]$logFile.sha256 -cne [string]$actual.log.sha256) {
            throw "Classic build command log changed after execution."
        }
        $started = ConvertFrom-ClassicUtcTimestamp `
            -Value ([string]$actual.startedAtUtc) `
            -Purpose "Classic build command start time"
        $completed = ConvertFrom-ClassicUtcTimestamp `
            -Value ([string]$actual.completedAtUtc) `
            -Purpose "Classic build command completion time"
        if ($started -lt $priorCompleted -or $completed -lt $started) {
            throw "Classic build command times overlap or run out of order."
        }
        $priorCompleted = $completed
    }
    return $priorCompleted
}

function Get-ClassicNativeWarningGate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [object[]]$Commands
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $coverageIds = [string[]]@("Release|x64", "Release|Win32")
    $expectedSequences = @(3, 4)
    $nativeCommands = New-Object "Collections.Generic.List[object]"
    $logPaths = New-Object "Collections.Generic.List[string]"
    for ($i = 0; $i -lt $coverageIds.Count; $i++) {
        $matches = @($Commands | Where-Object {
            [string]$_.role -ceq "build" -and
            [string]$_.tuple -ceq $coverageIds[$i]
        })
        if ($matches.Count -ne 1 -or
            [int]$matches[0].sequence -ne $expectedSequences[$i]) {
            throw "Classic native warning coverage does not match the canonical x64 and Win32 builds."
        }
        Assert-ClassicExactProperties `
            -Value $matches[0].log `
            -Expected @("path", "bytes", "sha256") `
            -Purpose "Classic native warning command log"
        $logPath = Resolve-ClassicRepositoryPath `
            -RepositoryRoot $root `
            -RelativePath ([string]$matches[0].log.path) `
            -Purpose "Classic native warning command log"
        $nativeCommands.Add($matches[0])
        $logPaths.Add($logPath)
    }

    $baselineRelativePath = "eng/ci/native-warning-baseline.json"
    $baselinePath = Resolve-ClassicRepositoryPath `
        -RepositoryRoot $root `
        -RelativePath $baselineRelativePath `
        -Purpose "Classic native warning baseline"
    $evaluation = Get-NativeWarningBaselineResult `
        -RepositoryRoot $root `
        -WarningBaselinePath $baselinePath `
        -LogPaths $logPaths.ToArray() `
        -CoverageIds $coverageIds
    if (-not [string]::Equals(
            [string]$evaluation.baseline.path,
            $baselinePath,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Native warning evaluation returned an unexpected baseline path."
    }

    $logs = New-Object "Collections.Generic.List[object]"
    for ($i = 0; $i -lt $nativeCommands.Count; $i++) {
        $command = $nativeCommands[$i]
        $evaluatedLog = @($evaluation.logs)[$i]
        if (-not [string]::Equals(
                [string]$evaluatedLog.path,
                $logPaths[$i],
                [StringComparison]::OrdinalIgnoreCase) -or
            [long]$evaluatedLog.bytes -ne [long]$command.log.bytes -or
            [string]$evaluatedLog.sha256 -cne
                [string]$command.log.sha256) {
            throw "Native warning evaluation is not bound to the canonical command log."
        }
        $logs.Add([pscustomobject]([ordered]@{
            sequence = [int]$command.sequence
            tuple = [string]$command.tuple
            path = [string]$command.log.path
            bytes = [long]$evaluatedLog.bytes
            sha256 = [string]$evaluatedLog.sha256
        }))
    }

    return [pscustomobject]([ordered]@{
        schemaVersion = 1
        coverageIds = $coverageIds
        baseline = [pscustomobject]([ordered]@{
            path = $baselineRelativePath
            bytes = [long]$evaluation.baseline.bytes
            sha256 = [string]$evaluation.baseline.sha256
        })
        logs = $logs.ToArray()
        emittedWarningLines = [int]$evaluation.emittedWarningLines
        fingerprints = [string[]]@($evaluation.fingerprints)
        resolvedFingerprints =
            [string[]]@($evaluation.resolvedFingerprints)
    })
}

function Assert-ClassicNativeWarningGate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [object[]]$Commands,
        [Parameter(Mandatory = $true)]
        [object]$Gate
    )

    Assert-ClassicExactProperties `
        -Value $Gate `
        -Expected @(
            "schemaVersion",
            "coverageIds",
            "baseline",
            "logs",
            "emittedWarningLines",
            "fingerprints",
            "resolvedFingerprints") `
        -Purpose "Classic native warning gate"
    Assert-ClassicExactProperties `
        -Value $Gate.baseline `
        -Expected @("path", "bytes", "sha256") `
        -Purpose "Classic native warning gate baseline"
    foreach ($log in @($Gate.logs)) {
        Assert-ClassicExactProperties `
            -Value $log `
            -Expected @("sequence", "tuple", "path", "bytes", "sha256") `
            -Purpose "Classic native warning gate log"
    }
    $current = Get-ClassicNativeWarningGate `
        -RepositoryRoot $RepositoryRoot `
        -Commands $Commands
    if (-not (Test-ClassicJsonEquivalent -Left $Gate -Right $current)) {
        throw "Classic native warning gate is stale or not bound to the canonical native build logs."
    }
    return $current
}

function Get-ClassicCollectionInputRecords {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Plan
    )

    $paths = [string[]]@($Plan.files | ForEach-Object {
        [string]$_.path
    })
    [Array]::Sort($paths, [StringComparer]::OrdinalIgnoreCase)
    $records = New-Object "Collections.Generic.List[object]"
    foreach ($path in $paths) {
        $source = @($Plan.files | Where-Object {
            [string]$_.path -ceq $path
        } | Select-Object -First 1)[0]
        $file = Get-ClassicBuildRegularFileRecord `
            -Path ([string]$source.fullPath) `
            -Purpose "Classic collection input"
        $records.Add([pscustomobject]([ordered]@{
            path = $path
            bytes = [long]$file.bytes
            sha256 = [string]$file.sha256
            freshBuildOutput = [bool]$source.freshBuildOutput
        }))
    }
    return $records.ToArray()
}

function Assert-ClassicCollectionInputRecords {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Plan,
        [Parameter(Mandatory = $true)]
        [object[]]$Records,
        [switch]$Rehash
    )

    $expectedPaths = [string[]]@($Plan.files | ForEach-Object {
        [string]$_.path
    })
    [Array]::Sort($expectedPaths, [StringComparer]::OrdinalIgnoreCase)
    if ($Records.Count -ne $expectedPaths.Count) {
        throw "Classic build receipt collection input count is stale."
    }
    $freshLookup = New-Object "Collections.Generic.HashSet[string]" (
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($path in @($Plan.requiredAbsentAtBegin)) {
        [void]$freshLookup.Add([string]$path)
    }
    for ($i = 0; $i -lt $expectedPaths.Count; $i++) {
        $record = $Records[$i]
        Assert-ClassicExactProperties `
            -Value $record `
            -Expected @("path", "bytes", "sha256", "freshBuildOutput") `
            -Purpose "Classic collection input receipt"
        if ([string]$record.path -cne $expectedPaths[$i] -or
            [long]$record.bytes -lt 0 -or
            [bool]$record.freshBuildOutput -ne
                $freshLookup.Contains([string]$record.path)) {
            throw "Classic build receipt collection input set is stale."
        }
        Assert-ClassicSha256 `
            -Value ([string]$record.sha256) `
            -Purpose "Classic collection input hash"
        if ($Rehash) {
            $fullPath = Resolve-ClassicRepositoryPath `
                -RepositoryRoot ([string]$Plan.repositoryRoot) `
                -RelativePath ([string]$record.path) `
                -Purpose "Receipted classic collection input"
            $file = Get-ClassicBuildRegularFileRecord `
                -Path $fullPath `
                -Purpose "Receipted classic collection input"
            if ([long]$file.bytes -ne [long]$record.bytes -or
                [string]$file.sha256 -cne [string]$record.sha256) {
                throw "Classic collection input changed after its build receipt: $($record.path)"
            }
        }
    }
}

function Assert-ClassicBuildReleaseLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [object]$Lease,
        [Parameter(Mandatory = $true)]
        [string]$LeaseToken
    )

    $releaseRoot = Join-Path (
        [IO.Path]::GetFullPath($RepositoryRoot)) "bin\Release"
    Assert-ActiveClassicReleaseLease `
        -Lease $Lease `
        -ReleaseRoot $releaseRoot `
        -LeaseToken $LeaseToken
}

function Archive-ValidatedPendingClassicBuildIntent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$PlanPath,
        [Parameter(Mandatory = $true)]
        [string]$ReceiptPath,
        [Parameter(Mandatory = $true)]
        [string]$Configuration,
        [Parameter(Mandatory = $true)]
        [string[]]$Platforms,
        [Parameter(Mandatory = $true)]
        [object]$Lease,
        [Parameter(Mandatory = $true)]
        [string]$LeaseToken,
        [switch]$AllowStaleSource,
        [string]$TestToolRoot
    )

    Assert-ClassicBuildTuple `
        -Configuration $Configuration `
        -Platforms $Platforms
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    Assert-ClassicBuildReleaseLease `
        -RepositoryRoot $root `
        -Lease $Lease `
        -LeaseToken $LeaseToken
    $receiptFullPath = [IO.Path]::GetFullPath($ReceiptPath)
    $intentPath = $receiptFullPath + ".intent.json"
    if (-not (Test-Path -LiteralPath $intentPath)) {
        return $null
    }

    $intentDocument = Read-ClassicBuildJsonDocument `
        -Path $intentPath `
        -Purpose "Pending classic build intent"
    $intent = $intentDocument.value
    Assert-ClassicExactProperties `
        -Value $intent `
        -Expected @(
            "schemaVersion",
            "kind",
            "buildId",
            "leaseToken",
            "collectionId",
            "planSha256",
            "solution",
            "configuration",
            "platforms",
            "source",
            "toolchain",
            "commandPlan",
            "requiredAbsentAtBegin",
            "startedAtUtc") `
        -Purpose "Pending classic build intent"
    if ([int]$intent.schemaVersion -ne 2 -or
        [string]$intent.kind -ne "cuetools-classic-build-intent" -or
        [string]$intent.buildId -cnotmatch "^[0-9a-f]{32}$" -or
        [string]$intent.leaseToken -cnotmatch "^[0-9a-f]{32}$" -or
        [string]$intent.configuration -cne $Configuration -or
        ((@($intent.platforms) -join "`n") -cne ($Platforms -join "`n"))) {
        throw "Pending classic build intent does not match the recovery tuple."
    }
    $priorLeaseMatches =
        -not [string]::IsNullOrWhiteSpace([string]$Lease.priorToken) -and
        [string]$intent.leaseToken -ceq [string]$Lease.priorToken

    $plan = Get-ClassicCollectionPlan `
        -RepositoryRoot $root `
        -PlanPath $PlanPath
    if ([string]$intent.collectionId -ne
            [string]$plan.value.collectionId -or
        [string]$intent.planSha256 -cne
            [string]$plan.document.sha256) {
        throw "Pending classic build intent belongs to another collection plan."
    }
    Assert-ClassicSha256 `
        -Value ([string]$intent.planSha256) `
        -Purpose "Pending classic build intent plan hash"
    Assert-ClassicExactProperties `
        -Value $intent.solution `
        -Expected @("path", "sha256") `
        -Purpose "Pending classic build intent solution"
    if ([string]$intent.solution.path -cne "CUETools.sln") {
        throw "Pending classic build intent names an unexpected solution."
    }
    Assert-ClassicSha256 `
        -Value ([string]$intent.solution.sha256) `
        -Purpose "Pending classic build intent solution hash"
    $solution = Get-ClassicBuildRegularFileRecord `
        -Path (Resolve-ClassicRepositoryPath `
            -RepositoryRoot $root `
            -RelativePath "CUETools.sln" `
            -Purpose "Classic solution") `
        -Purpose "Classic solution"
    if ([string]$solution.sha256 -cne [string]$intent.solution.sha256) {
        throw "Pending classic build intent has a stale solution hash."
    }
    $source = Get-ClassicBuildSourceIdentity -RepositoryRoot $root
    $sourceChanged = -not (Test-ClassicJsonEquivalent `
        -Left $source `
        -Right $intent.source)
    if ($sourceChanged -and -not $AllowStaleSource) {
        throw "Pending classic build intent has a stale source fingerprint."
    }
    if (-not $priorLeaseMatches -and
        -not ($AllowStaleSource -and $sourceChanged)) {
        throw "Pending classic build intent is not bound to the prior repo-wide release lease."
    }
    [void](Assert-CurrentClassicBuildToolchain `
        -Toolchain $intent.toolchain `
        -TestToolRoot $TestToolRoot)
    # An explicitly approved stale-source recovery may also cross a command-plan
    # revision. The old bytes are archived, never executed; current plans remain
    # mandatory for same-source recovery and every new intent.
    if (-not ($AllowStaleSource -and $sourceChanged)) {
        Assert-ClassicBuildCommandPlan `
            -CommandPlan @($intent.commandPlan) `
            -BuildId ([string]$intent.buildId)
    }
    if ((@($intent.requiredAbsentAtBegin) -join "`n") -cne
        (@($plan.requiredAbsentAtBegin) -join "`n")) {
        throw "Pending classic build intent has a stale fresh-output set."
    }
    [void](ConvertFrom-ClassicUtcTimestamp `
        -Value ([string]$intent.startedAtUtc) `
        -Purpose "Pending classic build intent start time")

    $archivePath = $receiptFullPath + ".intent." +
        [string]$intent.buildId + ".abandoned.json"
    if (Test-Path -LiteralPath $archivePath) {
        throw "Pending classic build intent archive already exists: $archivePath"
    }
    [IO.File]::Move($intentPath, $archivePath)
    $archiveDocument = Read-ClassicBuildJsonDocument `
        -Path $archivePath `
        -Purpose "Archived classic build intent"
    if ([string]$archiveDocument.sha256 -cne
        [string]$intentDocument.sha256) {
        throw "Archived classic build intent bytes changed during recovery."
    }
    $archiveReason = if ($sourceChanged) {
        " after an explicitly approved source change"
    } else {
        ""
    }
    Write-Host (
        "Archived validated pending classic build intent$archiveReason`: " +
        $archivePath)
    return [pscustomobject]@{
        buildId = [string]$intent.buildId
        priorLeaseToken = [string]$intent.leaseToken
        archivePath = $archivePath
        contentSha256 = [string]$archiveDocument.sha256
        sourceChanged = $sourceChanged
        priorLeaseMatched = $priorLeaseMatches
    }
}

function Start-ClassicBuildReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$PlanPath,
        [Parameter(Mandatory = $true)]
        [string]$ReceiptPath,
        [Parameter(Mandatory = $true)]
        [string]$Configuration,
        [Parameter(Mandatory = $true)]
        [string[]]$Platforms,
        [string]$DevenvPath,
        [string]$MSBuildPath,
        [object]$TestToolchain,
        [string]$TestToolRoot,
        [Parameter(Mandatory = $true)]
        [object]$Lease,
        [Parameter(Mandatory = $true)]
        [string]$LeaseToken
    )

    Assert-ClassicBuildTuple `
        -Configuration $Configuration `
        -Platforms $Platforms
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    Assert-ClassicBuildReleaseLease `
        -RepositoryRoot $root `
        -Lease $Lease `
        -LeaseToken $LeaseToken
    $receiptFullPath = [IO.Path]::GetFullPath($ReceiptPath)
    $intentPath = $receiptFullPath + ".intent.json"
    if (Test-Path -LiteralPath $intentPath) {
        throw "A classic build intent already exists. Complete or inspect it before starting another build: $intentPath"
    }
    $plan = Get-ClassicCollectionPlan `
        -RepositoryRoot $root `
        -PlanPath $PlanPath
    $present = New-Object "Collections.Generic.List[string]"
    foreach ($relativePath in @($plan.requiredAbsentAtBegin)) {
        $fullPath = Resolve-ClassicRepositoryPath `
            -RepositoryRoot $root `
            -RelativePath ([string]$relativePath) `
            -Purpose "Classic fresh build output"
        if (Test-Path -LiteralPath $fullPath) {
            $present.Add([string]$relativePath)
        }
    }
    if ($present.Count -ne 0) {
        throw "Classic build freshness requires a clean output set. " +
            "These collection inputs already exist: " +
            ((@($present | Select-Object -First 5)) -join ", ") +
            $(if ($present.Count -gt 5) { ", ..." } else { "" })
    }

    $solutionRelativePath = "CUETools.sln"
    $solutionPath = Resolve-ClassicRepositoryPath `
        -RepositoryRoot $root `
        -RelativePath $solutionRelativePath `
        -Purpose "Classic solution"
    $solution = Get-ClassicBuildRegularFileRecord `
        -Path $solutionPath `
        -Purpose "Classic solution"
    if ($TestToolchain -ne $null) {
        if ([string]::IsNullOrWhiteSpace($TestToolRoot)) {
            throw "A test toolchain requires its isolated test root."
        }
        Assert-ClassicBuildToolchainShape `
            -Toolchain $TestToolchain `
            -AllowTestFixture
        [void](Assert-CurrentClassicBuildToolchain `
            -Toolchain $TestToolchain `
            -TestToolRoot $TestToolRoot)
        $toolchain = $TestToolchain
    }
    else {
        if (-not [string]::IsNullOrWhiteSpace($TestToolRoot)) {
            throw "A test tool root cannot be used with a production toolchain."
        }
        $toolchain = Get-ClassicBuildToolchain `
            -DevenvPath $DevenvPath `
            -MSBuildPath $MSBuildPath
        Assert-ClassicBuildToolchainShape -Toolchain $toolchain
    }
    $buildId = [Guid]::NewGuid().ToString("N")
    $commandPlan = @(New-ClassicBuildCommandPlan -BuildId $buildId)
    $intent = [ordered]@{
        schemaVersion = 2
        kind = "cuetools-classic-build-intent"
        buildId = $buildId
        leaseToken = $LeaseToken
        collectionId = [string]$plan.value.collectionId
        planSha256 = [string]$plan.document.sha256
        solution = [pscustomobject]([ordered]@{
            path = $solutionRelativePath
            sha256 = [string]$solution.sha256
        })
        configuration = $Configuration
        platforms = $Platforms
        source = Get-ClassicBuildSourceIdentity `
            -RepositoryRoot $root
        toolchain = $toolchain
        commandPlan = $commandPlan
        requiredAbsentAtBegin =
            [string[]]@($plan.requiredAbsentAtBegin)
        startedAtUtc = Get-ClassicUtcTimestamp
    }
    Write-AtomicClassicBuildJson `
        -Path $intentPath `
        -NoReplace `
        -Value $intent
    $intentDocument = Read-ClassicBuildJsonDocument `
        -Path $intentPath `
        -Purpose "Classic build intent"
    $result = $intentDocument.value
    $result | Add-Member -NotePropertyName intentContentSha256 `
        -NotePropertyValue ([string]$intentDocument.sha256)
    $result | Add-Member -NotePropertyName intentPath `
        -NotePropertyValue $intentPath
    Write-Host "Classic build intent: $intentPath"
    Write-Host "Classic build ID: $($intent.buildId)"
    return $result
}

function Complete-ClassicBuildReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$PlanPath,
        [Parameter(Mandatory = $true)]
        [string]$ReceiptPath,
        [Parameter(Mandatory = $true)]
        [string]$Configuration,
        [Parameter(Mandatory = $true)]
        [string[]]$Platforms,
        [Parameter(Mandatory = $true)]
        [object[]]$CommandRecords,
        [Parameter(Mandatory = $true)]
        [object]$NativeWarningGate,
        [string]$TestToolRoot,
        [Parameter(Mandatory = $true)]
        [object]$Lease,
        [Parameter(Mandatory = $true)]
        [string]$LeaseToken
    )

    Assert-ClassicBuildTuple `
        -Configuration $Configuration `
        -Platforms $Platforms
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    Assert-ClassicBuildReleaseLease `
        -RepositoryRoot $root `
        -Lease $Lease `
        -LeaseToken $LeaseToken
    $receiptFullPath = [IO.Path]::GetFullPath($ReceiptPath)
    $intentPath = $receiptFullPath + ".intent.json"
    $intentDocument = Read-ClassicBuildJsonDocument `
        -Path $intentPath `
        -Purpose "Classic build intent"
    $intent = $intentDocument.value
    Assert-ClassicExactProperties `
        -Value $intent `
        -Expected @(
            "schemaVersion",
            "kind",
            "buildId",
            "leaseToken",
            "collectionId",
            "planSha256",
            "solution",
            "configuration",
            "platforms",
            "source",
            "toolchain",
            "commandPlan",
            "requiredAbsentAtBegin",
            "startedAtUtc") `
        -Purpose "Classic build intent"
    if ([int]$intent.schemaVersion -ne 2 -or
        [string]$intent.kind -ne "cuetools-classic-build-intent" -or
        [string]$intent.buildId -cnotmatch "^[0-9a-f]{32}$" -or
        [string]$intent.leaseToken -cne $LeaseToken -or
        [string]$intent.configuration -cne $Configuration -or
        ((@($intent.platforms) -join "`n") -cne ($Platforms -join "`n"))) {
        throw "Classic build intent does not match the completion tuple."
    }
    $plan = Get-ClassicCollectionPlan `
        -RepositoryRoot $root `
        -PlanPath $PlanPath
    if ([string]$intent.collectionId -ne
            [string]$plan.value.collectionId -or
        [string]$intent.planSha256 -cne
            [string]$plan.document.sha256) {
        throw "Classic collection plan changed after the build intent was created."
    }
    Assert-ClassicSha256 `
        -Value ([string]$intent.planSha256) `
        -Purpose "Classic build intent plan hash"
    Assert-ClassicExactProperties `
        -Value $intent.solution `
        -Expected @("path", "sha256") `
        -Purpose "Classic build intent solution"
    if ([string]$intent.solution.path -cne "CUETools.sln") {
        throw "Classic build intent names an unexpected solution."
    }
    Assert-ClassicSha256 `
        -Value ([string]$intent.solution.sha256) `
        -Purpose "Classic build intent solution hash"
    $solution = Get-ClassicBuildRegularFileRecord `
        -Path (Resolve-ClassicRepositoryPath `
            -RepositoryRoot $root `
            -RelativePath "CUETools.sln" `
            -Purpose "Classic solution") `
        -Purpose "Classic solution"
    if ([string]$solution.sha256 -cne
        [string]$intent.solution.sha256) {
        throw "Classic solution changed after the build intent was created."
    }
    $source = Get-ClassicBuildSourceIdentity `
        -RepositoryRoot $root
    if (-not (Test-ClassicJsonEquivalent `
        -Left $source `
        -Right $intent.source)) {
        throw "Source changed after the classic build intent was created."
    }
    $toolchain = Assert-CurrentClassicBuildToolchain `
        -Toolchain $intent.toolchain `
        -TestToolRoot $TestToolRoot
    Assert-ClassicBuildCommandPlan `
        -CommandPlan @($intent.commandPlan) `
        -BuildId ([string]$intent.buildId)
    if ((@($intent.requiredAbsentAtBegin) -join "`n") -cne
        (@($plan.requiredAbsentAtBegin) -join "`n")) {
        throw "Classic fresh build output set changed after intent creation."
    }
    $intentStarted = ConvertFrom-ClassicUtcTimestamp `
        -Value ([string]$intent.startedAtUtc) `
        -Purpose "Classic build intent start time"
    $lastCommandCompleted = Assert-ClassicBuildCommandRecords `
        -Commands @($CommandRecords) `
        -CommandPlan @($intent.commandPlan) `
        -RepositoryRoot $root `
        -IntentStartedAt $intentStarted
    $nativeWarningGateRecord = Assert-ClassicNativeWarningGate `
        -RepositoryRoot $root `
        -Commands @($CommandRecords) `
        -Gate $NativeWarningGate
    $inputRecords = @(Get-ClassicCollectionInputRecords -Plan $plan)
    Assert-ClassicCollectionInputRecords `
        -Plan $plan `
        -Records $inputRecords

    $completedAt = Get-ClassicUtcTimestamp
    $completedTime = ConvertFrom-ClassicUtcTimestamp `
        -Value $completedAt `
        -Purpose "Classic build completion time"
    if ($completedTime -lt $lastCommandCompleted) {
        throw "Classic build completion predates its last command."
    }
    $receipt = [ordered]@{
        schemaVersion = 2
        kind = "cuetools-classic-build-receipt"
        buildId = [string]$intent.buildId
        leaseToken = $LeaseToken
        collectionId = [string]$intent.collectionId
        planSha256 = [string]$plan.document.sha256
        intentSha256 = [string]$intentDocument.sha256
        solution = $intent.solution
        configuration = $Configuration
        platforms = $Platforms
        source = $source
        toolchain = $toolchain
        commands = @($CommandRecords)
        nativeWarningGate = $nativeWarningGateRecord
        requiredAbsentAtBegin =
            [string[]]@($plan.requiredAbsentAtBegin)
        collectionInputs = $inputRecords
        startedAtUtc = [string]$intent.startedAtUtc
        completedAtUtc = $completedAt
    }
    Write-AtomicClassicBuildJson `
        -Path $receiptFullPath `
        -Value $receipt
    [IO.File]::Delete($intentPath)
    $receiptDocument = Read-ClassicBuildJsonDocument `
        -Path $receiptFullPath `
        -Purpose "Classic build receipt"
    $result = $receiptDocument.value
    $result | Add-Member -NotePropertyName receiptContentSha256 `
        -NotePropertyValue ([string]$receiptDocument.sha256)
    Write-Host "Classic build receipt: $receiptFullPath"
    return $result
}

function Assert-ClassicBuildReceipt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$PlanPath,
        [Parameter(Mandatory = $true)]
        [string]$ReceiptPath,
        [Parameter(Mandatory = $true)]
        [string]$Configuration,
        [Parameter(Mandatory = $true)]
        [string[]]$Platforms,
        [string]$TestToolRoot,
        [Parameter(Mandatory = $true)]
        [object]$Lease,
        [Parameter(Mandatory = $true)]
        [string]$LeaseToken
    )

    Assert-ClassicBuildTuple `
        -Configuration $Configuration `
        -Platforms $Platforms
    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    Assert-ClassicBuildReleaseLease `
        -RepositoryRoot $root `
        -Lease $Lease `
        -LeaseToken $LeaseToken
    $receiptFullPath = [IO.Path]::GetFullPath($ReceiptPath)
    $intentPath = $receiptFullPath + ".intent.json"
    if (Test-Path -LiteralPath $intentPath) {
        throw "Classic build receipt is not complete; a build intent is still pending: $intentPath"
    }
    $receiptDocument = Read-ClassicBuildJsonDocument `
        -Path $receiptFullPath `
        -Purpose "Classic build receipt"
    $receipt = $receiptDocument.value
    Assert-ClassicExactProperties `
        -Value $receipt `
        -Expected @(
            "schemaVersion",
            "kind",
            "buildId",
            "leaseToken",
            "collectionId",
            "planSha256",
            "intentSha256",
            "solution",
            "configuration",
            "platforms",
            "source",
            "toolchain",
            "commands",
            "nativeWarningGate",
            "requiredAbsentAtBegin",
            "collectionInputs",
            "startedAtUtc",
            "completedAtUtc") `
        -Purpose "Classic build receipt"
    $plan = Get-ClassicCollectionPlan `
        -RepositoryRoot $root `
        -PlanPath $PlanPath
    if ([int]$receipt.schemaVersion -ne 2 -or
        [string]$receipt.kind -ne "cuetools-classic-build-receipt" -or
        [string]$receipt.buildId -cnotmatch "^[0-9a-f]{32}$" -or
        [string]$receipt.leaseToken -cne $LeaseToken -or
        [string]$receipt.collectionId -ne
            [string]$plan.value.collectionId -or
        [string]$receipt.planSha256 -cne
            [string]$plan.document.sha256 -or
        [string]$receipt.configuration -cne $Configuration -or
        ((@($receipt.platforms) -join "`n") -cne ($Platforms -join "`n"))) {
        throw "Classic build receipt does not match the current plan and build tuple."
    }
    Assert-ClassicSha256 `
        -Value ([string]$receipt.planSha256) `
        -Purpose "Classic build receipt plan hash"
    Assert-ClassicSha256 `
        -Value ([string]$receipt.intentSha256) `
        -Purpose "Classic build receipt intent hash"
    Assert-ClassicExactProperties `
        -Value $receipt.solution `
        -Expected @("path", "sha256") `
        -Purpose "Classic build receipt solution"
    if ([string]$receipt.solution.path -cne "CUETools.sln") {
        throw "Classic build receipt names an unexpected solution."
    }
    $solution = Get-ClassicBuildRegularFileRecord `
        -Path (Resolve-ClassicRepositoryPath `
            -RepositoryRoot $root `
            -RelativePath "CUETools.sln" `
            -Purpose "Classic solution") `
        -Purpose "Classic solution"
    if ([string]$solution.sha256 -cne
        [string]$receipt.solution.sha256) {
        throw "Classic build receipt solution hash is stale."
    }
    $source = Get-ClassicBuildSourceIdentity `
        -RepositoryRoot $root
    if (-not (Test-ClassicJsonEquivalent `
        -Left $source `
        -Right $receipt.source)) {
        throw "Classic build receipt source fingerprint is stale."
    }

    [void](Assert-CurrentClassicBuildToolchain `
        -Toolchain $receipt.toolchain `
        -TestToolRoot $TestToolRoot)
    $commandPlan = @(New-ClassicBuildCommandPlan `
        -BuildId ([string]$receipt.buildId))
    $started = ConvertFrom-ClassicUtcTimestamp `
        -Value ([string]$receipt.startedAtUtc) `
        -Purpose "Classic build receipt start time"
    $lastCommandCompleted = Assert-ClassicBuildCommandRecords `
        -Commands @($receipt.commands) `
        -CommandPlan $commandPlan `
        -RepositoryRoot $root `
        -IntentStartedAt $started
    [void](Assert-ClassicNativeWarningGate `
        -RepositoryRoot $root `
        -Commands @($receipt.commands) `
        -Gate $receipt.nativeWarningGate)
    $completed = ConvertFrom-ClassicUtcTimestamp `
        -Value ([string]$receipt.completedAtUtc) `
        -Purpose "Classic build receipt completion time"
    if ($completed -lt $lastCommandCompleted) {
        throw "Classic build receipt completion predates its commands."
    }
    if ((@($receipt.requiredAbsentAtBegin) -join "`n") -cne
        (@($plan.requiredAbsentAtBegin) -join "`n")) {
        throw "Classic build receipt fresh output set is stale."
    }
    Assert-ClassicCollectionInputRecords `
        -Plan $plan `
        -Records @($receipt.collectionInputs) `
        -Rehash
    $receipt | Add-Member -NotePropertyName receiptContentSha256 `
        -NotePropertyValue ([string]$receiptDocument.sha256)
    $receipt | Add-Member -NotePropertyName receiptContentBytes `
        -NotePropertyValue ([long]$receiptDocument.bytes)
    return $receipt
}

if ($MyInvocation.InvocationName -ne ".") {
    throw "Direct classic build receipt execution is disabled. Run Invoke-ClassicRelease.ps1 so cleanup, build, receipt, collection, and publication share one release lease."
}
