Set-StrictMode -Version 2.0

function Assert-NoReparsePointInExistingPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $pathRoot = [IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrWhiteSpace($pathRoot)) {
        throw "$Purpose is not an absolute path: $Path"
    }

    $currentPath = $pathRoot
    $components = $fullPath.Substring($pathRoot.Length) -split "[\\/]"
    $pathsToInspect = @($pathRoot)
    foreach ($component in $components) {
        if ([string]::IsNullOrWhiteSpace($component)) { continue }
        $currentPath = Join-Path $currentPath $component
        $pathsToInspect += $currentPath
    }

    foreach ($candidatePath in $pathsToInspect) {
        try {
            $item = Get-Item -LiteralPath $candidatePath -Force -ErrorAction Stop
        }
        catch {
            if ($_.CategoryInfo.Category -eq [Management.Automation.ErrorCategory]::ObjectNotFound) {
                continue
            }
            throw "Unable to inspect $Purpose path component '$candidatePath': $($_.Exception.Message)"
        }
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Purpose must not contain a reparse point: $candidatePath"
        }
    }
}

function Test-SameOrDescendantPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$CandidatePath,
        [Parameter(Mandatory = $true)]
        [string]$RootPath
    )

    $candidateFullPath = [IO.Path]::GetFullPath($CandidatePath)
    $rootFullPath = [IO.Path]::GetFullPath($RootPath)
    if ([string]::Equals(
        $candidateFullPath,
        $rootFullPath,
        [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $rootPrefix = $rootFullPath.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    return $candidateFullPath.StartsWith(
        $rootPrefix,
        [StringComparison]::OrdinalIgnoreCase)
}

function Assert-SafeArtifactName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Name) -or
        $Name.Length -gt 128 -or
        $Name -notmatch "^[A-Za-z0-9][A-Za-z0-9._-]*$" -or
        $Name.EndsWith(".", [StringComparison]::Ordinal) -or
        $Name -match "^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)") {
        throw "ArtifactName must be a simple safe filename (1-128 ASCII letters, digits, '.', '_' or '-', beginning with a letter or digit and not using a reserved Windows device name)."
    }
}

function Get-GeneratedUntrackedClassification {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $normalizedPath = $RelativePath.Replace("\", "/")
    $fileName = [IO.Path]::GetFileName($normalizedPath)
    $extension = [IO.Path]::GetExtension($fileName).ToLowerInvariant()
    switch ($extension) {
        ".obj" { return "native-object" }
        ".iobj" { return "native-object" }
        ".tlog" { return "native-build-trace" }
        ".lastbuildstate" { return "native-build-trace" }
        ".recipe" { return "native-build-trace" }
        ".pdb" { return "native-debug-symbol" }
        ".ipdb" { return "native-debug-symbol" }
        ".lib" { return "native-link-output" }
        ".exp" { return "native-link-output" }
        ".ilk" { return "native-link-output" }
        ".res" { return "native-resource-output" }
        ".idb" { return "native-compiler-cache" }
        ".pch" { return "native-compiler-cache" }
        ".sbr" { return "native-compiler-cache" }
        ".bsc" { return "native-compiler-cache" }
    }

    $hasNativeBuildDirectory =
        $normalizedPath -match "(?i)(^|/)(Debug|Release(?:_[^/]+)?|Win32|x64|obj)(/|$)"
    if ($extension -eq ".log" -and $hasNativeBuildDirectory) {
        return "native-build-log"
    }
    if ($fileName -match "(?i)\.vcxproj\.FileListAbsolute\.txt$") {
        return "native-build-log"
    }

    return $null
}

function Get-ProvenanceWorkspaceState {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [string]$PatchId,
        [int]$UntrackedSourceCount,
        [int]$ClassifiedGeneratedFileCount,
        [bool]$NestedWorkspacesClean
    )

    if ($UntrackedSourceCount -lt 0 -or
        $ClassifiedGeneratedFileCount -lt 0) {
        throw "Provenance workspace counts must not be negative."
    }
    # Build residue is surfaced by count and classification but is not source.
    # A validated archive-derived source closure is recorded separately and never
    # reaches this policy decision. Only a tracked patch, unknown untracked source,
    # or a dirty/missing nested workspace changes the source-state verdict.
    if ([string]::IsNullOrWhiteSpace($PatchId) -and
        $UntrackedSourceCount -eq 0 -and
        $NestedWorkspacesClean) {
        return "clean"
    }
    return "patched-or-untracked"
}

