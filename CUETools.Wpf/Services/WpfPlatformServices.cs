using System.Windows;
using System.Windows.Threading;

namespace CUETools.Wpf.Services;

/// <summary>WPF implementation of the app core's file/folder pickers.</summary>
public sealed class WpfFileDialogService : IFileDialogService
{
    public string[]? PickFiles(string title, bool multiselect, IReadOnlyList<FilePickerGroup> groups)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Multiselect = multiselect,
            Filter = BuildFilter(groups)
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string BuildFilter(IReadOnlyList<FilePickerGroup> groups)
        => string.Join("|", groups.Select(group =>
        {
            string patterns = string.Join(";", group.Extensions.Select(
                extension => extension == "*" ? "*.*" : "*." + extension));
            return group.Name + "|" + patterns;
        }));
}

/// <summary>WPF implementation of the app core's blocking confirmations.</summary>
public sealed class WpfUserPrompt : IUserPrompt
{
    public bool ConfirmOkCancel(string message, string title)
        => MessageBox.Show(
            message,
            title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question) == MessageBoxResult.OK;
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
