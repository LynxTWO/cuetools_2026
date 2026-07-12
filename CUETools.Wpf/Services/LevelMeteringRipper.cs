using System;
using System.Collections;
using CUETools.CDImage;
using CUETools.Codecs;
using CUETools.Ripper;

namespace CUETools.Wpf.Services;

/// <summary>
/// Wraps a real <see cref="ICDRipper"/> and taps the audio as CUESheet pulls it, computing the
/// true per-channel peak of each buffer. That drives the VU meter with real disc levels - no
/// engine change, because CUESheet already reads through this interface. Everything else is
/// delegated to the inner ripper unchanged.
/// </summary>
public sealed class LevelMeteringRipper : ICDRipper
{
    private const int WindowSize = 320;    // contiguous frames handed to the codec scope

    private readonly ICDRipper _inner;
    private readonly Action<double, double> _onLevels;
    private readonly Action<float[]>? _onSamples;
    private DateTime _lastPush = DateTime.MinValue;

    public LevelMeteringRipper(ICDRipper inner, Action<double, double> onLevels, Action<float[]>? onSamples = null)
    {
        _inner = inner;
        _onLevels = onLevels;
        _onSamples = onSamples;
    }

    public int Read(AudioBuffer buffer, int maxLength)
    {
        int n = _inner.Read(buffer, maxLength);
        try
        {
            if (n > 0 && buffer.PCM.ChannelCount >= 2)
            {
                int[,] s = buffer.Samples;
                int peakL = 0, peakR = 0;
                for (int i = 0; i < n; i += 4)   // every 4th frame is plenty for a meter
                {
                    int a = s[i, 0]; if (a < 0) a = -a; if (a > peakL) peakL = a;
                    int b = s[i, 1]; if (b < 0) b = -b; if (b > peakR) peakR = b;
                }
                double full = 1 << (buffer.PCM.BitsPerSample - 1);
                _onLevels(peakL / full, peakR / full);

                // Hand the codec scope a window of CONSECUTIVE mono samples (not decimated) so the
                // predictor it runs sees real sample-to-sample correlation - the residual it draws
                // is then the true prediction error, not decoration. Throttled to ~40/s.
                if (_onSamples != null && (DateTime.UtcNow - _lastPush).TotalMilliseconds >= 25)
                {
                    int m = Math.Min(WindowSize, n);
                    var win = new float[m];
                    float inv = (float)(1.0 / full);
                    for (int i = 0; i < m; i++) win[i] = ((s[i, 0] + s[i, 1]) * 0.5f) * inv;
                    _onSamples(win);
                    _lastPush = DateTime.UtcNow;
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

    public event EventHandler<ReadProgressArgs> ReadProgress
    {
        add => _inner.ReadProgress += value;
        remove => _inner.ReadProgress -= value;
    }

    public void Dispose() => _inner.Dispose();
}
