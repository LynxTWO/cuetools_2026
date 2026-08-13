using System;
using System.Collections;
using System.Diagnostics;
using CUETools.CDImage;
using CUETools.Codecs;
using CUETools.Ripper;

namespace CUETools.Wpf.Services;

/// <summary>
/// Wraps a real <see cref="ICDRipper"/> and taps the audio as CUESheet pulls it. Telemetry is
/// copied into a preallocated bounded mailbox; a stalled UI can drop visualization, but it can
/// never block or change the underlying audio read. Everything else delegates unchanged.
/// </summary>
public sealed class LevelMeteringRipper : ICDRipper
{
    private readonly ICDRipper _inner;
    private readonly RipTelemetryMailbox _telemetry;
    private readonly long _minimumPublishIntervalTicks;
    private long _lastPushTimestamp;

    public LevelMeteringRipper(
        ICDRipper inner,
        RipTelemetryMailbox telemetry)
        : this(
            inner,
            telemetry,
            Math.Max(1, Stopwatch.Frequency / 50))
    {
    }

    internal LevelMeteringRipper(
        ICDRipper inner,
        RipTelemetryMailbox telemetry,
        long minimumPublishIntervalTicks)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _telemetry =
            telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        if (minimumPublishIntervalTicks < 0)
            throw new ArgumentOutOfRangeException(
                nameof(minimumPublishIntervalTicks));
        _minimumPublishIntervalTicks = minimumPublishIntervalTicks;
    }

    public int Read(AudioBuffer buffer, int maxLength)
    {
        int n = _inner.Read(buffer, maxLength);
        try
        {
            if (n > 0 &&
                buffer.PCM.ChannelCount >= 2 &&
                buffer.PCM.BitsPerSample == 16)
            {
                long now = Stopwatch.GetTimestamp();
                if (_minimumPublishIntervalTicks == 0 ||
                    now - _lastPushTimestamp >=
                    _minimumPublishIntervalTicks)
                {
                    _lastPushTimestamp = now;
                    _telemetry.TryPublish(
                        buffer,
                        n);
                }
            }
        }
        catch { /* metering is best-effort; never disturb the rip */ }
        return n;
    }

    // ---- everything else delegates to the real ripper ----
    public IAudioDecoderSettings Settings => _inner.Settings;
    public AudioPCMConfig PCM => _inner.PCM;
    public string Path => _inner.Path;
    public TimeSpan Duration => _inner.Duration;
    public long Length => _inner.Length;
    public long Position { get => _inner.Position; set => _inner.Position = value; }
    public long Remaining => _inner.Remaining;
    public void Close() => _inner.Close();

    public bool Open(char drive) => _inner.Open(drive);
    public void EjectDisk() => _inner.EjectDisk();
    public void DisableEjectDisc(bool disable) => _inner.DisableEjectDisc(disable);
    public bool DetectGaps() => _inner.DetectGaps();
    public bool GapsDetected => _inner.GapsDetected;
    public CDImageLayout TOC => _inner.TOC;
    public string ARName => _inner.ARName;
    public string EACName => _inner.EACName;
    public int DriveOffset { get => _inner.DriveOffset; set => _inner.DriveOffset = value; }
    public int DriveC2ErrorMode { get => _inner.DriveC2ErrorMode; set => _inner.DriveC2ErrorMode = value; }
    public bool ForceBE { get => _inner.ForceBE; set => _inner.ForceBE = value; }
    public bool ForceD8 { get => _inner.ForceD8; set => _inner.ForceD8 = value; }
    public string RipperVersion => _inner.RipperVersion;
    public string CurrentReadCommand => _inner.CurrentReadCommand;
    public int CorrectionQuality { get => _inner.CorrectionQuality; set => _inner.CorrectionQuality = value; }
    public BitArray FailedSectors => _inner.FailedSectors;
    public byte[] RetryCount => _inner.RetryCount;
    public int CacheDefeatBytes => _inner.CacheDefeatBytes;

    public event EventHandler<ReadProgressArgs> ReadProgress
    {
        add => _inner.ReadProgress += value;
        remove => _inner.ReadProgress -= value;
    }

    public void Dispose() => _inner.Dispose();
}
