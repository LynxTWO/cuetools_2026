Set-StrictMode -Version 2.0

function Get-NativeWarningFileDocument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    $stream = New-Object IO.FileStream(
        $fullPath,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        if ($stream.Length -lt 0 -or $stream.Length -gt 64MB -or
            $stream.Length -gt [int]::MaxValue) {
            throw "$Purpose is outside the bounded file size: $fullPath"
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
        $sha256 = [BitConverter]::ToString(
            $algorithm.ComputeHash($bytes)).Replace("-", "")

        $encoding = $null
        $start = 0
        if ($bytes.Length -ge 4 -and
            (($bytes[0] -eq 0xFF -and
              $bytes[1] -eq 0xFE -and
              $bytes[2] -eq 0x00 -and
              $bytes[3] -eq 0x00) -or
             ($bytes[0] -eq 0x00 -and
              $bytes[1] -eq 0x00 -and
              $bytes[2] -eq 0xFE -and
              $bytes[3] -eq 0xFF))) {
            throw "$Purpose uses unsupported UTF-32 encoding: $fullPath"
        }
        elseif ($bytes.Length -ge 2 -and
            $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
            $encoding = New-Object Text.UnicodeEncoding($false, $true, $true)
            $start = 2
        }
        elseif ($bytes.Length -ge 2 -and
            $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
            $encoding = New-Object Text.UnicodeEncoding($true, $true, $true)
            $start = 2
        }
        else {
            $encoding = New-Object Text.UTF8Encoding($false, $true)
            if ($bytes.Length -ge 3 -and
                $bytes[0] -eq 0xEF -and
                $bytes[1] -eq 0xBB -and
                $bytes[2] -eq 0xBF) {
                $start = 3
            }
        }
        $text = $encoding.GetString($bytes, $start, $bytes.Length - $start)
        if ($text.IndexOf([char]0) -ge 0) {
            throw "$Purpose contains NUL text or unsupported BOM-less UTF-16: $fullPath"
        }
        return [pscustomobject]([ordered]@{
            path = $fullPath
            bytes = [long]$bytes.Length
            sha256 = $sha256
            text = $text
        })
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Assert-NativeWarningBaselineShape {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Baseline
    )

    $expectedProperties = @(
        "schemaVersion",
        "lane",
        "builds",
        "fingerprints",
        "limitations")
    $actualProperties = [string[]]@($Baseline.PSObject.Properties.Name)
    [Array]::Sort($actualProperties, [StringComparer]::Ordinal)
    [Array]::Sort($expectedProperties, [StringComparer]::Ordinal)
    if (($actualProperties -join "`n") -cne
        ($expectedProperties -join "`n")) {
        throw "Native warning baseline has an unexpected schema."
    }
    if ([int]$Baseline.schemaVersion -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$Baseline.lane) -or
        @($Baseline.builds).Count -eq 0) {
        throw "Unsupported or incomplete native warning baseline."
    }

    $fingerprints = [string[]]@($Baseline.fingerprints)
    $sorted = [string[]]@($fingerprints)
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    if (($fingerprints -join "`n") -cne ($sorted -join "`n") -or
        @($fingerprints | Sort-Object -Unique).Count -ne
            $fingerprints.Count) {
        throw "Native warning baseline fingerprints must be sorted and unique."
    }
    foreach ($fingerprint in $fingerprints) {
        if ([string]::IsNullOrWhiteSpace($fingerprint) -or
            $fingerprint -cnotmatch "^[^|]+\|[A-Z]+\d+\|.+$") {
            throw "Native warning baseline contains a malformed fingerprint."
        }
    }
}

