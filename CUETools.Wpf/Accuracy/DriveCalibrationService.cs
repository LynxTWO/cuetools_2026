using System;
using System.Diagnostics.CodeAnalysis;
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
    // Bump when a newly required capability is added so the first subsequent Rip/Verify/Test &
    // Copy refreshes old records before trusting them. 2026.2 adds real lead-in/out probing.
    internal const string CurrentVersion = "2026.2.0";
    private readonly IDiagnosticLog _log;
    private readonly DriveCalibrationStore _store;

    public DriveCalibrationService(IDiagnosticLog log, DriveCalibrationStore store)
    {
        _log = log;
        _store = store;
    }

    /// <summary>Raised after a complete calibration has been durably saved. Drive-facing views use
    /// this to refresh the displayed evidence after first-use calibration performed by Rip/Verify/
    /// Test &amp; Copy, without opening or otherwise disturbing the drive held by that job.</summary>
    public event Action<DriveCalibration>? CalibrationSaved;

    /// <summary>The saved calibration for a drive signature, or null if it has never been run.</summary>
    public DriveCalibration? Get(string signature) => _store.Get(signature);

    /// <summary>Run the probes on the drive (a disc must be loaded), persist, and return the record.
    /// Returns null if the drive could not be opened / has no audio disc. Safe to call any time
    /// there is no rip in progress; holds the app SCSI gate only for the probe.</summary>
    public DriveCalibration? Calibrate(char drive)
    {
        // the probe opens its own handle, so it counts as drive-busy too
        using var ripScope =
            CUETools.Wpf.Services.DriveService.TryEnterRip(drive, _log);
        if (ripScope == null)
        {
            _log.Warn("calibrate",
                $"drive {drive}: another CUETools job owns the drive");
            return null;
        }
        var reader = new CDDriveReader();
        try
        {
            bool opened;
            lock (DriveService.ScsiGate) opened = reader.Open(drive);
            if (!opened) { _log.Warn("calibrate", $"drive {drive}: no audio disc / not ready"); return null; }

            string sig = (reader.ARName ?? "").Trim();
            DriveCalibration? previous = _store.Get(sig);
            int driveOffset = 0;
            bool driveOffsetKnown = false;
            try
            {
                driveOffsetKnown =
                    CUETools.AccurateRip.AccurateRipVerify.FindDriveReadOffset(
                    reader.ARName,
                    out driveOffset);
            }
            catch (Exception ex)
            {
                // Offset lookup failure is not a reason to lose cache calibration. Probe
                // one sector at each edge; a later run can still safely zero-pad.
                _log.Warn(
                    "calibrate",
                    $"drive {drive}: read-offset lookup failed: {ex.GetType().Name}");
            }
            CDDriveReader.DriveProbe probe;
            int minSpeedKbps;
            lock (DriveService.ScsiGate)
            {
                probe = reader.Probe(driveOffset);
                minSpeedKbps = reader.ProbeMinSpeedKbps();
            }
            if (!probe.Probed)
            {
                // Do not version-stamp and persist a default-valued record as if the
                // required cache and edge probes completed. The previous record stays
                // intact, and the first read gate can retry or fail explicitly.
                _log.Warn(
                    "calibrate",
                    $"drive {drive}: capability probe did not complete");
                return null;
            }

            var cal = new DriveCalibration
            {
                DriveSignature = sig,
                MaxSpeedKbps = (probe.SupportedSpeeds != null && probe.SupportedSpeeds.Length > 0)
                    ? probe.SupportedSpeeds[probe.SupportedSpeeds.Length - 1] : 0,
                MinSpeedKbps = minSpeedKbps,
                CalibratedUtc = DateTime.UtcNow,
                RipperVersion = CurrentVersion,
                ReadOffsetSamples = driveOffset,
                ReadOffsetKnown = driveOffsetKnown,
                OverreadLeadIn = probe.OverreadLeadIn,
                OverreadLeadOut = probe.OverreadLeadOut,
            };

            cal.CacheDefeat = SelectConservativeCacheDefeat(
                probe,
                previous,
                out CalConfidence cacheConfidence);
            cal.CacheConfidence = cacheConfidence;

            try
            {
                _store.Save(cal);
            }
            catch (Exception ex)
            {
                _log.Warn("calibrate", $"drive {drive}: calibration persistence failed: {ex.GetType().Name}");
                throw new DriveCalibrationPersistenceException(
                    "Calibration measurements completed, but could not be saved.", ex);
            }
            _log.Info("calibrate", $"drive {drive}: cache={cal.CacheDefeat} ({cal.CacheConfidence}) " +
                $"maxSpeed={cal.MaxSpeedKbps}kBps minSpeed={cal.MinSpeedKbps}kBps flushEvict={probe.FlushEvictBytes}B " +
                $"offset={(cal.ReadOffsetKnown ? cal.ReadOffsetSamples.ToString() : "unknown")} " +
                $"overreadIn={(cal.OverreadLeadIn ? 1 : 0)} overreadOut={(cal.OverreadLeadOut ? 1 : 0)} " +
                $"read1={probe.FirstReadMs:0}ms reread={probe.ReReadMs:0}ms");
            try { CalibrationSaved?.Invoke(cal); }
            catch (Exception ex)
            {
                // Persistence is already complete. A stale or faulty view must not make the
                // calibration gate report failure after its evidence was safely saved.
                try { _log.Warn("calibrate", "saved-calibration UI notification failed: " + ex.GetType().Name); }
                catch { }
            }
            return cal;
        }
        catch (DriveCalibrationPersistenceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed capability probe is operationally important: it blocks the first
            // Rip/Verify/Test & Copy on this drive. Preserve the scrubbed exception and
            // SCSI sense details instead of collapsing every failure to its CLR type.
            _log.Error("calibrate", $"drive {drive} probe failed", ex);
            return null;
        }
        finally
        {
            try { reader.Close(); } catch { }
        }
    }

    internal static int ParseFlushBytes(string? value)
    {
        if (string.IsNullOrEmpty(value) ||
            !value.StartsWith("Flush:", StringComparison.Ordinal) ||
            !int.TryParse(value.Substring(6), out int bytes) ||
            bytes <= 0)
            return 0;
        return bytes;
    }

    internal static string SelectConservativeCacheDefeat(
        CDDriveReader.DriveProbe probe,
        DriveCalibration? previous,
        out CalConfidence confidence)
    {
        int priorFlush = ParseFlushBytes(previous?.CacheDefeat);

        // Timing can fluctuate enough for the same caching drive to look uncached
        // during a later run. An observed cache is positive evidence; a later failure
        // to observe it is not proof that the hardware stopped caching. Retain the
        // largest flush ever proven safe for this drive signature.
        if (priorFlush > 0)
        {
            int conservativeFlush = Math.Max(priorFlush, probe.FlushEvictBytes);
            confidence = CalConfidence.Confirmed;
            return $"Flush:{conservativeFlush}";
        }

        if (probe.CachesReReads)
        {
            if (probe.FlushEvictBytes > 0)
            {
                confidence = CalConfidence.Confirmed;
                return $"Flush:{probe.FlushEvictBytes}";
            }

            confidence = CalConfidence.Estimated;
            return "Caches re-reads - flush size unknown";
        }

        confidence = CalConfidence.Confirmed;
        return "Media re-reads (no cache)";
    }

    internal static bool IsCurrent(
        [NotNullWhen(true)] DriveCalibration? calibration) =>
        calibration != null &&
        string.Equals(
            calibration.RipperVersion,
            CurrentVersion,
            StringComparison.Ordinal);

    internal static bool CanApplyOverread(
        DriveCalibration? calibration,
        bool currentOffsetKnown,
        int currentOffsetSamples) =>
        calibration != null &&
        calibration.ReadOffsetKnown &&
        currentOffsetKnown &&
        calibration.ReadOffsetSamples == currentOffsetSamples;
}
