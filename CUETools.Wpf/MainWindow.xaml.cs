using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using CUETools.Wpf.Services;

namespace CUETools.Wpf;

public partial class MainWindow : Window
{
    private readonly ThemeService _theme;
    private readonly AppStatusService? _status;

    // live-icon assets, loaded once: the crisp resting icon, 8 spin frames, and the state badges
    private readonly BitmapFrame _restIcon;
    private readonly BitmapImage[] _spin = new BitmapImage[8];
    private readonly BitmapImage _badgeReread, _badgeUnreadable, _badgeDone;
    private readonly DispatcherTimer _spinTimer;
    private int _spinFrame;

    public MainWindow(ThemeService theme, AppStatusService? status = null)
    {
        InitializeComponent();
        _theme = theme;
        _status = status;
        // The palette brushes are recolored in place by ThemeService (they are single mutable
        // objects in the app resources), so the window just plays the light-switch animation.
        // Detach on Closed: ThemeService is a singleton and the startup retry loop can create and
        // discard windows - without the unsubscribe a dead window stays pinned and keeps animating.
        EventHandler onChanged = (_, _) => AnimateLightSwitch(_theme.Current);
        _theme.Changed += onChanged;
        Closed += (_, _) => _theme.Changed -= onChanged;

        // The LIVE ICON: the taskbar button carries the real rip progress; the window/taskbar icon
        // spins while a disc is being read; badges mark re-read (amber), unreadable (red), done
        // (green). All driven by AppStatusService - the same real state the pages report.
        _restIcon = BitmapFrame.Create(Pack("app.ico"));
        Icon = _restIcon;
        for (int i = 0; i < 8; i++) _spin[i] = Png("Assets/disc-spin-" + i + ".png");
        _badgeReread = Png("Assets/badge-reread.png");
        _badgeUnreadable = Png("Assets/badge-unreadable.png");
        _badgeDone = Png("Assets/badge-done.png");
        TaskbarItemInfo = new TaskbarItemInfo();
        _spinTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(110) };
        _spinTimer.Tick += (_, _) => { _spinFrame = (_spinFrame + 1) % 8; Icon = _spin[_spinFrame]; };
        if (_status != null)
        {
            EventHandler onStatus = (_, _) => UpdateLiveIcon();
            _status.Changed += onStatus;
            Closed += (_, _) => { _status.Changed -= onStatus; _spinTimer.Stop(); };
        }
    }

    private static Uri Pack(string rel) => new("pack://application:,,,/" + rel);
    private static BitmapImage Png(string rel)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.UriSource = Pack(rel);
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.EndInit();
        b.Freeze();
        return b;
    }

    private void UpdateLiveIcon()
    {
        if (_status == null || TaskbarItemInfo == null) return;
        var t = TaskbarItemInfo;
        switch (_status.Activity)
        {
            case AppActivity.ReadingDisc:
                t.ProgressState = TaskbarItemProgressState.Indeterminate;
                t.Overlay = null;
                StartSpin();
                break;
            case AppActivity.Ripping:
            case AppActivity.Verifying:
                t.ProgressState = TaskbarItemProgressState.Normal;
                t.ProgressValue = _status.Progress;
                t.Overlay = null;
                StartSpin();
                break;
            case AppActivity.Rereading:
                // paused (amber) fill + amber badge: the drive is fighting a damaged spot
                t.ProgressState = TaskbarItemProgressState.Paused;
                t.ProgressValue = _status.Progress;
                t.Overlay = _badgeReread;
                StartSpin();
                break;
            case AppActivity.Unreadable:
                t.ProgressState = TaskbarItemProgressState.Error;
                t.ProgressValue = Math.Max(0.05, _status.Progress);
                t.Overlay = _badgeUnreadable;
                StartSpin();
                break;
            case AppActivity.Done:
                t.ProgressState = TaskbarItemProgressState.None;
                t.Overlay = _badgeDone;
                StopSpin();
                break;
            default:
                t.ProgressState = TaskbarItemProgressState.None;
                t.Overlay = null;
                StopSpin();
                break;
        }
    }

    private void StartSpin() { if (!_spinTimer.IsEnabled) _spinTimer.Start(); }
    private void StopSpin() { _spinTimer.Stop(); Icon = _restIcon; }

    // Model a real light being switched: going to LIGHT the room brightens (a dark cover fades out
    // fast, like a bulb warming); going to DARK it dims and the warm glow lingers (a warm cover
    // fades out with a long tail, like a filament cooling). The palette itself has already swapped;
    // this overlay just carries the physical brightness curve on top of it.
    private void AnimateLightSwitch(AppTheme theme)
    {
        bool toLight = theme == AppTheme.Light;

        // cover color = the look we are leaving, so fading it out reveals the new theme
        ThemeFlash.Background = new SolidColorBrush(toLight
            ? Color.FromRgb(0x0A, 0x0E, 0x0C)   // dark room (fades away as the light comes on)
            : Color.FromRgb(0xFB, 0xF4, 0xE6));  // warm incandescent glow (lingers as it dies)

        var anim = new DoubleAnimation
        {
            From = toLight ? 0.92 : 0.85,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(toLight ? 300 : 420),  // cool-down lasts longer
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, // fast then long tail
            FillBehavior = FillBehavior.Stop
        };
        ThemeFlash.BeginAnimation(OpacityProperty, anim);
    }
}
