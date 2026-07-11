using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CUETools.Wpf.Controls;

/// <summary>
/// A front-loading optical drive drawn in elevation: a fixed bezel with a slot, and a tray that
/// slides out of it carrying the disc. Bind <see cref="Open"/> to the tray state; the control
/// animates <see cref="Openness"/> (0 = retracted, 1 = fully out) with real-world physics -
/// opening accelerates then settles into the mechanical stop (a slight overshoot), closing pulls
/// in and stops firmly. GPU-composited retained-mode drawing; the DoubleAnimation on an
/// AffectsRender property re-renders each frame for free (no CompositionTarget loop needed).
/// </summary>
public sealed class DiscTray : FrameworkElement
{
    public static readonly DependencyProperty OpenProperty = DependencyProperty.Register(
        nameof(Open), typeof(bool), typeof(DiscTray), new PropertyMetadata(false, OnOpenChanged));

    /// <summary>True to slide the tray out, false to pull it in. Drives the animation.</summary>
    public bool Open { get => (bool)GetValue(OpenProperty); set => SetValue(OpenProperty, value); }

    public static readonly DependencyProperty OpennessProperty = DependencyProperty.Register(
        nameof(Openness), typeof(double), typeof(DiscTray),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0 = tray fully in, 1 = fully out. Animated; also settable for a static pose.</summary>
    public double Openness { get => (double)GetValue(OpennessProperty); set => SetValue(OpennessProperty, value); }

    public static readonly DependencyProperty HasDiscProperty = DependencyProperty.Register(
        nameof(HasDisc), typeof(bool), typeof(DiscTray),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Whether a disc rides on the tray (drawn once the tray is out far enough).</summary>
    public bool HasDisc { get => (bool)GetValue(HasDiscProperty); set => SetValue(HasDiscProperty, value); }

    private static readonly Color Teal = Color.FromRgb(0x34, 0xcf, 0xc0);

    private static void OnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DiscTray)d).Animate((bool)e.NewValue);

    private void Animate(bool open)
    {
        var anim = new DoubleAnimation
        {
            To = open ? 1.0 : 0.0,
            // opening a motorised tray takes a beat longer than snapping it shut
            Duration = TimeSpan.FromMilliseconds(open ? 640 : 460),
            EasingFunction = open
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.26 }  // glides out, settles at the stop
                : new CubicEase { EasingMode = EasingMode.EaseIn }                     // draws in, firm close
        };
        BeginAnimation(OpennessProperty, anim);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double open = Math.Max(0, Math.Min(1, Openness));

        double bezelH = h * 0.30;
        double slotY = bezelH * 0.80;
        double travel = h - bezelH - 4;
        double extend = open * travel;

        double trayL = w * 0.10, trayR = w * 0.90;
        double trayW = trayR - trayL;
        double trayTop = slotY - 3;
        double trayBot = slotY + extend;

        // everything below the slot is "outside the drive"; clip the tray to it so it reads as
        // emerging from the slot rather than floating.
        dc.PushClip(new RectangleGeometry(new Rect(0, slotY, w, h - slotY)));

        if (extend > 2)
        {
            // soft shadow that grows as the tray comes out
            var shadow = new SolidColorBrush(Color.FromArgb((byte)(70 * open), 0, 0, 0));
            shadow.Freeze();
            dc.DrawRoundedRectangle(shadow, null, new Rect(trayL + 4, trayTop + 6, trayW, trayBot - trayTop), 9, 9);

            // the tray shelf (light brushed metal)
            var tray = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            tray.GradientStops.Add(new GradientStop(Color.FromRgb(0x2c, 0x33, 0x2e), 0));
            tray.GradientStops.Add(new GradientStop(Color.FromRgb(0x18, 0x1d, 0x19), 1));
            tray.Freeze();
            var trayPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 0.8);
            trayPen.Freeze();
            dc.DrawRoundedRectangle(tray, trayPen, new Rect(trayL, trayTop, trayW, trayBot - trayTop), 8, 8);

