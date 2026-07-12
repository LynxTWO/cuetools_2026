using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    // Real CD geometry, in millimetres (used only as proportions).
    private const double RHole = 7.5, RData0 = 25.0, RDataN = 58.0, REdge = 60.0;
    private static double Radius(double f) => Math.Sqrt(RData0 * RData0 + Math.Max(0, Math.Min(1, f)) * (RDataN * RDataN - RData0 * RData0));

    private static readonly Color Teal = Color.FromRgb(0x34, 0xCF, 0xC0);

    private readonly RadialGradientBrush _surface;   // the read glow, updated from Progress
    private readonly GradientStop _readEdge;
    private readonly TranslateTransform3D _laserPos;
    private readonly RotateTransform3D _spin;
    private double _spinAngle;
    private DateTime _last = DateTime.Now;

    public DiscModel3D()
    {
        ClipToBounds = true;

        // camera: a 3/4 view looking down at the disc from the front
        Camera = new PerspectiveCamera
        {
            Position = new Point3D(0, 95, 96),
            LookDirection = new Vector3D(0, -95, -96),
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 46
        };

        var root = new Model3DGroup();
        root.Children.Add(new AmbientLight(Color.FromRgb(0x28, 0x2c, 0x2a)));
        root.Children.Add(new DirectionalLight(Color.FromRgb(0xC8, 0xD2, 0xCC), new Vector3D(-0.35, -1, -0.45)));

        // the disc surface (top face, +Y): a dark reflective material with a radial read glow. Planar
        // UVs so a RadialGradientBrush maps to real world radius.
        _readEdge = new GradientStop(Teal, 0.0);
        _surface = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        RebuildSurfaceStops(0);
        var topMaterial = new MaterialGroup
        {
            Children =
            {
                // a dark grey base so the disc has form under the lights
                new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x18, 0x1E, 0x1C))),
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
        if (Active)
        {
            // the disc spins (visual cue; CLV would vary with radius, shown once pits give it texture)
            _spinAngle = (_spinAngle + dt * 120) % 360;
            ((AxisAngleRotation3D)_spin.Rotation).Angle = _spinAngle;
        }
        RebuildSurfaceStops(Progress);
        PlaceLaser();
    }

    // Put the laser spot at the true spiral radius for the current read fraction, at the front of the
    // disc (angle -90 deg, toward the camera) so it is always in view. Idle: parked at the data start.
    private void PlaceLaser()
    {
        _laserPos.OffsetX = 0;
        _laserPos.OffsetZ = Active ? Radius(Progress) : RData0;   // front of the disc, toward the camera
    }

    // Read glow (emissive): teal from the hub out to the laser radius, a bright edge at the laser,
    // then transparent beyond so only the dark base disc shows in the not-yet-read region. Idle shows
    // no read region (the disc is not being read).
    private void RebuildSurfaceStops(double f)
    {
        double hub = RHole / REdge;
        double v = Active ? Math.Max(hub + 0.02, Radius(f) / REdge) : hub;   // laser radius in planar-UV terms
        Color glow = Color.FromArgb(0x55, Teal.R, Teal.G, Teal.B);
        Color edge = Active ? Color.FromRgb(0xD8, 0xFF, 0xF6) : Color.FromArgb(0x66, Teal.R, Teal.G, Teal.B);
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
