using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Pred = CUETools.Wpf.Controls.CodecMath.Pred;

namespace CUETools.Wpf.Controls;

/// <summary>
/// The convert round trip: source format -> PCM -> target format. A transcode decodes the source
/// back to plain PCM, then re-encodes it in the target codec, so the interesting thing is how the
/// SAME audio packs differently in each format. This shows exactly that: the real reconstructed PCM
/// in the middle, and on each side the codec's real compactness (bits/sample and packed bars),
/// computed from that PCM by the same predictor math the rip scope uses (see <see cref="CodecMath"/>).
/// Fed the real decoded source samples via <see cref="Samples"/>.
/// </summary>
public sealed class ConvertScope : FrameworkElement
{
    public static readonly DependencyProperty SourceCodecProperty = DependencyProperty.Register(
        nameof(SourceCodec), typeof(string), typeof(ConvertScope), new FrameworkPropertyMetadata("flac", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TargetCodecProperty = DependencyProperty.Register(
        nameof(TargetCodec), typeof(string), typeof(ConvertScope), new FrameworkPropertyMetadata("wav", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(ConvertScope), new PropertyMetadata(false));
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(float[]), typeof(ConvertScope), new PropertyMetadata(null, OnSamplesChanged));

    public string SourceCodec { get => (string)GetValue(SourceCodecProperty); set => SetValue(SourceCodecProperty, value); }
    public string TargetCodec { get => (string)GetValue(TargetCodecProperty); set => SetValue(TargetCodecProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public float[]? Samples { get => (float[]?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }

    private static readonly Color Teal = Color.FromRgb(0x34, 0xCF, 0xC0);
    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);
    private static readonly Color Ink = Color.FromRgb(0xED, 0xF1, 0xE9);
    private static readonly Color Muted = Color.FromRgb(0x7D, 0x88, 0x7C);
    private static readonly Color Line = Color.FromRgb(0x28, 0x31, 0x2A);
    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Typeface Mono = new("Consolas");

    private const int Roll = 640;
    private readonly float[] _roll = new float[Roll];
    private readonly float[] _demo = new float[Roll];
    private float[] _show;                       // _roll when real audio flows, else the idle demo
    private readonly float[] _predS = new float[Roll], _residS = new float[Roll];
    private readonly float[] _predT = new float[Roll], _residT = new float[Roll];
    private double _srcBitsEma = 12, _tgtBitsEma = 12;
    private double _phase;
    private TimeSpan _last;

    public ConvertScope()
    {
        _show = _roll;
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is float[] win && win.Length > 0) ((ConvertScope)d).Push(win);
    }

    private void Push(float[] win)
    {
        int m = Math.Min(win.Length, Roll);
        if (m < Roll) Array.Copy(_roll, m, _roll, 0, Roll - m);
        Array.Copy(win, Math.Max(0, win.Length - m), _roll, Roll - m, m);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        var t = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == default ? 0 : (t - _last).TotalSeconds;
        _last = t;
        _phase += dt * (Active ? 3.0 : 1.0);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var src = CodecMath.Info(SourceCodec);
        var tgt = CodecMath.Info(TargetCodec);

        // real audio when it is flowing; a gentle demo when idle so the round trip stays legible
        if (CodecMath.HasSignal(_roll)) _show = _roll;
        else { CodecMath.FillDemo(_demo, _phase); _show = _demo; }

        // both formats' real compactness on the SAME reconstructed PCM
        double srcBits = Bits(src.Predictor, _predS, _residS);
        double tgtBits = Bits(tgt.Predictor, _predT, _residT);
        _srcBitsEma += (srcBits - _srcBitsEma) * 0.12;
        _tgtBitsEma += (tgtBits - _tgtBitsEma) * 0.12;

        // header: the round trip and the net size change
        Text(dc, "ROUND TRIP", new Point(2, 0), Mono, 9, Muted, true);
        Text(dc, src.Name + "  ->  PCM  ->  " + tgt.Name, new Point(2, 13), Mono, 13, Ink, true);
        string net = $"{_srcBitsEma:0.0}  ->  {_tgtBitsEma:0.0} bits/sample";
        var nf = MakeText(net, Mono, 12, _tgtBitsEma <= _srcBitsEma ? Teal : Amber);
        dc.DrawText(nf, new Point(w - nf.Width - 2, 6));
        string verdict = _tgtBitsEma < _srcBitsEma - 0.1 ? "smaller" : _tgtBitsEma > _srcBitsEma + 0.1 ? "larger" : "same size";
        var vf = MakeText(verdict, Face, 10, Muted);
        dc.DrawText(vf, new Point(w - vf.Width - 2, 22));

        double top = 40, bot = h - 15, bh = bot - top;
        if (bh < 24) return;
        double gap = 14;
        double cw = (w - 2 * gap) / 3;
        var rSrc = new Rect(0, top, cw, bh);
        var rPcm = new Rect(cw + gap, top, cw, bh);
        var rTgt = new Rect(2 * (cw + gap), top, cw, bh);

        DrawCard(dc, rSrc); DrawCard(dc, rPcm); DrawCard(dc, rTgt);

        // source card: decode - packed data unpacks to audio (its bars are sized by its real ratio)
        dc.PushClip(new RectangleGeometry(rSrc, 8, 8));
        PackBars(dc, rSrc, _srcBitsEma / 16.0, src.Packer);
        dc.Pop();
        Label(dc, rSrc, src.PredLabel + " -> unpack", $"{_srcBitsEma:0.0} b/s");

        // PCM card: the real reconstructed audio, the shared currency of the round trip
        dc.PushClip(new RectangleGeometry(rPcm, 8, 8));
        Trace(dc, rPcm, _show, Teal, 1.6);
        dc.Pop();
        Label(dc, rPcm, "PCM", "16.0 b/s");

        // target card: encode - predict + residual, then packed at its real ratio
        dc.PushClip(new RectangleGeometry(rTgt, 8, 8));
        if (tgt.Predictor == Pred.None) StoreStage(dc, rTgt);
        else PackBars(dc, rTgt, _tgtBitsEma / 16.0, tgt.Packer);
        dc.Pop();
        Label(dc, rTgt, "encode -> " + tgt.PackLabel, $"{_tgtBitsEma:0.0} b/s");

        Arrow(dc, new Point(rSrc.Right + 1, top + bh / 2), new Point(rPcm.Left - 1, top + bh / 2));
        Arrow(dc, new Point(rPcm.Right + 1, top + bh / 2), new Point(rTgt.Left - 1, top + bh / 2));
    }

    private double Bits(Pred kind, float[] pred, float[] resid)
    {
        if (kind == Pred.None) return 16.0;
        CodecMath.ComputeResidual(_show, kind, pred, resid);
        return CodecMath.BitsPerSample(resid, kind);
    }

    // packed vs raw 16-bit columns; shorter packed columns = more compression
    private void PackBars(DrawingContext dc, Rect r, double ratio, CodecMath.Pack pack)
    {
        double frac = Math.Max(0.06, Math.Min(1, ratio));
        int cols = 8; double pad = 8, cw = (r.Width - pad * 2) / (cols * 1.7);
        double baseY = r.Bottom - 8, fullH = r.Height - 18;
        var raw = new SolidColorBrush(Color.FromArgb(50, Teal.R, Teal.G, Teal.B)); raw.Freeze();
        var packed = new SolidColorBrush(pack == CodecMath.Pack.Range ? Color.FromRgb(0x5f, 0xE0, 0xD3) : Teal); packed.Freeze();
        for (int i = 0; i < cols; i++)
        {
            double x = r.Left + pad + i * cw * 1.7;
            dc.DrawRectangle(raw, null, new Rect(x, baseY - fullH, cw, fullH));
            double ph = fullH * frac * (0.9 + 0.1 * Math.Sin(_phase * 3 + i));
            dc.DrawRectangle(packed, null, new Rect(x, baseY - ph, cw, ph));
        }
    }

    private void StoreStage(DrawingContext dc, Rect r)
    {
        Trace(dc, r, _show, Amber, 1.4);
        var tick = new Pen(new SolidColorBrush(Color.FromArgb(80, Muted.R, Muted.G, Muted.B)), 1); tick.Freeze();
        for (double bx = r.Left + 6; bx < r.Right - 4; bx += 7) dc.DrawLine(tick, new Point(bx, r.Bottom - 8), new Point(bx, r.Bottom - 4));
    }

    private void Label(DrawingContext dc, Rect r, string left, string right)
    {
        Text(dc, left, new Point(r.Left + 3, r.Bottom + 1), Mono, 9, Muted, false);
        var rf = MakeText(right, Mono, 9, Muted);
        dc.DrawText(rf, new Point(r.Right - rf.Width - 3, r.Bottom + 1));
    }

    private void Trace(DrawingContext dc, Rect r, float[] data, Color c, double thick)
    {
        double mid = r.Top + r.Height / 2, amp = r.Height * 0.42;
        double left = r.Left + 5, wide = r.Width - 10;
        var fig = new PathFigure { IsClosed = false };
        int n = data.Length; bool first = true;
        int stepPx = Math.Max(1, n / (int)Math.Max(1, wide));
        for (int i = 0; i < n; i += stepPx)
        {
            double v = data[i]; if (v > 1.4) v = 1.4; if (v < -1.4) v = -1.4;
            var pt = new Point(left + wide * i / (n - 1.0), mid - v * amp);
            if (first) { fig.StartPoint = pt; first = false; } else fig.Segments.Add(new LineSegment(pt, true));
        }
        var geo = new PathGeometry(new[] { fig }); geo.Freeze();
        var pen = new Pen(new SolidColorBrush(c), thick) { LineJoin = PenLineJoin.Round }; pen.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static void DrawCard(DrawingContext dc, Rect r)
    {
        var bg = new SolidColorBrush(Color.FromArgb(80, 14, 19, 17)); bg.Freeze();
        var edge = new Pen(new SolidColorBrush(Line), 1); edge.Freeze();
        dc.DrawRoundedRectangle(bg, edge, r, 8, 8);
    }

    private static void Arrow(DrawingContext dc, Point a, Point b)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 125, 136, 124)), 1.6) { EndLineCap = PenLineCap.Round }; pen.Freeze();
        dc.DrawLine(pen, a, b);
        dc.DrawLine(pen, b, new Point(b.X - 4, b.Y - 3));
        dc.DrawLine(pen, b, new Point(b.X - 4, b.Y + 3));
    }

    private static FormattedText MakeText(string s, Typeface tf, double size, Color c)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, new SolidColorBrush(c), 1.0);

    private static void Text(DrawingContext dc, string s, Point p, Typeface tf, double size, Color c, bool bold)
    {
        var ft = MakeText(s, bold ? new Typeface(tf.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : tf, size, c);
        dc.DrawText(ft, p);
    }
}
