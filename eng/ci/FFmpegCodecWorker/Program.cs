using System;
using System.IO;
using System.Text;
using CUETools.Codecs;
using CUETools.Codecs.ffmpegdll;

namespace CUETools.FFmpegCodecWorker
{
    internal static class Program
    {
        private const int Channels = 2;
        private const int SampleRate = 44100;
        private const int FrameCount = 5003;
        private const int SeekFrame = 1234;

        private static int Main()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "cuetools-ffmpeg-codec-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                byte[] callbackFixture = null;
                foreach (int bitsPerSample in new[] { 16, 24 })
                {
                    int[,] expected = CreateSamples(bitsPerSample);
                    byte[] aiff = CreateAiff(expected, bitsPerSample);
                    string fixtureName =
                        "deterministic-" + bitsPerSample + ".aiff";
                    string aiffPath = Path.Combine(tempRoot, fixtureName);
                    File.WriteAllBytes(aiffPath, aiff);

                    DecodeAndVerify(
                        new AudioDecoder(new AiffDecoderSettings(), aiffPath, null),
                        expected,
                        bitsPerSample,
                        bitsPerSample + "-bit path");
                    DecodeAndVerify(
                        new AudioDecoder(
                            new AiffDecoderSettings(),
                            fixtureName,
                            new MemoryStream(aiff, false)),
                        expected,
                        bitsPerSample,
                        bitsPerSample + "-bit stream");
                    if (bitsPerSample == 16)
                        callbackFixture = aiff;
                }
                VerifyCallbackFailure(callbackFixture);

                Console.WriteLine(
                    "FFmpeg codec checks passed: runtime {0}, 16+24-bit, " +
                    "{1} frames each, path+stream decode, nonzero seek replay, " +
                    "callback containment",
                    AudioDecoder.NativeVersion,
                    FrameCount);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
        }

        private static void DecodeAndVerify(
            AudioDecoder decoder,
            int[,] expected,
            int bitsPerSample,
            string sourceKind)
        {
            using (decoder)
            {
                Require(
                    decoder.PCM.BitsPerSample == bitsPerSample,
                    sourceKind + " bit depth");
                Require(decoder.PCM.ChannelCount == Channels, sourceKind + " channels");
                Require(decoder.PCM.SampleRate == SampleRate, sourceKind + " sample rate");

                var buffer = new AudioBuffer(decoder, 257);
                int position = 0;
                int read;
                while ((read = decoder.Read(buffer, 257)) != 0)
                {
                    for (int frame = 0; frame < read; frame++)
                    {
                        for (int channel = 0; channel < Channels; channel++)
                        {
                            Require(
                                buffer.Samples[frame, channel] ==
                                    expected[position + frame, channel],
                                sourceKind + " decoded PCM");
                        }
                    }
                    position += read;
                }

                Require(position == FrameCount, sourceKind + " frame count");
                Require(decoder.Length == FrameCount, sourceKind + " final length");

                decoder.Position = SeekFrame;
                int replayed = decoder.Read(buffer, 31);
                Require(replayed == 31, sourceKind + " seek replay length");
                for (int frame = 0; frame < replayed; frame++)
                {
                    for (int channel = 0; channel < Channels; channel++)
                    {
                        Require(
                            buffer.Samples[frame, channel] ==
                                expected[SeekFrame + frame, channel],
                            sourceKind + " seek replay PCM");
                    }
                }
                Require(
                    decoder.Position == SeekFrame + 31,
                    sourceKind + " seek replay position");
            }

            bool disposedRejected = false;
            try
            {
                decoder.Read(new AudioBuffer(AudioPCMConfig.RedBook, 1), 1);
            }
            catch (ObjectDisposedException)
            {
                disposedRejected = true;
            }
            Require(disposedRejected, sourceKind + " disposed read");
        }

        private static void VerifyCallbackFailure(byte[] aiff)
        {
            var stream = new ThrowingReadStream(aiff);
            bool contained = false;
            try
            {
                new AudioDecoder(
                    new AiffDecoderSettings(),
                    "callback-failure.aiff",
                    stream);
            }
            catch (IOException exception)
            {
                contained =
                    exception.Message.IndexOf(
                        "custom I/O",
                        StringComparison.OrdinalIgnoreCase) >= 0 &&
                    exception.InnerException is InvalidDataException;
            }
            Require(contained, "callback exception containment");
            Require(stream.Disposed, "failed-constructor stream disposal");
        }

        private static int[,] CreateSamples(int bitsPerSample)
        {
            var samples = new int[FrameCount, Channels];
            int mask = (1 << bitsPerSample) - 1;
            int signBit = 1 << (bitsPerSample - 1);
            int signExtension = ~mask;
            for (int frame = 0; frame < FrameCount; frame++)
            {
                int sample = (frame * 65797 + 17) & mask;
                if ((sample & signBit) != 0)
                    sample |= signExtension;
                samples[frame, 0] = sample;
                int opposite = (-sample) & mask;
                if ((opposite & signBit) != 0)
                    opposite |= signExtension;
                samples[frame, 1] = opposite;
            }
            return samples;
        }

        private static byte[] CreateAiff(int[,] samples, int bitsPerSample)
        {
            int bytesPerSample = bitsPerSample / 8;
            int dataSize = FrameCount * Channels * bytesPerSample;
            using (var stream = new MemoryStream(54 + dataSize))
            {
                WriteAscii(stream, "FORM");
                WriteUInt32BigEndian(stream, (uint)(46 + dataSize));
                WriteAscii(stream, "AIFF");
                WriteAscii(stream, "COMM");
                WriteUInt32BigEndian(stream, 18);
                WriteUInt16BigEndian(stream, Channels);
                WriteUInt32BigEndian(stream, FrameCount);
                WriteUInt16BigEndian(stream, bitsPerSample);
                stream.Write(
                    new byte[] { 0x40, 0x0e, 0xac, 0x44, 0, 0, 0, 0, 0, 0 },
                    0,
                    10);
                WriteAscii(stream, "SSND");
                WriteUInt32BigEndian(stream, (uint)(8 + dataSize));
                WriteUInt32BigEndian(stream, 0);
                WriteUInt32BigEndian(stream, 0);
                for (int frame = 0; frame < FrameCount; frame++)
                {
                    for (int channel = 0; channel < Channels; channel++)
                    {
                        WriteSampleBigEndian(
                            stream,
                            samples[frame, channel],
                            bytesPerSample);
                    }
                }
                return stream.ToArray();
            }
        }

        private static void WriteAscii(Stream stream, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static void WriteUInt16BigEndian(Stream stream, int value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static void WriteSampleBigEndian(
            Stream stream,
            int value,
            int bytesPerSample)
        {
            for (int shift = (bytesPerSample - 1) * 8; shift >= 0; shift -= 8)
                stream.WriteByte((byte)(value >> shift));
        }

        private static void WriteUInt32BigEndian(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static void Require(bool condition, string check)
        {
            if (!condition)
                throw new InvalidDataException("FFmpeg check failed: " + check);
        }

        private sealed class ThrowingReadStream : MemoryStream
        {
            public ThrowingReadStream(byte[] bytes)
                : base(bytes, false)
            {
            }

            public bool Disposed { get; private set; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new InvalidDataException("synthetic callback failure");
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
