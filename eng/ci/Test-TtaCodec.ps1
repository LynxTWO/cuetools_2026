[CmdletBinding()]
param(
    [string]$MSBuildPath,
    [switch]$Worker,
    [ValidateSet("x64", "x86")]
    [string]$Architecture,
    [string]$WorkRoot,
    [string]$FfmpegPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Resolve-MSBuild {
    if (-not [string]::IsNullOrWhiteSpace($MSBuildPath)) {
        $candidate = [IO.Path]::GetFullPath($MSBuildPath)
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Specified MSBuild does not exist: $candidate"
        }
        return $candidate
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} (
        "Microsoft Visual Studio\Installer\vswhere.exe")
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        throw "vswhere.exe was not found; pass -MSBuildPath explicitly."
    }
    $candidate = @(
        & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
            -find "MSBuild\**\Bin\MSBuild.exe"
    ) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($candidate) -or
        -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw "Visual Studio MSBuild was not found."
    }
    return [IO.Path]::GetFullPath($candidate)
}

function New-PcmBytes(
    [int]$BitsPerSample,
    [int]$Channels,
    [int]$Frames) {
    $bytesPerSample = [int]($BitsPerSample / 8)
    $bytes = New-Object byte[] ($Frames * $Channels * $bytesPerSample)
    $offset = 0
    for ($frame = 0; $frame -lt $Frames; $frame++) {
        for ($channel = 0; $channel -lt $Channels; $channel++) {
            if ($BitsPerSample -eq 16) {
                $value = [int16](
                    ((($frame * 7919 + $channel * 1049) % 65521) - 32760))
                $sample = [BitConverter]::GetBytes($value)
                $bytes[$offset] = $sample[0]
                $bytes[$offset + 1] = $sample[1]
                $offset += 2
            }
            elseif ($BitsPerSample -eq 24) {
                $value = [int](
                    ((($frame * 104729 + $channel * 8191) % 16777199) -
                        8388590))
                $bytes[$offset] = [byte]($value -band 0xff)
                $bytes[$offset + 1] = [byte](($value -shr 8) -band 0xff)
                $bytes[$offset + 2] = [byte](($value -shr 16) -band 0xff)
                $offset += 3
            }
            else {
                throw "Unsupported TTA test bit depth: $BitsPerSample"
            }
        }
    }
    return ,$bytes
}

function New-ObjectWithArguments(
    [Type]$Type,
    [Type[]]$ParameterTypes,
    [object[]]$Arguments) {
    $constructor = $Type.GetConstructor($ParameterTypes)
    if ($null -eq $constructor) {
        throw "Required constructor was not found: $($Type.FullName)"
    }
    return $constructor.Invoke($Arguments)
}

function New-ArgumentArray([int]$Length) {
    return New-Object object[] $Length
}

