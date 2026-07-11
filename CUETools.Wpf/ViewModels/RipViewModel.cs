using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using CUETools.Processor;
using CUETools.Ripper.SCSI;
using CUETools.Wpf.Models;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Rip page. Increment 1: enumerate drives, read the inserted disc's TOC through the
/// ripper, show the track list; no-disc shows the recently-ripped list. Encode/verify and
/// the live disc read-map come in later increments.
/// </summary>
public sealed class RipViewModel : PageViewModel
{
    private readonly IDriveService _drives;
    private readonly IRipService _rip;
    private readonly IReportStore _reports;
    private readonly IHistoryStore _history;
    private readonly CUEConfig _config;

    // The last disc read, kept so a finished job can be turned into a full RipReport
    // (drive, offset, TOC) rather than just the AR/CTDB numbers.
    private DiscInfo? _lastDisc;

    public ObservableCollection<char> Drives { get; } = new();
    public ObservableCollection<TrackItem> Tracks { get; } = new();
    public ObservableCollection<RecentRip> Recent { get; } = new();

    // Real output formats (only those with a working encoder in this build), not a fixed list.
    public ObservableCollection<string> Formats { get; } = new();

    private string _selectedFormat = "flac";
    public string SelectedFormat { get => _selectedFormat; set => Set(ref _selectedFormat, value); }

    // Ranked release matches for the inserted disc; picking one applies its metadata to the rip.
    public ObservableCollection<ReleaseMatch> Releases { get; } = new();
    public bool HasReleases => Releases.Count > 0;

    private CUEMetadata? _chosenMetadata;

    private ReleaseMatch? _selectedRelease;
    public ReleaseMatch? SelectedRelease
    {
        get => _selectedRelease;
        set { if (Set(ref _selectedRelease, value)) ApplyRelease(value); }
    }

    // where ripped albums go (an "Artist - Album" folder is created under this base)
    private string _outputBaseDir = "";
    public string OutputBaseDir { get => _outputBaseDir; set => Set(ref _outputBaseDir, value); }

    private string _lastOutputDir = "";
    public string LastOutputDir { get => _lastOutputDir; private set => Set(ref _lastOutputDir, value); }

    // "rip complete" state shown after a successful rip
    private bool _ripDone;
    public bool RipDone { get => _ripDone; private set => Set(ref _ripDone, value); }

    private string _ripSummary = "";
    public string RipSummary { get => _ripSummary; private set => Set(ref _ripSummary, value); }

    private char _selectedDrive;
    public char SelectedDrive
    {
        get => _selectedDrive;
        set { if (Set(ref _selectedDrive, value)) _ = ReadDiscAsync(); }
    }

