using System;
using System.IO;
using CUETools.AccurateRip;
using CUETools.CTDB;
using CUETools.Processor;
using CUETools.Ripper;
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

    /// <summary>Per-audio-track AccurateRip / CTDB confidence, index-aligned to the track list.</summary>
    public int[] ArPerTrack { get; init; } = System.Array.Empty<int>();
    public int[] CtdbPerTrack { get; init; } = System.Array.Empty<int>();
}

public interface IRipService
{
    /// <summary>Verify the disc against AccurateRip + CTDB (reads the whole disc, writes nothing).
    /// <paramref name="onLevels"/> receives real per-channel peak (L,R) of each read buffer.
    /// <paramref name="metadata"/>, when given, is the release the user chose (else auto-picked).</summary>
    VerifyResult RunVerify(char drive, int correctionQuality, CUEMetadata? metadata, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null);

    /// <summary>Rip the disc (read + encode + verify) to the given format under
    /// <paramref name="outputBaseDir"/>\Artist - Album, using the chosen release metadata when
    /// given. <paramref name="onSamples"/> receives a window of real consecutive PCM samples for
    /// the codec scope.</summary>
    VerifyResult RunEncode(char drive, int correctionQuality, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null);
}

public sealed class RipService : IRipService
{
    private readonly CUEConfig _config;
    private readonly IDiagnosticLog _log;

    public RipService(CUEConfig config, IDiagnosticLog log) { _config = config; _log = log; }

    public VerifyResult RunVerify(char drive, int cq, CUEMetadata? metadata, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null) => Run(drive, cq, encode: false, "flac", metadata, "", onProgress, onLevels, onSamples);
    public VerifyResult RunEncode(char drive, int cq, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null) => Run(drive, cq, encode: true, string.IsNullOrWhiteSpace(format) ? "flac" : format, metadata, outputBaseDir, onProgress, onLevels, onSamples);

    private VerifyResult Run(char drive, int cq, bool encode, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels, Action<float[]>? onSamples = null)
    {
        var reader = new CDDriveReader();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (!reader.Open(drive)) { _log.Warn("rip", "no disc / not ready"); return new VerifyResult { Error = "No disc." }; }

            int offset = 0;
            try { AccurateRipVerify.FindDriveReadOffset(reader.ARName, out offset); } catch { }
            reader.DriveOffset = offset;
            reader.CorrectionQuality = Math.Max(0, Math.Min(2, cq));
            _log.Info("rip", $"start mode={(encode ? "encode" : "verify")} format={format} cq={cq} offset={offset} drive='{(reader.ARName ?? "").Trim()}' chosen_release={(metadata != null)}");

            // Tap real audio for the VU meter (levels) and the codec scope (a window of real
            // samples); everything else delegates to the drive unchanged.
            ICDRipper ripper = (onLevels != null || onSamples != null)
                ? new LevelMeteringRipper(reader, onLevels ?? ((_, _) => { }), onSamples)
                : reader;

            var cue = new CUESheet(_config);
            cue.OpenCD(ripper);
            if (metadata != null)
            {
                try { cue.CopyMetadata(metadata); } catch { }   // honor the user's chosen release
            }
            else
            {
                try { var rel = cue.LookupAlbumInfo(false, false, true, CTDBMetadataSearch.Fast); if (rel.Count > 0) cue.CopyMetadata(((CUEMetadataEntry)rel[0]).metadata); } catch { }
            }
            // from here on, any album/artist text (incl. in paths or errors) is scrubbed from the log
            _log.Redact(cue.Metadata?.Artist, cue.Metadata?.Title);

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
                string baseDir = string.IsNullOrWhiteSpace(outputBaseDir)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CUETools")
                    : outputBaseDir;
                outDir = Path.Combine(baseDir, album);
                Directory.CreateDirectory(outDir);
                cue.GenerateFilenames(AudioEncoderType.Lossless, format, Path.Combine(outDir, "album.cue"));
                onProgress(0, $"Encoding to {format.ToUpperInvariant()} -> {outDir}");
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
            try { if (encode && Directory.Exists(outDir)) files = Directory.GetFiles(outDir, "*." + format).Length; } catch { }

            _log.Info("rip", $"done mode={(encode ? "encode" : "verify")} elapsed={sw.Elapsed.TotalSeconds:0}s " +
                $"ar_conf={arConf}/{arTotal} ctdb_conf={ctConf}/{ctTotal} accurate={arConf > 0} files={files} status={status}");

            int n = Math.Max(0, cue.TrackCount);
            var arpt = new int[n];
            var ctpt = new int[n];
            for (int t = 0; t < n; t++)
            {
                try { arpt[t] = (int)cue.ArVerify.Confidence(t); } catch { }
                try { ctpt[t] = cue.CTDB.GetConfidence(t); } catch { }
            }

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
                FileCount = files,
                ArPerTrack = arpt,
                CtdbPerTrack = ctpt
            };
        }
        catch (Exception ex)
        {
            _log.Error("rip", $"failed after {sw.Elapsed.TotalSeconds:0}s", ex);
            return new VerifyResult { Error = ex.Message };
        }
        finally
        {
            try { reader.Close(); } catch { }
        }
    }

    private string Safe(string s) => string.IsNullOrEmpty(s) ? "" : _config.CleanseString(s);
}
