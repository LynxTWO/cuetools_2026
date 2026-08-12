using System.Windows;
using System.Windows.Threading;

namespace CUETools.Wpf.Services;

/// <summary>WPF implementation of the app core's file/folder pickers. The
/// dialogs are modal and synchronous; completed tasks preserve that behavior
/// under the async seam exactly.</summary>
public sealed class WpfFileDialogService : IFileDialogService
{
    public Task<string[]?> PickFilesAsync(string title, bool multiselect, IReadOnlyList<FilePickerGroup> groups)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Multiselect = multiselect,
            Filter = BuildFilter(groups)
        };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileNames : null);
    }

    public Task<string?> PickFolderAsync(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
    }

    private static string BuildFilter(IReadOnlyList<FilePickerGroup> groups)
        => string.Join("|", groups.Select(group =>
        {
            string patterns = string.Join(";", group.Extensions.Select(
                extension => extension == "*" ? "*.*" : "*." + extension));
            return group.Name + "|" + patterns;
        }));
}

/// <summary>WPF implementation of the app core's confirmations.</summary>
public sealed class WpfUserPrompt : IUserPrompt
{
    public Task<bool> ConfirmOkCancelAsync(string message, string title)
        => Task.FromResult(MessageBox.Show(
            message,
            title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question) == MessageBoxResult.OK);
}

/// <summary>
/// WPF implementation of the app core's UI-thread marshaling seam. Reads
/// Application.Current lazily so behavior matches the historical
/// Application.Current?.Dispatcher pattern, including headless runs where no
/// Application exists and work applies inline.
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private static Dispatcher? Current => Application.Current?.Dispatcher;

    public bool CheckAccess() => Current?.CheckAccess() ?? true;

    public void Post(Action action)
    {
        Dispatcher? dispatcher = Current;
        if (dispatcher == null)
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}
