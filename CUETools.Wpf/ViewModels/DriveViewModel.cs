using System.Threading.Tasks;
using System.Windows.Input;
using CUETools.Wpf.Models;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>Drive & Read page. Shows real per-drive data (model + AccurateRip read offset)
/// and the read capabilities. Cache defeat / overread are still "planned" per the design
/// spec, and are labelled honestly rather than faked.</summary>
public sealed class DriveViewModel : PageViewModel
{
    private readonly IDriveService _drives;
    private bool _busy;

    public DriveViewModel(IDriveService drives)
    {
        Title = "Drive & Read";
        Group = "Setup";
        Subtitle = "What CUETools knows about this drive. Detect reads the model and offset from the disc.";
        _drives = drives;
        var d = drives.GetDrives();
        DriveLetter = d.Count > 0 ? d[0] + ":" : "no optical drive";
        DetectCommand = new RelayCommand(_ => { _ = DetectAsync(); }, _ => !_busy);
    }

    private string _driveLetter = "";
    public string DriveLetter { get => _driveLetter; private set => Set(ref _driveLetter, value); }

    private string _driveModel = "not detected";
    public string DriveModel { get => _driveModel; private set => Set(ref _driveModel, value); }

    private int _offset;
    public int Offset { get => _offset; private set { if (Set(ref _offset, value)) OnPropertyChanged(nameof(OffsetText)); } }

    private bool _detected;
    public bool Detected { get => _detected; private set { if (Set(ref _detected, value)) OnPropertyChanged(nameof(OffsetText)); } }

    public string OffsetText => _detected ? (Offset >= 0 ? "+" + Offset : Offset.ToString()) : "--";

    private string _status = "Insert a disc and click Detect to read this drive's model and offset.";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public ICommand DetectCommand { get; }

    private async Task DetectAsync()
    {
        var d = _drives.GetDrives();
        if (d.Count == 0) { Status = "No optical drive found."; return; }
        _busy = true;
        Status = "Detecting drive (reading the disc)...";
        var info = await Task.Run(() => _drives.ReadDisc(d[0]));
        if (info != null)
        {
            DriveModel = string.IsNullOrWhiteSpace(info.DriveName) ? "unknown" : info.DriveName;
            Offset = info.Offset;
            Detected = true;
            Status = $"Detected: {DriveModel}, AccurateRip read offset {(Offset >= 0 ? "+" : "")}{Offset}.";
        }
        else
        {
            Status = "No disc - insert an audio CD and Detect again.";
        }
        _busy = false;
    }
}
