using System;
using System.Collections.Generic;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.Models;

/// <summary>Which read of the job the display is currently showing (SLICE-010).</summary>
public enum RipPhaseKind
{
    /// <summary>No job running.</summary>
    None,

    /// <summary>A plain Rip or Verify: one read, solid fill, no phase chip.</summary>
    SingleRead,

    /// <summary>Test read of a Test and Copy: hollow fill, TEST chip.</summary>
    Test,

    /// <summary>Copy read: solid fill drawn inside the retained hollow Test outline.</summary>
    Copy,

    /// <summary>A third, confirming read after Test and Copy disagreed.</summary>
    Confirm
}

/// <summary>
/// The pure derivation behind the SLICE-010 rip progress visuals: per-track fill from the
/// whole-job fraction plus TOC boundaries, phase from completed-read events, pass activity
/// and per-track damage attribution from re-read reports. No engine objects, no UI, no
/// clock, so every rule is unit-testable without hardware (S10-006).
///
/// Every number here is a literal engine value or a pure function of one. The only derived
/// quantities are the per-track fractions, which are arithmetic over the TOC boundaries the
/// engine reported.
/// </summary>
public sealed class TrackProgressModel
{
    private double[] _endFrac = Array.Empty<double>();
    private int _completedReads;
    private bool _phaseFlipPending;
    private double _lastWindowFrac = -1;
    private int _lastReReads;

    /// <summary>Fractions 0..1 of the whole job at which each track ends.</summary>
    public IReadOnlyList<double> EndFractions => _endFrac;

    public RipPhaseKind Phase { get; private set; } = RipPhaseKind.None;

    /// <summary>TEST / COPY / READ 3, or empty when the phase has no chip (single read, idle).</summary>
    public string PhaseChip => Phase switch
    {
        RipPhaseKind.Test => "TEST",
        RipPhaseKind.Copy => "COPY",
        RipPhaseKind.Confirm => "READ 3",
        _ => "",
    };

    /// <summary>BURST / SECURE / PARANOID / SALVAGE while a job runs, else empty.</summary>
    public string ModeChip { get; private set; } = "";

    /// <summary>
    /// The pass lane is the honest display of multi-pass work, so it exists only for modes
    /// that re-read: Burst shows no lane, and the absence is the display (D-058).
    /// </summary>
    public bool PassLaneVisible { get; private set; }

    /// <summary>Literal pass count of the window being re-read right now, zero when none is.</summary>
    public int PassTicks { get; private set; }

    /// <summary>The engine's pass budget for the current window, for drawing the lane's scale.</summary>
    public int PassMax { get; private set; } = 1;

    /// <summary>Current-phase fill per track, 0..1.</summary>
    public double[] Current { get; private set; } = Array.Empty<double>();

    /// <summary>Fill each track reached during Test, retained under the Copy fill (D-057:
    /// the visual mirror of the immutable phase-evidence rule). Zero outside Test and Copy.</summary>
    public double[] TestRetained { get; private set; } = Array.Empty<double>();

    /// <summary>Distinct re-read windows attributed to each track. Literal window count,
    /// not sectors: one scratch that costs one window is one tick.</summary>
    public int[] RereadWindows { get; private set; } = Array.Empty<int>();

    /// <summary>Sectors the engine gave up on, attributed to each track. Nonzero is the
    /// red-edged terminal state (S10-005); routine corrections never mark (D-058).</summary>
    public int[] GivenUpSectors { get; private set; } = Array.Empty<int>();

    /// <summary>Index of the track the head is inside, -1 when none.</summary>
    public int ActiveIndex { get; private set; } = -1;

    /// <summary>Configure the per-track boundaries from track lengths (the TOC's own
    /// proportions). Resets all per-track state.</summary>
    public void SetBoundaries(IReadOnlyList<TimeSpan> trackLengths)
    {
        ArgumentNullException.ThrowIfNull(trackLengths);
        int n = trackLengths.Count;
        _endFrac = new double[n];
        double total = 0;
        foreach (TimeSpan t in trackLengths) total += t.TotalSeconds;
        double cum = 0;
        for (int i = 0; i < n; i++)
        {
            cum += trackLengths[i].TotalSeconds;
            _endFrac[i] = total > 0 ? cum / total : 0;
        }
        Current = new double[n];
        TestRetained = new double[n];
        RereadWindows = new int[n];
        GivenUpSectors = new int[n];
        ActiveIndex = -1;
    }

