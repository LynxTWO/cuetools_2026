namespace CUETools.Wpf.Services;

/// <summary>One named group of pickable file extensions (no leading dots).</summary>
public sealed record FilePickerGroup(string Name, string[] Extensions);

/// <summary>
/// Platform file/folder pickers. Each head (WPF, Avalonia) supplies its own
/// implementation; view models never touch a toolkit dialog type directly.
/// Async because Avalonia's storage APIs are async-only; the WPF head wraps
/// its synchronous dialogs in completed tasks, preserving behavior exactly.
/// </summary>
public interface IFileDialogService
{
    /// <summary>Returns the chosen absolute paths, or null when cancelled.</summary>
    Task<string[]?> PickFilesAsync(string title, bool multiselect, IReadOnlyList<FilePickerGroup> groups);

    /// <summary>Returns the chosen folder, or null when cancelled.</summary>
    Task<string?> PickFolderAsync(string title);
}

/// <summary>User confirmations. Heads map this to their dialog stack.</summary>
public interface IUserPrompt
{
    /// <summary>OK/Cancel question; true only on an explicit OK.</summary>
    Task<bool> ConfirmOkCancelAsync(string message, string title);
}

/// <summary>
/// UI-thread marshaling seam. A null service (tests, headless) means progress
/// callbacks apply inline on the calling thread, matching the historical
/// behavior when no WPF Application existed.
/// </summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);
}
