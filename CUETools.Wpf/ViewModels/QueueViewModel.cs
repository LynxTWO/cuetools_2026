using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CUETools.Wpf.Models;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

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

    public ObservableCollection<QueueItem> Items { get; } = new();
    public ObservableCollection<string> Actions { get; } = new() { "Verify", "Convert" };
    public ObservableCollection<string> Formats { get; } = new();

    public QueueViewModel(IVerifyService verify, IConvertService convert, EncoderCatalog catalog)
    {
        Title = "Queue";
        Group = "Session";
        Subtitle = "Process a stack of discs or jobs in one sitting.";
        _verify = verify;
        _convert = convert;

        void RebuildFormats()
        {
            Formats.Clear();
            foreach (var f in convert.LosslessFormats()) Formats.Add(f);
            foreach (var f in convert.LossyFormats()) Formats.Add(f);   // lossy last
        }
        RebuildFormats();
        catalog.Changed += (_, _) => { var keep = SelectedFormat; RebuildFormats(); SelectedFormat = Formats.Contains(keep) ? keep : Formats.FirstOrDefault() ?? "flac"; };
        _selectedFormat = Formats.Contains("flac") ? "flac" : Formats.FirstOrDefault() ?? "flac";

        AddFilesCommand = new RelayCommand(_ => AddFiles());
        AddFolderCommand = new RelayCommand(_ => AddFolder());
        RemoveCommand = new RelayCommand(o => { if (o is QueueItem i) Items.Remove(i); }, _ => !IsRunning);
        ClearCommand = new RelayCommand(_ => Items.Clear(), _ => Items.Count > 0 && !IsRunning);
        RunAllCommand = new RelayCommand(_ => { _ = RunAllAsync(); }, _ => Items.Count > 0 && !IsRunning);
        Items.CollectionChanged += (_, __) => CommandManager.InvalidateRequerySuggested();
    }

    private string _selectedAction = "Verify";
    public string SelectedAction { get => _selectedAction; set => Set(ref _selectedAction, value); }

    private string _selectedFormat;
    public string SelectedFormat { get => _selectedFormat; set => Set(ref _selectedFormat, value); }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; private set { if (Set(ref _isRunning, value)) CommandManager.InvalidateRequerySuggested(); } }

    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private string _statusText = "Add album folders or .cue files, then run the batch.";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public ICommand AddFilesCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RunAllCommand { get; }

    private void AddFiles()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add rips to the queue",
            Multiselect = true,
            Filter = "Rip sets (*.cue, *.m3u)|*.cue;*.m3u|Audio with embedded cue|*.flac;*.wv;*.ape;*.tak;*.m4a|All files|*.*"
        };
        if (dlg.ShowDialog() == true)
            foreach (var f in dlg.FileNames) Enqueue(f);
    }

    private void AddFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Add an album folder to the queue" };
        if (dlg.ShowDialog() == true) Enqueue(dlg.FolderName);
    }

    private void Enqueue(string source)
    {
        Items.Add(new QueueItem
        {
            Source = source,
            Action = _selectedAction,
            Format = _selectedAction == "Convert" ? _selectedFormat : ""
        });
        StatusText = $"{Items.Count} item(s) queued.";
    }

    private async Task RunAllAsync()
    {
        if (Items.Count == 0 || IsRunning) return;
        IsRunning = true;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        int done = 0, total = Items.Count;

        foreach (var item in Items.ToList())
        {
            item.Status = "Running";
            item.Running = true;
            StatusText = $"[{done + 1}/{total}] {item.Action}: {item.Display}";

            void Report(double frac, string status)
                => dispatcher?.BeginInvoke(new Action(() => { Progress = (done + frac) / total; }));

            if (item.Action == "Convert")
            {
                var r = await Task.Run(() => _convert.Convert(item.Source, item.Format, "", Report));
                item.Status = r.Ok ? "Done" : "Failed";
                item.Result = r.Ok ? $"{r.FileCount} {item.Format} file(s)" : r.Error;
            }
            else
            {
                var r = await Task.Run(() => _verify.Verify(item.Source, Report));
                item.Status = r.Ok ? (r.Accurate || r.CtdbConfidence > 0 ? "Verified" : r.CanRecover ? "Repairable" : "No match") : "Failed";
                item.Result = r.Ok ? r.Status : r.Error;
            }

            item.Running = false;
            done++;
            Progress = (double)done / total;
        }

        IsRunning = false;
        StatusText = $"Batch complete: {done}/{total} processed.";
    }
}