function Get-ClassicReleaseLeasePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseRoot
    )

    $releaseFullPath = [IO.Path]::GetFullPath($ReleaseRoot)
    return Join-Path $releaseFullPath ".cuetools-classic-release.lock"
}

function Enter-ClassicReleaseLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseRoot,
        [int]$TimeoutMilliseconds = 30000
    )

    if ($TimeoutMilliseconds -lt 1) {
        throw "Classic release lease timeout must be positive."
    }

    $releaseFullPath = [IO.Path]::GetFullPath($ReleaseRoot)
    if (-not (Test-Path -LiteralPath $releaseFullPath -PathType Container)) {
        New-Item -ItemType Directory -Path $releaseFullPath | Out-Null
    }
    Assert-NoReparsePointInExistingPath `
        -Path $releaseFullPath `
        -Purpose "Classic release lease directory"
    $leasePath = Get-ClassicReleaseLeasePath -ReleaseRoot $releaseFullPath
    if (Test-Path -LiteralPath $leasePath) {
        Assert-NoReparsePointInExistingPath `
            -Path $leasePath `
            -Purpose "Classic release lease"
        $leaseInfo = Get-Item -LiteralPath $leasePath -Force -ErrorAction Stop
        if ($leaseInfo.PSIsContainer -or
            -not ($leaseInfo -is [IO.FileInfo]) -or
            ($leaseInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Classic release lease path is not a regular file: $leasePath"
        }
    }

    $timer = [Diagnostics.Stopwatch]::StartNew()
    $lastFailure = $null
    while ($timer.ElapsedMilliseconds -lt $TimeoutMilliseconds) {
        $stream = $null
        try {
            $stream = New-Object IO.FileStream(
                $leasePath,
                [IO.FileMode]::OpenOrCreate,
                [IO.FileAccess]::ReadWrite,
                [IO.FileShare]::None)
            $priorToken = $null
            if ($stream.Length -eq 32) {
                $priorBytes = New-Object byte[] 32
                $priorRead = $stream.Read($priorBytes, 0, $priorBytes.Length)
                if ($priorRead -eq $priorBytes.Length) {
                    $strictUtf8 = New-Object Text.UTF8Encoding($false, $true)
                    $candidatePriorToken =
                        $strictUtf8.GetString($priorBytes)
                    if ($candidatePriorToken -cmatch "^[0-9a-f]{32}$") {
                        $priorToken = $candidatePriorToken
                    }
                }
            }
            $token = [Guid]::NewGuid().ToString("N")
            $tokenBytes = (New-Object Text.UTF8Encoding($false)).GetBytes($token)
            $stream.SetLength(0)
            $stream.Write($tokenBytes, 0, $tokenBytes.Length)
            $stream.Flush()
            $stream.Position = 0
            return [pscustomobject]@{
                path = [IO.Path]::GetFullPath($leasePath)
                releaseRoot = $releaseFullPath
                token = $token
                priorToken = $priorToken
                stream = $stream
            }
        }
        catch {
            if ($stream -ne $null) {
                $stream.Dispose()
                $stream = $null
            }
            $ioFailure = $_.Exception
            while ($ioFailure -ne $null -and
                -not ($ioFailure -is [IO.IOException])) {
                $ioFailure = $ioFailure.InnerException
            }
            if ($ioFailure -eq $null) { throw }
            $code = $ioFailure.HResult -band 0xffff
            if ($code -ne 32 -and $code -ne 33) { throw }
            $lastFailure = $ioFailure
            Start-Sleep -Milliseconds 25
        }
    }
    $timeout = New-Object TimeoutException(
        "Timed out waiting for the classic release lease at $leasePath.",
        $lastFailure)
    throw $timeout
}

