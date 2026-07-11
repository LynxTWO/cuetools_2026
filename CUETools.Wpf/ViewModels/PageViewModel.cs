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

// RipViewModel lives in its own file (RipViewModel.cs) - it is a real page now.

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

// DriveViewModel lives in its own file (DriveViewModel.cs) - it is a real page now.
// SettingsViewModel lives in its own file (SettingsViewModel.cs) - it is a real page now.
