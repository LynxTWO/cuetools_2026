using System;
using CUETools.CTDB;
using CUETools.Processor;

namespace CUETools.Wpf.Services;

public sealed class VerifyFilesResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public string Status { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public int TrackCount { get; init; }
    public int ArConfidence { get; init; }
    public int ArTotal { get; init; }
    public int CtdbConfidence { get; init; }
    public int CtdbTotal { get; init; }
    public bool Accurate { get; init; }
    public bool HasErrors { get; init; }
    public bool CanRecover { get; init; }
    public string Source { get; init; } = "";

    // CTDB Reed-Solomon repair detail, read straight off the matching DBEntry.repair after the verify
    // pass (the same data the destructive repair would apply). All real: RepairSamples is the count of
    // 16-bit samples the parity can/did reconstruct, RepairSectorMap is a downsampled view of exactly
    // which sectors were damaged (from AffectedSectorArray), RepairNpar the parity depth used.
    public int RepairSamples { get; init; }
    public int RepairSectors { get; init; }
    public int RepairTotalSectors { get; init; }
    public int RepairNpar { get; init; }
    public double[] RepairSectorMap { get; init; } = System.Array.Empty<double>();
    public string RepairRanges { get; init; } = "";
    public bool RepairApplied { get; init; }   // true after the repair pass actually rewrote the audio
}

/// <summary>Verify and repair existing audio files (a .cue, a single file, or a folder) against
/// AccurateRip and CTDB - the file-based twin of the disc rip. Blocking + network, so callers
/// marshal it onto a background thread.</summary>
public interface IVerifyService
{
    VerifyFilesResult Verify(string path, Action<double, string> onProgress);

    /// <summary>Drive the engine's "repair" script: verify, then re-encode the affected sectors
    /// from CTDB Reed-Solomon parity. Only meaningful when a prior Verify reported CanRecover.
    /// Rewrites the audio the source cue points at (CTDB's in-place repair semantics).</summary>
    VerifyFilesResult Repair(string path, Action<double, string> onProgress);
}

public sealed class VerifyService : IVerifyService
{
    private readonly CUEConfig _config;
    private readonly IDiagnosticLog _log;

    public VerifyService(CUEConfig config, IDiagnosticLog log) { _config = config; _log = log; }

    public VerifyFilesResult Verify(string path, Action<double, string> onProgress)
    {
        try
        {
            var cue = new CUESheet(_config);
            cue.CUEToolsProgress += (s, e) => onProgress(Clamp(e.percent), e.status);
            cue.Open(path);

            cue.UseCUEToolsDB("CUETools 2026", null, true, CTDBMetadataSearch.Fast);
            cue.UseAccurateRip();
            cue.Action = CUEAction.Verify;

            onProgress(0, "Verifying against AccurateRip + CTDB...");
            string status = cue.Go();
            onProgress(1, status);

            return Gather(cue, status, path, ok: true, error: "", applied: false);
        }
        catch (Exception ex)
        {
            _log.Error("verify", "file verify failed", ex);
            return new VerifyFilesResult { Error = ex.Message, Source = path };
        }
    }

    public VerifyFilesResult Repair(string path, Action<double, string> onProgress)
    {
        try
        {
            var cue = new CUESheet(_config);
            cue.CUEToolsProgress += (s, e) => onProgress(Clamp(e.percent), e.status);
            // When more than one CTDB entry is recoverable, take the first; a lone entry is
            // auto-selected by ChooseFile(quietIfSingle) without firing this.
            cue.CUEToolsSelection += (s, e) => e.selection = 0;
            cue.Open(path);

            if (!_config.scripts.TryGetValue("repair", out var repair))
                return new VerifyFilesResult { Error = "Repair script not available.", Source = path };

            onProgress(0, "Repairing from CTDB parity...");
            string status = cue.ExecuteScript(repair);
            onProgress(1, status);

            return Gather(cue, status, path, ok: true, error: "", applied: true);
        }
        catch (Exception ex)
        {
            _log.Error("verify", "repair failed", ex);
            return new VerifyFilesResult { Error = ex.Message, Source = path };
        }
    }

