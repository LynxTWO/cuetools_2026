using System;

namespace CUETools.Wpf.Controls;

/// <summary>
/// Portable, UI-free lossy-codec math for the codec scope - the REAL perceptual pipeline run on the
/// real PCM window, not a canned animation. For each analysis block it computes:
///
///  1. a 1024-point FFT of the Hann-windowed samples (this is honestly what MP3 encoders do for the
///     psychoacoustic side: LAME's psymodel is FFT-based; the codec's DATA path uses a hybrid
///     polyphase+MDCT filterbank, which the stage labels name);
///  2. critical-band (Bark) energies, a spreading function, and the absolute threshold of hearing,
///     giving the real MASKING THRESHOLD - the level below which a component is inaudible next to
///     its neighbours;
///  3. which spectral components fall BELOW that threshold: those are what a lossy codec discards -
///     the "% discarded" figure is measured, not asserted;
///  4. mask-shaped quantization of the kept components (step sized to the local threshold, the real
///     noise-shaping idea) and an entropy-cost estimate of the quantized values, giving the
///     estimated kbps.
///
/// Honesty: this is REPRESENTATIVE of the psychoacoustic model family (a simplified Schroeder
/// spreading + Terhardt absolute threshold), not LAME's exact psymodel; the kbps is an estimate
/// from the real masked spectrum, not the encoder's final rate. The lossless scopes never use this
/// pipeline and this pipeline never draws a predictor/residual - the families are different and the
/// drawing keeps them different.
/// </summary>
public static class LossyMath
{
    public const int N = 1024;            // analysis block (512 spectral bins at 44.1 kHz)
    public const int Bins = N / 2;
    private const double Fs = 44100.0;

    public sealed class Profile
    {
        public string Name = "";
        public string Tagline = "";
        public string[] Stages = Array.Empty<string>();
        /// <summary>Quantizer coarseness: how far below the mask the codec keeps noise (dB).
        /// Smaller margin = coarser steps = lower bitrate. Distinguishes codec tunings.</summary>
        public double NoiseMarginDb = 0;
        /// <summary>MP3 packs with Huffman tables; WMA-family uses run-level vector coding. Only
        /// affects the pack-stage drawing and cost model label.</summary>
        public bool HuffmanPack = true;
    }

    /// <summary>Per-codec profiles. Each lossy codec gets ITS OWN pipeline labels and tuning - the
    /// on-screen difference between codecs is the real difference in their designs.</summary>
    public static Profile? Info(string codec) => (codec ?? "").ToLowerInvariant() switch
    {
        "mp3" => new Profile
        {
            Name = "MP3 (LAME)",
            Tagline = "polyphase+MDCT filterbank, FFT psychoacoustic mask, Huffman pack",
            Stages = new[] { "spectrum", "mask", "quantize", "Huffman" },
            NoiseMarginDb = 0,
            HuffmanPack = true
        },
        "wma" => new Profile
        {
            Name = "WMA",
            Tagline = "pure-MDCT filterbank, perceptual mask, run-level pack",
            Stages = new[] { "MDCT", "mask", "quantize", "run-level" },
            NoiseMarginDb = 2,
            HuffmanPack = false
        },
        // external (imported) lossy codecs - each labeled with ITS real pipeline:
        "mpc" => new Profile
        {
            Name = "Musepack (SV8)",
            Tagline = "pure 32-subband filterbank (no MDCT), psychoacoustic mask, Huffman pack",
            Stages = new[] { "subbands", "mask", "quantize", "Huffman" },
            NoiseMarginDb = -2,   // Musepack keeps a little more margin - tuned for transparency
            HuffmanPack = true
        },
        "ogg" => new Profile
        {
            Name = "Ogg Vorbis",
            Tagline = "pure-MDCT filterbank, floor-curve mask, codebook (VQ) pack",
            Stages = new[] { "MDCT", "floor", "quantize", "codebook" },
            NoiseMarginDb = 1,
            HuffmanPack = true    // codebook lookup draws like variable-length codes
        },
        "opus" => new Profile
        {
            Name = "Opus (CELT)",
            Tagline = "MDCT bands with constrained energy, per-band allocation, range coding",
            Stages = new[] { "MDCT", "band energy", "allocate", "range code" },
            NoiseMarginDb = 1,
            HuffmanPack = false   // range coder: run-level style drawing reads closer
        },
        // m4a with the TYPE picker set to lossy (an imported AAC encoder): AAC's real pipeline
        "m4a-lossy" => new Profile
        {
            Name = "AAC",
            Tagline = "pure-MDCT filterbank (1024-line), perceptual mask, Huffman pack",
            Stages = new[] { "MDCT", "mask", "quantize", "Huffman" },
            NoiseMarginDb = 1,
            HuffmanPack = true
        },
        _ => null
    };

    public static bool IsLossy(string codec) => Info(codec) != null;

