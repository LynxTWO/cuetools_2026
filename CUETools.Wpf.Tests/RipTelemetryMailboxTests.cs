using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CUETools.CDImage;
using CUETools.Codecs;
using CUETools.Ripper;
using CUETools.Wpf.Controls;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class RipTelemetryMailboxTests
{
    [TestMethod]
    public void ProducerAllocatesNothingAfterWarmupAcrossThousandsOfReads()
    {
        const int frameCount = 32;
        var inner = new RepeatingRipper(frameCount, 8192, -4096);
        var mailbox = new RipTelemetryMailbox(1, frameCount);
        var metered = new LevelMeteringRipper(
            inner,
            mailbox,
            minimumPublishIntervalTicks: 0);
        var buffer = new AudioBuffer(AudioPCMConfig.RedBook, frameCount);

        for (int i = 0; i < 128; i++)
        {
            Assert.AreEqual(frameCount, metered.Read(buffer, frameCount));
            Assert.IsTrue(mailbox.TryAcquire(out RipTelemetryFrame frame));
            mailbox.Release(frame);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 5000; i++)
        {
            metered.Read(buffer, frameCount);
            mailbox.TryAcquire(out RipTelemetryFrame frame);
            mailbox.Release(frame);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(
            0L,
            allocated,
            "The audio-read telemetry producer allocated after warm-up.");
    }

    [TestMethod]
    public void ColdByteBackedReadDoesNotMaterializeSamplesOrAllocate()
    {
        const int frameCount = 32;

        // Warm the code paths, then measure a distinct AudioBuffer whose lazy int[,] sample
        // representation has never been touched.
        var warmMailbox = new RipTelemetryMailbox(1, frameCount);
        var warmRipper = new LevelMeteringRipper(
            new RepeatingRipper(frameCount, 100, -100),
            warmMailbox,
            minimumPublishIntervalTicks: 0);
        var warmBuffer =
            new AudioBuffer(AudioPCMConfig.RedBook, frameCount);
        warmRipper.Read(warmBuffer, frameCount);
        warmMailbox.TryAcquire(out RipTelemetryFrame warmFrame);
        warmMailbox.Release(warmFrame);

        var mailbox = new RipTelemetryMailbox(1, frameCount);
        var metered = new LevelMeteringRipper(
            new RepeatingRipper(frameCount, 8192, -4096),
            mailbox,
            minimumPublishIntervalTicks: 0);
        var coldBuffer =
            new AudioBuffer(AudioPCMConfig.RedBook, frameCount);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int read = metered.Read(coldBuffer, frameCount);
        bool acquired =
            mailbox.TryAcquire(out RipTelemetryFrame frame);
        mailbox.Release(frame);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(frameCount, read);
        Assert.IsTrue(acquired);
        Assert.AreEqual(0L, allocated);
        Assert.AreEqual(frameCount, frame.SampleCount);
        Assert.AreEqual(0.25, frame.LevelL, 0.0000001);
        Assert.AreEqual(0.125, frame.LevelR, 0.0000001);
        Assert.AreEqual(0.0625f, frame.Samples[0], 0.000001f);
        FieldInfo samples = typeof(AudioBuffer).GetField(
            "samples",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(samples);
        Assert.IsNull(
            samples.GetValue(coldBuffer),
            "Telemetry materialized AudioBuffer.Samples on the producer thread.");
    }

    [TestMethod]
    public void ByteBackedDecoderPreservesSignedLittleEndianChannelsAndRms()
    {
        const int frameCount = 4;
        var mailbox = new RipTelemetryMailbox(1, frameCount);
        var buffer = new AudioBuffer(AudioPCMConfig.RedBook, frameCount);
        var bytes = new byte[frameCount * AudioPCMConfig.RedBook.BlockAlign];
        for (int i = 0; i < frameCount; i++)
        {
            WriteInt16(bytes, i * 4, 32767);
            WriteInt16(bytes, i * 4 + 2, -16384);
        }
        buffer.Prepare(bytes, frameCount);

        Assert.IsTrue(mailbox.TryPublish(buffer, frameCount));
        Assert.IsTrue(mailbox.TryAcquire(out RipTelemetryFrame frame));
        Assert.AreEqual(frameCount, frame.SampleCount);
        Assert.AreEqual(32767.0 / 32768.0, frame.LevelL, 0.0000001);
        Assert.AreEqual(0.5, frame.LevelR, 0.0000001);
        Assert.AreEqual(
            (32767 - 16384) * 0.5f / 32768.0f,
            frame.Samples[0],
            0.000001f);
        mailbox.Release(frame);
    }

    [TestMethod]
    public void StalledConsumerStaysBoundedAndProducerReturnsImmediately()
    {
        var mailbox = new RipTelemetryMailbox(3, 4);
        int[,] samples = ConstantSamples(4, 1000, -1000);
        for (int i = 0; i < mailbox.SlotCount; i++)
            Assert.IsTrue(mailbox.TryPublish(samples, 4, 16));

        var elapsed = Stopwatch.StartNew();
        const int attempts = 100000;
        int rejected = 0;
        for (int i = 0; i < attempts; i++)
        {
            if (!mailbox.TryPublish(samples, 4, 16))
                rejected++;
        }
        elapsed.Stop();

        Assert.AreEqual(attempts, rejected);
        Assert.AreEqual(3, mailbox.PendingCount);
        Assert.AreEqual(attempts, mailbox.DroppedCount);
        Assert.IsTrue(
            elapsed.Elapsed < TimeSpan.FromSeconds(2),
            "A full visualization mailbox blocked the producer.");
    }

    [TestMethod]
    public void AcquiredSlotRemainsImmutableUntilRelease()
    {
        var mailbox = new RipTelemetryMailbox(1, 2);
        int[,] first = ConstantSamples(2, 8192, 8192);
        int[,] second = ConstantSamples(2, -16384, -16384);

        Assert.IsTrue(mailbox.TryPublish(first, 2, 16));
        Assert.IsTrue(mailbox.TryAcquire(out RipTelemetryFrame acquired));
        float original = acquired.Samples[0];

        Assert.IsFalse(mailbox.TryPublish(second, 2, 16));
        Assert.AreEqual(original, acquired.Samples[0]);
        Assert.AreEqual(0.25f, acquired.Samples[0], 0.000001f);

        mailbox.Release(acquired);
        Assert.IsTrue(mailbox.TryPublish(second, 2, 16));
        Assert.IsTrue(mailbox.TryAcquire(out RipTelemetryFrame reused));
        Assert.AreSame(acquired.SampleBuffer, reused.SampleBuffer);
        Assert.AreEqual(-0.5f, reused.Samples[0], 0.000001f);
        mailbox.Release(reused);
    }

    [TestMethod]
    public void FramesPreserveOrderScalingAndReusePreallocatedSlots()
    {
        var mailbox = new RipTelemetryMailbox(2, 4);
        int[,] first = ConstantSamples(4, 16384, -8192);
        int[,] second = ConstantSamples(4, 8192, 8192);
        int[,] third = ConstantSamples(4, -32768, -32768);

        Assert.IsTrue(mailbox.TryPublish(first, 4, 16));
        Assert.IsTrue(mailbox.TryPublish(second, 4, 16));

        Assert.IsTrue(mailbox.TryAcquire(out RipTelemetryFrame frame0));
        Assert.AreEqual(0L, frame0.Sequence);
        Assert.AreEqual(0.5, frame0.LevelL, 0.0000001);
        Assert.AreEqual(0.25, frame0.LevelR, 0.0000001);
        Assert.AreEqual(0.125f, frame0.Samples[0], 0.000001f);
        float[] firstSlot = frame0.SampleBuffer;
        mailbox.Release(frame0);

        Assert.IsTrue(mailbox.TryPublish(third, 4, 16));
        Assert.IsTrue(mailbox.TryAcquire(out RipTelemetryFrame frame1));
        Assert.AreEqual(1L, frame1.Sequence);
        Assert.AreEqual(0.25f, frame1.Samples[0], 0.000001f);
        mailbox.Release(frame1);

        Assert.IsTrue(mailbox.TryAcquire(out RipTelemetryFrame frame2));
        Assert.AreEqual(2L, frame2.Sequence);
        Assert.AreSame(firstSlot, frame2.SampleBuffer);
        Assert.AreEqual(1.0, frame2.LevelL, 0.0000001);
        Assert.AreEqual(-1.0f, frame2.Samples[0], 0.000001f);
        mailbox.Release(frame2);
    }

    [TestMethod]
    public void FullMailboxDropsTelemetryButReturnsTheAudioRead()
    {
        const int frameCount = 4;
        var inner = new RepeatingRipper(frameCount, 2048, -2048);
        var mailbox = new RipTelemetryMailbox(1, frameCount);
        var metered = new LevelMeteringRipper(
            inner,
            mailbox,
            minimumPublishIntervalTicks: 0);
        var buffer = new AudioBuffer(AudioPCMConfig.RedBook, frameCount);

        Assert.AreEqual(frameCount, metered.Read(buffer, frameCount));
        Assert.AreEqual(frameCount, metered.Read(buffer, frameCount));

        Assert.AreEqual(2, inner.ReadCount);
        Assert.AreEqual(1, mailbox.PendingCount);
        Assert.AreEqual(1L, mailbox.DroppedCount);
        Assert.AreEqual(frameCount, buffer.Length);
    }

    [TestMethod]
    [Timeout(10000)]
    public void ConcurrentProducerAndConsumerPreserveOrderAndSlotLifetime()
    {
        const int frameCount = 4;
        const int messageCount = 2000;
        var mailbox = new RipTelemetryMailbox(4, frameCount);
        var cancelled = new CancellationTokenSource();
        Task producer = Task.Run(() =>
        {
            var samples = new int[frameCount, 2];
            for (int sequence = 0;
                 sequence < messageCount &&
                 !cancelled.IsCancellationRequested;
                 sequence++)
            {
                for (int frame = 0; frame < frameCount; frame++)
                {
                    samples[frame, 0] = sequence;
                    samples[frame, 1] = sequence;
                }
                while (!mailbox.TryPublish(
                    samples,
                    frameCount,
                    bitsPerSample: 16))
                {
                    if (cancelled.IsCancellationRequested)
                        return;
                    Thread.Yield();
                }
            }
        });

        bool ordered = true;
        bool immutable = true;
        bool timedOut = false;
        int consumed = 0;
        var deadline = Stopwatch.StartNew();
        try
        {
            while (consumed < messageCount)
            {
                if (!mailbox.TryAcquire(out RipTelemetryFrame frame))
                {
                    if (deadline.Elapsed > TimeSpan.FromSeconds(5))
                    {
                        timedOut = true;
                        break;
                    }
                    Thread.Yield();
                    continue;
                }

                float before = frame.Samples[0];
                ordered &= frame.Sequence == consumed;
                ordered &= Math.Abs(
                    before - consumed / 32768.0f) < 0.000001f;
                Thread.SpinWait(250);
                immutable &= before == frame.Samples[0];
                mailbox.Release(frame);
                consumed++;
            }
        }
        finally
        {
            cancelled.Cancel();
            producer.Wait(TimeSpan.FromSeconds(2));
            cancelled.Dispose();
        }

        Assert.IsFalse(timedOut);
        Assert.AreEqual(messageCount, consumed);
        Assert.IsTrue(ordered);
        Assert.IsTrue(immutable);
        Assert.IsTrue(producer.IsCompleted);
    }

    [TestMethod]
    [Timeout(10000)]
    public void CodecScopeClearsAudioAndEmaStateAtSessionBoundary()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var mailbox = new RipTelemetryMailbox(1, 4);
                Assert.IsTrue(mailbox.TryPublish(
                    ConstantSamples(4, 8192, 8192),
                    4,
                    16));
                Assert.IsTrue(mailbox.TryAcquire(
                    out RipTelemetryFrame frame));
                var display = new RipTelemetryDisplayFrame(4);
                display.CopyFrom(frame);
                mailbox.Release(frame);

                var scope = new CodecScope
                {
                    Samples = display
                };
                Assert.AreEqual(
                    4L,
                    GetPrivateField<long>(scope, "_ringWrite"));

                scope.Samples = null;

                Assert.AreEqual(
                    0L,
                    GetPrivateField<long>(scope, "_ringWrite"));
                Assert.AreEqual(
                    0.0,
                    GetPrivateField<double>(scope, "_readPos"));
                Assert.AreEqual(
                    16.0,
                    GetPrivateField<double>(scope, "_bitsEma"));
                Assert.AreEqual(
                    1.0,
                    GetPrivateField<double>(scope, "_ratioEma"));
                float[] ring =
                    GetPrivateField<float[]>(scope, "_ring");
                Assert.IsTrue(Array.TrueForAll(
                    ring,
                    sample => sample == 0));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)));
        if (failure != null)
            Assert.Fail(failure.ToString());
    }

    private static int[,] ConstantSamples(
        int frameCount,
        int left,
        int right)
    {
        var result = new int[frameCount, 2];
        for (int i = 0; i < frameCount; i++)
        {
            result[i, 0] = left;
            result[i, 1] = right;
        }
        return result;
    }

    private static void WriteInt16(
        byte[] destination,
        int offset,
        int value)
    {
        short sample = unchecked((short)value);
        destination[offset] = unchecked((byte)sample);
        destination[offset + 1] =
            unchecked((byte)(sample >> 8));
    }

    private static T GetPrivateField<T>(
        object instance,
        string name)
    {
        FieldInfo field = instance.GetType().GetField(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        return (T)field.GetValue(instance)!;
    }

        private sealed class RepeatingRipper : ICDRipper
    {
        private readonly int _frameCount;
        private readonly byte[] _bytes;
        private readonly BitArray _failedSectors = new(0);
        private readonly byte[] _retryCount = Array.Empty<byte>();

        internal RepeatingRipper(
            int frameCount,
            int left,
            int right)
        {
            _frameCount = frameCount;
            _bytes =
                new byte[frameCount * AudioPCMConfig.RedBook.BlockAlign];
            for (int i = 0; i < frameCount; i++)
            {
                RipTelemetryMailboxTests.WriteInt16(
                    _bytes,
                    i * 4,
                    left);
                RipTelemetryMailboxTests.WriteInt16(
                    _bytes,
                    i * 4 + 2,
                    right);
            }
            Settings = new CUETools.Codecs.WAV.DecoderSettings();
        }

        internal int ReadCount { get; private set; }
        public IAudioDecoderSettings Settings { get; }
        public AudioPCMConfig PCM => AudioPCMConfig.RedBook;
        public string Path => "telemetry-test";
        public TimeSpan Duration => TimeSpan.Zero;
        public long Length => _frameCount;
        public long Position { get; set; }
        public long Remaining => _frameCount;

        public int Read(AudioBuffer buffer, int maxLength)
        {
            int count = maxLength < 0
                ? _frameCount
                : Math.Min(_frameCount, maxLength);
            buffer.Prepare(_bytes, count);
            ReadCount++;
            Position += count;
            return count;
        }

        public void Close() { }
        public bool Open(char drive) => true;
        public void EjectDisk() { }
        public void DisableEjectDisc(bool disable) { }
        public bool DetectGaps() => true;
        public bool GapsDetected => true;
        public CDImageLayout TOC => null;
        public string ARName => "telemetry-test";
        public string EACName => "telemetry-test";
        public int DriveOffset { get; set; }
        public int DriveC2ErrorMode { get; set; }
        public bool ForceBE { get; set; }
        public bool ForceD8 { get; set; }
        public string RipperVersion => "test";
        public string CurrentReadCommand => "test";
        public int CorrectionQuality { get; set; }
        public BitArray FailedSectors => _failedSectors;
        public byte[] RetryCount => _retryCount;
        public int CacheDefeatBytes => 0;

        public event EventHandler<ReadProgressArgs> ReadProgress
        {
            add { }
            remove { }
        }

        public void Dispose() { }
    }
}
