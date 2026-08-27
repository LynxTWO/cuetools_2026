using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CUETools.Wpf.Services;
using CUETools.Wpf.Services.Artwork;
using CUETools.Wpf.ViewModels;
using CUETools.Wpf.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// An on-screen evidence capture, not a unit test. It shows the artwork browser and the codec
/// picker against synthetic data - no disc, no network, no service graph - and reads the real
/// pixels off the screen, so the archive shows what the app actually draws at a given display
/// scale and theme. It runs only when CUETOOLS_SELECTOR_CAPTURE_DIR names an output folder;
/// eng/evidence/Run-SelectorSweep.ps1 sets that and drives the display scale. Without the
/// variable it is Inconclusive, so the ordinary suite never opens a window.
/// </summary>
[TestClass]
public sealed class SelectorCaptureTests
{
    private const string CaptureDirVariable = "CUETOOLS_SELECTOR_CAPTURE_DIR";

    [TestMethod]
    public void CaptureTheArtworkBrowserAndCodecPickerOnScreen()
    {
        string? dir = Environment.GetEnvironmentVariable(CaptureDirVariable);
        if (string.IsNullOrWhiteSpace(dir))
            Assert.Inconclusive(
                "Set " + CaptureDirVariable + " to a folder to capture the selector windows on screen.");
        Directory.CreateDirectory(dir);

        var written = new List<string>();
        RunSta(() =>
        {
            // One Application per process: it owns the pack: scheme, the merged theme, and the
            // dispatcher every window below runs on. The test filter keeps this method alone in
            // the process, so nothing else contends for it.
            _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
            Application app = Application.Current
                ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/CUETools.Wpf;component/Theme/Theme.xaml",
                    UriKind.Absolute),
            });