function Invoke-TtaWorker {
    if ([string]::IsNullOrWhiteSpace($Architecture) -or
        [string]::IsNullOrWhiteSpace($WorkRoot) -or
        [string]::IsNullOrWhiteSpace($FfmpegPath)) {
        throw "TTA worker requires Architecture, WorkRoot, and FfmpegPath."
    }
    if (($Architecture -eq "x64") -ne [Environment]::Is64BitProcess) {
        throw (
            "TTA worker process architecture mismatch. Requested $Architecture; " +
            "Is64BitProcess=$([Environment]::Is64BitProcess).")
    }

    $workerRoot = [IO.Path]::GetFullPath(
        (Join-Path $WorkRoot $Architecture))
    [void][IO.Directory]::CreateDirectory($workerRoot)
    $platformDirectory = if ($Architecture -eq "x64") { "x64" } else { "win32" }
    $managedRoot = Join-Path $repoRoot "bin\Release\net47"
    $codecAssembly = Join-Path $managedRoot "CUETools.Codecs.dll"
    $jsonAssembly = Join-Path $managedRoot "Newtonsoft.Json.dll"
    $ttaAssembly = Join-Path $managedRoot (
        "plugins\$platformDirectory\CUETools.Codecs.TTA.dll")
    foreach ($path in @($codecAssembly, $jsonAssembly, $ttaAssembly)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required TTA worker assembly does not exist: $path"
        }
    }

    $assemblyRoot = Join-Path $workerRoot "assembly"
    [void][IO.Directory]::CreateDirectory($assemblyRoot)
    foreach ($path in @($codecAssembly, $jsonAssembly, $ttaAssembly)) {
        Copy-Item -LiteralPath $path -Destination $assemblyRoot
    }
    [void][Reflection.Assembly]::LoadFrom(
        (Join-Path $assemblyRoot "Newtonsoft.Json.dll"))
    $codecs = [Reflection.Assembly]::LoadFrom(
        (Join-Path $assemblyRoot "CUETools.Codecs.dll"))
    $tta = [Reflection.Assembly]::LoadFrom(
        (Join-Path $assemblyRoot "CUETools.Codecs.TTA.dll"))

    $pcmType = $codecs.GetType("CUETools.Codecs.AudioPCMConfig", $true)
    $speakerConfigType = $codecs.GetType(
        "CUETools.Codecs.AudioPCMConfig+SpeakerConfig",
        $true)
    $bufferType = $codecs.GetType("CUETools.Codecs.AudioBuffer", $true)
    $fingerprintType = $codecs.GetType(
        "CUETools.Codecs.LosslessPcmFingerprint",
        $true)
    $encoderSettingsType = $tta.GetType(
        "CUETools.Codecs.TTA.EncoderSettings",
        $true)
    $encoderType = $tta.GetType("CUETools.Codecs.TTA.AudioEncoder", $true)
    $decoderSettingsType = $tta.GetType(
        "CUETools.Codecs.TTA.DecoderSettings",
        $true)
    $decoderType = $tta.GetType("CUETools.Codecs.TTA.AudioDecoder", $true)
    $streamType = [IO.Stream]

    function New-Pcm([int]$Bits, [int]$Channels, [int]$Rate) {
        $arguments = New-ArgumentArray 4
        $arguments[0] = $Bits
        $arguments[1] = $Channels
        $arguments[2] = $Rate
        $arguments[3] = [Enum]::ToObject($speakerConfigType, 0)
        return New-ObjectWithArguments `
            $pcmType `
            @([int], [int], [int], $speakerConfigType) `
            $arguments
    }

    function New-Buffer($Pcm, [byte[]]$Bytes, [int]$Frames) {
        $arguments = New-ArgumentArray 3
        $arguments[0] = $Pcm
        $arguments[1] = $Bytes
        $arguments[2] = $Frames
        return New-ObjectWithArguments `
            $bufferType `
            @($pcmType, [byte[]], [int]) `
            $arguments
    }

    function New-Encoder($Settings, [string]$Path) {
        $arguments = New-ArgumentArray 3
        $arguments[0] = $Settings
        $arguments[1] = $Path
        $arguments[2] = $null
        return New-ObjectWithArguments `
            $encoderType `
            @($encoderSettingsType, [string], $streamType) `
            $arguments
    }

    function New-Decoder([string]$Path) {
        $settings = [Activator]::CreateInstance($decoderSettingsType)
        $arguments = New-ArgumentArray 3
        $arguments[0] = $settings
        $arguments[1] = $Path
        $arguments[2] = $null
        return New-ObjectWithArguments `
            $decoderType `
            @($decoderSettingsType, [string], $streamType) `
            $arguments
    }

    function Read-TtaPcm([string]$Path) {
        $decoder = New-Decoder $Path
        $output = New-Object IO.MemoryStream
        try {
            $arguments = New-ArgumentArray 2
            $arguments[0] = $decoder.PCM
            $arguments[1] = 4096
            $buffer = New-ObjectWithArguments `
                $bufferType `
                @($pcmType, [int]) `
                $arguments
            while (($read = $decoder.Read($buffer, 4096)) -gt 0) {
                $output.Write($buffer.Bytes, 0, $buffer.ByteLength)
            }
            return ,$output.ToArray()
        }
        finally {
            $output.Dispose()
            $decoder.Close()
        }
    }

    $cases = @(
        [pscustomobject]@{
            Name = "stereo16"
            Bits = 16
            Channels = 2
            Rate = 44100
            Frames = 10007
        },
        [pscustomobject]@{
            Name = "multichannel24"
            Bits = 24
            Channels = 6
            Rate = 48000
            Frames = 4099
        })
    $caseResults = New-Object "Collections.Generic.List[object]"
    foreach ($case in $cases) {
        $pcm = New-Pcm $case.Bits $case.Channels $case.Rate
        $sourceBytes = New-PcmBytes $case.Bits $case.Channels $case.Frames
        $sourcePath = Join-Path $workerRoot "$($case.Name).source.raw"
        [IO.File]::WriteAllBytes($sourcePath, $sourceBytes)
        $outputPath = Join-Path $workerRoot "$($case.Name).tta"
        $settings = [Activator]::CreateInstance($encoderSettingsType)
        $settings.PCM = $pcm
        $encoder = New-Encoder $settings $outputPath
        $encoder.FinalSampleCount = [int64]$case.Frames
        $inputBuffer = New-Buffer $pcm $sourceBytes $case.Frames
        $fingerprint = [Activator]::CreateInstance($fingerprintType)
        try {
            $fingerprint.Append($inputBuffer)
            $fingerprintHex = (
                [BitConverter]::ToString($fingerprint.Complete())
            ).Replace("-", "").ToLowerInvariant()
        }
        finally { $fingerprint.Dispose() }
        $sourceHashBeforeEncode = Get-Sha256 $sourcePath
        if ($fingerprintHex -ne $sourceHashBeforeEncode) {
            $bufferBytesPath = Join-Path $workerRoot "$($case.Name).buffer.raw"
            [IO.File]::WriteAllBytes($bufferBytesPath, $inputBuffer.Bytes)
            throw (
                "TTA $($case.Name) input fingerprint does not match its PCM bytes. " +
                "source=$sourceHashBeforeEncode fingerprint=$fingerprintHex " +
                "buffer=$(Get-Sha256 $bufferBytesPath) " +
                "sourceBytes=$($sourceBytes.Length) bufferBytes=$($inputBuffer.ByteLength)")
        }
        $encoder.Write($inputBuffer)
        $encoder.Close()
        $encoder.Close()

        $writeAfterCloseFailed = $false
        try {
            $encoder.Write((New-Buffer $pcm $sourceBytes 1))
        }
        catch {
            $writeAfterCloseFailed = $_.Exception.ToString() -match "already closed"
        }
        if (-not $writeAfterCloseFailed) {
            throw "TTA $($case.Name) accepted a write after Close."
        }

        $decodedBytes = Read-TtaPcm $outputPath
        $decodedPath = Join-Path $workerRoot "$($case.Name).decoded.raw"
        [IO.File]::WriteAllBytes($decodedPath, $decodedBytes)
        $sourceHash = Get-Sha256 $sourcePath
        $decodedHash = Get-Sha256 $decodedPath
        if ($sourceHash -ne $decodedHash) {
            throw "TTA $($case.Name) managed decode did not reproduce the source PCM."
        }

        $ffmpegOutputPath = Join-Path $workerRoot "$($case.Name).ffmpeg.raw"
        $codec = if ($case.Bits -eq 16) { "pcm_s16le" } else { "pcm_s24le" }
        $rawFormat = if ($case.Bits -eq 16) { "s16le" } else { "s24le" }
        & $FfmpegPath -hide_banner -loglevel error -xerror -y `
            -i $outputPath -map_metadata -1 -vn -acodec $codec `
            -f $rawFormat $ffmpegOutputPath
        if ($LASTEXITCODE -ne 0) {
            throw "ffmpeg rejected TTA $($case.Name)."
        }
        if ((Get-Sha256 $ffmpegOutputPath) -ne $sourceHash) {
            throw "ffmpeg TTA $($case.Name) decode did not reproduce the source PCM."
        }

        $caseResults.Add([pscustomobject]@{
            name = $case.Name
            sourceSha256 = $sourceHash
            ttaSha256 = Get-Sha256 $outputPath
            ttaBytes = (Get-Item -LiteralPath $outputPath).Length
        })
    }

    $preservePath = Join-Path $workerRoot "preserve-existing.tta"
    $preservedBytes = [Text.Encoding]::UTF8.GetBytes(
        "existing destination must survive failed TTA finalization")
    [IO.File]::WriteAllBytes($preservePath, $preservedBytes)
    $preservedHash = Get-Sha256 $preservePath
    $preservePcm = New-Pcm 16 2 44100
    $preserveSettings = [Activator]::CreateInstance($encoderSettingsType)
    $preserveSettings.PCM = $preservePcm
    $preserveEncoder = New-Encoder $preserveSettings $preservePath
    $preserveEncoder.FinalSampleCount = 11
    $preserveSource = New-PcmBytes 16 2 10
    $preserveEncoder.Write((New-Buffer $preservePcm $preserveSource 10))
    $finalizeFailed = $false
    try { $preserveEncoder.Close() }
    catch {
        $finalizeFailed =
            $_.Exception.ToString() -match "expected sample count"
    }
    if (-not $finalizeFailed) {
        throw "TTA sample-count mismatch did not fail finalization."
    }
    if ((Get-Sha256 $preservePath) -ne $preservedHash) {
        throw "Failed TTA finalization changed an existing destination."
    }
    $workResidue = @(
        Get-ChildItem -LiteralPath $workerRoot -Force -File |
            Where-Object {
                $_.Name -like ".preserve-existing.cuetools-lossless-*"
            })
    if ($workResidue.Count -ne 0) {
        throw "Failed TTA finalization left an owned work file."
    }

    $eightBitRejected = $false
    try {
        $settings = [Activator]::CreateInstance($encoderSettingsType)
        $settings.PCM = New-Pcm 8 2 44100
        [void](New-Encoder $settings (Join-Path $workerRoot "invalid-8bit.tta"))
    }
    catch {
        $eightBitRejected = $_.Exception.ToString() -match "16..24"
    }
    if (-not $eightBitRejected) {
        throw "TTA encoder did not reject unsupported 8-bit input."
    }

    $oversizeRejected = $false
    try {
        $settings = [Activator]::CreateInstance($encoderSettingsType)
        $settings.PCM = New-Pcm 16 2 44100
        $encoder = New-Encoder $settings (Join-Path $workerRoot "oversize.tta")
        $encoder.FinalSampleCount = [int64][uint32]::MaxValue + 1
    }
    catch {
        $oversizeRejected = $_.Exception.ToString() -match "2\^32-1"
    }
    if (-not $oversizeRejected) {
        throw "TTA encoder did not reject an unrepresentable sample count."
    }

    $result = [pscustomobject]@{
        architecture = $Architecture
        process64Bit = [Environment]::Is64BitProcess
        cases = $caseResults.ToArray()
        preservation = "passed"
        stateContract = "passed"
    }
    $resultPath = Join-Path $workerRoot "result.json"
    [IO.File]::WriteAllText(
        $resultPath,
        ($result | ConvertTo-Json -Depth 8),
        (New-Object Text.UTF8Encoding($false)))
    Write-Host "TTA $Architecture runtime checks passed."
}

if ($Worker) {
    Invoke-TtaWorker
    exit 0
}

$tempBase = [IO.Path]::GetTempPath()
$tempRoot = Join-Path $tempBase (
    "cuetools-tta-codec-" + [Guid]::NewGuid().ToString("N"))
try {
    [void][IO.Directory]::CreateDirectory($tempRoot)
    $msbuild = Resolve-MSBuild
    $solutionDirectory = $repoRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    foreach ($platform in @("x64", "Win32")) {
        & $msbuild `
            (Join-Path $repoRoot "CUETools.Codecs.TTA\CUETools.Codecs.TTA.vcxproj") `
            /t:Rebuild `
            /p:Configuration=Release `
            "/p:Platform=$platform" `
            "/p:SolutionDir=$solutionDirectory" `
            /m:1 `
            /nologo `
            /v:minimal
        if ($LASTEXITCODE -ne 0) {
            throw "TTA $platform Release build failed."
        }
    }

    $ffmpeg = if (-not [string]::IsNullOrWhiteSpace($FfmpegPath)) {
        [IO.Path]::GetFullPath($FfmpegPath)
    }
    else {
        $command = Get-Command ffmpeg.exe -ErrorAction Stop
        [IO.Path]::GetFullPath($command.Source)
    }
    if (-not (Test-Path -LiteralPath $ffmpeg -PathType Leaf)) {
        throw "ffmpeg does not exist: $ffmpeg"
    }

    $windowsPowerShellRoot = Join-Path $env:WINDIR (
        "System32\WindowsPowerShell\v1.0\powershell.exe")
    $windowsPowerShell32 = Join-Path $env:WINDIR (
        "SysWOW64\WindowsPowerShell\v1.0\powershell.exe")
    foreach ($workerDefinition in @(
        [pscustomobject]@{
            Architecture = "x64"
            PowerShell = $windowsPowerShellRoot
        },
        [pscustomobject]@{
            Architecture = "x86"
            PowerShell = $windowsPowerShell32
        })) {
        if (-not (Test-Path -LiteralPath $workerDefinition.PowerShell -PathType Leaf)) {
            throw "Required Windows PowerShell host is missing: $($workerDefinition.PowerShell)"
        }
        & $workerDefinition.PowerShell `
            -NoLogo `
            -NoProfile `
            -ExecutionPolicy Bypass `
            -File $PSCommandPath `
            -Worker `
            -Architecture $workerDefinition.Architecture `
            -WorkRoot $tempRoot `
            -FfmpegPath $ffmpeg
        if ($LASTEXITCODE -ne 0) {
            throw "TTA $($workerDefinition.Architecture) runtime worker failed."
        }
    }

    $x64 = Get-Content -LiteralPath (Join-Path $tempRoot "x64\result.json") -Raw |
        ConvertFrom-Json
    $x86 = Get-Content -LiteralPath (Join-Path $tempRoot "x86\result.json") -Raw |
        ConvertFrom-Json
    if (@($x64.cases).Count -ne @($x86.cases).Count) {
        throw "TTA architecture workers returned different case counts."
    }
    for ($index = 0; $index -lt @($x64.cases).Count; $index++) {
        $left = @($x64.cases)[$index]
        $right = @($x86.cases)[$index]
        if ($left.name -ne $right.name -or
            $left.sourceSha256 -ne $right.sourceSha256 -or
            $left.ttaSha256 -ne $right.ttaSha256 -or
            [int64]$left.ttaBytes -ne [int64]$right.ttaBytes) {
            throw "TTA output differs between x64 and x86 for case '$($left.name)'."
        }
    }
    Write-Host (
        "TTA codec checks passed: 2 architectures, " +
        "$(@($x64.cases).Count) PCM cases, managed+ffmpeg decode, " +
        "cross-architecture identity, state and failure-preservation contracts.")
}
finally {
    $tempPrefix = $tempBase.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $tempLeaf = [IO.Path]::GetFileName($tempRoot)
    if (-not $tempRoot.StartsWith(
            $tempPrefix,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not $tempLeaf.StartsWith(
            "cuetools-tta-codec-",
            [StringComparison]::Ordinal)) {
        throw "Refusing to clean an unexpected TTA test path: $tempRoot"
    }
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
