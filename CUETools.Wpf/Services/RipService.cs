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
    public string OutputDir { get; init; } = "";
    public int FileCount { get; init; }
}

public interface IRipService
{
    /// <summary>Verify the disc against AccurateRip + CTDB (reads the whole disc, writes nothing).</summary>
    VerifyResult RunVerify(char drive, int correctionQuality, Action<double, string> onProgress);

    /// <summary>Rip the disc to FLAC (read + encode + verify) under Music\CUETools\Artist - Album.</summary>
    VerifyResult RunEncode(char drive, int correctionQuality, Action<double, string> onProgress);
}

public sealed class RipService : IRipService
{
    private readonly CUEConfig _config;

    public RipService(CUEConfig config) => _config = config;

    public VerifyResult RunVerify(char drive, int cq, Action<double, string> onProgress) => Run(drive, cq, encode: false, onProgress);
    public VerifyResult RunEncode(char drive, int cq, Action<double, string> onProgress) => Run(drive, cq, encode: true, onProgress);

    private VerifyResult Run(char drive, int cq, bool encode, Action<double, string> onProgress)
    {
        var reader = new CDDriveReader();
        try
        {
            if (!reader.Open(drive)) return new VerifyResult { Error = "No disc." };

            int offset = 0;
            try { AccurateRipVerify.FindDriveReadOffset(reader.ARName, out offset); } catch { }
            reader.DriveOffset = offset;
            reader.CorrectionQuality = Math.Max(0, Math.Min(2, cq));

            var cue = new CUESheet(_config);
            cue.OpenCD(reader);
            try { var rel = cue.LookupAlbumInfo(false, false, true, CTDBMetadataSearch.Fast); if (rel.Count > 0) cue.CopyMetadata(((CUEMetadataEntry)rel[0]).metadata); } catch { }

            cue.UseCUEToolsDB("CUETools 2026", reader.ARName, false, CTDBMetadataSearch.Fast);
            cue.UseAccurateRip();
            cue.ArTestVerify = null;
            cue.OutputStyle = CUEStyle.GapsAppended;

            string outDir = "";
            if (encode)
            {
                cue.Action = CUEAction.Encode;
                string artist = Safe(cue.Metadata.Artist), title = Safe(cue.Metadata.Title);
                string album = (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)) ? "Unknown Album" : $"{artist} - {title}";
                outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CUETools", album);
                Directory.CreateDirectory(outDir);
                cue.GenerateFilenames(AudioEncoderType.Lossless, "flac", Path.Combine(outDir, "album.cue"));
            }
            else
            {
                cue.Action = CUEAction.Verify;
                cue.GenerateFilenames(AudioEncoderType.Lossless, "flac", Path.Combine(Path.GetTempPath(), "cueverify", "v.cue"));
            }

            double total = Math.Max(1, reader.TOC.AudioLength);
            double lastFrac = -1;
            reader.ReadProgress += (s, e) =>
            {
                double frac = e.Position / total;
                if (frac - lastFrac >= 0.004 || frac >= 1.0)
                {
                    lastFrac = frac;
                    onProgress(Math.Min(1.0, Math.Max(0.0, frac)), (encode ? "Ripping" : "Verifying") + $"... {(int)(frac * 100)}%");
                }
            };

            onProgress(0, encode ? "Ripping + verifying..." : "Verifying against AccurateRip + CTDB...");
            string status = cue.Go();
            onProgress(1, status);

            int arConf = 0, arTotal = 0, ctConf = cue.CTDB.Confidence, ctTotal = cue.CTDB.Total;
            try { arConf = (int)cue.ArVerify.WorstConfidence(); arTotal = (int)cue.ArVerify.WorstTotal(); } catch { }
            int files = 0;
            try { if (encode && Directory.Exists(outDir)) files = Directory.GetFiles(outDir, "*.flac").Length; } catch { }

            return new VerifyResult
            {
                Ok = true,
                Status = status,
                ArConfidence = arConf,
                ArTotal = arTotal,
                CtdbConfidence = ctConf,
                CtdbTotal = ctTotal,
                Accurate = arConf > 0,
                OutputDir = outDir,
                FileCount = files
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

    private string Safe(string s) => string.IsNullOrEmpty(s) ? "" : _config.CleanseString(s);
}