    private bool _isDiscPresent;
    public bool IsDiscPresent { get => _isDiscPresent; private set => Set(ref _isDiscPresent, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    // physical tray/media state, polled live from the drive (open / closed-empty / closed-disc)
    private DriveTrayState _tray = DriveTrayState.Unknown;
    public DriveTrayState TrayState
    {
        get => _tray;
        private set
        {
            if (Set(ref _tray, value))
            {
                OnPropertyChanged(nameof(IsTrayOpen));
                OnPropertyChanged(nameof(EjectButtonText));
                OnPropertyChanged(nameof(EjectButtonTip));
            }
        }
    }
    public bool IsTrayOpen => _tray == DriveTrayState.Open;
    // one button ejects when the tray is in, and closes it when it is out
    public string EjectButtonText => _tray == DriveTrayState.Open ? "Close" : "Eject";
    public string EjectButtonTip => _tray == DriveTrayState.Open
        ? "Pull the tray in, then read the disc automatically."
        : "Open the drive tray.";

    // the drive model shown in the bar (read from INQUIRY - no disc needed)
    private string _driveModel = "";
    public string DriveModel { get => _driveModel; private set => Set(ref _driveModel, value); }

    // disc-insertion watcher state
    private DispatcherTimer? _trayWatch;
    private bool _triedCurrentMedia;   // guards against re-reading a data disc every poll

    private string _albumTitle = "Insert a disc";
    public string AlbumTitle { get => _albumTitle; private set => Set(ref _albumTitle, value); }

    private string _albumArtist = "";
    public string AlbumArtist { get => _albumArtist; private set => Set(ref _albumArtist, value); }

    private string _discInfoText = "";
    public string DiscInfoText { get => _discInfoText; private set => Set(ref _discInfoText, value); }

    private string _statusText = "Ready";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private double _ripProgress;
    public double RipProgress { get => _ripProgress; private set => Set(ref _ripProgress, value); }

    private bool _isRipping;
    public bool IsRipping { get => _isRipping; private set => Set(ref _isRipping, value); }

    // live read-speed readout for the SpeedGraph (0..1 of a 12x display cap) + "6.4x" text
    private double _speedLevel;
    public double SpeedLevel { get => _speedLevel; private set => Set(ref _speedLevel, value); }

    private string _speedText = "";
    public string SpeedText { get => _speedText; private set => Set(ref _speedText, value); }

    // real per-channel peak levels for the VU meter (0..1), tapped from the disc audio
    private double _levelL;
    public double LevelL { get => _levelL; private set => Set(ref _levelL, value); }

    private double _levelR;
    public double LevelR { get => _levelR; private set => Set(ref _levelR, value); }

    private const double SpeedCapX = 12.0;
    private double _discSeconds;
    private double _lastSpeedFrac;
    private DateTime _lastSpeedTick;
    private double[] _trackEndFrac = Array.Empty<double>();

    private string _ripStatus = "";
    public string RipStatus { get => _ripStatus; private set => Set(ref _ripStatus, value); }

    // 0 = Burst, 1 = Secure, 2 = Paranoid (maps to the drive's CorrectionQuality)
    private int _correctionQuality = 1;
    public int CorrectionQuality { get => _correctionQuality; set => Set(ref _correctionQuality, value); }

    private string _arText = "not checked";
    public string ArText { get => _arText; private set => Set(ref _arText, value); }

    private string _ctdbText = "not checked";
    public string CtdbText { get => _ctdbText; private set => Set(ref _ctdbText, value); }

    private bool _accurate;
    public bool Accurate { get => _accurate; private set => Set(ref _accurate, value); }

    // per-disc options, bound to the live config
    public bool CreateCue { get => _config.createCUEFileInTracksMode; set { _config.createCUEFileInTracksMode = value; OnPropertyChanged(); } }
    public bool WriteLog { get => _config.createEACLOG; set { _config.createEACLOG = value; OnPropertyChanged(); } }
    public bool EmbedArt { get => _config.embedAlbumArt; set { _config.embedAlbumArt = value; OnPropertyChanged(); } }

    public ICommand ReadDiscCommand { get; }
    public ICommand VerifyCommand { get; }
    public ICommand RipCommand { get; }
    public ICommand EjectCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand DismissDoneCommand { get; }

    public RipViewModel(IDriveService drives, IRipService rip, IConvertService codecs, IReportStore reports, IHistoryStore history, CUEConfig config)
    {
        Title = "Rip";
        Group = "Work";
        Subtitle = "Rip a CD: read, encode, and verify against AccurateRip and CTDB.";
        _drives = drives;
        _rip = rip;
        _reports = reports;
        _history = history;
        _config = config;
        _outputBaseDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CUETools");

        foreach (var f in codecs.LosslessFormats()) Formats.Add(f);
        if (!Formats.Contains(_selectedFormat)) _selectedFormat = Formats.Count > 0 ? Formats[0] : "flac";

        ReadDiscCommand = new RelayCommand(_ => { _ = ReadDiscAsync(); });
        VerifyCommand = new RelayCommand(_ => { _ = RunJobAsync(encode: false); }, _ => IsDiscPresent && !IsRipping && !IsBusy);
        RipCommand = new RelayCommand(_ => { _ = RunJobAsync(encode: true); }, _ => IsDiscPresent && !IsRipping && !IsBusy);
        EjectCommand = new RelayCommand(_ => ToggleTray(), _ => Drives.Count > 0 && !IsRipping && !IsBusy);
        BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
        OpenFolderCommand = new RelayCommand(_ => OpenFolder(), _ => LastOutputDir.Length > 0);
        DismissDoneCommand = new RelayCommand(_ => RipDone = false);

        foreach (var d in drives.GetDrives()) Drives.Add(d);
        LoadRecent();

        if (Drives.Count > 0)
        {
            _selectedDrive = Drives[0];   // set the field to avoid a double read from the setter
            OnPropertyChanged(nameof(SelectedDrive));
            _ = ReadDiscAsync();
            StartTrayWatch();
        }
        else
        {
            StatusText = "No optical drive found.";
        }
    }

    // Poll the drive for tray/media changes so the UI reacts to the physical eject button and to a
    // disc being dropped in and the tray pushed shut - the classic "insert disc, it just reads"
    // behaviour. GET EVENT STATUS NOTIFICATION does not spin the disc; we skip polling mid-read so
    // we never fight the ripper for the device.
    private void StartTrayWatch()
    {
        _trayWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        _trayWatch.Tick += async (_, _) => await PollTrayAsync();
        _trayWatch.Start();
    }

    private async Task PollTrayAsync()
    {
        if (_selectedDrive == '\0' || _isBusy || IsRipping) return;
        char drive = _selectedDrive;
        DriveTrayState tray = await Task.Run(() => _drives.GetTrayState(drive));
        if (tray == DriveTrayState.Unknown) return;
        TrayState = tray;

        if (tray == DriveTrayState.ClosedWithDisc)
        {
            // a disc is loaded and we have not read this one yet -> read it and show the rip screen
            if (!IsDiscPresent && !_triedCurrentMedia && !_isBusy)
                await ReadDiscAsync();
        }
        else
        {
            _triedCurrentMedia = false;          // media is gone; allow a fresh read next time
            if (IsDiscPresent) ClearDiscView(tray);
        }
    }

    private async Task ReadDiscAsync()
    {
        if (_selectedDrive == '\0' || _isBusy) return;
        IsBusy = true;
        _triedCurrentMedia = true;           // this read counts as our attempt at the current media
        char drive = _selectedDrive;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        // identity + tray state first (works with an empty tray) so the bar shows the drive model
        StatusText = "Reading drive...";
        DriveDetails details = await Task.Run(() => _drives.GetDriveDetails(drive));
        if (details.Valid) DriveModel = details.Model;
        TrayState = details.Tray;

        StatusText = "Reading disc...";
        // surface the live metadata-lookup step (which database is being queried)
        void OnStatus(string s) => dispatcher?.BeginInvoke(new Action(() => { if (_isBusy) StatusText = s; }));

        DiscInfo? info = await Task.Run(() => _drives.ReadDisc(drive, OnStatus));
        _lastDisc = info;

        Tracks.Clear();
        Releases.Clear();
        _chosenMetadata = null;
        _selectedRelease = null;
        if (info == null)
        {
            IsDiscPresent = false;
            bool open = TrayState == DriveTrayState.Open;
            AlbumTitle = open ? "Tray open - insert a disc, then Close" : "No disc in drive " + drive + ":";
            AlbumArtist = "";
            DiscInfoText = "";
            StatusText = open ? "Tray open." : "Drive ready - insert an audio CD to begin.";
        }
        else
        {
            IsDiscPresent = true;
            AlbumTitle = info.Album;
            AlbumArtist = string.IsNullOrWhiteSpace(info.Artist)
                ? (string.IsNullOrWhiteSpace(info.DriveName) ? $"Drive {drive}:" : info.DriveName)
                : (string.IsNullOrWhiteSpace(info.Year) ? info.Artist : $"{info.Artist}  ({info.Year})");
            DiscInfoText = $"{info.AudioTracks} tracks   {Fmt(info.TotalLength)}   {info.Releases.Count} release match(es)";
            foreach (var t in info.Tracks) Tracks.Add(t);
            foreach (var rm in info.Releases) Releases.Add(rm);
            _selectedRelease = System.Linq.Enumerable.FirstOrDefault(Releases, r => r.IsBest) ?? (Releases.Count > 0 ? Releases[0] : null);
            _chosenMetadata = _selectedRelease?.Metadata;
            StatusText = info.Releases.Count > 0
                ? $"Identified: {info.Artist} - {info.Album}. Ripping comes next."
                : "Disc read; not found in the metadata databases (generic track names).";
        }
        OnPropertyChanged(nameof(SelectedRelease));
        OnPropertyChanged(nameof(HasReleases));
        IsBusy = false;
    }

    // Choosing a release re-labels the album and re-titles the tracks from that release, and makes
    // the rip use its metadata instead of the auto-picked best.
    private void ApplyRelease(ReleaseMatch? r)
    {
        if (r?.Metadata == null) return;
        _chosenMetadata = r.Metadata;
        if (!string.IsNullOrWhiteSpace(r.Title)) AlbumTitle = r.Title;
        AlbumArtist = string.IsNullOrWhiteSpace(r.Artist)
            ? AlbumArtist
            : (string.IsNullOrWhiteSpace(r.Year) ? r.Artist : $"{r.Artist}  ({r.Year})");
        var mt = r.Metadata.Tracks;
        for (int i = 0; i < Tracks.Count; i++)
            if (mt != null && i < mt.Count && !string.IsNullOrWhiteSpace(mt[i].Title))
                Tracks[i].Title = mt[i].Title;
    }

    private async Task RunJobAsync(bool encode)
    {
        if (!IsDiscPresent || IsRipping || IsBusy) return;
        char drive = _selectedDrive;
        int cq = CorrectionQuality;
        IsRipping = true;
        RipDone = false;
        RipProgress = 0;
        StatusText = encode ? "Starting rip..." : "Starting verify...";
        _discSeconds = _lastDisc?.TotalLength.TotalSeconds ?? 0;
        _lastSpeedFrac = 0;
        _lastSpeedTick = DateTime.UtcNow;
        SpeedLevel = 0;
        SpeedText = "";

        // per-track progress boundaries as fractions of the whole disc (by track length)
        double totalSec = 0;
        foreach (var t in Tracks) totalSec += t.Length.TotalSeconds;
        _trackEndFrac = new double[Tracks.Count];
        double cum = 0;
        for (int i = 0; i < Tracks.Count; i++)
        {
            cum += Tracks[i].Length.TotalSeconds;
            _trackEndFrac[i] = totalSec > 0 ? cum / totalSec : 0;
            Tracks[i].Progress = 0;
            Tracks[i].Active = false;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        // ReadProgress + level metering fire on the ripper's thread; marshal to the UI.
        void Report(double frac, string status)
            => dispatcher?.BeginInvoke(new Action(() => { RipProgress = frac; StatusText = status; UpdateSpeed(frac); UpdateTrackProgress(frac); }));
        void Levels(double l, double r)
            => dispatcher?.BeginInvoke(new Action(() => { LevelL = l; LevelR = r; }));

        string fmt = SelectedFormat;
        var meta = _chosenMetadata;
        string outBase = OutputBaseDir;
        var result = await Task.Run(() => encode ? _rip.RunEncode(drive, cq, fmt, meta, outBase, Report, Levels) : _rip.RunVerify(drive, cq, meta, Report, Levels));

        RipProgress = result.Ok ? 1 : RipProgress;
        if (result.Ok)
        {
            ArText = $"{result.ArConfidence} / {result.ArTotal}" + (result.Accurate ? "  accurate" : "");
            CtdbText = result.CtdbConfidence > 0 ? $"match . conf {result.CtdbConfidence}" : $"{result.CtdbConfidence} / {result.CtdbTotal}";
            Accurate = result.Accurate;
            StatusText = encode ? $"Ripped {result.FileCount} files -> {result.OutputDir}" : result.Status;
            if (encode)
            {
                LastOutputDir = result.OutputDir;
                RipSummary = $"Ripped {result.FileCount} {fmt} files"
                    + (result.Accurate ? $"  .  AccurateRip verified (confidence {result.ArConfidence})" : "  .  not found in AccurateRip")
                    + (result.CtdbConfidence > 0 ? $"  .  CTDB confidence {result.CtdbConfidence}" : "");
                RipDone = true;
            }
            ApplyPerTrack(result);
            PublishReport(encode, result);
        }
        else
        {
            StatusText = (encode ? "Rip failed: " : "Verify failed: ") + result.Error;
        }
        LevelL = 0; LevelR = 0;   // needles fall back to rest when the job ends
        foreach (var t in Tracks) { t.Active = false; if (result.Ok) t.Progress = 1; }
        IsRipping = false;
    }

    // Convert progress deltas into read speed as a multiple of realtime (1x = 75 sectors/s).
    // speed = (fraction of disc read) * (disc seconds) / (elapsed seconds).
    private void UpdateSpeed(double frac)
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastSpeedTick).TotalSeconds;
        double dFrac = frac - _lastSpeedFrac;
        if (dt < 0.08 || dFrac <= 0 || _discSeconds <= 0) return;
        double sx = dFrac * _discSeconds / dt;
        SpeedText = $"{sx:0.0}x";
        SpeedLevel = Math.Max(0, Math.Min(1, sx / SpeedCapX));
        _lastSpeedTick = now;
        _lastSpeedFrac = frac;
    }

    // The eject button toggles the tray: open it when it is in, close it when it is out. Closing
    // hands off to the watcher, which sees the disc land and reads it automatically.
    private void ToggleTray()
    {
        char d = _selectedDrive;
        if (d == '\0') return;
        _triedCurrentMedia = false;   // a fresh tray cycle: allow the next disc to be read
        if (TrayState == DriveTrayState.Open)
        {
            StatusText = $"Closing tray {d}:...";
            TrayState = DriveTrayState.ClosedNoDisc;   // optimistic; the watcher confirms + reads
            Task.Run(() => { try { _drives.CloseTray(d); } catch { } });
        }
        else
        {
            StatusText = $"Ejecting drive {d}:...";
            TrayState = DriveTrayState.Open;
            ClearDiscView(DriveTrayState.Open);
            Task.Run(() => { try { _drives.OpenTray(d); } catch { } });
        }
    }

    // Drop back to the no-disc view (tray open or emptied). Keeps the disc read-map / tracks from
    // lingering after the media is gone.
    private void ClearDiscView(DriveTrayState tray)
    {
        IsDiscPresent = false;
        RipDone = false;
        Tracks.Clear();
        Releases.Clear();
        _chosenMetadata = null;
        _selectedRelease = null;
        bool open = tray == DriveTrayState.Open;
        AlbumTitle = open ? "Tray open - insert a disc, then Close" : "No disc - insert an audio CD";
        AlbumArtist = "";
        DiscInfoText = "";
        StatusText = open ? "Tray open." : "Drive ready - insert an audio CD.";
        OnPropertyChanged(nameof(HasReleases));
        OnPropertyChanged(nameof(SelectedRelease));
    }

    private void BrowseOutput()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where ripped albums go" };
        if (dlg.ShowDialog() == true) OutputBaseDir = dlg.FolderName;
    }

