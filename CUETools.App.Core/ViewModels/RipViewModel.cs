using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CUETools.Processor;
using CUETools.Ripper.SCSI;
using CUETools.Wpf.Models;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;
using CUETools.Wpf.Services.Artwork;

namespace CUETools.Wpf.ViewModels;

internal enum TestCopyCompletionState
{
    Verified,
    ConsistentRepairable,
    ConsistentDamaged
}

/// <summary>
/// A rip that has published its files, and whether the databases answered while it read.
/// A head that keeps an offline journal uses this to queue the published album when they did
/// not, so the verdict arrives later instead of never.
/// </summary>
/// <param name="OutputDirectory">The published album directory.</param>
/// <param name="ArLookupFailed">AccurateRip never answered during the read.</param>
/// <param name="CtdbLookupFailed">CTDB never answered during the read.</param>
public sealed record RipPublication(
    string OutputDirectory,
    bool ArLookupFailed,
    bool CtdbLookupFailed)
{
    /// <summary>True when neither database judged this rip, so it has no verdict at all.</summary>
    public bool NoDatabaseAnswered => ArLookupFailed && CtdbLookupFailed;
}

/// <summary>
/// Rip page: enumerate drives, read the inserted disc (TOC + metadata + releases), rip or verify
/// with live visuals (3D disc, VU, speed, re-read), embed the hi-res cover, and keep the
/// recently-ripped history for the no-disc screen.
/// </summary>
public sealed class RipViewModel : PageViewModel
{
    internal static TestCopyCompletionState ClassifyTestCopyCompletion(
        TestCopyRunResult result)
    {
        if (result.CtdbHasErrors
            && result.CtdbCanRecover
            && result.CtdbRepairSectors > 0)
        {
            return TestCopyCompletionState.ConsistentRepairable;
        }
        return result.FailedWindows > 0 || result.CtdbHasErrors
            ? TestCopyCompletionState.ConsistentDamaged
            : TestCopyCompletionState.Verified;
    }

    private readonly IDriveService _drives;
    private readonly IRipService _rip;
    private readonly IVerifyService _verify;
    private readonly IReportStore _reports;
    private readonly IHistoryStore _history;
    private readonly CUEConfig _config;
    private readonly IAlbumArtService _art;
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly AppLaunchOptions _launchOptions;
    private readonly IDiagnosticLog _log;
    private readonly IConvertService _codecs;
    private readonly AppStatusService _status;
    private readonly EncoderCatalog _catalog;
    // Platform seams (M2 pattern): UI timers, UI-thread marshaling, dialogs,
    // artwork decoding, and display capabilities come from the head.
    private readonly IUiTimerFactory _timers;
    private readonly IUiDispatcher _ui;
    private readonly IUserPrompt _prompt;
    private readonly IArtworkPreviewFactory _artFactory;
    private readonly IFileDialogService _dialogs;
    private AppActivity _baseActivity = AppActivity.Idle;   // what the icon returns to after a re-read clears

    // The last disc read, kept so a finished job can be turned into a full RipReport
    // (drive, offset, TOC) rather than just the AR/CTDB numbers.
    private DiscInfo? _lastDisc;

    public ObservableCollection<char> Drives { get; } = new();
    public ObservableCollection<char> ParallelDrives { get; } = new();
    public ObservableCollection<TrackItem> Tracks { get; } = new();
    public ObservableCollection<RecentRip> Recent { get; } = new();
    public ObservableCollection<ArtworkCandidate> ArtworkCandidates { get; } = new();

    // Real output formats (only those with a working encoder in this build), not a fixed list.
    public ObservableCollection<string> Formats { get; } = new();
    public ObservableCollection<CodecChoice> CodecChoices { get; } = new();

