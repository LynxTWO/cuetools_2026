using System;
using CUETools.Ripper.SCSI;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.Accuracy;

/// <summary>
/// Owns the hardware side of the per-drive calibration: opens the drive, runs the read-only
/// probes (cache behaviour, and the supported-speed max), maps the result to a <see
/// cref="DriveCalibration"/> record with honest confidence labels, and persists it keyed by the
/// drive's AccurateRip signature. Everything here is a capability probe run OUTSIDE a rip - it
/// never writes rip output and cannot affect accuracy. A drive is characterised once; a normal
/// rip just reads the saved record (the reader/UI does the reading).
/// </summary>
public sealed class DriveCalibrationService
{
    private const string Version = "2026.1.0";
    private readonly IDiagnosticLog _log;
    private readonly DriveCalibrationStore _store;

    public DriveCalibrationService(IDiagnosticLog log, DriveCalibrationStore store)
    {
        _log = log;
        _store = store;
    }

    /// <summary>The saved calibration for a drive signature, or null if it has never been run.</summary>
    public DriveCalibration Get(string signature) => _store.Get(signature);

    /// <summary>Run the probes on the drive (a disc must be loaded), persist, and return the record.
    /// Returns null if the drive could not be opened / has no audio disc. Safe to call any time
    /// there is no rip in progress; holds the app SCSI gate only for the probe.</summary>
    public DriveCalibration Calibrate(char drive)
    {
        var reader = new CDDriveReader();
        try
        {
            bool opened;
            lock (DriveService.ScsiGate) opened = reader.Open(drive);
            if (!opened) { _log.Warn("calibrate", $"drive {drive}: no audio disc / not ready"); return null; }

            string sig = (reader.ARName ?? "").Trim();
            CDDriveReader.DriveProbe probe;
            int minSpeedKbps;
            lock (DriveService.ScsiGate) { probe = reader.Probe(); minSpeedKbps = reader.ProbeMinSpeedKbps(); }

            var cal = new DriveCalibration
            {
                DriveSignature = sig,
                MaxSpeedKbps = (probe.SupportedSpeeds != null && probe.SupportedSpeeds.Length > 0)
                    ? probe.SupportedSpeeds[probe.SupportedSpeeds.Length - 1] : 0,
                MinSpeedKbps = minSpeedKbps,
                CalibratedUtc = DateTime.UtcNow,
                RipperVersion = Version,
                // overread not probed yet (finicky lead-in/out addressing) - left false, a follow-up
                OverreadLeadIn = false,
                OverreadLeadOut = false,
            };

            if (!probe.Probed)
            {
                cal.CacheDefeat = "Unconfirmed";
                cal.CacheConfidence = CalConfidence.Unconfirmed;
            }
            else if (probe.CachesReReads)
            {
                // the drive served the re-read from cache: a secure re-read must flush it. Sizing the
                // flush is a follow-up (CacheDefeatSearch); for now record the conservative fallback.
                cal.CacheDefeat = "Caches re-reads - flush needed";
                cal.CacheConfidence = CalConfidence.Estimated;
            }
            else
            {
                // the re-read cost as much as the first: the drive genuinely re-reads from media, so a
                // secure re-read is already a real read - no cache defeat needed.
                cal.CacheDefeat = "Media re-reads (no cache)";
                cal.CacheConfidence = CalConfidence.Confirmed;
            }

            _store.Save(cal);
            _log.Info("calibrate", $"drive {drive}: cache={cal.CacheDefeat} ({cal.CacheConfidence}) " +
                $"maxSpeed={cal.MaxSpeedKbps}kBps minSpeed={cal.MinSpeedKbps}kBps read1={probe.FirstReadMs:0}ms reread={probe.ReReadMs:0}ms");
            return cal;
        }
        catch (Exception ex)
        {
            _log.Warn("calibrate", $"drive {drive} probe failed: {ex.GetType().Name}");
            return null;
        }
        finally
        {
            try { reader.Close(); } catch { }
        }
    }
}
