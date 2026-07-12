using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Controls;

/// <summary>
/// Shows a REAL sector re-read in progress, driven by the drive's own retry loop - not a canned
/// animation. It appears only when the drive is doing EXTRA passes over a window that did not agree
/// with itself (a scratched or pin-holed disc) and hides again once the read moves on.
///
/// Every number is the truth from the ripper's ReadProgress event:
///  - <see cref="Count"/>  = how many times BEYOND the guaranteed minimum this window has been
///    re-read (pass - correctionQuality). This is "how many times it is getting re-read".
///  - <see cref="Errors"/> = how many sectors in the window still disagree between passes.
///  - <see cref="Max"/>    = the drive's hard cap on re-reads (Burst/Secure/Paranoid = 16/32/64
///    passes, minus the minimum), after which unresolved sectors are given up on.
/// The band is the stuck window, the dark pits are the disagreeing sectors, and the bright head is
/// one pass sweeping across. The head sweeps SLOWER as the count climbs, mirroring the drive
/// actually dropping its read speed on a hard pass. When the sectors finally agree the band turns
/// green ("recovered"); if the drive gives up it fades out red and CTDB repair can take over.
/// </summary>
public sealed class RereadScope : FrameworkElement
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(RereadScope), new PropertyMetadata(false));
    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
        nameof(Count), typeof(int), typeof(RereadScope), new PropertyMetadata(0));
    public static readonly DependencyProperty MaxProperty = DependencyProperty.Register(
        nameof(Max), typeof(int), typeof(RereadScope), new PropertyMetadata(30));
    public static readonly DependencyProperty ErrorsProperty = DependencyProperty.Register(
        nameof(Errors), typeof(int), typeof(RereadScope), new PropertyMetadata(0));

    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    public int Max { get => (int)GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public int Errors { get => (int)GetValue(ErrorsProperty); set => SetValue(ErrorsProperty, value); }

    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);
    private static readonly Color Crit = Color.FromRgb(0xEF, 0x6D, 0x6D);
    private static readonly Color Good = Color.FromRgb(0x5C, 0xCB, 0x8B);
    private static readonly Color Ink = Color.FromRgb(0xD4, 0xDC, 0xD2);

    private double _alpha;   // fade in/out 0..1
    private double _sweep;   // read-head position 0..1
    private DateTime _last = DateTime.Now;

    public RereadScope()
    {
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.05, (now - _last).TotalSeconds);
        _last = now;

        double target = Active ? 1 : 0;
        _alpha += (target - _alpha) * (target > _alpha ? 0.25 : 0.06);

        // The head sweeps slower as the re-read count climbs, mirroring the real drive dropping its
        // read speed on a hard pass (32500 -> 300 -> 150 -> 75 sectors/s in the ripper).
        double speed = 2.1 - 1.6 * Math.Min(1.0, Count / 8.0);   // sweeps per second (~2.1 down to 0.5)
        _sweep += dt * Math.Max(0.4, speed);
        if (_sweep >= 1) _sweep -= 1;

        if (_alpha < 0.004 && !Active) { _alpha = 0; return; }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || _alpha <= 0.004) return;

        bool converged = Errors <= 0 && Count > 0;
        double sev = Math.Min(1.0, Count / (double)Math.Max(1, Max));
        Color hot = converged ? Good : Lerp(Amber, Crit, sev);
        byte a = (byte)(255 * Math.Min(1, _alpha));

        // headline count "x7" - how many times this spot has been re-read
        var countFt = new FormattedText("x" + Math.Max(1, Count), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 22,
            new SolidColorBrush(Color.FromArgb(a, hot.R, hot.G, hot.B)), 1.0);
        dc.DrawText(countFt, new Point(1, 0));

        // status to the right of the count
        string sub = converged ? "recovered"
            : Errors == 1 ? "1 sector disagrees"
            : Errors + " sectors disagree";
        var subFt = new FormattedText(sub, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), 10.5, new SolidColorBrush(Color.FromArgb((byte)(a * 0.85), Ink.R, Ink.G, Ink.B)), 1.0);
        dc.DrawText(subFt, new Point(countFt.Width + 9, 8));

        // the band = the stuck sector window
        double bandTop = h - 15, bandH = 9, bandL = 1, bandR = w - 1, bandW = bandR - bandL;
        var track = new RectangleGeometry(new Rect(bandL, bandTop, bandW, bandH), 3, 3);
        track.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(a * 0.18), hot.R, hot.G, hot.B)), null, track);
        var edge = new Pen(new SolidColorBrush(Color.FromArgb((byte)(a * 0.5), hot.R, hot.G, hot.B)), 1);
        edge.Freeze();
        dc.DrawGeometry(null, edge, track);

        dc.PushClip(track);
        // error pits: one dark notch per disagreeing sector, stable position per index (the physical
        // defect that keeps failing - like a pin-hole in the data layer)
        int pits = Math.Min(Errors, 48);
        var pitBrush = new SolidColorBrush(Color.FromArgb((byte)(a * 0.92), 0x12, 0x16, 0x14));
        pitBrush.Freeze();
        for (int i = 0; i < pits; i++)
        {
            double frac = Frac(i * 0.61803398875 + 0.13);
            double px = bandL + 3 + frac * (bandW - 6);
            dc.DrawRoundedRectangle(pitBrush, null, new Rect(px - 1.3, bandTop + 1.5, 2.6, bandH - 3), 1, 1);
        }
        // the read head: one pass sweeping across the window
        double hx = bandL + _sweep * bandW;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(a * 0.5), hot.R, hot.G, hot.B)), null,
            new Rect(hx - 4, bandTop, 8, bandH));
        var head = new Pen(new SolidColorBrush(Color.FromArgb(a, hot.R, hot.G, hot.B)), 1.6);
        head.Freeze();
        dc.DrawLine(head, new Point(hx, bandTop - 1.5), new Point(hx, bandTop + bandH + 1.5));
        dc.Pop();
    }

    private static double Frac(double x) => x - Math.Floor(x);

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
