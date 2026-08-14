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

/// <summary>
/// A UI-thread timer behind a platform seam (WPF DispatcherTimer, Avalonia
/// DispatcherTimer). Created stopped; Tick runs on the UI thread.
/// </summary>
public interface IUiTimer : IDisposable
{
    TimeSpan Interval { get; set; }
    bool IsEnabled { get; }
    void Start();
    void Stop();
    event EventHandler? Tick;
}

/// <summary>Creates UI-thread timers for view models in the shared core.</summary>
public interface IUiTimerFactory
{
    /// <summary>
    /// renderPriority hints the platform to align ticks with frame rendering
    /// (the live telemetry animation); platforms without the concept ignore
    /// it.
    /// </summary>
    IUiTimer Create(TimeSpan interval, bool renderPriority = false);
}

/// <summary>
/// Decodes artwork bytes into the platform's display image type for
/// previews and thumbnails. Returns null when the bytes do not decode. The
/// result is object so the shared core stays toolkit-free; each head's view
/// binds its own image type. decodeWidth bounds decode memory (0 = full
/// size).
/// </summary>
public interface IArtworkPreviewFactory
{
    object? CreatePreview(byte[] bytes, int decodeWidth);
}

/// <summary>
/// Platform image transcoding for artwork bytes. The service keeps its own
/// byte-level dimension and size safety checks; the transcoder does only the
/// pixel work.
/// </summary>
public interface IImageTranscoder
{
    /// <summary>True when the bytes decode as a displayable image.</summary>
    bool CanDecode(byte[] bytes);

    /// <summary>
    /// Decode, cap the long edge to maxSize, and JPEG-encode at the given
    /// quality. A JPEG already within the cap returns the source bytes
    /// untranscoded. Null when the bytes cannot be decoded.
    /// </summary>
    byte[]? ResizeToJpeg(byte[] source, int maxSize, int quality);
}

/// <summary>
/// Static platform/display capabilities the shared core cannot probe itself.
/// </summary>
public interface IPlatformCapabilities
{
    /// <summary>True when the 3D disc visual has hardware rendering; false
    /// falls back to the 2D read map.</summary>
    bool HardwareAccelerated3D { get; }
}
