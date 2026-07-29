using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CUETools.Wpf.Controls;

internal enum DiscFrameState
{
    Idle,
    Reading,
    Reread,
    Unreadable
}

/// <summary>
/// Opt-in, numeric-only frame receipt for the live CD model. The hot path uses
/// fixed histograms and value-type transition slots, so measuring a damaged-disc
/// run does not add per-frame allocation or change the optical worker.
/// </summary>
internal sealed class DiscFrameMetrics
{
    internal const string EnvironmentVariableName =
        "CUETOOLS_DISC_FRAME_METRICS";
    private const double BucketMilliseconds = 0.1;
    private const double MaximumHistogramMilliseconds = 250.0;
    private const int HistogramBuckets =
        (int)(MaximumHistogramMilliseconds / BucketMilliseconds) + 1;
    private const int MaximumTransitions = 512;

    private readonly string _outputPath;
    private readonly int _renderTier;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly FrameStats[] _states =
    {
        new(),
        new(),
        new(),
        new()
    };
    private readonly TransitionEntry[] _transitions =
        new TransitionEntry[MaximumTransitions];
    private int _transitionCount;
    private int _transitionOverflow;
    private long _lastCallbackStart;
    private DiscFrameState _lastState;
    private bool _hasState;
    private bool _completed;

    private DiscFrameMetrics(string outputPath, int renderTier)
    {
        _outputPath = Path.GetFullPath(outputPath);
        _renderTier = renderTier;
    }

    internal static DiscFrameMetrics? TryCreate(int renderTier)
    {
        string? outputPath =
            Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return TryCreate(outputPath, renderTier);
    }

    internal static DiscFrameMetrics? TryCreate(
        string? outputPath,
        int renderTier)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            return null;

