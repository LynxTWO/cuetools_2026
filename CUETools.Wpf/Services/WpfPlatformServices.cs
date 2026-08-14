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
/// WPF UI-thread timers (DispatcherTimer) behind the app core's timer seam.
/// renderPriority maps to DispatcherPriority.Render, matching the telemetry
/// animation's historical priority.
/// </summary>
public sealed class WpfUiTimerFactory : IUiTimerFactory
{
    public IUiTimer Create(TimeSpan interval, bool renderPriority = false)
        => new WpfUiTimer(interval, renderPriority);

    private sealed class WpfUiTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public WpfUiTimer(TimeSpan interval, bool renderPriority)
        {
            _timer = renderPriority
                ? new DispatcherTimer(DispatcherPriority.Render)
                : new DispatcherTimer();
            _timer.Interval = interval;
            _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
        }

        public TimeSpan Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        public bool IsEnabled => _timer.IsEnabled;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        public event EventHandler? Tick;

        public void Dispose() => _timer.Stop();
    }
}

/// <summary>
/// WPF artwork previews: the exact frozen-BitmapImage decode the rip page has
/// always used (OnLoad caching, bounded DecodePixelWidth), now behind the app
/// core's preview seam.
/// </summary>
public sealed class WpfArtworkPreviewFactory : IArtworkPreviewFactory
{
    public object? CreatePreview(byte[] bytes, int decodeWidth)
    {
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.StreamSource = new System.IO.MemoryStream(bytes);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}

/// <summary>
/// WPF artwork transcoding: the exact decode (PreservePixelFormat, OnLoad,
/// Bgra32), JPEG passthrough-within-cap, RIOT-resampled resize, and Bgr24
/// JPEG encode the album-art service has always used.
/// </summary>
public sealed class WpfImageTranscoder : IImageTranscoder
{
    public bool CanDecode(byte[] bytes) => Decode(bytes) != null;

    public byte[]? ResizeToJpeg(byte[] source, int maxSize, int quality)
    {
        System.Windows.Media.Imaging.BitmapSource? src = Decode(source);
        if (src == null) return null;
        int w = src.PixelWidth;
        int h = src.PixelHeight;

        if (Math.Max(w, h) <= maxSize)
        {
            if (source.Length >= 2 && source[0] == 0xFF && source[1] == 0xD8)
                return source;
            return EncodeJpeg(src, quality);
        }

        double factor = (double)maxSize / Math.Max(w, h);
        int destinationWidth = Math.Max(1, (int)Math.Round(w * factor));
        int destinationHeight = Math.Max(1, (int)Math.Round(h * factor));
        return EncodeJpeg(
            CUETools.Wpf.Imaging.RiotResampler.Resize(src, destinationWidth, destinationHeight),
            quality);
    }

    private static System.Windows.Media.Imaging.BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new System.IO.MemoryStream(bytes);
            System.Windows.Media.Imaging.BitmapFrame frame =
                System.Windows.Media.Imaging.BitmapFrame.Create(
                    stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            return converted;
        }
        catch { return null; }
    }

    private static byte[] EncodeJpeg(
        System.Windows.Media.Imaging.BitmapSource source, int quality)
    {
        var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder
        {
            QualityLevel = Math.Clamp(quality, 1, 100)
        };
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
            new System.Windows.Media.Imaging.FormatConvertedBitmap(
                source, System.Windows.Media.PixelFormats.Bgr24, null, 0)));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

/// <summary>
/// WPF display capabilities: the 3D disc uses WPF's GPU-rasterized
/// Viewport3D, so hardware acceleration means render tier 1 or above.
/// </summary>
public sealed class WpfPlatformCapabilities : IPlatformCapabilities
{
    public bool HardwareAccelerated3D { get; } =
        (System.Windows.Media.RenderCapability.Tier >> 16) >= 1;
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
