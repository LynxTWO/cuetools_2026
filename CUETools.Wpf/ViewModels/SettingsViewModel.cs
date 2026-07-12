using System.Runtime.CompilerServices;
using System.Windows.Input;
using CUETools.Processor;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Settings page bound to the live CUEConfig singleton - editing here changes the actual
/// config the rip/metadata paths use. A representative slice of the ~90 options across the
/// main sections; the rest (and the codec editor) follow. CUEConfig exposes public fields,
/// so each is wrapped as a bindable property.
/// </summary>
public sealed class SettingsViewModel : PageViewModel
{
    private readonly CUEConfig _c;
    private readonly AppSettings _app;
    private readonly IDiagnosticLog _log;

    public SettingsViewModel(CUEConfig config, AppSettings app, IDiagnosticLog log)
    {
        Title = "Settings";
        Group = "Setup";
        Subtitle = "Every engine option, grouped. Changes apply to the live configuration.";
        _c = config;
        _app = app;
        _log = log;
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
    }

    private void Raise([CallerMemberName] string? n = null) => OnPropertyChanged(n);

    // Diagnostics: the privacy-safe log for this run (structure only, no album/artist/track names).
    public string LogPath => _log.LogPath;
    public ICommand OpenLogFolderCommand { get; }

    private void OpenLogFolder()
    {
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(_log.LogPath);
            if (dir != null && System.IO.Directory.Exists(dir))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch { /* best-effort */ }
    }

    // Ripping behaviour (app-level)
    public bool PreventSleepDuringRip { get => _app.PreventSleepDuringRip; set { _app.PreventSleepDuringRip = value; Raise(); } }
    public bool LockTrayDuringRip { get => _app.LockTrayDuringRip; set { _app.LockTrayDuringRip = value; Raise(); } }
    public bool StopOnUnrecoverable { get => _app.StopOnUnrecoverable; set { _app.StopOnUnrecoverable = value; Raise(); } }

    // General
    public bool OneInstance { get => _c.oneInstance; set { _c.oneInstance = value; Raise(); } }
    public bool CheckForUpdates { get => _c.checkForUpdates; set { _c.checkForUpdates = value; Raise(); } }
    public bool SeparateDecodingThread { get => _c.separateDecodingThread; set { _c.separateDecodingThread = value; Raise(); } }
    public bool EjectAfterRip { get => _c.ejectAfterRip; set { _c.ejectAfterRip = value; Raise(); } }

    // AccurateRip & CTDB
    public bool NoUnverifiedOutput { get => _c.noUnverifiedOutput; set { _c.noUnverifiedOutput = value; Raise(); } }
    public bool FixOffset { get => _c.fixOffset; set { _c.fixOffset = value; Raise(); } }
    public bool WriteArTagsOnEncode { get => _c.writeArTagsOnEncode; set { _c.writeArTagsOnEncode = value; Raise(); } }
    public bool WriteArLogOnConvert { get => _c.writeArLogOnConvert; set { _c.writeArLogOnConvert = value; Raise(); } }
    public bool CtdbSubmit { get => _c.advanced.CTDBSubmit; set { _c.advanced.CTDBSubmit = value; Raise(); } }
    public bool CtdbAsk { get => _c.advanced.CTDBAsk; set { _c.advanced.CTDBAsk = value; Raise(); } }

    // File naming & output
    public string TrackFilenameFormat { get => _c.trackFilenameFormat; set { _c.trackFilenameFormat = value; Raise(); } }
    public bool CreateCueInTracksMode { get => _c.createCUEFileInTracksMode; set { _c.createCUEFileInTracksMode = value; Raise(); } }
    public bool CreateM3U { get => _c.createM3U; set { _c.createM3U = value; Raise(); } }
    public bool CreateEacLog { get => _c.createEACLOG; set { _c.createEACLOG = value; Raise(); } }
    public bool WriteUtf8Bom { get => _c.writeUTF8BOM; set { _c.writeUTF8BOM = value; Raise(); } }

    // Gaps & HTOA
    public bool DetectGaps { get => _c.detectGaps; set { _c.detectGaps = value; Raise(); } }
    public bool PreserveHtoa { get => _c.preserveHTOA; set { _c.preserveHTOA = value; Raise(); } }
    public bool UseHtoaThreshold { get => _c.useHTOALengthThreshold; set { _c.useHTOALengthThreshold = value; Raise(); } }

    // Tagging
    public bool BasicTagsFromCue { get => _c.writeBasicTagsFromCUEData; set { _c.writeBasicTagsFromCUEData = value; Raise(); } }
    public bool CopyBasicTags { get => _c.copyBasicTags; set { _c.copyBasicTags = value; Raise(); } }
    public bool CopyUnknownTags { get => _c.copyUnknownTags; set { _c.copyUnknownTags = value; Raise(); } }

    // Album art
    public bool EmbedAlbumArt { get => _c.embedAlbumArt; set { _c.embedAlbumArt = value; Raise(); } }
    public bool ExtractAlbumArt { get => _c.extractAlbumArt; set { _c.extractAlbumArt = value; Raise(); } }
    public int MaxAlbumArtSize { get => _c.maxAlbumArtSize; set { _c.maxAlbumArtSize = value; Raise(); } }

    // HDCD
    public bool DetectHdcd { get => _c.detectHDCD; set { _c.detectHDCD = value; Raise(); } }
    public bool DecodeHdcd { get => _c.decodeHDCD; set { _c.decodeHDCD = value; Raise(); } }
    public bool DecodeHdcdTo24 { get => _c.decodeHDCDto24bit; set { _c.decodeHDCDto24bit = value; Raise(); } }
}
