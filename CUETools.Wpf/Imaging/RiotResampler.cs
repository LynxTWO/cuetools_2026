using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CUETools.Wpf.Imaging;

/// <summary>
/// Downscales images the way RIOT (riot-optimizer.com) does for album art: a Mitchell-Netravali
/// bicubic filter (B = C = 1/3) applied in gamma space (directly on the sRGB byte values, no
/// linearization) with no sharpening. Empirically confirmed against RIOT's own resampled test set
/// (TESTIMAGES 2400x2400 -> 600x600). Separable two-pass; the filter footprint scales with the
/// minification factor so it low-pass filters instead of aliasing.
/// </summary>
public static class RiotResampler
{
    /// <summary>Resize a BGRA32 pixel buffer to dw x dh. Each channel (incl. alpha) resampled
    /// independently in gamma space. Pure - no I/O - so it is testable against reference images.</summary>
    public static byte[] ResizeBgra(byte[] src, int sw, int sh, int dw, int dh)
    {
        if (sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0) throw new ArgumentException("bad size");

        // horizontal pass: sw x sh -> dw x sh (float)
        var hx = BuildTaps(sw, dw);
        float[] mid = new float[dw * sh * 4];
        for (int y = 0; y < sh; y++)
        {
            int srow = y * sw * 4;
            int drow = y * dw * 4;
            for (int x = 0; x < dw; x++)
            {
                var t = hx[x];
                float b = 0, g = 0, r = 0, a = 0;
                for (int i = 0; i < t.Idx.Length; i++)
                {
                    int p = srow + t.Idx[i] * 4;
                    float w = t.Wt[i];
                    b += src[p] * w; g += src[p + 1] * w; r += src[p + 2] * w; a += src[p + 3] * w;
                }
                int d = drow + x * 4;
                mid[d] = b; mid[d + 1] = g; mid[d + 2] = r; mid[d + 3] = a;
            }
        }

        // vertical pass: dw x sh -> dw x dh (byte)
        var vy = BuildTaps(sh, dh);
        byte[] dst = new byte[dw * dh * 4];
        for (int y = 0; y < dh; y++)
        {
            var t = vy[y];
            int drow = y * dw * 4;
            for (int x = 0; x < dw; x++)
            {
                float b = 0, g = 0, r = 0, a = 0;
                for (int i = 0; i < t.Idx.Length; i++)
                {
                    int p = (t.Idx[i] * dw + x) * 4;
                    float w = t.Wt[i];
                    b += mid[p] * w; g += mid[p + 1] * w; r += mid[p + 2] * w; a += mid[p + 3] * w;
                }
                int d = drow + x * 4;
                dst[d] = Clamp(b); dst[d + 1] = Clamp(g); dst[d + 2] = Clamp(r); dst[d + 3] = Clamp(a);
            }
        }
        return dst;
    }

    /// <summary>WPF convenience: resize any BitmapSource to a Bgra32 result of dw x dh.</summary>
    public static BitmapSource Resize(BitmapSource source, int dw, int dh)
    {
        var bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int sw = bgra.PixelWidth, sh = bgra.PixelHeight;
        byte[] src = new byte[sw * sh * 4];
        bgra.CopyPixels(src, sw * 4, 0);
        byte[] outp = ResizeBgra(src, sw, sh, dw, dh);
        return BitmapSource.Create(dw, dh, 96, 96, PixelFormats.Bgra32, null, outp, dw * 4);
    }

    private readonly struct Tap { public Tap(int[] idx, float[] wt) { Idx = idx; Wt = wt; } public int[] Idx { get; } public float[] Wt { get; } }

    // One set of source taps per output coordinate. Filter scale s = max(1, src/dst); support 2s.
    private static Tap[] BuildTaps(int srcN, int dstN)
    {
        double scale = (double)srcN / dstN;
        double s = Math.Max(1.0, scale);
        double support = 2.0 * s;
        var taps = new Tap[dstN];
        for (int o = 0; o < dstN; o++)
        {
            double center = (o + 0.5) * scale - 0.5;
            int lo = (int)Math.Ceiling(center - support);
            int hi = (int)Math.Floor(center + support);
            int n = hi - lo + 1;
            var idx = new int[n];
            var wt = new float[n];
            double sum = 0;
            for (int k = 0; k < n; k++)
            {
                int sx = lo + k;
                double w = Mitchell((sx - center) / s);
                idx[k] = sx < 0 ? 0 : sx >= srcN ? srcN - 1 : sx;   // clamp to edge
                wt[k] = (float)w;
                sum += w;
            }
            if (sum != 0) for (int k = 0; k < n; k++) wt[k] = (float)(wt[k] / sum);
            taps[o] = new Tap(idx, wt);
        }
        return taps;
    }

    // Mitchell-Netravali with B = C = 1/3.
    private static double Mitchell(double x)
    {
        x = Math.Abs(x);
        double x2 = x * x, x3 = x2 * x;
        if (x < 1.0) return (7.0 * x3 - 12.0 * x2 + 16.0 / 3.0) / 6.0;
        if (x < 2.0) return (-7.0 / 3.0 * x3 + 12.0 * x2 - 20.0 * x + 32.0 / 3.0) / 6.0;
        return 0.0;
    }

    private static byte Clamp(float v) => v <= 0 ? (byte)0 : v >= 255 ? (byte)255 : (byte)(v + 0.5f);
}
