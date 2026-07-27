using System.Globalization;
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
        : this(drives, calibration, autoDetect: true)
    {
    }

    internal DriveViewModel(
        IDriveService drives,
        DriveCalibrationService calibration,
        bool autoDetect)
    {
        Title = "Drive & Read";
        Group = "Setup";
        Subtitle = "Everything this drive reports about itself. Detect reads it live over SCSI - no disc needed.";
        _drives = drives;
        _calibration = calibration;
        var d = drives.GetDrives();
        DriveLetter = d.Count > 0 ? d[0] + ":" : "no optical drive";
        DetectCommand = new RelayCommand(_ => { _ = DetectAsync(); }, _ => !_busy && !drives.RipInProgress);
        CalibrateCommand = new RelayCommand(_ => { _ = CalibrateAsync(); }, _ => !_busy && HasDetails && !drives.RipInProgress);
        if (autoDetect && d.Count > 0) _ = DetectAsync();   // populate on open so the page is never empty
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
    public DriveCalibration? Cal { get => _cal; private set { if (Set(ref _cal, value)) { OnPropertyChanged(nameof(HasCal)); OnPropertyChanged(nameof(CacheText)); OnPropertyChanged(nameof(CalMaxSpeedText)); OnPropertyChanged(nameof(CalMinSpeedText)); OnPropertyChanged(nameof(CalWhenText)); } } }
    public bool HasCal => _cal != null;
    public string CacheText => _cal == null ? "not calibrated" : $"{_cal.CacheDefeat}  ({_cal.CacheConfidence})";
    public string CalMaxSpeedText => _cal == null || _cal.MaxSpeedKbps <= 0 ? "--" : $"{_cal.MaxSpeedKbps} kB/s  (~{_cal.MaxSpeedKbps / 176}x)";
    public string CalMinSpeedText => _cal == null || _cal.MinSpeedKbps <= 0 ? "--" : $"{(_cal.MinSpeedKbps / 176.0).ToString("0.##", CultureInfo.InvariantCulture)}x ({_cal.MinSpeedKbps} kB/s)";
    public string CalWhenText => _cal == null ? "" : "calibrated " + _cal.CalibratedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public ICommand DetectCommand { get; }
    public ICommand CalibrateCommand { get; }

    /// <summary>The drive this page acts on: the one the user selected on the Rip page, falling back to
    /// the first drive when nothing has been selected yet. Both Detect and Calibrate used to hardcode
    /// GetDrives()[0], so on a two-drive machine you could calibrate drive 1 while ripping drive 2 - the
    /// rip's lookup by drive signature then found nothing, cache defeat was silently skipped, and this
    /// page showed drive 1's numbers under text that says "this drive".</summary>
    private char TargetDrive(System.Collections.Generic.IReadOnlyList<char> drives)
    {
        char sel = _drives.SelectedDrive;
        foreach (var c in drives) if (c == sel) return sel;
        return drives[0];
    }

    private async Task DetectAsync()
    {
        if (_busy || _drives.RipInProgress)
            return;
        var d = _drives.GetDrives();
        if (d.Count == 0) { Status = "No optical drive found."; return; }
        _busy = true;
        CommandManager.InvalidateRequerySuggested();
        Status = "Reading the drive over SCSI...";
        char drive = TargetDrive(d);
        DriveLetter = drive + ":";
        try
        {
            var det = await Task.Run(() => _drives.GetDriveDetails(drive));
            Details = det;
            if (det.Valid)
            {
                Status = "Read live from " + det.Model + " over SCSI"
                    + (det.OffsetKnown ? ". AccurateRip offset " + det.OffsetText + "." : ". AccurateRip offset not in the cached table.");
                // A corrupt calibration store is deliberately fail-closed so it cannot be silently
                // overwritten. That failure must still remain a normal UI state: do not let this
                // fire-and-forget detect task fault or leave the page permanently busy.
                try
                {
                    Cal = await Task.Run(() => _calibration.Get(det.ARName ?? ""));
                }
                catch (System.IO.InvalidDataException)
                {
                    Cal = null;
                    Status += " Saved calibration is unreadable and was not used.";
                }
            }
            else
            {
                Cal = null;
                Status = "Could not read the drive" + (det.Error.Length > 0 ? " (" + det.Error + ")." : ".");
            }
        }
        catch (System.Exception ex)
        {
            Details = null;
            Cal = null;
            Status = "Could not read the drive (" + ex.GetType().Name + ").";
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    internal Task DetectForTestAsync() => DetectAsync();
    internal bool IsBusyForTest => _busy;

    // Probe the drive's cache behaviour + speed and persist it. A disc must be loaded (the probe
    // reads real audio sectors). Read-only - it never writes rip output.
    private async Task CalibrateAsync()
    {
        if (_busy)
            return;
        var d = _drives.GetDrives();
        if (d.Count == 0) return;
        // Belt and braces with the CanExecute above: never probe a drive a rip holds open. The
        // failure would otherwise be reported as a missing disc, and following that advice means
        // ejecting mid-rip.
        if (_drives.RipInProgress) { Status = "A rip is running on this drive - calibration has to wait."; return; }
        char drive = TargetDrive(d);
        _busy = true;
        CommandManager.InvalidateRequerySuggested();
        Status = "Calibrating " + drive + ": (probing cache and speed - needs a disc)...";
        try
        {
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
        }
        catch (DriveCalibrationPersistenceException ex)
        {
            Status = ex.Message;
        }
        catch (System.Exception ex)
        {
            Status = "Calibration failed (" + ex.GetType().Name + ").";
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