function Assert-ActiveClassicReleaseLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Lease,
        [Parameter(Mandatory = $true)]
        [string]$ReleaseRoot,
        [Parameter(Mandatory = $true)]
        [string]$LeaseToken
    )

    if ($LeaseToken -cnotmatch "^[0-9a-f]{32}$") {
        throw "Classic release lease token is invalid."
    }
    $releaseFullPath = [IO.Path]::GetFullPath($ReleaseRoot)
    $expectedPath = [IO.Path]::GetFullPath(
        (Get-ClassicReleaseLeasePath -ReleaseRoot $releaseFullPath))
    if ($Lease -eq $null -or
        $Lease.PSObject.Properties["path"] -eq $null -or
        $Lease.PSObject.Properties["releaseRoot"] -eq $null -or
        $Lease.PSObject.Properties["token"] -eq $null -or
        $Lease.PSObject.Properties["priorToken"] -eq $null -or
        $Lease.PSObject.Properties["stream"] -eq $null -or
        [string]$Lease.token -cne $LeaseToken -or
        -not [string]::Equals(
            [string]$Lease.releaseRoot,
            $releaseFullPath,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals(
            [string]$Lease.path,
            $expectedPath,
            [StringComparison]::OrdinalIgnoreCase) -or
        $Lease.stream -eq $null -or
        $Lease.stream.SafeFileHandle.IsClosed -or
        -not [string]::Equals(
            [IO.Path]::GetFullPath($Lease.stream.Name),
            $expectedPath,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "An active repo-wide classic release lease with the exact token is required."
    }

    $position = $Lease.stream.Position
    try {
        $Lease.stream.Position = 0
        $reader = New-Object IO.StreamReader(
            $Lease.stream,
            (New-Object Text.UTF8Encoding($false)),
            $false,
            128,
            $true)
        try {
            $storedToken = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        if ($storedToken -cne $LeaseToken) {
            throw "Classic release lease token no longer matches its locked file."
        }
    }
    finally {
        $Lease.stream.Position = $position
    }
}

function Exit-ClassicReleaseLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Lease
    )

    if ($Lease.PSObject.Properties["stream"] -ne $null -and
        $Lease.stream -ne $null) {
        $Lease.stream.Dispose()
        $Lease.stream = $null
    }
}

function Get-VerifiedArtifactFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $fullRoot = [IO.Path]::GetFullPath($Root)
    Assert-NoReparsePointInExistingPath -Path $fullRoot -Purpose "Artifact directory"
    $rootInfo = Get-Item -LiteralPath $fullRoot -Force -ErrorAction Stop
    if (-not $rootInfo.PSIsContainer) {
        throw "Artifact directory is not a directory: $fullRoot"
    }

    $pendingDirectories = New-Object "Collections.Generic.Stack[string]"
    $files = New-Object "Collections.Generic.List[IO.FileInfo]"
    $pendingDirectories.Push($fullRoot)
    while ($pendingDirectories.Count -gt 0) {
        $directoryPath = $pendingDirectories.Pop()
        Assert-NoReparsePointInExistingPath `
            -Path $directoryPath `
            -Purpose "Artifact directory"
        $directoryInfo = Get-Item -LiteralPath $directoryPath -Force -ErrorAction Stop
        if (-not $directoryInfo.PSIsContainer -or
            ($directoryInfo.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Artifact directory must be a regular directory: $directoryPath"
        }

        foreach ($entry in @(Get-ChildItem -LiteralPath $directoryPath -Force)) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Artifact must not contain a reparse point: $($entry.FullName)"
            }
            if ($entry.PSIsContainer) {
                $pendingDirectories.Push($entry.FullName)
                continue
            }
            if (-not ($entry -is [IO.FileInfo])) {
                throw "Artifact contains an unsupported filesystem entry: $($entry.FullName)"
            }
            $files.Add($entry)
        }
    }

    return $files.ToArray()
}
