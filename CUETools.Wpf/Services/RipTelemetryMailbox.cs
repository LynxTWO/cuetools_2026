using System;
using System.Threading;
using CUETools.Codecs;

namespace CUETools.Wpf.Services;

/// <summary>
/// A bounded single-producer/single-consumer mailbox for best-effort rip visualization.
/// Every sample array is allocated up front. A full mailbox drops telemetry immediately;
/// it never delays or changes the audio read that produced it.
/// </summary>
public sealed class RipTelemetryMailbox
{
    public const int DefaultSlotCount = 4;
    public const int DefaultSampleCapacity = 16384;

    private sealed class Slot
    {
        internal readonly float[] Samples;
        internal int SampleCount;
        internal double LevelL;
        internal double LevelR;

        internal Slot(int sampleCapacity)
        {
            Samples = new float[sampleCapacity];
        }
    }

    private readonly Slot[] _slots;
    private long _writeSequence;
    private long _readSequence;
    private long _droppedCount;
    private bool _consumerHasFrame;

    public RipTelemetryMailbox(
        int slotCount = DefaultSlotCount,
        int sampleCapacity = DefaultSampleCapacity)
    {
        if (slotCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(slotCount));
        if (sampleCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCapacity));

        _slots = new Slot[slotCount];
        for (int i = 0; i < _slots.Length; i++)
            _slots[i] = new Slot(sampleCapacity);
        SampleCapacity = sampleCapacity;
    }

    public int SlotCount => _slots.Length;
    public int SampleCapacity { get; }
    public long PublishedCount => Volatile.Read(ref _writeSequence);
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public int PendingCount
    {
        get
        {
            long pending =
                Volatile.Read(ref _writeSequence) -
                Volatile.Read(ref _readSequence);
            if (pending <= 0)
                return 0;
            return pending >= _slots.Length
                ? _slots.Length
                : (int)pending;
        }
    }

    /// <summary>
    /// Copies one already-byte-backed stereo PCM read into the next preallocated slot. The
    /// capacity check happens before the buffer representation is inspected, and
    /// <see cref="AudioBuffer.TryGetExistingBytes"/> never materializes another representation.
    /// A source that is not already byte-backed is dropped rather than allocating on the producer.
    /// </summary>
    public bool TryPublish(
        AudioBuffer source,
        int frameCount)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (frameCount < 0 || frameCount > source.Length)
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (source.PCM.ChannelCount < 2)
            throw new ArgumentException(
                "Stereo telemetry requires at least two channels.",
                nameof(source));
        if (source.PCM.BitsPerSample != 16)
            throw new ArgumentException(
                "Byte-backed rip telemetry requires 16-bit PCM.",
                nameof(source));

        long write = _writeSequence;
        if (write - Volatile.Read(ref _readSequence) >= _slots.Length)
        {
            Interlocked.Increment(ref _droppedCount);
            return false;
        }
        if (!source.TryGetExistingBytes(out byte[] bytes))
        {
            Interlocked.Increment(ref _droppedCount);
            return false;
        }

        int blockAlign = source.PCM.BlockAlign;
        if ((long)frameCount * blockAlign > bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(frameCount));

        Slot slot = _slots[(int)(write % _slots.Length)];
        const double FullScale = 32768.0;
        const float InverseScale = 1.0f / 32768.0f;

        int rmsStart = Math.Max(0, frameCount - 8192);
        double sumL = 0;
        double sumR = 0;
        int rmsCount = 0;
        for (int i = rmsStart; i < frameCount; i += 2)
        {
            int offset = i * blockAlign;
            short left = unchecked((short)(
                bytes[offset] |
                (bytes[offset + 1] << 8)));
            short right = unchecked((short)(
                bytes[offset + 2] |
                (bytes[offset + 3] << 8)));
            sumL += (double)left * left;
            sumR += (double)right * right;
            rmsCount++;
        }

        slot.LevelL =
            rmsCount > 0 ? Math.Sqrt(sumL / rmsCount) / FullScale : 0;
        slot.LevelR =
            rmsCount > 0 ? Math.Sqrt(sumR / rmsCount) / FullScale : 0;

        int sampleCount = Math.Min(SampleCapacity, frameCount);
        for (int i = 0; i < sampleCount; i++)
        {
            int offset = i * blockAlign;
            short left = unchecked((short)(
                bytes[offset] |
                (bytes[offset + 1] << 8)));
            short right = unchecked((short)(
                bytes[offset + 2] |
                (bytes[offset + 3] << 8)));
            slot.Samples[i] =
                ((left + right) * 0.5f) * InverseScale;
        }
        slot.SampleCount = sampleCount;

        Volatile.Write(ref _writeSequence, write + 1);
        return true;
    }

    /// <summary>
    /// Copies one stereo PCM read into the next preallocated slot. This is the producer path:
    /// it performs no waits and returns false without touching a slot when the mailbox is full.
    /// </summary>
    public bool TryPublish(
        int[,] source,
        int frameCount,
        int bitsPerSample)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (frameCount < 0 || frameCount > source.GetLength(0))
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        if (source.GetLength(1) < 2)
            throw new ArgumentException(
                "Stereo telemetry requires at least two channels.",
                nameof(source));
        if (bitsPerSample <= 0 || bitsPerSample > 32)
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample));

        long write = _writeSequence;
        if (write - Volatile.Read(ref _readSequence) >= _slots.Length)
        {
            Interlocked.Increment(ref _droppedCount);
            return false;
        }

        Slot slot = _slots[(int)(write % _slots.Length)];
        double fullScale = 1L << (bitsPerSample - 1);

        int rmsStart = Math.Max(0, frameCount - 8192);
        double sumL = 0;
        double sumR = 0;
        int rmsCount = 0;
        for (int i = rmsStart; i < frameCount; i += 2)
        {
            int left = source[i, 0];
            int right = source[i, 1];
            sumL += (double)left * left;
            sumR += (double)right * right;
            rmsCount++;
        }

        slot.LevelL =
            rmsCount > 0 ? Math.Sqrt(sumL / rmsCount) / fullScale : 0;
        slot.LevelR =
            rmsCount > 0 ? Math.Sqrt(sumR / rmsCount) / fullScale : 0;

        int sampleCount = Math.Min(SampleCapacity, frameCount);
        float inverseScale = (float)(1.0 / fullScale);
        for (int i = 0; i < sampleCount; i++)
        {
            slot.Samples[i] =
                (float)(((double)source[i, 0] + source[i, 1]) * 0.5) *
                inverseScale;
        }
        slot.SampleCount = sampleCount;

        // Publish only after every field and sample is complete. The consumer does not
        // advance _readSequence until it has copied the acquired slot.
        Volatile.Write(ref _writeSequence, write + 1);
        return true;
    }

    public bool TryAcquire(out RipTelemetryFrame frame)
    {
        if (_consumerHasFrame)
            throw new InvalidOperationException(
                "Release the acquired telemetry frame before acquiring another.");

        long read = _readSequence;
        if (read >= Volatile.Read(ref _writeSequence))
        {
            frame = default;
            return false;
        }

        Slot slot = _slots[(int)(read % _slots.Length)];
        frame = new RipTelemetryFrame(
            this,
            read,
            slot.Samples,
            slot.SampleCount,
            slot.LevelL,
            slot.LevelR);
        _consumerHasFrame = true;
        return true;
    }

    public void Release(in RipTelemetryFrame frame)
    {
        if (!_consumerHasFrame ||
            !ReferenceEquals(frame.Owner, this) ||
            frame.Sequence != _readSequence)
        {
            throw new InvalidOperationException(
                "The telemetry frame does not belong to the current acquisition.");
        }

        Volatile.Write(ref _readSequence, _readSequence + 1);
        _consumerHasFrame = false;
    }
}

