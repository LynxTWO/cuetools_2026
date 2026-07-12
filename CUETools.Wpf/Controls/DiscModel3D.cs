using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace CUETools.Wpf.Controls;

/// <summary>
/// A real 3D model of a CD being read: the disc, and a laser tracking the spiral of data from the
/// inside out. GPU-rasterized through WPF's built-in Viewport3D (no native dependency; it degrades on
/// weak hardware rather than needing a separate renderer).
///
/// Driven by real rip data: <see cref="Progress"/> (0..1 read fraction) places the laser on the
/// spiral via the true CD geometry - inner data radius ~25 mm, outer ~58 mm, and an equal-area
/// mapping so a linear read rate moves the laser at constant data density (the CLV truth). This is
/// the geometry spike; surface pit detail, the read glow, and the re-read zoom build on top.
/// </summary>
public sealed class DiscModel3D : Viewport3D
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(DiscModel3D), new PropertyMetadata(0.0));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(DiscModel3D), new PropertyMetadata(false));
    // Re-read: when the drive is re-reading a stuck spot, the camera dollies in to it. RereadFrac is
    // where on the disc (0..1); Unreadable holds the zoom on a spot the drive/parity could not recover.
    public static readonly DependencyProperty RereadActiveProperty = DependencyProperty.Register(
        nameof(RereadActive), typeof(bool), typeof(DiscModel3D), new PropertyMetadata(false));
    public static readonly DependencyProperty RereadFracProperty = DependencyProperty.Register(
        nameof(RereadFrac), typeof(double), typeof(DiscModel3D), new PropertyMetadata(0.0));
    public static readonly DependencyProperty UnreadableProperty = DependencyProperty.Register(
        nameof(Unreadable), typeof(bool), typeof(DiscModel3D), new PropertyMetadata(false));
    // Explore mode (stage 2): free orbit + zoom via the mouse, no read/damage camera behaviour.
    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(
        nameof(Interactive), typeof(bool), typeof(DiscModel3D), new PropertyMetadata(false));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public bool RereadActive { get => (bool)GetValue(RereadActiveProperty); set => SetValue(RereadActiveProperty, value); }
    public double RereadFrac { get => (double)GetValue(RereadFracProperty); set => SetValue(RereadFracProperty, value); }
    public bool Unreadable { get => (bool)GetValue(UnreadableProperty); set => SetValue(UnreadableProperty, value); }
    public bool Interactive { get => (bool)GetValue(InteractiveProperty); set => SetValue(InteractiveProperty, value); }

    // Orbit state for explore mode (spherical: azimuth, elevation, distance).
    private double _az = -Math.PI / 2, _el = 0.9, _dist = 150;

    /// <summary>Explore mode: orbit the camera by mouse-drag deltas.</summary>
    public void Orbit(double dAz, double dEl)
    {
        _az += dAz;
        _el = Math.Max(0.12, Math.Min(1.52, _el + dEl));   // keep above the disc, below straight-down
    }

    /// <summary>Explore mode: dolly the camera in/out (factor &gt; 1 zooms out).</summary>
    public void Zoom(double factor) => _dist = Math.Max(18, Math.Min(320, _dist * factor));

    // Real CD geometry, in millimetres (used only as proportions).
    private const double RHole = 7.5, RData0 = 25.0, RDataN = 58.0, REdge = 60.0;
    private static double Radius(double f) => Math.Sqrt(RData0 * RData0 + Math.Max(0, Math.Min(1, f)) * (RDataN * RDataN - RData0 * RData0));

    private static readonly Color Teal = Color.FromRgb(0x34, 0xCF, 0xC0);
    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);
    private static readonly Color Crit = Color.FromRgb(0xEF, 0x6D, 0x6D);

    private readonly PerspectiveCamera _cam;
    private readonly ImageBrush _tracks;             // data-track rings, opacity rises with zoom
    private readonly RadialGradientBrush _surface;   // the read glow, updated from Progress
    private readonly TranslateTransform3D _laserPos;
    private readonly RotateTransform3D _spin;
    private readonly TranslateTransform3D _markerPos;   // damage marker position
    private readonly ScaleTransform3D _markerScale;     // damage marker pulse
    private readonly SolidColorBrush _markerBrush;      // amber re-reading / red unreadable
    private double _spinAngle;
    private double _zoom;      // 0 = overview, 1 = dollied in on the damage
    private double _pulse;     // marker pulse phase
    private DateTime _last = DateTime.Now;

    // camera poses: overview, and the reference the damage-focus is derived from
    private static readonly Point3D OverviewPos = new(0, 95, 96);

    public DiscModel3D()
    {
        ClipToBounds = true;

        // camera: a 3/4 view looking down at the disc from the front (animated toward damage on re-read)
        _cam = new PerspectiveCamera
        {
            Position = OverviewPos,
            LookDirection = new Vector3D(0, -OverviewPos.Y, -OverviewPos.Z),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 46
        };
        Camera = _cam;

        var root = new Model3DGroup();
        root.Children.Add(new AmbientLight(Color.FromRgb(0x28, 0x2c, 0x2a)));
        root.Children.Add(new DirectionalLight(Color.FromRgb(0xC8, 0xD2, 0xCC), new Vector3D(-0.35, -1, -0.45)));

        // the disc surface (top face, +Y): a dark reflective material with a radial read glow. Planar
        // UVs so a RadialGradientBrush maps to real world radius.
        _surface = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        RebuildSurfaceStops(0);
        _tracks = MakeTracks(512);   // fine data-track rings, brought up when zoomed in on the surface
        var topMaterial = new MaterialGroup
        {
            Children =
            {
                // a dark grey base so the disc has form under the lights
                new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x14, 0x1A, 0x18))),
                // a subtle diffraction rainbow (the "CD sheen"), spins with the disc
                new EmissiveMaterial(MakeRainbow(512)),
                // the data spiral - faint at overview, clearer when the camera zooms toward the surface
                new EmissiveMaterial(_tracks),
                // the read glow is emissive (self-lit) so it shows regardless of lighting
                new EmissiveMaterial(_surface),
                new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), 30)
            }
        };
        // double-sided so the glowing face shows regardless of winding
        var disc = new GeometryModel3D(Annulus(RHole, REdge, 220, 0, ny: 1), topMaterial) { BackMaterial = topMaterial };
        // a dark back face so the disc reads as solid from a low angle
        var discBack = new GeometryModel3D(Annulus(RHole, REdge, 220, -0.4, ny: -1, flip: true),
            new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x0C, 0x10, 0x0F))));

        _spin = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0));
        var discModel = new Model3DGroup { Children = { disc, discBack } };
        discModel.Transform = _spin;
        root.Children.Add(discModel);

        // the laser: a bright spot on the surface plus a thin beam up to the pickup. Positioned by a
        // translate we move each frame; angle fixed toward the camera so it is always visible.
        _laserPos = new TranslateTransform3D(0, 0, 0);
        var laserGroup = new Model3DGroup();
        var spotColor = Color.FromRgb(0xFF, 0x5A, 0x4A);
        laserGroup.Children.Add(new GeometryModel3D(Sphere(new Point3D(0, 0.8, 0), 1.7, 16),
            new EmissiveMaterial(new SolidColorBrush(spotColor))));
        laserGroup.Children.Add(new GeometryModel3D(Cylinder(new Point3D(0, 1.0, 0), new Point3D(0, 26, 0), 0.35, 10),
            new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0x6A, 0x5A)))));
        laserGroup.Transform = _laserPos;
        root.Children.Add(laserGroup);

        // damage marker: an emissive halo that sits on the re-read spot, pulsing; amber while being
        // re-read, red when the spot is unreadable. Hidden (scaled to nothing) when there is no damage.
        _markerBrush = new SolidColorBrush(Amber);
        _markerPos = new TranslateTransform3D(0, 1.2, 0);
        _markerScale = new ScaleTransform3D(0, 0, 0);
        var marker = new Model3DGroup { Transform = new Transform3DGroup { Children = { _markerScale, _markerPos } } };
        marker.Children.Add(new GeometryModel3D(Sphere(new Point3D(0, 0, 0), 3.2, 18), new EmissiveMaterial(_markerBrush)));
        root.Children.Add(marker);

        Children.Add(new ModelVisual3D { Content = root });

        Loaded += (_, _) => { _last = DateTime.Now; CompositionTarget.Rendering += OnTick; };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnTick;
        PlaceLaser();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.05, (now - _last).TotalSeconds);
        _last = now;

        // Explore mode: free-orbit camera, a slow idle spin, data tracks that emerge as you zoom in.
        if (Interactive)
        {
            _spinAngle = (_spinAngle + dt * 15) % 360;
            ((AxisAngleRotation3D)_spin.Rotation).Angle = _spinAngle;
            UpdateOrbitCamera();
            _tracks.Opacity = Math.Max(0.14, Math.Min(0.92, (175 - _dist) / 155));
            RebuildSurfaceStops(0);
            _markerScale.ScaleX = _markerScale.ScaleY = _markerScale.ScaleZ = 0;
            _laserPos.OffsetZ = RData0;
            return;
        }

        if (Active)
        {
            // the disc spins (visual cue; CLV would vary with radius, shown once pits give it texture)
            _spinAngle = (_spinAngle + dt * 120) % 360;
            ((AxisAngleRotation3D)_spin.Rotation).Angle = _spinAngle;
        }

        // dolly the camera toward the damaged spot while re-reading or when it is unreadable, then
        // ease back out. Real-outcome-driven: RereadActive / Unreadable come straight from the rip.
        bool damage = RereadActive || Unreadable;
        _zoom += ((damage ? 1.0 : 0.0) - _zoom) * 0.05;
        _pulse += dt * 4.2;
        _tracks.Opacity = 0.06 + 0.7 * _zoom;   // data tracks emerge as the camera zooms toward the surface
        UpdateCamera();
        UpdateMarker(damage);

        RebuildSurfaceStops(Progress);
        PlaceLaser();
    }

    private void UpdateOrbitCamera()
    {
        double x = _dist * Math.Cos(_el) * Math.Cos(_az);
        double y = _dist * Math.Sin(_el);
        double z = _dist * Math.Cos(_el) * Math.Sin(_az);
        _cam.Position = new Point3D(x, y, z);
        _cam.LookDirection = new Vector3D(-x, -y, -z);
    }

    private void UpdateCamera()
    {
        if (_zoom < 0.002)
        {
            _cam.Position = OverviewPos;
            _cam.LookDirection = new Vector3D(-OverviewPos.X, -OverviewPos.Y, -OverviewPos.Z);
            return;
        }
        double r = Radius(RereadFrac);
        var damagePt = new Point3D(0, 0, r);                 // the stuck spot, at the front of the disc
        var focusPos = new Point3D(0, 42, r + 34);           // closer, above and in front of it
        var pos = Lerp(OverviewPos, focusPos, _zoom);
        _cam.Position = pos;
        _cam.LookDirection = Lerp(new Point3D(0, 0, 0), damagePt, _zoom) - pos;
    }

    private void UpdateMarker(bool damage)
    {
        _markerPos.OffsetZ = Radius(RereadFrac);
        double pulse = 0.7 + 0.3 * Math.Sin(_pulse);
        double s = damage ? pulse * (0.4 + 0.6 * _zoom) : Math.Max(0, _markerScale.ScaleX - 0.06);
        _markerScale.ScaleX = _markerScale.ScaleY = _markerScale.ScaleZ = s;
        _markerBrush.Color = Unreadable
            ? Color.FromArgb((byte)(255 * (0.45 + 0.55 * Math.Abs(Math.Sin(_pulse * 0.8)))), Crit.R, Crit.G, Crit.B)  // flashing red
            : Amber;
    }

    // Put the laser spot at the true spiral radius for the current read fraction, at the front of the
    // disc so it is always in view. During a re-read it sits on the stuck spot; idle it parks at the
    // data start.
    private void PlaceLaser()
    {
        _laserPos.OffsetX = 0;
        _laserPos.OffsetZ = RereadActive ? Radius(RereadFrac) : Active ? Radius(Progress) : RData0;
    }

    private static Point3D Lerp(Point3D a, Point3D b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    // ---- procedural surface textures (built once, planar-UV mapped like the read glow) ----

    // A faint spectral sheen: two rainbow bands swept around the disc, brighter toward the rim, low
    // alpha. Emissive, so it reads as the diffraction shimmer of a CD; it spins with the disc.
    private static ImageBrush MakeRainbow(int size)
    {
        var px = new byte[size * size * 4];
        double c = size / 2.0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = x - c, dy = y - c, r = Math.Sqrt(dx * dx + dy * dy) / c;
                if (r > 1.0 || r < 0.14) continue;                 // outside the disc / inside the hub
                double ang = (Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI);
                double hue = (ang * 2.0 % 1.0) * 360;              // two bands around
                HsvToRgb(hue, 0.5, 1.0, out byte rr, out byte gg, out byte bb);
                double a = 0.12 * (0.3 + 0.7 * r);
                int i = (y * size + x) * 4;
                px[i] = bb; px[i + 1] = gg; px[i + 2] = rr; px[i + 3] = (byte)(a * 255);
            }
        return BrushFrom(px, size);
    }

    // Fine concentric rings across the data band: a REPRESENTATIVE data spiral (not the literal 1.6 um
    // pitch, which is sub-pixel), faint so it does not moire at overview and reads when zoomed in.
    private static ImageBrush MakeTracks(int size)
    {
        var px = new byte[size * size * 4];
        double c = size / 2.0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = x - c, dy = y - c, r = Math.Sqrt(dx * dx + dy * dy) / c;
                if (r > 0.985 || r < 0.42) continue;               // the 25..58 mm data band
                double ring = 0.5 + 0.5 * Math.Sin(r * c * 0.7);   // representative track frequency
                double a = ring * ring * ring;                     // thin bright lines, dark gaps
                int i = (y * size + x) * 4;
                byte t = 0xC8;
                px[i] = t; px[i + 1] = t; px[i + 2] = t; px[i + 3] = (byte)(a * 120);
            }
        return BrushFrom(px, size);
    }

    private static ImageBrush BrushFrom(byte[] bgra, int size)
    {
        var wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, size, size), bgra, size * 4, 0);
        wb.Freeze();
        return new ImageBrush(wb) { Stretch = Stretch.Fill };
    }

    private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
    {
        double c = v * s, x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)), m = v - c;
        double rr = 0, gg = 0, bb = 0;
        if (h < 60) { rr = c; gg = x; }
        else if (h < 120) { rr = x; gg = c; }
        else if (h < 180) { gg = c; bb = x; }
        else if (h < 240) { gg = x; bb = c; }
        else if (h < 300) { rr = x; bb = c; }
        else { rr = c; bb = x; }
        r = (byte)((rr + m) * 255); g = (byte)((gg + m) * 255); b = (byte)((bb + m) * 255);
    }

    // Read glow (emissive): teal from the hub out to the laser radius, a bright edge at the laser,
    // then transparent beyond so only the dark base disc shows in the not-yet-read region. Idle shows
    // no read region (the disc is not being read).
    private void RebuildSurfaceStops(double f)
    {
        double hub = RHole / REdge;
        double v = Active ? Math.Max(hub + 0.02, Radius(f) / REdge) : hub;   // laser radius in planar-UV terms
        Color glow = Color.FromArgb(0x55, Teal.R, Teal.G, Teal.B);
        Color edge = Unreadable ? Crit
            : RereadActive ? Amber
            : Active ? Color.FromRgb(0xD8, 0xFF, 0xF6)
            : Color.FromArgb(0x66, Teal.R, Teal.G, Teal.B);
        Color clear = Color.FromArgb(0x00, Teal.R, Teal.G, Teal.B);
        var stops = new GradientStopCollection
        {
            new GradientStop(Color.FromArgb(0x22, Teal.R, Teal.G, Teal.B), 0.0),
            new GradientStop(glow, hub),
            new GradientStop(glow, Math.Max(hub + 0.001, v - 0.03)),
            new GradientStop(edge, v),
            new GradientStop(clear, Math.Min(1.0, v + 0.012)),
            new GradientStop(clear, 1.0)
        };
        _surface.GradientStops = stops;
    }

    // ---- mesh builders ----

    // A flat ring in the XZ plane at height y. Planar UVs (0..1 across the bounding square) so a
    // RadialGradientBrush centred at (0.5,0.5) maps to world radius.
    private static MeshGeometry3D Annulus(double rInner, double rOuter, int seg, double y, int ny = 1, bool flip = false)
    {
        var m = new MeshGeometry3D();
        var normal = new Vector3D(0, ny, 0);
        for (int i = 0; i <= seg; i++)
        {
            double a = 2 * Math.PI * i / seg, c = Math.Cos(a), s = Math.Sin(a);
            m.Positions.Add(new Point3D(rInner * c, y, rInner * s));
            m.Positions.Add(new Point3D(rOuter * c, y, rOuter * s));
            m.Normals.Add(normal); m.Normals.Add(normal);
            m.TextureCoordinates.Add(new Point(0.5 + 0.5 * (rInner / rOuter) * c, 0.5 + 0.5 * (rInner / rOuter) * s));
            m.TextureCoordinates.Add(new Point(0.5 + 0.5 * c, 0.5 + 0.5 * s));
        }
        for (int i = 0; i < seg; i++)
        {
            int b = i * 2;
            if (!flip)
            {
                m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b + 3);
                m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 3); m.TriangleIndices.Add(b + 2);
            }
            else
            {
                m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 3); m.TriangleIndices.Add(b + 1);
                m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 2); m.TriangleIndices.Add(b + 3);
            }
        }
        return m;
    }

    private static MeshGeometry3D Sphere(Point3D c, double r, int seg)
    {
        var m = new MeshGeometry3D();
        for (int iy = 0; iy <= seg; iy++)
        {
            double phi = Math.PI * iy / seg;
            for (int ix = 0; ix <= seg; ix++)
            {
                double th = 2 * Math.PI * ix / seg;
                m.Positions.Add(new Point3D(
                    c.X + r * Math.Sin(phi) * Math.Cos(th),
                    c.Y + r * Math.Cos(phi),
                    c.Z + r * Math.Sin(phi) * Math.Sin(th)));
            }
        }
        int w = seg + 1;
        for (int iy = 0; iy < seg; iy++)
            for (int ix = 0; ix < seg; ix++)
            {
                int p = iy * w + ix;
                m.TriangleIndices.Add(p); m.TriangleIndices.Add(p + w); m.TriangleIndices.Add(p + 1);
                m.TriangleIndices.Add(p + 1); m.TriangleIndices.Add(p + w); m.TriangleIndices.Add(p + w + 1);
            }
        return m;
    }

    private static MeshGeometry3D Cylinder(Point3D p0, Point3D p1, double r, int seg)
    {
        var m = new MeshGeometry3D();
        var axis = p1 - p0; axis.Normalize();
        var up = Math.Abs(axis.Y) > 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
        var u = Vector3D.CrossProduct(axis, up); u.Normalize();
        var v = Vector3D.CrossProduct(axis, u); v.Normalize();
        for (int i = 0; i <= seg; i++)
        {
            double a = 2 * Math.PI * i / seg;
            var off = r * (Math.Cos(a) * u + Math.Sin(a) * v);
            m.Positions.Add(p0 + off);
            m.Positions.Add(p1 + off);
        }
        for (int i = 0; i < seg; i++)
        {
            int b = i * 2;
            m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b + 3);
            m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 3); m.TriangleIndices.Add(b + 2);
        }
        return m;
    }
}
