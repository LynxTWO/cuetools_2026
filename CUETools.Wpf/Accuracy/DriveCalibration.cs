using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CUETools.Wpf.Accuracy;

/// <summary>Per-feature confidence label from calibration (spec: honest degradation).</summary>
public enum CalConfidence { Unconfirmed, Estimated, Confirmed }

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
        var all = Load();
        all[Key(cal.DriveSignature)] = cal;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best-effort */ }
    }

    private Dictionary<string, DriveCalibration> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Dictionary<string, DriveCalibration>>(File.ReadAllText(_path))
                       ?? new Dictionary<string, DriveCalibration>();
        }
        catch { }
        return new Dictionary<string, DriveCalibration>();
    }

    private static string Key(string signature) => (signature ?? "").Trim();
}
