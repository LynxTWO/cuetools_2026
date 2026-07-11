using System.Threading.Tasks;
using System.Windows.Input;
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
    private bool _busy;

    public DriveViewModel(IDriveService drives)
    {
        Title = "Drive & Read";
        Group = "Setup";
        Subtitle = "Everything this drive reports about itself. Detect reads it live over SCSI - no disc needed.";
        _drives = drives;
        var d = drives.GetDrives();
        DriveLetter = d.Count > 0 ? d[0] + ":" : "no optical drive";
        DetectCommand = new RelayCommand(_ => { _ = DetectAsync(); }, _ => !_busy);
        if (d.Count > 0) _ = DetectAsync();   // populate on open so the page is never empty
    }

    private string _driveLetter = "";
    public string DriveLetter { get => _driveLetter; private set => Set(ref _driveLetter, value); }

    private DriveDetails? _details;
    public DriveDetails? Details { get => _details; private set { if (Set(ref _details, value)) OnPropertyChanged(nameof(HasDetails)); } }
    public bool HasDetails => _details != null && _details.Valid;

    private string _status = "Reading the drive...";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public ICommand DetectCommand { get; }

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
        }
        else
        {
            Status = "Could not read the drive" + (det.Error.Length > 0 ? " (" + det.Error + ")." : ".");
        }
        _busy = false;
    }
}