    private void OpenFolder()
    {
        try
        {
            if (System.IO.Directory.Exists(LastOutputDir))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = LastOutputDir, UseShellExecute = true });
        }
        catch { }
    }

    // Map the whole-disc read fraction onto each track: done tracks fill, the current track shows
    // its own progress, later tracks stay empty. Real (position-based), not a status-string guess.
    private void UpdateTrackProgress(double frac)
    {
        double start = 0;
        for (int i = 0; i < Tracks.Count && i < _trackEndFrac.Length; i++)
        {
            double end = _trackEndFrac[i];
            if (frac >= end) { Tracks[i].Progress = 1; Tracks[i].Active = false; }
            else if (frac >= start) { double span = end - start; Tracks[i].Progress = span > 1e-6 ? (frac - start) / span : 0; Tracks[i].Active = true; }
            else { Tracks[i].Progress = 0; Tracks[i].Active = false; }
            start = end;
        }
    }

    private void ApplyPerTrack(VerifyResult result)
    {
        for (int i = 0; i < Tracks.Count; i++)
        {
            int ar = i < result.ArPerTrack.Length ? result.ArPerTrack[i] : 0;
            int ct = i < result.CtdbPerTrack.Length ? result.CtdbPerTrack[i] : 0;
            Tracks[i].ArResult = ar > 0 ? ar.ToString() : "-";
            Tracks[i].CtdbResult = ct > 0 ? ct.ToString() : "-";
        }
    }

    private void PublishReport(bool encode, VerifyResult result)
    {
        var d = _lastDisc;
        var report = new RipReport
        {
            Mode = encode ? "Rip" : "Verify",
            Album = d?.Album ?? AlbumTitle,
            Artist = d?.Artist ?? "",
            Year = d?.Year ?? "",
            DriveName = d?.DriveName ?? "",
            Offset = d?.Offset ?? 0,
            CorrectionQuality = CorrectionQuality,
            ArConfidence = result.ArConfidence,
            ArTotal = result.ArTotal,
            CtdbConfidence = result.CtdbConfidence,
            CtdbTotal = result.CtdbTotal,
            Accurate = result.Accurate,
            Status = result.Status,
            OutputDir = result.OutputDir,
            FileCount = result.FileCount,
            TrackCount = d?.AudioTracks ?? Tracks.Count,
            TocId = d?.TocId ?? ""
        };
        _reports.Publish(report);
        _history.Add(report);
        LoadRecent();
    }

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private void LoadRecent()
    {
        // Real history only - the last discs handled in this app, from the persistent store.
        Recent.Clear();
        foreach (var r in _history.Recent(10)) Recent.Add(r);
    }
}
