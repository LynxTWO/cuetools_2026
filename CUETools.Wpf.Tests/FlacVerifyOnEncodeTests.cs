using System;
using System.IO;
using CUETools.Codecs;
using CUETools.Codecs.Flake;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Isolated repro + regression gate for FLAC verify-on-encode (DoVerify). Encoding with verify ON
    /// used to abort with "BitReader.read_rice_block: read past end of buffer" - the verify DECODER
    /// failing on a perfectly good frame. No drive and no disc involved, so this runs in ~seconds
    /// instead of a 6-minute rip cycle. See docs/review/flac-verify-on-encode-finding.md.
    /// </summary>
    [TestClass]
    public class FlacVerifyOnEncodeTests
    {
        private static readonly AudioPCMConfig Cd = new AudioPCMConfig(16, 2, 44100);

        // Encode `seconds` of generated audio with DoVerify as given. Returns the output path.
        // `kind` picks the sample content, because which content is encoded decides which rice
        // partitions get emitted - and the crash was content-dependent.
        private static string Encode(bool doVerify, string kind, double seconds, int seed = 1234, string mode = null)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "flacverify-" + Guid.NewGuid().ToString("N") + ".flac");
            int total = (int)(Cd.SampleRate * seconds);

            var settings = new EncoderSettings() { PCM = Cd, DoVerify = doVerify };
            // The app's archival default is FLAC mode 8 (max subset compression), which partitions
            // rice codes differently than the stock mode - so the mode is part of the repro.
            if (mode != null) settings.EncoderMode = mode;
            var enc = new AudioEncoder(settings, path);
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
                        int l, r;
                        switch (kind)
                        {
                            case "silence":
                                l = r = 0;
                                break;
                            case "noise":
                                // worst case for the predictor: high-entropy residuals, big rice params
                                l = rnd.Next(-32768, 32768);
                                r = rnd.Next(-32768, 32768);
                                break;
                            case "loud":
                                // full-scale square-ish content, which is what a peak-100% CD track
                                // looks like to the encoder
                                l = r = ((written + i) / 64) % 2 == 0 ? 32767 : -32768;
                                break;
                            case "transients":
                                // Targets the crash mechanism: a mostly-predictable quiet signal with
                                // sparse full-scale spikes. A spike is a big prediction miss, which
                                // becomes a long rice unary run; when one of those lands near a
                                // frame's last byte the verify decoder's lookahead hits the buffer
                                // end. Uniform noise or a clean tone never produces that.
                                phase += 2 * Math.PI * 220.0 / Cd.SampleRate;
                                int q = (int)(Math.Sin(phase) * 300);
                                if (rnd.Next(0, 97) == 0)
                                {
                                    int spike = rnd.Next(0, 2) == 0 ? 32767 : -32768;
                                    l = spike; r = -spike / 2;
                                }
                                else { l = q + rnd.Next(-3, 4); r = q + rnd.Next(-3, 4); }
                                break;
                            default: // "music" - a tone plus light noise, the common case
                                phase += 2 * Math.PI * 440.0 / Cd.SampleRate;
                                int t = (int)(Math.Sin(phase) * 20000);
                                l = t + rnd.Next(-64, 64);
                                r = t + rnd.Next(-64, 64);
                                break;
                        }
                        s[i, 0] = l;
                        s[i, 1] = r;
                    }
                    enc.Write(buf);
                    written += n;
                }
            }
            finally { enc.Close(); }
            return path;
        }

        private static void EncodeAndDelete(bool doVerify, string kind, double seconds)
        {
            string p = null;
            try { p = Encode(doVerify, kind, seconds); }
            finally { if (p != null) try { File.Delete(p); } catch { } }
        }

        [TestMethod]
        public void VerifyOff_Encodes() // control: the encoder itself is fine
        {
            EncodeAndDelete(false, "music", 2.0);
        }

        [TestMethod]
        public void VerifyOn_Music_Encodes()
        {
            EncodeAndDelete(true, "music", 2.0);
        }

        [TestMethod]
        public void VerifyOn_Noise_Encodes()
        {
            EncodeAndDelete(true, "noise", 2.0);
        }

        [TestMethod]
        public void VerifyOn_Silence_Encodes()
        {
            EncodeAndDelete(true, "silence", 2.0);
        }

        [TestMethod]
        public void VerifyOn_Loud_Encodes()
        {
            EncodeAndDelete(true, "loud", 2.0);
        }

        [TestMethod]
        public void VerifyOn_LongEncode_ManyFrames_Encodes()
        {
            // The crash was bit-alignment dependent: it needs the rice residuals of SOME frame to end
            // near that frame's last byte. A 2-second clip (~20 frames) misses it; the real failure hit
            // frame 111. So encode long enough, at the app's mode 8, across several seeds to make the
            // alignment come up. This is the case that actually reproduces the bug.
            foreach (int seed in new[] { 1, 7, 4242 })
            {
                foreach (var kind in new[] { "transients", "music", "noise" })
                {
                    string p = null;
                    try { p = Encode(true, kind, 30.0, seed: seed, mode: "8"); }
                    catch (Exception ex)
                    {
                        Assert.Fail($"verify-on-encode failed (seed {seed}, {kind}): "
                            + ex.GetType().Name + ": " + ex.Message);
                    }
                    finally { if (p != null) try { File.Delete(p); } catch { } }
                }
            }
        }

        [TestMethod]
        public void VerifyOn_EveryCompressionMode_Encodes()
        {
            // the app ships FLAC mode 8; sweep every offered mode so no compression level can
            // regress the verify path
            var settings = new EncoderSettings() { PCM = Cd };
            foreach (var mode in (settings.SupportedModes ?? "").Split(' '))
            {
                if (string.IsNullOrWhiteSpace(mode)) continue;
                foreach (var kind in new[] { "music", "noise", "loud" })
                {
                    string p = null;
                    try { p = Encode(true, kind, 1.0, mode: mode); }
                    catch (Exception ex)
                    {
                        Assert.Fail($"verify-on-encode failed at mode {mode}, content {kind}: " +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                    finally { if (p != null) try { File.Delete(p); } catch { } }
                }
            }
        }

        [TestMethod]
        public void VerifyOn_ProducesTheSameBytesAsVerifyOff()
        {
            // verify must be an observer: turning it on cannot change the encoded output
            string a = null, b = null;
            try
            {
                a = Encode(false, "music", 1.5, seed: 99);
                b = Encode(true, "music", 1.5, seed: 99);
                CollectionAssert.AreEqual(File.ReadAllBytes(a), File.ReadAllBytes(b),
                    "verify-on-encode changed the encoded bytes; it must only observe");
            }
            finally
            {
                if (a != null) try { File.Delete(a); } catch { }
                if (b != null) try { File.Delete(b); } catch { }
            }
        }
    }
}