public readonly struct RipTelemetryFrame
{
    private readonly float[]? _samples;

    internal RipTelemetryFrame(
        RipTelemetryMailbox owner,
        long sequence,
        float[] samples,
        int sampleCount,
        double levelL,
        double levelR)
    {
        Owner = owner;
        Sequence = sequence;
        _samples = samples;
        SampleCount = sampleCount;
        LevelL = levelL;
        LevelR = levelR;
    }

    internal RipTelemetryMailbox? Owner { get; }
    internal float[]? SampleBuffer => _samples;
    public long Sequence { get; }
    public int SampleCount { get; }
    public double LevelL { get; }
    public double LevelR { get; }
    public ReadOnlySpan<float> Samples =>
        _samples == null
            ? ReadOnlySpan<float>.Empty
            : _samples.AsSpan(0, SampleCount);

    public void CopySamplesTo(float[] destination)
    {
        if (destination == null)
            throw new ArgumentNullException(nameof(destination));
        if (destination.Length < SampleCount)
            throw new ArgumentException(
                "The destination is smaller than the telemetry frame.",
                nameof(destination));
        if (_samples != null)
            Array.Copy(_samples, destination, SampleCount);
    }
}

/// <summary>
/// A UI-owned copy of a mailbox slot. Two instances are alternated so WPF observes a
/// property change without retaining a producer-owned slot after it is released.
/// </summary>
public sealed class RipTelemetryDisplayFrame
{
    private readonly float[] _samples;

    public RipTelemetryDisplayFrame(
        int sampleCapacity = RipTelemetryMailbox.DefaultSampleCapacity)
    {
        if (sampleCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCapacity));
        _samples = new float[sampleCapacity];
    }

    public int SampleCount { get; private set; }
    internal float[] Samples => _samples;

    internal void CopyFrom(in RipTelemetryFrame frame)
    {
        frame.CopySamplesTo(_samples);
        SampleCount = frame.SampleCount;
    }
}
