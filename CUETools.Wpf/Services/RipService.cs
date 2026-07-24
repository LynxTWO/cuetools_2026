using System;
using System.IO;
using CUETools.AccurateRip;
using CUETools.CTDB;
using CUETools.Processor;
using CUETools.Ripper;
using CUETools.Ripper.SCSI;
using CUETools.Wpf.Accuracy;

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
    /// <paramref name="onLevels"/> receives the real per-channel RMS loudness (L,R) of each read.
    /// <paramref name="onReread"/> reports a real sector re-read: (reReads, maxReReads, errorSectors,
    /// discFrac); reReads &gt; 0 only when the drive is doing extra passes over a stuck window.
    /// <paramref name="metadata"/>, when given, is the release the user chose (else auto-picked).</summary>
    VerifyResult RunVerify(char drive, int correctionQuality, CUEMetadata? metadata, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null);

    /// <summary>Rip the disc (read + encode + verify) to the given format under
    /// <paramref name="outputBaseDir"/>\Artist - Album, using the chosen release metadata when
    /// given. <paramref name="onSamples"/> receives a window of real consecutive PCM samples for
    /// the codec scope. <paramref name="onReread"/> reports real sector re-reads (see RunVerify).
    /// <paramref name="coverArt"/>, when given, is the hi-res cover to embed (already resized); the
    /// engine's database cover is used when it is null.</summary>
    VerifyResult RunEncode(char drive, int correctionQuality, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null);

    /// <summary>Ask the running rip/verify to stop at the next safe point. No-op if nothing runs.</summary>
    void Stop();
}

public sealed class RipService : IRipService
{
    private readonly CUEConfig _config;
    private readonly IDiagnosticLog _log;
    private readonly AppSettings _settings;
    private readonly EncoderCatalog _catalog;
    private CUESheet? _current;   // the running sheet, so Stop() can abort it
    private readonly object _stopGate = new();

    public RipService(CUEConfig config, IDiagnosticLog log, AppSettings settings, EncoderCatalog catalog)
    { _config = config; _log = log; _settings = settings; _catalog = catalog; }

    public void Stop()
    {
        CUESheet? cue; lock (_stopGate) cue = _current;
        try { cue?.Stop(); _log.Info("rip", "stop requested"); }
        catch (Exception ex) { _log.Warn("rip", "stop request failed: " + ex.GetType().Name); }
    }

