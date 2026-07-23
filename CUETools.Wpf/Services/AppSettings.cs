namespace CUETools.Wpf.Services;

/// <summary>
/// App-level behaviour settings (not CUETools engine config). Persisted alongside the engine
/// config by <see cref="SettingsStore"/> (loaded at startup, saved on exit). Injected where the
/// rip lifecycle needs them.
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

    /// <summary>Stop the rip when the drive exhausts its retries on a sector and still cannot read it,
    /// instead of finishing and leaving that spot unread. Off by default (today's behaviour is to
    /// carry on and mark the bad sectors, then let CTDB parity try to fill them). When on, the 3D disc
    /// holds zoomed on the failed spot with a flashing marker until you eject or stop.</summary>
    public bool StopOnUnrecoverable { get; set; } = false;

    // Rip-page preferences that should survive a restart. Empty string means "not set yet" and the
    // page falls back to its default (MyMusic\CUETools, flac, Secure).
    public string OutputBaseDir { get; set; } = "";
    public string SelectedFormat { get; set; } = "";
    public int CorrectionQuality { get; set; } = 1;   // 0=Burst, 1=Secure, 2=Paranoid

    /// <summary>The one-time archival encoder defaults (max compression lossless, sweet-spot lossy)
    /// were applied to this profile. After that, every encoder-mode choice is the user's.</summary>
    public bool ArchivalDefaultsApplied { get; set; } = false;

    // The naming scheme (template + clean-up rule flags), edited on the Naming page. Defaults to the
    // owner's archival scheme with all rules on.
    public string NamingTemplate { get; set; } = NamingScheme.ArchivalTemplate;
    public bool NamingExtractFeatured { get; set; } = true;
    public bool NamingUnifySeparators { get; set; } = true;
    public bool NamingHandleArticles { get; set; } = true;
    public bool NamingStripIllegal { get; set; } = true;
    public bool NamingReleaseDescriptor { get; set; } = true;

    public NamingScheme LoadNamingScheme() => new NamingScheme
    {
        Template = string.IsNullOrWhiteSpace(NamingTemplate) ? NamingScheme.ArchivalTemplate : NamingTemplate,
        ExtractFeatured = NamingExtractFeatured,
        UnifySeparators = NamingUnifySeparators,
        HandleArticles = NamingHandleArticles,
        StripIllegal = NamingStripIllegal,
        ReleaseDescriptor = NamingReleaseDescriptor,
    };

    public void SaveNamingScheme(NamingScheme s)
    {
        NamingTemplate = s.Template;
        NamingExtractFeatured = s.ExtractFeatured;
        NamingUnifySeparators = s.UnifySeparators;
        NamingHandleArticles = s.HandleArticles;
        NamingStripIllegal = s.StripIllegal;
        NamingReleaseDescriptor = s.ReleaseDescriptor;
    }
}