        try
        {
            string expanded = outputPath.Replace(
                "{pid}",
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase);
            return new DiscFrameMetrics(expanded, renderTier);
        }
        catch
        {
            // Diagnostic measurement must never prevent the visual from loading.
            return null;
        }
    }

    internal void RecordFrame(
        long callbackStart,
        long callbackEnd,
        bool active,
        bool rereadActive,
        bool unreadable,
        double progress,
        double rereadFraction,
        double zoom)
    {
        if (_completed)
            return;

        DiscFrameState state = unreadable
            ? DiscFrameState.Unreadable
            : rereadActive
                ? DiscFrameState.Reread
                : active
                    ? DiscFrameState.Reading
                    : DiscFrameState.Idle;

        if (!_hasState || state != _lastState)
        {
            if (_transitionCount < _transitions.Length)
            {
                _transitions[_transitionCount++] = new TransitionEntry(
                    DateTime.UtcNow,
                    state,
                    Clamp01(progress),
                    Clamp01(rereadFraction),
                    Clamp01(zoom));
            }
            else
            {
                _transitionOverflow++;
            }
            _lastState = state;
            _hasState = true;
        }

        if (_lastCallbackStart != 0 && callbackStart >= _lastCallbackStart)
        {
            long callbackTicks = Math.Max(0, callbackEnd - callbackStart);
            double intervalMilliseconds =
                TicksToMilliseconds(callbackStart - _lastCallbackStart);
            double callbackMilliseconds =
                TicksToMilliseconds(callbackTicks);
            _states[(int)state].Record(
                intervalMilliseconds,
                callbackMilliseconds,
                Clamp01(zoom));
        }
        _lastCallbackStart = callbackStart;
    }

    internal void Complete()
    {
        if (_completed)
            return;
        _completed = true;

        try
        {
            string? directory = Path.GetDirectoryName(_outputPath);
            if (string.IsNullOrWhiteSpace(directory))
                return;
            Directory.CreateDirectory(directory);

            var stateReceipts = new Dictionary<string, StateReceipt>(
                StringComparer.Ordinal)
            {
                ["idle"] = _states[(int)DiscFrameState.Idle].ToReceipt(),
                ["reading"] = _states[(int)DiscFrameState.Reading].ToReceipt(),
                ["reread"] = _states[(int)DiscFrameState.Reread].ToReceipt(),
                ["unreadable"] =
                    _states[(int)DiscFrameState.Unreadable].ToReceipt()
            };
            var transitionReceipts =
                new TransitionReceipt[_transitionCount];
            for (int i = 0; i < transitionReceipts.Length; i++)
            {
                TransitionEntry transition = _transitions[i];
                transitionReceipts[i] = new TransitionReceipt
                {
                    Utc = transition.Utc,
                    State = StateName(transition.State),
                    Progress = transition.Progress,
                    RereadFraction = transition.RereadFraction,
                    Zoom = transition.Zoom
                };
            }

            var receipt = new MetricsReceipt
            {
                SchemaVersion = 1,
                ProductVersion = ProductVersion(),
                ProcessId = Environment.ProcessId,
                RenderTier = _renderTier,
                StartedUtc = _startedUtc,
                CompletedUtc = DateTime.UtcNow,
                HistogramBucketMilliseconds = BucketMilliseconds,
                HistogramMaximumMilliseconds =
                    MaximumHistogramMilliseconds,
                IntervalAssignment =
                    "State observed at the current CompositionTarget.Rendering callback.",
                States = stateReceipts,
                Transitions = transitionReceipts,
                TransitionOverflow = _transitionOverflow
            };

            string json = JsonSerializer.Serialize(
                receipt,
                new JsonSerializerOptions { WriteIndented = true });
            string temporary = _outputPath + ".tmp-" +
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(
                    temporary,
                    json,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                File.Move(temporary, _outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        catch
        {
            // A failed benchmark receipt cannot alter or fail a rip.
        }
    }

    private static string ProductVersion()
    {
        Assembly assembly = typeof(DiscFrameMetrics).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                   ?.InformationalVersion ??
               assembly.GetName().Version?.ToString() ??
               "unknown";
    }

    private static double Clamp01(double value) =>
        Math.Max(0.0, Math.Min(1.0, value));

    private static double TicksToMilliseconds(long ticks) =>
        ticks * 1000.0 / Stopwatch.Frequency;

    private static string StateName(DiscFrameState state) => state switch
    {
        DiscFrameState.Idle => "idle",
        DiscFrameState.Reading => "reading",
        DiscFrameState.Reread => "reread",
        DiscFrameState.Unreadable => "unreadable",
        _ => "unknown"
    };

    private sealed class FrameStats
    {
        private readonly long[] _intervalHistogram =
            new long[HistogramBuckets];
        private readonly long[] _callbackHistogram =
            new long[HistogramBuckets];
        private long _frames;
        private long _intervalOverflow;
        private long _callbackOverflow;
        private long _over16_67Milliseconds;
        private long _over25Milliseconds;
        private long _over33_33Milliseconds;
        private long _over50Milliseconds;
        private double _intervalSum;
        private double _callbackSum;
        private double _intervalMaximum;
        private double _callbackMaximum;
        private double _zoomSum;
        private double _zoomMaximum;

        internal void Record(
            double intervalMilliseconds,
            double callbackMilliseconds,
            double zoom)
        {
            _frames++;
            _intervalSum += intervalMilliseconds;
            _callbackSum += callbackMilliseconds;
            _zoomSum += zoom;
            if (intervalMilliseconds > _intervalMaximum)
                _intervalMaximum = intervalMilliseconds;
            if (callbackMilliseconds > _callbackMaximum)
                _callbackMaximum = callbackMilliseconds;
            if (zoom > _zoomMaximum)
                _zoomMaximum = zoom;

            AddHistogram(
                _intervalHistogram,
                intervalMilliseconds,
                ref _intervalOverflow);
            AddHistogram(
                _callbackHistogram,
                callbackMilliseconds,
                ref _callbackOverflow);

            if (intervalMilliseconds > 16.67)
                _over16_67Milliseconds++;
            if (intervalMilliseconds > 25.0)
                _over25Milliseconds++;
            if (intervalMilliseconds > 33.33)
                _over33_33Milliseconds++;
            if (intervalMilliseconds > 50.0)
                _over50Milliseconds++;
        }

        internal StateReceipt ToReceipt() => new()
        {
            Frames = _frames,
            DurationSeconds = _intervalSum / 1000.0,
            MeanIntervalMilliseconds =
                _frames == 0 ? 0 : _intervalSum / _frames,
            P50IntervalMilliseconds = Percentile(
                _intervalHistogram,
                _frames,
                0.50),
            P95IntervalMilliseconds = Percentile(
                _intervalHistogram,
                _frames,
                0.95),
            P99IntervalMilliseconds = Percentile(
                _intervalHistogram,
                _frames,
                0.99),
            MaximumIntervalMilliseconds = _intervalMaximum,
            IntervalOverflow = _intervalOverflow,
            Over16_67Milliseconds = _over16_67Milliseconds,
            Over25Milliseconds = _over25Milliseconds,
            Over33_33Milliseconds = _over33_33Milliseconds,
            Over50Milliseconds = _over50Milliseconds,
            MeanCallbackMilliseconds =
                _frames == 0 ? 0 : _callbackSum / _frames,
            P95CallbackMilliseconds = Percentile(
                _callbackHistogram,
                _frames,
                0.95),
            P99CallbackMilliseconds = Percentile(
                _callbackHistogram,
                _frames,
                0.99),
            MaximumCallbackMilliseconds = _callbackMaximum,
            CallbackOverflow = _callbackOverflow,
            MeanZoom = _frames == 0 ? 0 : _zoomSum / _frames,
            MaximumZoom = _zoomMaximum
        };

        private static void AddHistogram(
            long[] histogram,
            double milliseconds,
            ref long overflow)
        {
            int bucket = (int)Math.Round(
                Math.Max(0.0, milliseconds) / BucketMilliseconds,
                MidpointRounding.AwayFromZero);
            if (bucket >= histogram.Length)
            {
                overflow++;
                bucket = histogram.Length - 1;
            }
            histogram[bucket]++;
        }

        private static double Percentile(
            long[] histogram,
            long count,
            double percentile)
        {
            if (count <= 0)
                return 0;
            long target = Math.Max(
                1,
                (long)Math.Ceiling(count * percentile));
            long cumulative = 0;
            for (int i = 0; i < histogram.Length; i++)
            {
                cumulative += histogram[i];
                if (cumulative >= target)
                    return i * BucketMilliseconds;
            }
            return MaximumHistogramMilliseconds;
        }
    }

    private readonly struct TransitionEntry
    {
        internal TransitionEntry(
            DateTime utc,
            DiscFrameState state,
            double progress,
            double rereadFraction,
            double zoom)
        {
            Utc = utc;
            State = state;
            Progress = progress;
            RereadFraction = rereadFraction;
            Zoom = zoom;
        }

        internal DateTime Utc { get; }
        internal DiscFrameState State { get; }
        internal double Progress { get; }
        internal double RereadFraction { get; }
        internal double Zoom { get; }
    }

    private sealed class MetricsReceipt
    {
        public int SchemaVersion { get; init; }
        public string ProductVersion { get; init; } = "";
        public int ProcessId { get; init; }
        public int RenderTier { get; init; }
        public DateTime StartedUtc { get; init; }
        public DateTime CompletedUtc { get; init; }
        public double HistogramBucketMilliseconds { get; init; }
        public double HistogramMaximumMilliseconds { get; init; }
        public string IntervalAssignment { get; init; } = "";
        public Dictionary<string, StateReceipt> States { get; init; } = new();
        public TransitionReceipt[] Transitions { get; init; } = Array.Empty<TransitionReceipt>();
        public int TransitionOverflow { get; init; }
    }

    private sealed class StateReceipt
    {
        public long Frames { get; init; }
        public double DurationSeconds { get; init; }
        public double MeanIntervalMilliseconds { get; init; }
        public double P50IntervalMilliseconds { get; init; }
        public double P95IntervalMilliseconds { get; init; }
        public double P99IntervalMilliseconds { get; init; }
        public double MaximumIntervalMilliseconds { get; init; }
        public long IntervalOverflow { get; init; }
        public long Over16_67Milliseconds { get; init; }
        public long Over25Milliseconds { get; init; }
        public long Over33_33Milliseconds { get; init; }
        public long Over50Milliseconds { get; init; }
        public double MeanCallbackMilliseconds { get; init; }
        public double P95CallbackMilliseconds { get; init; }
        public double P99CallbackMilliseconds { get; init; }
        public double MaximumCallbackMilliseconds { get; init; }
        public long CallbackOverflow { get; init; }
        public double MeanZoom { get; init; }
        public double MaximumZoom { get; init; }
    }

    private sealed class TransitionReceipt
    {
        public DateTime Utc { get; init; }
        public string State { get; init; } = "";
        public double Progress { get; init; }
        public double RereadFraction { get; init; }
        public double Zoom { get; init; }
    }
}