    public sealed class Analysis
    {
        public double[] SpectrumDb = new double[Bins];   // real FFT magnitude, dB
        public double[] MaskDb = new double[Bins];       // masking threshold, dB
        public bool[] Kept = new bool[Bins];             // above the mask = audible = kept
        public double[] QuantDb = new double[Bins];      // quantized level of kept bins, dB
        public double PercentDiscarded;                  // % of bins below the mask
        public double EstKbps;                           // entropy estimate of the quantized frame
    }

    private static readonly double[] _win = BuildHann();
    private static double[] BuildHann()
    {
        var w = new double[N];
        for (int i = 0; i < N; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1));
        return w;
    }

    /// <summary>Run the full perceptual pipeline on the last N samples of the window.</summary>
    public static Analysis Analyze(float[] samples, Profile p)
    {
        var a = new Analysis();
        if (samples == null || samples.Length < N) return a;

        // 1. windowed FFT of the real samples
        var re = new double[N];
        var im = new double[N];
        int off = samples.Length - N;
        for (int i = 0; i < N; i++) re[i] = samples[off + i] * _win[i];
        Fft(re, im);

        // magnitude in dB (0 dB = full scale sine)
        for (int k = 0; k < Bins; k++)
        {
            double mag = Math.Sqrt(re[k] * re[k] + im[k] * im[k]) / (N / 4.0);
            a.SpectrumDb[k] = 20 * Math.Log10(Math.Max(1e-7, mag));
        }

        // 2. Bark-band energies -> spreading -> masking threshold
        int nBands = 25;
        var bandOf = new int[Bins];
        var bandEnergy = new double[nBands];
        for (int k = 1; k < Bins; k++)
        {
            double f = k * Fs / N;
            int b = Math.Min(nBands - 1, (int)Bark(f));
            bandOf[k] = b;
            double lin = Math.Pow(10, a.SpectrumDb[k] / 10.0);
            bandEnergy[b] += lin;
        }
        // Schroeder-style spreading: masking leaks +25 dB/Bark downward, -10 dB/Bark upward
        var maskBand = new double[nBands];
        for (int b = 0; b < nBands; b++)
        {
            double sum = 0;
            for (int m = 0; m < nBands; m++)
            {
                double d = b - m;
                double slope = d >= 0 ? -10.0 * d : 25.0 * d;   // dB
                sum += bandEnergy[m] * Math.Pow(10, slope / 10.0);
            }
            // tonal offset: the mask sits ~14 dB below the spread energy (representative psymodel value)
            maskBand[b] = 10 * Math.Log10(Math.Max(1e-12, sum)) - 14.0 + p.NoiseMarginDb;
        }

        // 3. per-bin threshold = max(spread mask, absolute threshold of hearing); below = discarded
        int discarded = 0;
        for (int k = 1; k < Bins; k++)
        {
            double f = k * Fs / N;
            double mask = Math.Max(maskBand[bandOf[k]], AbsThresholdDb(f));
            a.MaskDb[k] = mask;
            a.Kept[k] = a.SpectrumDb[k] > mask;
            if (!a.Kept[k]) discarded++;
        }
        a.PercentDiscarded = 100.0 * discarded / (Bins - 1);

        // 4. quantize kept bins with a mask-shaped step + entropy estimate
        double bits = 0;
        for (int k = 1; k < Bins; k++)
        {
            if (!a.Kept[k]) { a.QuantDb[k] = -140; continue; }
            // step sized so quantization noise sits at the mask: SNR needed = signal - mask
            double snrDb = a.SpectrumDb[k] - a.MaskDb[k];
            double levels = Math.Pow(10, snrDb / 20.0);          // distinguishable amplitude steps
            double q = Math.Max(1, Math.Round(levels));
            a.QuantDb[k] = a.MaskDb[k] + 20 * Math.Log10(q);     // reconstructed (stepped) level
            // entropy cost of the quantized magnitude (Huffman/run-level proxy: log2(1+q) + sign)
            bits += Math.Log2(1 + q) + 1;
        }
        // frame -> bitrate: N samples per block per channel, stereo, at Fs
        double bitsPerSample = bits / N;
        a.EstKbps = bitsPerSample * Fs * 2 / 1000.0;
        return a;
    }

    /// <summary>Traunmuller Bark scale (real formula).</summary>
    private static double Bark(double f) => 26.81 * f / (1960 + f) - 0.53;

    /// <summary>Terhardt absolute threshold of hearing approximation (dB SPL-ish, shifted to our
    /// full-scale-relative dB world).</summary>
    private static double AbsThresholdDb(double f)
    {
        double khz = Math.Max(0.02, f / 1000.0);
        double t = 3.64 * Math.Pow(khz, -0.8)
                 - 6.5 * Math.Exp(-0.6 * (khz - 3.3) * (khz - 3.3))
                 + 1e-3 * Math.Pow(khz, 4);
        return t - 90;   // map "0 dB SPL" to about -90 dBFS, the usual playback alignment
    }

    /// <summary>In-place iterative radix-2 FFT (real math, no external deps).</summary>
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        // bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double xr = re[b] * cr - im[b] * ci;
                    double xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                    double ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
    }
}