    private VerifyFilesResult Gather(CUESheet cue, string status, string path, bool ok, string error, bool applied)
    {
        int arConf = 0, arTotal = 0;
        // a throw here must not be reported as "not in database" - that is a different fact
        try { arConf = (int)cue.ArVerify.WorstConfidence(); arTotal = (int)cue.ArVerify.WorstTotal(); }
        catch (Exception ex) { _log.Warn("verify", "AccurateRip result read failed (shown as not in database): " + ex.GetType().Name); }

        int ctConf = 0, ctTotal = 0;
        bool hasErrors = false, canRecover = false;
        DBEntry? rep = null;
        try
        {
            ctConf = cue.CTDB.Confidence;
            ctTotal = cue.CTDB.Total;
            foreach (DBEntry e in cue.CTDB.Entries)
            {
                if (e.hasErrors) hasErrors = true;
                if (e.canRecover) canRecover = true;
                // the entry we can actually reconstruct from (has real corrections computed)
                if (rep == null && e.hasErrors && e.canRecover && e.repair != null) rep = e;
            }
            if (rep == null) { var se = cue.CTDB.SelectedEntry; if (se != null && se.repair != null && se.hasErrors) rep = se; }
        }
        // if this walk throws, CanRepair would silently never enable - repairable damage unreported
        catch (Exception ex) { _log.Warn("verify", "CTDB entries walk failed (repair state may be understated): " + ex.GetType().Name); }

        // Pull the real Reed-Solomon repair detail off the chosen entry: how many samples parity can
        // reconstruct, the parity depth, and the exact damaged-sector map (downsampled for drawing).
        int repSamples = 0, repSectors = 0, repTotal = 0, repNpar = 0;
        double[] repMap = System.Array.Empty<double>();
        string repRanges = "";
        if (rep != null)
        {
            try
            {
                var fx = rep.repair;
                repSamples = fx.CorrectableErrors;
                repNpar = rep.Npar;
                var arr = fx.AffectedSectorArray;   // one bit per CD sector, true = a sample there was corrected
                repTotal = arr.Length;
                const int B = 200;
                repMap = new double[B];
                int hit = 0;
                for (int i = 0; i < arr.Length; i++)
                    if (arr[i]) { hit++; repMap[(int)((long)i * B / System.Math.Max(1, arr.Length))] += 1; }
                repSectors = hit;
                double per = System.Math.Max(1.0, (double)arr.Length / B);
                for (int b = 0; b < B; b++) repMap[b] = System.Math.Min(1.0, repMap[b] / per);
                try { repRanges = fx.AffectedSectors; } catch { }
            }
            // cosmetic blast radius only (the repair scope hides; CanRepair derives from the walk above)
            catch (Exception ex) { _log.Warn("verify", "repair detail extraction failed: " + ex.GetType().Name); }
        }

        return new VerifyFilesResult
        {
            Ok = ok,
            Error = error,
            Status = status,
            Artist = cue.Metadata?.Artist ?? "",
            Album = cue.Metadata?.Title ?? "",
            TrackCount = cue.TrackCount,
            ArConfidence = arConf,
            ArTotal = arTotal,
            CtdbConfidence = ctConf,
            CtdbTotal = ctTotal,
            Accurate = arConf > 0,
            HasErrors = hasErrors,
            CanRecover = canRecover,
            Source = path,
            RepairSamples = repSamples,
            RepairSectors = repSectors,
            RepairTotalSectors = repTotal,
            RepairNpar = repNpar,
            RepairSectorMap = repMap,
            RepairRanges = repRanges,
            RepairApplied = applied && rep != null
        };
    }

    private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
