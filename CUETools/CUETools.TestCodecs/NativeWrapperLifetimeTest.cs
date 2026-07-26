using CUETools.Codecs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Reflection;

namespace CUETools.TestCodecs
{
    [TestClass]
    public class NativeWrapperLifetimeTest
    {
        private static readonly AudioPCMConfig Cd = new AudioPCMConfig(16, 2, 44100);

        [TestMethod]
        public void Flac_CloseBeforeWrite_IsIdempotentAndTerminal()
        {
            var stream = new TrackingMemoryStream();
            var encoder = new Codecs.libFLAC.Encoder(
                new Codecs.libFLAC.EncoderSettings { PCM = Cd, Verify = false },
                "memory.flac",
                stream);

            encoder.Close();
            Assert.IsTrue(stream.Closed);
            Assert.AreEqual(IntPtr.Zero, GetPrivateIntPtr(encoder, "m_encoder"));
            encoder.Close();
            Assert.ThrowsException<InvalidOperationException>(
                delegate { encoder.Write(CreateBuffer(Cd, 1)); });
        }

        [TestMethod]
        public void Flac_InitFailure_RollsBackEncoderMetadataAndStream()
        {
            var invalidPcm = new AudioPCMConfig(16, 0, 44100);
            var stream = new TrackingMemoryStream();
            var encoder = new Codecs.libFLAC.Encoder(
                new Codecs.libFLAC.EncoderSettings { PCM = invalidPcm, Verify = false },
                "invalid.flac",
                stream);

            Assert.ThrowsException<Exception>(
                delegate { encoder.Write(new AudioBuffer(invalidPcm, 1)); });
            Assert.IsTrue(stream.Closed);
            Assert.AreEqual(IntPtr.Zero, GetPrivateIntPtr(encoder, "m_encoder"));

            encoder.Close();
            encoder.Close();
            Assert.ThrowsException<InvalidOperationException>(
                delegate { encoder.Write(new AudioBuffer(invalidPcm, 1)); });
        }

        [TestMethod]
        public void Flac_MalformedInput_RollsBackDecoderAndStream()
        {
            var stream = new TrackingMemoryStream(new byte[] {
                0x66, 0x4c, 0x61, 0x43, 0xff, 0xff, 0xff, 0xff
            });

            Assert.ThrowsException<Exception>(
                delegate
                {
                    new Codecs.libFLAC.AudioDecoder(
                        new Codecs.libFLAC.DecoderSettings(),
                        "malformed.flac",
                        stream);
                });
            Assert.IsTrue(stream.Closed);
        }

