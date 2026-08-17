using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CUETools.Wpf.Models;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;
using CUETools.Processor;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Queue page. Process a stack of rips in one sitting: add album folders or .cue files, choose
/// Verify or Convert, and run them all. Each item runs on a background thread through the same
/// proven verify/convert services, updating its own status as it goes.
/// </summary>
public sealed class QueueViewModel : PageViewModel
{
    private readonly IVerifyService _verify;
    private readonly IConvertService _convert;
    private readonly EncoderCatalog _catalog;
    private readonly CUEConfig _config;

    public ObservableCollection<QueueItem> Items { get; } = new();
    public ObservableCollection<string> Actions { get; } = new() { "Verify", "Convert" };
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
    public string SelectedCodecLabel => SelectedCodecChoice == null
        ? SelectedFormat.ToUpperInvariant()
        : SelectedCodecChoice.CompactLabel + " - " +
          SelectedCodecChoice.Implementation;
    public string SelectedCodecTooltip => SelectedCodecChoice == null
        ? "Choose an output codec."
        : SelectedCodecChoice.AccessibleLabel + ". " +
          SelectedCodecChoice.HealthDetail;

    private readonly IFileDialogService? _dialogs;
    private readonly IUiDispatcher? _dispatcher;

    public QueueViewModel(
        IVerifyService verify,
        IConvertService convert,
        EncoderCatalog catalog,
        CUEConfig config,
        IFileDialogService? dialogs = null,
        IUiDispatcher? dispatcher = null)
    {
        _dialogs = dialogs;
        _dispatcher = dispatcher;
        Title = "Queue";
        Group = "Session";
        Subtitle = "Process a stack of discs or jobs in one sitting.";
        _verify = verify;
        _convert = convert;
        _catalog = catalog;
        _config = config;

        void RebuildFormats()
        {
            Formats.Clear();
            foreach (var f in convert.LosslessFormats()) Formats.Add(f);
            foreach (var f in convert.LossyFormats()) Formats.Add(f);   // lossy last
            CodecChoices.Clear();
            foreach (CodecChoice choice in catalog.BuildChoices(config))
                CodecChoices.Add(choice);
        }
        RebuildFormats();
        catalog.Changed += (_, _) =>
        {
            var keep = SelectedFormat;
            RebuildFormats();
            SelectedFormat = Formats.Contains(keep)
                ? keep
                : Formats.FirstOrDefault() ?? "flac";
            RefreshSelectedCodecChoice();
        };
        _selectedFormat = Formats.Contains("flac") ? "flac" : Formats.FirstOrDefault() ?? "flac";
        RefreshSelectedCodecChoice();

        AddFilesCommand = new RelayCommand(_ => AddFiles());
        AddFolderCommand = new RelayCommand(_ => AddFolder());
        BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
        ClearOutputCommand = new RelayCommand(
            _ => OutputDir = "",
            _ => !string.IsNullOrWhiteSpace(OutputDir));
        // Removing the row that is running would leave a job with no row to report into, so
        // only a waiting row can be removed. Adding during a batch is fine: the runner takes
        // the next waiting item each time round rather than a snapshot.
        RemoveCommand = new RelayCommand(
            o => { if (o is QueueItem i && !i.Running) Items.Remove(i); },
            o => o is QueueItem item && !item.Running);
        ClearCommand = new RelayCommand(_ => Items.Clear(), _ => Items.Count > 0 && !IsRunning);
        RunAllCommand = new RelayCommand(_ => { _ = RunAllAsync(); }, _ => Items.Count > 0 && !IsRunning);
        // Stops between items, the same promise Stop after disc makes on the Verify page: the
        // item in flight finishes and keeps its result, and everything still waiting stays
        // queued rather than being thrown away.
        StopCommand = new RelayCommand(
            _ =>
            {
                _stopRequested = true;
                StatusText = "Stopping after the current item. Its result will be kept.";
                // Nothing else changes an observable property here, so the button would stay
                // enabled until the batch ended without this.
                RequeryHub.RequestRequery();
            },
            _ => IsRunning && !_stopRequested);
        Items.CollectionChanged += (_, __) => RequeryHub.RequestRequery();
    }