    // Keep the machine awake for the duration of a rip. ES_CONTINUOUS persists the request until it
    // is cleared, so it does not matter which thread sets it.
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
    private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001, ES_DISPLAY_REQUIRED = 0x00000002;
    private void KeepAwake(bool on)
    {
        // returns 0 on failure - the machine could then sleep mid-rip, so leave a trace
        if (SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED : ES_CONTINUOUS) == 0 && on)
            _log.Warn("rip", "keep-awake request rejected - the system may sleep during this rip");
    }

    public VerifyResult RunVerify(char drive, int cq, CUEMetadata? metadata, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null) => Run(drive, cq, encode: false, "flac", metadata, "", onProgress, onLevels, onSamples, onReread);
    public VerifyResult RunEncode(char drive, int cq, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null) => Run(drive, cq, encode: true, string.IsNullOrWhiteSpace(format) ? "flac" : format, metadata, outputBaseDir, onProgress, onLevels, onSamples, onReread, coverArt);

    private VerifyResult Run(char drive, int cq, bool encode, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null)
    {
        var reader = new CDDriveReader();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // open under the app-wide device gate so a rip start cannot collide with an in-flight
            // tray poll / capability query (the gate is held only for the open, not the whole rip)
            bool opened;
            lock (DriveService.ScsiGate) opened = reader.Open(drive);
            if (!opened) { _log.Warn("rip", "no disc / not ready"); return new VerifyResult { Error = "No disc." }; }

            int offset = 0;
            try { AccurateRipVerify.FindDriveReadOffset(reader.ARName, out offset); }
            catch (Exception ex) { _log.Warn("rip", "read-offset lookup failed - ripping with offset 0: " + ex.GetType().Name); }
            reader.DriveOffset = offset;
            reader.CorrectionQuality = Math.Max(0, Math.Min(2, cq));
            reader.DeepRecovery = _settings.DeepRecovery;
            if (reader.DeepRecovery) _log.Info("rip", "deep recovery ON: progress-aware cap + slow-to-floor + slip probe");

            // Adaptive read speed (Feature 3): start at the drive's max, drop a step when the drive
            // gets stuck on a window, ease back up after clean stretches. Only REQUESTS are made
            // here; the reader applies them at the next fresh-window boundary on its own read
            // thread (a mid-window SET CD SPEED crashed the read - see PrefetchSector). The audio
            // is identical at any speed, so accuracy is unaffected either way.
            AdaptiveSpeedController speedCtl = null;
            int lastRequested = 0;
            if (_settings.AdaptiveReadSpeed)
            {
                int[] speeds = reader.GetSupportedSpeeds();
                if (speeds.Length > 1)
                {
                    speedCtl = new AdaptiveSpeedController(speeds);
                    lastRequested = speedCtl.CurrentSpeed;
                    reader.RequestReadSpeed(lastRequested);
                    _log.Info("rip", $"adaptive speed on: {speeds.Length} steps {speeds[0]}-{speeds[speeds.Length - 1]} kB/s, start {lastRequested} ({lastRequested / 176}x)");
                }
                else _log.Info("rip", "adaptive speed: drive reports no speed list - using drive default");
            }
            void RequestSpeed()
            {
                if (speedCtl == null || speedCtl.CurrentSpeed == lastRequested) return;
                lastRequested = speedCtl.CurrentSpeed;
                reader.RequestReadSpeed(lastRequested);
                _log.Info("rip.speed", $"read speed request -> {lastRequested} kB/s ({lastRequested / 176}x)");
            }

            // keep the machine awake for the whole read; optionally lock the tray so the disc cannot
            // be ejected mid-read (which would fail the read and can crash the drive layer).
            if (_settings.PreventSleepDuringRip) KeepAwake(true);
            if (_settings.LockTrayDuringRip) { try { reader.DisableEjectDisc(true); } catch (Exception ex) { _log.Warn("rip", "tray lock failed: " + ex.GetType().Name); } }

            _log.Info("rip", $"start mode={(encode ? "encode" : "verify")} format={format} cq={cq} offset={offset} drive='{(reader.ARName ?? "").Trim()}' " +
                $"chosen_release={(metadata != null)} preventSleep={_settings.PreventSleepDuringRip} lockTray={_settings.LockTrayDuringRip}");

            // Tap real audio for the VU meter (levels) and the codec scope (a window of real
            // samples); everything else delegates to the drive unchanged.
            ICDRipper ripper = (onLevels != null || onSamples != null)
                ? new LevelMeteringRipper(reader, onLevels ?? ((_, _) => { }), onSamples)
                : reader;

            var cue = new CUESheet(_config);
            lock (_stopGate) _current = cue;   // so Stop() can abort this run
            cue.OpenCD(ripper);
            if (metadata != null)
            {
                // honor the user's chosen release; if it cannot be applied the rip would proceed with
                // generic tags, so say so rather than silently discarding an explicit choice
                try { cue.CopyMetadata(metadata); }
                catch (Exception ex)
                {
                    _log.Warn("rip", "chosen release metadata not applied: " + ex.GetType().Name);
                    onProgress(0, "Warning: the chosen release's metadata could not be applied.");
                }
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
                // pick the encoder type from the format via the catalog's single rule: a format
                // with a USABLE lossy encoder encodes lossy (mp3 bundled, wma OS runtime, mpc when
                // its exe has been imported)
                bool lossy = _config.formats.TryGetValue(format, out var fmtInfo) && _catalog.IsLossyFormat(fmtInfo);
                cue.GenerateFilenames(lossy ? AudioEncoderType.Lossy : AudioEncoderType.Lossless, format, Path.Combine(outDir, "album.cue"));
                onProgress(0, $"Encoding to {format.ToUpperInvariant()}{(lossy ? " (lossy)" : "")} -> {outDir}");
            }
            else
            {
                cue.Action = CUEAction.Verify;
                cue.GenerateFilenames(AudioEncoderType.Lossless, "flac", Path.Combine(Path.GetTempPath(), "cueverify", "v.cue"));
            }

            double total = Math.Max(1, reader.TOC.AudioLength);
            double lastFrac = -1;
            // re-read reporting: the drive guarantees (cqc + 1) clean passes per window and breaks
            // early once they agree; any pass BEYOND that is a real re-read of a stuck window. The cap
            // is (16 << cqc) total passes, so maxReReads extra passes before it gives up.
            int cqc = Math.Max(0, Math.Min(2, cq));
            int maxReReads = Math.Max(1, (16 << cqc) - 1 - cqc);
            int lastReReads = 0, peakReRead = 0, rereadWindows = 0, failedWindows = 0;
            double lastEaseFrac = 0;   // progress point of the last speed ease-up
            // rip.recovery diagnostic: quantify the re-read sawtooth on stuck windows only (numbers
            // only - no titles/paths). fresh = ThisPassErrors (this pass alone); running = consensus.
            // A pass whose fresh count is near the whole window is a drive slip, not new damage.
            int rcWin = -1, rcMinFresh = int.MaxValue, rcSlips = 0, rcPasses = 0, rcLastPass = -1;
            bool rcConverged = false;
            void RcFlushWindow()
            {
                if (rcWin >= 0 && rcPasses > 0)
                    _log.Info("rip.recovery", $"window={rcWin} DONE passes={rcPasses} converged={(rcConverged ? 1 : 0)} minFresh={(rcMinFresh == int.MaxValue ? 0 : rcMinFresh)} slipPasses={rcSlips} speed={(lastRequested > 0 ? lastRequested / 176 : 0)}x");
                rcWin = -1; rcMinFresh = int.MaxValue; rcSlips = 0; rcPasses = 0; rcLastPass = -1; rcConverged = false;
            }
            reader.ReadProgress += (s, e) =>
            {
                double frac = e.Position / total;
                if (frac - lastFrac >= 0.004 || frac >= 1.0)
                {
                    lastFrac = frac;
                    onProgress(Math.Min(1.0, Math.Max(0.0, frac)), (encode ? "Ripping" : "Verifying") + $"... {(int)(frac * 100)}%");
                }

                // Report a re-read only while one is actually happening (pass > cqc), plus one final
                // "cleared" report so the viz can hide. e.Pass == -1 is the TOC/pregap read, not audio.
                if (e.Pass >= 0)
                {
                    int reReads = Math.Max(0, e.Pass - cqc);
                    if (reReads > peakReRead) peakReRead = reReads;
                    if (reReads > 0 && lastReReads == 0)
                    {
                        rereadWindows++;   // count each stuck window once
                        // one line per damaged spot (position + errors only, no titles): tells you
                        // where a disc is scratched/pin-holed and confirms the re-read path is live.
                        _log.Info("rip.reread", $"stuck window at {(int)(frac * 100)}% errors={e.ErrorsCount}");
                        // adaptive speed: the drive is struggling - request one step down (the
                        // reader applies it when the NEXT window starts, never mid-recovery)
                        speedCtl?.OnErrorCluster(); RequestSpeed(); lastEaseFrac = frac;
                    }
                    // adaptive speed: a clean ~5% stretch with no re-read eases back up one step
                    if (speedCtl != null && reReads == 0 && lastReReads == 0 && frac - lastEaseFrac >= 0.05)
                    {
                        speedCtl.OnCleanRegion(); RequestSpeed(); lastEaseFrac = frac;
                    }
                    // last pass and the sectors still disagree: the drive has given up on this window
                    if (reReads >= maxReReads && e.ErrorsCount > 0 && lastReReads < maxReReads)
                    {
                        failedWindows++;
                        _log.Warn("rip.reread", $"gave up on window at {(int)(frac * 100)}% errors={e.ErrorsCount} (unreadable by drive)");
                    }
                    if (onReread != null && (reReads > 0 || lastReReads > 0))
                    {
                        double wfrac = e.PassEnd > e.PassStart ? (double)e.PassStart / total : frac;
                        onReread(reReads, maxReReads, e.ErrorsCount, Math.Min(1.0, Math.Max(0.0, wfrac)));
                    }
                    // rip.recovery: one line per re-read pass of a stuck window (logged at the pass's
                    // last chunk, where its fresh count is complete), plus a per-window summary flushed
                    // when the next stuck window starts or the rip ends. Stuck windows only, so the log
                    // stays small - this is the sawtooth data the recovery-fix spec will consume.
                    if (reReads > 0)
                    {
                        if (e.PassStart != rcWin) { RcFlushWindow(); rcWin = e.PassStart; }
                        int winSize = Math.Max(1, e.PassEnd - e.PassStart);
                        if (e.Position >= e.PassEnd && e.Pass != rcLastPass)
                        {
                            rcLastPass = e.Pass;
                            bool slip = e.ThisPassErrors >= 0.85 * winSize;
                            rcPasses++;
                            if (e.ThisPassErrors < rcMinFresh) rcMinFresh = e.ThisPassErrors;
                            if (slip) rcSlips++;
                            if (e.ErrorsCount == 0) rcConverged = true;
                            _log.Info("rip.recovery", $"window={e.PassStart} pass={e.Pass} running={e.ErrorsCount} fresh={e.ThisPassErrors}/{winSize} speed={(lastRequested > 0 ? lastRequested / 176 : 0)}x slip={(slip ? 1 : 0)}");
                        }
                    }
                    lastReReads = reReads;
                }
            };

            // Embed the hi-res Apple cover when we have one; otherwise leave Metadata.AlbumArt intact
            // so the engine falls back to the CTDB/database cover. Clearing Metadata.AlbumArt stops the
            // engine re-adding the DB cover on top of ours (LoadAndResizeAlbumArt reads that list).
            if (encode && _config.embedAlbumArt && coverArt != null && coverArt.Length > 0)
            {
                try
                {
                    // build the picture FIRST: if construction throws after the lists were cleared,
                    // the album would ship with NO art at all (not even the database fallback)
                    var pic = new TagLib.Picture(new TagLib.ByteVector(coverArt)) { Type = TagLib.PictureType.FrontCover };
                    cue.Metadata.AlbumArt.Clear();
                    cue.AlbumArt.Clear();
                    cue.AlbumArt.Add(pic);
                    _log.Info("rip", $"embed hi-res cover {coverArt.Length}B");
                }
                catch (Exception ex) { _log.Warn("rip", "cover inject failed (database cover keeps): " + ex.GetType().Name); }
            }

            onProgress(0, encode ? "Ripping + verifying..." : "Verifying against AccurateRip + CTDB...");
            string status = cue.Go();
            onProgress(1, status);
            RcFlushWindow();   // emit the summary for the last stuck window (it never advances past)

            int arConf = 0, arTotal = 0, ctConf = cue.CTDB.Confidence, ctTotal = cue.CTDB.Total;
            // a throw here would otherwise read as "not found in AccurateRip" - a different fact
            try { arConf = (int)cue.ArVerify.WorstConfidence(); arTotal = (int)cue.ArVerify.WorstTotal(); }
            catch (Exception ex) { _log.Warn("rip", "AccurateRip result read failed (reported as not found): " + ex.GetType().Name); }
            int files = 0;
            try { if (encode && Directory.Exists(outDir)) files = Directory.GetFiles(outDir, "*." + format).Length; } catch { }

            _log.Info("rip", $"done mode={(encode ? "encode" : "verify")} elapsed={sw.Elapsed.TotalSeconds:0}s " +
                $"ar_conf={arConf}/{arTotal} ctdb_conf={ctConf}/{ctTotal} accurate={arConf > 0} files={files} " +
                $"reread_windows={rereadWindows} reread_peak={peakReRead} failed_windows={failedWindows} status={status}");

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
        catch (StopException)
        {
            _log.Info("rip", $"stopped by user after {sw.Elapsed.TotalSeconds:0}s");
            return new VerifyResult { Error = "Stopped." };
        }
        catch (Exception ex)
        {
            _log.Error("rip", $"failed after {sw.Elapsed.TotalSeconds:0}s", ex);
            return new VerifyResult { Error = ex.Message };
        }
        finally
        {
            lock (_stopGate) _current = null;
            // always re-allow eject; if this fails the eject button stays dead until the handle closes
            try { if (_settings.LockTrayDuringRip) reader.DisableEjectDisc(false); }
            catch (Exception ex) { _log.Warn("rip", "tray unlock failed: " + ex.GetType().Name); }
            if (_settings.PreventSleepDuringRip) KeepAwake(false);
            try { reader.Close(); } catch { }
        }
    }

    private string Safe(string s) => string.IsNullOrEmpty(s) ? "" : _config.CleanseString(s);
}
