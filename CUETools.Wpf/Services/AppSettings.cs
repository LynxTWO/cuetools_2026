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

    /// <summary>One-time owner-default migration, round 2: cover size 300 (the stale engine
    /// default) -> 1000 px, and AccurateRip tags on encode ON. After it runs once, the user's
    /// choices stick.</summary>
    public bool DefaultsV2Applied { get; set; } = false;

    /// <summary>Raised when the cover max-size setting changes, so an already-fetched cover is
    /// re-derived from its cached master at the new size (no re-fetch).</summary>
    public event System.EventHandler? ArtSizeChanged;
    public void NotifyArtSizeChanged() => ArtSizeChanged?.Invoke(this, System.EventArgs.Empty);

    // Per-format lossless/lossy choice for two-faced extensions (wma = WMA Lossless vs Standard;
    // m4a = ALAC vs imported AAC). Compact persisted form: "wma=lossy;m4a=lossless".
    public string FormatTypeOverrides { get; set; } = "";

    public bool? GetFormatTypeOverride(string ext)
    {
        foreach (var part in (FormatTypeOverrides ?? "").Split(';'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0] == ext)
                return kv[1] == "lossy";
        }
        return null;
    }

    public void SetFormatTypeOverride(string ext, bool lossy)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (var part in (FormatTypeOverrides ?? "").Split(';'))
            if (part.Length > 0 && !part.StartsWith(ext + "=")) parts.Add(part);
        parts.Add(ext + "=" + (lossy ? "lossy" : "lossless"));
        FormatTypeOverrides = string.Join(";", parts);
    }

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
