using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CUETools.Processor;
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

        foreach (var f in codecs.LosslessFormats()) Formats.Add(f);
        if (!Formats.Contains(_selectedFormat)) _selectedFormat = Formats.Count > 0 ? Formats[0] : "flac";

        ReadDiscCommand = new RelayCommand(_ => { _ = ReadDiscAsync(); });
        VerifyCommand = new RelayCommand(_ => { _ = RunJobAsync(encode: false); }, _ => IsDiscPresent && !IsRipping && !IsBusy);
        RipCommand = new RelayCommand(_ => { _ = RunJobAsync(encode: true); }, _ => IsDiscPresent && !IsRipping && !IsBusy);

        foreach (var d in drives.GetDrives()) Drives.Add(d);
        LoadRecent();

        if (Drives.Count > 0)
        {
            _selectedDrive = Drives[0];   // set the field to avoid a double read from the setter
            OnPropertyChanged(nameof(SelectedDrive));
            _ = ReadDiscAsync();
        }
        else
        {
            StatusText = "No optical drive found.";
        }
    }

    private async Task ReadDiscAsync()
    {
        if (_selectedDrive == '\0' || _isBusy) return;
        IsBusy = true;
        StatusText = "Reading disc...";
        char drive = _selectedDrive;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        // surface the live metadata-lookup step (which database is being queried)
        void OnStatus(string s) => dispatcher?.BeginInvoke(new Action(() => { if (_isBusy) StatusText = s; }));

        DiscInfo? info = await Task.Run(() => _drives.ReadDisc(drive, OnStatus));
        _lastDisc = info;

        Tracks.Clear();
        if (info == null)
        {
            IsDiscPresent = false;
            AlbumTitle = "No disc in drive " + drive + ":";
            AlbumArtist = "";
            DiscInfoText = "";
            StatusText = "Drive ready - insert an audio CD to begin.";
        }
        else
        {
            IsDiscPresent = true;
            AlbumTitle = info.Album;
            AlbumArtist = string.IsNullOrWhiteSpace(info.Artist)
                ? (string.IsNullOrWhiteSpace(info.DriveName) ? $"Drive {drive}:" : info.DriveName)
                : (string.IsNullOrWhiteSpace(info.Year) ? info.Artist : $"{info.Artist}  ({info.Year})");
            DiscInfoText = $"{info.AudioTracks} tracks   {Fmt(info.TotalLength)}   {info.ReleaseMatches.Count} release match(es)";
            foreach (var t in info.Tracks) Tracks.Add(t);
            StatusText = info.ReleaseMatches.Count > 0
                ? $"Identified: {info.Artist} - {info.Album}. Ripping comes next."
                : "Disc read; not found in the metadata databases (generic track names).";
        }
        IsBusy = false;
    }

    private async Task RunJobAsync(bool encode)
    {
        if (!IsDiscPresent || IsRipping || IsBusy) return;
        char drive = _selectedDrive;
        int cq = CorrectionQuality;
        IsRipping = true;
        RipProgress = 0;
        StatusText = encode ? "Starting rip..." : "Starting verify...";
        _discSeconds = _lastDisc?.TotalLength.TotalSeconds ?? 0;
        _lastSpeedFrac = 0;
        _lastSpeedTick = DateTime.UtcNow;
        SpeedLevel = 0;
        SpeedText = "";
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        // ReadProgress + level metering fire on the ripper's thread; marshal to the UI.
        void Report(double frac, string status)
            => dispatcher?.BeginInvoke(new Action(() => { RipProgress = frac; StatusText = status; UpdateSpeed(frac); }));
        void Levels(double l, double r)
            => dispatcher?.BeginInvoke(new Action(() => { LevelL = l; LevelR = r; }));

        string fmt = SelectedFormat;
        var result = await Task.Run(() => encode ? _rip.RunEncode(drive, cq, fmt, Report, Levels) : _rip.RunVerify(drive, cq, Report, Levels));

        RipProgress = result.Ok ? 1 : RipProgress;
        if (result.Ok)
        {
            ArText = $"{result.ArConfidence} / {result.ArTotal}" + (result.Accurate ? "  accurate" : "");
            CtdbText = result.CtdbConfidence > 0 ? $"match . conf {result.CtdbConfidence}" : $"{result.CtdbConfidence} / {result.CtdbTotal}";
            Accurate = result.Accurate;
            StatusText = encode ? $"Ripped {result.FileCount} files -> {result.OutputDir}" : result.Status;
            ApplyPerTrack(result);
            PublishReport(encode, result);
        }
        else
        {
            StatusText = (encode ? "Rip failed: " : "Verify failed: ") + result.Error;
        }
        LevelL = 0; LevelR = 0;   // needles fall back to rest when the job ends
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

    private void ApplyPerTrack(VerifyResult result)
    {
        for (int i = 0; i < Tracks.Count; i++)
        {
            int ar = i < result.ArPerTrack.Length ? result.ArPerTrack[i] : 0;
            int ct = i < result.CtdbPerTrack.Length ? result.CtdbPerTrack[i] : 0;
            Tracks[i].ArResult = ar > 0 ? $"conf {ar}" : "not found";
            Tracks[i].CtdbResult = ct > 0 ? $"conf {ct}" : "not found";
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
