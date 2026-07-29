using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.Controls;

/// <summary>
/// A real 3D model of a CD being read: the disc, and a laser tracking the spiral of data from the
/// inside out. GPU-rasterized through WPF's built-in Viewport3D (no native dependency; it degrades on
/// weak hardware rather than needing a separate renderer).
///
/// Driven by real rip data: <see cref="Progress"/> (0..1 read fraction) places the laser on the
/// spiral via the true CD geometry - inner data radius ~25 mm, outer ~58 mm, and an equal-area
/// mapping so a linear read rate moves the laser at constant data density (the CLV truth).
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

    // Orbit state for explore mode (spherical az/el/dist about a movable look-at target).
    private double _az = -Math.PI / 2, _el = 0.9, _dist = 150;
    private Point3D _target;   // right-drag pans this, so zoom homes in on any point, not just centre

    /// <summary>Explore mode: orbit the camera by mouse-drag deltas.</summary>
    public void Orbit(double dAz, double dEl)
    {
        _az += dAz;
        _el = Math.Max(0.12, Math.Min(1.52, _el + dEl));   // keep above the disc, below straight-down
    }

    /// <summary>Explore mode: dolly the camera in/out (factor &gt; 1 zooms out).</summary>
    public void Zoom(double factor) => _dist = Math.Max(9, Math.Min(320, _dist * factor));

    /// <summary>Explore mode: pan the look-at target in the camera's screen plane (right-drag), so the
    /// next zoom homes in on that point. Scaled by distance so it feels the same at any zoom.</summary>
    public void Pan(double dx, double dy)
    {
        double ce = Math.Cos(_el), se = Math.Sin(_el), ca = Math.Cos(_az), sa = Math.Sin(_az);
        var dir = new Vector3D(-ce * ca, -se, -ce * sa);                 // camera -> target
        var right = Vector3D.CrossProduct(dir, new Vector3D(0, 1, 0)); right.Normalize();
        var up = Vector3D.CrossProduct(right, dir); up.Normalize();
        double k = _dist * 0.0026;
        _target += right * (-dx * k) + up * (dy * k);
        _target = new Point3D(Clamp(_target.X, -58, 58), Clamp(_target.Y, -12, 12), Clamp(_target.Z, -58, 58));
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    // Real CD geometry, in millimetres (used only as proportions). The 15 mm centre
    // hole, 120 mm edge, and 25..58 mm program area stay distinct in the mesh.
    private const double RHole = 7.5, RStack = 16.5, RData0 = 25.0;
    private const double RDataN = 58.0, REdge = 60.0;
    internal static double DataRadius(double f) =>
        Math.Sqrt(
            RData0 * RData0 +
            Math.Max(0, Math.Min(1, f)) *
            (RDataN * RDataN - RData0 * RData0));
    internal static double VisualSpinDegreesPerSecond(double f) =>
        145.0 * RData0 / DataRadius(f);

    private static readonly Color Teal = Color.FromRgb(0x34, 0xCF, 0xC0);
    private static readonly Color Amber = Color.FromRgb(0xE9, 0xA6, 0x3F);
    private static readonly Color Crit = Color.FromRgb(0xEF, 0x6D, 0x6D);

    private readonly PerspectiveCamera _cam;
    private readonly ImageBrush _tracks;             // data-track rings, opacity rises with zoom
    private readonly RadialGradientBrush _surface;   // the read glow, updated from Progress
    private readonly GradientStop _readCentre;
    private readonly GradientStop _readHub;
    private readonly GradientStop _readBody;
    private readonly GradientStop _readEdge;
    private readonly GradientStop _readClear;
    private readonly GradientStop _readOuter;
    private readonly AmbientLight _ambient;
    private readonly DirectionalLight _keyLight;
    private readonly DirectionalLight _fillLight;
    private readonly SolidColorBrush _dataBrush;
    private readonly SolidColorBrush _hubBrush;
    private readonly SolidColorBrush _rimBrush;
    private readonly SolidColorBrush _edgeBrush;
    private readonly SolidColorBrush _backBrush;
    private readonly SolidColorBrush _trackBrush;
    private readonly SolidColorBrush _pickupBrush;
    private readonly TranslateTransform3D _laserPos;
    private readonly RotateTransform3D _spin;
    private readonly TranslateTransform3D _markerPos;   // damage marker position
    private readonly ScaleTransform3D _markerScale;     // damage marker pulse
    private readonly SolidColorBrush _markerBrush;      // amber re-reading / red unreadable
    private readonly SolidColorBrush _markerBackingBrush;
    private double _spinAngle;
    private double _zoom;      // 0 = overview, 1 = dollied in on the damage
    private double _pulse;     // marker pulse phase
    private DateTime _last = DateTime.Now;
    private Color _lastDataColor;
    private Color _lastHubColor;
    private Color _lastEdgeColor;
    private Color _lastBackColor;
    private Color _lastTrackColor;
    private int _palettePollFrame;
    private double _lastVisualProgress = double.NaN;
    private double _lastVisualRereadFrac = double.NaN;
    private bool _lastVisualActive;
    private bool _lastVisualRereadActive;
    private bool _lastVisualUnreadable;
    private double _lastTrackOpacity = double.NaN;

    // camera poses: overview, and the reference the damage-focus is derived from
    private static readonly Point3D OverviewPos = new(0, 95, 96);

    internal double DamageZoom => _zoom;
    internal double LaserRadius => _laserPos.OffsetZ;
    internal Point3D CameraPosition => _cam.Position;

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
        _ambient = new AmbientLight(Color.FromRgb(0x48, 0x50, 0x4D));
        _keyLight = new DirectionalLight(
            Color.FromRgb(0xF0, 0xF6, 0xF3),
            new Vector3D(-0.35, -1, -0.45));
        _fillLight = new DirectionalLight(
            Color.FromRgb(0x74, 0x9A, 0x95),
            new Vector3D(0.8, -0.45, 0.25));
        root.Children.Add(_ambient);
        root.Children.Add(_keyLight);
        root.Children.Add(_fillLight);

        _dataBrush = new SolidColorBrush();
        _hubBrush = new SolidColorBrush();
        _rimBrush = new SolidColorBrush();
        _edgeBrush = new SolidColorBrush();
        _backBrush = new SolidColorBrush();
        _trackBrush = new SolidColorBrush();
        _pickupBrush = new SolidColorBrush();

        // The program area owns the optical texture and read glow. The clear hub,
        // mirrored clamp band, outer rim, back, and two vertical edges are separate
        // meshes so the object reads as a compact disc rather than a dark platter.
        _surface = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        _readCentre = new GradientStop();
        _readHub = new GradientStop();
        _readBody = new GradientStop();
        _readEdge = new GradientStop();
        _readClear = new GradientStop();
        _readOuter = new GradientStop();
        _surface.GradientStops = new GradientStopCollection
        {
            _readCentre,
            _readHub,
            _readBody,
            _readEdge,
            _readClear,
            _readOuter
        };
        RebuildSurfaceStops(0);
        _tracks = MakeTracks(1024);
        var topMaterial = new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(_dataBrush),
                new EmissiveMaterial(MakeMirrorSweep()),
                new EmissiveMaterial(MakeOpticalSheen(768)),
                new EmissiveMaterial(_tracks),
                new EmissiveMaterial(_surface),
                new SpecularMaterial(_trackBrush, 72)
            }
        };
        var backMaterial = new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(_backBrush),
                new SpecularMaterial(_edgeBrush, 34)
            }
        };
        var hubMaterial = new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(_hubBrush),
                new SpecularMaterial(_trackBrush, 58)
            }
        };
        var rimMaterial = new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(_rimBrush),
                new SpecularMaterial(_trackBrush, 68)
            }
        };
        var edgeMaterial = new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(_edgeBrush),
                new SpecularMaterial(_trackBrush, 80)
            }
        };

        var dataFace = new GeometryModel3D(
            Annulus(RData0, RDataN, 256, 0.10, ny: 1, uvRadius: REdge),
            topMaterial)
        {
            BackMaterial = backMaterial
        };
        var clearHub = new GeometryModel3D(
            Annulus(RHole, RStack, 160, 0.08, ny: 1, uvRadius: REdge),
            rimMaterial);
        var mirrorBand = new GeometryModel3D(
            Annulus(RStack, RData0, 192, 0.09, ny: 1, uvRadius: REdge),
            hubMaterial);
        var clearRim = new GeometryModel3D(
            Annulus(RDataN, REdge, 192, 0.08, ny: 1, uvRadius: REdge),
            rimMaterial);
        var clampRing = new GeometryModel3D(
            Annulus(RStack - 0.45, RStack + 0.45, 160, 0.14, ny: 1, uvRadius: REdge),
            edgeMaterial);
        var leadInRing = new GeometryModel3D(
            Annulus(RData0 - 0.22, RData0 + 0.22, 192, 0.14, ny: 1, uvRadius: REdge),
            edgeMaterial);
        var leadOutRing = new GeometryModel3D(
            Annulus(RDataN - 0.18, RDataN + 0.18, 224, 0.14, ny: 1, uvRadius: REdge),
            edgeMaterial);
        var discBack = new GeometryModel3D(
            Annulus(RHole, REdge, 256, -0.55, ny: -1, flip: true, uvRadius: REdge),
            backMaterial);
        var outerEdge = new GeometryModel3D(
            RingWall(REdge, 0.08, -0.55, 256, inward: false),
            edgeMaterial);
        var holeEdge = new GeometryModel3D(
            RingWall(RHole, 0.08, -0.55, 128, inward: true),
            edgeMaterial);

        _spin = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0));
        var discModel = new Model3DGroup
        {
            Children =
            {
                discBack,
                outerEdge,
                holeEdge,
                clearHub,
                mirrorBand,
                dataFace,
                clearRim,
                clampRing,
                leadInRing,
                leadOutRing
            }
        };
        discModel.Transform = _spin;
        root.Children.Add(discModel);

        // A CD pickup reads through the clear substrate from below. The visible red
        // cue represents the otherwise near-infrared beam; the lens and radial sled
        // sit beneath the disc while only the focus spot reaches the data surface.
        root.Children.Add(new GeometryModel3D(
            Cylinder(
                new Point3D(0, -7.0, RData0 - 3),
                new Point3D(0, -7.0, RDataN + 3),
                0.75,
                12),
            backMaterial));
        _laserPos = new TranslateTransform3D(0, 0, 0);
        var laserGroup = new Model3DGroup();
        var spotColor = Color.FromRgb(0xFF, 0x5A, 0x4A);
        laserGroup.Children.Add(new GeometryModel3D(
            Annulus(0, 3.4, 48, 0.42, ny: 1, uvRadius: 3.4),
            new EmissiveMaterial(MakeLaserHalo())));
        laserGroup.Children.Add(new GeometryModel3D(
            Sphere(new Point3D(0, 0.72, 0), 1.05, 16),
            new EmissiveMaterial(new SolidColorBrush(spotColor))));
        laserGroup.Children.Add(new GeometryModel3D(
            Cylinder(new Point3D(0, -5.6, 0), new Point3D(0, 0.50, 0), 0.20, 10),
            new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0x6A, 0x5A)))));
        laserGroup.Children.Add(new GeometryModel3D(
            Sphere(new Point3D(0, -6.0, 0), 2.0, 18),
            new MaterialGroup
            {
                Children =
                {
                    new DiffuseMaterial(_pickupBrush),
                    new SpecularMaterial(_trackBrush, 70)
                }
            }));
        laserGroup.Transform = _laserPos;
        root.Children.Add(laserGroup);

        // The damage marker is a surface ring, not a claimed physical scratch.
        // It marks the real re-read outcome and preserves the existing zoom state.
        _markerBrush = new SolidColorBrush(Amber);
        _markerBackingBrush = new SolidColorBrush(
            Color.FromArgb(0xE0, 0x12, 0x17, 0x14));
        _markerPos = new TranslateTransform3D(0, 1.2, 0);
        _markerScale = new ScaleTransform3D(0, 0, 0);
        var marker = new Model3DGroup { Transform = new Transform3DGroup { Children = { _markerScale, _markerPos } } };
        var markerMaterial = new MaterialGroup
        {
            Children =
            {
                new DiffuseMaterial(_markerBackingBrush),
                new EmissiveMaterial(_markerBrush)
            }
        };
        marker.Children.Add(new GeometryModel3D(
            Annulus(2.0, 4.1, 48, 0, ny: 1, uvRadius: 4.1),
            markerMaterial));
        marker.Children.Add(new GeometryModel3D(
            Sphere(new Point3D(0, 0.25, 0), 0.9, 14),
            markerMaterial));
        root.Children.Add(marker);

        Children.Add(new ModelVisual3D { Content = root });

        Loaded += (_, _) =>
        {
            RefreshPalette();
            _last = DateTime.Now;
            CompositionTarget.Rendering += OnTick;
        };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnTick;
        RefreshPalette();
        PlaceLaser();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.05, (now - _last).TotalSeconds);
        _last = now;
        Advance(dt);
    }

    internal void Advance(double dt)
    {
        dt = Math.Max(0, Math.Min(0.05, dt));
        // FrameworkElement resource lookup allocates. Polling every 30 frames
        // keeps live theme changes prompt without adding per-frame GC pressure.
        if (_palettePollFrame++ % 30 == 0)
            RefreshPalette();
        // Explore mode: free-orbit camera, a slow idle spin, data tracks that emerge as you zoom in.
        if (Interactive)
        {
            _spinAngle = (_spinAngle + dt * 15) % 360;
            ((AxisAngleRotation3D)_spin.Rotation).Angle = _spinAngle;
            UpdateOrbitCamera();
            SetTrackOpacity(
                Math.Max(0.14, Math.Min(0.92, (175 - _dist) / 155)));
            UpdateReadVisuals();
            _markerScale.ScaleX = _markerScale.ScaleY = _markerScale.ScaleZ = 0;
            return;
        }

        if (Active)
        {
            // The visible rate is slowed for legibility but keeps the real CLV
            // relationship: the disc rotates faster at the inner program radius.
            double f = RereadActive ? RereadFrac : Progress;
            _spinAngle =
                (_spinAngle + dt * VisualSpinDegreesPerSecond(f)) % 360;
            ((AxisAngleRotation3D)_spin.Rotation).Angle = _spinAngle;
        }

        // dolly the camera toward the damaged spot while re-reading or when it is unreadable, then
        // ease back out. Real-outcome-driven: RereadActive / Unreadable come straight from the rip.
        bool damage = RereadActive || Unreadable;
        _zoom += ((damage ? 1.0 : 0.0) - _zoom) * 0.05;
        _pulse += dt * 4.2;
        SetTrackOpacity(0.10 + 0.70 * _zoom);
        UpdateCamera();
        UpdateMarker(damage);
        UpdateReadVisuals();
    }

    private void UpdateOrbitCamera()
    {
        double x = _dist * Math.Cos(_el) * Math.Cos(_az);
        double y = _dist * Math.Sin(_el);
        double z = _dist * Math.Cos(_el) * Math.Sin(_az);
        var pos = new Point3D(_target.X + x, _target.Y + y, _target.Z + z);
        _cam.Position = pos;
        _cam.LookDirection = _target - pos;
    }

    private void UpdateCamera()
    {
        if (_zoom < 0.002)
        {
            if (_cam.Position != OverviewPos)
            {
                _cam.Position = OverviewPos;
                _cam.LookDirection = new Vector3D(
                    -OverviewPos.X,
                    -OverviewPos.Y,
                    -OverviewPos.Z);
            }
            return;
        }
        double r = DataRadius(RereadFrac);
        var damagePt = new Point3D(0, 0, r);                 // the stuck spot, at the front of the disc
        var focusPos = new Point3D(0, 42, r + 34);           // closer, above and in front of it
        var pos = Lerp(OverviewPos, focusPos, _zoom);
        _cam.Position = pos;
        _cam.LookDirection = Lerp(new Point3D(0, 0, 0), damagePt, _zoom) - pos;
    }

    private void UpdateMarker(bool damage)
    {
        if (!damage && _markerScale.ScaleX <= 0)
            return;
        _markerPos.OffsetZ = DataRadius(RereadFrac);
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
        double radius = RereadActive
            ? DataRadius(RereadFrac)
            : Active
                ? DataRadius(Progress)
                : RData0;
        if (_laserPos.OffsetX != 0)
            _laserPos.OffsetX = 0;
        if (_laserPos.OffsetZ != radius)
            _laserPos.OffsetZ = radius;
    }

    private void UpdateReadVisuals()
    {
        if (Progress == _lastVisualProgress &&
            RereadFrac == _lastVisualRereadFrac &&
            Active == _lastVisualActive &&
            RereadActive == _lastVisualRereadActive &&
            Unreadable == _lastVisualUnreadable)
            return;

        _lastVisualProgress = Progress;
        _lastVisualRereadFrac = RereadFrac;
        _lastVisualActive = Active;
        _lastVisualRereadActive = RereadActive;
        _lastVisualUnreadable = Unreadable;
        RebuildSurfaceStops(Progress);
        PlaceLaser();
    }

    private void SetTrackOpacity(double opacity)
    {
        if (Math.Abs(opacity - _lastTrackOpacity) < 0.000001)
            return;
        _lastTrackOpacity = opacity;
        _tracks.Opacity = opacity;
    }

    private void RefreshPalette()
    {
        Color data = ThemeColor.Get(
            this,
            "DiscData",
            Color.FromRgb(0x93, 0xA3, 0x9F));
        Color hub = ThemeColor.Get(
            this,
            "DiscHub",
            Color.FromRgb(0xBC, 0xC8, 0xC4));
        Color edge = ThemeColor.Get(
            this,
            "DiscEdge",
            Color.FromRgb(0xDD, 0xE6, 0xE2));
        Color back = ThemeColor.Get(
            this,
            "DiscBack",
            Color.FromRgb(0x30, 0x3A, 0x36));
        Color track = ThemeColor.Get(
            this,
            "DiscTrack",
            Color.FromRgb(0xE5, 0xF3, 0xEE));
        if (data == _lastDataColor &&
            hub == _lastHubColor &&
            edge == _lastEdgeColor &&
            back == _lastBackColor &&
            track == _lastTrackColor)
            return;

        _lastDataColor = data;
        _lastHubColor = hub;
        _lastEdgeColor = edge;
        _lastBackColor = back;
        _lastTrackColor = track;

        _dataBrush.Color = data;
        _hubBrush.Color = WithAlpha(hub, 0xE2);
        _rimBrush.Color = WithAlpha(hub, 0xA8);
        _edgeBrush.Color = edge;
        _backBrush.Color = back;
        _trackBrush.Color = WithAlpha(track, 0xC8);
        _pickupBrush.Color = Blend(back, track, 0.42);
        _ambient.Color = Blend(back, data, 0.58);
        _keyLight.Color = Blend(track, Colors.White, 0.62);
        _fillLight.Color = Blend(data, Teal, 0.32);
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Blend(Color a, Color b, double amount)
    {
        amount = Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * amount),
            (byte)Math.Round(a.G + (b.G - a.G) * amount),
            (byte)Math.Round(a.B + (b.B - a.B) * amount));
    }

    private static Point3D Lerp(Point3D a, Point3D b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    // ---- procedural surface textures (built once, planar-UV mapped like the read glow) ----

    private static Brush MakeMirrorSweep()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.08, 0.92),
            EndPoint = new Point(0.92, 0.08)
        };
        brush.GradientStops.Add(
            new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.00));
        brush.GradientStops.Add(
            new GradientStop(Color.FromArgb(0x10, 0xD9, 0xEB, 0xE5), 0.30));
        brush.GradientStops.Add(
            new GradientStop(Color.FromArgb(0x58, 0xFF, 0xFF, 0xFF), 0.46));
        brush.GradientStops.Add(
            new GradientStop(Color.FromArgb(0x12, 0xC8, 0xE4, 0xDD), 0.58));
        brush.GradientStops.Add(
            new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush MakeLaserHalo()
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        brush.GradientStops.Add(
            new GradientStop(Color.FromArgb(0xC8, 0xFF, 0x86, 0x6F), 0.00));
        brush.GradientStops.Add(
            new GradientStop(Color.FromArgb(0x60, 0xFF, 0x5A, 0x4A), 0.32));
        brush.GradientStops.Add(
            new GradientStop(Color.FromArgb(0x00, 0xFF, 0x5A, 0x4A), 1.00));
        brush.Freeze();
        return brush;
    }

    // Compact-disc diffraction appears as narrow, curved spectral highlights,
    // not a solid rainbow wedge. This bounded texture creates two low-alpha
    // lobes inside the physical data band and rotates with the disc.
    private static ImageBrush MakeOpticalSheen(int size)
    {
        var px = new byte[size * size * 4];
        double c = size / 2.0;
        double inner = RData0 / REdge;
        double outer = RDataN / REdge;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = x - c, dy = y - c, r = Math.Sqrt(dx * dx + dy * dy) / c;
                if (r > outer || r < inner)
                    continue;
                double ang = Math.Atan2(dy, dx);
                double curve = ang + (r - inner) * 3.2;
                double lobeA = Math.Pow(
                    Math.Max(0, Math.Cos(curve + 0.72)),
                    10);
                double lobeB = 0.72 * Math.Pow(
                    Math.Max(0, Math.Cos(curve - 2.28)),
                    14);
                double band = 0.70 + 0.30 * Math.Sin(r * 44 + ang * 3);
                double edgeFade =
                    SmoothStep((r - inner) / 0.035) *
                    SmoothStep((outer - r) / 0.025);
                double alpha =
                    (0.012 + 0.24 * Math.Min(1, lobeA + lobeB)) *
                    band *
                    edgeFade;
                double hue = (205 + r * 245 + ang * 29) % 360;
                if (hue < 0)
                    hue += 360;
                HsvToRgb(hue, 0.62, 1.0, out byte rr, out byte gg, out byte bb);
                int i = (y * size + x) * 4;
                px[i] = bb;
                px[i + 1] = gg;
                px[i + 2] = rr;
                px[i + 3] = (byte)(Clamp(alpha, 0, 1) * 255);
            }
        return BrushFrom(px, size);
    }

    private static double SmoothStep(double value)
    {
        value = Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }

    // The data spiral, with pit/land structure. Each track carries a hashed run of pits (dark bumps)
    // and lands (bright flats); the groove between tracks is dark. Faint at overview (fades in with
    // zoom) so it does not moire; zoom in far enough and the individual pits resolve. Representative,
    // not the literal 1.6 um pitch (that is sub-pixel), and stated as such in the honesty rules.
    private static ImageBrush MakeTracks(int size)
    {
        var px = new byte[size * size * 4];
        double c = size / 2.0;
        const double trackFreq = 0.34;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = x - c, dy = y - c, rr = Math.Sqrt(dx * dx + dy * dy), r = rr / c;
                if (r > RDataN / REdge || r < RData0 / REdge)
                    continue;
                int i = (y * size + x) * 4;

                double ang = Math.Atan2(dy, dx);
                if (ang < 0)
                    ang += 2 * Math.PI;
                // Adding one angular turn to the radial phase makes this a
                // continuous representative spiral rather than concentric rings.
                double tf = rr * trackFreq + ang / (2 * Math.PI);
                int track = (int)Math.Floor(tf);
                double within = tf - track;
                if (within > 0.20)
                    continue;

                int cells = 300 + track * 2;
                int cell = (int)(ang / (2 * Math.PI) * cells);
                bool pit = (Hash((uint)(track * 73856093) ^ (uint)((cell / 3) * 19349663)) & 3u) == 0u;

                byte v = pit ? (byte)0xA8 : (byte)0xF2;
                double alpha = pit ? 0.13 : 0.32;
                px[i] = v;
                px[i + 1] = v;
                px[i + 2] = v;
                px[i + 3] = (byte)(alpha * 255);
            }
        return BrushFrom(px, size);
    }

    private static uint Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
        return x;
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

    // Read glow (emissive): teal through the completed program area, a bright
    // edge at the pickup, then transparent media ahead. The existing stops are
    // mutated in place because this runs on every composition frame.
    private void RebuildSurfaceStops(double f)
    {
        double hub = RData0 / REdge;
        double v = Active
            ? Math.Max(hub + 0.002, DataRadius(f) / REdge)
            : hub;
        Color clear = Color.FromArgb(0x00, Teal.R, Teal.G, Teal.B);
        Color glow = Active
            ? Color.FromArgb(0x50, Teal.R, Teal.G, Teal.B)
            : clear;
        Color edge = Unreadable
            ? Crit
            : RereadActive ? Amber
            : Active ? Color.FromRgb(0xD8, 0xFF, 0xF6)
            : clear;
        _readCentre.Color = clear;
        _readCentre.Offset = 0;
        _readHub.Color = glow;
        _readHub.Offset = hub;
        _readBody.Color = glow;
        _readBody.Offset = Math.Max(hub + 0.001, v - 0.025);
        _readEdge.Color = edge;
        _readEdge.Offset = v;
        _readClear.Color = clear;
        _readClear.Offset = Math.Min(1.0, v + 0.010);
        _readOuter.Color = clear;
        _readOuter.Offset = 1;
    }

    // ---- mesh builders ----

    // A flat ring in the XZ plane at height y. Planar UVs (0..1 across the bounding square) so a
    // RadialGradientBrush centred at (0.5,0.5) maps to world radius.
    private static MeshGeometry3D Annulus(
        double rInner,
        double rOuter,
        int seg,
        double y,
        int ny = 1,
        bool flip = false,
        double uvRadius = 0)
    {
        var m = new MeshGeometry3D();
        var normal = new Vector3D(0, ny, 0);
        if (uvRadius <= 0)
            uvRadius = rOuter;
        for (int i = 0; i <= seg; i++)
        {
            double a = 2 * Math.PI * i / seg, c = Math.Cos(a), s = Math.Sin(a);
            m.Positions.Add(new Point3D(rInner * c, y, rInner * s));
            m.Positions.Add(new Point3D(rOuter * c, y, rOuter * s));
            m.Normals.Add(normal); m.Normals.Add(normal);
            m.TextureCoordinates.Add(new Point(
                0.5 + 0.5 * (rInner / uvRadius) * c,
                0.5 + 0.5 * (rInner / uvRadius) * s));
            m.TextureCoordinates.Add(new Point(
                0.5 + 0.5 * (rOuter / uvRadius) * c,
                0.5 + 0.5 * (rOuter / uvRadius) * s));
        }
        for (int i = 0; i < seg; i++)
        {
            int b = i * 2;
            // The point order starts at the inner radius and advances around
            // +Y. Reverse the historical branch so the declared normal and the
            // visible front face agree.
            if (!flip)
            {
                m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 3); m.TriangleIndices.Add(b + 1);
                m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 2); m.TriangleIndices.Add(b + 3);
            }
            else
            {
                m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b + 3);
                m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 3); m.TriangleIndices.Add(b + 2);
            }
        }
        return m;
    }

    private static MeshGeometry3D RingWall(
        double radius,
        double yTop,
        double yBottom,
        int seg,
        bool inward)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i <= seg; i++)
        {
            double a = 2 * Math.PI * i / seg;
            double c = Math.Cos(a);
            double s = Math.Sin(a);
            var normal = new Vector3D(c, 0, s);
            if (inward)
                normal *= -1;
            mesh.Positions.Add(new Point3D(radius * c, yTop, radius * s));
            mesh.Positions.Add(new Point3D(radius * c, yBottom, radius * s));
            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);
            mesh.TextureCoordinates.Add(new Point((double)i / seg, 0));
            mesh.TextureCoordinates.Add(new Point((double)i / seg, 1));
        }
        for (int i = 0; i < seg; i++)
        {
            int b = i * 2;
            if (!inward)
            {
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(b + 3);
                mesh.TriangleIndices.Add(b + 1);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(b + 2);
                mesh.TriangleIndices.Add(b + 3);
            }
            else
            {
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(b + 1);
                mesh.TriangleIndices.Add(b + 3);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(b + 3);
                mesh.TriangleIndices.Add(b + 2);
            }
        }
        return mesh;
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
