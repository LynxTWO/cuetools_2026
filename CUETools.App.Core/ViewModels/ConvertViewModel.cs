using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;
using CUETools.Processor;

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
    private readonly EncoderCatalog _catalog;
    private readonly CUEConfig _config;
    private readonly IFileDialogService? _dialogs;
    private readonly IUiDispatcher? _dispatcher;

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

    public ConvertViewModel(
        IConvertService convert,
        EncoderCatalog catalog,
        CUEConfig config,
        IFileDialogService? dialogs = null,
        IUiDispatcher? dispatcher = null)
    {
        Title = "Convert";
        Group = "Work";
        Subtitle = "Transcode existing files to another format, layout, or tagging.";
        _convert = convert;
        _catalog = catalog;
        _config = config;
        _dialogs = dialogs;
        _dispatcher = dispatcher;

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
        // an imported external encoder lights its format up without a restart
        catalog.Changed += (_, _) =>
        {
            var keep = SelectedFormat; RebuildFormats();
            SelectedFormat = Formats.Contains(keep) ? keep : Formats.FirstOrDefault() ?? "flac";
            RefreshSelectedCodecChoice();
            OnPropertyChanged(nameof(ScopeCodec));   // a type flip changes what the scope must draw
        };
        _selectedFormat = Formats.Contains("flac") ? "flac" : Formats.FirstOrDefault() ?? "flac";
        RefreshSelectedCodecChoice();

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
    public string SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (Set(ref _selectedFormat, value))
            {
                RefreshSelectedCodecChoice();
                OnPropertyChanged(nameof(ScopeCodec));
            }
        }
    }

    /// <summary>Scope remap for two-faced formats - same rule as the Rip page: the visualization
    /// must match the encoder that will actually run (WMA Lossless's predictor vs Standard's MDCT
    /// mask; imported AAC vs ALAC).</summary>
    public string ScopeCodec
    {
        get
        {
            string f = _selectedFormat;
            bool lossy = _convert.IsLossy(f);
            if (lossy && Controls.LossyMath.Info(f) == null) return f + "-lossy";
            if (!lossy && Controls.LossyMath.Info(f) != null) return f + "-lossless";
            return f;
        }
    }

    // the source codec (for the round-trip scope) - guessed from the extension, refined by the
    // real decode when the convert runs
    private string _sourceCodec = "flac";
    public string SourceCodec { get => _sourceCodec; private set => Set(ref _sourceCodec, value); }

    // a window of real decoded source PCM, fed to the ConvertScope during a convert
    private float[]? _sampleWindow;
    public float[]? SampleWindow { get => _sampleWindow; private set => Set(ref _sampleWindow, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                OnPropertyChanged(nameof(CodecUnlocked));
        }
    }
    public bool CodecUnlocked => !IsBusy;

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

    /// <summary>Programmatic source selection (startup path arguments, tests):
    /// the same path rules as the browse dialogs, with an optional explicit
    /// output directory. Returns false for a path that does not exist.</summary>
    public bool LoadSource(string path, string? outputDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            (!File.Exists(path) && !Directory.Exists(path)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(outputDirectory)) OutputDir = outputDirectory;
        SetSource(path);
        return true;
    }

    public void SelectCodec(CodecChoice choice)
    {
        _catalog.SelectCodec(_config, choice);
        _selectedFormat = choice.Extension;
        OnPropertyChanged(nameof(SelectedFormat));
        RefreshSelectedCodecChoice(choice.StableId);
        OnPropertyChanged(nameof(ScopeCodec));
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

    private static readonly FilePickerGroup[] BrowseFileGroups =
    {
        new("Rip sets", new[] { "cue", "m3u" }),
        new("Audio with embedded cue", new[] { "flac", "wv", "ape", "tak", "m4a" }),
        new("All files", new[] { "*" }),
    };

    private async void BrowseFile()
    {
        if (_dialogs == null) return;
        string[]? files = await _dialogs.PickFilesAsync(
            "Choose a rip to convert", multiselect: false, BrowseFileGroups);
        if (files is { Length: > 0 }) SetSource(files[0]);
    }

    private async void BrowseFolder()
    {
        if (_dialogs == null) return;
        string? folder = await _dialogs.PickFolderAsync("Choose an album folder to convert");
        if (folder != null) SetSource(folder);
    }

    private async void BrowseOutput()
    {
        if (_dialogs == null) return;
        string? folder = await _dialogs.PickFolderAsync("Choose an output folder");
        if (folder != null) OutputDir = folder;
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
        CodecChoice? choice = _catalog.GetSelectedChoice(_config, _selectedFormat);
        if (choice == null || !_catalog.GetHealth(choice.Encoder).IsReady)
        {
            StatusText = "Convert cannot start: choose a ready output codec.";
            return;
        }
        string path = _sourcePath, fmt = _selectedFormat, outDir = _outputDir;
        IsBusy = true;
        Progress = 0;
        HasResult = false;
        StatusText = $"Converting to {fmt}...";

        void Report(double frac, string status)
        {
            void Apply() { Progress = frac; StatusText = status; }
            if (_dispatcher == null || _dispatcher.CheckAccess()) Apply();
            else _dispatcher.Post(Apply);
        }

        // decode a real snippet of the source up front, then loop it through the round-trip scope
        // while the convert runs (no contention with the encoder, and it is the real source audio).
        // The feed is an async loop on the calling (UI) context rather than a toolkit timer, so
        // the same code drives WPF and Avalonia and stops itself when the convert ends.
        var preview = await Task.Run(() => _convert.PreloadSource(path));
        if (!string.IsNullOrEmpty(preview.SourceFormat)) SourceCodec = preview.SourceFormat;
        var windows = preview.Windows;
        bool feeding = windows.Count > 0;
        async void FeedScope()
        {
            for (int wi = 0; feeding; wi++)
            {
                SampleWindow = windows[wi % windows.Count];
                await Task.Delay(28);
            }
        }
        if (feeding) FeedScope();

        ConvertResult result = await Task.Run(() => _convert.Convert(path, fmt, outDir, Report));
        feeding = false;

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
