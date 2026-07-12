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
    private const int WindowSize = 16384;  // contiguous frames handed to the codec scope per read

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
                double full = 1 << (buffer.PCM.BitsPerSample - 1);
                // RMS (average power) over a short recent window, not peak over the whole 1.5s
                // buffer: peak pins near full-scale for any loud music, so the needle would sit
                // frozen at the top. RMS reflects loudness and actually moves with the music.
                int start = Math.Max(0, n - 8192);   // most recent ~186ms of the read
                double sumL = 0, sumR = 0; int cnt = 0;
                for (int i = start; i < n; i += 2)
                {
                    int a = s[i, 0]; int b = s[i, 1];
                    sumL += (double)a * a; sumR += (double)b * b; cnt++;
                }
                double rmsL = cnt > 0 ? Math.Sqrt(sumL / cnt) / full : 0;
                double rmsR = cnt > 0 ? Math.Sqrt(sumR / cnt) / full : 0;
                _onLevels(rmsL, rmsR);

                // Hand the codec scope a big chunk of CONSECUTIVE mono samples (not decimated). We
                // deliver a large slice, not a tiny snippet, so the scope has real audio to keep
                // scrolling through during the long gaps between reads in secure mode (a Read() can
                // block for seconds re-reading a hard sector). Light throttle (~20ms) so the burst
                // reads - the ripper drains its buffer in ~3ms chunks - do not flood the dispatcher.
                if (_onSamples != null && n > 0 && (DateTime.UtcNow - _lastPush).TotalMilliseconds >= 20)
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
