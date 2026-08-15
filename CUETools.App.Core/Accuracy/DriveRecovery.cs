using System;
using System.Collections.Generic;
using System.IO;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.Accuracy;

/// <summary>
/// One stuck-drive incident for one physical drive (SLICE-011, D-061). Records
/// hardware identity context and counters only - never disc or album content.
/// An empty <see cref="CuringRung"/> means the ladder ended uncured.
/// </summary>
public sealed class DriveRecoveryIncident
{
    public string TimestampUtc { get; set; } = "";
    /// <summary>Where the signature was classified: "cache-defeat" or "payload-storm".</summary>
    public string Trigger { get; set; } = "";
    /// <summary>The scrubbed failure-context counters from the fatal error.</summary>
    public string TriggerContext { get; set; } = "";
    /// <summary>Rungs attempted in order (see <see cref="RecoveryLadderPolicy"/> rung names).</summary>
    public List<string> RungsAttempted { get; set; } = new();
    /// <summary>The rung whose verification probe confirmed the cure; "" if none did.</summary>
    public string CuringRung { get; set; } = "";
}

/// <summary>
/// Loads/saves the per-drive incident table (signature -> incidents, oldest
/// first) as JSON under the user's app data, beside the calibration store and
/// with the same signature normalization, so a drive's incidents and its
/// calibration share one identity. Pure I/O - no SCSI - testable with no drive.
/// </summary>
public sealed class DriveRecoveryIncidentStore
{
    private readonly string _path;

    public DriveRecoveryIncidentStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CUETools2026", "recovery-incidents.json");
    }

    public IReadOnlyList<DriveRecoveryIncident> GetHistory(string signature)
    {
        var all = Load();
        return all.TryGetValue(Key(signature), out var list)
            ? list
            : (IReadOnlyList<DriveRecoveryIncident>)Array.Empty<DriveRecoveryIncident>();
    }

    public void Append(string signature, DriveRecoveryIncident incident)
    {
        GzJson.Update<Dictionary<string, List<DriveRecoveryIncident>>, bool>(
            _path,
            loaded =>
            {
                Dictionary<string, List<DriveRecoveryIncident>> all = Load(loaded);
                if (!all.TryGetValue(Key(signature), out var list))
                {
                    list = new List<DriveRecoveryIncident>();
                    all[Key(signature)] = list;
                }
                list.Add(incident);
                return (all, true);
            });
    }

    private Dictionary<string, List<DriveRecoveryIncident>> Load()
    {
        GzJsonLoadResult<Dictionary<string, List<DriveRecoveryIncident>>> loaded =
            GzJson.TryLoad<Dictionary<string, List<DriveRecoveryIncident>>>(_path);
        return Load(loaded);
    }

    private static Dictionary<string, List<DriveRecoveryIncident>> Load(
        GzJsonLoadResult<Dictionary<string, List<DriveRecoveryIncident>>> loaded)
    {
        if (loaded.Status == GzJsonLoadStatus.Failed)
            throw new InvalidDataException(
                "The recovery-incident store exists but could not be read; it was left unchanged.",
                loaded.Error);
        return loaded.Status == GzJsonLoadStatus.Loaded
            ? loaded.Value!
            : new Dictionary<string, List<DriveRecoveryIncident>>();
    }

    private static string Key(string signature) => (signature ?? "").Trim();
}

/// <summary>
/// The guided physical recovery ladder (D-060/D-061). Rungs are human
/// actions only - the app never resets hardware and never elevates - and the
/// dialog verifies each rung live. This policy decides only the ORDER: after
/// the same rung has cured twice consecutively for a drive, the dialog leads
/// with it; every rung always stays reachable.
/// </summary>
public static class RecoveryLadderPolicy
{
    public const string CableReplugRung = "cable-replug";
    public const string PowerCycleRung = "power-cycle";
    public const int ConsecutiveCuresToLead = 2;

    private static readonly string[] DefaultOrder =
        { CableReplugRung, PowerCycleRung };

    /// <summary>
    /// The rung a drive's history has proven, or null. Counts consecutive
    /// cured incidents from the most recent backward; uncured incidents
    /// break the streak (they are evidence the known cure stopped working).
    /// </summary>
    public static string? ProvenCure(IReadOnlyList<DriveRecoveryIncident> history)
    {
        string? candidate = null;
        int streak = 0;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            string cure = history[i].CuringRung ?? "";
            if (cure.Length == 0)
                break;
            if (candidate == null)
            {
                candidate = cure;
                streak = 1;
            }
            else if (cure == candidate)
            {
                streak++;
            }
            else
            {
                break;
            }
            if (streak >= ConsecutiveCuresToLead)
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// The dialog's rung order for this drive: the proven cure leads when one
    /// exists, and every other rung follows in default order. Always returns
    /// the complete ladder.
    /// </summary>
    public static IReadOnlyList<string> RungOrder(
        IReadOnlyList<DriveRecoveryIncident> history)
    {
        string? proven = ProvenCure(history);
        if (proven == null)
            return DefaultOrder;
        var order = new List<string>(DefaultOrder.Length) { proven };
        foreach (string rung in DefaultOrder)
            if (rung != proven)
                order.Add(rung);
        return order;
    }
}
