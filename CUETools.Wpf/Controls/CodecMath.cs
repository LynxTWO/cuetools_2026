using System;

namespace CUETools.Wpf.Controls;

/// <summary>
/// The real (representative) DSP a lossless audio codec performs, shared by the rip
/// <see cref="CodecScope"/> and the convert <see cref="ConvertScope"/>. Given a window of real PCM
/// it runs the predictor of a codec's family and returns the true residual, then estimates the
/// bits/sample from that residual with the Rice cost an encoder would actually spend. The
/// predictors are representative of each family, not bit-exact reimplementations - but the
/// signal/prediction/residual and the resulting ratio are computed, not decorative, so a better
/// predictor genuinely earns a smaller residual and a lower ratio.
///
/// This class is deliberately UI-free (no WPF types) so it can move into a reusable
/// codec-visualization skill and be dropped into other programs.
/// </summary>
public static class CodecMath
{
    public enum Pred { None, Fixed2, Adaptive, Cascade, Lms }
    public enum Pack { Store, Rice, Range }

    public readonly struct CodecInfo
    {
        public readonly string Name, Desc, PredLabel, PackLabel;
        public readonly Pred Predictor;
        public readonly Pack Packer;
        public CodecInfo(string name, string desc, Pred pred, Pack pack, string predLabel, string packLabel)
        { Name = name; Desc = desc; Predictor = pred; Packer = pack; PredLabel = predLabel; PackLabel = packLabel; }
    }

    public static CodecInfo Info(string codec) => (codec ?? "").ToLowerInvariant() switch
    {
        "wav" => new CodecInfo("WAV", "uncompressed PCM - every sample stored exactly", Pred.None, Pack.Store, "store", "1:1"),
        "flac" => new CodecInfo("FLAC", "fixed / LPC predictor, then Rice-coded residual", Pred.Fixed2, Pack.Rice, "predict", "Rice pack"),
        "m4a" or "alac" => new CodecInfo("ALAC", "linear predictor + Rice/Golomb (Apple Lossless)", Pred.Fixed2, Pack.Rice, "predict", "Rice pack"),
        "tta" => new CodecInfo("TTA", "adaptive predictor + adaptive Rice (True Audio)", Pred.Adaptive, Pack.Rice, "adapt", "Rice pack"),
        "wv" => new CodecInfo("WavPack", "cascaded decorrelation + entropy coding", Pred.Cascade, Pack.Rice, "decorrelate", "entropy"),
        "tak" => new CodecInfo("TAK", "LPC prediction + Rice coding", Pred.Fixed2, Pack.Rice, "predict", "Rice pack"),
        "ape" => new CodecInfo("Monkey's Audio", "adaptive NLMS filters + range coder (max ratio)", Pred.Lms, Pack.Range, "adapt filter", "range code"),
        _ => new CodecInfo((codec ?? "").ToUpperInvariant(), "lossless prediction + entropy coding", Pred.Fixed2, Pack.Rice, "predict", "pack"),
    };

    /// <summary>Run the family predictor over <paramref name="s"/>; fill <paramref name="pred"/> (what
    /// it predicted) and <paramref name="resid"/> (the true error = s - pred). Arrays share length.</summary>
    public static void ComputeResidual(float[] s, Pred kind, float[] pred, float[] resid)
    {
        int n = s.Length;
        switch (kind)
        {
            case Pred.None:
                for (int i = 0; i < n; i++) { pred[i] = s[i]; resid[i] = 0; }
                break;
            case Pred.Fixed2:   // FLAC / ALAC / TAK: 2nd-order fixed polynomial predictor
                if (n > 0) { pred[0] = s[0]; resid[0] = 0; }
                if (n > 1) { pred[1] = s[1]; resid[1] = 0; }
                for (int i = 2; i < n; i++) { pred[i] = 2 * s[i - 1] - s[i - 2]; resid[i] = s[i] - pred[i]; }
                break;
            case Pred.Adaptive: // TTA-style order-1 adaptive predictor
            {
                double a = 1.0; if (n > 0) { pred[0] = s[0]; resid[0] = 0; }
                for (int i = 1; i < n; i++)
                {
                    double pr = a * s[i - 1];
                    double e = s[i] - pr;
                    a += 0.004 * e * s[i - 1]; if (a < 0) a = 0; if (a > 1.3) a = 1.3;
                    pred[i] = (float)pr; resid[i] = (float)e;
                }
                break;
            }
            case Pred.Cascade:  // WavPack-style cascaded decorrelation (two difference passes)
            {
                if (n > 0) { pred[0] = s[0]; resid[0] = 0; }
                float d1prev = 0, prev = n > 0 ? s[0] : 0;
                for (int i = 1; i < n; i++)
                {
                    float d1 = s[i] - prev;      // pass 1: first difference
                    float e = d1 - d1prev;       // pass 2: difference of differences
                    pred[i] = s[i] - e; resid[i] = e;
                    d1prev = d1; prev = s[i];
                }
                break;
            }
            case Pred.Lms:      // Monkey's Audio-style adaptive NLMS FIR filter (order 8)
            {
                const int P = 8; var wts = new double[P];
                for (int i = 0; i < n; i++)
                {
                    if (i < P) { pred[i] = s[i]; resid[i] = 0; continue; }
                    double pr = 0, norm = 1e-6;
                    for (int j = 0; j < P; j++) { pr += wts[j] * s[i - 1 - j]; norm += s[i - 1 - j] * s[i - 1 - j]; }
                    double e = s[i] - pr;
                    double g = 0.5 * e / norm;
                    for (int j = 0; j < P; j++) wts[j] += g * s[i - 1 - j];
                    pred[i] = (float)pr; resid[i] = (float)e;
                }
                break;
            }
        }
    }

    /// <summary>The Rice-code cost of the residual - the true bits/sample an encoder spends on this
    /// block (uncompressed PCM assumed 16-bit). Range coders do a hair better, but the residual size
    /// dominates and is what differs between codecs.</summary>
    public static double BitsPerSample(float[] resid, Pred kind)
    {
        int start = kind == Pred.Lms ? 8 : 2;
        int n = resid.Length;
        if (start >= n) return 16;
        double mean = 0; int nn = 0;
        for (int i = start; i < n; i++) { mean += Math.Abs(resid[i]) * 32768.0; nn++; }
        if (nn == 0) return 16;
        mean /= nn;
        int k = mean > 1 ? (int)Math.Round(Math.Log(mean, 2)) : 0; if (k < 0) k = 0; if (k > 15) k = 15;
        double bits = 0;
        for (int i = start; i < n; i++)
        {
            int v = (int)(Math.Abs(resid[i]) * 32768.0);
            bits += (v >> k) + 1 + k;   // Rice codeword length for |residual|
        }
        return Math.Max(1.0, Math.Min(16.0, bits / nn));
    }
}
