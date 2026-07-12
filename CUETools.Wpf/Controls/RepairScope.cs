using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Controls;

/// <summary>
/// Shows CTDB's Reed-Solomon parity repair for real. When a rip has errors the drive could not read
/// cleanly, the CUETools database has parity computed across the whole disc and can reconstruct the
/// exact damaged samples. This draws what actually happened, from the real CDRepairFix:
///
///  - <see cref="Map"/> is a downsample of the true AffectedSectorArray (one bit per CD sector), so
///    the strip shows exactly WHERE the disc was damaged - a scratch shows as a cluster.
///  - <see cref="Samples"/> / <see cref="Sectors"/> are the real corrected-sample and damaged-sector
///    counts; <see cref="Npar"/> is the real parity depth used (4/8/16).
///  - The pipeline (syndrome -> locate -> Chien -> Forney -> apply) is the real RS decode. The first
///    four stages are computed during the Verify pass, so they light as soon as errors are found; the
///    fifth (apply) is the destructive write and lights only during / after Repair.
///
/// States: recoverable (post-verify, damage shown amber, parity sufficient), repairing (a sweep
/// reconstructs left-to-right, tied to the real repair <see cref="Progress"/>), repaired (all green).
/// </summary>
public sealed class RepairScope : FrameworkElement
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(RepairScope), new PropertyMetadata(false));
    public static readonly DependencyProperty AppliedProperty = DependencyProperty.Register(
        nameof(Applied), typeof(bool), typeof(RepairScope), new PropertyMetadata(false));
    public static readonly DependencyProperty RecoverableProperty = DependencyProperty.Register(
        nameof(Recoverable), typeof(bool), typeof(RepairScope), new PropertyMetadata(false));
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(RepairScope), new PropertyMetadata(0.0));
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(int), typeof(RepairScope), new PropertyMetadata(0));
    public static readonly DependencyProperty SectorsProperty = DependencyProperty.Register(
        nameof(Sectors), typeof(int), typeof(RepairScope), new PropertyMetadata(0));
    public static readonly DependencyProperty NparProperty = DependencyProperty.Register(
        nameof(Npar), typeof(int), typeof(RepairScope), new PropertyMetadata(0));
    public static readonly DependencyProperty MapProperty = DependencyProperty.Register(
        nameof(Map), typeof(double[]), typeof(RepairScope), new PropertyMetadata(null));

    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public bool Applied { get => (bool)GetValue(AppliedProperty); set => SetValue(AppliedProperty, value); }
    public bool Recoverable { get => (bool)GetValue(RecoverableProperty); set => SetValue(RecoverableProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public int Samples { get => (int)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public int Sectors { get => (int)GetValue(SectorsProperty); set => SetValue(SectorsProperty, value); }
    public int Npar { get => (int)GetValue(NparProperty); set => SetValue(NparProperty, value); }
    public double[]? Map { get => (double[]?)GetValue(MapProperty); set => SetValue(MapProperty, value); }

    private static readonly Color Teal = Color.FromRgb(0x34, 0xCF, 0xC0);
    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);
    private static readonly Color Crit = Color.FromRgb(0xEF, 0x6D, 0x6D);
    private static readonly Color Good = Color.FromRgb(0x5C, 0xCB, 0x8B);
    private static readonly Color Ink = Color.FromRgb(0xD4, 0xDC, 0xD2);
    private static readonly Color Mut = Color.FromRgb(0x7C, 0x8A, 0x84);

    private static readonly string[] Stages = { "syndrome", "locate", "Chien", "Forney", "apply" };

    private double _sweep;   // smoothed repair sweep 0..1
    private double _phase;   // pulse phase
    private DateTime _last = DateTime.Now;

    public RepairScope()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.05, (now - _last).TotalSeconds);
        _last = now;
        double target = Applied ? 1.0 : Active ? Math.Max(0, Math.Min(1, Progress)) : 0.0;
        _sweep += (target - _sweep) * 0.15;
        _phase += dt * 2.2;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // ---- headline ----
        string headline = Applied
            ? $"Recovered {Fmt(Samples)} samples across {Sectors} sector" + (Sectors == 1 ? "" : "s")
            : Active
                ? "Reconstructing damaged sectors from parity..."
                : $"{Fmt(Samples)} samples across {Sectors} sector" + (Sectors == 1 ? "" : "s") + " - recoverable from parity";
        Color hColor = Applied ? Good : Amber;
        Text(dc, headline, 1, 0, 14, hColor, bold: true);

        // state pill (right)
        string pill = Applied ? "REPAIRED" : Active ? "REPAIRING" : "REPAIRABLE";
        Color pc = Applied ? Good : Amber;
        var pillFt = MakeText(pill, 9.5, pc, bold: true);
        double pillW = pillFt.Width + 16, pillH = 17, pillX = w - pillW - 1;
        var pillRect = new Rect(pillX, 1, pillW, pillH);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(28, pc.R, pc.G, pc.B)),
            new Pen(new SolidColorBrush(Color.FromArgb(150, pc.R, pc.G, pc.B)), 1), pillRect, 8, 8);
        dc.DrawText(pillFt, new Point(pillX + 8, 3));

        // parity structure sub-line (real numbers)
        string sub = Npar > 0
            ? $"Reed-Solomon  .  npar={Npar} parity symbols / 10-sector stride  .  GF(2^16)"
            : "Reed-Solomon parity";
        Text(dc, sub, 1, 20, 10.5, Mut);

        // ---- damage / repair sector strip (the real AffectedSectorArray) ----
        double bandY = 46, bandH = 22, bandL = 1, bandR = w - 1, bandW = bandR - bandL;
        var track = new RectangleGeometry(new Rect(bandL, bandY, bandW, bandH), 4, 4);
        track.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x10, 0x16, 0x14)), null, track);

        var map = Map;
        dc.PushClip(track);
        if (map != null && map.Length > 0)
        {
            int B = map.Length;
            double bw = bandW / B;
            for (int b = 0; b < B; b++)
            {
                double d = map[b];
                if (d <= 0) continue;
                double cx = (b + 0.5) / B;
                bool recovered = Applied || (Active && cx <= _sweep);
                Color c = recovered ? Good : Lerp(Amber, Crit, d);
                byte al = (byte)(90 + 150 * Math.Min(1, 0.4 + 0.6 * d));   // single-sector damage still visible
                double x = bandL + b * bw;
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(al, c.R, c.G, c.B)), null,
                    new Rect(x, bandY + 2, Math.Max(1.0, bw - 0.5), bandH - 4));
            }
            // the reconstruction sweep head (tied to the real repair progress)
            if (Active && _sweep > 0.001 && _sweep < 0.999)
            {
                double hx = bandL + _sweep * bandW;
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, Good.R, Good.G, Good.B)), null,
                    new Rect(hx - 5, bandY, 10, bandH));
                var head = new Pen(new SolidColorBrush(Good), 1.8);
                head.Freeze();
                dc.DrawLine(head, new Point(hx, bandY - 1), new Point(hx, bandY + bandH + 1));
            }
        }
        dc.Pop();
        var edge = new Pen(new SolidColorBrush(Color.FromArgb(60, Ink.R, Ink.G, Ink.B)), 1);
        edge.Freeze();
        dc.DrawGeometry(null, edge, track);
        Text(dc, "disc  (inside", 1, bandY + bandH + 2, 8.5, Mut);
        var outFt = MakeText("outside)", 8.5, Mut);
        dc.DrawText(outFt, new Point(bandR - outFt.Width, bandY + bandH + 2));

        // ---- RS pipeline: first four stages computed at verify, "apply" is the repair write ----
        double py = h - 24, gap = 8;
        double totalW = bandW;
        double chipW = (totalW - gap * (Stages.Length - 1)) / Stages.Length;
        for (int i = 0; i < Stages.Length; i++)
        {
            double x = bandL + i * (chipW + gap);
            bool isApply = i == Stages.Length - 1;
            // stages 0..3 are done once errors are located (verify pass); apply is done only on repair
            bool done = isApply ? Applied : (Recoverable || Active || Applied);
            bool running = isApply && Active;
            Color c = done ? (isApply ? Good : Teal) : Mut;
            double pulse = running ? 0.5 + 0.5 * Math.Sin(_phase * 2) : 1.0;
            byte fill = (byte)((done ? 34 : 16) * pulse + 6);
            var chip = new Rect(x, py, chipW, 18);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(fill, c.R, c.G, c.B)),
                new Pen(new SolidColorBrush(Color.FromArgb((byte)(done ? 150 : 60), c.R, c.G, c.B)), 1), chip, 5, 5);
            var ft = MakeText(Stages[i], 9.5, done ? c : Mut);
            dc.DrawText(ft, new Point(x + (chipW - ft.Width) / 2, py + 3));
            if (i < Stages.Length - 1)
            {
                var ar = new Pen(new SolidColorBrush(Color.FromArgb(120, Mut.R, Mut.G, Mut.B)), 1);
                ar.Freeze();
                double ax = x + chipW + gap / 2;
                dc.DrawLine(ar, new Point(ax - 2.5, py + 9), new Point(ax + 2.5, py + 9));
            }
        }
    }

    private static string Fmt(int n) => n.ToString("#,0", CultureInfo.InvariantCulture);

    private void Text(DrawingContext dc, string s, double x, double y, double size, Color c, bool bold = false)
        => dc.DrawText(MakeText(s, size, c, bold), new Point(x, y));

    private static FormattedText MakeText(string s, double size, Color c, bool bold = false)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Consolas"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            size, new SolidColorBrush(c), 1.0);

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }
}