            // the circular disc well, sitting near the leading (outer) end of the tray
            double wellR = Math.Min(trayW * 0.42, (trayBot - slotY) * 0.5);
            if (wellR > 6)
            {
                var cx = (trayL + trayR) / 2;
                var cy = trayBot - wellR - 6;
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x11)), null, new Point(cx, cy), wellR + 3, wellR + 3);

                if (HasDisc)
                {
                    // disc body: dark platter with a faint radial sheen and a teal-lit hub
                    var body = new RadialGradientBrush { GradientOrigin = new Point(0.42, 0.4), Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
                    body.GradientStops.Add(new GradientStop(Color.FromRgb(0x1a, 0x22, 0x1e), 0));
                    body.GradientStops.Add(new GradientStop(Color.FromRgb(0x0c, 0x10, 0x0e), 0.7));
                    body.GradientStops.Add(new GradientStop(Color.FromRgb(0x06, 0x09, 0x07), 1));
                    body.Freeze();
                    dc.DrawEllipse(body, null, new Point(cx, cy), wellR, wellR);

                    // a thin iridescent ring to sell the disc surface
                    var ring = new Pen(new SolidColorBrush(Color.FromArgb(46, Teal.R, Teal.G, Teal.B)), 1);
                    ring.Freeze();
                    dc.DrawEllipse(null, ring, new Point(cx, cy), wellR * 0.74, wellR * 0.74);

                    // hub + centre hole
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x0d, 0x12, 0x0f)), null, new Point(cx, cy), wellR * 0.30, wellR * 0.30);
                    var hubPen = new Pen(new SolidColorBrush(Color.FromArgb(120, Teal.R, Teal.G, Teal.B)), 1.2);
                    hubPen.Freeze();
                    dc.DrawEllipse(null, hubPen, new Point(cx, cy), wellR * 0.30, wellR * 0.30);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x04, 0x06, 0x05)), null, new Point(cx, cy), wellR * 0.13, wellR * 0.13);
                }
            }
        }
        dc.Pop(); // clip

        // the drive bezel (fixed face), drawn last so the tray slides out from behind it
        var bezel = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        bezel.GradientStops.Add(new GradientStop(Color.FromRgb(0x20, 0x25, 0x22), 0));
        bezel.GradientStops.Add(new GradientStop(Color.FromRgb(0x0d, 0x11, 0x0e), 1));
        bezel.Freeze();
        dc.DrawRoundedRectangle(bezel, null, new Rect(0, 0, w, bezelH), 7, 7);

        // the tray slot (a dark recessed line the tray rides in)
        var slotPen = new Pen(new SolidColorBrush(Color.FromRgb(0x05, 0x07, 0x06)), 3);
        slotPen.Freeze();
        dc.DrawLine(slotPen, new Point(trayL, slotY), new Point(trayR, slotY));
        var slotHi = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 0.8);
        slotHi.Freeze();
        dc.DrawLine(slotHi, new Point(trayL, slotY + 2), new Point(trayR, slotY + 2));

        // eject button + activity LED on the bezel face
        double by = bezelH * 0.34;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x2a, 0x30, 0x2b)), null, new Rect(trayR - 22, by, 14, 9), 2, 2);
        var led = new SolidColorBrush(open > 0.02 ? Color.FromRgb(0xE9, 0xA6, 0x3F) : Teal);  // amber while the tray is out
        led.Freeze();
        var ledC = new Point(trayL + 10, by + 4.5);
        var glow = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
        glow.GradientStops.Add(new GradientStop(((SolidColorBrush)led).Color, 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, ((SolidColorBrush)led).Color.R, ((SolidColorBrush)led).Color.G, ((SolidColorBrush)led).Color.B), 1));
        glow.Freeze();
        dc.DrawEllipse(glow, null, ledC, 7, 7);
        dc.DrawEllipse(led, null, ledC, 2.4, 2.4);
    }
}
