using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using CUETools.Wpf.Controls;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class DiscModel3DTests
{
    [TestMethod]
    public void DataRadiusUsesEqualAreaCdGeometryAndClamps()
    {
        Assert.AreEqual(25.0, DiscModel3D.DataRadius(-1), 0.000001);
        Assert.AreEqual(25.0, DiscModel3D.DataRadius(0), 0.000001);
        Assert.AreEqual(
            Math.Sqrt((25.0 * 25.0 + 58.0 * 58.0) / 2.0),
            DiscModel3D.DataRadius(0.5),
            0.000001);
        Assert.AreEqual(58.0, DiscModel3D.DataRadius(1), 0.000001);
        Assert.AreEqual(58.0, DiscModel3D.DataRadius(2), 0.000001);
    }

    [TestMethod]
    public void VisualSpinRetainsInnerFasterClvRelationship()
    {
        double inner = DiscModel3D.VisualSpinDegreesPerSecond(0);
        double middle = DiscModel3D.VisualSpinDegreesPerSecond(0.5);
        double outer = DiscModel3D.VisualSpinDegreesPerSecond(1);

        Assert.AreEqual(145.0, inner, 0.000001);
        Assert.IsTrue(inner > middle);
        Assert.IsTrue(middle > outer);
        Assert.AreEqual(145.0 * 25.0 / 58.0, outer, 0.000001);
    }

    [TestMethod]
    public void AdvanceDoesNotAllocatePerFrameAfterWarmup()
    {
        RunSta(() =>
        {
            var disc = new DiscModel3D
            {
                Active = true,
                Progress = 0.63
            };
            Advance(disc, 100);
            _ = GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            Advance(disc, 1000);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.IsTrue(
                allocated <= 128 * 1024,
                $"The visual render state allocated {allocated} bytes over 1000 frames.");
        });
    }

    [TestMethod]
    public void RereadAndUnreadableStatesKeepDamageAutozoomContract()
    {
        RunSta(() =>
        {
            var disc = new DiscModel3D
            {
                Active = true,
                Progress = 0.82,
                RereadFrac = 0.24
            };

            disc.Advance(0.016);
            Assert.AreEqual(
                DiscModel3D.DataRadius(0.82),
                disc.LaserRadius,
                0.000001);

            disc.RereadActive = true;
            Advance(disc, 40);
            double rereadZoom = disc.DamageZoom;
            Assert.IsTrue(rereadZoom > 0.85);
            Assert.AreEqual(
                DiscModel3D.DataRadius(0.24),
                disc.LaserRadius,
                0.000001);

            disc.RereadActive = false;
            Advance(disc, 40);
            double recoveredZoom = disc.DamageZoom;
            Assert.IsTrue(recoveredZoom < rereadZoom);

            disc.Unreadable = true;
            Advance(disc, 40);
            double unreadableZoom = disc.DamageZoom;
            Assert.IsTrue(unreadableZoom > 0.85);
            Assert.AreNotEqual(
                new System.Windows.Media.Media3D.Point3D(0, 95, 96),
                disc.CameraPosition);

            Advance(disc, 20);
            Assert.IsTrue(
                disc.DamageZoom >= unreadableZoom,
                "Unreadable must hold the damage zoom instead of easing out.");
        });
    }

    [TestMethod]
    public void FrameMetricsAreDisabledWithoutAnExplicitOutput()
    {
        Assert.IsNull(DiscFrameMetrics.TryCreate(null, renderTier: 2));
        Assert.IsNull(DiscFrameMetrics.TryCreate("", renderTier: 2));
        Assert.IsNull(DiscFrameMetrics.TryCreate("   ", renderTier: 2));
    }

    [TestMethod]
    public void FrameMetricsWriteNumericStateSpecificReceipt()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "cuetools-frame-metrics-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "metrics.json");
        try
        {
            DiscFrameMetrics metrics =
                DiscFrameMetrics.TryCreate(path, renderTier: 2)!;
            long start = Stopwatch.GetTimestamp();
            long interval = Math.Max(1, Stopwatch.Frequency / 60);
            long callback = Math.Max(1, Stopwatch.Frequency / 2000);

            Record(metrics, ref start, interval, callback, DiscFrameState.Idle);
            Record(metrics, ref start, interval, callback, DiscFrameState.Idle);
            Record(metrics, ref start, interval, callback, DiscFrameState.Reading);
            Record(metrics, ref start, interval, callback, DiscFrameState.Reading);
            Record(metrics, ref start, interval, callback, DiscFrameState.Reread);
            Record(metrics, ref start, interval, callback, DiscFrameState.Reread);
            Record(metrics, ref start, interval, callback, DiscFrameState.Unreadable);
            Record(metrics, ref start, interval, callback, DiscFrameState.Unreadable);
            metrics.Complete();

            string json = File.ReadAllText(path);
            Assert.IsFalse(json.Contains(path, StringComparison.OrdinalIgnoreCase));
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.AreEqual(1, root.GetProperty("SchemaVersion").GetInt32());
            Assert.AreEqual(2, root.GetProperty("RenderTier").GetInt32());
            Assert.AreEqual(
                0,
                root.GetProperty("TransitionOverflow").GetInt32());
            Assert.AreEqual(
                4,
                root.GetProperty("Transitions").GetArrayLength());

            JsonElement states = root.GetProperty("States");
            foreach (string state in new[]
                     {
                         "idle",
                         "reading",
                         "reread",
                         "unreadable"
                     })
            {
                JsonElement receipt = states.GetProperty(state);
                Assert.IsTrue(receipt.GetProperty("Frames").GetInt64() > 0);
                Assert.IsTrue(
                    receipt.GetProperty("MeanIntervalMilliseconds")
                        .GetDouble() > 0);
                Assert.IsTrue(
                    receipt.GetProperty("P99CallbackMilliseconds")
                        .GetDouble() >= 0);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void FrameMetricsDoNotAllocatePerFrameAfterWarmup()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "cuetools-frame-metrics-" + Guid.NewGuid().ToString("N"),
            "metrics.json");
        DiscFrameMetrics metrics =
            DiscFrameMetrics.TryCreate(path, renderTier: 2)!;
        long start = Stopwatch.GetTimestamp();
        long interval = Math.Max(1, Stopwatch.Frequency / 60);
        long callback = Math.Max(1, Stopwatch.Frequency / 2000);

        for (int i = 0; i < 100; i++)
            Record(
                metrics,
                ref start,
                interval,
                callback,
                DiscFrameState.Reading);

        _ = GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            Record(
                metrics,
                ref start,
                interval,
                callback,
                DiscFrameState.Reading);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(
            0,
            allocated,
            $"The live frame sampler allocated {allocated} bytes over 1000 frames.");
    }

    [TestMethod]
    public void RipViewKeepsLiveDamageBindingsAndStopsIdleFallbackSpin()
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsFalse(string.IsNullOrEmpty(root));
        XDocument document = XDocument.Load(
            Path.Combine(root, "CUETools.Wpf", "Views", "RipView.xaml"));

        XElement model = document.Descendants().Single(
            element => element.Name.LocalName == "DiscModel3D");
        Assert.AreEqual(
            "{Binding RipProgress, Mode=OneWay}",
            model.Attribute("Progress")?.Value);
        Assert.AreEqual(
            "{Binding RereadActive, Mode=OneWay}",
            model.Attribute("RereadActive")?.Value);
        Assert.AreEqual(
            "{Binding RereadFrac, Mode=OneWay}",
            model.Attribute("RereadFrac")?.Value);
        Assert.AreEqual(
            "{Binding Unreadable, Mode=OneWay}",
            model.Attribute("Unreadable")?.Value);

        XElement fallback = document.Descendants().Single(
            element => element.Name.LocalName == "DiscReadMap");
        Assert.AreEqual(
            "{Binding IsRipping, Mode=OneWay}",
            fallback.Attribute("Spinning")?.Value);
    }

    [TestMethod]
    public void IdleReadingRereadAndUnreadableRenderOffscreenInBothThemes()
    {
        RunSta(() =>
        {
            string outputDirectory = Environment.GetEnvironmentVariable(
                "CUETOOLS_DISC_RENDER_OUTPUT");
            foreach (AppTheme theme in Enum.GetValues<AppTheme>())
            {
                RenderedFrame idle = Render(theme, DiscState.Idle);
                RenderedFrame reading = Render(theme, DiscState.Reading);
                RenderedFrame reread = Render(theme, DiscState.Reread);
                RenderedFrame unreadable = Render(theme, DiscState.Unreadable);
                RenderedFrame fallback = RenderFallback(theme);

                Assert.IsTrue(CountColorBuckets(idle.Pixels) > 80);
                Assert.IsTrue(CountColorBuckets(fallback.Pixels) > 40);
                Assert.IsFalse(idle.Pixels.SequenceEqual(reading.Pixels));
                Assert.IsFalse(reading.Pixels.SequenceEqual(reread.Pixels));
                Assert.IsTrue(
                    CountRedPixels(unreadable.Pixels) >
                    CountRedPixels(reading.Pixels) + 12,
                    "Unreadable must add a visible critical marker.");

                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                    Save(idle.Bitmap, outputDirectory, theme, DiscState.Idle);
                    Save(reading.Bitmap, outputDirectory, theme, DiscState.Reading);
                    Save(reread.Bitmap, outputDirectory, theme, DiscState.Reread);
                    Save(
                        unreadable.Bitmap,
                        outputDirectory,
                        theme,
                        DiscState.Unreadable);
                    SaveFallback(fallback.Bitmap, outputDirectory, theme);
                }
            }
        });
    }

    private static RenderedFrame Render(AppTheme theme, DiscState state)
    {
        const int size = 432;
        var root = new Grid { Width = size, Height = size };
        ThemeService.Swap(root.Resources, theme);
        root.Background = (Brush)root.Resources["Ground"];
        var disc = new DiscModel3D
        {
            Width = size,
            Height = size,
            Active = state != DiscState.Idle,
            Progress = 0.68,
            RereadFrac = 0.37,
            RereadActive = state == DiscState.Reread,
            Unreadable = state == DiscState.Unreadable
        };
        root.Children.Add(disc);
        Advance(disc, state is DiscState.Reread or DiscState.Unreadable ? 48 : 2);
        root.Measure(new Size(size, size));
        root.Arrange(new Rect(0, 0, size, size));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            size,
            size,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        return new RenderedFrame(bitmap, pixels);
    }

    private static RenderedFrame RenderFallback(AppTheme theme)
    {
        const int size = 432;
        var root = new Grid { Width = size, Height = size };
        ThemeService.Swap(root.Resources, theme);
        root.Background = (Brush)root.Resources["Ground"];
        root.Children.Add(new DiscReadMap
        {
            Width = size,
            Height = size,
            Progress = 0.68,
            Spinning = false
        });
        root.Measure(new Size(size, size));
        root.Arrange(new Rect(0, 0, size, size));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            size,
            size,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        return new RenderedFrame(bitmap, pixels);
    }

    private static int CountColorBuckets(byte[] pixels)
    {
        var buckets = new HashSet<int>();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int bucket =
                (pixels[i] >> 3) |
                ((pixels[i + 1] >> 3) << 5) |
                ((pixels[i + 2] >> 3) << 10);
            buckets.Add(bucket);
        }
        return buckets.Count;
    }

    private static int CountRedPixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte blue = pixels[i];
            byte green = pixels[i + 1];
            byte red = pixels[i + 2];
            if (red > 150 && red > green + 30 && red > blue + 30)
                count++;
        }
        return count;
    }

    private static void Save(
        BitmapSource bitmap,
        string outputDirectory,
        AppTheme theme,
        DiscState state)
    {
        string path = Path.Combine(
            outputDirectory,
            $"{theme.ToString().ToLowerInvariant()}-{state.ToString().ToLowerInvariant()}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void SaveFallback(
        BitmapSource bitmap,
        string outputDirectory,
        AppTheme theme)
    {
        string path = Path.Combine(
            outputDirectory,
            $"{theme.ToString().ToLowerInvariant()}-fallback.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Advance(DiscModel3D disc, int frames)
    {
        for (int i = 0; i < frames; i++)
            disc.Advance(0.016);
    }

    private static void Record(
        DiscFrameMetrics metrics,
        ref long start,
        long interval,
        long callback,
        DiscFrameState state)
    {
        bool active = state != DiscFrameState.Idle;
        metrics.RecordFrame(
            start,
            start + callback,
            active,
            state == DiscFrameState.Reread,
            state == DiscFrameState.Unreadable,
            progress: 0.61,
            rereadFraction: 0.37,
            zoom: state is DiscFrameState.Reread or DiscFrameState.Unreadable
                ? 0.92
                : 0.0);
        start += interval;
    }

    private static void RunSta(Action action)
    {
        Exception error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed record RenderedFrame(BitmapSource Bitmap, byte[] Pixels);

    private enum DiscState
    {
        Idle,
        Reading,
        Reread,
        Unreadable
    }
}
