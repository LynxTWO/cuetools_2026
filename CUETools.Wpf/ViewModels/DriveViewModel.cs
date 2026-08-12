using System.Globalization;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
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
    private bool _selectionRefreshPending;

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
        _drives.SelectedDriveChanged += OnSelectedDriveChanged;
        _drives.RipInProgressChanged += OnRipInProgressChanged;
        _calibration.CalibrationSaved += OnCalibrationSaved;
        var d = drives.GetDrives();
        foreach (char drive in d)
            Drives.Add(drive);
        _selectedDrive = d.Count > 0 ? TargetDrive(d) : '\0';
        DriveLetter = d.Count > 0 ? _selectedDrive + ":" : "no optical drive";
        DetectCommand = new RelayCommand(_ => { _ = DetectAsync(); }, _ => !_busy && !drives.RipInProgress);
        CalibrateCommand = new RelayCommand(_ => { _ = CalibrateAsync(); }, _ => !_busy && HasDetails && !drives.RipInProgress);
        if (autoDetect && d.Count > 0) _ = DetectAsync();   // populate on open so the page is never empty
    }

    private string _driveLetter = "";
    public string DriveLetter { get => _driveLetter; private set => Set(ref _driveLetter, value); }

    public ObservableCollection<char> Drives { get; } = new();

    private char _selectedDrive;
    public char SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (_selectedDrive == value)
                return;
            if (_drives.RipInProgress)
            {
                Status = "The active drive cannot be changed while a rip, verify, Test & Copy, or calibration is running.";
                OnPropertyChanged();
                return;
            }
            if (!Drives.Contains(value))
                return;
            Set(ref _selectedDrive, value);
            _drives.SelectedDrive = value;
        }
    }

    public bool IsDriveSelectionEnabled => !_busy && !_drives.RipInProgress;

    private DriveDetails? _details;
    public DriveDetails? Details { get => _details; private set { if (Set(ref _details, value)) OnPropertyChanged(nameof(HasDetails)); } }
    public bool HasDetails => _details != null && _details.Valid;

    private string _status = "Reading the drive...";
    public string Status { get => _status; private set => Set(ref _status, value); }

    // Per-drive calibration (persisted). Loaded on detect; refreshed by Calibrate (a disc needed).
    private DriveCalibration? _cal;
    public DriveCalibration? Cal { get => _cal; private set { if (Set(ref _cal, value)) { OnPropertyChanged(nameof(HasCal)); OnPropertyChanged(nameof(CacheText)); OnPropertyChanged(nameof(OverreadText)); OnPropertyChanged(nameof(CalMaxSpeedText)); OnPropertyChanged(nameof(CalMinSpeedText)); OnPropertyChanged(nameof(CalWhenText)); } } }
    public bool HasCal => _cal != null;
    public string CacheText => _cal == null ? "not calibrated" : $"{_cal.CacheDefeat}  ({_cal.CacheConfidence})";
    public string OverreadText => _cal == null
        ? "not calibrated"
        : !_cal.ReadOffsetKnown
            ? "offset unknown  .  safely disabled"
            : $"lead-in {(_cal.OverreadLeadIn ? "yes" : "no")}  .  lead-out {(_cal.OverreadLeadOut ? "yes" : "no")}";
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

    private void OnSelectedDriveChanged(object? sender, EventArgs e)
    {
        // Remove stale identity/calibration immediately. A fast drive switch must never leave H:'s
        // evidence visible under a session that is now operating on K:, even while the asynchronous
        // SCSI refresh waits behind another drive operation.
        var drives = _drives.GetDrives();
        if (drives.Count == 0)
            return;
        char selected = TargetDrive(drives);
        Set(ref _selectedDrive, selected, nameof(SelectedDrive));
        DriveLetter = selected + ":";
        Details = null;
        Cal = null;

        if (_busy || _drives.RipInProgress)
        {
            _selectionRefreshPending = true;
            Status = _drives.RipInProgress
                ? $"Drive {selected}: is in use. Details will refresh when the job finishes."
                : $"Switching to drive {selected}:...";
            return;
        }
        _ = DetectAsync();
    }

    private void OnRipInProgressChanged(object? sender, EventArgs e)
    {
        void Apply()
        {
            OnPropertyChanged(nameof(IsDriveSelectionEnabled));
            RequeryHub.RequestRequery();
            if (!_drives.RipInProgress && _selectionRefreshPending)
            {
                _selectionRefreshPending = false;
                _ = DetectAsync();
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            _ = dispatcher.BeginInvoke((Action)Apply);
        else
            Apply();
    }

    private void OnCalibrationSaved(DriveCalibration calibration)
    {
        // Calibrate runs on a worker thread. Marshal only the in-memory UI update; do not issue a
        // Detect here because the active rip owns the drive handle.
        void Apply()
        {
            if (Details?.ARName != null &&
                string.Equals(
                    Details.ARName.Trim(),
                    calibration.DriveSignature.Trim(),
                    StringComparison.Ordinal))
            {
                Cal = calibration;
                Status = $"Calibration saved for {DriveLetter}: cache {calibration.CacheDefeat}; " +
                    $"overread lead-in {(calibration.OverreadLeadIn ? "yes" : "no")}, " +
                    $"lead-out {(calibration.OverreadLeadOut ? "yes" : "no")}.";
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
            _ = dispatcher.BeginInvoke((Action)Apply);
        else
            Apply();
    }

    private async Task DetectAsync()
    {
        if (_busy || _drives.RipInProgress)
            return;
        var d = _drives.GetDrives();
        if (d.Count == 0) { Status = "No optical drive found."; return; }
        _busy = true;
        OnPropertyChanged(nameof(IsDriveSelectionEnabled));
        RequeryHub.RequestRequery();
        Status = "Reading the drive over SCSI...";
        char drive = TargetDrive(d);
        DriveLetter = drive + ":";
        try
        {
            var det = await Task.Run(() => _drives.GetDriveDetails(drive));
            if (TargetDrive(_drives.GetDrives()) != drive)
            {
                _selectionRefreshPending = true;
                return;
            }
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
                    var loadedCalibration =
                        await Task.Run(() => _calibration.Get(det.ARName ?? ""));
                    if (TargetDrive(_drives.GetDrives()) != drive)
                    {
                        _selectionRefreshPending = true;
                        return;
                    }
                    Cal = loadedCalibration;
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
            OnPropertyChanged(nameof(IsDriveSelectionEnabled));
            RequeryHub.RequestRequery();
            if (_selectionRefreshPending && !_drives.RipInProgress)
            {
                _selectionRefreshPending = false;
                _ = DetectAsync();
            }
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
        OnPropertyChanged(nameof(IsDriveSelectionEnabled));
        RequeryHub.RequestRequery();
        Status = "Calibrating " + drive + ": (probing cache, overread, and speed - needs a disc)...";
        try
        {
            var cal = await Task.Run(() => _calibration.Calibrate(drive));
            if (cal != null)
            {
                Cal = cal;
                Status = "Calibrated " + drive + ": cache " + cal.CacheDefeat
                    + $"; overread lead-in {(cal.OverreadLeadIn ? "yes" : "no")}, "
                    + $"lead-out {(cal.OverreadLeadOut ? "yes" : "no")}.";
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
            OnPropertyChanged(nameof(IsDriveSelectionEnabled));
            RequeryHub.RequestRequery();
        }
    }
}
