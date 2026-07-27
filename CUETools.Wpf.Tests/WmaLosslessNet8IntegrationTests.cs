using System;
using System.IO;
using System.Runtime.InteropServices;
using CUETools.Codecs;
using CUETools.Codecs.WMA;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WmaAudioEncoder = CUETools.Codecs.WMA.AudioEncoder;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class WmaLosslessNet8IntegrationTests
    {
        private static readonly AudioPCMConfig Cd = new AudioPCMConfig(16, 2, 44100);

        [TestMethod]
        public void RealLosslessRoundTripVerifiesOnNet8WhenWindowsCodecIsAvailable()
        {
            var settings = new LosslessEncoderSettings { PCM = Cd };
            Assert.IsTrue(settings.DoVerify,
                "WMA Lossless must default to finalized-output verification.");
            if (!LosslessCodecIsAvailable(settings, out string unavailableReason))
                Assert.Inconclusive("Windows Media Lossless runtime is unavailable: " + unavailableReason);

            const int sampleCount = 11025;
            string path = Path.Combine(
                Path.GetTempPath(),
                "cuetools-wma-net8-verify-" + Guid.NewGuid().ToString("N") + ".wma");
            WmaAudioEncoder encoder = null;
            try
            {
                encoder = new WmaAudioEncoder(settings, path);
                encoder.FinalSampleCount = sampleCount;

                var buffer = new AudioBuffer(Cd, sampleCount);
                buffer.Prepare(sampleCount);
                int[,] pcm = buffer.Samples;
                for (int i = 0; i < sampleCount; i++)
                {
                    pcm[i, 0] = (i * 7919 % 65536) - 32768;
                    pcm[i, 1] = (i * 3571 % 65536) - 32768;
                }

                encoder.Write(buffer);
                // Lossless Close finalizes the file, reopens it through the independent WMA
                // decoder, and compares sample count plus a SHA-256 PCM fingerprint.
                encoder.Close();

                Assert.IsTrue(File.Exists(path));
                Assert.IsTrue(new FileInfo(path).Length > 0);
                encoder.Delete();
                Assert.IsFalse(File.Exists(path));
            }
            finally
            {
                if (encoder != null)
                {
                    try { encoder.Delete(); }
                    catch { }
                }
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static bool LosslessCodecIsAvailable(
            LosslessEncoderSettings settings,
            out string reason)
        {
            object writer = null;
            try
            {
                writer = settings.GetWriter();
                reason = null;
                return true;
            }
            catch (NotSupportedException ex) when (
                string.Equals(ex.Message, "codec/format not found", StringComparison.Ordinal))
            {
                reason = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                if (writer != null && Marshal.IsComObject(writer))
                    Marshal.ReleaseComObject(writer);
            }
        }
    }
}
