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
                OnPropertyChanged(nameof(CodecPickerEnabled));
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
            RequeryHub.RequestRequery();
        }
    }
    public bool CodecPickerEnabled => !IsRunning &&
        string.Equals(SelectedAction, "Convert", StringComparison.Ordinal);

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
        if (folder != null) Enqueue(folder);
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
                : ""
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
                        _convert.Convert(item.Source, item.Format, "", Report));
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
