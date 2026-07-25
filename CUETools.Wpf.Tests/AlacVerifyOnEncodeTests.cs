using System;
using System.IO;
using CUETools.Codecs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AlacEncoder = CUETools.Codecs.ALAC.AudioEncoder;
using AlacSettings = CUETools.Codecs.ALAC.EncoderSettings;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// ALAC (m4a) verify-on-encode gate. ALAC has verify ON in the shipped config and hands its verify
    /// decoder a single frame the same way the FLAC encoder did, so it is checked against the same
    /// transient-heavy content that exposed the FLAC lookahead bug. See
    /// docs/review/flac-verify-on-encode-finding.md.
    /// </summary>
    [TestClass]
    public class AlacVerifyOnEncodeTests
    {
        private static readonly AudioPCMConfig Cd = new AudioPCMConfig(16, 2, 44100);

        // Sparse full-scale spikes over a quiet tone: each spike is a large prediction miss, which is
        // what produces the long rice unary runs that broke FLAC's verify path.
        private static string Encode(bool doVerify, double seconds, string mode, int seed)
        {
            string path = Path.Combine(Path.GetTempPath(), "alacverify-" + Guid.NewGuid().ToString("N") + ".m4a");
            int total = (int)(Cd.SampleRate * seconds);
            var settings = new AlacSettings() { PCM = Cd, DoVerify = doVerify };
            if (mode != null) settings.EncoderMode = mode;
            var enc = new AlacEncoder(settings, path, null);
            enc.FinalSampleCount = total;
            try
            {
                var rnd = new Random(seed);
                const int chunk = 4096;
                var buf = new AudioBuffer(Cd, chunk);
                int written = 0;
                double phase = 0;
                while (written < total)
                {
                    int n = Math.Min(chunk, total - written);
                    buf.Prepare(n);
                    var s = buf.Samples;
                    for (int i = 0; i < n; i++)
                    {
                        phase += 2 * Math.PI * 220.0 / Cd.SampleRate;
                        int q = (int)(Math.Sin(phase) * 300);
                        if (rnd.Next(0, 97) == 0)
                        {
                            int spike = rnd.Next(0, 2) == 0 ? 32767 : -32768;
                            s[i, 0] = spike; s[i, 1] = -spike / 2;
                        }
                        else { s[i, 0] = q + rnd.Next(-3, 4); s[i, 1] = q + rnd.Next(-3, 4); }
                    }
                    enc.Write(buf);
                    written += n;
                }
            }
            finally { enc.Close(); }
            return path;
        }

        [TestMethod]
        public void VerifyOn_Transients_Mode10_Encodes()
        {
            // mode 10 is the app's archival ALAC default
            foreach (int seed in new[] { 1, 7, 4242 })
            {
                string p = null;
                try { p = Encode(true, 30.0, "10", seed); }
                catch (Exception ex)
                {
                    Assert.Fail($"ALAC verify-on-encode failed (seed {seed}): "
                        + ex.GetType().Name + ": " + ex.Message);
                }
                finally { if (p != null) try { File.Delete(p); } catch { } }
            }
        }

        [TestMethod]
        public void VerifyOn_DoesNotChangeTheEncodedSize()
        {
            // Byte-for-byte equality is NOT a valid invariant here, unlike FLAC: the MP4/m4a container
            // embeds a creation/modification timestamp, so two runs of the SAME settings can differ in
            // the header (measured: they differed at byte 55). Do not assert that they DO differ either
            // - the timestamp has one-second resolution, so two back-to-back encodes often land in the
            // same second and would be byte-identical. Size is the invariant that is both meaningful
            // and stable: it still catches verify altering the encoding.
            string off = null, on = null;
            try
            {
                off = Encode(false, 5.0, "10", 55);
                on = Encode(true, 5.0, "10", 55);
                Assert.AreEqual(new FileInfo(off).Length, new FileInfo(on).Length,
                    "ALAC verify-on-encode changed the encoded size; it must only observe");
            }
            finally
            {
                foreach (var p in new[] { off, on })
                    if (p != null) try { File.Delete(p); } catch { }
            }
        }
    }
}