function ConvertTo-NativeWarningFingerprint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Line,
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot
    )

    $marker = [Text.RegularExpressions.Regex]::Match(
        $Line,
        ':\s*warning\s+(?<code>[A-Za-z]+\d+)\s*:',
        [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $marker.Success) { return $null }

    $left = $Line.Substring(0, $marker.Index).Trim()
    $message = $Line.Substring($marker.Index + $marker.Length).Trim()
    $message = [Text.RegularExpressions.Regex]::Replace(
        $message,
        '\s+\[[^\]]+\]\s*$',
        "")
    $message = [Text.RegularExpressions.Regex]::Replace(
        $message,
        "\s+",
        " ")
    $rootForward = [IO.Path]::GetFullPath($RepositoryRoot).
        TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar).
        Replace("\", "/")
    $message = $message.Replace(
        [IO.Path]::GetFullPath($RepositoryRoot),
        "<repo>")
    $message = $message.Replace($rootForward, "<repo>")

    # Devenv prefixes lines with a project ordinal (for example 4>), and hosted
    # loggers may add a timestamp first. Locate the repository-native path anywhere
    # in the left side instead of trusting a particular prefix or slash style.
    $source = $left.Replace("\", "/")
    $nativeRoots = @(
        "ThirdParty/flac/",
        "ThirdParty/WavPack/",
        "ThirdParty/MAC_SDK/",
        "ttalib-1.1/",
        "CUETools.Codecs.TTA/")
    $nativePath = $null
    foreach ($nativeRoot in $nativeRoots) {
        $index = $source.IndexOf(
            $nativeRoot,
            [StringComparison]::OrdinalIgnoreCase)
        if ($index -ge 0) {
            $nativePath = $source.Substring($index)
            break
        }
    }
    if ($nativePath -ne $null) {
        $nativePath = [Text.RegularExpressions.Regex]::Replace(
            $nativePath,
            '\(\d+(?:,\d+){0,3}\)\s*$',
            "")
        return "$nativePath|$($marker.Groups["code"].Value.ToUpperInvariant())|$message"
    }

    # A repository path outside the native roots is a managed/non-target warning.
    # Recognize both slash forms and timestamp/project prefixes.
    $rootIndex = $source.IndexOf(
        $rootForward + "/",
        [StringComparison]::OrdinalIgnoreCase)
    if ($rootIndex -ge 0) { return $null }

    $code = $marker.Groups["code"].Value.ToUpperInvariant()
    if ($code.StartsWith("CS", [StringComparison]::Ordinal) -or
        $code.StartsWith("AL", [StringComparison]::Ordinal)) {
        return $null
    }

    # Native tools can issue warnings without a source path, such as
    # "cl : warning D9025". Keep those in policy rather than silently dropping them.
    $tool = [Text.RegularExpressions.Regex]::Replace(
        $source,
        '^.*?(?:\d+>)',
        "").Trim()
    $tool = [Text.RegularExpressions.Regex]::Replace(
        $tool,
        '\(\d+(?:,\d+){0,3}\)\s*$',
        "").Trim()
    if ([string]::IsNullOrWhiteSpace($tool)) {
        $tool = "<native-tool>"
    }
    return "$tool|$code|$message"
}

function Get-NativeWarningBaselineResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory = $true)]
        [string]$WarningBaselinePath,
        [string[]]$LogPaths = @(),
        [string[]]$WarningLines = @(),
        [Parameter(Mandatory = $true)]
        [string[]]$CoverageIds,
        [switch]$UpdateWarningBaseline
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $baselinePath = [IO.Path]::GetFullPath($WarningBaselinePath)
    $baselineDocument = Get-NativeWarningFileDocument `
        -Path $baselinePath `
        -Purpose "Native warning baseline"
    $warningBaseline = $baselineDocument.text | ConvertFrom-Json
    Assert-NativeWarningBaselineShape -Baseline $warningBaseline

    if (@($CoverageIds).Count -eq 0 -or
        @($CoverageIds | Sort-Object -Unique).Count -ne
            @($CoverageIds).Count) {
        throw "Native warning coverage ids must be nonempty and unique."
    }
    if (@($LogPaths).Count -eq 0 -and @($WarningLines).Count -eq 0) {
        throw "Native warning evaluation requires actual log text."
    }
    $normalizedLogPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $allLines = [Collections.Generic.List[string]]::new()
    foreach ($line in @($WarningLines)) {
        $allLines.Add([string]$line)
    }
    $logRecords = [Collections.Generic.List[object]]::new()
    foreach ($logPath in @($LogPaths)) {
        $fullLogPath = [IO.Path]::GetFullPath($logPath)
        if (-not $normalizedLogPaths.Add($fullLogPath)) {
            throw "Native warning evaluation received a duplicate log path."
        }
        $log = Get-NativeWarningFileDocument `
            -Path $fullLogPath `
            -Purpose "Native warning input log"
        $logRecords.Add([pscustomobject]([ordered]@{
            path = [string]$log.path
            bytes = [long]$log.bytes
            sha256 = [string]$log.sha256
        }))
        foreach ($line in @($log.text -split "\r?\n")) {
            $allLines.Add([string]$line)
        }
    }

    $fingerprints = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $emittedNativeLines = 0
    foreach ($line in $allLines) {
        if ($line -notmatch
            ":\s*warning\s+[A-Za-z]+\d+\s*:") {
            continue
        }
        $fingerprint = ConvertTo-NativeWarningFingerprint `
            -Line $line `
            -RepositoryRoot $root
        if ($fingerprint -eq $null) { continue }
        [void]$fingerprints.Add($fingerprint)
        $emittedNativeLines++
    }

    $actual = [string[]]@($fingerprints)
    [Array]::Sort($actual, [StringComparer]::Ordinal)
    $expected = [string[]]@($warningBaseline.fingerprints)
    $newWarnings = @(
        $actual |
            Where-Object {
                [Array]::BinarySearch(
                    $expected,
                    $_,
                    [StringComparer]::Ordinal) -lt 0
            })
    $resolvedWarnings = @(
        $expected |
            Where-Object {
                [Array]::BinarySearch(
                    $actual,
                    $_,
                    [StringComparer]::Ordinal) -lt 0
            })

    if ($UpdateWarningBaseline) {
        $expectedPath = [IO.Path]::GetFullPath(
            (Join-Path $root "eng\ci\native-warning-baseline.json"))
        if (-not [string]::Equals(
            $baselinePath,
            $expectedPath,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Native warning baseline updates are limited to the repository policy file."
        }
        $warningBaseline.fingerprints = $actual
        $temporaryPath = $baselinePath + ".tmp-" +
            [Guid]::NewGuid().ToString("N")
        try {
            $utf8 = New-Object Text.UTF8Encoding($false)
            [IO.File]::WriteAllText(
                $temporaryPath,
                (($warningBaseline | ConvertTo-Json -Depth 8) + "`n"),
                $utf8)
            [IO.File]::Replace(
                $temporaryPath,
                $baselinePath,
                [System.Management.Automation.Language.NullString]::Value)
        }
        finally {
            if (Test-Path -LiteralPath $temporaryPath) {
                [IO.File]::Delete($temporaryPath)
            }
        }
        $baselineDocument = Get-NativeWarningFileDocument `
            -Path $baselinePath `
            -Purpose "Updated native warning baseline"
        $newWarnings = @()
        $resolvedWarnings = @()
    }
    elseif ($newWarnings.Count -gt 0) {
        throw "Native warning budget failed with $($newWarnings.Count) new fingerprint(s): " +
            ($newWarnings -join "; ")
    }

    return [pscustomobject]([ordered]@{
        schemaVersion = 1
        coverageIds = [string[]]@($CoverageIds)
        baseline = [pscustomobject]([ordered]@{
            path = $baselinePath
            bytes = [long]$baselineDocument.bytes
            sha256 = [string]$baselineDocument.sha256
        })
        logs = $logRecords.ToArray()
        emittedWarningLines = $emittedNativeLines
        fingerprints = $actual
        resolvedFingerprints = [string[]]$resolvedWarnings
    })
}

function Write-NativeWarningBaselineSummary {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result
    )

    Write-Host ""
    Write-Host "=== Native Win32/x64 warning budget ==="
    Write-Host "Covered builds: $(@($Result.coverageIds).Count)"
    Write-Host "Emitted native warning lines: $($Result.emittedWarningLines)"
    Write-Host "Distinct current fingerprints: $(@($Result.fingerprints).Count)"
    if (@($Result.resolvedFingerprints).Count -gt 0) {
        Write-Host (
            "Resolved since baseline ($(@($Result.resolvedFingerprints).Count)); " +
            "baseline may be pruned:")
        @($Result.resolvedFingerprints) |
            ForEach-Object { Write-Host "  - $_" }
    }
    Write-Host "Native Win32/x64 warning budget PASS: no new warning fingerprints."
}
