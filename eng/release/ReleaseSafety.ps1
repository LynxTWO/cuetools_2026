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
