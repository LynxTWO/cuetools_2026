using System;
using System.Collections.Generic;
using System.IO;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.Accuracy;

/// <summary>Per-feature confidence label from calibration (spec: honest degradation).</summary>
public enum CalConfidence { Unconfirmed, Estimated, Confirmed }

/// <summary>The hardware probes completed, but their result could not be persisted.</summary>
public sealed class DriveCalibrationPersistenceException : IOException
{
    public DriveCalibrationPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// One drive's calibration record (spec: the hybrid per-drive model). Keyed by the drive's
/// AccurateRip signature so a drive is characterized once and never re-probed on a normal rip.
/// Cache defeat / overread / max speed are filled by hardware probes; this type just holds and
/// persists the result.
/// </summary>
public sealed class DriveCalibration
{
    public string DriveSignature { get; set; } = "";
    public string CacheDefeat { get; set; } = "Unconfirmed";   // "FUA" | "Flush:<bytes>" | "Unconfirmed"
    public CalConfidence CacheConfidence { get; set; } = CalConfidence.Unconfirmed;
    /// <summary>The AccurateRip read offset whose full edge range was probed.</summary>
    public int ReadOffsetSamples { get; set; }
    /// <summary>False when offset lookup was unavailable; overread must then remain disabled.</summary>
    public bool ReadOffsetKnown { get; set; }
    public bool OverreadLeadIn { get; set; }
    public bool OverreadLeadOut { get; set; }
    public int MaxSpeedKbps { get; set; }
    public int MinSpeedKbps { get; set; }   // lowest read speed the drive accepts (probed); 0 = unknown
    public DateTime CalibratedUtc { get; set; }
    public string RipperVersion { get; set; } = "";
}

/// <summary>
/// Loads/saves the whole calibration table (signature -> record) as JSON under the user's app
/// data. Pure I/O - no SCSI - so it is testable with no drive. The DriveCalibrationService (which
/// owns the hardware probes) reads/writes through this.
/// </summary>
public sealed class DriveCalibrationStore
{
    private readonly string _path;

    public DriveCalibrationStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CUETools2026", "drive-calibration.json");
    }

    public DriveCalibration? Get(string signature)
    {
        var all = Load();
        return all.TryGetValue(Key(signature), out var c) ? c : null;
    }

    public void Save(DriveCalibration cal)
    {
        GzJson.Update<Dictionary<string, DriveCalibration>, bool>(
            _path,
            loaded =>
            {
                Dictionary<string, DriveCalibration> all = Load(loaded);
                all[Key(cal.DriveSignature)] = cal;
                return (all, true);
            });
    }

    private Dictionary<string, DriveCalibration> Load()
    {
        GzJsonLoadResult<Dictionary<string, DriveCalibration>> loaded =
            GzJson.TryLoad<Dictionary<string, DriveCalibration>>(_path);
        return Load(loaded);
    }

    private static Dictionary<string, DriveCalibration> Load(
        GzJsonLoadResult<Dictionary<string, DriveCalibration>> loaded)
    {
        if (loaded.Status == GzJsonLoadStatus.Failed)
            throw new InvalidDataException(
                "The drive-calibration store exists but could not be read; it was left unchanged.",
                loaded.Error);
        return loaded.Status == GzJsonLoadStatus.Loaded
            ? loaded.Value!
            : new Dictionary<string, DriveCalibration>();
    }

    private static string Key(string signature) => (signature ?? "").Trim();
}
