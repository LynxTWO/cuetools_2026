using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using CUETools.Wpf.Models;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Album-level Verify &amp; Repair workspace. A selection is resolved into independent disc
/// manifests before any network or decoder work starts. Multi-disc sets run serially so the
/// shared engine configuration and each result keep one clear owner.
/// </summary>
public sealed class VerifyViewModel : PageViewModel
{
    private readonly IVerifyService _verify;
    private readonly IReportStore _reports;
    private readonly IVerificationSourceDiscovery _discovery;
    private readonly IFileDialogService? _dialogs;
    private readonly IUserPrompt? _prompts;
    private readonly IUiDispatcher? _dispatcher;
    private VerificationSourceSet? _sourceSet;
    private bool _stopRequested;

    public VerifyViewModel(
        IVerifyService verify,
        IReportStore reports,
        IVerificationSourceDiscovery discovery,
        IFileDialogService? dialogs = null,
        IUserPrompt? prompts = null,
        IUiDispatcher? dispatcher = null)
    {
        Title = "Verify & Repair";
        Group = "Work";
        Subtitle = "Check existing files against AccurateRip and CTDB, and repair from CTDB parity.";
        _verify = verify;
        _reports = reports;
        _discovery = discovery;
        _dialogs = dialogs;
        _prompts = prompts;
        _dispatcher = dispatcher;

        BrowseFileCommand = new RelayCommand(_ => BrowseFile(), _ => !IsBusy);
        BrowseFolderCommand = new RelayCommand(_ => BrowseFolder(), _ => !IsBusy);
        VerifyCommand = new RelayCommand(
            _ => { _ = VerifyAllAsync(); },
            _ => HasSource && !IsBusy);
        StopCommand = new RelayCommand(
            _ => RequestStop(),
            _ => IsBusy && !_stopRequested);
        RepairDiscCommand = new RelayCommand(
            disc => { _ = RepairDiscAsync(disc as VerifyDiscViewModel, confirm: true); },
            disc => disc is VerifyDiscViewModel item && item.CanRepair && !IsBusy);
    }

    public ObservableCollection<VerifyDiscViewModel> Discs { get; } = new();

    private string _sourcePath = "";
    public string SourcePath
    {
        get => _sourcePath;
        private set => Set(ref _sourcePath, value);
    }