    private string _selectedAction = "Verify";
    public string SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (Set(ref _selectedAction, value))
            {
                OnPropertyChanged(nameof(CodecPickerEnabled));
                OnPropertyChanged(nameof(OutputPickerEnabled));
            }
        }
    }

    private string _selectedFormat;
    public string SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (Set(ref _selectedFormat, value))
                RefreshSelectedCodecChoice();
        }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            OnPropertyChanged(nameof(CodecPickerEnabled));
            OnPropertyChanged(nameof(OutputPickerEnabled));
            RequeryHub.RequestRequery();
        }
    }
    /// <summary>Convert's own settings are editable: the action is Convert and no batch
    /// owns the queue. Both pickers read this rather than repeating the condition, so they
    /// cannot drift apart.</summary>
    private bool ConvertOptionsEnabled => !IsRunning &&
        string.Equals(SelectedAction, "Convert", StringComparison.Ordinal);
    public bool CodecPickerEnabled => ConvertOptionsEnabled;
    public bool OutputPickerEnabled => ConvertOptionsEnabled;

    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private string _statusText = "Add album folders or .cue files, then run the batch.";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public ICommand AddFilesCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RunAllCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand ClearOutputCommand { get; }

    private string _outputDir = "";
    /// <summary>
    /// Where converted files go. Empty means each album's own usual location, which is
    /// what the Queue did unconditionally before: it passed "" to Convert and ignored
    /// the Convert page's folder entirely, so a user who set one there watched the batch
    /// write somewhere else (F-14).
    ///
    /// Each item freezes this when it is added, so this property only steers what is
    /// queued next.
    /// </summary>
    public string OutputDir
    {
        get => _outputDir;
        set
        {
            if (!Set(ref _outputDir, value ?? "")) return;
            OnPropertyChanged(nameof(OutputLabel));
            RequeryHub.RequestRequery();
        }
    }

    /// <summary>What the page shows for the destination, never an empty box.</summary>
    public string OutputLabel => string.IsNullOrWhiteSpace(OutputDir)
        ? "Beside each album (default)"
        : OutputDir;

    private async void BrowseOutput()
    {
        if (_dialogs == null) return;
        string? folder = await _dialogs.PickFolderAsync("Choose where converted files go");
        if (folder != null) OutputDir = folder;
    }

    /// <summary>Set by Stop; read between items so the one in flight keeps its result.</summary>
    private bool _stopRequested;

    public void SelectCodec(CodecChoice choice)
    {
        _catalog.SelectCodec(_config, choice);
        _selectedFormat = choice.Extension;
        OnPropertyChanged(nameof(SelectedFormat));
        RefreshSelectedCodecChoice(choice.StableId);
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
            ?? CodecChoices.FirstOrDefault(choice => choice.CanSelect);
    }

    private static readonly FilePickerGroup[] AddFileGroups =
    {
        new("Rip sets (*.cue, *.m3u, *.m3u8)", new[] { "cue", "m3u", "m3u8" }),
        new("Audio with embedded cue", new[] { "flac", "wv", "ape", "tak", "m4a" }),
        new("All files", new[] { "*" }),
    };

    private async void AddFiles()
    {
        if (_dialogs == null) return;
        string[]? files = await _dialogs.PickFilesAsync(
            "Add rips to the queue", multiselect: true, AddFileGroups);
        if (files == null) return;
        foreach (var f in files) Enqueue(f);
    }

    private async void AddFolder()
    {
        if (_dialogs == null) return;
        string? folder = await _dialogs.PickFolderAsync("Add an album folder to the queue");
        if (folder == null) return;
        if (!EnqueueFolder(folder, out string error)) StatusText = error;
    }

    /// <summary>
    /// Resolve a folder to the manifests inside it and queue each disc. The engine cannot open
    /// a directory (CUESheet.Open throws "is a directory"), so queuing the folder path itself
    /// produced an item that always failed. A multi-disc folder becomes one item per disc,
    /// which is what a queue is for.
    /// </summary>
    private bool EnqueueFolder(string folder, out string error)
    {
        VerificationSourceDiscoveryResult found =
            new VerificationSourceDiscovery(_config).Discover(new[] { folder });
        if (!found.Ok)
        {
            error = found.Error;
            return false;
        }
        foreach (VerificationDiscSource disc in found.SourceSet!.Discs)
            Enqueue(disc.Path);
        error = "";
        return true;
    }

    /// <summary>Programmatic enqueue (startup arguments, tests): the same
    /// rules as the add dialogs. Returns false for a path that does not
    /// exist.</summary>
    public bool EnqueuePath(string source)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            (!System.IO.File.Exists(source) && !System.IO.Directory.Exists(source)))
        {
            return false;
        }

        // Same rule as the dialog: a directory is resolved to its manifests, because the
        // engine cannot open one.
        if (System.IO.Directory.Exists(source))
            return EnqueueFolder(source, out _);

        Enqueue(source);
        return true;
    }

    private void Enqueue(string source)
    {
        Items.Add(new QueueItem
        {
            Source = source,
            Action = _selectedAction,
            Format = _selectedAction == "Convert" ? _selectedFormat : "",
            CodecStableId = _selectedAction == "Convert"
                ? SelectedCodecChoice?.StableId ?? ""
                : "",
            // Frozen here rather than read at run time, the same rule CodecStableId
            // follows. Changing the folder mid-batch retargets what is added next and
            // cannot move work already queued somewhere the user has stopped looking.
            OutputDir = _selectedAction == "Convert" ? OutputDir : ""
        });
        StatusText = $"{Items.Count} item(s) queued.";
    }

    private async Task RunAllAsync()
    {
        if (Items.Count == 0 || IsRunning) return;
        IsRunning = true;
        _stopRequested = false;
        int done = 0;

        // Take the next item still waiting, rather than a snapshot of the list taken before
        // the batch started. A row added while the batch ran used to appear in the list, be
        // counted in neither the tally nor the bar, and sit at Pending after the batch
        // reported completion.
        while (true)
        {
            if (_stopRequested)
            {
                StatusText = $"Stopped after {done} item(s). The rest are still queued.";
                break;
            }
            QueueItem? item = Items.FirstOrDefault(candidate =>
                string.Equals(candidate.Status, "Pending", StringComparison.Ordinal));
            if (item == null) break;
            int total = done + Items.Count(candidate =>
                string.Equals(candidate.Status, "Pending", StringComparison.Ordinal));

            item.Status = "Running";
            item.Running = true;
            StatusText = $"[{done + 1}/{total}] {item.Action}: {item.Display}";

            void Report(double frac, string status)
            {
                void Apply() => Progress = (done + frac) / total;
                if (_dispatcher == null || _dispatcher.CheckAccess()) Apply();
                else _dispatcher.Post(Apply);
            }

            if (item.Action == "Convert")
            {
                CodecChoice? queuedChoice = _catalog.BuildChoices(_config)
                    .FirstOrDefault(choice => string.Equals(
                        choice.StableId,
                        item.CodecStableId,
                        StringComparison.OrdinalIgnoreCase));
                if (queuedChoice?.CanSelect != true)
                {
                    item.Status = "Failed";
                    item.Result = "The queued codec is no longer ready.";
                }
                else
                {
                    _catalog.SelectCodec(_config, queuedChoice);
                    var r = await Task.Run(() =>
                        _convert.Convert(item.Source, item.Format, item.OutputDir, Report));
                    item.Status = r.Ok ? "Done" : "Failed";
                    item.Result = r.Ok
                        ? $"{r.FileCount} {item.Format} file(s)"
                        : r.Error;
                }
            }
            else
            {
                var r = await Task.Run(() => _verify.Verify(item.Source, Report));
                // "No match" is a claim about the audio, so it may only be used when both
                // databases actually answered. A lookup that never completed says nothing.
                item.Status = r.Ok
                    ? r.Accurate || r.CtdbConfidence > 0 ? "Verified"
                        : r.CanRecover ? "Repairable"
                        : r.ArLookupFailed && r.CtdbLookupFailed ? "Lookup failed"
                        : "No match"
                    : "Failed";
                item.Result = r.Ok ? r.Status : r.Error;
            }

            item.Running = false;
            done++;
            Progress = total == 0 ? 1 : (double)done / total;
        }

        IsRunning = false;
        if (!_stopRequested)
            StatusText = $"Batch complete: {done} item(s) processed.";
        _stopRequested = false;
    }
}
