namespace CUETools.Wpf.Services;

/// <summary>
/// App-level behaviour settings (not CUETools engine config). In-memory with sensible defaults for
/// now; broad settings persistence lands later. Injected where the rip lifecycle needs them.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Prevent the drive tray from being ejected (its own button or software) while a rip
    /// or verify is running, so the read cannot be interrupted mid-stream. Off by default; turning
    /// it on also avoids the drive-not-ready failure a mid-read eject would otherwise cause.</summary>
    public bool LockTrayDuringRip { get; set; } = false;

    /// <summary>Keep the computer awake (no sleep, no display timeout) while a rip or verify runs,
    /// so a long secure rip is not cut short by the machine sleeping. On by default.</summary>
    public bool PreventSleepDuringRip { get; set; } = true;
}
