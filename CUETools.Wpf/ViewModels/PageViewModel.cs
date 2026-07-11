using CUETools.Wpf.Mvvm;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Base for each destination in the left-nav shell. Phase 2 pages are stubs carrying just
/// a title/group/subtitle; Phase 3 fills them with the real Rip/Verify/Repair/Convert UI.
/// </summary>
public abstract class PageViewModel : ViewModelBase
{
    public string Title { get; protected init; } = "";
    public string Group { get; protected init; } = "";
    public string Subtitle { get; protected init; } = "";
}

public sealed class RipViewModel : PageViewModel
{
    public RipViewModel() { Title = "Rip"; Group = "Work"; Subtitle = "Rip a CD: read, encode, and verify against AccurateRip and CTDB."; }
}

public sealed class VerifyViewModel : PageViewModel
{
    public VerifyViewModel() { Title = "Verify & Repair"; Group = "Work"; Subtitle = "Check existing files against AccurateRip and CTDB, and repair from CTDB parity."; }
}

public sealed class ConvertViewModel : PageViewModel
{
    public ConvertViewModel() { Title = "Convert"; Group = "Work"; Subtitle = "Transcode existing files to another format, layout, or tagging."; }
}

public sealed class QueueViewModel : PageViewModel
{
    public QueueViewModel() { Title = "Queue"; Group = "Session"; Subtitle = "Process a stack of discs or jobs in one sitting."; }
}

public sealed class ReportViewModel : PageViewModel
{
    public ReportViewModel() { Title = "Report"; Group = "Session"; Subtitle = "The per-job accuracy log, with a tamper-evident checksum."; }
}

public sealed class DriveViewModel : PageViewModel
{
    public DriveViewModel() { Title = "Drive & Read"; Group = "Setup"; Subtitle = "Drive capabilities (cache defeat, overread, offset) and calibration."; }
}

public sealed class SettingsViewModel : PageViewModel
{
    public SettingsViewModel() { Title = "Settings"; Group = "Setup"; Subtitle = "Every engine option, grouped."; }
}
