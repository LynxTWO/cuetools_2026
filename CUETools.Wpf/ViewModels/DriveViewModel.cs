using System.Threading.Tasks;
using System.Windows.Input;
using CUETools.Wpf.Accuracy;
using CUETools.Wpf.Models;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>Drive &amp; Read page - the full readout of everything the drive tells us about itself:
/// identity (vendor/model/firmware from INQUIRY), capabilities and supported media (GET
/// CONFIGURATION), speeds (GET PERFORMANCE), the AccurateRip read offset, and the live feature
/// list. All read straight from the drive with no disc required; nothing here is hardcoded.</summary>
public sealed class DriveViewModel : PageViewModel
{
    private readonly IDriveService _drives;
    private readonly DriveCalibrationService _calibration;
    private bool _busy;

    public DriveViewModel(IDriveService drives, DriveCalibrationService calibration)
    {
        Title = "Drive & Read";
        Group = "Setup";
        Subtitle = "Everything this drive reports about itself. Detect reads it live over SCSI - no disc needed.";
        _drives = drives;
        _calibration = calibration;
        var d = drives.GetDrives();
        DriveLetter = d.Count > 0 ? d[0] + ":" : "no optical drive";
        DetectCommand = new RelayCommand(_ => { _ = DetectAsync(); }, _ => !_busy);
        CalibrateCommand = new RelayCommand(_ => { _ = CalibrateAsync(); }, _ => !_busy && HasDetails);
        if (d.Count > 0) _ = DetectAsync();   // populate on open so the page is never empty
    }

    private string _driveLetter = "";
    public string DriveLetter { get => _driveLetter; private set => Set(ref _driveLetter, value); }

    private DriveDetails? _details;
    public DriveDetails? Details { get => _details; private set { if (Set(ref _details, value)) OnPropertyChanged(nameof(HasDetails)); } }
    public bool HasDetails => _details != null && _details.Valid;

    private string _status = "Reading the drive...";
    public string Status { get => _status; private set => Set(ref _status, value); }

    // Per-drive calibration (persisted). Loaded on detect; refreshed by Calibrate (a disc needed).
    private DriveCalibration? _cal;
    public DriveCalibration? Cal { get => _cal; private set { if (Set(ref _cal, value)) { OnPropertyChanged(nameof(HasCal)); OnPropertyChanged(nameof(CacheText)); OnPropertyChanged(nameof(CalMaxSpeedText)); OnPropertyChanged(nameof(CalWhenText)); } } }
    public bool HasCal => _cal != null;
    public string CacheText => _cal == null ? "not calibrated" : $"{_cal.CacheDefeat}  ({_cal.CacheConfidence})";
    public string CalMaxSpeedText => _cal == null || _cal.MaxSpeedKbps <= 0 ? "--" : $"{_cal.MaxSpeedKbps} kB/s  (~{_cal.MaxSpeedKbps / 176}x)";
    public string CalWhenText => _cal == null ? "" : "calibrated " + _cal.CalibratedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public ICommand DetectCommand { get; }
    public ICommand CalibrateCommand { get; }

    private async Task DetectAsync()
    {
        var d = _drives.GetDrives();
        if (d.Count == 0) { Status = "No optical drive found."; return; }
        _busy = true;
        Status = "Reading the drive over SCSI...";
        char drive = d[0];
        DriveLetter = drive + ":";
        var det = await Task.Run(() => _drives.GetDriveDetails(drive));
        Details = det;
        if (det.Valid)
        {
            Status = "Read live from " + det.Model + " over SCSI"
                + (det.OffsetKnown ? ". AccurateRip offset " + det.OffsetText + "." : ". AccurateRip offset not in the cached table.");
            // show any saved calibration for this drive (signature = AR name)
            Cal = await Task.Run(() => _calibration.Get(det.ARName ?? ""));
        }
        else
        {
            Status = "Could not read the drive" + (det.Error.Length > 0 ? " (" + det.Error + ")." : ".");
        }
        _busy = false;
    }

    // Probe the drive's cache behaviour + speed and persist it. A disc must be loaded (the probe
    // reads real audio sectors). Read-only - it never writes rip output.
    private async Task CalibrateAsync()
    {
        var d = _drives.GetDrives();
        if (d.Count == 0) return;
        char drive = d[0];
        _busy = true;
        Status = "Calibrating " + drive + ": (probing cache and speed - needs a disc)...";
        var cal = await Task.Run(() => _calibration.Calibrate(drive));
        if (cal != null)
        {
            Cal = cal;
            Status = "Calibrated " + drive + ":  cache " + cal.CacheDefeat + ".";
        }
        else
        {
            Status = "Calibration needs an audio disc in the drive. Insert one and try again.";
        }
        _busy = false;
    }
}
