using System;

namespace CUETools.Wpf.Services;

/// <summary>What the app is doing right now, at icon granularity.</summary>
public enum AppActivity
{
    Idle,
    ReadingDisc,
    Ripping,
    Verifying,
    Rereading,      // the drive is fighting a damaged spot
    Unreadable,     // a spot the drive gave up on
    Done            // a rip finished (until dismissed / next job)
}

/// <summary>
/// The app-wide activity feed for the LIVE ICON: the Rip page reports what is happening and the
/// main window turns it into taskbar truth - a spinning disc icon while ripping, the real rip
/// progress filling the taskbar button, and overlay badges for re-read / unreadable / done.
/// UI-thread only (the reporters already marshal), deliberately tiny.
/// </summary>
public sealed class AppStatusService
{
    public AppActivity Activity { get; private set; } = AppActivity.Idle;
    public double Progress { get; private set; }

    public event EventHandler? Changed;

    public void Report(AppActivity activity, double progress = double.NaN)
    {
        bool changed = activity != Activity;
        Activity = activity;
        if (!double.IsNaN(progress) && Math.Abs(progress - Progress) > 0.001) { Progress = progress; changed = true; }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }
}
