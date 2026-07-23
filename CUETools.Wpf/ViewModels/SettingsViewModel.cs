using System;
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
    private readonly EncoderCatalog _catalog;

    public SettingsViewModel(CUEConfig config, AppSettings app, IDiagnosticLog log, EncoderCatalog catalog)
    {
        Title = "Settings";
        Group = "Setup";
        Subtitle = "Every engine option, grouped. Changes apply to the live configuration.";
        _c = config;
        _app = app;
        _log = log;
        _catalog = catalog;
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());
        RefreshExternalEncoders();
    }

    // ---- external encoders: download links + import for codecs the app cannot bundle ----

    public System.Collections.ObjectModel.ObservableCollection<ExternalEncoderRow> ExternalEncoders { get; } = new();

    private void RefreshExternalEncoders()
    {
        ExternalEncoders.Clear();
        foreach (var info in _catalog.Snapshot(_c))
            ExternalEncoders.Add(new ExternalEncoderRow(info, _c, _catalog, RefreshExternalEncoders));
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
    public bool DisableEject { get => _c.disableEjectDisc; set { _c.disableEjectDisc = value; Raise(); } }

    // General. (OneInstance / CheckForUpdates deliberately not exposed: this app implements
    // neither yet, and a switch that does nothing would be a lie.)
    public bool SeparateDecodingThread { get => _c.separateDecodingThread; set { _c.separateDecodingThread = value; Raise(); } }
    public bool EjectAfterRip { get => _c.ejectAfterRip; set { _c.ejectAfterRip = value; Raise(); } }

    // AccurateRip & CTDB
    public bool NoUnverifiedOutput { get => _c.noUnverifiedOutput; set { _c.noUnverifiedOutput = value; Raise(); } }
    public bool FixOffset { get => _c.fixOffset; set { _c.fixOffset = value; Raise(); } }
    public bool FixOffsetToNearest { get => _c.fixOffsetToNearest; set { _c.fixOffsetToNearest = value; Raise(); } }
    public bool WriteArTagsOnEncode { get => _c.writeArTagsOnEncode; set { _c.writeArTagsOnEncode = value; Raise(); } }
    public bool WriteArTagsOnVerify { get => _c.writeArTagsOnVerify; set { _c.writeArTagsOnVerify = value; Raise(); } }
    public bool WriteArLogOnConvert { get => _c.writeArLogOnConvert; set { _c.writeArLogOnConvert = value; Raise(); } }
    public bool WriteArLogOnVerify { get => _c.writeArLogOnVerify; set { _c.writeArLogOnVerify = value; Raise(); } }
    public bool ArLogToSourceFolder { get => _c.arLogToSourceFolder; set { _c.arLogToSourceFolder = value; Raise(); } }
    public bool ArLogVerbose { get => _c.arLogVerbose; set { _c.arLogVerbose = value; Raise(); } }
    public bool CtdbSubmit { get => _c.advanced.CTDBSubmit; set { _c.advanced.CTDBSubmit = value; Raise(); } }
    public bool CtdbAsk { get => _c.advanced.CTDBAsk; set { _c.advanced.CTDBAsk = value; Raise(); } }

    // File naming & output. (Filename-hygiene rules - remove special chars, replace spaces,
    // ANSI-safe - are owned by the dedicated naming editor, not duplicated here.)
    public string TrackFilenameFormat { get => _c.trackFilenameFormat; set { _c.trackFilenameFormat = value; Raise(); } }
    public bool CreateCueInTracksMode { get => _c.createCUEFileInTracksMode; set { _c.createCUEFileInTracksMode = value; Raise(); } }
    public bool CreateCueWhenEmbedded { get => _c.createCUEFileWhenEmbedded; set { _c.createCUEFileWhenEmbedded = value; Raise(); } }
    public bool CreateM3U { get => _c.createM3U; set { _c.createM3U = value; Raise(); } }
    public bool CreateEacLog { get => _c.createEACLOG; set { _c.createEACLOG = value; Raise(); } }
    public bool EmbedLog { get => _c.embedLog; set { _c.embedLog = value; Raise(); } }
    public bool WriteUtf8Bom { get => _c.writeUTF8BOM; set { _c.writeUTF8BOM = value; Raise(); } }
    public bool AlwaysWriteUtf8Cue { get => _c.alwaysWriteUTF8CUEFile; set { _c.alwaysWriteUTF8CUEFile = value; Raise(); } }
    public bool FillUpCue { get => _c.fillUpCUE; set { _c.fillUpCUE = value; Raise(); } }

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
    public int MaxAlbumArtSize
    {
        get => _c.maxAlbumArtSize;
        set { _c.maxAlbumArtSize = value; Raise(); _app.NotifyArtSizeChanged(); }   // re-derives a fetched cover live
    }

    // HDCD
    public bool DetectHdcd { get => _c.detectHDCD; set { _c.detectHDCD = value; Raise(); } }
    public bool DecodeHdcd { get => _c.decodeHDCD; set { _c.decodeHDCD = value; Raise(); } }
    public bool DecodeHdcdTo24 { get => _c.decodeHDCDto24bit; set { _c.decodeHDCDto24bit = value; Raise(); } }
    public bool Wait750ForHdcd { get => _c.wait750FramesForHDCD; set { _c.wait750FramesForHDCD = value; Raise(); } }
}

/// <summary>One externally-obtainable encoder on the Settings page: its install status, the
/// OFFICIAL download link, and an import (Locate...) that copies the picked exe into the app's
/// encoders folder and lights the format up everywhere.</summary>
public sealed class ExternalEncoderRow : ViewModelBase
{
    private readonly ExternalEncoderInfo _info;
    private readonly CUEConfig _config;
    private readonly EncoderCatalog _catalog;
    private readonly Action _refresh;

    public ExternalEncoderRow(ExternalEncoderInfo info, CUEConfig config, EncoderCatalog catalog, Action refresh)
    {
        _info = info; _config = config; _catalog = catalog; _refresh = refresh;
        DownloadCommand = new RelayCommand(_ => OpenSite());
        LocateCommand = new RelayCommand(_ => Locate());
    }

    public string Display => $"{_info.FormatName}  (.{_info.Extension}, {(_info.Lossless ? "lossless" : "lossy")})";
    public string StatusText => _info.Found ? "installed" : "not installed - download " + _info.ExeName + ", then Locate it here";
    public bool Found => _info.Found;
    public string Tip => _info.Found
        ? $"Using {_info.ResolvedPath}"
        : $"Get {_info.ExeName} from the official site ({_info.DownloadUrl}), then click Locate to import it. " +
          "The file is copied into this app's own encoders folder.";

    public System.Windows.Input.ICommand DownloadCommand { get; }
    public System.Windows.Input.ICommand LocateCommand { get; }

    private void OpenSite()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _info.DownloadUrl, UseShellExecute = true }); }
        catch { }
    }

    private void Locate()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Locate " + _info.ExeName,
            Filter = _info.ExeName + "|" + _info.ExeName + "|Programs|*.exe"
        };
        if (dlg.ShowDialog() != true) return;
        string? err = _catalog.Import(_config, _info, dlg.FileName);
        if (err != null)
            System.Windows.MessageBox.Show(err, "Import failed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        _refresh();
    }
}
