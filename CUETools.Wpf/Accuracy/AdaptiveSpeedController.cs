using System;
using System.Collections.Generic;
using System.Linq;

namespace CUETools.Wpf.Accuracy;

/// <summary>
/// Feature 3 (adaptive read speed), pure decision core - the part that runs and is testable with
/// no drive. Starts at the drive's max supported speed; a cluster of C2 flags or pass-to-pass
/// mismatches drops one step; a run of clean regions eases back up one step. The hardware layer
/// feeds it events and applies <see cref="CurrentSpeed"/> via SetCdSpeed/SetStreaming.
///
/// See docs/superpowers/specs/2026-07-10-rip-accuracy-and-log-integrity-design.md, Feature 3.
/// </summary>
public sealed class AdaptiveSpeedController
{
    private readonly int[] _speeds;   // ascending, distinct, real drive speeds
    private readonly int _easeUpAfter;
    private int _idx;                 // index into _speeds
    private int _cleanRun;

    /// <param name="supportedSpeeds">The drive's real supported speeds (any order, from GetSpeed).</param>
    /// <param name="ceilingSpeed">Optional Auto ceiling (calibrated MaxSpeed); start no higher.</param>
    /// <param name="easeUpAfter">Clean regions before easing up a step.</param>
    public AdaptiveSpeedController(IEnumerable<int> supportedSpeeds, int? ceilingSpeed = null, int easeUpAfter = 4)
    {
        _speeds = supportedSpeeds.Where(s => s > 0).Distinct().OrderBy(s => s).ToArray();
        if (_speeds.Length == 0) throw new ArgumentException("no supported speeds", nameof(supportedSpeeds));
        _easeUpAfter = Math.Max(1, easeUpAfter);

        _idx = _speeds.Length - 1; // start at max
        if (ceilingSpeed is int cap)
        {
            int capIdx = Array.FindLastIndex(_speeds, s => s <= cap);
            if (capIdx >= 0) _idx = capIdx;
        }
    }

    public int CurrentSpeed => _speeds[_idx];
    public bool AtMin => _idx == 0;
    public bool AtMax => _idx == _speeds.Length - 1;

    /// <summary>A cluster of C2 errors or a pass-to-pass mismatch: back off one step.</summary>
    public void OnErrorCluster()
    {
        _cleanRun = 0;
        if (_idx > 0) _idx--;
    }

    /// <summary>A clean region read with no errors: after enough of them, ease back up a step.</summary>
    public void OnCleanRegion()
    {
        if (++_cleanRun >= _easeUpAfter && _idx < _speeds.Length - 1)
        {
            _idx++;
            _cleanRun = 0;
        }
    }
}