    /// <summary>
    /// A job begins. <paramref name="testAndCopy"/> starts the phase ladder at Test;
    /// otherwise the whole job is one solid read. <paramref name="modeName"/> is the
    /// correction mode's display name; <paramref name="multiPass"/> gates the pass lane
    /// (false for Burst).
    /// </summary>
    public void StartJob(bool testAndCopy, string modeName, bool multiPass)
    {
        Phase = testAndCopy ? RipPhaseKind.Test : RipPhaseKind.SingleRead;
        ModeChip = (modeName ?? "").ToUpperInvariant();
        PassLaneVisible = multiPass;
        PassTicks = 0;
        PassMax = 1;
        _completedReads = 0;
        _phaseFlipPending = false;
        _lastWindowFrac = -1;
        _lastReReads = 0;
        Array.Clear(Current);
        Array.Clear(TestRetained);
        Array.Clear(RereadWindows);
        Array.Clear(GivenUpSectors);
        ActiveIndex = -1;
    }

    /// <summary>The job ended. Terminal marks (given-up sectors) survive; activity stops.
    /// A successful end fills every track: the engine finished them all by definition.</summary>
    public void EndJob(bool completed)
    {
        if (completed)
        {
            for (int i = 0; i < Current.Length; i++) Current[i] = 1;
        }
        ActiveIndex = -1;
        PassTicks = 0;
        Phase = RipPhaseKind.None;
        // The chips describe a running job; a finished one is described by its result
        // text. Leaving PARANOID up beside a failure banner read as if a job were still
        // going (owner screenshot, 2026-08-19).
        ModeChip = "";
        PassLaneVisible = false;
    }

    /// <summary>
    /// A read finished (ReadVerdict.ReadIndex, zero-based). The phase does not flip here:
    /// it flips on the next progress event, because the verdict for read N and the first
    /// progress of read N+1 arrive in that order, and flipping early would relabel the
    /// finished read's final frame. A job that ends after its last verdict therefore keeps
    /// its last honest phase on screen.
    /// </summary>
    public void OnReadCompleted(int readIndex)
    {
        _completedReads = Math.Max(_completedReads, readIndex + 1);
        _phaseFlipPending = Phase is RipPhaseKind.Test or RipPhaseKind.Copy;
    }

    /// <summary>Whole-job fraction from the engine's progress event.</summary>
    public void OnProgress(double frac)
    {
        if (_phaseFlipPending)
        {
            _phaseFlipPending = false;
            if (Phase == RipPhaseKind.Test)
            {
                // D-057: the Test outline is evidence of a completed phase; it is retained,
                // never repainted, and the Copy fill draws inside it.
                Array.Copy(Current, TestRetained, Current.Length);
                Array.Clear(Current);
                Phase = RipPhaseKind.Copy;
            }
            else if (Phase == RipPhaseKind.Copy)
            {
                Array.Clear(Current);
                Phase = RipPhaseKind.Confirm;
            }
            // A window's pass state belongs to the read that ran it.
            PassTicks = 0;
            _lastWindowFrac = -1;
            _lastReReads = 0;
        }

        frac = Math.Clamp(frac, 0, 1);
        double start = 0;
        ActiveIndex = -1;
        for (int i = 0; i < Current.Length && i < _endFrac.Length; i++)
        {
            double end = _endFrac[i];
            if (frac >= end)
            {
                Current[i] = 1;
            }
            else if (frac >= start)
            {
                double span = end - start;
                Current[i] = span > 1e-9 ? (frac - start) / span : 0;
                ActiveIndex = i;
            }
            else
            {
                Current[i] = 0;
            }
            start = end;
        }
    }

    /// <summary>
    /// A re-read report. Pass ticks are the literal running pass count of the current
    /// window. A report whose WindowFrac differs from the last one is a new window, and a
    /// window is attributed to the track its fraction falls inside; the attribution counts
    /// windows, not reports, so a thirty-pass grind on one window is one amber tick.
    /// Given-up sectors are the engine's one-shot verdict and accumulate per track.
    /// </summary>
    public void OnReread(RereadReport report)
    {
        if (report.ReReads > 0)
        {
            bool newWindow =
                Math.Abs(report.WindowFrac - _lastWindowFrac) > 1e-9 ||
                report.ReReads < _lastReReads;
            if (newWindow)
            {
                int track = TrackAt(report.WindowFrac);
                if (track >= 0) RereadWindows[track]++;
                _lastWindowFrac = report.WindowFrac;
            }
            _lastReReads = report.ReReads;
            PassTicks = report.ReReads;
            PassMax = Math.Max(1, report.MaxReReads);
        }
        else
        {
            PassTicks = 0;
        }

        if (report.WindowGivenUpSectors > 0)
        {
            int track = TrackAt(report.WindowFrac);
            if (track >= 0) GivenUpSectors[track] += report.WindowGivenUpSectors;
        }
    }

    /// <summary>The track whose span contains the given whole-job fraction, -1 if none.</summary>
    public int TrackAt(double frac)
    {
        double start = 0;
        for (int i = 0; i < _endFrac.Length; i++)
        {
            if (frac >= start && (frac < _endFrac[i] || (i == _endFrac.Length - 1 && frac <= 1)))
                return i;
            start = _endFrac[i];
        }
        return -1;
    }
}
