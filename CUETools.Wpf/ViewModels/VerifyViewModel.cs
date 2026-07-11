using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CUETools.Wpf.Models;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Verify &amp; Repair page. Point at an existing rip (a .cue, an album folder, or a file with
/// an embedded cue) and check it against AccurateRip + CTDB - the file-based twin of the disc
/// rip, proven on net8. When CTDB has enough parity to recover the errors, Repair rewrites the
/// affected sectors; that is guarded behind a confirmation because it rewrites audio in place.
/// </summary>
public sealed class VerifyViewModel : PageViewModel
{
    private readonly IVerifyService _verify;
    private readonly IReportStore _reports;

    public VerifyViewModel(IVerifyService verify, IReportStore reports)
    {
        Title = "Verify & Repair";
        Group = "Work";
        Subtitle = "Check existing files against AccurateRip and CTDB, and repair from CTDB parity.";
        _verify = verify;
        _reports = reports;

        BrowseFileCommand = new RelayCommand(_ => BrowseFile());
        BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
        VerifyCommand = new RelayCommand(_ => { _ = RunAsync(repair: false); }, _ => HasSource && !IsBusy);
        RepairCommand = new RelayCommand(_ => { _ = RunAsync(repair: true); }, _ => CanRepair && !IsBusy);
    }

    private string _sourcePath = "";
    public string SourcePath
    {
        get => _sourcePath;
        private set { if (Set(ref _sourcePath, value)) OnPropertyChanged(nameof(HasSource)); }
    }
    public bool HasSource => !string.IsNullOrEmpty(_sourcePath);

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    private double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    private string _statusText = "Choose a .cue, an album folder, or a file with an embedded cue.";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    private bool _hasResult;
    public bool HasResult { get => _hasResult; private set => Set(ref _hasResult, value); }

    private string _album = "";
    public string Album { get => _album; private set => Set(ref _album, value); }

    private string _arText = "not checked";
    public string ArText { get => _arText; private set => Set(ref _arText, value); }

    private string _ctdbText = "not checked";
    public string CtdbText { get => _ctdbText; private set => Set(ref _ctdbText, value); }

    private bool _accurate;
    public bool Accurate { get => _accurate; private set => Set(ref _accurate, value); }

    private bool _hasErrors;
    public bool HasErrors { get => _hasErrors; private set => Set(ref _hasErrors, value); }

    private bool _canRepair;
    public bool CanRepair { get => _canRepair; private set { if (Set(ref _canRepair, value)) CommandManager.InvalidateRequerySuggested(); } }

    public ICommand BrowseFileCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand VerifyCommand { get; }
    public ICommand RepairCommand { get; }

    private void BrowseFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a rip to verify",
            Filter = "Rip sets (*.cue, *.m3u)|*.cue;*.m3u|Audio with embedded cue|*.flac;*.wv;*.ape;*.tak;*.m4a|All files|*.*"
        };
        if (dlg.ShowDialog() == true) SetSource(dlg.FileName);
    }

    private void BrowseFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose an album folder to verify" };
        if (dlg.ShowDialog() == true) SetSource(dlg.FolderName);
    }

    private void SetSource(string path)
    {
        SourcePath = path;
        HasResult = false;
        CanRepair = false;
        StatusText = "Ready to verify: " + path;
    }

    private async Task RunAsync(bool repair)
    {
        if (!HasSource || IsBusy) return;
        if (repair && !ConfirmRepair()) return;

        string path = _sourcePath;
        IsBusy = true;
        Progress = 0;
        HasResult = false;
        StatusText = repair ? "Repairing from CTDB parity..." : "Verifying against AccurateRip + CTDB...";
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        void Report(double frac, string status)
            => dispatcher?.BeginInvoke(new Action(() => { Progress = frac; StatusText = status; }));

        VerifyFilesResult result = await Task.Run(() => repair ? _verify.Repair(path, Report) : _verify.Verify(path, Report));

        Progress = result.Ok ? 1 : Progress;
        if (result.Ok)
        {
            HasResult = true;
            Album = string.IsNullOrWhiteSpace(result.Album)
                ? Path.GetFileNameWithoutExtension(path)
                : (string.IsNullOrWhiteSpace(result.Artist) ? result.Album : $"{result.Artist} - {result.Album}");
            Accurate = result.Accurate;
            HasErrors = result.HasErrors;
            ArText = result.Accurate ? $"accurate (confidence {result.ArConfidence})"
                : result.ArTotal > 0 ? $"not accurate ({result.ArConfidence} / {result.ArTotal})" : "not in database";
            CtdbText = result.CtdbConfidence > 0 ? $"verified (confidence {result.CtdbConfidence})"
                : result.CanRecover ? "errors found - repairable from parity" : "not found";
            CanRepair = result.HasErrors && result.CanRecover;
            StatusText = result.Status;
            PublishReport(repair, path, result);
        }
        else
        {
            StatusText = (repair ? "Repair failed: " : "Verify failed: ") + result.Error;
        }
        IsBusy = false;
    }

    private bool ConfirmRepair()
    {
        return MessageBox.Show(
            "Repair rewrites the affected audio using CTDB parity, replacing the source files. " +
            "Back up the rip first if you want to keep the original.\n\nProceed with repair?",
            "Repair from CTDB parity",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    private void PublishReport(bool repair, string path, VerifyFilesResult result)
    {
        _reports.Publish(new RipReport
        {
            Mode = repair ? "Repair" : "Verify",
            Album = string.IsNullOrWhiteSpace(result.Album) ? Path.GetFileNameWithoutExtension(path) : result.Album,
            Artist = result.Artist,
            DriveName = "files: " + path,
            CorrectionQuality = 1,
            ArConfidence = result.ArConfidence,
            ArTotal = result.ArTotal,
            CtdbConfidence = result.CtdbConfidence,
            CtdbTotal = result.CtdbTotal,
            Accurate = result.Accurate,
            Status = result.Status,
            TrackCount = result.TrackCount
        });
    }
}
