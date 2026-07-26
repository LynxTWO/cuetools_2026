using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CUETools.Codecs;
using CUETools.Codecs.HDCD;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FlacDecoder = CUETools.Codecs.libFLAC.AudioDecoder;
using FlacDecoderSettings = CUETools.Codecs.libFLAC.DecoderSettings;
using FlacEncoder = CUETools.Codecs.libFLAC.Encoder;
using FlacEncoderSettings = CUETools.Codecs.libFLAC.EncoderSettings;
using MacDecoder = CUETools.Codecs.MACLib.AudioDecoder;
using MacDecoderSettings = CUETools.Codecs.MACLib.DecoderSettings;
using MacEncoder = CUETools.Codecs.MACLib.AudioEncoder;
using MacEncoderSettings = CUETools.Codecs.MACLib.EncoderSettings;
using M2tsDecoder = CUETools.Codecs.MPEG.BDLPCM.AudioDecoder;
using M2tsDecoderSettings = CUETools.Codecs.MPEG.BDLPCM.DecoderSettings;
using WavPackDecoder = CUETools.Codecs.libwavpack.AudioDecoder;
using WavPackDecoderSettings = CUETools.Codecs.libwavpack.DecoderSettings;
using WavPackEncoder = CUETools.Codecs.libwavpack.AudioEncoder;
using WavPackEncoderSettings = CUETools.Codecs.libwavpack.EncoderSettings;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class CodecImportIntegrationTests
    {
        private string root;

        [TestInitialize]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "cuetools-codec-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TestCleanup]
        public void CleanUp()
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
            }
        }

        [TestMethod]
        public void ImportedLosslessEncoders_DefaultToVerification()
        {
            Assert.IsTrue(new FlacEncoderSettings().Verify);
            Assert.IsTrue(new WavPackEncoderSettings().Verify);
            Assert.IsTrue(new MacEncoderSettings().Verify);
        }

        [DataTestMethod]
        [DataRow(16)]
        [DataRow(24)]
        public void LibFlac_RoundTripsRealPcm_OnNet8(int bitsPerSample)
        {
            AudioPCMConfig pcm = new AudioPCMConfig(bitsPerSample, 2, 44100);
            int[,] samples = MakeSamples(pcm, 4097);
            string path = Path.Combine(root, "roundtrip.flac");
            var settings = new FlacEncoderSettings { PCM = pcm, EncoderMode = "5", Verify = true, MD5Sum = true };
            var encoder = new FlacEncoder(settings, path);
            encoder.FinalSampleCount = samples.GetLength(0);
            var input = new AudioBuffer(pcm, samples, samples.GetLength(0));
            encoder.Write(input);
            Assert.IsFalse(File.Exists(path), "The requested FLAC path must not be published before finalization and verification.");
            encoder.Close();
            encoder.Close();
            Assert.ThrowsException<InvalidOperationException>(() => encoder.Write(input));

            var decoder = new FlacDecoder(new FlacDecoderSettings(), path, null);
            try
            {
                AssertDecodedSamples(decoder, pcm, samples);
            }
            finally
            {
                decoder.Close();
            }
        }

        [DataTestMethod]
        [DataRow(16)]
        [DataRow(24)]
        public void WavPack_RoundTripsAndVerifiesRealPcm_OnNet8(int bitsPerSample)
        {
            AudioPCMConfig pcm = new AudioPCMConfig(bitsPerSample, 2, 44100);
            int[,] samples = MakeSamples(pcm, 4097);
            string path = Path.Combine(root, "roundtrip.wv");
            var settings = new WavPackEncoderSettings
            {
                PCM = pcm,
                EncoderMode = "normal",
                Verify = true,
                MD5Sum = true
            };
            var encoder = new WavPackEncoder(settings, path);
            encoder.FinalSampleCount = samples.GetLength(0);
            var input = new AudioBuffer(pcm, samples, samples.GetLength(0));
            encoder.Write(input);
            Assert.IsFalse(File.Exists(path), "The requested WavPack path must not be published before finalization and verification.");
            encoder.Close();
            encoder.Close();
            Assert.ThrowsException<InvalidOperationException>(() => encoder.Write(input));

            var decoder = new WavPackDecoder(new WavPackDecoderSettings(), path, null);
            try
            {
                AssertDecodedSamples(decoder, pcm, samples);
            }
            finally
            {
                decoder.Close();
            }
        }

        [DataTestMethod]
        [DataRow(16)]
        [DataRow(24)]
        public void MonkeyAudio_RoundTripsAndVerifiesRealPcm_OnNet8(int bitsPerSample)
        {
            AudioPCMConfig pcm = new AudioPCMConfig(bitsPerSample, 2, 44100);
            int[,] samples = MakeSamples(pcm, 4097);
            string path = Path.Combine(root, "roundtrip.ape");
            var settings = new MacEncoderSettings { PCM = pcm, EncoderMode = "high", Verify = true };
            var encoder = new MacEncoder(settings, path);
            encoder.FinalSampleCount = samples.GetLength(0);
            var input = new AudioBuffer(pcm, samples, samples.GetLength(0));
            encoder.Write(input);
            Assert.IsFalse(File.Exists(path), "The requested Monkey's Audio path must not be published before finalization and verification.");
            encoder.Close();
            encoder.Close();
            Assert.ThrowsException<InvalidOperationException>(() => encoder.Write(input));

            var decoder = new MacDecoder(new MacDecoderSettings(), path, null);
            try
            {
                AssertDecodedSamples(decoder, pcm, samples);
            }
            finally
            {
                decoder.Close();
            }
        }

        [TestMethod]
        public void LibFlac_FinalizationFailure_DoesNotReplaceRequestedDestination()
        {
            string path = Path.Combine(root, "existing.flac");
            byte[] sentinel = { 0x43, 0x55, 0x45 };
            File.WriteAllBytes(path, sentinel);
            AudioPCMConfig pcm = AudioPCMConfig.RedBook;
            int[,] samples = MakeSamples(pcm, 31);
            var encoder = new FlacEncoder(
                new FlacEncoderSettings { PCM = pcm, EncoderMode = "5", Verify = true },
                path);
            encoder.FinalSampleCount = samples.GetLength(0) + 1;
            encoder.Write(new AudioBuffer(pcm, samples, samples.GetLength(0)));

            AssertBytes(sentinel, File.ReadAllBytes(path));
            Assert.ThrowsException<Exception>(() => encoder.Close());
            encoder.Close();
            Assert.ThrowsException<InvalidOperationException>(
                () => encoder.Write(new AudioBuffer(
                    pcm,
                    samples,
                    samples.GetLength(0))));
            AssertBytes(sentinel, File.ReadAllBytes(path));
            Assert.AreEqual(0, Directory.GetFiles(root, "*.cuetools-lossless-*").Length);
        }

        [TestMethod]
        public void LosslessVerifier_RejectsDecodedPcmMismatch()
        {
            AudioPCMConfig pcm = AudioPCMConfig.RedBook;
            int[,] expected = MakeSamples(pcm, 16);
            int[,] changed = (int[,])expected.Clone();
            changed[8, 1] ^= 1;
            byte[] digest;
            using (var fingerprint = new LosslessPcmFingerprint())
            {
                fingerprint.Append(new AudioBuffer(pcm, expected, expected.GetLength(0)));
                digest = fingerprint.Complete();
            }

            Assert.ThrowsException<InvalidDataException>(
                () => LosslessPcmVerifier.Verify(
                    "test",
                    pcm,
                    expected.GetLength(0),
                    digest,
                    delegate { return new ArrayAudioSource(pcm, changed, changed.GetLength(0)); }));
        }

        [TestMethod]
        public void LosslessVerifier_RejectsTruncatedDecode()
        {
            AudioPCMConfig pcm = AudioPCMConfig.RedBook;
            int[,] expected = MakeSamples(pcm, 16);
            int[,] truncated = new int[15, pcm.ChannelCount];
            Array.Copy(expected, truncated, truncated.Length);
            byte[] digest;
            using (var fingerprint = new LosslessPcmFingerprint())
            {
                fingerprint.Append(new AudioBuffer(pcm, expected, expected.GetLength(0)));
                digest = fingerprint.Complete();
            }

            Assert.ThrowsException<InvalidDataException>(
                () => LosslessPcmVerifier.Verify(
                    "test",
                    pcm,
                    expected.GetLength(0),
                    digest,
                    delegate { return new ArrayAudioSource(pcm, truncated, truncated.GetLength(0)); }));
        }

        [TestMethod]
        public void LosslessTransaction_FinalizeFailurePreservesDestinationAndRemovesWorkFile()
        {
            string path = Path.Combine(root, "existing.wv");
            File.WriteAllText(path, "original");
            var transaction = new LosslessFileOutputTransaction(path);
            File.WriteAllText(transaction.WorkPath, "candidate");

            Assert.ThrowsException<InvalidOperationException>(
                () => transaction.Complete(delegate { throw new InvalidOperationException("finish failed"); }));
            Assert.AreEqual("original", File.ReadAllText(path));
            Assert.IsFalse(File.Exists(transaction.WorkPath));
        }

        [TestMethod]
        public void LosslessTransaction_DoesNotReplaceDestinationCreatedAfterStart()
        {
            string path = Path.Combine(root, "competing.wv");
            byte[] candidate = { 1, 2, 3, 4 };
            byte[] competitor = { 9, 8, 7, 6 };
            var transaction = new LosslessFileOutputTransaction(path);
            File.WriteAllBytes(transaction.WorkPath, candidate);

            Assert.ThrowsException<IOException>(
                () => transaction.Complete(
                    delegate { File.WriteAllBytes(path, competitor); }));

            AssertBytes(competitor, File.ReadAllBytes(path));
            Assert.IsFalse(transaction.Published);
            Assert.IsFalse(File.Exists(transaction.WorkPath));
        }

        [TestMethod]
        public void LosslessTransaction_ReplacesDestinationThatExistedAtStart()
        {
            string path = Path.Combine(root, "replace.wv");
            byte[] candidate = { 1, 2, 3, 4 };
            File.WriteAllBytes(path, new byte[] { 9, 8, 7, 6 });
            var transaction = new LosslessFileOutputTransaction(path);
            File.WriteAllBytes(transaction.WorkPath, candidate);

            transaction.Complete(delegate { });
            transaction.Complete(
                delegate
                {
                    throw new AssertFailedException(
                        "A published transaction must not finalize twice.");
                });

            AssertBytes(candidate, File.ReadAllBytes(path));
            Assert.IsTrue(transaction.Published);
            Assert.IsFalse(File.Exists(transaction.WorkPath));
        }

        [TestMethod]
        public void HdcdNativeWrapper_LoadsAndProcessesOnNet8()
        {
            var detector = new HDCDDotNet(2, 44100, 24, false);
            try
            {
                int[,] silence = new int[512, 2];
                detector.Write(new AudioBuffer(AudioPCMConfig.RedBook, silence, silence.GetLength(0)));
                Assert.IsFalse(detector.Detected);
            }
            finally
            {
                detector.Close();
            }
        }

        [TestMethod]
        public void LibMp3LameNativeWrapper_ReportsVersionAndEncodesOnNet8()
        {
            AudioPCMConfig pcm = AudioPCMConfig.RedBook;
            int[,] samples = MakeSamples(pcm, 4097);
            string path = Path.Combine(root, "native-lame.mp3");
            Assembly lameAssembly = Assembly.LoadFrom(Path.Combine(
                AppContext.BaseDirectory,
                "CUETools.Codecs.libmp3lame.dll"));
            Type settingsType = lameAssembly.GetType(
                "CUETools.Codecs.libmp3lame.VBREncoderSettings",
                throwOnError: true);
            Type encoderType = lameAssembly.GetType(
                "CUETools.Codecs.libmp3lame.AudioEncoder",
                throwOnError: true);
            object settings = Activator.CreateInstance(settingsType);
            settingsType.GetProperty("PCM").SetValue(settings, pcm);
            settingsType.GetProperty("EncoderMode").SetValue(settings, "V2");

            string version = settingsType.GetProperty("Version")
                .GetValue(settings) as string;
            Assert.IsFalse(String.IsNullOrWhiteSpace(version));
            object encoder = Activator.CreateInstance(
                encoderType,
                new object[] { settings, path, null });
            encoderType.GetProperty("FinalSampleCount")
                .SetValue(encoder, (long)samples.GetLength(0));
            try
            {
                encoderType.GetMethod("Write").Invoke(
                    encoder,
                    new object[]
                    {
                        new AudioBuffer(pcm, samples, samples.GetLength(0))
                    });
            }
            finally
            {
                encoderType.GetMethod("Close").Invoke(encoder, null);
            }

            Assert.IsTrue(File.Exists(path));
            int padding = (int)settingsType.GetProperty("Padding")
                .GetValue(settings);
            Assert.IsTrue(
                new FileInfo(path).Length > padding,
                "The native LAME encoder must publish MP3 frames, not only ID3 padding.");
        }

        [TestMethod]
        public void MpegPlugin_DecodesSyntheticBlurayLpcmPacket_OnNet8()
        {
            byte[] stream = BuildM2tsStereo24BitFixture();
            var settings = new M2tsDecoderSettings { StreamId = 0x101 };
            var decoder = new M2tsDecoder(settings, "synthetic.m2ts", new MemoryStream(stream, false));
            try
            {
                Assert.AreEqual(24, decoder.PCM.BitsPerSample);
                Assert.AreEqual(2, decoder.PCM.ChannelCount);
                Assert.AreEqual(48000, decoder.PCM.SampleRate);

                var buffer = new AudioBuffer(decoder, 2);
                Assert.AreEqual(2, decoder.Read(buffer, 2));
                Assert.AreEqual(1, buffer.Samples[0, 0]);
                Assert.AreEqual(-1, buffer.Samples[0, 1]);
                Assert.AreEqual(0x123456, buffer.Samples[1, 0]);
                Assert.AreEqual(-0x123456, buffer.Samples[1, 1]);
            }
            finally
            {
                decoder.Close();
            }
        }

        private static void AssertDecodedSamples(IAudioSource decoder, AudioPCMConfig expectedPcm, int[,] expected)
        {
            Assert.AreEqual(expectedPcm.BitsPerSample, decoder.PCM.BitsPerSample);
            Assert.AreEqual(expectedPcm.ChannelCount, decoder.PCM.ChannelCount);
            Assert.AreEqual(expectedPcm.SampleRate, decoder.PCM.SampleRate);
            Assert.AreEqual(expected.GetLength(0), decoder.Length);

            var buffer = new AudioBuffer(decoder, expected.GetLength(0));
            Assert.AreEqual(expected.GetLength(0), decoder.Read(buffer, -1));
            for (int sample = 0; sample < expected.GetLength(0); sample++)
                for (int channel = 0; channel < expectedPcm.ChannelCount; channel++)
                    Assert.AreEqual(expected[sample, channel], buffer.Samples[sample, channel]);
        }

        private static int[,] MakeSamples(AudioPCMConfig pcm, int count)
        {
            var result = new int[count, pcm.ChannelCount];
            int magnitudeMask = (1 << (pcm.BitsPerSample - 1)) - 1;
            for (int sample = 0; sample < count; sample++)
            {
                for (int channel = 0; channel < pcm.ChannelCount; channel++)
                {
                    int value = unchecked((sample * 1103515245) + (channel * 12345) + 0x13579);
                    value &= magnitudeMask;
                    if (((sample + channel) & 1) != 0)
                        value = -value;
                    result[sample, channel] = value;
                }
            }
            return result;
        }

        private static void AssertBytes(byte[] expected, byte[] actual)
        {
            CollectionAssert.AreEqual(expected, actual);
        }

        private static byte[] BuildM2tsStereo24BitFixture()
        {
            byte[] pat = NewPacket(0x000, true, false);
            WritePayload(
                pat,
                8,
                new byte[]
                {
                    0x00, 0x00, 0x30, 0x08,
                    0x00, 0x01, 0xE1, 0x00,
                    0x00, 0x00, 0x00, 0x00
                });

            byte[] pmt = NewPacket(0x100, true, false);
            WritePayload(
                pmt,
                8,
                new byte[]
                {
                    0x00, 0x02, 0x30, 0x13,
                    0xE1, 0x01, 0xF0, 0x00,
                    0x80, 0xE1, 0x01, 0xF0, 0x06,
                    0x05, 0x04, 0x48, 0x44, 0x4D, 0x56,
                    0x00, 0x00, 0x00, 0x00
                });

            byte[] pes = NewPacket(0x101, true, true);
            pes[8] = 158;
            int payloadOffset = 8 + 1 + pes[8];
            WritePayload(
                pes,
                payloadOffset,
                new byte[]
                {
                    0x00, 0x00, 0x01, 0xBD, 0x00, 0x00, 0x80, 0x00, 0x00,
                    0x00, 0x00, 0x31, 0xC0,
                    0x00, 0x00, 0x01, 0xFF, 0xFF, 0xFF,
                    0x12, 0x34, 0x56, 0xED, 0xCB, 0xAA
                });

            return pat.Concat(pmt).Concat(pes).ToArray();
        }

        private static byte[] NewPacket(int pid, bool payloadUnitStart, bool adaptationField)
        {
            byte[] packet = Enumerable.Repeat((byte)0xFF, 192).ToArray();
            packet[0] = packet[1] = packet[2] = packet[3] = 0;
            packet[4] = 0x47;
            packet[5] = (byte)((payloadUnitStart ? 0x40 : 0) | ((pid >> 8) & 0x1F));
            packet[6] = (byte)pid;
            packet[7] = (byte)(adaptationField ? 0x30 : 0x10);
            return packet;
        }

        private static void WritePayload(byte[] packet, int offset, byte[] payload)
        {
            Buffer.BlockCopy(payload, 0, packet, offset, payload.Length);
        }

        private sealed class ArrayAudioSource : IAudioSource
        {
            private readonly int[,] samples;
            private long position;

            internal ArrayAudioSource(AudioPCMConfig pcm, int[,] samples, long length)
            {
                PCM = pcm;
                this.samples = samples;
                Length = length;
            }

            public IAudioDecoderSettings Settings { get { return null; } }
            public AudioPCMConfig PCM { get; private set; }
            public string Path { get { return String.Empty; } }
            public TimeSpan Duration { get { return TimeSpan.FromSeconds((double)Length / PCM.SampleRate); } }
            public long Length { get; private set; }
            public long Position
            {
                get { return position; }
                set { position = value; }
            }
            public long Remaining { get { return Length - position; } }

            public int Read(AudioBuffer buffer, int maxLength)
            {
                buffer.Prepare(this, maxLength);
                for (int sample = 0; sample < buffer.Length; sample++)
                    for (int channel = 0; channel < PCM.ChannelCount; channel++)
                        buffer.Samples[sample, channel] = samples[(int)position + sample, channel];
                position += buffer.Length;
                return buffer.Length;
            }

            public void Close()
            {
            }
        }
    }
}
