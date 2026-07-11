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

    // So the nav list items report their page name (not the VM type name) to UI automation /
    // screen readers.
    public override string ToString() => Title;
}

// RipViewModel lives in its own file (RipViewModel.cs) - it is a real page now.

// VerifyViewModel lives in its own file (VerifyViewModel.cs) - it is a real page now.

// ConvertViewModel lives in its own file (ConvertViewModel.cs) - it is a real page now.

// QueueViewModel lives in its own file (QueueViewModel.cs) - it is a real page now.
// ReportViewModel lives in its own file (ReportViewModel.cs) - it is a real page now.
// DriveViewModel lives in its own file (DriveViewModel.cs) - it is a real page now.
// SettingsViewModel lives in its own file (SettingsViewModel.cs) - it is a real page now.
