using System;
using System.Collections.Generic;
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

    /// <summary>The per-track checksum record this read produced (used by Test & Copy to compare
    /// reads). Null when the record build failed.</summary>
    public CUETools.Wpf.Accuracy.VerifyRecord? Record { get; init; }
    /// <summary>Count of windows the drive could not read even after every retry (0 on a clean read).</summary>
    public int FailedWindows { get; init; }

    /// <summary>The album folder the namer rendered, RELATIVE to the output base (e.g.
    /// "Artist - Album (1995)" or "Artist - Album [2-CD Set]/Disc 2"). Callers that re-home the output
    /// (Test &amp; Copy commits from a staging folder) must reuse this instead of taking the last path
    /// segment - a multi-disc scheme renders more than one segment, and dropping the leading ones makes
    /// every disc 2 land in the same "Disc 2" folder.</summary>
    public string OutputRelDir { get; init; } = "";

    /// <summary>Per-audio-track AccurateRip / CTDB confidence, index-aligned to the track list.</summary>
    public int[] ArPerTrack { get; init; } = System.Array.Empty<int>();
    public int[] CtdbPerTrack { get; init; } = System.Array.Empty<int>();

    /// <summary>Local verify-history outcome (second-source bit-exactness): whether this disc was read
    /// before, whether the read matched, how many prior reads, and how many tracks differed.</summary>
    public bool HistoryKnown { get; init; }
    public bool HistoryMatches { get; init; }
    public int HistoryPriorReads { get; init; }
    public int HistoryDiffTracks { get; init; }
}

/// <summary>Outcome of a Test & Copy run: either a committed output (Passed) or a held result with
/// the staging retained for the user's Accept anyway / Discard / Re-run decision.</summary>
public sealed class TestCopyRunResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public CUETools.Wpf.Accuracy.TestCopyOutcome Outcome { get; init; }
    public int ReadsUsed { get; init; }
    public int[] HeldTracks { get; init; } = System.Array.Empty<int>();
    public string OutputDir { get; init; } = "";
    public int FileCount { get; init; }
    public int ArConfidence { get; init; }
    public int ArTotal { get; init; }
    public int CtdbConfidence { get; init; }
    public int CtdbTotal { get; init; }
    public bool Accurate { get; init; }
    public string CopyStagingDir { get; init; } = "";
    public string[] StagingDirs { get; init; } = System.Array.Empty<string>();

    /// <summary>The format actually encoded, polled at encode start - not the one selected when the
    /// button was pressed. The caller must report THIS in the completion summary, or a mid-verify
    /// codec change makes the summary lie about what was written.</summary>
    public string Format { get; init; } = "";

    /// <summary>The rendered album folder relative to the output base (see VerifyResult.OutputRelDir).
    /// Accepting a held read must re-home the staging with THIS, not its last path segment.</summary>
    public string OutputRelDir { get; init; } = "";
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
    /// engine's database cover is used when it is null. <paramref name="onEncodeStart"/>, when
    /// given, fires once right before the actual encode begins (never on a verify-only pass) - the
    /// caller uses it to lock the codec choice at that moment.</summary>
    VerifyResult RunEncode(char drive, int correctionQuality, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null, Action? onEncodeStart = null);

    /// <summary>Ask the running rip/verify to stop at the next safe point. No-op if nothing runs.</summary>
    void Stop();

    /// <summary>Test & Copy: read the disc twice (a third time on a mismatch), commit only tracks two
    /// independent reads agree on bit-for-bit, hold the rest. Forces at least Secure and forces cache
    /// defeat (auto-calibrating first when needed) so the reads are genuinely independent.
    /// <paramref name="liveFormat"/>, when given, is polled just before each encode read (Copy, and
    /// the third read on a mismatch) so a codec change made during the Test read is honored -
    /// <paramref name="format"/> is otherwise used as-is. <paramref name="onEncodeStart"/> fires once
    /// before each of those encode reads (never before the Test read) so the caller can lock the
    /// codec choice once encoding actually starts.</summary>
    TestCopyRunResult RunTestAndCopy(char drive, int correctionQuality, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null, Func<string>? liveFormat = null, Action? onEncodeStart = null);

    /// <summary>Accept a held Test & Copy's Copy read into the output folder anyway, flagged not
    /// test-verified, and discard the staging. Returns the committed output directory, or "" on
    /// failure.</summary>
    string CommitCopyReadAnyway(TestCopyRunResult held, string outputBaseDir);

    /// <summary>Delete the staging folders a held Test & Copy retained.</summary>
    void DiscardStaging(TestCopyRunResult held);
}

public sealed class RipService : IRipService
{
    private readonly CUEConfig _config;
    private readonly IDiagnosticLog _log;
    private readonly AppSettings _settings;
    private readonly EncoderCatalog _catalog;
    private CUESheet? _current;   // the running sheet, so Stop() can abort it
    private readonly object _stopGate = new();

    private readonly CUETools.Wpf.Accuracy.DriveCalibrationStore _calStore;
    private readonly CUETools.Wpf.Accuracy.VerifyHistoryStore _history;
    private readonly CUETools.Wpf.Accuracy.DriveCalibrationService _calService;

    public RipService(CUEConfig config, IDiagnosticLog log, AppSettings settings, EncoderCatalog catalog, CUETools.Wpf.Accuracy.DriveCalibrationStore calStore, CUETools.Wpf.Accuracy.VerifyHistoryStore history, CUETools.Wpf.Accuracy.DriveCalibrationService calService)
    { _config = config; _log = log; _settings = settings; _catalog = catalog; _calStore = calStore; _history = history; _calService = calService; }

