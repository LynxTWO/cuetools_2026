using System;
using System.IO;
using CUETools.AccurateRip;
using CUETools.CTDB;
using CUETools.Processor;
using CUETools.Ripper.SCSI;

namespace CUETools.Wpf.Services;

public sealed class VerifyResult
{
    public bool Ok { get; init; }
    public string Status { get; init; } = "";
    public string Error { get; init; } = "";
    public int ArConfidence { get; init; }
    public int ArTotal { get; init; }
    public int CtdbConfidence { get; init; }
    public int CtdbTotal { get; init; }
    public bool Accurate { get; init; }
}

public interface IRipService
{
    /// <summary>Run the real CUESheet verify pipeline against the disc (reads the whole disc,
    /// checks AccurateRip + CTDB, writes nothing). correctionQuality: 0 burst, 1 secure,
    /// 2 paranoid. onProgress reports (fraction 0..1, status) on the ripper's thread.</summary>
    VerifyResult RunVerify(char drive, int correctionQuality, Action<double, string> onProgress);
}

public sealed class RipService : IRipService
{
    private readonly CUEConfig _config;

    public RipService(CUEConfig config) => _config = config;

    public VerifyResult RunVerify(char drive, int correctionQuality, Action<double, string> onProgress)
    {
        var reader = new CDDriveReader();
        try
        {
            if (!reader.Open(drive)) return new VerifyResult { Error = "No disc." };

            int offset = 0;
            try { AccurateRipVerify.FindDriveReadOffset(reader.ARName, out offset); } catch { }
            reader.DriveOffset = offset;
            reader.CorrectionQuality = Math.Max(0, Math.Min(2, correctionQuality));

            var cue = new CUESheet(_config);
            cue.OpenCD(reader);
            try { var rel = cue.LookupAlbumInfo(false, false, true, CTDBMetadataSearch.Fast); if (rel.Count > 0) cue.CopyMetadata(((CUEMetadataEntry)rel[0]).metadata); } catch { }

            cue.UseCUEToolsDB("CUETools 2026", reader.ARName, false, CTDBMetadataSearch.Fast);
            cue.UseAccurateRip();
            cue.ArTestVerify = null;
            cue.OutputStyle = CUEStyle.GapsAppended;
            cue.Action = CUEAction.Verify;
            cue.GenerateFilenames(AudioEncoderType.Lossless, "flac", Path.Combine(Path.GetTempPath(), "cueverify", "v.cue"));

            double total = Math.Max(1, reader.TOC.AudioLength);
            double lastFrac = -1;
            reader.ReadProgress += (s, e) =>
            {
                double frac = e.Position / total;
                if (frac - lastFrac >= 0.004 || frac >= 1.0)
                {
                    lastFrac = frac;
                    onProgress(Math.Min(1.0, Math.Max(0.0, frac)), $"Verifying against AccurateRip... {(int)(frac * 100)}%");
                }
            };

            onProgress(0, "Verifying against AccurateRip + CTDB...");
            string status = cue.Go();
            onProgress(1, status);

            int arConf = 0, arTotal = 0, ctConf = cue.CTDB.Confidence, ctTotal = cue.CTDB.Total;
            try { arConf = (int)cue.ArVerify.WorstConfidence(); arTotal = (int)cue.ArVerify.WorstTotal(); } catch { }

            return new VerifyResult
            {
                Ok = true,
                Status = status,
                ArConfidence = arConf,
                ArTotal = arTotal,
                CtdbConfidence = ctConf,
                CtdbTotal = ctTotal,
                Accurate = arConf > 0
            };
        }
        catch (Exception ex)
        {
            return new VerifyResult { Error = ex.Message };
        }
        finally
        {
            try { reader.Close(); } catch { }
        }
    }
}