        [TestMethod]
        public void Flac_RealRoundTrip_WithMetadata_Completes()
        {
            string path = TempFile(".flac");
            AudioBuffer input = CreateBuffer(Cd, 8192);
            try
            {
                var encoder = new Codecs.libFLAC.Encoder(
                    new Codecs.libFLAC.EncoderSettings {
                        PCM = Cd,
                        Verify = true,
                        Padding = 64
                    },
                    path);
                encoder.FinalSampleCount = input.Length;
                encoder.Write(input);
                encoder.Close();
                encoder.Close();

                var decoder = new Codecs.libFLAC.AudioDecoder(
                    new Codecs.libFLAC.DecoderSettings(),
                    path,
                    null);
                try
                {
                    var output = new AudioBuffer(decoder, input.Length);
                    Assert.AreEqual(input.Length, decoder.Read(output, input.Length));
                    CollectionAssert.AreEqual(input.Bytes, output.Bytes);
                }
                finally
                {
                    decoder.Close();
                    decoder.Close();
                }
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [TestMethod]
        public void WavPack_CloseBeforeWrite_IsIdempotentAndTerminal()
        {
            var stream = new TrackingMemoryStream();
            var encoder = new Codecs.libwavpack.AudioEncoder(
                new Codecs.libwavpack.EncoderSettings {
                    PCM = Cd,
                    Verify = false
                },
                "memory.wv",
                stream);

            encoder.Close();
            Assert.IsTrue(stream.Closed);
            encoder.Close();
            Assert.ThrowsException<InvalidOperationException>(
                delegate { encoder.Write(CreateBuffer(Cd, 1)); });
        }

        [TestMethod]
        public void WavPack_RealRoundTrip_Completes()
        {
            string path = TempFile(".wv");
            AudioBuffer input = CreateBuffer(Cd, 8192);
            try
            {
                var encoder = new Codecs.libwavpack.AudioEncoder(
                    new Codecs.libwavpack.EncoderSettings {
                        PCM = Cd,
                        Verify = true,
                        MD5Sum = true
                    },
                    path);
                encoder.FinalSampleCount = input.Length;
                encoder.Write(input);
                encoder.Close();
                encoder.Close();

                var decoder = new Codecs.libwavpack.AudioDecoder(
                    new Codecs.libwavpack.DecoderSettings(),
                    path,
                    null);
                try
                {
                    var output = new AudioBuffer(decoder, input.Length);
                    Assert.AreEqual(input.Length, decoder.Read(output, input.Length));
                    CollectionAssert.AreEqual(input.Bytes, output.Bytes);
                }
                finally
                {
                    decoder.Close();
                    decoder.Close();
                }
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [TestMethod]
        public void Hdcd_ValidAndRejectedConstructors_AreSafe()
        {
            var decoder = new Codecs.HDCD.HDCDDotNet(2, 44100, 24, false);
            decoder.Close();
            decoder.Close();

            Assert.ThrowsException<Exception>(
                delegate { new Codecs.HDCD.HDCDDotNet(0, 44100, 24, false); });
        }

        [TestMethod]
        public void Lame_CloseBeforeWrite_AndInitFailure_AreTerminal()
        {
            var closeStream = new TrackingMemoryStream();
            var closeEncoder = new Codecs.libmp3lame.AudioEncoder(
                new Codecs.libmp3lame.CBREncoderSettings { PCM = Cd },
                "memory.mp3",
                closeStream);
            closeEncoder.Close();
            closeEncoder.Close();
            Assert.IsTrue(closeStream.Closed);
            Assert.ThrowsException<InvalidOperationException>(
                delegate { closeEncoder.Write(CreateBuffer(Cd, 1)); });

            var failingStream = new ThrowingWriteStream();
            var failingEncoder = new Codecs.libmp3lame.AudioEncoder(
                new Codecs.libmp3lame.CBREncoderSettings { PCM = Cd },
                "failing.mp3",
                failingStream);
            IOException failure = Assert.ThrowsException<IOException>(
                delegate { failingEncoder.Write(CreateBuffer(Cd, 128)); });
            Assert.AreEqual("primary-write-failure", failure.Message);
            Assert.IsTrue(failingStream.Closed);
            Assert.AreEqual(IntPtr.Zero, GetPrivateIntPtr(failingEncoder, "m_handle"));
            failingEncoder.Close();
            Assert.ThrowsException<InvalidOperationException>(
                delegate { failingEncoder.Write(CreateBuffer(Cd, 1)); });
        }

        [TestMethod]
        public void Lame_RealEncode_ProducesBytesAndCloses()
        {
            var stream = new TrackingMemoryStream();
            var encoder = new Codecs.libmp3lame.AudioEncoder(
                new Codecs.libmp3lame.CBREncoderSettings { PCM = Cd },
                "memory.mp3",
                stream);
            encoder.FinalSampleCount = 4096;
            encoder.Write(CreateBuffer(Cd, 4096));
            encoder.Close();

            Assert.IsTrue(stream.Closed);
            Assert.IsTrue(stream.ToArray().Length > 10);
        }

        [TestMethod]
        public void NativeOwnershipSourceInvariants_AreExplicit()
        {
            string root = FindRepoRoot();
            if (root == null)
            {
                Assert.Inconclusive("Repository source is unavailable for ownership invariants.");
                return;
            }

            string flacBinding = File.ReadAllText(Path.Combine(
                root, "CUETools.Codecs.libFLAC", "FLACDLL.cs"));
            string flacWriter = File.ReadAllText(Path.Combine(
                root, "CUETools.Codecs.libFLAC", "Writer.cs"));
            string hdcd = File.ReadAllText(Path.Combine(
                root, "CUETools.Codecs.HDCD", "HDCDDotNet.cs"));
            string lame = File.ReadAllText(Path.Combine(
                root, "CUETools.Codecs.libmp3lame", "LameWriter.cs"));
            string wavPackWriter = File.ReadAllText(Path.Combine(
                root, "CUETools.Codecs.libwavpack", "Writer.cs"));
            string wavPackReader = File.ReadAllText(Path.Combine(
                root, "CUETools.Codecs.libwavpack", "Reader.cs"));

            StringAssert.Contains(flacBinding, "FLAC__metadata_object_delete");
            StringAssert.Contains(flacWriter, "m_seekTableMetadata");
            StringAssert.Contains(flacWriter, "m_vorbisCommentMetadata");
            StringAssert.Contains(flacWriter, "m_paddingMetadata");
            StringAssert.Contains(flacWriter, "DeleteMetadataObjects");
            StringAssert.Contains(hdcd, "RollBackConstructorNoThrow");
            StringAssert.Contains(hdcd, "_gch.Free()");
            StringAssert.Contains(hdcd, "hdcd_decoder_delete");
            StringAssert.Contains(lame, "if (m_handle == IntPtr.Zero)");
            StringAssert.Contains(lame, "CloseHandleNoThrow");
            StringAssert.Contains(wavPackWriter, "CloseEncoderNoThrow");
            StringAssert.Contains(wavPackWriter, "string error = wavpackdll.WavpackGetErrorMessage(_wpc)");
            StringAssert.Contains(wavPackReader, "RollBackConstructorNoThrow");
            StringAssert.Contains(wavPackReader, "Marshal.FreeHGlobal");
        }

        private static IntPtr GetPrivateIntPtr(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private ownership field " + fieldName);
            return (IntPtr)field.GetValue(instance);
        }

        private static AudioBuffer CreateBuffer(AudioPCMConfig pcm, int sampleCount)
        {
            var samples = new int[sampleCount, pcm.ChannelCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int sample = ((i * 257) & 0xffff) - 32768;
                for (int channel = 0; channel < pcm.ChannelCount; channel++)
                    samples[i, channel] = channel == 0 ? sample : -(sample / 2);
            }
            return new AudioBuffer(pcm, samples, sampleCount);
        }

        private static string TempFile(string extension)
        {
            return System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cuetools-native-lifetime-" + Guid.NewGuid().ToString("N") + extension);
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CUETools.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }

        private class TrackingMemoryStream : MemoryStream
        {
            public TrackingMemoryStream()
            {
            }

            public TrackingMemoryStream(byte[] bytes)
                : base(bytes)
            {
            }

            public bool Closed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    Closed = true;
                base.Dispose(disposing);
            }
        }

        private sealed class ThrowingWriteStream : TrackingMemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new IOException("primary-write-failure");
            }
        }
    }
}