    /// <summary>Set by Stop(), cleared when a new operation starts. Stop() alone was not enough:
    /// it only forwards to whatever CUESheet is currently running, and a Test & Copy makes 2-3
    /// SEPARATE Run calls with the calibration prologue before them - so between reads, and for the
    /// whole prologue, there was nothing to forward to and the stop was silently dropped. The user
    /// pressed Stop and the album was written anyway. The latch survives those gaps.</summary>
    private volatile bool _stopRequested;

    public void Stop()
    {
        _stopRequested = true;
        CUESheet? cue; lock (_stopGate) cue = _current;
        try { cue?.Stop(); _log.Info("rip", "stop requested"); }
        catch (Exception ex) { _log.Warn("rip", "stop request failed: " + ex.GetType().Name); }
    }

    /// <summary>Throw if a stop was requested. Called at every point where no CUESheet is running and
    /// Stop() would therefore have nowhere to land.</summary>
    private void ThrowIfStopRequested()
    {
        if (_stopRequested) throw new StopException();
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

    public VerifyResult RunVerify(char drive, int cq, CUEMetadata? metadata, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null) { _stopRequested = false; return Run(drive, cq, encode: false, "flac", metadata, "", onProgress, onLevels, onSamples, onReread); }
    public VerifyResult RunEncode(char drive, int cq, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null, Action? onEncodeStart = null) { _stopRequested = false; return Run(drive, cq, encode: true, string.IsNullOrWhiteSpace(format) ? "flac" : format, metadata, outputBaseDir, onProgress, onLevels, onSamples, onReread, coverArt, onEncodeStart: onEncodeStart); }

    private VerifyResult Run(char drive, int cq, bool encode, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null, bool stageOnly = false, bool forceCacheDefeat = false, Action? onEncodeStart = null)
    {
        var reader = new CDDriveReader();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Snapshot the toggles this job runs under, BEFORE the try - the finally releases
        // keep-awake and the tray lock on these locals. Re-reading the live settings there
        // stranded the keep-awake request when the user turned it off mid-rip, so the machine
        // would not sleep again until the app closed. Deep recovery had the mirror problem:
        // one consumer re-read it live, so a mid-run toggle produced a half-deep run.
        bool deepRecovery = _settings.DeepRecovery;
        bool keepAwakeTaken = _settings.PreventSleepDuringRip;
        bool trayLockTaken = _settings.LockTrayDuringRip;
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
            // Snapshot the toggles this job runs under. They are all live-bindable from other
            // pages, and re-reading them later produced jobs that half-obeyed a mid-run change:
            // deep recovery kept its unbounded re-read cap but stopped slowing to the floor, and the
            // verify record reported whichever value happened to be current at the end.
            reader.DeepRecovery = deepRecovery;
            if (reader.DeepRecovery) _log.Info("rip", "deep recovery ON: progress-aware cap + slow-to-floor + slip probe");

            // Adaptive read speed (Feature 3): start at the drive's max, drop a step when the drive
            // gets stuck on a window, ease back up after clean stretches. Only REQUESTS are made
            // here; the reader applies them at the next fresh-window boundary on its own read
            // thread (a mid-window SET CD SPEED crashed the read - see PrefetchSector). The audio
            // is identical at any speed, so accuracy is unaffected either way.
            // Fetch this drive's saved calibration BEFORE the speed controller is built: its probed max
            // speed is the adaptive ceiling (the Drive & Read page says so), and the controller has
            // always accepted a ceiling argument that nobody passed - so the calibrated value was read
            // only into a display string and the tooltip's claim was simply untrue.
            var cal = _calStore.Get((reader.ARName ?? "").Trim());

            AdaptiveSpeedController speedCtl = null;
            int lastRequested = 0;
            if (_settings.AdaptiveReadSpeed)
            {
                int[] speeds = reader.GetSupportedSpeeds();
                if (speeds.Length > 1)
                {
                    int? ceiling = (cal != null && cal.MaxSpeedKbps > 0) ? cal.MaxSpeedKbps : (int?)null;
                    speedCtl = new AdaptiveSpeedController(speeds, ceiling);
                    lastRequested = speedCtl.CurrentSpeed;
                    reader.RequestReadSpeed(lastRequested);
                    _log.Info("rip", $"adaptive speed on: {speeds.Length} steps {speeds[0]}-{speeds[speeds.Length - 1]} kB/s, " +
                        $"start {lastRequested} ({lastRequested / 176}x)" +
                        (ceiling.HasValue ? $", calibrated ceiling {ceiling.Value} kB/s" : ", no calibrated ceiling"));
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

            // Deep recovery: a window that stays stuck drops to the drive's floor (probed min speed, or
            // the lowest supported) - slow reads track marginal/scratched sectors better. Requested only
            // at window boundaries via the same path as adaptive speed; the audio is unchanged.
            int deepFloor = 0;
            if (deepRecovery)
            {
                var sp = reader.GetSupportedSpeeds();
                int ladderLow = sp.Length > 0 ? sp[0] : 0;
                deepFloor = (cal != null && cal.MinSpeedKbps > 0) ? cal.MinSpeedKbps : ladderLow;
                if (deepFloor > 0) _log.Info("rip", $"deep recovery floor {deepFloor} kB/s ({deepFloor / 176}x)");
            }

            // Cache defeat (opt-in under Deep recovery for the proving phase): on a caching drive the
            // secure re-read returns the cached FIRST read, so Secure cannot catch a read error during
            // the rip (AccurateRip still catches it at the end, but not on a non-AR disc). When the drive
            // is calibrated as caching, flush the drive-specific calibrated size before each re-read so it
            // hits media. Scratch-only - it can recover error detection but can never corrupt the audio.
            if ((deepRecovery || forceCacheDefeat) && cal != null && (cal.CacheDefeat ?? "").StartsWith("Flush:")
                && int.TryParse(cal.CacheDefeat.Substring(6), out int flushBytes) && flushBytes > 0)
            {
                reader.SetCacheDefeat(flushBytes);
                _log.Info("rip", $"cache defeat on: flush {flushBytes}B before each secure re-read" +
                    (forceCacheDefeat ? " (forced: Test & Copy)" : " (drive caches, calibrated)"));
            }

            // keep the machine awake for the whole read; optionally lock the tray so the disc cannot
            // be ejected mid-read (which would fail the read and can crash the drive layer).
            if (keepAwakeTaken) KeepAwake(true);
            if (trayLockTaken) { try { reader.DisableEjectDisc(true); } catch (Exception ex) { _log.Warn("rip", "tray lock failed: " + ex.GetType().Name); } }

            _log.Info("rip", $"start mode={(encode ? "encode" : "verify")} format={format} cq={cq} offset={offset} drive='{(reader.ARName ?? "").Trim()}' " +
                $"chosen_release={(metadata != null)} preventSleep={keepAwakeTaken} lockTray={trayLockTaken}");

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
                // DISC-SWAP GUARD. The release was chosen for the disc that was in the drive when it was
                // read. Nothing else notices a swap: the tray poll and the disc re-read are both
                // suppressed while a job runs (deliberately - a mid-rip re-read used to file results
                // against a different album), and no code invalidates the cached release afterwards. So
                // a disc changed mid-run would be ripped under the PREVIOUS disc's album name and track
                // titles. Compare what the release was built from against the disc actually loaded, and
                // refuse rather than mislabel. A missing id is treated as unknown, never as a mismatch,
                // so this can only ever reject a genuine disagreement.
                string loadedId = reader.TOC?.TOCID ?? "";
                int loadedTracks = (int)(reader.TOC?.AudioTracks ?? 0);
                bool idDisagrees;
                if (DiscDisagreesWithRelease(metadata.Id, metadata.Tracks?.Count ?? 0, loadedId, loadedTracks, out idDisagrees))
                {
                    _log.Warn("rip", $"disc mismatch: release built for tracks={metadata.Tracks?.Count ?? 0} " +
                        $"but the loaded disc has tracks={loadedTracks} (id match={!idDisagrees}) - refusing to rip");
                    return new VerifyResult
                    {
                        Error = "The disc in the drive is not the disc that was identified - it looks like it "
                              + "was changed. Read the disc again before ripping.",
                    };
                }

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
                try { var rel = cue.LookupAlbumInfo(_config.advanced.CacheMetadata, false, true, CTDBMetadataSearch.Fast); if (rel.Count > 0) cue.CopyMetadata(((CUEMetadataEntry)rel[0]).metadata); } catch { }
            }
            // from here on, any album/artist text (incl. in paths or errors) is scrubbed from the log
            _log.Redact(cue.Metadata?.Artist, cue.Metadata?.Title);

            cue.UseCUEToolsDB("CUETools 2026", reader.ARName, false, CTDBMetadataSearch.Fast);
            cue.UseAccurateRip();
            cue.ArTestVerify = null;
            cue.OutputStyle = CUEStyle.GapsAppended;

            string outDir = "";
            string outRelDir = "";   // the album folder relative to baseDir - see VerifyResult.OutputRelDir
            if (encode)
            {
                cue.Action = CUEAction.Encode;
                string baseDir = string.IsNullOrWhiteSpace(outputBaseDir)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CUETools")
                    : outputBaseDir;

                // Naming comes from the ONE naming engine - the same NamingEngine.Render the Naming page
                // previews with - so what is previewed is what lands on disk. Render each track's relative
                // path, split off the shared album folder (the .cue/.log/cover live there), create the
                // whole tree including any "Disc N/" subfolder, then hand the engine the per-track names
                // so it writes exactly those instead of re-deriving them from trackFilenameFormat (whose
                // token vocabulary differs from the WPF one - that mismatch was the encode-path bug).
                int trackCount = Math.Max(0, cue.TrackCount);
                if (trackCount > 0)
                {
                    var scheme = _settings.LoadNamingScheme();
                    var rel = new string[trackCount];
                    for (int t = 0; t < trackCount; t++)
                        rel[t] = CUETools.Wpf.Services.NamingEngine.Render(
                            NamingContextMapper.FromMetadata(cue.Metadata, t, trackCount), scheme);
                    var split = NamingPaths.Split(rel);
                    // There must ALWAYS be an album folder. A template with no folder part, or one whose
                    // first segment differs per track (the "Simple" preset on a various-artists disc,
                    // where %artist% is the leading segment), yields no shared leading directory - and
                    // then album.cue, the rip log, the cover and rip.verify would be written straight
                    // into the output base and overwritten by the next such rip, while Test & Copy would
                    // commit under its temp staging name. Fall back to an album folder derived from the
                    // metadata so every rip keeps its own directory.
                    outRelDir = string.IsNullOrWhiteSpace(split.commonDir)
                        ? AlbumFolderFallback(cue.Metadata) : split.commonDir;
                    // never write over an existing rip - see NonClobberingAlbumDir
                    outRelDir = OutputGuard.NonClobberingAlbumDir(baseDir, outRelDir, format, m => _log.Info("rip", m));
                    outDir = Path.Combine(baseDir, outRelDir);
                    Directory.CreateDirectory(outDir);
                    // cap the assembled path length, then guarantee non-empty/unique names - in that
                    // order, so the uniquifier can still disambiguate any collision truncation creates
                    var capped = NamingPaths.CapPathLength(split.remainders, outDir.Length);
                    var finalNames = NamingPaths.EnsureUniqueTrackNames(capped);
                    foreach (var r in finalNames)
                    {
                        string sub = Path.GetDirectoryName(Path.Combine(outDir, r));
                        if (!string.IsNullOrEmpty(sub)) Directory.CreateDirectory(sub);
                    }
                    cue.SetExplicitTrackNames(finalNames);
                }
                else
                {
                    // no tracks to name (should not happen for a real disc) - keep a sane album folder
                    string album = AlbumFolderFallback(cue.Metadata);
                    outRelDir = OutputGuard.NonClobberingAlbumDir(baseDir, album, format, m => _log.Info("rip", m));
                    album = outRelDir;
                    outDir = Path.Combine(baseDir, album);
                    Directory.CreateDirectory(outDir);
                }
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
                    // deep recovery: only a GENUINELY persistent window (8+ re-reads deep) drops to the
                    // drive floor - not every minor stuck spot. Slow reads recover marginal sectors best,
                    // but 4x on a window that would clear fast just wastes time.
                    if (deepRecovery && deepFloor > 0 && reReads >= 8 && lastRequested != deepFloor)
                    {
                        lastRequested = deepFloor;
                        reader.RequestReadSpeed(deepFloor);
                        _log.Info("rip.speed", $"deep recovery: drive floor {deepFloor} kB/s ({deepFloor / 176}x)");
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
                    // deep recovery: slip classification verdict (read-only probe result, numbers only)
                    if (e.SlipStrengthPct >= 0)
                        _log.Info("rip.recovery", $"slip probe window={e.PassStart} strength={e.SlipStrengthPct}% offset={e.SlipOffset} " +
                            (e.SlipStrengthPct >= 90 && e.SlipOffset != 0 ? "-> recoverable JITTER (real audio, shifting)"
                             : e.SlipStrengthPct >= 90 ? "-> reads identical (cache or stable, not jittering)"
                             : "-> DEAD MEDIA (no shared signal)"));
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
            // Fire exactly once, right before the encode actually starts, and only for a real encode -
            // a verify-only pass never touches the codec, so it never locks it. This is the moment the
            // caller uses to lock the codec dropdown (the format string above is already final by now).
            if (encode) onEncodeStart?.Invoke();
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

            // Verify history: capture the per-track AccurateRip CRCs this read produced (deterministic
            // in the bytes) and compare against our own earlier reads of this disc - a second, offline,
            // AccurateRip-independent bit-exactness check.
            var vh = new CUETools.Wpf.Accuracy.VerifyOutcome();
            CUETools.Wpf.Accuracy.VerifyRecord? built = null;
            try
            {
                var tracks = new CUETools.Wpf.Accuracy.TrackCrc[n];
                for (int t = 0; t < n; t++)
                {
                    uint v1 = 0, v2 = 0, c32 = 0;
                    try { v1 = cue.ArVerify.CRC(t); } catch { }
                    try { v2 = cue.ArVerify.CRCV2(t); } catch { }
                    // CRC32 is 1-indexed unlike CRC/CRCV2: CRC32(0) is the whole-disc row, CRC32(N) is
                    // track N, so track t needs CRC32(t + 1).
                    try { c32 = cue.ArVerify.CRC32(t + 1); } catch { }
                    tracks[t] = new CUETools.Wpf.Accuracy.TrackCrc { ArV1 = v1, ArV2 = v2, Crc32 = c32 };
                }
                built = new CUETools.Wpf.Accuracy.VerifyRecord
                {
                    DiscId = cue.TOC.TOCID ?? "",
                    Tracks = tracks,
                    ArConfidence = arConf, ArTotal = arTotal,
                    CtdbConfidence = ctConf, CtdbTotal = ctTotal,
                    Drive = (reader.ARName ?? "").Trim(),
                    ReadOffset = offset,
                    CorrectionQuality = cq,
                    DeepRecovery = deepRecovery,
                    Title = cue.Metadata?.Title ?? "",
                    Artist = cue.Metadata?.Artist ?? "",
                    Utc = DateTime.UtcNow,
                    RipperVersion = "2026.1.0",
                };
                if (!stageOnly)
                {
                    vh = _history.CompareAndUpsert(built);
                    _log.Info("verify.history", $"disc={built.DiscId} known={(vh.KnownDisc ? 1 : 0)} matches={(vh.Matches ? 1 : 0)} diffTracks={vh.DiffTrackCount}");
                    if (encode && Directory.Exists(outDir))
                    {
                        try { File.WriteAllText(Path.Combine(outDir, "rip.verify"), CUETools.Wpf.Accuracy.VerifyHistoryStore.ToJson(built)); }
                        catch (Exception ex) { _log.Warn("verify.history", "sidecar write failed: " + ex.GetType().Name); }
                    }
                }
            }
            catch (Exception ex) { _log.Warn("verify.history", "record build failed: " + ex.GetType().Name); }

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
                CtdbPerTrack = ctpt,
                HistoryKnown = vh.KnownDisc,
                HistoryMatches = vh.Matches,
                HistoryPriorReads = vh.PriorReads,
                HistoryDiffTracks = vh.DiffTrackCount,
                Record = built,
                FailedWindows = failedWindows,
                OutputRelDir = outRelDir,
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
            try { if (trayLockTaken) reader.DisableEjectDisc(false); }
            catch (Exception ex) { _log.Warn("rip", "tray unlock failed: " + ex.GetType().Name); }
            if (keepAwakeTaken) KeepAwake(false);
            try { reader.Close(); } catch { }
        }
    }

    private string Safe(string s) => string.IsNullOrEmpty(s) ? "" : _config.CleanseString(s);


    /// <summary>True when the loaded disc disagrees with the release the user picked - the disc-swap
    /// check, split out so it can be tested without a drive. An EMPTY id or a zero track count on either
    /// side means "unknown" and never counts as a disagreement, so this can only reject a genuine
    /// mismatch, never a legitimate rip whose source simply did not carry a TOC id.</summary>
    public static bool DiscDisagreesWithRelease(string releaseId, int releaseTracks, string loadedId,
        int loadedTracks, out bool idDisagrees)
    {
        idDisagrees = !string.IsNullOrEmpty(releaseId) && !string.IsNullOrEmpty(loadedId)
            && !string.Equals(releaseId, loadedId, StringComparison.Ordinal);
        bool countDisagrees = releaseTracks > 0 && loadedTracks > 0 && releaseTracks != loadedTracks;
        return idDisagrees || countDisagrees;
    }

    /// <summary>"Artist - Album" (or "Unknown Album") for use as the album directory when the naming
    /// scheme does not produce one. Every rip needs its own folder: without it the .cue, rip log, cover
    /// and rip.verify collide in the output base, and a Test &amp; Copy commit has no name to re-home to.</summary>
    private string AlbumFolderFallback(CUEMetadata meta)
    {
        string artist = Safe(meta?.Artist ?? ""), title = Safe(meta?.Title ?? "");
        return (string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title))
            ? "Unknown Album" : $"{artist} - {title}".Trim(' ', '-');
    }

    // ---- Test & Copy ---------------------------------------------------------------------

    public TestCopyRunResult RunTestAndCopy(char drive, int cq, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, Action<double, double>? onLevels = null, Action<float[]>? onSamples = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null, Func<string>? liveFormat = null, Action? onEncodeStart = null)
    {
        _stopRequested = false;   // fresh operation - see the latch on Stop()
        int rq = Math.Max(1, Math.Min(2, cq));            // force at least Secure
        string fmt = string.IsNullOrWhiteSpace(format) ? "flac" : format;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        string stage1 = "", stage2 = "";
        bool keepStaging = false;   // set true only when we return a HELD result the VM must clean up

        Action<double, string> WithLabel(string label) => (frac, msg) => onProgress(frac, label + ": " + msg);

        try
        {
            ThrowIfStopRequested();   // Stop pressed during the calibration prologue
            if (!EnsureIndependence(drive, onProgress))
                return new TestCopyRunResult { Error = "Calibration failed - cannot guarantee two independent reads." };

            string stem = Path.Combine(Path.GetTempPath(), "cuetc", Guid.NewGuid().ToString("N"));
            stage1 = stem + "-copy";
            stage2 = stem + "-third";

            // Read 1 (Test, index 0): verify pass, not staged - nothing on disk to compare tracks
            // against but its checksums still count as an independent read.
            ThrowIfStopRequested();
            var testResult = Run(drive, rq, encode: false, "flac", metadata, "", WithLabel("Test read (1 of 2)"), onLevels, onSamples, onReread, coverArt: null, stageOnly: true, forceCacheDefeat: true);
            if (!testResult.Ok) return new TestCopyRunResult { Error = testResult.Error };

            // Read 2 (Copy, index 1): staged encode - this is the file set that gets committed on a
            // 2-read pass, or is the preferred source per track on a 3-read pass. This is the first
            // actual encode read, so re-poll the live codec choice now (a change made during the Test
            // read above is honored) and carry it forward - fmt then also drives the final commit's
            // file-extension count below, so it stays consistent with what was actually encoded.
            { string live = liveFormat?.Invoke() ?? ""; if (!string.IsNullOrWhiteSpace(live)) fmt = live; }
            ThrowIfStopRequested();   // between reads: no CUESheet exists for Stop() to reach
            var copyResult = Run(drive, rq, encode: true, fmt, metadata, stage1, WithLabel("Copy read (2 of 2)"), onLevels, onSamples, onReread, coverArt, stageOnly: true, forceCacheDefeat: true, onEncodeStart: onEncodeStart);
            if (!copyResult.Ok) return new TestCopyRunResult { Error = copyResult.Error };

            var reads = new System.Collections.Generic.List<VerifyRecord> { testResult.Record, copyResult.Record };
            var staged = new System.Collections.Generic.List<bool> { false, true };
            var stagingAlbumDirs = new System.Collections.Generic.List<string> { "", copyResult.OutputDir };
            int failedWindows = Math.Max(testResult.FailedWindows, copyResult.FailedWindows);

            var resolve = TestAndCopyResolver.Resolve(reads, staged);

            if (resolve.Outcome == TestCopyOutcome.Held)
            {
                // Read 3 (third, index 2): staged encode, only run when the first two disagree
                // somewhere. Re-resolve with all three reads staged (Test is still index 0/unstaged).
                // The codec is locked by the Copy read's onEncodeStart above by the time we get here,
                // so this re-poll is just for consistency - it will report the same locked choice.
                { string live = liveFormat?.Invoke() ?? ""; if (!string.IsNullOrWhiteSpace(live)) fmt = live; }
                ThrowIfStopRequested();
                var thirdResult = Run(drive, rq, encode: true, fmt, metadata, stage2, WithLabel("Confirming (read 3)"), onLevels, onSamples, onReread, coverArt, stageOnly: true, forceCacheDefeat: true, onEncodeStart: onEncodeStart);
                if (!thirdResult.Ok) return new TestCopyRunResult { Error = thirdResult.Error };

                reads.Add(thirdResult.Record);
                staged.Add(true);
                stagingAlbumDirs.Add(thirdResult.OutputDir);
                failedWindows = Math.Max(failedWindows, thirdResult.FailedWindows);

                resolve = TestAndCopyResolver.Resolve(reads, staged);
            }

            string discId = copyResult.Record?.DiscId ?? testResult.Record?.DiscId ?? "";
            string driveSig = copyResult.Record?.Drive ?? "";
            int offset = copyResult.Record?.ReadOffset ?? 0;

            // Held: write nothing to outputBaseDir. Retain staging for the VM's Accept anyway /
            // Discard / Re-run follow-ups; keepStaging suppresses the finally-block cleanup.
            TestCopyRunResult BuildHeld(int[] heldTracks)
            {
                ThrowIfStopRequested();   // do not commit an album the user cancelled
                keepStaging = true;
                var last = reads[reads.Count - 1];
                var dirs = string.IsNullOrEmpty(stage2) ? new[] { stage1 } : new[] { stage1, stage2 };
                return new TestCopyRunResult
                {
                    Ok = true,
                    Outcome = TestCopyOutcome.Held,
                    ReadsUsed = resolve.ReadsUsed,
                    Format = fmt,
                    OutputRelDir = copyResult.OutputRelDir,
                    HeldTracks = heldTracks,
                    CopyStagingDir = copyResult.OutputDir,
                    StagingDirs = dirs,
                    ArConfidence = last?.ArConfidence ?? 0,
                    ArTotal = last?.ArTotal ?? 0,
                    CtdbConfidence = last?.CtdbConfidence ?? 0,
                    CtdbTotal = last?.CtdbTotal ?? 0,
                    Accurate = (last?.ArConfidence ?? 0) > 0,
                };
            }

            if (resolve.Outcome == TestCopyOutcome.Passed)
            {
                // The old per-track resolver only requires SOME agreement per track, which can be
                // satisfied by mixing tracks from different reads - that is the fragile file-sort
                // assembly this replaces. Only commit when a SINGLE staged read agrees with some
                // other read on EVERY track: its folder is then track-aligned by construction and
                // can be copied wholesale, with nothing to sort or index.
                int whole = TestAndCopyResolver.FullyVerifiedReadIndex(reads, staged);
                if (whole < 0)
                {
                    // Scattered errors: each track found agreement somewhere, but no one read was
                    // clean throughout. Report which tracks the Copy read (index 1) itself got
                    // wrong, so the user sees why nothing was committed.
                    var copyTracks = reads.Count > 1 ? reads[1]?.Tracks : null;
                    int trackCount = copyTracks?.Length ?? 0;
                    var mismatches = new List<int>();
                    for (int t = 0; t < trackCount; t++)
                    {
                        var ct = copyTracks[t];
                        bool agreesAny = false;
                        for (int j = 0; j < reads.Count && !agreesAny; j++)
                        {
                            if (j == 1) continue;
                            var ot = reads[j]?.Tracks;
                            var otc = (ot != null && t < ot.Length) ? ot[t] : null;
                            if (VerifyHistoryStore.SameAudio(ct, otc)) agreesAny = true;
                        }
                        if (!agreesAny) mismatches.Add(t);
                    }
                    _log.Info("rip", $"testcopy disc={discId} reads={resolve.ReadsUsed} passed=0 heldTracks={mismatches.Count}");
                    _log.Info("rip", "testcopy held: no single read was clean on every track (scattered errors across reads) - refusing to assemble per track");
                    _log.Info("rip", $"test&copy done elapsed={sw.Elapsed.TotalSeconds:0}s reads={resolve.ReadsUsed} outcome=held");
                    return BuildHeld(mismatches.ToArray());
                }

                // Protect the staging ACROSS the commit. The copy into the library is the one unguarded
                // I/O left (disk full, an AV or indexer lock, a too-long path), and if it throws, the
                // finally below would delete both bit-verified staged reads - destroying a rip that was
                // already proven correct and forcing a full re-rip. Hold them until the commit returns.
                ThrowIfStopRequested();   // do not commit an album the user cancelled
                keepStaging = true;
                var (outDir, fileCount) = AssembleAndCommit(resolve, reads, stagingAlbumDirs[whole], whole, outputBaseDir, discId, driveSig, offset, failedWindows, fmt, copyResult.OutputRelDir);
                keepStaging = false;   // committed - the staging is now redundant
                var last = reads[reads.Count - 1];
                _log.Info("rip", $"testcopy disc={discId} reads={resolve.ReadsUsed} passed=1 heldTracks=0");
                _log.Info("rip", $"test&copy done elapsed={sw.Elapsed.TotalSeconds:0}s reads={resolve.ReadsUsed} outcome=passed");
                return new TestCopyRunResult
                {
                    Ok = true,
                    Outcome = TestCopyOutcome.Passed,
                    ReadsUsed = resolve.ReadsUsed,
                    Format = fmt,
                    OutputRelDir = copyResult.OutputRelDir,
                    OutputDir = outDir,
                    FileCount = fileCount,
                    ArConfidence = last?.ArConfidence ?? 0,
                    ArTotal = last?.ArTotal ?? 0,
                    CtdbConfidence = last?.CtdbConfidence ?? 0,
                    CtdbTotal = last?.CtdbTotal ?? 0,
                    Accurate = (last?.ArConfidence ?? 0) > 0,
                };
            }
            else
            {
                _log.Info("rip", $"testcopy disc={discId} reads={resolve.ReadsUsed} passed=0 heldTracks={resolve.HeldTracks.Length}");
                _log.Info("rip", $"test&copy done elapsed={sw.Elapsed.TotalSeconds:0}s reads={resolve.ReadsUsed} outcome=held");
                return BuildHeld(resolve.HeldTracks);
            }
        }
        catch (StopException)
        {
            _log.Info("rip", $"test&copy stopped by user after {sw.Elapsed.TotalSeconds:0}s");
            return new TestCopyRunResult { Error = "Stopped." };
        }
        catch (Exception ex)
        {
            _log.Error("rip", $"test&copy failed after {sw.Elapsed.TotalSeconds:0}s", ex);
            // A throw with keepStaging set means the failure happened DURING the commit, so the
            // verified reads are still on disk. Say where: the audio is proven and re-rippable only at
            // the cost of another 2-3 full reads.
            if (keepStaging && !string.IsNullOrEmpty(stage1))
            {
                _log.Warn("rip", "test&copy commit failed - the verified staged reads were KEPT");
                return new TestCopyRunResult
                {
                    Error = ex.Message + "  The verified reads were kept at: " + stage1
                        + (string.IsNullOrEmpty(stage2) ? "" : " and " + stage2),
                };
            }
            return new TestCopyRunResult { Error = ex.Message };
        }
        finally
        {
            // A stop, an error before the commit, or a completed commit all reach here with keepStaging
            // false, so staging is cleaned up. It is held for a genuine HELD result (the VM resolves it
            // via Accept/Discard) and for a commit that threw part-way, so a proven rip is never thrown
            // away because of a transient disk error.
            if (!keepStaging)
            {
                DeleteStagingDir(stage1);
                DeleteStagingDir(stage2);
            }
        }
    }

    /// <summary>Make sure the next reads on this drive are genuinely independent: a caching drive
    /// must have a sized cache-defeat flush before Test &amp; Copy can trust two "different" reads
    /// are not just the same cached bytes served twice. Calibrates once, then the result is reused
    /// (persisted) on every later Test &amp; Copy for the same drive.</summary>
    private bool EnsureIndependence(char drive, Action<double, string> onProgress)
    {
        string sig = "";
        var reader = new CDDriveReader();
        try
        {
            bool opened;
            lock (DriveService.ScsiGate) opened = reader.Open(drive);
            if (opened) sig = (reader.ARName ?? "").Trim();
        }
        catch (Exception ex) { _log.Warn("rip", "test&copy drive signature read failed: " + ex.GetType().Name); }
        finally { try { reader.Close(); } catch { } }

        var cal = _calStore.Get(sig);
        bool sized = cal != null && ((cal.CacheDefeat ?? "").StartsWith("Flush:") || cal.CacheDefeat == "Media re-reads (no cache)");
        if (sized) return true;

        onProgress(0, "Calibrating drive...");
        var newCal = _calService.Calibrate(drive);
        bool newSized = newCal != null && ((newCal.CacheDefeat ?? "").StartsWith("Flush:") || newCal.CacheDefeat == "Media re-reads (no cache)");
        return newSized;
    }

    /// <summary>Assemble the committed album folder and write the Test &amp; Copy proof. Always
    /// commits ONE staged read's folder wholesale - the read <see cref="TestAndCopyResolver.FullyVerifiedReadIndex"/>
    /// found to agree with some other read on every track - so the files are track-aligned by
    /// construction and there is nothing to sort or index per track.</summary>
    private (string outDir, int fileCount) AssembleAndCommit(TestCopyResult resolve, List<VerifyRecord> reads, string sourceStagingAlbumDir, int sourceReadIndex, string outputBaseDir, string discId, string drive, int offset, int failedWindows, string format, string albumRelDir)
    {
        // Re-home the staged album folder using the RENDERED relative dir, not its last path segment:
        // a multi-disc scheme renders "Artist - Album [2-CD Set]/Disc 2", so taking the last segment
        // would commit every disc 2 of every set into one shared "Disc 2" folder and overwrite.
        string albumName = string.IsNullOrWhiteSpace(albumRelDir)
            ? Path.GetFileName(sourceStagingAlbumDir) : albumRelDir;
        string realBase = string.IsNullOrWhiteSpace(outputBaseDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CUETools")
            : outputBaseDir;
        // a Test & Copy commit is still a rip - do not let it land on top of an earlier one
        albumName = OutputGuard.NonClobberingAlbumDir(realBase, albumName, format, m => _log.Info("rip", m));
        string outDir = Path.Combine(realBase, albumName);

        // Wholesale commit only: the chosen read's own staged folder is copied as-is, so its files
        // can never misalign with its own track order.
        CopyDirectoryRecursive(sourceStagingAlbumDir, outDir);

        // Build the committed record from the single committed read's own checksums (never a
        // per-track mix of different reads).
        var source = (sourceReadIndex >= 0 && sourceReadIndex < reads.Count) ? reads[sourceReadIndex] : null;
        var newest = reads[reads.Count - 1];
        var committedRecord = new VerifyRecord
        {
            DiscId = discId,
            Tracks = source?.Tracks ?? Array.Empty<TrackCrc>(),
            ArConfidence = newest?.ArConfidence ?? 0,
            ArTotal = newest?.ArTotal ?? 0,
            CtdbConfidence = newest?.CtdbConfidence ?? 0,
            CtdbTotal = newest?.CtdbTotal ?? 0,
            Drive = drive,
            ReadOffset = offset,
            CorrectionQuality = newest?.CorrectionQuality ?? 0,
            DeepRecovery = newest?.DeepRecovery ?? false,
            Title = newest?.Title ?? "",
            Artist = newest?.Artist ?? "",
            Utc = DateTime.UtcNow,
            RipperVersion = "2026.1.0",
        };

        try
        {
            string logText = TestAndCopyLog.Format(resolve, reads, discId, drive, offset, failedWindows);
            File.WriteAllText(Path.Combine(outDir, "Test & Copy.log"), logText);
        }
        catch (Exception ex) { _log.Warn("rip", "Test & Copy log write failed: " + ex.GetType().Name); }

        try
        {
            var vh = _history.CompareAndUpsert(committedRecord);
            _log.Info("verify.history", $"disc={committedRecord.DiscId} known={(vh.KnownDisc ? 1 : 0)} matches={(vh.Matches ? 1 : 0)} diffTracks={vh.DiffTrackCount}");
        }
        catch (Exception ex) { _log.Warn("verify.history", "test&copy upsert failed: " + ex.GetType().Name); }

        try { File.WriteAllText(Path.Combine(outDir, "rip.verify"), VerifyHistoryStore.ToJson(committedRecord)); }
        catch (Exception ex) { _log.Warn("verify.history", "test&copy sidecar write failed: " + ex.GetType().Name); }

        int fileCount = 0;
        try { fileCount = Directory.GetFiles(outDir, "*." + format).Length; } catch { }
        return (outDir, fileCount);
    }

    /// <summary>Accept a held Test &amp; Copy's Copy read into the output folder anyway, flagged not
    /// test-verified, and discard the staging. Never writes to outputBaseDir on failure. Returns the
    /// committed output directory, or "" on failure.</summary>
    public string CommitCopyReadAnyway(TestCopyRunResult held, string outputBaseDir)
    {
        if (held == null || string.IsNullOrEmpty(held.CopyStagingDir) || !Directory.Exists(held.CopyStagingDir))
        {
            _log.Warn("rip", "test&copy accept-anyway: no staged copy read available");
            return "";
        }
        try
        {
            // rendered relative dir, not the last segment - see AssembleAndCommit
            string albumName = string.IsNullOrWhiteSpace(held.OutputRelDir)
                ? Path.GetFileName(held.CopyStagingDir) : held.OutputRelDir;
            string realBase = string.IsNullOrWhiteSpace(outputBaseDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CUETools")
                : outputBaseDir;
            albumName = OutputGuard.NonClobberingAlbumDir(realBase, albumName, held.Format ?? "", m => _log.Info("rip", m));
            string outDir = Path.Combine(realBase, albumName);
            CopyDirectoryRecursive(held.CopyStagingDir, outDir);

            string heldList = string.Join(", ", System.Array.ConvertAll(held.HeldTracks, x => (x + 1).ToString()));
            string log = "Test & Copy log\n\n" +
                "NOT test-verified - accepted by user without agreement.\n" +
                $"Reads used: {held.ReadsUsed}\n" +
                $"Held track(s) (no agreement): {heldList}\n";
            File.WriteAllText(Path.Combine(outDir, "Test & Copy.log"), log);

            _log.Info("rip", $"testcopy accept-anyway reads={held.ReadsUsed} heldTracks={held.HeldTracks.Length}");
            DiscardStaging(held);
            return outDir;
        }
        catch (Exception ex)
        {
            _log.Warn("rip", "test&copy accept-anyway failed: " + ex.GetType().Name);
            return "";
        }
    }

    /// <summary>Delete the staging folders a held Test &amp; Copy retained. Best-effort.</summary>
    public void DiscardStaging(TestCopyRunResult held)
    {
        if (held?.StagingDirs == null) return;
        foreach (var dir in held.StagingDirs) DeleteStagingDir(dir);
    }

    private void DeleteStagingDir(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception ex) { _log.Warn("rip", "test&copy staging cleanup failed: " + ex.GetType().Name); }
    }

    private static void CopyDirectoryRecursive(string srcDir, string dstDir)
    {
        Directory.CreateDirectory(dstDir);
        foreach (var dir in Directory.GetDirectories(srcDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dstDir, Path.GetRelativePath(srcDir, dir)));
        foreach (var file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(dstDir, Path.GetRelativePath(srcDir, file)), true);
    }

}
