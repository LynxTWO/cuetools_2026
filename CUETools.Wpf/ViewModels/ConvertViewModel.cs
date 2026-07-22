using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Convert page. Transcode an existing rip (a .cue, an album folder, or a file with an embedded
/// cue) to another lossless format and layout - the file-source twin of the disc encode, proven
/// on net8. Output formats are data-driven: only those with a working encoder in this build are
/// offered (FLAC via the managed Flake plugin, WAV built-in, plus any dropped-in codec).
/// </summary>
public sealed class ConvertViewModel : PageViewModel
{
    private readonly IConvertService _convert;

    public ObservableCollection<string> Formats { get; } = new();

    public ConvertViewModel(IConvertService convert)
    {
        Title = "Convert";
        Group = "Work";
        Subtitle = "Transcode existing files to another format, layout, or tagging.";
        _convert = convert;

        foreach (var f in convert.LosslessFormats()) Formats.Add(f);
        foreach (var f in convert.LossyFormats()) Formats.Add(f);   // lossy last, e.g. mp3 (bundled libmp3lame)
        _selectedFormat = Formats.Contains("flac") ? "flac" : Formats.FirstOrDefault() ?? "flac";

        BrowseFileCommand = new RelayCommand(_ => BrowseFile());
        BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
        BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
        ConvertCommand = new RelayCommand(_ => { _ = RunAsync(); }, _ => HasSource && !IsBusy);
    }

    private string _sourcePath = "";
    public string SourcePath
    {
        get => _sourcePath;
        private set { if (Set(ref _sourcePath, value)) OnPropertyChanged(nameof(HasSource)); }
    }
    public bool HasSource => !string.IsNullOrEmpty(_sourcePath);

    private string _outputDir = "";
    public string OutputDir { get => _outputDir; private set => Set(ref _outputDir, value); }

    private string _selectedFormat;
    public string SelectedFormat { get => _selectedFormat; set => Set(ref _selectedFormat, value); }

    // the source codec (for the round-trip scope) - guessed from the extension, refined by the
    // real decode when the convert runs
    private string _sourceCodec = "flac";
    public string SourceCodec { get => _sourceCodec; private set => Set(ref _sourceCodec, value); }

    // a window of real decoded source PCM, fed to the ConvertScope during a convert
    private float[]? _sampleWindow;
    public float[]? SampleWindow { get => _sampleWindow; private set => Set(ref _sampleWindow, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private string _statusText = "Choose a source, pick a format, and convert.";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private bool _hasResult;
    public bool HasResult { get => _hasResult; private set => Set(ref _hasResult, value); }

    private string _resultText = "";
    public string ResultText { get => _resultText; private set => Set(ref _resultText, value); }

    public ICommand BrowseFileCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand BrowseOutputCommand { get; }
    public ICommand ConvertCommand { get; }

    private void BrowseFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a rip to convert",
            Filter = "Rip sets (*.cue, *.m3u)|*.cue;*.m3u|Audio with embedded cue|*.flac;*.wv;*.ape;*.tak;*.m4a|All files|*.*"
        };
        if (dlg.ShowDialog() == true) SetSource(dlg.FileName);
    }

    private void BrowseFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose an album folder to convert" };
        if (dlg.ShowDialog() == true) SetSource(dlg.FolderName);
    }

    private void BrowseOutput()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose an output folder" };
        if (dlg.ShowDialog() == true) OutputDir = dlg.FolderName;
    }

    private void SetSource(string path)
    {
        SourcePath = path;
        HasResult = false;
        SourceCodec = GuessFormat(path);
        StatusText = "Ready to convert: " + path;
    }

    // the source format for the round-trip label, before we decode it for real
    private static string GuessFormat(string path)
    {
        string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext.Length > 0 && ext != "cue" && ext != "m3u" ? ext : "flac";
    }

    private async Task RunAsync()
    {
        if (!HasSource || IsBusy) return;
        string path = _sourcePath, fmt = _selectedFormat, outDir = _outputDir;
        IsBusy = true;
        Progress = 0;
        HasResult = false;
        StatusText = $"Converting to {fmt}...";
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        void Report(double frac, string status)
            => dispatcher?.BeginInvoke(new Action(() => { Progress = frac; StatusText = status; }));

        // decode a real snippet of the source up front, then loop it through the round-trip scope
        // while the convert runs (no contention with the encoder, and it is the real source audio)
        var preview = await Task.Run(() => _convert.PreloadSource(path));
        if (!string.IsNullOrEmpty(preview.SourceFormat)) SourceCodec = preview.SourceFormat;
        var windows = preview.Windows;
        DispatcherTimer? feed = null;
        if (windows.Count > 0)
        {
            int wi = 0;
            feed = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(28) };
            feed.Tick += (_, _) => { SampleWindow = windows[wi % windows.Count]; wi++; };
            feed.Start();
        }

        ConvertResult result = await Task.Run(() => _convert.Convert(path, fmt, outDir, Report));
        feed?.Stop();

        Progress = result.Ok ? 1 : Progress;
        if (result.Ok)
        {
            HasResult = true;
            ResultText = $"Wrote {result.FileCount} {fmt} file(s) to {result.OutputDir}";
            StatusText = result.Status;
        }
        else
        {
            StatusText = "Convert failed: " + result.Error;
        }
        IsBusy = false;
    }
}