    public bool HasSource => _sourceSet != null && Discs.Count > 0;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            RequeryHub.RequestRequery();
        }
    }
    public bool IsIdle => !IsBusy;

    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private string _statusText =
        "Drop an album folder, CUE sheet, playlist, or supported lossless file.";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private bool _hasResult;
    public bool HasResult { get => _hasResult; private set => Set(ref _hasResult, value); }

    private string _albumHeading = "Verify an album you already have";
    public string AlbumHeading { get => _albumHeading; private set => Set(ref _albumHeading, value); }

    private string _overviewHeadline = "";
    public string OverviewHeadline { get => _overviewHeadline; private set => Set(ref _overviewHeadline, value); }

    private string _overviewDetail = "";
    public string OverviewDetail { get => _overviewDetail; private set => Set(ref _overviewDetail, value); }

    private string _discCountText = "";
    public string DiscCountText { get => _discCountText; private set => Set(ref _discCountText, value); }

    private string _trackCountText = "";
    public string TrackCountText { get => _trackCountText; private set => Set(ref _trackCountText, value); }

    private string _durationText = "";
    public string DurationText { get => _durationText; private set => Set(ref _durationText, value); }

    public ICommand BrowseFileCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand VerifyCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RepairDiscCommand { get; }

    private static readonly FilePickerGroup[] BrowseFileGroups =
    {
        new("Rip sets (*.cue, *.m3u, *.m3u8)", new[] { "cue", "m3u", "m3u8" }),
        new("Supported lossless audio",
            new[] { "flac", "wv", "ape", "tak", "m4a", "tta", "wav", "ofr", "wma", "aiff" }),
        new("All files", new[] { "*" })
    };

    private async void BrowseFile()
    {
        if (_dialogs == null) return;
        string[]? files = await _dialogs.PickFilesAsync(
            "Choose a rip to verify", multiselect: true, BrowseFileGroups);
        if (files is { Length: > 0 })
            LoadSources(files);
    }

    private async void BrowseFolder()
    {
        if (_dialogs == null) return;
        string? folder = await _dialogs.PickFolderAsync("Choose an album folder to verify");
        if (folder != null)
            LoadSources(new[] { folder });
    }

    /// <summary>Entry point shared by dialogs and WPF file-drop handling.</summary>
    public bool LoadSources(IEnumerable<string> paths)
    {
        if (IsBusy)
        {
            StatusText = "Stop the current verification before choosing another album.";
            return false;
        }

        VerificationSourceDiscoveryResult discovered = _discovery.Discover(paths);
        if (!discovered.Ok || discovered.SourceSet == null)
        {
            StatusText = discovered.Error;
            return false;
        }

        _sourceSet = discovered.SourceSet;
        Discs.Clear();
        foreach (VerificationDiscSource source in _sourceSet.Discs)
            Discs.Add(new VerifyDiscViewModel(source)
            {
                IsExpanded = _sourceSet.Discs.Count == 1
            });

        SourcePath = Discs.Count == 1
            ? Discs[0].Source.Path
            : $"{_sourceSet.RootPath}  ({Discs.Count} discs)";
        AlbumHeading = _sourceSet.DisplayName;
        Progress = 0;
        HasResult = false;
        _stopRequested = false;
        StatusText = Discs.Count == 1
            ? "Ready to verify one disc."
            : $"Ready to verify {Discs.Count} discs in order.";
        UpdateOverview();
        OnPropertyChanged(nameof(HasSource));
        RequeryHub.RequestRequery();
        return true;
    }

    internal async Task VerifyAllAsync()
    {
        if (!HasSource || IsBusy) return;

        VerifyDiscViewModel[] work = Discs.ToArray();
        foreach (VerifyDiscViewModel disc in work)
            disc.Reset();
        HasResult = false;
        _stopRequested = false;
        IsBusy = true;
        Progress = 0;
        UpdateOverview();

        int completed = 0;
        try
        {
            for (int index = 0; index < work.Length; index++)
            {
                if (_stopRequested) break;
                VerifyDiscViewModel disc = work[index];
                disc.Begin("Verifying against AccurateRip and CTDB...");
                StatusText = $"Verifying disc {index + 1} of {work.Length}: {disc.DisplayName}";
                UpdateOverview();

                VerifyFilesResult result = await RunServiceAsync(
                    disc,
                    repair: false,
                    index,
                    work.Length);
                disc.Apply(result);
                completed++;
                HasResult = true;
                PublishReport(repair: false, disc, result);
                UpdateOverview();
            }
        }
        finally
        {
            IsBusy = false;
            Progress = work.Length == 0 ? 0 : (double)completed / work.Length;
            if (_stopRequested && completed < work.Length)
                StatusText = $"Stopped after {completed} of {work.Length} discs. Completed results were kept.";
            else
                StatusText = BuildCompletionStatus();
            _stopRequested = false;
            UpdateOverview();
        }
    }

    internal async Task RepairDiscAsync(VerifyDiscViewModel? disc, bool confirm)
    {
        if (disc == null || !disc.CanRepair || IsBusy) return;
        if (confirm && !await ConfirmRepairAsync(disc)) return;

        _stopRequested = false;
        IsBusy = true;
        disc.Begin("Building and independently verifying a repaired copy...");
        StatusText = "Repairing " + disc.DisplayName + " from CTDB parity...";
        UpdateOverview();
        try
        {
            VerifyFilesResult result = await RunServiceAsync(disc, repair: true, 0, 1);
            if (result.Ok)
                disc.Apply(result);
            else
                disc.ApplyRepairFailure(result);
            HasResult = true;
            Progress = result.Ok ? 1 : Progress;
            StatusText = result.Ok ? result.Status : "Repair failed: " + result.Error;
            PublishReport(repair: true, disc, result);
        }
        finally
        {
            IsBusy = false;
            UpdateOverview();
        }
    }

    private async Task<VerifyFilesResult> RunServiceAsync(
        VerifyDiscViewModel disc,
        bool repair,
        int index,
        int count)
    {
        IUiDispatcher? dispatcher = _dispatcher;
        void Report(double fraction, string status)
        {
            void ApplyProgress()
            {
                double bounded = Math.Clamp(fraction, 0, 1);
                Progress = (index + bounded) / Math.Max(1, count);
                disc.UpdateStatus(status);
                StatusText = status;
            }

            if (dispatcher == null || dispatcher.CheckAccess())
                ApplyProgress();
            else
                dispatcher.Post(ApplyProgress);
        }

        try
        {
            return await Task.Run(() => repair
                ? _verify.Repair(disc.Source.Path, Report)
                : _verify.Verify(disc.Source.Path, Report));
        }
        catch (Exception ex)
        {
            return new VerifyFilesResult
            {
                Error = "The verification service stopped unexpectedly (" +
                    ex.GetType().Name + ").",
                Source = disc.Source.Path
            };
        }
    }

    private void RequestStop()
    {
        _stopRequested = true;
        StatusText = "Stopping after the current disc. Its completed evidence will be kept.";
        RequeryHub.RequestRequery();
    }

    private async Task<bool> ConfirmRepairAsync(VerifyDiscViewModel disc)
    {
        // No prompt service (headless) means no confirmation is possible, so
        // repair stays fail-closed rather than silently proceeding.
        if (_prompts == null) return false;
        return await _prompts.ConfirmOkCancelAsync(
            "Repair will build a new sibling folder for " + disc.DisplayName +
            ", independently verify the repaired audio, and publish it only if verification " +
            "succeeds. The selected source files will not be changed.\n\nProceed with repair?",
            "Create repaired copy from CTDB parity");
    }

    private void UpdateOverview()
    {
        int completed = Discs.Count(disc => disc.HasResult);
        int confirmed = Discs.Count(disc =>
            disc.DatabaseConfirmed && !disc.HasErrors && !disc.RepairApplied);
        int repairable = Discs.Count(disc => disc.CanRepair);
        int failed = Discs.Count(disc => disc.Failed);
        int repaired = Discs.Count(disc => disc.RepairApplied);
        int tracks = Discs.Where(disc => disc.HasResult).Sum(disc => disc.TrackCount);
        long duration = Discs.Where(disc => disc.HasResult).Sum(disc => disc.DurationSeconds);

        DiscCountText = Discs.Count == 1 ? "1 disc" : $"{Discs.Count} discs";
        TrackCountText = completed == 0 ? "tracks pending" : $"{tracks} tracks";
        DurationText = completed == 0 ? "duration pending" : FormatDuration(duration);

        if (IsBusy)
            OverviewHeadline = repairable > 0 ? "Repair in progress" : "Album verification in progress";
        else if (completed == 0)
            OverviewHeadline = Discs.Count == 1 ? "Ready to verify" : $"{Discs.Count}-disc set ready";
        else if (failed > 0)
            OverviewHeadline = failed == 1 ? "1 disc could not be verified" : $"{failed} discs could not be verified";
        else if (repairable > 0)
            OverviewHeadline = repairable == 1 ? "1 disc can be repaired from CTDB" : $"{repairable} discs can be repaired from CTDB";
        else if (repaired > 0)
            OverviewHeadline = repaired == 1 ? "Repaired copy verified" : $"{repaired} repaired copies verified";
        else if (confirmed == completed && completed == Discs.Count)
            OverviewHeadline = Discs.Count == 1 ? "Album verified" : $"All {Discs.Count} discs verified";
        else
            OverviewHeadline = "Verification complete - review the evidence";

        var facts = new List<string>();
        if (confirmed > 0) facts.Add($"{confirmed} database-confirmed");
        if (repairable > 0) facts.Add($"{repairable} repairable");
        if (repaired > 0) facts.Add($"{repaired} repaired");
        if (failed > 0) facts.Add($"{failed} failed");
        int unconfirmed = completed - confirmed - repairable - failed - repaired;
        if (unconfirmed > 0) facts.Add($"{unconfirmed} not database-confirmed");
        OverviewDetail = facts.Count == 0
            ? "CUE/playlist identity is fixed before verification starts."
            : string.Join(" | ", facts);

        string[] albums = Discs
            .Where(disc => disc.Result?.Ok == true && !string.IsNullOrWhiteSpace(disc.Result.Album))
            .Select(disc => disc.Result!.Album.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (albums.Length == 1)
        {
            string[] artists = Discs
                .Where(disc => disc.Result?.Ok == true && !string.IsNullOrWhiteSpace(disc.Result.Artist))
                .Select(disc => disc.Result!.Artist.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            AlbumHeading = artists.Length == 1 ? artists[0] + " - " + albums[0] : albums[0];
        }
    }

    private string BuildCompletionStatus()
    {
        int failed = Discs.Count(disc => disc.Failed);
        int repairable = Discs.Count(disc => disc.CanRepair);
        if (failed > 0)
            return $"Verification completed with {failed} failed disc(s). Review each card for details.";
        if (repairable > 0)
            return $"Verification found {repairable} disc(s) with CTDB-repairable damage.";
        return $"Verification complete for {Discs.Count} disc(s).";
    }

    private void PublishReport(bool repair, VerifyDiscViewModel disc, VerifyFilesResult result)
    {
        if (!result.Ok) return;
        _reports.Publish(new RipReport
        {
            Mode = repair ? "Repair" : "Verify",
            Album = string.IsNullOrWhiteSpace(result.Album)
                ? Path.GetFileNameWithoutExtension(disc.Source.Path)
                : result.Album,
            Artist = result.Artist,
            Year = result.Year,
            DriveName = "file verification: " + disc.Source.RelativePath,
            CorrectionQuality = 1,
            ArConfidence = result.ArConfidence,
            ArTotal = result.ArTotal,
            CtdbConfidence = result.CtdbConfidence,
            CtdbTotal = result.CtdbTotal,
            Accurate = result.Accurate,
            DamageRepairRequired = result.HasErrors,
            Status = result.Status,
            TrackCount = result.TrackCount,
            TocId = result.TocId,
            OutputDir = repair ? result.OutputPath : ""
        });
    }

    internal static string FormatDuration(long totalSeconds)
    {
        if (totalSeconds < 0) totalSeconds = 0;
        TimeSpan duration = TimeSpan.FromSeconds(totalSeconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes}:{duration.Seconds:00}";
    }
}

public sealed class VerifyDiscViewModel : ViewModelBase
{
    public VerifyDiscViewModel(VerificationDiscSource source)
    {
        Source = source;
        StatusText = "Ready";
    }

    public VerificationDiscSource Source { get; }
    public VerifyFilesResult? Result { get; private set; }
    public string DisplayName => Source.DisplayName;
    public string SourceCaption => Source.Kind switch
    {
        VerificationSourceKind.CueSheet => "CUE sheet | " + Source.RelativePath,
        VerificationSourceKind.Playlist => "Playlist | " + Source.RelativePath,
        _ => "Audio file | " + Source.RelativePath
    };

    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    private bool _hasResult;
    public bool HasResult { get => _hasResult; private set => Set(ref _hasResult, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public bool Failed => HasResult && Result?.Ok != true;
    public bool DatabaseConfirmed => Result?.Ok == true &&
        (Result.Accurate || Result.CtdbConfidence > 0);
    public bool HasErrors => Result?.HasErrors == true;
    public bool CanRepair => Result?.Ok == true && Result.HasErrors &&
        Result.CanRecover && !Result.RepairApplied;
    public bool RepairApplied => Result?.RepairApplied == true;
    public bool NotConfirmed => Result?.Ok == true && !DatabaseConfirmed && !CanRepair;

    public string OutcomeText => IsBusy ? "WORKING"
        : !HasResult ? "PENDING"
        : Failed ? "FAILED"
        : RepairApplied ? "REPAIRED + VERIFIED"
        : CanRepair ? "REPAIRABLE"
        : DatabaseConfirmed ? "DATABASE VERIFIED"
        : "NOT DATABASE-CONFIRMED";

    public string AlbumText
    {
        get
        {
            if (Result?.Ok != true || string.IsNullOrWhiteSpace(Result.Album))
                return DisplayName;
            return string.IsNullOrWhiteSpace(Result.Artist)
                ? Result.Album
                : Result.Artist + " - " + Result.Album;
        }
    }

    public string DiscIdentity
    {
        get
        {
            if (Result?.Ok != true) return DisplayName;
            string number = Result.DiscNumber;
            string total = Result.TotalDiscs;
            string identity = number.Length == 0
                ? DisplayName
                : total.Length > 0 && total != "1" ? $"Disc {number} of {total}" : $"Disc {number}";
            return string.IsNullOrWhiteSpace(Result.DiscName)
                ? identity
                : identity + " | " + Result.DiscName;
        }
    }

    // A failed lookup is not an absent disc. It is checked after the real answers so a
    // database that replied before failing still reports what it said.
    public string ArText => Result?.Ok != true ? "not checked"
        : Result.Accurate ? $"accurate | confidence {Result.ArConfidence}"
        : Result.ArTotal > 0 ? $"no match | {Result.ArConfidence}/{Result.ArTotal}"
        : Result.ArLookupFailed ? "lookup failed"
        : "not in database";

    public string CtdbText => Result?.Ok != true ? "not checked"
        : Result.CtdbConfidence > 0 ? $"verified | confidence {Result.CtdbConfidence}"
        : Result.CanRecover ? "damage found | parity available"
        : Result.CtdbTotal > 0 ? $"no exact match | {Result.CtdbTotal} entries"
        : Result.CtdbLookupFailed ? "lookup failed"
        : "not found";

    public int TrackCount => Result?.TrackCount ?? 0;
    public long DurationSeconds => Result?.DurationSeconds ?? 0;
    public string TrackCountText => Result?.Ok == true ? $"{Result.TrackCount} tracks" : "tracks pending";
    public string DurationText => Result?.Ok == true
        ? VerifyViewModel.FormatDuration(Result.DurationSeconds)
        : "duration pending";
    public string ReleaseText => JoinNonempty(Result?.Year, Result?.ReleaseDate, Result?.Country);
    public string LabelText => JoinNonempty(Result?.Label, Result?.CatalogNumber);
    public string GenreText => Result?.Genre ?? "";
    public string BarcodeText => Result?.Barcode ?? "";
    public string TocText => Result?.TocId ?? "";
    public bool HasRelease => ReleaseText.Length > 0;
    public bool HasLabel => LabelText.Length > 0;
    public bool HasGenre => GenreText.Length > 0;
    public bool HasBarcode => BarcodeText.Length > 0;
    public bool HasToc => TocText.Length > 0;
    public bool ShowRepairDetail => Result?.RepairSectors > 0 || RepairApplied;
    public string RepairSummary => Result == null ? ""
        : $"{Result.RepairSamples:N0} samples in {Result.RepairSectors:N0} sectors";
    public string RepairHeadroom => Result == null || Result.RepairStripeCapacity <= 0
        ? "parity headroom not reported"
        : $"worst stripe {Result.RepairWorstStripeErrors}/{Result.RepairStripeCapacity}";
    public string RepairRanges => Result?.RepairRanges ?? "";
    public string RepairOutput => Result?.OutputPath ?? "";
    public bool HasRepairOutput => RepairOutput.Length > 0;
    public IReadOnlyList<VerifyTrackPresentation> Tracks { get; private set; } =
        Array.Empty<VerifyTrackPresentation>();
    public string TrackEvidenceHeader => Result?.Ok == true
        ? $"TRACK EVIDENCE ({Tracks.Count})"
        : "TRACK EVIDENCE";

    internal void Reset()
    {
        Result = null;
        Tracks = Array.Empty<VerifyTrackPresentation>();
        IsBusy = false;
        HasResult = false;
        StatusText = "Ready";
        OnPropertyChanged(string.Empty);
    }

    internal void Begin(string status)
    {
        IsBusy = true;
        StatusText = status;
        OnPropertyChanged(nameof(OutcomeText));
    }

    internal void UpdateStatus(string status) => StatusText = status;

    internal void Apply(VerifyFilesResult result)
    {
        Result = result;
        Tracks = result.Tracks.Select(track => new VerifyTrackPresentation(track)).ToArray();
        IsBusy = false;
        HasResult = true;
        StatusText = result.Ok ? result.Status : result.Error;
        OnPropertyChanged(string.Empty);
        RequeryHub.RequestRequery();
    }

    internal void ApplyRepairFailure(VerifyFilesResult failure)
    {
        IsBusy = false;
        StatusText = "Repair failed: " + failure.Error +
            " The completed verification evidence was kept.";
        OnPropertyChanged(nameof(OutcomeText));
        RequeryHub.RequestRequery();
    }

    private static string JoinNonempty(params string?[] values)
        => string.Join(" | ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class VerifyTrackPresentation
{
    public VerifyTrackPresentation(VerifyTrackResult track)
    {
        Number = track.Number;
        Title = string.IsNullOrWhiteSpace(track.Title) ? $"Track {track.Number}" : track.Title;
        Artist = track.Artist;
        Crc32 = string.IsNullOrWhiteSpace(track.Crc32) ? "-" : track.Crc32;
        ArText = track.ArConfidence > 0
            ? track.ArConfidence.ToString()
            : track.ArTotal > 0 ? $"0/{track.ArTotal}" : "-";
        CtdbText = track.CtdbConfidence > 0 ? track.CtdbConfidence.ToString() : "-";
        Confirmed = track.ArConfidence > 0 || track.CtdbConfidence > 0;
    }

    public int Number { get; }
    public string Title { get; }
    public string Artist { get; }
    public string Crc32 { get; }
    public string ArText { get; }
    public string CtdbText { get; }
    public bool Confirmed { get; }
}