            foreach (AppTheme theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Swap(app.Resources, theme);
                written.Add(CaptureBrowser(dir, theme));
                written.Add(CaptureCodecPicker(dir, theme));
            }
        });

        foreach (string path in written)
            Assert.IsTrue(new FileInfo(path).Length > 1024, "capture is suspiciously small: " + path);
    }

    private static string CaptureBrowser(string dir, AppTheme theme)
    {
        var host = new FakeArtworkHost();
        var window = new ArtworkBrowserWindow(host, new StubArtService(host))
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 40,
            Top = 40,
            Width = 1040,
            Height = 700,
            Topmost = true,
        };
        window.Show();
        Pump(TimeSpan.FromMilliseconds(600));
        // Thumbnails decode asynchronously; the capture must show them, not their placeholders.
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline &&
               window.Rows.Any(row => row.LoadStatus == "loading" || row.Thumbnail == null && row.LoadStatus == ""))
            Pump(TimeSpan.FromMilliseconds(100));
        Pump(TimeSpan.FromMilliseconds(500));

        string path = Save(window, dir, theme, "artwork-browser");
        window.Close();
        Pump(TimeSpan.FromMilliseconds(200));
        return path;
    }

    private static string CaptureCodecPicker(string dir, AppTheme theme)
    {
        var window = new CodecPickerWindow(SyntheticChoices(), "flac:libflac")
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 40,
            Top = 40,
            Topmost = true,
        };
        window.Show();
        Pump(TimeSpan.FromMilliseconds(900));

        string path = Save(window, dir, theme, "codec-picker");
        window.Close();
        Pump(TimeSpan.FromMilliseconds(200));
        return path;
    }

    // ---- what the windows are shown against -------------------------------------------------

    private sealed class FakeArtworkHost : IArtworkBrowserHost
    {
        public string AlbumTitle => "The Complete Columbia Album Collection";
        public string AlbumArtist => "Johnny Cash";
        public ObservableCollection<ArtworkCandidate> ArtworkCandidates { get; } = new(SyntheticCandidates());
        public ArtworkCandidate? SelectedArtwork { get; private set; }

        public FakeArtworkHost() { SelectedArtwork = ArtworkCandidates[0]; }
        public Task SelectArtworkAsync(ArtworkCandidate candidate) { SelectedArtwork = candidate; return Task.CompletedTask; }
        public void ChooseNoArtwork() => SelectedArtwork = null;
        public void RefreshArtwork() { }
        public Task ImportLocalArtworkAsync(string path) => Task.CompletedTask;
    }

    private static IEnumerable<ArtworkCandidate> SyntheticCandidates()
    {
        ArtworkCandidate Make(
            string id, string provider, ArtworkMatchTier tier, ArtworkProviderConfidence confidence,
            string why, int size, long bytes, bool front = true, bool approved = true,
            bool primary = false, bool watermarked = false, bool automatic = true, string type = "Front")
            => new()
            {
                CandidateId = id,
                Provider = provider,
                ProviderItemId = id,
                ThumbnailUri = new Uri("https://example.invalid/thumb/" + id),
                OriginalUri = new Uri("https://example.invalid/full/" + id),
                MatchTier = tier,
                ProviderConfidence = confidence,
                MatchReason = why,
                Width = size,
                Height = size,
                ByteLength = bytes,
                MimeType = "image/jpeg",
                IsFront = front,
                IsApproved = approved,
                IsPrimary = primary,
                IsWatermarked = watermarked,
                AutomaticEligible = automatic,
                ProviderOrder = 0,
                InfoUri = new Uri("https://example.invalid/info/" + id),
                ArtworkType = type,
            };

        yield return Make("caa-front", "Cover Art Archive", ArtworkMatchTier.ExactRelease,
            ArtworkProviderConfidence.CoverArtArchiveApproved, "Exact release, approved front", 1400, 381_000,
            primary: true);
        yield return Make("ctdb-primary", "CTDB", ArtworkMatchTier.ExactRelease,
            ArtworkProviderConfidence.CtdbPrimary, "CTDB primary cover for this TOC", 600, 95_400);
        yield return Make("caa-unapproved", "Cover Art Archive", ArtworkMatchTier.MetadataRelease,
            ArtworkProviderConfidence.CoverArtArchiveUnapproved, "Selected release, not yet approved", 1000, 212_000,
            approved: false);
        yield return Make("caa-back", "Cover Art Archive", ArtworkMatchTier.ExactRelease,
            ArtworkProviderConfidence.CoverArtArchiveApproved, "Exact release, back cover", 1400, 355_000,
            front: false, automatic: false, type: "Back");
        yield return Make("tadb-group", "TheAudioDB", ArtworkMatchTier.ReleaseGroup,
            ArtworkProviderConfidence.TheAudioDbReleaseGroup, "Release group match, text fallback", 500, 64_000,
            automatic: false);
        yield return Make("text-weak", "Text search", ArtworkMatchTier.WeakText,
            ArtworkProviderConfidence.TextSearch, "Weak text match, watermarked", 300, 22_000,
            approved: false, watermarked: true, automatic: false);
    }

    private sealed class StubArtService : IAlbumArtService
    {
        private readonly FakeArtworkHost _host;
        public StubArtService(FakeArtworkHost host) { _host = host; }

        public Task<IReadOnlyList<ArtworkCandidate>> FindCandidatesAsync(ArtworkQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ArtworkCandidate>>(_host.ArtworkCandidates.ToList());

        public Task<AlbumArt?> DownloadAsync(ArtworkCandidate candidate, bool thumbnail, CancellationToken ct = default)
        {
            int size = thumbnail ? 240 : candidate.Width ?? 600;
            int hue = Math.Abs(candidate.CandidateId.GetHashCode()) % 360;
            byte[] jpeg = SyntheticJpeg(size, hue, candidate.Provider);
            return Task.FromResult<AlbumArt?>(new AlbumArt(candidate, jpeg, size, size));
        }

        public AlbumArt ImportLocalFile(string path, int maxSize, int quality = 92) => throw new NotSupportedException();
        public byte[]? ResizeToJpeg(byte[] source, int maxSize, int quality = 92) => throw new NotSupportedException();
    }

    private static byte[] SyntheticJpeg(int size, int hue, string label)
    {
        Color a = Hsv(hue, 0.55, 0.55), b = Hsv((hue + 40) % 360, 0.65, 0.25);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(a, b, 45), null, new Rect(0, 0, size, size));
            var text = new FormattedText(
                label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), size / 9.0, Brushes.White, 1.0);
            dc.DrawText(text, new Point(size * 0.08, size * 0.08));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static Color Hsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x),
            < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static IEnumerable<CodecChoice> SyntheticChoices()
    {
        CodecChoice Make(string id, string ext, string format, string impl, bool lossless, CodecHealth health,
            string description, string bestUse, string distribution, string history, int rank)
            => new()
            {
                StableId = id, Extension = ext, FormatName = format, Implementation = impl,
                Lossless = lossless, Health = health, Description = description, BestUse = bestUse,
                Distribution = distribution, History = history, RecommendedRank = rank,
                CompressionRank = rank, EfficiencyRank = rank,
            };

        yield return Make("flac:libflac", "flac", "FLAC", "libFLAC", true,
            CodecHealth.Ready("bundled", "native wrapper validated"),
            "Free Lossless Audio Codec.", "Archival rips, wide player support.", "BSD, bundled.",
            "Xiph.Org, 2001 to present.", 0);
        yield return Make("wv:libwavpack", "wv", "WavPack", "libwavpack", true,
            CodecHealth.Ready("bundled", "native wrapper validated"),
            "Hybrid lossless with optional correction file.", "Archival rips with hybrid lossy copies.",
            "BSD, bundled.", "David Bryant, 1998 to present.", 1);
        yield return Make("m4a:alac", "m4a", "Apple Lossless", "managed ALAC", true,
            CodecHealth.Ready("managed", ""),
            "Lossless in an MP4 container.", "Apple devices and players.", "Apache 2.0, bundled.",
            "Apple, 2004; open-sourced 2011.", 2);
        yield return Make("ape:maclib", "ape", "Monkey's Audio", "MACLib 13.20", true,
            CodecHealth.Ready("bundled", "SDK archive byte-validated"),
            "High-compression lossless.", "Smallest archives, Windows players.", "Custom license, bundled.",
            "Matthew T. Ashland, 2000 to present.", 3);
        yield return Make("tak:cli", "tak", "TAK", "takc.exe (imported)", true,
            CodecHealth.SetupRequired("user import", "Import takc.exe from the TAK distribution to enable."),
            "Tom's lossless Audio Kompressor.", "Compact archives, fast decode.", "Freeware CLI, not redistributed.",
            "Thomas Becker, 2007 to present.", 4);
        yield return Make("mp3:libmp3lame", "mp3", "MP3", "libmp3lame", false,
            CodecHealth.Ready("bundled", "native wrapper validated"),
            "The lossy standard.", "Portable copies for old players and cars.", "LGPL, bundled.",
            "Fraunhofer, 1993; LAME 1998 to present.", 5);
        yield return Make("opus:cli", "opus", "Opus", "opusenc (imported)", false,
            CodecHealth.SetupRequired("user import", "Import opusenc from the opus-tools distribution."),
            "Modern lossy codec.", "Streaming and small portable copies.", "BSD CLI, not redistributed.",
            "IETF, 2012 to present.", 6);
        yield return Make("tta:plugin", "tta", "True Audio", "TTA plugin", true,
            CodecHealth.LoadFailed("plugin", "Plugin hash did not match the trust manifest."),
            "Simple lossless codec.", "Legacy collections.", "LGPL plugin.", "Aleksander Djuric, 1999.", 7);
    }

    // ---- screen capture -------------------------------------------------------------------------

    private static string Save(Window window, string dir, AppTheme theme, string what)
    {
        var helper = new WindowInteropHelper(window);
        Assert.IsTrue(GetWindowRect(helper.Handle, out RECT rect), "window rect");
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        int percent = (int)Math.Round(100 * VisualTreeHelper.GetDpi(window).PixelsPerDip);
        string name = $"{percent}pct-{theme.ToString().ToLowerInvariant()}-{what}.png";
        string path = Path.Combine(dir, name);

        // The DWM-composed desktop is read through a screen DC. WPF windows are not layered, so a
        // plain SRCCOPY blit of the window's physical rectangle is the real presented surface.
        byte[] pixels = new byte[width * height * 4];
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr bitmap = CreateCompatibleBitmap(screen, width, height);
        IntPtr previous = SelectObject(memory, bitmap);
        try
        {
            Assert.IsTrue(BitBlt(memory, 0, 0, width, height, screen, rect.Left, rect.Top, SrcCopy), "BitBlt");
            var info = new BITMAPINFO
            {
                biSize = 40, biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32, biCompression = 0,
            };
            SelectObject(memory, previous);
            Assert.AreEqual(height, GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0), "GetDIBits");
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }

        BitmapSource source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using (FileStream stream = File.Create(path))
            encoder.Save(stream);
        return path;
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private const uint SrcCopy = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public uint biSize; public int biWidth; public int biHeight; public ushort biPlanes; public ushort biBitCount;
        public uint biCompression; public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
        public uint biClrUsed; public uint biClrImportant; public uint bmiColors;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr handle);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hdc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFO info, uint usage);
}