    private CodecChoice? _selectedCodecChoice;
    public CodecChoice? SelectedCodecChoice
    {
        get => _selectedCodecChoice;
        private set
        {
            if (ReferenceEquals(_selectedCodecChoice, value)) return;
            _selectedCodecChoice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedCodecLabel));
            OnPropertyChanged(nameof(SelectedCodecTooltip));
        }
    }
    public string SelectedCodecLabel =>
        SelectedCodecChoice?.CompactLabel ?? SelectedFormat.ToUpperInvariant();
    public string SelectedCodecTooltip => SelectedCodecChoice == null
        ? "Choose an output codec."
        : SelectedCodecChoice.AccessibleLabel + ". " +
          SelectedCodecChoice.HealthDetail;

    private string _selectedFormat = "flac";
    public string SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (Set(ref _selectedFormat, value))
            {
                _settings.SelectedFormat = value;
                RefreshSelectedCodecChoice();
                OnPropertyChanged(nameof(ScopeCodec));
                OnPropertyChanged(nameof(ScopeMode));
            }
        }
    }

    public string[] OutputLayouts { get; } =
        { "Tracks", "Image + embedded CUE" };
    public string SelectedOutputLayout
    {
        get => _settings.RipOutputLayout == RipOutputLayout.ImageWithEmbeddedCue
            ? "Image + embedded CUE"
            : "Tracks";
        set
        {
            _settings.RipOutputLayout =
                string.Equals(value, "Image + embedded CUE", StringComparison.Ordinal)
                    ? RipOutputLayout.ImageWithEmbeddedCue
                    : RipOutputLayout.Tracks;
            OnPropertyChanged();
        }
    }

    /// <summary>What the codec scope should draw for the selected format. A two-faced format is
    /// remapped so the visualization matches the ENCODER that will actually run: wma flipped to
    /// lossless draws WMA Lossless's predictor pipeline (not the MDCT mask), and m4a flipped to an
    /// imported AAC draws AAC's lossy pipeline (not ALAC's).</summary>
    public string ScopeCodec
    {
        get
        {
            string f = _selectedFormat;
            bool lossy = _codecs.IsLossy(f);
            if (lossy && Controls.LossyMath.Info(f) == null) return f + "-lossy";
            if (!lossy && Controls.LossyMath.Info(f) != null) return f + "-lossless";
            return f;
        }
    }

    /// <summary>The configured encoder mode for the selected format (e.g. "320", "V2", or a
    /// compression level), surfaced in the scope header so it reflects the ACTUAL setting rather
    /// than just the codec family. Read from the encoder that will actually run for this format;
    /// empty when none is resolved.</summary>
    public string ScopeMode
    {
        get
        {
            string f = _selectedFormat;
            if (_config.formats == null || !_config.formats.TryGetValue(f, out var fmt)) return "";
            var enc = _codecs.IsLossy(f) ? fmt.encoderLossy : fmt.encoderLossless;
            return enc?.Settings?.EncoderMode ?? "";
        }
    }

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
    public string OutputBaseDir { get => _outputBaseDir; set { if (Set(ref _outputBaseDir, value)) _settings.OutputBaseDir = value; } }

    private string _lastOutputDir = "";
    public string LastOutputDir { get => _lastOutputDir; private set => Set(ref _lastOutputDir, value); }

    // "rip complete" state shown after a successful rip
    private bool _ripDone;
    public bool RipDone { get => _ripDone; private set { if (Set(ref _ripDone, value)) OnPropertyChanged(nameof(ShowDiscArea)); } }

    private string _ripSummary = "";
    public string RipSummary { get => _ripSummary; private set => Set(ref _ripSummary, value); }

    private bool _canRepairLastRip;
    public bool CanRepairLastRip
    {
        get => _canRepairLastRip;
        private set
        {
            if (Set(ref _canRepairLastRip, value))
                RequeryHub.RequestRequery();
        }
    }

    private string _repairLastRipText = "";
    public string RepairLastRipText
    {
        get => _repairLastRipText;
        private set => Set(ref _repairLastRipText, value);
    }

    private string _lastRepairSource = "";

    private char _selectedDrive;
    public char SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (IsRipping)
            {
                // This page remains bound to the immutable drive snapshot owned by
                // the active job. Choosing another attached drive uses the same
                // selector to open an isolated worker instead of retargeting Stop,
                // status, metadata, or CRC evidence in this window.
                if (value != _selectedDrive && Drives.Contains(value))
                    OpenParallelDrive(value);
                OnPropertyChanged(nameof(SelectedDrive));
                return;
            }
            if (!Set(ref _selectedDrive, value)) return;
            // publish the choice so the Drive & Read page detects and CALIBRATES this same drive - it
            // used to act on GetDrives()[0], so calibrating there while ripping a different drive left
            // the rip with no calibration and silently skipped cache defeat
            _drives.SelectedDrive = value;
            RebuildParallelDrives();
            _ = ReadDiscAsync();
        }
    }

    private char _parallelDrive;
    public char ParallelDrive
    {
        get => _parallelDrive;
        set
        {
            if (Set(ref _parallelDrive, value))
                RequeryHub.RequestRequery();
        }
    }

    /// <summary>
    /// A running job keeps this window pinned to its immutable drive snapshot. Any other
    /// attached drive is launched in an explicitly isolated CUETools process with an OS-wide
    /// hardware lease, so Stop, CUESheet state, telemetry, and staging cannot cross jobs.
    /// </summary>
    public bool ShowParallelDriveLauncher =>
        IsRipping && ParallelDrives.Count > 0;

    private bool _isDiscPresent;
    public bool IsDiscPresent { get => _isDiscPresent; private set { if (Set(ref _isDiscPresent, value)) OnPropertyChanged(nameof(ShowDiscArea)); } }

    /// <summary>Whether to show the disc area. It stays up after the disc is gone while a rip
    /// completion panel is still showing, so ejecting after a rip does not take the result with it.</summary>
    public bool ShowDiscArea => IsDiscPresent || RipDone;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                OnPropertyChanged(nameof(DriveSelectorEnabled));
        }
    }

    /// <summary>
    /// A disc-identification or repair transaction cannot change drives. During
    /// an optical read the selector stays available, but another drive opens in
    /// its own process and this window remains pinned to the active job.
    /// </summary>
    public bool DriveSelectorEnabled => !IsBusy;

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
    private IUiTimer? _trayWatch;
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
    public bool IsRipping
    {
        get => _isRipping;
        private set
        {
            if (Set(ref _isRipping, value))
            {
                OnPropertyChanged(nameof(ControlsUnlocked));
                OnPropertyChanged(nameof(ShowParallelDriveLauncher));
                RequeryHub.RequestRequery();
            }
        }
    }

    /// <summary>
    /// False while a rip or verify runs. The accuracy mode is a job input and
    /// cannot change after its snapshot is taken.
    /// </summary>
    public bool ControlsUnlocked => !IsRipping;

    // The codec is immutable for every encoded job. Test & Copy freezes one health-checked codec
    // before its Test read, so the Copy and possible confirming read cannot drift to another
    // implementation after the evidence transaction has begun.
    // Cleared back to false wherever IsRipping is reset to false, on every path (success/error/stop).
    private bool _codecLocked;
    public bool CodecLocked { get => _codecLocked; private set { if (Set(ref _codecLocked, value)) OnPropertyChanged(nameof(CodecUnlocked)); } }
    /// <summary>Inverse of <see cref="CodecLocked"/>, for the format ComboBox's IsEnabled (mirrors the
    /// TrayState -&gt; IsTrayOpen / ArtPreview -&gt; HasArtPreview derived-property pattern used elsewhere
    /// in this view model, rather than a converter).</summary>
    public bool CodecUnlocked => !CodecLocked;

    // Weak-hardware fallback: with no hardware-accelerated 3D (software
    // rendering / RemoteFX on WPF) fall back to the 2D read-map. The head's
    // capability service answers; see IPlatformCapabilities.
    public bool Supports3DDisc { get; }

    // live read-speed readout for the SpeedGraph (0..1 of a 12x display cap) + "6.4x" text
    private double _speedLevel;
    public double SpeedLevel { get => _speedLevel; private set => Set(ref _speedLevel, value); }

    private string _speedText = "";
    public string SpeedText { get => _speedText; private set => Set(ref _speedText, value); }

    // real per-channel RMS levels for the VU meter (0..1), tapped from the disc audio
    private double _levelL;
    public double LevelL { get => _levelL; private set => Set(ref _levelL, value); }

    private double _levelR;
    public double LevelR { get => _levelR; private set => Set(ref _levelR, value); }

    // a window of real consecutive PCM samples, fed to the codec scope so it runs the real
    // predictor math on the actual audio being ripped
    private RipTelemetryDisplayFrame? _sampleWindow;
    public RipTelemetryDisplayFrame? SampleWindow { get => _sampleWindow; private set => Set(ref _sampleWindow, value); }
    private readonly RipTelemetryDisplayFrame _telemetryDisplayA = new();
    private readonly RipTelemetryDisplayFrame _telemetryDisplayB = new();
    private RipTelemetryMailbox? _telemetryMailbox;
    private IUiTimer? _telemetryTimer;
    private bool _useTelemetryDisplayB;

    // Re-read state, driven by the drive's real retry loop. RereadVisible governs the box (it lingers
    // ~1.4s after the last re-read so the "recovered" state and fade-out are seen); RereadActive
    // governs the live animation; the counts are the truth from ReadProgress. Only non-zero when the
    // drive is genuinely fighting a bad spot on the disc.
    private bool _rereadVisible;
    public bool RereadVisible { get => _rereadVisible; private set => Set(ref _rereadVisible, value); }
    private bool _rereadActive;
    public bool RereadActive { get => _rereadActive; private set => Set(ref _rereadActive, value); }
    private int _rereadCount;
    public int RereadCount { get => _rereadCount; private set => Set(ref _rereadCount, value); }
    private int _rereadMax = 30;
    public int RereadMax { get => _rereadMax; private set => Set(ref _rereadMax, value); }
    private int _rereadErrors;
    public int RereadErrors { get => _rereadErrors; private set => Set(ref _rereadErrors, value); }
    private string _rereadText = "";
    public string RereadText { get => _rereadText; private set => Set(ref _rereadText, value); }
    private double _rereadFrac;   // where on the disc (0..1) the stuck window is - drives the 3D zoom
    public double RereadFrac { get => _rereadFrac; private set => Set(ref _rereadFrac, value); }
    private bool _unreadable;     // the drive exhausted its retries and could not read the spot
    public bool Unreadable { get => _unreadable; private set => Set(ref _unreadable, value); }

    private DateTime _lastRereadUtc;
    private IUiTimer? _rereadTimer;
    private bool _holdDamageZoom;   // stop-on-unrecoverable fired: hold the red zoom until the job ends

    private const double SpeedCapX = 12.0;
    private double _discSeconds;
    private double _lastSpeedFrac;
    private DateTime _lastSpeedTick;
    private double[] _trackEndFrac = Array.Empty<double>();

    private string _ripStatus = "";
    public string RipStatus { get => _ripStatus; private set => Set(ref _ripStatus, value); }

    // Picker index: 0 = Burst, 1 = Secure, 2 = Paranoid, 3 = Salvage (R119: a defective-disc
    // capture - Burst-quality Test & Copy reads with C2 off at the drive's minimum speed).
    private int _correctionQuality = 1;
    public int CorrectionQuality
    {
        get => _correctionQuality;
        set
        {
            if (!Set(ref _correctionQuality, value)) return;
            _settings.CorrectionQuality = value;
            // Rip and Verify are disabled for a Salvage selection, so their enablement has to
            // be re-evaluated when the picker moves.
            OnPropertyChanged(nameof(SalvageRead));
            RequeryHub.RequestRequery();
        }
    }
    /// <summary>Engine quality for the picker selection: Salvage runs the engine at Burst.</summary>
    public int EffectiveCorrectionQuality => _correctionQuality == 3 ? 0 : _correctionQuality;
    /// <summary>True when the picker selects the Salvage capture mode (R119).</summary>
    public bool SalvageRead => _correctionQuality == 3;

    private string _arText = "not checked";
    public string ArText { get => _arText; private set => Set(ref _arText, value); }

    private string _ctdbText = "not checked";
    public string CtdbText { get => _ctdbText; private set => Set(ref _ctdbText, value); }

    private bool _accurate;
    public bool Accurate { get => _accurate; private set => Set(ref _accurate, value); }

    // verify-history verdict: a second, offline, AccurateRip-independent bit-exactness check
    // against this app's own earlier reads of the same disc (see VerifyHistoryStore).
    private string _historyText = "";
    public string HistoryText { get => _historyText; private set => Set(ref _historyText, value); }
    private bool _historyIsWarning;
    public bool HistoryIsWarning { get => _historyIsWarning; private set => Set(ref _historyIsWarning, value); }

    // Test & Copy: HELD state (two/three independent reads disagreed on some track) and the
    // held run's staging, kept until the user picks Re-run / Accept anyway / Discard.
    private bool _testCopyHeld;
    public bool TestCopyHeld { get => _testCopyHeld; private set => Set(ref _testCopyHeld, value); }
    private string _testCopyText = "";
    public string TestCopyText { get => _testCopyText; private set => Set(ref _testCopyText, value); }
    private bool _testCopyIsWarning;
    public bool TestCopyIsWarning { get => _testCopyIsWarning; private set => Set(ref _testCopyIsWarning, value); }
    private TestCopyRunResult? _heldResult;
    // A held result parked across a tray or disc-view clear (R108): a phantom tray event or an
    // accidental eject must not destroy the only completed Copy. It is restored when the same
    // disc returns, and discarded only when a different disc loads, a new job starts, or the
    // user discards explicitly.
    private TestCopyRunResult? _parkedHeldResult;
    private string _parkedHeldDiscId = "";
    private string _currentDiscId = "";

    // per-disc options, bound to the live config
    public bool CreateCue { get => _config.createCUEFileInTracksMode; set { _config.createCUEFileInTracksMode = value; OnPropertyChanged(); } }
    public bool WriteLog { get => _config.createEACLOG; set { _config.createEACLOG = value; OnPropertyChanged(); } }
    public bool EmbedArt
    {
        get => _config.embedAlbumArt;
        set
        {
            _config.embedAlbumArt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ArtEnabled));
            if (ArtEnabled) TriggerArtFetch(); else ClearArt();
        }
    }
    public bool ArtEnabled => _config.embedAlbumArt || _config.extractAlbumArt;

    // Cover preview and immutable candidate choice. _coverBytes is the resized JPEG captured by a
    // job. Discovery is release-generation-bound so stale responses cannot replace a newer disc.
    private object? _artPreview;
    public object? ArtPreview { get => _artPreview; private set { if (Set(ref _artPreview, value)) OnPropertyChanged(nameof(HasArtPreview)); } }
    public bool HasArtPreview => _artPreview != null;
    private string _artInfo = "";
    public string ArtInfo { get => _artInfo; private set => Set(ref _artInfo, value); }
    private bool _artLoading;
    public bool ArtLoading
    {
        get => _artLoading;
        private set
        {
            if (Set(ref _artLoading, value))
                RequeryHub.RequestRequery();
        }
    }
    private byte[]? _coverBytes;
    private byte[]? _artMaster;   // the hi-res master as fetched, kept so a size change re-derives without re-fetching
    private string _artLabel = "";
    private int _artMasterSide;      // the master's longest side - scaling never goes above it
    private System.Threading.CancellationTokenSource? _artCts;
    private long _artGeneration;
    private bool _localArtworkOverride;
    private ArtworkCandidate? _selectedArtwork;
    public ArtworkCandidate? SelectedArtwork
    {
        get => _selectedArtwork;
        private set => Set(ref _selectedArtwork, value);
    }

    public ICommand ReadDiscCommand { get; }
    public ICommand VerifyCommand { get; }
    public ICommand RipCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand EjectCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand DismissDoneCommand { get; }
    public ICommand RepairLastRipCommand { get; }
    public ICommand TestCopyCommand { get; }
    public ICommand AcceptCopyAnywayCommand { get; }
    public ICommand DiscardHeldCommand { get; }
    public ICommand OpenParallelDriveCommand { get; }

    public RipViewModel(
        IDriveService drives,
        IRipService rip,
        IVerifyService verify,
        IConvertService codecs,
        IReportStore reports,
        IHistoryStore history,
        CUEConfig config,
        IAlbumArtService art,
        AppSettings settings,
        EncoderCatalog catalog,
        AppStatusService status,
        SettingsStore settingsStore,
        AppLaunchOptions launchOptions,
        IDiagnosticLog log,
        IUiTimerFactory timers,
        IUiDispatcher uiDispatcher,
        IUserPrompt prompt,
        IArtworkPreviewFactory artPreviewFactory,
        IPlatformCapabilities capabilities,
        IFileDialogService fileDialogs)
    {
        _timers = timers;
        _dialogs = fileDialogs;
        _ui = uiDispatcher;
        _prompt = prompt;
        _artFactory = artPreviewFactory;
        Supports3DDisc = capabilities.HardwareAccelerated3D;
        Title = "Rip";
        Group = "Work";
        Subtitle = "Rip a CD: read, encode, and verify against AccurateRip and CTDB.";
        _drives = drives;
        _rip = rip;
        _verify = verify;
        _reports = reports;
        _history = history;
        _config = config;
        _art = art;
        _settings = settings;
        _settingsStore = settingsStore;
        _launchOptions = launchOptions;
        _log = log;
        _status = status;
        _catalog = catalog;
        _codecs = codecs;
        _drives.SelectedDriveChanged += OnSharedSelectedDriveChanged;
        // restore the persisted rip prefs; empty means never set - fall back to defaults
        _outputBaseDir = !string.IsNullOrWhiteSpace(settings.OutputBaseDir) && System.IO.Directory.Exists(settings.OutputBaseDir)
            ? settings.OutputBaseDir
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CUETools");
        _correctionQuality = Math.Max(0, Math.Min(3, settings.CorrectionQuality));

        void RebuildFormats()
        {
            Formats.Clear();
            foreach (var f in codecs.LosslessFormats()) Formats.Add(f);
            foreach (var f in codecs.LossyFormats()) Formats.Add(f);   // lossy last, e.g. mp3, wma
            CodecChoices.Clear();
            foreach (CodecChoice choice in catalog.BuildChoices(config))
                CodecChoices.Add(choice);
        }
        RebuildFormats();
        // an imported external encoder (e.g. mppenc.exe for Musepack) lights its format up live
        catalog.Changed += (_, _) =>
        {
            var keep = SelectedFormat; RebuildFormats();
            SelectedFormat = Formats.Contains(keep) ? keep : (Formats.Count > 0 ? Formats[0] : "flac");
            RefreshSelectedCodecChoice();
            OnPropertyChanged(nameof(ScopeCodec)); OnPropertyChanged(nameof(ScopeMode));   // a type flip changes what the scope draws + its mode
        };
        // a cover-size change in Settings re-derives the already-fetched cover at the new size
        settings.ArtSizeChanged += (_, _) => RefreshArtSize();
        // the Settings page can toggle embed/extract too; without this its toggle changed the config
        // but never armed (or cleared) the cover fetch the Rip page owns
        settings.ArtEnabledChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(EmbedArt));
            OnPropertyChanged(nameof(ArtEnabled));
            if (ArtEnabled) TriggerArtFetch(); else ClearArt();
        };
        settings.ArtProviderChanged += (_, _) =>
        {
            if (ArtEnabled && !_localArtworkOverride)
                TriggerArtFetch();
        };
        if (Formats.Contains(settings.SelectedFormat)) _selectedFormat = settings.SelectedFormat;
        if (!Formats.Contains(_selectedFormat)) _selectedFormat = Formats.Count > 0 ? Formats[0] : "flac";
        RefreshSelectedCodecChoice();

        // canExecute, not just the early-return guard inside ReadDiscOrClose: without it the button
        // stays fully enabled during a rip and does nothing when pressed.
        ReadDiscCommand = new RelayCommand(_ => ReadDiscOrClose(), _ => !IsRipping && !IsBusy);
        // Salvage is a Test & Copy mode: only RunTestAndCopy takes the salvage flag, so Rip and
        // Verify with Salvage selected used to run a plain Burst job with no minimum-speed pin,
        // no concealment, and no salvaged grade on the output - the user believing they had
        // made a salvage capture. They are disabled for that selection instead.
        VerifyCommand = new RelayCommand(
            _ => { _ = RunJobAsync(encode: false); },
            _ => IsDiscPresent && !IsRipping && !IsBusy && !SalvageRead);
        // A cover is immutable job input. Do not freeze a null snapshot while release-bound
        // discovery is still resolving; Verify remains available because it publishes no audio.
        RipCommand = new RelayCommand(
            _ => { _ = RunJobAsync(encode: true); },
            _ => CanStartEncodedJob(IsDiscPresent, IsRipping, IsBusy, ArtLoading) && !SalvageRead);
        StopCommand = new RelayCommand(_ => Stop(), _ => IsRipping);
        EjectCommand = new RelayCommand(_ => ToggleTray(), _ => Drives.Count > 0 && !IsRipping && !IsBusy);
        BrowseOutputCommand = new RelayCommand(async _ => await BrowseOutputAsync());
        OpenFolderCommand = new RelayCommand(_ => OpenFolder(), _ => LastOutputDir.Length > 0);
        DismissDoneCommand = new RelayCommand(_ =>
        {
            RipDone = false;
            CanRepairLastRip = false;
            _lastRepairSource = "";
            _status.Report(AppActivity.Idle);
        });
        RepairLastRipCommand = new RelayCommand(
            _ => { _ = RepairLastRipAsync(); },
            _ => CanRepairLastRip && !IsBusy && !IsRipping);
        TestCopyCommand = new RelayCommand(
            _ => { _ = RunTestCopyAsync(); },
            _ => CanStartEncodedJob(IsDiscPresent, IsRipping, IsBusy, ArtLoading));
        AcceptCopyAnywayCommand = new RelayCommand(_ => AcceptCopyAnyway(), _ => _heldResult != null);
        DiscardHeldCommand = new RelayCommand(_ => DiscardHeld(), _ => _heldResult != null);
        OpenParallelDriveCommand = new RelayCommand(
            _ => OpenParallelDrive(ParallelDrive),
            _ => IsRipping && ParallelDrive != '\0' &&
                 ParallelDrives.Contains(ParallelDrive));

        foreach (var d in drives.GetDrives()) Drives.Add(d);
        LoadRecent();

        if (Drives.Count > 0)
        {
            char shared = _drives.SelectedDrive;
            _selectedDrive = Drives.Contains(shared)
                ? shared
                : Drives[0];   // set the field to avoid a double read from the setter
            _drives.SelectedDrive = _selectedDrive;   // ...but still publish it for the Drive & Read page
            RebuildParallelDrives();
            OnPropertyChanged(nameof(SelectedDrive));
            _ = ReadDiscAsync();
            StartTrayWatch();
        }
        else
        {
            StatusText = "No optical drive found.";
        }
    }

    private void OnSharedSelectedDriveChanged(object? sender, EventArgs e)
    {
        char selected = _drives.SelectedDrive;
        if (selected == '\0' || selected == _selectedDrive || !Drives.Contains(selected))
            return;
        // Drive & Read exposes the same session selection. Its selector is locked while a job owns
        // a drive, so an external change here is always an idle-time request to read the new disc.
        if (IsRipping || IsBusy)
            return;
        _selectedDrive = selected;
        RebuildParallelDrives();
        OnPropertyChanged(nameof(SelectedDrive));
        _ = ReadDiscAsync();
    }

    private void RebuildParallelDrives()
    {
        char previous = _parallelDrive;
        ParallelDrives.Clear();
        foreach (char drive in Drives)
            if (drive != _selectedDrive)
                ParallelDrives.Add(drive);
        _parallelDrive = ParallelDrives.Contains(previous)
            ? previous
            : ParallelDrives.Count > 0 ? ParallelDrives[0] : '\0';
        OnPropertyChanged(nameof(ParallelDrive));
        OnPropertyChanged(nameof(ShowParallelDriveLauncher));
        RequeryHub.RequestRequery();
    }

    // Poll the drive for tray/media changes so the UI reacts to the physical eject button and to a
    // disc being dropped in and the tray pushed shut - the classic "insert disc, it just reads"
    // behaviour. GET EVENT STATUS NOTIFICATION does not spin the disc; we skip polling mid-read so
    // we never fight the ripper for the device.
    private void StartTrayWatch()
    {
        _trayWatch = _timers.Create(TimeSpan.FromMilliseconds(2000));
        _trayWatch.Tick += async (_, _) => await PollTrayAsync();
        _trayWatch.Start();
    }

    private DriveTrayState _pendingTray = DriveTrayState.Unknown;   // debounce: last raw reading

    private async Task PollTrayAsync()
    {
        if (_selectedDrive == '\0' || _isBusy || IsRipping) return;
        char drive = _selectedDrive;
        DriveTrayState tray = await Task.Run(() => _drives.GetTrayState(drive));
        if (tray == DriveTrayState.Unknown) return;
        // Debounce: the GESN media-status poll can flap for one reading around drive activity
        // (measured live: a 15 s phantom "tray open" and a spurious disc re-read with no tray event).
        // Accept a CHANGE only when two consecutive polls agree; matching reads pass straight through.
        if (tray != TrayState && tray != _pendingTray)
        {
            _pendingTray = tray;
            return;
        }
        _pendingTray = tray;
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
        // Never re-read the disc while a rip runs: it clears Tracks/Releases/_chosenMetadata, so
        // per-track progress and AccurateRip results would land in a DIFFERENT album's rows and the
        // report be filed against them - and it issues INQUIRY/TOC/CD-Text traffic on the very
        // device the ripper holds open.
        if (_selectedDrive == '\0' || _isBusy || IsRipping) return;
        IsBusy = true;
        _status.Report(AppActivity.ReadingDisc);
        // the finally guarantees the page can never be left stuck "busy" (Rip/Verify/Eject disabled,
        // tray watcher parked) if a drive query throws mid-read - e.g. the drive letter vanishing
        try
        {
        _triedCurrentMedia = true;           // this read counts as our attempt at the current media
        char drive = _selectedDrive;
        // identity + tray state first (works with an empty tray) so the bar shows the drive model
        StatusText = "Reading drive...";
        DriveDetails details = await Task.Run(() => _drives.GetDriveDetails(drive));
        if (details.Valid) DriveModel = details.Model;
        TrayState = details.Tray;

        StatusText = "Reading disc...";
        // surface the live metadata-lookup step (which database is being queried)
        void OnStatus(string s) => _ui.Post(() => { if (_isBusy) StatusText = s; });

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
            bool ownedElsewhere = !details.Valid &&
                details.Error.Contains(
                    "Another CUETools job",
                    StringComparison.OrdinalIgnoreCase);
            // the drive tells us the media type: distinguish "empty" from "a disc, but not audio"
            bool nonAudio = !open && details.Valid && details.MediaPresent && details.CurrentProfile.Length > 0;
            AlbumTitle = ownedElsewhere
                ? $"Drive {drive}: is already ripping in another CUETools window"
                : open ? "Tray open - insert a disc, then Close"
                : nonAudio ? $"Not an audio CD - {details.CurrentProfile} in drive {drive}:"
                : "No disc in drive " + drive + ":";
            AlbumArtist = "";
            DiscInfoText = "";
            ClearArt();
            StatusText = ownedElsewhere
                ? "This physical drive is locked to its existing job. Choose another drive or use that window."
                : open ? "Tray open."
                : nonAudio ? $"{details.CurrentProfile} loaded - insert an audio CD to rip."
                : "Drive ready - insert an audio CD to begin.";
        }
        else
        {
            IsDiscPresent = true;
            _currentDiscId = info.DiscId;
            ResolveParkedHeld(info.DiscId);
            AlbumTitle = info.Album;
            AlbumArtist = string.IsNullOrWhiteSpace(info.Artist)
                ? (string.IsNullOrWhiteSpace(info.DriveName) ? $"Drive {drive}:" : info.DriveName)
                : (string.IsNullOrWhiteSpace(info.Year) ? info.Artist : $"{info.Artist}  ({info.Year})");
            DiscInfoText = $"{info.AudioTracks} tracks   {Fmt(info.TotalLength)}   {info.Releases.Count} release match(es)";
            foreach (var t in info.Tracks) Tracks.Add(t);
            ApplyCrcEvidence(_rip.GetLatestCrcEvidence(info.DiscId));
            foreach (var rm in info.Releases) Releases.Add(rm);
            _selectedRelease = System.Linq.Enumerable.FirstOrDefault(Releases, r => r.IsBest) ?? (Releases.Count > 0 ? Releases[0] : null);
            _chosenMetadata = _selectedRelease?.Metadata;
            StatusText = info.Releases.Count > 0
                ? $"Identified: {info.Artist} - {info.Album}. Ripping comes next."
                : "Disc read; not found in the metadata databases (generic track names).";
            // Inserting a disc is not a request to download a cover. Off by default, so the
            // app no longer reaches a cover-art host just because it started with a disc in
            // the drive; every explicit path below still fetches.
            if (_settings.AutoFetchArtOnDiscRead) TriggerArtFetch();
        }
        OnPropertyChanged(nameof(SelectedRelease));
        OnPropertyChanged(nameof(HasReleases));
        }
        finally
        {
            IsBusy = false;
            if (!IsRipping) _status.Report(AppActivity.Idle);
        }
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
        TriggerArtFetch();
    }

    // Discover release-bound candidates, cancelling any in-flight lookup for an older selection.
    private void TriggerArtFetch()
    {
        _localArtworkOverride = false;
        string album = _chosenMetadata?.Title ?? "";
        string artist = _chosenMetadata?.Artist ?? "";
        string barcode = _chosenMetadata?.Barcode ?? "";
        // extract-only is a real choice: it writes folder.jpg without embedding, so it still needs
        // the cover fetched. Gating on embed alone meant extract silently produced nothing on a rip.
        if (!_config.embedAlbumArt && !_config.extractAlbumArt) { ClearArt(); return; }
        if (string.IsNullOrWhiteSpace(album) && string.IsNullOrWhiteSpace(artist)) { ClearArt(); return; }
        long generation = ++_artGeneration;
        var release = _selectedRelease;
        var query = new ArtworkQuery(
            artist,
            album,
            _chosenMetadata?.Year ?? "",
            barcode,
            Tracks.Count,
            _lastDisc?.MusicBrainzDiscId ?? "",
            _lastDisc?.MusicBrainzToc ?? "",
            release?.ProviderKey ?? "",
            release?.ProviderId ?? "",
            release?.InfoUrl ?? "",
            generation,
            _chosenMetadata?.AlbumArt,
            _config.advanced.coversSearch);
        _artFetch = FetchArtAsync(query);
        _ = _artFetch;
    }

    /// <summary>
    /// Starting a rip is an explicit request, so it may fetch a cover even when the disc read
    /// did not (see AppSettings.AutoFetchArtOnDiscRead). Waits for a fetch already in flight,
    /// or starts one, so the encode embeds the same cover the page shows. Bounded: a slow or
    /// dead artwork host delays the rip by at most this wait, and never fails it.
    /// </summary>
    private async Task EnsureArtBeforeRipAsync()
    {
        if (!ArtEnabled || _localArtworkOverride) return;
        if (_coverBytes == null && _artFetch == null) TriggerArtFetch();
        Task? fetch = _artFetch;
        if (fetch == null) return;
        try
        {
            await Task.WhenAny(fetch, Task.Delay(TimeSpan.FromSeconds(20)));
        }
        catch (Exception ex)
        {
            _log.Warn("rip", "cover fetch before rip failed: " + ex.GetType().Name);
        }
    }

    private Task? _artFetch;

    private void ClearArt()
    {
        _artCts?.Cancel();
        ++_artGeneration;
        _coverBytes = null;
        _artMaster = null;
        _artLabel = "";
        _localArtworkOverride = false;
        SelectedArtwork = null;
        ArtworkCandidates.Clear();
        ArtPreview = null;
        ArtInfo = "";
        ArtLoading = false;
    }

    // The cover max-size changed in Settings: re-derive the embed JPEG from the cached master at
    // the new size - no re-fetch, and the info line updates so the change is visible immediately.
    private async void RefreshArtSize()
    {
        var master = _artMaster;
        if (master == null) return;
        int max = Math.Max(200, _config.maxAlbumArtSize);
        var jpeg = await Task.Run(() => _art.ResizeToJpeg(master, max));
        if (_artMaster != master) return;   // a newer fetch replaced the master meanwhile
        _coverBytes = jpeg;
        if (jpeg != null)
        {
            int outSide = Math.Min(max, _artMasterSide);   // never scaled above the master
            ArtInfo = $"{_artLabel} -> re-scaled {outSide}px, {jpeg.Length / 1024}KB JPEG";
        }
    }

    private async Task FetchArtAsync(ArtworkQuery query)
    {
        _artCts?.Cancel();
        var cts = _artCts = new System.Threading.CancellationTokenSource();
        var ct = cts.Token;
        ArtLoading = true;
        ArtPreview = null;
        _coverBytes = null;
        SelectedArtwork = null;
        ArtworkCandidates.Clear();
        ArtInfo = query.SearchMode == CUEConfigAdvanced.CTDBCoversSearch.Extensive
            ? "searching all release-matched artwork sources..."
            : query.SearchMode == CUEConfigAdvanced.CTDBCoversSearch.Primary
                ? "searching for release-matched front artwork..."
                : "automatic artwork search is disabled";
        try
        {
            IReadOnlyList<ArtworkCandidate> candidates =
                await _art.FindCandidatesAsync(query, ct);
            if (ct.IsCancellationRequested || query.Generation != _artGeneration) return;
            _log.Info(
                "art",
                $"discovery complete candidates={candidates.Count} providers={candidates.Select(candidate => candidate.Provider).Distinct(StringComparer.Ordinal).Count()}");
            foreach (ArtworkCandidate candidate in candidates)
                ArtworkCandidates.Add(candidate);
            if (candidates.Count == 0)
            {
                ArtInfo = query.SearchMode == CUEConfigAdvanced.CTDBCoversSearch.None
                    ? "automatic artwork search is disabled; drop an image here to use your own"
                    : "no release-matched cover found";
                return;
            }
            ArtworkCandidate[] automatic = candidates.Where(
                    candidate => candidate.AutomaticEligible && candidate.IsFront)
                .ToArray();
            if (automatic.Length == 0)
            {
                ArtInfo = "artwork found for browsing; no front cover is eligible automatically";
                return;
            }
            foreach (ArtworkCandidate candidate in automatic)
                if (await SelectArtworkAsync(candidate, query.Generation, ct))
                    break;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Warn("art", "cover discovery failed: " + ex.GetType().Name);
            ArtInfo = "cover lookup unavailable - rip can continue without downloaded art";
        }
        finally
        {
            if (!ct.IsCancellationRequested && query.Generation == _artGeneration)
                ArtLoading = false;
        }
    }

    public async Task SelectArtworkAsync(ArtworkCandidate candidate)
    {
        _artCts?.Cancel();
        var cts = _artCts = new System.Threading.CancellationTokenSource();
        await SelectArtworkAsync(candidate, _artGeneration, cts.Token);
    }

    public void ChooseNoArtwork()
    {
        _artCts?.Cancel();
        SelectedArtwork = null;
        _artMaster = null;
        _artLabel = "";
        _artMasterSide = 0;
        _localArtworkOverride = false;
        _coverBytes = null;
        ArtPreview = null;
        ArtInfo = "no downloaded cover selected";
        ArtLoading = false;
    }

    public void RefreshArtwork() => TriggerArtFetch();

    public async Task ImportLocalArtworkAsync(string path)
    {
        _artCts?.Cancel();
        long generation = ++_artGeneration;
        var cts = _artCts = new System.Threading.CancellationTokenSource();
        ArtLoading = true;
        ArtInfo = "validating local cover...";
        try
        {
            int max = Math.Max(200, _config.maxAlbumArtSize);
            AlbumArt art = await Task.Run(
                () => _art.ImportLocalFile(path, max),
                cts.Token);
            byte[]? jpeg = art.PreparedJpeg ?? await Task.Run(
                () => _art.ResizeToJpeg(art.Bytes, max),
                cts.Token);
            if (cts.IsCancellationRequested || generation != _artGeneration)
                return;
            if (jpeg == null)
                throw new InvalidDataException("The local image could not be converted.");

            _localArtworkOverride = true;
            SelectedArtwork = art.Candidate;
            _artMaster = art.Bytes;
            _artMasterSide = Math.Max(art.Width, art.Height);
            _artLabel = $"Local file {art.Width}x{art.Height} (user override)";
            _coverBytes = jpeg;
            ArtPreview = MakeThumb(jpeg);
            int outputSide = Math.Min(max, _artMasterSide);
            ArtInfo = $"{_artLabel} -> {outputSide}px, {jpeg.Length / 1024}KB JPEG";
            _log.Info(
                "art",
                $"local override accepted width={art.Width} height={art.Height} bytes={art.Bytes.LongLength}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Warn("art", "local cover rejected: " + ex.GetType().Name);
            ArtInfo = ex is InvalidDataException
                ? ex.Message
                : "The local cover could not be read.";
        }
        finally
        {
            if (!cts.IsCancellationRequested && generation == _artGeneration)
                ArtLoading = false;
        }
    }

    private async Task<bool> SelectArtworkAsync(
        ArtworkCandidate candidate,
        long generation,
        System.Threading.CancellationToken ct)
    {
        ArtLoading = true;
        ArtInfo = $"loading {candidate.Provider} cover...";
        try
        {
            _localArtworkOverride = false;
            AlbumArt? art = await _art.DownloadAsync(candidate, thumbnail: false, ct);
            if (art == null || ct.IsCancellationRequested || generation != _artGeneration)
                return false;
            int max = Math.Max(200, _config.maxAlbumArtSize);
            byte[]? jpeg = await Task.Run(
                () => _art.ResizeToJpeg(art.Bytes, max),
                ct);
            if (ct.IsCancellationRequested || generation != _artGeneration)
                return false;
            if (jpeg == null)
            {
                ArtInfo = "selected cover could not be converted - choose another image";
                return false;
            }

            ArtworkCandidate resolved = candidate with
            {
                Width = art.Width,
                Height = art.Height,
                ByteLength = art.Bytes.LongLength,
                MimeType = art.Bytes.Length >= 8 &&
                           art.Bytes[0] == 0x89 && art.Bytes[1] == 0x50
                    ? "image/png"
                    : "image/jpeg"
            };
            int candidateIndex = ArtworkCandidates.IndexOf(candidate);
            if (candidateIndex >= 0) ArtworkCandidates[candidateIndex] = resolved;
            SelectedArtwork = resolved;
            _artMaster = art.Bytes;
            _artMasterSide = Math.Max(art.Width, art.Height);
            _artLabel = $"{resolved.Provider} {art.Width}x{art.Height} ({resolved.MatchText})";
            _coverBytes = jpeg;
            ArtPreview = MakeThumb(art.Bytes);
            int outputSide = Math.Min(max, _artMasterSide);
            ArtInfo = $"{_artLabel} -> {outputSide}px, {jpeg.Length / 1024}KB JPEG";
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _log.Warn("art", "selected cover unavailable: " + ex.GetType().Name);
            ArtInfo = "selected cover unavailable - choose another image";
            return false;
        }
        finally
        {
            if (!ct.IsCancellationRequested && generation == _artGeneration)
                ArtLoading = false;
        }
    }

    private object? MakeArtworkImage(byte[] bytes, int decodeWidth) =>
        _artFactory.CreatePreview(bytes, decodeWidth);

    private object? MakeThumb(byte[] bytes) =>
        MakeArtworkImage(bytes, 240);

    // Ask the engine to stop the running rip/verify at the next safe point. The job's Task then
    // returns a "Stopped." result and the UI settles back to the disc view.
    private void Stop()
    {
        if (!IsRipping) return;
        StatusText = "Stopping...";
        Task.Run(() => { try { _rip.Stop(); } catch { } });
    }

    private RipTelemetryMailbox StartTelemetry()
    {
        var mailbox = new RipTelemetryMailbox();
        _telemetryMailbox = mailbox;
        _useTelemetryDisplayB = false;
        SampleWindow = null;
        LevelL = 0;
        LevelR = 0;

        _telemetryTimer ??= _timers.Create(
            TimeSpan.FromMilliseconds(16), renderPriority: true);
        _telemetryTimer.Tick -= TelemetryTick;
        _telemetryTimer.Tick += TelemetryTick;
        _telemetryTimer.Start();
        return mailbox;
    }

    private void StopTelemetry(RipTelemetryMailbox mailbox)
    {
        if (!ReferenceEquals(_telemetryMailbox, mailbox))
            return;
        _telemetryTimer?.Stop();
        _telemetryMailbox = null;
        SampleWindow = null;
        LevelL = 0;
        LevelR = 0;
    }

    private void TelemetryTick(object? sender, EventArgs e)
    {
        RipTelemetryMailbox? mailbox = _telemetryMailbox;
        if (mailbox == null)
            return;

        RipTelemetryDisplayFrame display =
            _useTelemetryDisplayB
                ? _telemetryDisplayB
                : _telemetryDisplayA;
        bool consumed = false;
        double levelL = 0;
        double levelR = 0;
        // Drain at most one full ring per UI tick. That discards stale presentation age while
        // keeping the UI callback bounded even if the producer publishes concurrently.
        for (int remaining = mailbox.SlotCount;
             remaining > 0 &&
             mailbox.TryAcquire(out RipTelemetryFrame frame);
             remaining--)
        {
            try
            {
                display.CopyFrom(frame);
                levelL = frame.LevelL;
                levelR = frame.LevelR;
                consumed = true;
            }
            finally
            {
                mailbox.Release(frame);
            }
        }
        if (!consumed)
            return;

        _useTelemetryDisplayB = !_useTelemetryDisplayB;
        LevelL = levelL;
        LevelR = levelR;
        SampleWindow = display;
    }

    // The re-read box lingers briefly after the last re-read so the "recovered"/fade-out is seen and
    // the box does not flicker between adjacent bad spots. This UI-thread timer does the hiding.
    private void StartRereadTimer()
    {
        RereadVisible = false; RereadActive = false; RereadCount = 0; RereadErrors = 0; RereadText = "";
        Unreadable = false; _holdDamageZoom = false;
        _rereadTimer ??= _timers.Create(TimeSpan.FromMilliseconds(200));
        _rereadTimer.Tick -= RereadTick;
        _rereadTimer.Tick += RereadTick;
        _rereadTimer.Start();
    }

    private void StopRereadTimer()
    {
        _rereadTimer?.Stop();
        RereadVisible = false; RereadActive = false; Unreadable = false; _holdDamageZoom = false;
    }

    private void RereadTick(object? sender, EventArgs e)
    {
        // When stop-on-unrecoverable fired, hold the zoomed red state until the job ends; otherwise let
        // the box/zoom linger briefly and then clear so the read can carry on.
        if (_holdDamageZoom) return;
        if (RereadVisible && (DateTime.UtcNow - _lastRereadUtc).TotalSeconds > 1.4)
        {
            RereadVisible = false;
            RereadActive = false;
            Unreadable = false;
        }
    }

    // A real re-read: ReReads = extra passes over a stuck window, ErrorSectors = sectors still
    // disagreeing. ReReads == 0 means the window cleared; the linger timer then hides the box.
    // The Unreadable badge and StopOnUnrecoverable key on the ENGINE's give-up verdict
    // (WindowGivenUpSectors > 0), surfaced once per window after the retry policy classified its
    // sectors failed - never on running mid-pass counts, which a later chunk of the same pass can
    // still converge, and never on a pass threshold, which would cut the deep-recovery extension
    // short (R110).
    private Action<RereadReport> MakeRereadHandler()
        => r => _ui.Post(() =>
        {
            if (r.ReReads > 0)
            {
                _lastRereadUtc = DateTime.UtcNow;
                RereadVisible = true;
                RereadCount = r.ReReads;
                RereadMax = Math.Max(1, r.MaxReReads);
                RereadErrors = r.ErrorSectors;
                RereadFrac = r.WindowFrac;
                RereadText = $"{(int)(r.WindowFrac * 100)}% in";

                bool failed = r.WindowGivenUpSectors > 0;
                RereadActive = !failed;
                Unreadable = failed;
                _status.Report(failed ? AppActivity.Unreadable : AppActivity.Rereading);   // taskbar badge
                if (failed && _settings.StopOnUnrecoverable && IsRipping)
                {
                    _holdDamageZoom = true;   // keep the disc zoomed on the failed spot until the job ends
                    StatusText = $"Unrecoverable damage at {(int)(r.WindowFrac * 100)}% - stopping.";
                    Task.Run(() => { try { _rip.Stop(); } catch { } });
                }
            }
            else
            {
                RereadActive = false;   // cleared: stop animating, let the box linger then hide
                RereadErrors = 0;
                if (!Unreadable) _status.Report(_baseActivity);   // badge drops with the recovery
            }
        });

    private async Task RunJobAsync(bool encode)
    {
        if (!IsDiscPresent || IsRipping || IsBusy || (encode && ArtLoading)) return;
        if (encode && !ValidateSelectedCodec("rip")) return;
        PersistPrimarySettingsSnapshot();
        DiscardParkedHeld("a new job started");   // the user moved on from the parked disc
        char drive = _selectedDrive;
        int cq = EffectiveCorrectionQuality;   // a Salvage selection runs a plain job at Burst
        CodecLocked = encode;
        IsRipping = true;
        RipDone = false;
        CanRepairLastRip = false;
        RepairLastRipText = "";
        _lastRepairSource = "";
        RipProgress = 0;
        HistoryText = "";
        // R120 hygiene: live per-track and database evidence is about to be written as this job
        // reads, so clear the previous job's values first. Without this a new disc would show
        // the last disc's verdict until its own reads reported.
        ArText = "not checked"; CtdbText = "not checked";
        foreach (var resetTrack in Tracks) { resetTrack.ArResult = "-"; resetTrack.CtdbResult = "-"; }
        HistoryIsWarning = false;
        _baseActivity = encode ? AppActivity.Ripping : AppActivity.Verifying;
        _status.Report(_baseActivity, 0);
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

        // ReadProgress and re-read events fire on the ripper's thread; marshal those to the UI.
        // RMS and scope samples use the bounded mailbox drained by the UI timer.
        void Report(double frac, string status)
            => _ui.Post(() =>
            {
                RipProgress = frac; StatusText = status; UpdateSpeed(frac); UpdateTrackProgress(frac);
                // feed the taskbar progress; keep a re-read/unreadable state's badge while it lasts
                var act = _status.Activity is AppActivity.Rereading or AppActivity.Unreadable ? _status.Activity : _baseActivity;
                _status.Report(act, frac);
            });
        var Reread = MakeRereadHandler();

        StartRereadTimer();

        string fmt = SelectedFormat;
        var meta = _chosenMetadata;
        string outBase = OutputBaseDir;
        RipOutputLayout outputLayout = _settings.RipOutputLayout;
        await EnsureArtBeforeRipAsync();
        byte[]? cover = _coverBytes == null
            ? null
            : (byte[])_coverBytes.Clone();
        void LockCodec() => _ui.Post(() => CodecLocked = true);
        RipTelemetryMailbox telemetry = StartTelemetry();
        VerifyResult result;
        try
        {
            result = await Task.Run(() => encode
                ? _rip.RunEncode(
                    drive, cq, fmt, meta, outBase, Report,
                    telemetry, Reread, cover,
                    onEncodeStart: LockCodec,
                    outputLayout: outputLayout)
                : _rip.RunVerify(
                    drive, cq, meta, Report, telemetry, Reread));
        }
        finally
        {
            StopTelemetry(telemetry);
        }

        RipProgress = result.Ok ? 1 : RipProgress;
        if (result.Ok)
        {
            // The final line must not contradict the per-read line: a lookup that never
            // completed reads as such rather than collapsing to 0 / 0.
            ArText = result.ArLookupFailed && result.ArTotal == 0
                ? "lookup failed"
                : $"{result.ArConfidence} / {result.ArTotal}" + (result.Accurate ? "  accurate" : "");
            CtdbText = result.CtdbConfidence > 0
                ? $"match . conf {result.CtdbConfidence}"
                : result.CtdbCanRecover
                    ? $"recoverable damage . {result.CtdbRepairSectors} sector(s)"
                    : result.CtdbLookupFailed && result.CtdbTotal == 0
                        ? "lookup failed"
                        : $"{result.CtdbConfidence} / {result.CtdbTotal}";
            Accurate = result.Accurate;
            ApplyHistoryStatus(result.HistoryRecorded, result.HistoryKnown,
                result.HistoryMatches, result.HistoryPriorReads, result.HistoryDiffTracks);
            StatusText = encode ? $"Ripped {result.FileCount} files -> {result.OutputDir}" : result.Status;
            if (encode)
            {
                SetPostRipRepair(result);
                string outputAssurance = !result.OutputVerificationKnown
                    ? ""
                    : result.LosslessOutput
                        ? result.OutputVerificationPerformed
                            ? "  .  final output PCM verified after metadata"
                            : "  .  WARNING: final output not verified"
                        : "  .  lossy output";
                RipSummary = $"Ripped {result.FileCount} {fmt} files"
                    + (result.Accurate ? $"  .  AccurateRip verified (confidence {result.ArConfidence})" : "  .  not found in AccurateRip")
                    + (result.CtdbConfidence > 0 ? $"  .  CTDB confidence {result.CtdbConfidence}" : "")
                    + (CanRepairLastRip
                        ? $"  .  CTDB can repair {result.CtdbRepairSectors} damaged sector(s)"
                        : "")
                    + outputAssurance;
                if (result.LosslessOutput &&
                    !result.OutputVerificationPerformed)
                    StatusText += "  WARNING: final output verification was not performed.";
                RaisePublished(result.OutputDir, result.ArLookupFailed, result.CtdbLookupFailed);
                await FinishRipAsync(result.OutputDir, drive);   // shared tail: RipDone + eject
            }
            ApplyPerTrack(result);
            PublishReport(encode, result, cq);   // cq = the snapshot taken when this job started
        }
        else
        {
            StatusText = result.Error == "Stopped."
                ? (encode ? "Rip stopped." : "Verify stopped.")
                : (encode ? "Rip failed: " : "Verify failed: ") + result.Error;
        }
        LevelL = 0; LevelR = 0;   // needles fall back to rest when the job ends
        StopRereadTimer();
        foreach (var t in Tracks) { t.Active = false; if (result.Ok) t.Progress = 1; }
        IsRipping = false;
        CodecLocked = false;
        _baseActivity = AppActivity.Idle;
        _status.Report(result.Ok && encode ? AppActivity.Done : AppActivity.Idle);   // green badge until dismissed
    }

    public void SelectCodec(CodecChoice choice)
    {
        if (choice == null)
            throw new ArgumentNullException(nameof(choice));
        _catalog.SelectCodec(_config, choice);
        _selectedFormat = choice.Extension;
        _settings.SelectedFormat = choice.Extension;
        OnPropertyChanged(nameof(SelectedFormat));
        RefreshSelectedCodecChoice(choice.StableId);
        OnPropertyChanged(nameof(ScopeCodec));
        OnPropertyChanged(nameof(ScopeMode));
    }

    private void RefreshSelectedCodecChoice(string? preferredStableId = null)
    {
        SelectedCodecChoice = CodecChoices.FirstOrDefault(choice =>
                preferredStableId != null &&
                string.Equals(
                    choice.StableId,
                    preferredStableId,
                    StringComparison.OrdinalIgnoreCase))
            ?? _catalog.GetSelectedChoice(_config, _selectedFormat)
            ?? CodecChoices.FirstOrDefault(choice =>
                string.Equals(
                    choice.Extension,
                    _selectedFormat,
                    StringComparison.OrdinalIgnoreCase) &&
                choice.CanSelect)
            ?? CodecChoices.FirstOrDefault(choice => choice.CanSelect);
    }

    private bool ValidateSelectedCodec(string operation)
    {
        CodecChoice? selected = _catalog.GetSelectedChoice(
            _config,
            SelectedFormat);
        if (selected != null)
        {
            CodecHealth health = _catalog.GetHealth(selected.Encoder);
            if (health.IsReady)
                return true;
            StatusText = operation + " cannot start: " + selected.CompactLabel +
                " is not ready. " + health.Detail;
            return false;
        }
        StatusText = operation + " cannot start: no encoder is configured for " +
            SelectedFormat + ". Open the codec picker to choose a ready encoder.";
        return false;
    }

    // Test & Copy: read the disc twice (a third time on a mismatch) and write only tracks two
    // independent reads agree on bit-for-bit. Mirrors RunJobAsync's progress/level/re-read wiring
    // and IsRipping lifecycle so the same live visuals work across the 2-3 reads; the result
    // surfacing differs (Passed / Held / failed) instead of a plain rip-or-verify outcome.
    private async Task RunTestCopyAsync()
    {
        if (!CanStartEncodedJob(IsDiscPresent, IsRipping, IsBusy, ArtLoading))
            return;
        if (!ValidateSelectedCodec("Test & Copy")) return;
        PersistPrimarySettingsSnapshot();
        DiscardParkedHeld("a new job started");   // the user moved on from the parked disc
        // Keep the previous held staging until the NEW run has produced a result. Discarding it up
        // front meant a re-run that failed early (calibration refused, no disc, stopped) left the user
        // with nothing to accept - the verified reads they already had were gone.
        var previousHeld = _heldResult;
        if (previousHeld != null) { _heldResult = null; TestCopyHeld = false; }
        char drive = _selectedDrive;
        int cq = EffectiveCorrectionQuality;
        bool salvage = SalvageRead;   // frozen with the rest of the job snapshot
        CodecLocked = true;
        IsRipping = true;
        RipDone = false;
        TestCopyHeld = false;
        RipProgress = 0;
        HistoryText = "";
        // R120 hygiene: live per-track and database evidence is about to be written as this job
        // reads, so clear the previous job's values first. Without this a new disc would show
        // the last disc's verdict until its own reads reported.
        ArText = "not checked"; CtdbText = "not checked";
        foreach (var resetTrack in Tracks) { resetTrack.ArResult = "-"; resetTrack.CtdbResult = "-"; }
        HistoryIsWarning = false;
        _baseActivity = AppActivity.Ripping;
        _status.Report(_baseActivity, 0);
        StatusText = "Starting Test & Copy...";
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



        // ReadProgress and re-read events fire on the ripper's thread; marshal those to the UI.
        // RMS and scope samples use the bounded mailbox drained by the UI timer.
        void Report(double frac, string status)
            => _ui.Post(() =>
            {
                RipProgress = frac; StatusText = status; UpdateSpeed(frac); UpdateTrackProgress(frac);
                // feed the taskbar progress; keep a re-read/unreadable state's badge while it lasts
                var act = _status.Activity is AppActivity.Rereading or AppActivity.Unreadable ? _status.Activity : _baseActivity;
                _status.Report(act, frac);
            });
        var Reread = MakeRereadHandler();

        StartRereadTimer();

        string fmt = SelectedFormat;
        var meta = _chosenMetadata;
        string outBase = OutputBaseDir;
        RipOutputLayout outputLayout = _settings.RipOutputLayout;
        await EnsureArtBeforeRipAsync();
        byte[]? cover = _coverBytes == null
            ? null
            : (byte[])_coverBytes.Clone();
        void LockCodec() => _ui.Post(() => CodecLocked = true);
        void PublishCrcEvidence(CUETools.Wpf.Accuracy.TrackCrc[] evidence)
        {
            void Apply()
            {
                try { ApplyCrcEvidence(evidence); }
                catch
                {
                    // The final Test & Copy result carries the same CRC evidence. A transient
                    // presentation failure must never terminate the active optical operation.
                }
            }
            if (!_ui.CheckAccess())
                _ui.Post(Apply);
            else
                Apply();
        }
        // Live per-track CRC (R120): one track's checksum as that track finishes reading,
        // instead of the whole grid staying empty until the read ends.
        void PublishTrackCrc(TrackCrcLive live)
        {
            void Apply()
            {
                try
                {
                    int index = live.TrackNumber - 1;
                    if (index >= 0 && index < Tracks.Count)
                        Tracks[index].ApplyLiveCrc(live.Crc32, live.IsCopyRead);
                }
                catch { }
            }
            if (!_ui.CheckAccess())
                _ui.Post(Apply);
            else
                Apply();
        }
        // Live database verdict (R120): what the Test read found, reported when that read ends
        // rather than after the whole transaction.
        void PublishReadVerdict(ReadVerdict verdict)
        {
            void Apply()
            {
                try { ApplyLiveReadVerdict(verdict); }
                catch { }
            }
            if (!_ui.CheckAccess())
                _ui.Post(Apply);
            else
                Apply();
        }
        // The codec was health-checked and locked before the Test read. Copy and a possible
        // confirming read use that immutable format snapshot.
        RipTelemetryMailbox telemetry = StartTelemetry();
        TestCopyRunResult result;
        try
        {
            result = await Task.Run(() => _rip.RunTestAndCopy(
                drive, cq, fmt, meta, outBase, Report, telemetry,
                Reread, cover, liveFormat: null,
                onEncodeStart: LockCodec,
                onCrcEvidence: PublishCrcEvidence,
                outputLayout: outputLayout,
                salvage: salvage,
                onTrackCrc: PublishTrackCrc,
                onReadVerdict: PublishReadVerdict));
        }
        finally
        {
            StopTelemetry(telemetry);
        }

        // The re-run has finished, so the PREVIOUS held staging is either superseded (this run produced
        // a real outcome) or still the best copy the user has (this run failed before producing
        // anything - calibration refused, no disc, stopped). Only discard it in the first case; the old
        // code discarded it up front, so a failed re-run left nothing to accept.
        if (previousHeld != null)
        {
            if (result.Ok) _rip.DiscardStaging(previousHeld);
            else { _heldResult = previousHeld; TestCopyHeld = true; }
        }

        RipProgress = result.Ok ? 1 : RipProgress;
        if (result.Ok)
            ApplyCrcEvidence(result.CrcEvidence);
        if (result.Ok && result.Outcome == CUETools.Wpf.Accuracy.TestCopyOutcome.Passed)
        {
            LastOutputDir = result.OutputDir;
            TestCopyHeld = false; _heldResult = null;
            TestCopyCompletionState completion =
                ClassifyTestCopyCompletion(result);
            bool repairRequired =
                completion == TestCopyCompletionState.ConsistentRepairable;
            bool damagedAgreement =
                completion != TestCopyCompletionState.Verified;
            TestCopyText = damagedAgreement
                ? $"Test & Copy consistent after {result.ReadsUsed} reads; " +
                  "at least two agreed per track." +
                  (repairRequired
                      ? $"  CTDB found recoverable damage in {result.CtdbRepairSectors} " +
                        "sector(s); repair is required for database-verified audio."
                      : "  WARNING: read damage remains without an exact database match.")
                : $"Test & Copy verified after {result.ReadsUsed} reads; " +
                  "at least two agreed per track.";
            TestCopyText +=
                (result.Accurate
                    ? $"  Also AccurateRip-accurate (confidence {result.ArConfidence})."
                    : result.ArTotal > 0
                        ? "  AccurateRip found the disc but no exact match."
                        : "  Not in AccurateRip.")
                + (result.LosslessOutput
                    ? result.OutputVerificationPerformed
                        ? "  Final output PCM was decoded and verified after metadata finalization."
                        : "  WARNING: final output was not independently verified."
                    : "");
            TestCopyIsWarning = damagedAgreement;
            // The final line must not contradict the per-read line: a lookup that never
            // completed reads as such rather than collapsing to 0 / 0.
            ArText = result.ArLookupFailed && result.ArTotal == 0
                ? "lookup failed"
                : $"{result.ArConfidence} / {result.ArTotal}" + (result.Accurate ? "  accurate" : "");
            CtdbText = result.CtdbConfidence > 0
                ? $"match . conf {result.CtdbConfidence}"
                : result.CtdbCanRecover
                    ? $"recoverable damage . {result.CtdbRepairSectors} sector(s)"
                    : result.CtdbLookupFailed && result.CtdbTotal == 0
                        ? "lookup failed"
                        : $"{result.CtdbConfidence} / {result.CtdbTotal}";
            Accurate = result.Accurate;
            ApplyHistoryStatus(result.HistoryRecorded, result.HistoryKnown,
                result.HistoryMatches, result.HistoryPriorReads, result.HistoryDiffTracks);
            // report the format that was ACTUALLY encoded (polled at encode start), not the one that
            // was selected when the button was pressed - otherwise a mid-verify codec switch makes
            // this summary name the wrong format
            string wroteFmt = string.IsNullOrWhiteSpace(result.Format) ? fmt : result.Format;
            RipSummary = $"Test & Copy: {result.FileCount} {wroteFmt} files, "
                + (result.Salvaged ? "salvaged (drive-stable) after " : damagedAgreement ? "consistent after " : "verified after ")
                + $"{result.ReadsUsed} reads (at least 2 agreed per track)"
                + (repairRequired
                    ? $"; CTDB repair required for {result.CtdbRepairSectors} sector(s)"
                    : damagedAgreement
                        ? "; read damage remains"
                        : "")
                + (result.LosslessOutput
                    ? result.OutputVerificationPerformed
                        ? "; final output PCM verified after metadata"
                        : "; WARNING: final output not verified"
                    : "");
            StatusText = result.Salvaged
                ? $"Test & Copy salvaged (drive-stable capture, not verified) -> {result.OutputDir}"
                : damagedAgreement
                    ? repairRequired
                        ? $"Test & Copy consistent; CTDB repair required -> {result.OutputDir}"
                        : $"Test & Copy consistent; read damage remains -> {result.OutputDir}"
                    : $"Test & Copy verified -> {result.OutputDir}";
            SetPostRipRepair(result);
            // Record it. A Test & Copy is the highest-assurance mode and was the ONE that left no
            // trace: it never published, so the report page and RECENTLY RIPPED had no record that
            // the most carefully verified rips ever happened.
            // result.CorrectionQuality, not the live dropdown: RunTestAndCopy forces the reads up to at
            // least Secure, so with the dropdown on Burst a Test & Copy that really read at Secure would
            // have archived a certificate claiming "Burst".
            PublishReport($"Test & Copy ({result.ReadsUsed} reads)", result.CorrectionQuality,
                result.ArConfidence, result.ArTotal,
                result.CtdbConfidence, result.CtdbTotal, result.Accurate,
                (result.Salvaged ? "salvaged (drive-stable capture)" : damagedAgreement ? "consistent" : "verified") +
                $" after {result.ReadsUsed} optical reads; at least 2 agreed per track" +
                (repairRequired ? "; CTDB repair required" : ""),
                result.OutputDir, result.FileCount, result.Format, result.ReadsUsed, 2,
                result.OutputVerificationKnown, result.LosslessOutput,
                result.OutputVerificationPerformed, result.OutputVerificationDetail,
                failedWindows: result.FailedWindows,
                damageRepairRequired: result.CtdbHasErrors,
                salvaged: result.Salvaged);
            // shared tail (sets RipDone, ejects when enabled). Only on PASSED: a HELD result may still
            // be re-run, and that needs the disc in the drive.
            RaisePublished(result.OutputDir, result.ArLookupFailed, result.CtdbLookupFailed);
            await FinishRipAsync(result.OutputDir, drive);
        }
        else if (result.Ok && result.Outcome == CUETools.Wpf.Accuracy.TestCopyOutcome.Held)
        {
            _heldResult = result;
            TestCopyHeld = true;
            TestCopyIsWarning = true;
            string tracks = string.Join(
                ", ",
                System.Array.ConvertAll(
                    result.HeldTracks,
                    x => (x + 1).ToString()));
            string reason = string.IsNullOrWhiteSpace(result.HoldReason)
                ? ""
                : result.HoldReason + " ";
            TestCopyText =
                $"Held - {reason}The Test and Copy CRCs disagree on track(s) {tracks}. " +
                "Nothing was written. The completed Copy is retained; re-run, accept it anyway, or discard.";
            StatusText = string.IsNullOrWhiteSpace(result.HoldReason)
                ? "Test & Copy held - tracks disagree."
                : "Test & Copy held - confirming read did not finish; completed Copy retained.";
        }
        else
        {
            StatusText = result.Error == "Stopped." ? "Test & Copy stopped." : "Test & Copy failed: " + result.Error;
        }
        LevelL = 0; LevelR = 0;   // needles fall back to rest when the job ends
        StopRereadTimer();
        foreach (var t in Tracks) { t.Active = false; if (result.Ok && result.Outcome == CUETools.Wpf.Accuracy.TestCopyOutcome.Passed) t.Progress = 1; }
        IsRipping = false;
        CodecLocked = false;
        _baseActivity = AppActivity.Idle;
        _status.Report(AppActivity.Idle);
    }

    internal static bool CanStartEncodedJob(
        bool isDiscPresent,
        bool isRipping,
        bool isBusy,
        bool artLoading) =>
        isDiscPresent && !isRipping && !isBusy && !artLoading;

    private void ApplyHistoryStatus(
        bool recorded,
        bool known,
        bool matches,
        int priorReads,
        int diffTracks)
    {
        if (!recorded)
        {
            HistoryText = "This job completed, but verify history could not be saved.";
            HistoryIsWarning = true;
        }
        else if (!known)
        {
            HistoryText = "First read of this disc - recorded to your verify history.";
            HistoryIsWarning = false;
        }
        else if (matches)
        {
            HistoryText = $"Consistent with your {priorReads} earlier read(s) - bytes match.";
            HistoryIsWarning = false;
        }
        else
        {
            HistoryText = $"DIFFERS from your earlier read on {diffTracks} track(s) - investigate.";
            HistoryIsWarning = true;
        }
    }

    private void SetPostRipRepair(VerifyResult result)
    {
        CanRepairLastRip = false;
        RepairLastRipText = "";
        _lastRepairSource = "";
        if (!result.CtdbHasErrors || !result.CtdbCanRecover)
            return;

        if (string.IsNullOrWhiteSpace(result.RepairSourcePath))
        {
            RepairLastRipText =
                "CTDB parity can recover this lossless rip, but the output has no unambiguous " +
                "album input. Keep cue-sheet creation enabled for multi-track repair.";
            StatusText += "  CTDB repair is available, but this multi-track output has no album cue.";
            return;
        }

        _lastRepairSource = result.RepairSourcePath;
        CanRepairLastRip = true;
        RepairLastRipText =
            $"CTDB parity can recover {result.CtdbRepairSectors} damaged sector(s). " +
            "Repair creates and independently verifies a sibling copy; this rip stays unchanged." +
            RepairHeadroomText(result.CtdbRepairWorstStripe, result.CtdbRepairStripeCapacity);
    }

    private void SetPostRipRepair(
        TestCopyRunResult result,
        string? publishedDirectory = null)
    {
        CanRepairLastRip = false;
        RepairLastRipText = "";
        _lastRepairSource = "";
        if (!result.CtdbHasErrors || !result.CtdbCanRecover)
            return;

        string source = string.IsNullOrWhiteSpace(publishedDirectory)
            ? result.RepairSourcePath
            : RipService.RebindRepairSource(
                publishedDirectory,
                result.RepairSourceRelativePath);
        if (string.IsNullOrWhiteSpace(source))
        {
            RepairLastRipText =
                "CTDB parity can recover this lossless Test & Copy output, but it has no unambiguous album input. Keep cue-sheet creation enabled for multi-track repair.";
            StatusText += "  CTDB repair is available, but this multi-track output has no album cue.";
            return;
        }

        _lastRepairSource = source;
        CanRepairLastRip = true;
        RepairLastRipText =
            $"CTDB parity can recover {result.CtdbRepairSectors} damaged sector(s). " +
            "Repair creates and independently verifies a sibling copy; this output stays unchanged." +
            RepairHeadroomText(result.CtdbRepairWorstStripe, result.CtdbRepairStripeCapacity);
    }

    // Repair headroom (R115): how close the fix's worst parity stripe came to the npar/2
    // capacity. At capacity, one more error there would have been unrecoverable - the honest
    // basis for choosing re-rip vs repair.
    private static string RepairHeadroomText(int worstStripe, int stripeCapacity)
        => stripeCapacity > 0
            ? $" Worst parity stripe uses {worstStripe} of {stripeCapacity} correctable errors" +
              (worstStripe >= stripeCapacity
                  ? " - at capacity; prefer a re-rip if the disc still reads."
                  : ".")
            : "";

    private async Task RepairLastRipAsync()
    {
        if (!CanRepairLastRip || IsBusy || IsRipping ||
            string.IsNullOrWhiteSpace(_lastRepairSource))
            return;
        if (!await _prompt.ConfirmOkCancelAsync(
                "CTDB repair will build a new sibling folder, independently verify the repaired " +
                "audio, and publish it only if verification succeeds. The completed rip will not " +
                "be changed.\n\nProceed with repair?",
                "Repair completed rip from CTDB parity"))
            return;

        string source = _lastRepairSource;
        IsBusy = true;
        RequeryHub.RequestRequery();
        StatusText = "Repairing completed rip from CTDB parity...";
        void Report(double fraction, string status)
        {
            _ui.Post(() =>
                {
                    RipProgress = fraction;
                    StatusText = status;
                });
        }

        try
        {
            VerifyFilesResult repaired = await Task.Run(
                () => _verify.Repair(source, Report));
            if (!repaired.Ok || !repaired.RepairApplied ||
                string.IsNullOrWhiteSpace(repaired.OutputPath))
            {
                StatusText = "CTDB repair failed: " + repaired.Error;
                return;
            }

            CanRepairLastRip = false;
            _lastRepairSource = "";
            LastOutputDir = repaired.OutputPath;
            RepairLastRipText =
                "CTDB repaired copy independently verified and published with repair.verify, " +
                "an AccurateRip report, and a CTDB repair log. The original rip remains unchanged.";
            RipSummary +=
                "  .  CTDB repaired copy independently verified with durable evidence";
            StatusText = "CTDB repaired copy verified -> " + repaired.OutputPath;
            RipProgress = 1;
            _status.Report(AppActivity.Done);
        }
        catch (Exception ex)
        {
            StatusText = "CTDB repair failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            RequeryHub.RequestRequery();
        }
    }

    /// <summary>The ONE tail every finished rip goes through, whichever button started it: the Rip
    /// button, a passed Test &amp; Copy, or accepting a held copy read. There used to be three copies of
    /// this, and each had to remember every post-rip setting independently - which is exactly how
    /// "Eject after rip" ended up working for Rip and doing nothing for Test &amp; Copy, and how accepting
    /// a held read left no "Open folder" panel. Anything that must happen when a rip ends belongs HERE,
    /// not in a caller.</summary>
    /// <summary>
    /// Raised when a rip has published its files, with the directory and whether the
    /// databases actually answered during the read.
    ///
    /// A rip makes its own AccurateRip and CTDB contact rather than going through the
    /// journaled verify service, so an offline rip used to finish with no verdict and
    /// nothing queued: only the Verify page and the Queue journal. A head that keeps an
    /// offline journal subscribes here and queues the published album, so the verdict
    /// arrives on a later launch instead of the user having to know to re-verify by hand.
    /// </summary>
    public event Action<RipPublication>? Published;

    private void RaisePublished(string outputDir, bool arFailed, bool ctdbFailed)
    {
        if (string.IsNullOrEmpty(outputDir)) return;
        try
        {
            Published?.Invoke(new RipPublication(outputDir, arFailed, ctdbFailed));
        }
        catch (Exception ex)
        {
            // A subscriber is ancillary: it must never change a completed rip's outcome.
            _log.Warn("rip", "published-notification subscriber failed: " + ex.GetType().Name);
        }
    }

    private async System.Threading.Tasks.Task FinishRipAsync(string outputDir, char drive)
    {
        if (!string.IsNullOrEmpty(outputDir)) LastOutputDir = outputDir;
        RipDone = true;
        // "Never eject from software" overrides "Eject after rip" - and DriveService.OpenTray enforces
        // that too, so this is belt and braces rather than the only guard.
        if (_config.ejectAfterRip && !_config.disableEjectDisc)
        {
            await System.Threading.Tasks.Task.Run(() => { try { _drives.OpenTray(drive); } catch { } });
            TrayState = DriveTrayState.Open;
        }
    }

    private async void AcceptCopyAnyway()
    {
        var held = _heldResult; if (held == null) return;
        string dir = _rip.CommitCopyReadAnyway(held, OutputBaseDir);
        bool ok = dir.Length > 0;
        // Only let go of the held result once it is safely committed. _heldResult is the ONLY live
        // handle to the staging, and both Accept anyway and Discard are gated on it being non-null, so
        // clearing it after a FAILED commit killed the retry buttons and orphaned a full verified album
        // in %TEMP% with no way to reach it.
        if (ok) { TestCopyHeld = false; _heldResult = null; }
        TestCopyText = ok
            ? "Copy read accepted anyway - written and flagged NOT test-verified."
            : "Could not write the copy read - the staged reads were kept, so you can try again.";
        StatusText = TestCopyText;
        // A committed copy read IS a finished rip: it needs the same tail as any other, which it never
        // used to get (no eject, and no RipDone so the "Open folder" panel never appeared).
        if (ok)
        {
            SetPostRipRepair(held, dir);
            // held.CorrectionQuality, not the live dropdown: the reads happened earlier, possibly at a
            // different setting, and the archived report must name the mode they were actually made at
            PublishReport($"Test & Copy (accepted, NOT verified, {held.ReadsUsed} reads)",
                held.CorrectionQuality, held.ArConfidence,
                held.ArTotal, held.CtdbConfidence, held.CtdbTotal, held.Accurate,
                $"accepted without agreement after {held.ReadsUsed} reads - NOT test-verified",
                dir, OutputLayout.CountAudioFiles(dir, held.Format), held.Format,
                outputVerificationKnown: held.OutputVerificationKnown,
                losslessOutput: held.LosslessOutput,
                outputVerificationPerformed: held.OutputVerificationPerformed,
                outputVerificationDetail: held.OutputVerificationDetail,
                failedWindows: held.FailedWindows,
                damageRepairRequired: held.CtdbHasErrors,
                salvaged: held.Salvaged);
            RaisePublished(dir, held.ArLookupFailed, held.CtdbLookupFailed);
            await FinishRipAsync(dir, _selectedDrive);
        }
    }

    private void DiscardHeld()
    {
        var held = _heldResult; if (held == null) return;
        _rip.DiscardStaging(held);
        TestCopyHeld = false; _heldResult = null;
        TestCopyText = "Discarded - nothing was written.";
        StatusText = TestCopyText;
    }

    // Convert progress deltas into read speed as a multiple of realtime (1x = 75 sectors/s).
    // speed = (fraction of disc read) * (disc seconds) / (elapsed seconds).
    private void UpdateSpeed(double frac)
    {
        var now = DateTime.UtcNow;
        // Test & Copy runs 2-3 reads over the same disc, so frac drops from ~1.0 back to ~0 at each
        // new read. Re-baseline on that reset instead of falling into the dFrac <= 0 early-return
        // below forever - otherwise the speed readout stalls for the rest of the operation.
        if (frac < _lastSpeedFrac) { _lastSpeedFrac = frac; _lastSpeedTick = now; return; }
        double dt = (now - _lastSpeedTick).TotalSeconds;
        double dFrac = frac - _lastSpeedFrac;
        if (dt < 0.08 || dFrac <= 0 || _discSeconds <= 0) return;
        double sx = dFrac * _discSeconds / dt;
        SpeedText = $"{sx:0.0}x";
        SpeedLevel = Math.Max(0, Math.Min(1, sx / SpeedCapX));
        _lastSpeedTick = now;
        _lastSpeedFrac = frac;
    }

    // "Read disc" with the tray open means "close it and read what's in there" - the natural thing
    // to expect. Closing hands off to the watcher, which reads the disc once it lands. With the
    // tray already shut, it just (re)reads.
    private void ReadDiscOrClose()
    {
        if (TrayState == DriveTrayState.Open)
        {
            char d = _selectedDrive;
            if (d == '\0') return;
            StatusText = $"Closing tray {d}:...";
            TrayState = DriveTrayState.ClosedNoDisc;   // optimistic; the watcher confirms + reads
            _triedCurrentMedia = false;
            Task.Run(() => { try { _drives.CloseTray(d); } catch { } });
        }
        else _ = ReadDiscAsync();
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
            // Do not fake the tray state when software eject is disabled: the IOCTL is suppressed, so
            // claiming "Open" and clearing the disc view would be a lie the user has to undo.
            if (_config.disableEjectDisc)
            {
                StatusText = "\"Never eject from software\" is on - use the drive's own eject button.";
                return;
            }
            StatusText = $"Ejecting drive {d}:...";
            TrayState = DriveTrayState.Open;
            ClearDiscView(DriveTrayState.Open);
            Task.Run(() => { try { _drives.OpenTray(d); } catch { } });
        }
    }

    // Parked-held resolution at disc arrival (R108): the same disc restores the held offer with
    // its staging intact; a different disc proves the user moved on, so the parked staging is
    // freed - it must never be committable under another disc's identity.
    private void ResolveParkedHeld(string discId)
    {
        var parked = _parkedHeldResult;
        if (parked == null) return;
        if (_parkedHeldDiscId == discId)
        {
            _parkedHeldResult = null;
            _parkedHeldDiscId = "";
            _heldResult = parked;
            TestCopyHeld = true;
            TestCopyIsWarning = true;
            TestCopyText =
                "Held - the disc is back. The completed Copy is retained; re-run, accept it anyway, or discard.";
        }
        else
        {
            DiscardParkedHeld("a different disc was loaded");
        }
    }

    private void DiscardParkedHeld(string reason)
    {
        var parked = _parkedHeldResult;
        if (parked == null) return;
        _parkedHeldResult = null;
        _parkedHeldDiscId = "";
        try { _rip.DiscardStaging(parked); } catch { }
        _log.Info("rip", $"parked held Test & Copy freed - {reason}");
    }

    // Drop back to the no-disc view (tray open or emptied). Keeps the disc read-map / tracks from
    // lingering after the media is gone.
    private void ClearDiscView(DriveTrayState tray)
    {
        IsDiscPresent = false;
        // RipDone is deliberately NOT cleared here. With "Eject after rip" on, the tray opens right
        // after a rip finishes, the tray poll sees the change and lands in this method about two
        // seconds later - which wiped the completion panel before the user could read the output
        // path or press Open folder. It is cleared when the NEXT job starts, or by Dismiss.
        Tracks.Clear();
        Releases.Clear();
        _chosenMetadata = null;
        _selectedRelease = null;
        // A held Test & Copy belongs to the disc that produced it. Leaving it live across a disc
        // change meant "Accept anyway" would commit the PREVIOUS disc's audio while the page
        // showed a different disc. But deleting it here destroyed the only completed Copy on a
        // phantom tray event or an accidental eject (R108). So PARK it, keyed to its disc: the
        // same disc returning restores the offer; a different disc, a new job, or an explicit
        // Discard frees the staging.
        bool heldParked = false;
        if (_heldResult != null)
        {
            if (_parkedHeldResult != null)
                try { _rip.DiscardStaging(_parkedHeldResult); } catch { }
            _parkedHeldResult = _heldResult;
            _parkedHeldDiscId = _currentDiscId;
            _heldResult = null;
            TestCopyHeld = false;
            TestCopyText = "";   // its only binding sits inside the panel that just collapsed
            heldParked = true;
        }
        bool open = tray == DriveTrayState.Open;
        AlbumTitle = open ? "Tray open - insert a disc, then Close" : "No disc - insert an audio CD";
        AlbumArtist = "";
        DiscInfoText = "";
        // Say why the held panel vanished. It must be assigned AFTER the line above, which would
        // otherwise overwrite it, and it cannot go in TestCopyText - that binding lives inside the
        // panel this same method collapses, so the explanation would never be rendered.
        StatusText = heldParked
            ? "The held Test & Copy is parked - reinsert the same disc to resume it. A different disc frees it."
            : open ? "Tray open." : "Drive ready - insert an audio CD.";
        OnPropertyChanged(nameof(HasReleases));
        OnPropertyChanged(nameof(SelectedRelease));
    }

    private async Task BrowseOutputAsync()
    {
        string? folder = await _dialogs.PickFolderAsync("Choose where ripped albums go");
        if (folder != null) OutputBaseDir = folder;
    }

    private void PersistPrimarySettingsSnapshot()
    {
        // The primary dashboard publishes its current preferences before a long job starts.
        // A secondary drive window launched during that job then begins from the same durable
        // snapshot. Secondary windows never write the shared file, including on exit.
        if (!_launchOptions.IsSecondaryDriveWindow)
            _settingsStore.Save(_config, _settings);
    }

    private void OpenParallelDrive(char drive)
    {
        if (!IsRipping || drive == '\0' || !ParallelDrives.Contains(drive))
            return;

        try
        {
            var start = DriveWindowLauncher.Create(drive);
            System.Diagnostics.Process.Start(start);
            StatusText =
                $"Drive {drive}: opened in an isolated CUETools window. " +
                $"This {SelectedDrive}: job continues here.";
            _log.Info("drive",
                $"launched secondary drive window requestedDrive={drive}");
        }
        catch (Exception ex)
        {
            StatusText =
                $"Could not open a CUETools window for drive {drive}: please try again.";
            _log.Error("drive",
                $"secondary drive window launch failed requestedDrive={drive}",
                ex);
        }
        finally
        {
            // A running job never changes this window's selected drive. Reassert
            // the source value after the ComboBox finishes its target update.
            _ui.Post(() => OnPropertyChanged(nameof(SelectedDrive)));
        }
    }

    private void OpenFolder()
    {
        try
        {
            if (System.IO.Directory.Exists(LastOutputDir))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = LastOutputDir, UseShellExecute = true });
            else
                StatusText = "Folder not found: " + LastOutputDir;   // e.g. deleted since the rip
        }
        catch { StatusText = "Could not open the folder."; }
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
        ApplyCrcEvidence(result.Record?.Tracks ?? Array.Empty<CUETools.Wpf.Accuracy.TrackCrc>());
    }

    private void ApplyCrcEvidence(
        System.Collections.Generic.IReadOnlyList<CUETools.Wpf.Accuracy.TrackCrc> evidence)
    {
        for (int i = 0; i < Tracks.Count; i++)
        {
            CUETools.Wpf.Accuracy.TrackCrc? crc =
                evidence != null && i < evidence.Count
                    ? evidence[i]
                    : null;
            Tracks[i].ApplyCrcEvidence(crc);
        }
    }

    // R120: light the AR and CTDB columns and the side panel from one completed read. This is
    // presentation only - the immutable records are still assembled at their existing points -
    // and it is deliberately silent about tracks the read had no verdict for.
    private void ApplyLiveReadVerdict(ReadVerdict verdict)
    {
        for (int i = 0; i < Tracks.Count; i++)
        {
            if (i < verdict.ArPerTrack.Length && verdict.ArPerTrack[i] > 0)
                Tracks[i].ArResult = verdict.ArPerTrack[i].ToString();
            if (i < verdict.CtdbPerTrack.Length && verdict.CtdbPerTrack[i] > 0)
                Tracks[i].CtdbResult = verdict.CtdbPerTrack[i].ToString();
        }
        // A failed lookup is not an absent disc, and it is checked after the real answers
        // so a database that replied before failing still reports what it said.
        ArText = verdict.Accurate
            ? $"{verdict.ArConfidence} / {verdict.ArTotal} accurate"
            : verdict.ArTotal > 0 ? $"{verdict.ArConfidence} / {verdict.ArTotal}"
            : verdict.ArLookupFailed ? "lookup failed" : "not in database";
        CtdbText = verdict.CtdbConfidence > 0
            ? $"match . conf {verdict.CtdbConfidence}"
            : verdict.CtdbTotal > 0 ? "found, no exact match"
            : verdict.CtdbLookupFailed ? "lookup failed" : "not found";
        StatusText = $"{verdict.ReadKind} read complete - {ArText} (AccurateRip), {CtdbText} (CTDB).";
    }

    private void PublishReport(bool encode, VerifyResult result, int correctionQualityUsed)
        => PublishReport(encode ? "Rip" : "Verify", correctionQualityUsed, result.ArConfidence, result.ArTotal,
            result.CtdbConfidence, result.CtdbTotal, result.Accurate, result.Status, result.OutputDir,
            result.FileCount, encode ? result.Format : "",
            outputVerificationKnown: result.OutputVerificationKnown,
            losslessOutput: result.LosslessOutput,
            outputVerificationPerformed: result.OutputVerificationPerformed,
            outputVerificationDetail: result.OutputVerificationDetail,
            failedWindows: result.FailedWindows,
            damageRepairRequired: result.CtdbHasErrors);

    /// <summary>Record a finished job in the report page and the history list. Every completed operation
    /// belongs here - a Test &amp; Copy most of all, since it is the highest-assurance mode and used to be
    /// the ONE that left no trace: it never called this, so the app's own history had no record that the
    /// most carefully verified rips had ever happened.</summary>
    private void PublishReport(string mode, int correctionQualityUsed, int arConf, int arTotal,
        int ctConf, int ctTotal, bool accurate, string status, string outputDir, int fileCount,
        string format = "", int opticalReadsUsed = 0, int minimumAgreeingReads = 0,
        bool outputVerificationKnown = false, bool losslessOutput = false,
        bool outputVerificationPerformed = false,
        string outputVerificationDetail = "",
        int failedWindows = 0, bool damageRepairRequired = false,
        bool salvaged = false)
    {
        var d = _lastDisc;
        var report = new RipReport
        {
            Mode = mode,
            Album = d?.Album ?? AlbumTitle,
            Artist = d?.Artist ?? "",
            Year = d?.Year ?? "",
            DriveName = d?.DriveName ?? "",
            Offset = d?.Offset ?? 0,
            // the mode the disc was ACTUALLY read at, not whatever the dropdown says now
            CorrectionQuality = correctionQualityUsed,
            ArConfidence = arConf,
            ArTotal = arTotal,
            CtdbConfidence = ctConf,
            CtdbTotal = ctTotal,
            Accurate = accurate,
            OpticalReadsUsed = opticalReadsUsed,
            MinimumAgreeingReads = minimumAgreeingReads,
            Status = status,
            OutputDir = outputDir,
            FileCount = fileCount,
            Format = format,
            OutputVerificationKnown = outputVerificationKnown,
            LosslessOutput = losslessOutput,
            OutputVerificationPerformed = outputVerificationPerformed,
            OutputVerificationDetail = outputVerificationDetail,
            TrackCount = d?.AudioTracks ?? Tracks.Count,
            TocId = d?.TocId ?? "",
            FailedWindows = failedWindows,
            DamageRepairRequired = damageRepairRequired,
            Salvaged = salvaged
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
