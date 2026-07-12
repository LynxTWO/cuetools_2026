using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CUETools.Wpf.Controls;

/// <summary>
/// A cross-section of a CD, drawn to scale-ish: the laser enters from the clear reading side, passes
/// through the 1.2 mm polycarbonate, focuses on the pits at the thin aluminium reflective layer, and
/// reflects back. This is the "why" behind CTDB repair and the clear-side-vs-label-side asymmetry: a
/// scratch on the clear side is out of focus (often recoverable), a pin-hole or scratch on the label
/// side destroys the reflective layer itself (the data is gone). Dimensions are the real CD spec.
/// </summary>
public sealed class LayerCrossSection : FrameworkElement
{
    private static readonly Color Teal = Color.FromRgb(0x34, 0xCF, 0xC0);
    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);
    private static readonly Color Crit = Color.FromRgb(0xEF, 0x6D, 0x6D);
    private static readonly Color Good = Color.FromRgb(0x5C, 0xCB, 0x8B);
    private static readonly Color Ink = Color.FromRgb(0xD4, 0xDC, 0xD2);
    private static readonly Color Mut = Color.FromRgb(0x7C, 0x8A, 0x84);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double left = 4, right = w - 150, bandW = right - left;   // leave room for labels on the right
        // layer bands from the top (label) down to the reading side
        double yLabel = 8, hLabel = 12;
        double yLacq = yLabel + hLabel, hLacq = 7;
        double yAlu = yLacq + hLacq, hAlu = 7;
        double yPoly = yAlu + hAlu, hPoly = h - yPoly - 34;       // the thick 1.2 mm substrate
        double yRead = yPoly + hPoly;                             // the clear reading surface

        Band(dc, left, yLabel, bandW, hLabel, Color.FromRgb(0x2A, 0x2E, 0x33), "label");
        Band(dc, left, yLacq, bandW, hLacq, Color.FromRgb(0x3A, 0x34, 0x22), "lacquer");
        // aluminium reflective layer with pit notches
        Band(dc, left, yAlu, bandW, hAlu, Color.FromRgb(0xB8, 0xBE, 0xC6), null);
        var pit = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x16));
        pit.Freeze();
        for (int i = 0; i < 9; i++)
        {
            double px = left + bandW * (0.12 + 0.09 * i) + (i % 2) * 3;
            dc.DrawRectangle(pit, null, new Rect(px, yAlu + 1.5, 5 + (i % 3) * 3, hAlu - 3));
        }
        // polycarbonate substrate (clear, faint blue)
        Band(dc, left, yPoly, bandW, hPoly, Color.FromArgb(0x30, 0x6C, 0xB6, 0xD6), null);

        // the laser: a focusing cone from the reading side up to a pit on the aluminium, then a faint
        // reflection back down
        double cx = left + bandW * 0.5, focusY = yAlu + hAlu;
        var beam = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x6A, 0x5A));
        beam.Freeze();
        var cone = new PathGeometry(new[]
        {
            new PathFigure(new Point(cx - 26, h - 2), new[]
            {
                (System.Windows.Media.PathSegment)new LineSegment(new Point(cx, focusY), true),
                new LineSegment(new Point(cx + 26, h - 2), true)
            }, true)
        });
        cone.Freeze();
        dc.DrawGeometry(beam, null, cone);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x4A)), 1.4);
        pen.Freeze();
        dc.DrawLine(pen, new Point(cx, h - 2), new Point(cx, focusY));

        // layer labels (right), with leader lines
        Label(dc, right, yLabel + hLabel / 2, "label (printed)", Ink);
        Label(dc, right, yLacq + hLacq / 2, "lacquer", Mut);
        Label(dc, right, yAlu + hAlu / 2, "aluminium (data)", Teal);
        Label(dc, right, yPoly + hPoly / 2, "polycarbonate 1.2mm", Mut);

        // the asymmetry, the real lesson
        Note(dc, left, yLabel - 2, "label side: damage here is fatal", Crit, above: true);
        Note(dc, left, h - 2, "clear side: laser reads through (scratches recover)", Good, above: false);
    }

    private static void Band(DrawingContext dc, double x, double y, double w, double h, Color c, string? inlineLabel)
    {
        var b = new SolidColorBrush(c); b.Freeze();
        dc.DrawRectangle(b, null, new Rect(x, y, w, h));
        if (inlineLabel != null && h >= 10)
        {
            var ft = Text(inlineLabel, 9, Color.FromArgb(0xC0, 0xE0, 0xE6, 0xDE));
            dc.DrawText(ft, new Point(x + 6, y + (h - ft.Height) / 2));
        }
    }

    private static void Label(DrawingContext dc, double x, double y, string s, Color c)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x70, Mut.R, Mut.G, Mut.B)), 1); pen.Freeze();
        dc.DrawLine(pen, new Point(x - 6, y), new Point(x + 6, y));
        var ft = Text(s, 10.5, c);
        dc.DrawText(ft, new Point(x + 10, y - ft.Height / 2));
    }

    private static void Note(DrawingContext dc, double x, double y, string s, Color c, bool above)
    {
        var ft = Text(s, 9.5, c);
        dc.DrawText(ft, new Point(x, above ? y - ft.Height : y - ft.Height));
    }

    private static FormattedText Text(string s, double size, Color c) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Consolas"), size, new SolidColorBrush(c), 1.0);
}
