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

    public VerifyService(CUEConfig config) => _config = config;

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

            return Gather(cue, status, path, ok: true, error: "");
        }
        catch (Exception ex)
        {
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

            return Gather(cue, status, path, ok: true, error: "");
        }
        catch (Exception ex)
        {
            return new VerifyFilesResult { Error = ex.Message, Source = path };
        }
    }

    private static VerifyFilesResult Gather(CUESheet cue, string status, string path, bool ok, string error)
    {
        int arConf = 0, arTotal = 0;
        try { arConf = (int)cue.ArVerify.WorstConfidence(); arTotal = (int)cue.ArVerify.WorstTotal(); } catch { }

        int ctConf = 0, ctTotal = 0;
        bool hasErrors = false, canRecover = false;
        try
        {
            ctConf = cue.CTDB.Confidence;
            ctTotal = cue.CTDB.Total;
            foreach (DBEntry e in cue.CTDB.Entries)
            {
                if (e.hasErrors) hasErrors = true;
                if (e.canRecover) canRecover = true;
            }
        }
        catch { }

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
            Source = path
        };
    }

    private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
