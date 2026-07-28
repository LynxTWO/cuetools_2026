using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using CUETools.AccurateRip;
using CUETools.Codecs;
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
    /// <summary>Portable artist/album identity used for human-facing cue and log sidecars.</summary>
    public string ArtifactStem { get; init; } = "";
    /// <summary>Codec the files were written as. Reported rather than assumed, so a certificate for an
    /// m4a rip does not claim FLAC. Empty on a verify-only pass, which writes nothing.</summary>
    public string Format { get; init; } = "";
    /// <summary>Whether this job recorded the encoded-output assurance contract. False on verify-only
    /// jobs and reports produced before the field existed.</summary>
    public bool OutputVerificationKnown { get; init; }
    public bool LosslessOutput { get; init; }
    /// <summary>True only when the selected lossless encoder passed its own check and CUESheet then
    /// decoded the final, metadata-complete files against the PCM delivered to each encoder.
    /// AccurateRip/CTDB and optical-read agreement are separate evidence.</summary>
    public bool OutputVerificationPerformed { get; init; }
    public string OutputVerificationDetail { get; init; } = "";
    /// <summary>
    /// Non-persisted evidence for the exact encoded output set. The proof objects expose no
    /// digests; Test &amp; Copy carries them across a later copy instead of detaching the public
    /// assurance fields from the bytes that earned them.
    /// </summary>
    internal IReadOnlyList<LosslessOutputProof> OutputProofs { get; init; } =
        Array.Empty<LosslessOutputProof>();

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

    /// <summary>
    /// CTDB repair evidence computed from the same PCM stream that was ripped. An exact CTDB match
    /// suppresses alternative-pressing differences, so these fields describe genuine recoverable
    /// damage rather than merely a different database pressing.
    /// </summary>
    public bool CtdbHasErrors { get; init; }
    public bool CtdbCanRecover { get; init; }
    public int CtdbRepairSectors { get; init; }
    public string CtdbRepairRanges { get; init; } = "";
    /// <summary>
    /// Exact published input for the source-preserving repair transaction. This is the album's sole
    /// top-level cue for track sets, or the sole lossless audio file for image mode. Empty means the
    /// output cannot be reconstructed unambiguously.
    /// </summary>
    public string RepairSourcePath { get; init; } = "";

    /// <summary>Local verify-history outcome (second-source bit-exactness): whether this disc was read
    /// before, whether the read matched, how many prior reads, and how many tracks differed.</summary>
    public bool HistoryRecorded { get; init; }
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
    /// <summary>
    /// Why a completed Copy was held instead of committed. Empty means all requested
    /// reads completed and their checksum disagreement alone caused the hold.
    /// </summary>
    public string HoldReason { get; init; } = "";
    public CUETools.Wpf.Accuracy.TestCopyOutcome Outcome { get; init; }
    public int ReadsUsed { get; init; }
    public int[] HeldTracks { get; init; } = System.Array.Empty<int>();
    public string OutputDir { get; init; } = "";
    public int FileCount { get; init; }
    public int ArConfidence { get; init; }
    public int ArTotal { get; init; }
    public int CtdbConfidence { get; init; }
    public int CtdbTotal { get; init; }
    public bool CtdbHasErrors { get; init; }
    public bool CtdbCanRecover { get; init; }
    public int CtdbRepairSectors { get; init; }
    public string CtdbRepairRanges { get; init; } = "";
    public string RepairSourcePath { get; init; } = "";
    internal string RepairSourceRelativePath { get; init; } = "";
    public bool Accurate { get; init; }
    public bool HistoryRecorded { get; init; }
    public bool HistoryKnown { get; init; }
    public bool HistoryMatches { get; init; }
    public int HistoryPriorReads { get; init; }
    public int HistoryDiffTracks { get; init; }
    public string CopyStagingDir { get; init; } = "";
    public string[] StagingDirs { get; init; } = System.Array.Empty<string>();
    internal TestCopyStagingWorkspace? StagingWorkspace { get; init; }

    /// <summary>The format actually encoded, polled at encode start - not the one selected when the
    /// button was pressed. The caller must report THIS in the completion summary, or a mid-verify
    /// codec change makes the summary lie about what was written.</summary>
    public string Format { get; init; } = "";
    public bool OutputVerificationKnown { get; init; }
    public bool LosslessOutput { get; init; }
    public bool OutputVerificationPerformed { get; init; }
    public string OutputVerificationDetail { get; init; } = "";
    internal IReadOnlyList<LosslessOutputProof> OutputProofs { get; init; } =
        Array.Empty<LosslessOutputProof>();

    /// <summary>The rendered album folder relative to the output base (see VerifyResult.OutputRelDir).
    /// Accepting a held read must re-home the staging with THIS, not its last path segment.</summary>
    public string OutputRelDir { get; init; } = "";
    public string ArtifactStem { get; init; } = "";

    /// <summary>The accuracy mode the reads were actually performed at (forced to at least Secure). The
    /// caller must report THIS when it later commits a held result: by then the dropdown may say
    /// something else entirely, and the archived report would claim a mode the disc was never read at.</summary>
    public int CorrectionQuality { get; init; }
    /// <summary>Per-track Test/Copy CRCs for immediate UI display and persisted history.</summary>
    public TrackCrc[] CrcEvidence { get; init; } = Array.Empty<TrackCrc>();
}

public interface IRipService
{
    /// <summary>Newest persisted per-track Test/Copy CRC evidence for an inserted disc.</summary>
    TrackCrc[] GetLatestCrcEvidence(string discId) => Array.Empty<TrackCrc>();

    /// <summary>Verify the disc against AccurateRip + CTDB (reads the whole disc, writes nothing).
    /// <paramref name="telemetry"/> receives best-effort RMS and scope samples through a bounded
    /// mailbox; a stalled UI drops visualization instead of delaying the disc read.
    /// <paramref name="onReread"/> reports a real sector re-read: (reReads, maxReReads, errorSectors,
    /// discFrac); reReads &gt; 0 only when the drive is doing extra passes over a stuck window.
    /// <paramref name="metadata"/>, when given, is the release the user chose (else auto-picked).</summary>
    VerifyResult RunVerify(char drive, int correctionQuality, CUEMetadata? metadata, Action<double, string> onProgress, RipTelemetryMailbox? telemetry = null, Action<int, int, int, double>? onReread = null);

    /// <summary>Rip the disc (read + encode + verify) to the given format under
    /// <paramref name="outputBaseDir"/>\Artist - Album, using the chosen release metadata when
    /// given. <paramref name="telemetry"/> receives bounded best-effort RMS and consecutive PCM
    /// samples. <paramref name="onReread"/> reports real sector re-reads (see RunVerify).
    /// <paramref name="coverArt"/>, when given, is the hi-res cover to embed (already resized); the
    /// engine's database cover is used when it is null. <paramref name="onEncodeStart"/>, when
    /// given, fires once right before the actual encode begins (never on a verify-only pass) - the
    /// caller uses it to lock the codec choice at that moment.</summary>
    VerifyResult RunEncode(char drive, int correctionQuality, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, RipTelemetryMailbox? telemetry = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null, Action? onEncodeStart = null);

    /// <summary>Ask the running rip/verify to stop at the next safe point. No-op if nothing runs.</summary>
    void Stop();

    /// <summary>Test & Copy: read the disc twice (a third time on a mismatch), commit only tracks two
    /// independent reads agree on bit-for-bit, hold the rest. Forces at least Secure and forces cache
    /// defeat (auto-calibrating first when needed) so the reads are genuinely independent.
    /// <paramref name="liveFormat"/>, when given, is polled just before each encode read (Copy, and
    /// the third read on a mismatch) so a codec change made during the Test read is honored -
    /// <paramref name="format"/> is otherwise used as-is. <paramref name="onEncodeStart"/> fires once
    /// before each of those encode reads (never before the Test read) so the caller can lock the
    /// codec choice once encoding actually starts. <paramref name="onCrcEvidence"/> receives a
    /// fresh named Test/Copy snapshot after each completed read; it is an ancillary notification
    /// and must not affect the read or final transaction result.</summary>
    TestCopyRunResult RunTestAndCopy(char drive, int correctionQuality, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, RipTelemetryMailbox? telemetry = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null, Func<string>? liveFormat = null, Action? onEncodeStart = null, Action<TrackCrc[]>? onCrcEvidence = null);

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
    internal Action<string>? AfterProofDirectoryMoveForTest { get; set; }

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

    public TrackCrc[] GetLatestCrcEvidence(string discId)
    {
        try
        {
            return _history.GetLatestCrcEvidence(discId);
        }
        catch (Exception ex)
        {
            _log.Warn(
                "verify.history",
                "CRC evidence load failed: " + ex.GetType().Name);
            return Array.Empty<TrackCrc>();
        }
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

    public VerifyResult RunVerify(char drive, int cq, CUEMetadata? metadata, Action<double, string> onProgress, RipTelemetryMailbox? telemetry = null, Action<int, int, int, double>? onReread = null)
    {
        // Hold one outer scope across calibration and the actual read. The nested scopes inside
        // those phases keep their own invariants, while this one prevents a drive selector from
        // briefly re-enabling in the handoff between them.
        using var operationScope = DriveService.TryEnterRip(drive, _log);
        if (operationScope == null)
            return new VerifyResult
            {
                Error = $"Drive {char.ToUpperInvariant(drive)}: is already in use by another CUETools job."
            };
        _stopRequested = false;
        if (!EnsureCalibration(
                drive,
                onProgress,
                requireIndependentReads: cq > 0,
                out string error))
            return new VerifyResult { Error = error };
        return Run(drive, cq, encode: false, "flac", metadata, "", onProgress, telemetry, onReread);
    }

    public VerifyResult RunEncode(char drive, int cq, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, RipTelemetryMailbox? telemetry = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null, Action? onEncodeStart = null)
    {
        using var operationScope = DriveService.TryEnterRip(drive, _log);
        if (operationScope == null)
            return new VerifyResult
            {
                Error = $"Drive {char.ToUpperInvariant(drive)}: is already in use by another CUETools job."
            };
        _stopRequested = false;
        if (!EnsureCalibration(
                drive,
                onProgress,
                requireIndependentReads: cq > 0,
                out string error))
            return new VerifyResult { Error = error };
        return Run(drive, cq, encode: true, string.IsNullOrWhiteSpace(format) ? "flac" : format, metadata, outputBaseDir, onProgress, telemetry, onReread, coverArt, onEncodeStart: onEncodeStart);
    }

    private void RedactOutputRoot(string? outputBaseDir)
    {
        // Register the caller's spelling first: even Path.GetFullPath can fail and its exception can
        // quote the value. Register the effective absolute root as well so later I/O errors cannot
        // disclose a custom library location.
        try { _log.Redact(outputBaseDir); } catch { }
        try
        {
            string effective = string.IsNullOrWhiteSpace(outputBaseDir)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    "CUETools")
                : Path.GetFullPath(outputBaseDir);
            _log.Redact(effective);
        }
        catch { }
    }

    private void RedactStagingRoot(string? stagingDirectory)
    {
        try { _log.Redact(stagingDirectory); } catch { }
        if (string.IsNullOrWhiteSpace(stagingDirectory)) return;
        try { _log.Redact(Path.GetFullPath(stagingDirectory)); } catch { }
    }

    private VerifyResult Run(char drive, int cq, bool encode, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, RipTelemetryMailbox? telemetry, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null, bool stageOnly = false, bool forceCacheDefeat = false, Action? onEncodeStart = null)
    {
        if (encode) RedactOutputRoot(outputBaseDir);
        var reader = new CDDriveReader();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        AlbumOutputTransaction? publication = null;
        int expectedAudioFiles = 0;
        // Snapshot the toggles this job runs under, BEFORE the try - the finally releases
        // keep-awake and the tray lock on these locals. Re-reading the live settings there
        // stranded the keep-awake request when the user turned it off mid-rip, so the machine
        // would not sleep again until the app closed. Deep recovery had the mirror problem:
        // one consumer re-read it live, so a mid-run toggle produced a half-deep run.
        bool deepRecovery = _settings.DeepRecovery;
        bool keepAwakeTaken = _settings.PreventSleepDuringRip;
        bool trayLockTaken = _settings.LockTrayDuringRip;
        // Tell the rest of the app this drive is in use, so the Drive & Read page cannot Detect or
        // Calibrate against a handle the ripper holds (it would fail on a sharing violation and be
        // reported as a missing disc - advice that invites a mid-rip eject).
        using var ripScope = DriveService.EnterRip();
        try
        {
            // open under the app-wide device gate so a rip start cannot collide with an in-flight
            // tray poll / capability query (the gate is held only for the open, not the whole rip)
            bool opened;
            lock (DriveService.ScsiGate) opened = reader.Open(drive);
            if (!opened) { _log.Warn("rip", "no disc / not ready"); return new VerifyResult { Error = "No disc." }; }

            int offset = 0;
            bool offsetKnown = false;
            try
            {
                offsetKnown =
                    AccurateRipVerify.FindDriveReadOffset(reader.ARName, out offset);
            }
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
            DriveCalibration? cal = null;
            try
            {
                cal = _calStore.Get((reader.ARName ?? "").Trim());
            }
            catch (InvalidDataException ex)
            {
                // EnsureCalibration read this same record immediately before Run. If it
                // changed or became corrupt in between, do not turn a successful gate
                // into an uncalibrated secure read.
                _log.Error(
                    "rip",
                    "drive calibration became unreadable before the optical read",
                    ex);
                return new VerifyResult
                {
                    Error =
                        "Saved drive calibration became unreadable before the optical read.",
                };
            }
            if (!DriveCalibrationService.IsCurrent(cal))
                return new VerifyResult
                {
                    Error =
                        "Drive calibration changed before the optical read; retry the operation.",
                };

            // Offset correction formerly zero-padded both disc edges unconditionally. Calibration now
            // probes the exact READ CD boundary capability; enable only the edges this drive proved.
            bool applyOverread = DriveCalibrationService.CanApplyOverread(
                cal,
                offsetKnown,
                offset);
            reader.SetOverread(
                applyOverread && cal?.OverreadLeadIn == true,
                applyOverread && cal?.OverreadLeadOut == true);
            if (!applyOverread &&
                (cal?.OverreadLeadIn == true || cal?.OverreadLeadOut == true))
                _log.Warn(
                    "rip",
                    "calibrated overread range does not match the current known read offset; using edge zero-padding");

            AdaptiveSpeedController? speedCtl = null;
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

            // Cache defeat: on a caching drive the
            // secure re-read returns the cached FIRST read, so Secure cannot catch a read error during
            // the rip (AccurateRip still catches it at the end, but not on a non-AR disc). When the drive
            // is calibrated as caching, flush the drive-specific calibrated size before each re-read so it
            // hits media. Secure and Paranoid therefore always use it; Deep recovery is no longer an
            // unrelated gate. Scratch-only - it can recover error detection but cannot alter the audio.
            if ((cq > 0 || forceCacheDefeat) &&
                cal?.CacheDefeat is string cacheDefeat &&
                cacheDefeat.StartsWith("Flush:", StringComparison.Ordinal) &&
                int.TryParse(cacheDefeat.Substring(6), out int flushBytes) &&
                flushBytes > 0)
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

            // Tap real audio into a bounded preallocated visualization mailbox. Queue pressure
            // can discard telemetry only; everything else delegates to the drive unchanged.
            ICDRipper ripper = telemetry != null
                ? new LevelMeteringRipper(reader, telemetry)
                : reader;

            var cue = new CUESheet(_config);
            lock (_stopGate) _current = cue;   // so Stop() can abort this run
            cue.OpenCD(ripper);
            var toc = reader.TOC ??
                throw new InvalidDataException(
                    "The opened audio disc did not provide a table of contents.");
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
                try { var rel = cue.LookupAlbumInfo(_config.advanced.CacheMetadata, false, true, _config.advanced.metadataSearch); if (rel.Count > 0) cue.CopyMetadata(((CUEMetadataEntry)rel[0]).metadata); } catch { }
            }
            // from here on, any album/artist text (incl. in paths or errors) is scrubbed from the log
            _log.Redact(cue.Metadata?.Artist, cue.Metadata?.Title);

            // Fast on purpose: this call is the CTDB VERIFICATION contact, not disc identification.
            // The release is already chosen by now, so a broader metadata sweep here would only add
            // latency to every rip. The "Metadata search" setting governs identification instead.
            cue.UseCUEToolsDB("CUETools 2026", reader.ARName, false, CTDBMetadataSearch.Fast);
            cue.UseAccurateRip();
            cue.ArTestVerify = null;
            cue.OutputStyle = CUEStyle.GapsAppended;

            string outDir = "";
            string outRelDir = "";   // the album folder relative to baseDir - see VerifyResult.OutputRelDir
            string artifactStem = "";
            var outputAssurance = new OutputVerificationAssurance(
                known: false, lossless: false, performed: false, detail: "");
            IReadOnlyList<LosslessOutputProof> outputProofs =
                Array.Empty<LosslessOutputProof>();
            AudioEncoderSettingsViewModel? selectedEncoder = null;
            bool outputLossy = false;
            VerifyFilesResult? repairAssessment = null;
            string repairSourceRelativePath = "";
            if (encode)
            {
                cue.Action = CUEAction.Encode;
                string baseDir = string.IsNullOrWhiteSpace(outputBaseDir)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CUETools")
                    : outputBaseDir;

                // One shared layout step for rip AND convert - see OutputLayout. Keeping this sequence
                // in two places is what produced the album-folder-collapse and staging-name bugs.
                OutputLayout.Plan layout;
                if (stageOnly)
                {
                    layout = OutputLayout.PrepareAndApply(cue, baseDir, format,
                        _settings.LoadNamingScheme(),
                        () => OutputLayout.AlbumFolderFallback(cue.Metadata, Safe),
                        m => _log.Info("rip", m));
                }
                else
                {
                    layout = OutputLayout.PrepareAndApplyTransactional(cue, baseDir,
                        _settings.LoadNamingScheme(),
                        () => OutputLayout.AlbumFolderFallback(cue.Metadata, Safe),
                        out publication, m => _log.Info("rip", m));
                }
                outDir = layout.OutputDir;
                outRelDir = layout.RelativeDir;
                // pick the encoder type from the format via the catalog's single rule: a format
                // with a USABLE lossy encoder encodes lossy (mp3 bundled, wma OS runtime, mpc when
                // its exe has been imported)
                outputLossy = _config.formats.TryGetValue(format, out var fmtInfo) &&
                    _catalog.IsLossyFormat(fmtInfo);
                selectedEncoder = fmtInfo == null
                    ? null
                    : outputLossy ? fmtInfo.encoderLossy : fmtInfo.encoderLossless;
                artifactStem = AlbumArtifactNames.CreateStem(cue.Metadata, Safe);
                cue.GenerateFilenames(
                    outputLossy ? AudioEncoderType.Lossy : AudioEncoderType.Lossless,
                    format,
                    Path.Combine(outDir, AlbumArtifactNames.CueFileName(artifactStem)));
                // DestPaths includes a preserved HTOA file when one is emitted; TrackCount does not.
                expectedAudioFiles = cue.DestPaths?.Length ?? 0;
                string displayDir = publication?.DestinationDirectory ?? outDir;
                onProgress(0, $"Encoding to {format.ToUpperInvariant()}{(outputLossy ? " (lossy)" : "")} -> {displayDir}");
            }
            else
            {
                cue.Action = CUEAction.Verify;
                cue.GenerateFilenames(AudioEncoderType.Lossless, "flac", Path.Combine(Path.GetTempPath(), "cueverify", "v.cue"));
            }

            double total = Math.Max(1, toc.AudioLength);
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
                    // Reserve the final two percent for output validation and atomic publication.
                    // Reaching 100% before the destination exists made a late finalize failure look
                    // like a completed rip that subsequently disappeared.
                    onProgress(Math.Min(0.98, Math.Max(0.0, frac)),
                        (encode ? "Ripping" : "Verifying") + $"... {(int)(frac * 100)}%");
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
            if (encode && (_config.embedAlbumArt || _config.extractAlbumArt) && coverArt != null && coverArt.Length > 0)
            {
                try
                {
                    // build the picture FIRST: if construction throws after the lists were cleared,
                    // the album would ship with NO art at all (not even the database fallback)
                    var pic = new TagLib.Picture(new TagLib.ByteVector(coverArt)) { Type = TagLib.PictureType.FrontCover };
                    CUEMetadata cueMetadata = cue.Metadata ??
                        throw new InvalidDataException(
                            "The opened disc did not provide metadata for cover embedding.");
                    cueMetadata.AlbumArt.Clear();
                    cue.AlbumArt.Clear();
                    cue.AlbumArt.Add(pic);
                    _log.Info("rip", $"embed hi-res cover {coverArt.Length}B");
                }
                catch (Exception ex) { _log.Warn("rip", "cover inject failed (database cover keeps): " + ex.GetType().Name); }
            }

            // Fire exactly once and freeze the assurance claim at the same boundary as the format
            // choice: immediately before CUESheet constructs and starts the encoder. A verify-only
            // pass never touches the codec and therefore never locks it.
            if (encode)
            {
                onEncodeStart?.Invoke();
                outputAssurance = OutputVerificationAssuranceEvaluator.Evaluate(
                    selectedEncoder?.Settings, outputLossy);
                // Only exact trusted encoder contracts reach Performed=true. CUESheet uses this
                // request to retain PCM receipts and run a second decode after every TagLib save.
                cue.VerifyFinalOutputAfterMetadata = outputAssurance.Performed;
            }
            string startText = !encode
                ? "Verifying against AccurateRip + CTDB..."
                : outputAssurance.Performed
                    ? "Ripping + verifying final encoded output..."
                    : outputAssurance.Lossless
                        ? "Ripping (encoded output verification not performed)..."
                        : "Ripping...";
            onProgress(0, startText);
            string status = cue.Go();
            // The rip already has the complete CTDB error map. Preserve that evidence instead of
            // forcing the user to rediscover it on the Verify page. Gather also applies the
            // alternate-pressing rule: any exact CTDB match suppresses repair for differences that
            // belong to another valid pressing.
            repairAssessment = VerifyService.Gather(
                cue,
                status,
                "",
                ok: true,
                error: "",
                applied: false,
                _log);
            if (encode &&
                !outputLossy &&
                repairAssessment.HasErrors &&
                repairAssessment.CanRecover)
            {
                repairSourceRelativePath = FindRepairSourceRelativePath(
                    outDir,
                    cue.DestPaths);
            }
            if (outputAssurance.Performed &&
                !cue.FinalOutputVerifiedAfterMetadata)
            {
                throw new InvalidDataException(
                    "The encoder verification contract completed without final-artifact proof.");
            }
            if (outputAssurance.Performed)
            {
                outputProofs = SnapshotAndValidateOutputProofs(
                    cue.DestPaths,
                    outDir,
                    cue.FinalOutputProofs);
            }
            else if (cue.FinalOutputProofs.Count != 0)
            {
                throw new InvalidDataException(
                    "Final-output proofs were produced without a trusted verification contract.");
            }
            try { onProgress(0.99, status + " Finalizing output..."); }
            catch (Exception ex)
            {
                // The expensive read has completed. A UI notification failure must not prevent
                // validation and publication of the already-produced album.
                try { _log.Warn("rip", "finalizing callback failed: " + ex.GetType().Name); }
                catch { }
            }
            RcFlushWindow();   // emit the summary for the last stuck window (it never advances past)

            int arConf = 0, arTotal = 0, ctConf = cue.CTDB.Confidence, ctTotal = cue.CTDB.Total;
            // a throw here would otherwise read as "not found in AccurateRip" - a different fact
            try { arConf = (int)cue.ArVerify.WorstConfidence(); arTotal = (int)cue.ArVerify.WorstTotal(); }
            catch (Exception ex) { _log.Warn("rip", "AccurateRip result read failed (reported as not found): " + ex.GetType().Name); }
            int files = 0;
            if (encode)
            {
                ValidateEncodedOutputs(cue.DestPaths, outDir);
                files = expectedAudioFiles;
            }

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
            bool historyRecorded = false;
            CUETools.Wpf.Accuracy.VerifyRecord? built = null;
            Exception? recordFailure = null;
            try
            {
                var tracks = new CUETools.Wpf.Accuracy.TrackCrc[n];
                for (int t = 0; t < n; t++)
                {
                    // CRC32 is 1-indexed unlike CRC/CRCV2: CRC32(0) is the whole-disc row, CRC32(N) is
                    // track N, so track t needs CRC32(t + 1).
                    uint crc32 = cue.ArVerify.CRC32(t + 1);
                    tracks[t] = new CUETools.Wpf.Accuracy.TrackCrc
                    {
                        ArV1 = cue.ArVerify.CRC(t),
                        ArV2 = cue.ArVerify.CRCV2(t),
                        Crc32 = crc32,
                        TestCrc32 = encode ? 0U : crc32,
                        CopyCrc32 = encode ? crc32 : 0U,
                    };
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
                    Format = encode ? format : "",
                    OutputVerificationKnown =
                        encode && outputAssurance.Known,
                    LosslessOutput =
                        encode && outputAssurance.Lossless,
                    OutputVerificationPerformed =
                        encode && outputAssurance.Performed,
                    OutputVerificationDetail =
                        encode ? outputAssurance.Detail : "",
                };
                // Carry the other named CRC role into this read's sidecar as well as the local
                // history. A Verify updates Test without erasing Copy; a Rip updates Copy without
                // erasing Test.
                try
                {
                    VerifyHistoryStore.MergePersistentCrcEvidence(
                        built.Tracks,
                        _history.GetLatestCrcEvidence(built.DiscId));
                }
                catch (Exception ex)
                {
                    _log.Warn(
                        "verify.history",
                        "prior CRC evidence load failed: " + ex.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                recordFailure = ex;
                _log.Warn("verify.history", "record build failed: " + ex.GetType().Name);
            }

            if (encode && built == null)
                throw new InvalidDataException(
                    "Could not build the required verification record.", recordFailure);

            if (encode && !stageOnly)
            {
                if (publication == null)
                    throw new InvalidOperationException("The album output was not reserved.");

                // The sidecar and every engine artifact are written inside the owned stage. A
                // disk-full or denied write therefore leaves the final album path untouched.
                File.WriteAllText(Path.Combine(outDir, "rip.verify"),
                    CUETools.Wpf.Accuracy.VerifyHistoryStore.ToJson(built!));
                ThrowIfStopRequested();
                outDir = outputAssurance.Performed
                    ? PublishProofBoundOutput(
                        publication,
                        format,
                        outputProofs,
                        built,
                        null)
                    : publication.Publish();
                // Directory.Move does not change the staged contents; the exact count was checked
                // immediately before publication. Keep that proven count instead of a best-effort
                // cosmetic recount that intentionally maps access errors to zero.
                files = expectedAudioFiles;
            }

            // The shared history is updated only after publication. A failed output transaction must
            // not create a history entry that looks like a successfully retained rip.
            if (!stageOnly && built != null)
            {
                try
                {
                    vh = _history.CompareAndUpsert(built);
                    historyRecorded = true;
                    _log.Info("verify.history", $"disc={built.DiscId} known={(vh.KnownDisc ? 1 : 0)} matches={(vh.Matches ? 1 : 0)} diffTracks={vh.DiffTrackCount}");
                }
                catch (Exception ex)
                {
                    _log.Warn("verify.history", "history upsert failed: " + ex.GetType().Name);
                }
            }

            try
            {
                _log.Info("rip", $"done mode={(encode ? "encode" : "verify")} elapsed={sw.Elapsed.TotalSeconds:0}s " +
                    $"ar_conf={arConf}/{arTotal} ctdb_conf={ctConf}/{ctTotal} accurate={arConf > 0} files={files} " +
                    $"output_verify={(outputAssurance.Performed ? 1 : 0)} " +
                    $"control_transition_retries={reader.ControlTransitionRetryCount} " +
                    $"cache_defeat_retries={reader.CacheDefeatRetryCount} " +
                    $"cache_defeat_chunk_fallbacks={reader.CacheDefeatChunkFallbackCount} " +
                    $"payload_batch_fallbacks={reader.PayloadBatchFallbackCount} " +
                    $"pinpoint_retries={reader.PinpointRetryCount} " +
                    $"corroborated_unreadable_pinpoints={reader.CorroboratedUnreadablePinpointCount} " +
                    $"reread_windows={rereadWindows} reread_peak={peakReRead} failed_windows={failedWindows} status={status}");
            }
            catch
            {
                // A diagnostic sink cannot turn an already-published album into a reported failure.
            }
            try { onProgress(1, status); }
            catch (Exception ex)
            {
                try { _log.Warn("rip", "completion callback failed: " + ex.GetType().Name); }
                catch { }
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
                ArtifactStem = artifactStem,
                Format = encode ? format : "",
                OutputVerificationKnown = encode && outputAssurance.Known,
                LosslessOutput = encode && outputAssurance.Lossless,
                OutputVerificationPerformed = encode && outputAssurance.Performed,
                OutputVerificationDetail = encode ? outputAssurance.Detail : "",
                OutputProofs = encode
                    ? outputProofs
                    : Array.Empty<LosslessOutputProof>(),
                ArPerTrack = arpt,
                CtdbPerTrack = ctpt,
                HistoryRecorded = historyRecorded,
                HistoryKnown = vh.KnownDisc,
                HistoryMatches = vh.Matches,
                HistoryPriorReads = vh.PriorReads,
                HistoryDiffTracks = vh.DiffTrackCount,
                Record = built,
                FailedWindows = failedWindows,
                OutputRelDir = outRelDir,
                CtdbHasErrors = repairAssessment?.HasErrors ?? false,
                CtdbCanRecover =
                    encode &&
                    !outputLossy &&
                    (repairAssessment?.HasErrors ?? false) &&
                    (repairAssessment?.CanRecover ?? false),
                CtdbRepairSectors = repairAssessment?.RepairSectors ?? 0,
                CtdbRepairRanges = repairAssessment?.RepairRanges ?? "",
                RepairSourcePath = ResolvePublishedRepairSource(
                    outDir,
                    repairSourceRelativePath),
            };
        }
        catch (StopException)
        {
            try { _log.Info("rip", $"stopped by user after {sw.Elapsed.TotalSeconds:0}s"); }
            catch { }
            return new VerifyResult { Error = "Stopped." };
        }
        catch (Exception ex)
        {
            try { _log.Error("rip", $"failed after {sw.Elapsed.TotalSeconds:0}s", ex); }
            catch { }
            string incomplete = "";
            if (publication != null && !publication.IsPublished)
            {
                try { incomplete = publication.PreserveIncomplete(); }
                catch (Exception preserveEx)
                {
                    try
                    {
                        _log.Warn("rip", "incomplete-stage quarantine failed: " +
                            preserveEx.GetType().Name);
                    }
                    catch { }
                }
            }
            return new VerifyResult
            {
                Error = ex.Message + (string.IsNullOrEmpty(incomplete)
                    ? "" : " Incomplete output was retained at: " + incomplete),
            };
        }
        finally
        {
            publication?.Dispose();
            lock (_stopGate) _current = null;
            // always re-allow eject; if this fails the eject button stays dead until the handle closes
            try { if (trayLockTaken) reader.DisableEjectDisc(false); }
            catch (Exception ex) { _log.Warn("rip", "tray unlock failed: " + ex.GetType().Name); }
            if (keepAwakeTaken) KeepAwake(false);
            try { reader.Close(); } catch { }
        }
    }

    /// <summary>
    /// Pick the exact input the existing file-repair transaction must reopen. One top-level cue is
    /// authoritative for a track set, whether it uses the legacy album.cue name or the portable
    /// artist/album name. Multiple cues are ambiguous. With no cue, only a single image is
    /// unambiguous. Never infer an album from several loose tracks because a partial or unrelated
    /// sibling set could then be repaired.
    /// </summary>
    internal static string FindRepairSourceRelativePath(
        string outputDirectory,
        IReadOnlyList<string>? audioPaths)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) ||
            !Directory.Exists(outputDirectory))
            return "";

        string root = Path.GetFullPath(outputDirectory);
        string rootPrefix = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        try
        {
            string? cuePath = null;
            foreach (string cueCandidate in Directory.EnumerateFiles(
                root,
                "*.cue",
                SearchOption.TopDirectoryOnly))
            {
                if (!IsSafeRepairSource(cueCandidate, rootPrefix))
                    continue;
                if (cuePath != null)
                    return "";
                cuePath = cueCandidate;
            }
            if (cuePath != null)
                return Path.GetRelativePath(root, cuePath);
        }
        catch
        {
            return "";
        }

        if (audioPaths == null || audioPaths.Count != 1)
            return "";
        string candidate;
        try { candidate = Path.GetFullPath(audioPaths[0]); }
        catch { return ""; }
        if (!IsSafeRepairSource(candidate, rootPrefix))
            return "";
        return Path.GetRelativePath(root, candidate);
    }

    private static bool IsSafeRepairSource(string candidate, string rootPrefix)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(candidate); }
        catch { return false; }
        if (!fullPath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullPath))
            return false;
        try
        {
            return (File.GetAttributes(fullPath) &
                FileAttributes.ReparsePoint) == 0;
        }
        catch { return false; }
    }

    private static string ResolvePublishedRepairSource(
        string outputDirectory,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            string.IsNullOrWhiteSpace(outputDirectory))
            return "";
        string root;
        string candidate;
        try
        {
            root = Path.GetFullPath(outputDirectory);
            candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        }
        catch { return ""; }
        string rootPrefix = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return IsSafeRepairSource(candidate, rootPrefix) ? candidate : "";
    }

    internal static string RebindRepairSource(
        string publishedDirectory,
        string relativePath) =>
        ResolvePublishedRepairSource(
            publishedDirectory,
            relativePath);

    private static string GetRepairSourceRelativePath(
        VerifyResult result)
    {
        if (string.IsNullOrWhiteSpace(result.OutputDir) ||
            string.IsNullOrWhiteSpace(result.RepairSourcePath))
            return "";
        string root;
        string source;
        try
        {
            root = Path.GetFullPath(result.OutputDir);
            source = Path.GetFullPath(result.RepairSourcePath);
        }
        catch { return ""; }
        string rootPrefix = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!IsSafeRepairSource(source, rootPrefix))
            return "";
        return Path.GetRelativePath(root, source);
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

    // ---- Test & Copy ---------------------------------------------------------------------

    public TestCopyRunResult RunTestAndCopy(char drive, int cq, string format, CUEMetadata? metadata, string outputBaseDir, Action<double, string> onProgress, RipTelemetryMailbox? telemetry = null, Action<int, int, int, double>? onReread = null, byte[]? coverArt = null, Func<string>? liveFormat = null, Action? onEncodeStart = null, Action<TrackCrc[]>? onCrcEvidence = null)
    {
        // Test, Copy, and an optional tie-break are separate Run calls. Keep drive ownership
        // continuous across their calibration, staging, and between-read gaps.
        using var operationScope = DriveService.TryEnterRip(drive, _log);
        if (operationScope == null)
            return new TestCopyRunResult
            {
                Error = $"Drive {char.ToUpperInvariant(drive)}: is already in use by another CUETools job."
            };
        // Calibration and drive setup can fail before the first staged Run call, so protect the
        // final user-selected destination at this outer entry point too.
        RedactOutputRoot(outputBaseDir);
        _stopRequested = false;   // fresh operation - see the latch on Stop()
        int rq = Math.Max(1, Math.Min(2, cq));            // force at least Secure
        string fmt = string.IsNullOrWhiteSpace(format) ? "flac" : format;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        string stage1 = "", stage2 = "";
        TestCopyStagingWorkspace? stagingWorkspace = null;
        bool keepStaging = false;   // set true only when we return a HELD result the VM must clean up

        Action<double, string> WithLabel(string label) => (frac, msg) => onProgress(frac, label + ": " + msg);
        void PublishCrcEvidence(
            IReadOnlyList<VerifyRecord> completedReads,
            int sourceReadIndex)
        {
            if (onCrcEvidence == null)
                return;
            TrackCrc[] snapshot =
                BuildTestCopyCrcEvidence(completedReads, sourceReadIndex);
            try { onCrcEvidence(snapshot); }
            catch (Exception ex)
            {
                // This is an immediate display notification. The immutable final result still
                // carries the same evidence, and a UI listener must never abort an optical read.
                try { _log.Warn("rip", "live CRC UI notification failed: " + ex.GetType().Name); }
                catch { }
            }
        }

        try
        {
            ThrowIfStopRequested();   // Stop pressed during the calibration prologue
            if (!EnsureCalibration(
                    drive,
                    onProgress,
                    requireIndependentReads: true,
                    out string calibrationError))
                return new TestCopyRunResult { Error = calibrationError };

            stagingWorkspace = TestCopyStagingWorkspace.Create();
            stage1 = stagingWorkspace.CopyBaseDirectory;
            stage2 = stagingWorkspace.ThirdBaseDirectory;
            RedactStagingRoot(stagingWorkspace.WorkspaceDirectory);
            RedactStagingRoot(stage1);
            RedactStagingRoot(stage2);

            // Read 1 (Test, index 0): verify pass, not staged - nothing on disk to compare tracks
            // against but its checksums still count as an independent read.
            ThrowIfStopRequested();
            var testResult = Run(drive, rq, encode: false, "flac", metadata, "", WithLabel("Test read (1 of 2)"), telemetry, onReread, coverArt: null, stageOnly: true, forceCacheDefeat: true);
            if (!testResult.Ok) return new TestCopyRunResult { Error = testResult.Error };
            VerifyRecord? testRecord = testResult.Record;
            if (testRecord == null)
                return new TestCopyRunResult
                {
                    Error = "Test read completed without checksum evidence."
                };
            PublishCrcEvidence(
                new[] { testRecord },
                sourceReadIndex: 0);

            // Read 2 (Copy, index 1): staged encode - this is the file set that gets committed on a
            // 2-read pass, or is the preferred source per track on a 3-read pass. This is the first
            // actual encode read, so re-poll the live codec choice now (a change made during the Test
            // read above is honored) and carry it forward - fmt then also drives the final commit's
            // file-extension count below, so it stays consistent with what was actually encoded.
            { string live = liveFormat?.Invoke() ?? ""; if (!string.IsNullOrWhiteSpace(live)) fmt = live; }
            ThrowIfStopRequested();   // between reads: no CUESheet exists for Stop() to reach
            var copyResult = Run(drive, rq, encode: true, fmt, metadata, stage1, WithLabel("Copy read (2 of 2)"), telemetry, onReread, coverArt, stageOnly: true, forceCacheDefeat: true, onEncodeStart: onEncodeStart);
            if (!copyResult.Ok) return new TestCopyRunResult { Error = copyResult.Error };
            VerifyRecord? copyRecord = copyResult.Record;
            if (copyRecord == null)
                return new TestCopyRunResult
                {
                    Error = "Copy read completed without checksum evidence."
                };

            var reads = new System.Collections.Generic.List<VerifyRecord>
            {
                testRecord,
                copyRecord
            };
            PublishCrcEvidence(reads, sourceReadIndex: 1);
            var staged = new System.Collections.Generic.List<bool> { false, true };
            var stagingAlbumDirs = new System.Collections.Generic.List<string> { "", copyResult.OutputDir };
            var encodedResults = new System.Collections.Generic.List<VerifyResult?> { null, copyResult };
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
                var thirdResult = Run(drive, rq, encode: true, fmt, metadata, stage2, WithLabel("Confirming (read 3)"), telemetry, onReread, coverArt, stageOnly: true, forceCacheDefeat: true, onEncodeStart: onEncodeStart);
                if (!thirdResult.Ok)
                {
                    if (thirdResult.Error == "Stopped.")
                        return new TestCopyRunResult { Error = thirdResult.Error };
                    _log.Warn(
                        "rip",
                        "confirming read failed after a complete staged Copy; holding the Copy instead of deleting it");
                    return BuildHeld(
                        resolve.HeldTracks,
                        "Confirming read failed: " + thirdResult.Error);
                }
                VerifyRecord? thirdRecord = thirdResult.Record;
                if (thirdRecord == null)
                    return BuildHeld(
                        resolve.HeldTracks,
                        "Confirming read completed without checksum evidence.");

                reads.Add(thirdRecord);
                PublishCrcEvidence(reads, sourceReadIndex: 2);
                staged.Add(true);
                stagingAlbumDirs.Add(thirdResult.OutputDir);
                encodedResults.Add(thirdResult);
                failedWindows = Math.Max(failedWindows, thirdResult.FailedWindows);

                resolve = TestAndCopyResolver.Resolve(reads, staged);
            }

            string discId = copyRecord.DiscId ?? testRecord.DiscId ?? "";
            string driveSig = copyRecord.Drive ?? "";
            int offset = copyRecord.ReadOffset;

            // Held: write nothing to outputBaseDir. Retain staging for the VM's Accept anyway /
            // Discard / Re-run follow-ups; keepStaging suppresses the finally-block cleanup.
            TestCopyRunResult BuildHeld(
                int[] heldTracks,
                string holdReason = "")
            {
                ThrowIfStopRequested();   // do not commit an album the user cancelled
                keepStaging = true;
                var last = reads[reads.Count - 1];
                return new TestCopyRunResult
                {
                    Ok = true,
                    Outcome = TestCopyOutcome.Held,
                    HoldReason = holdReason,
                    ReadsUsed = resolve.ReadsUsed,
                    Format = fmt,
                    OutputRelDir = copyResult.OutputRelDir,
                    ArtifactStem = copyResult.ArtifactStem,
                    CorrectionQuality = rq,   // the mode the reads were really made at
                    // What the Copy read actually staged. Never left unset: it is what the history row
                    // and the certificate report if the user accepts this held result anyway.
                    FileCount = OutputLayout.CountAudioFiles(copyResult.OutputDir, fmt),
                    HeldTracks = heldTracks,
                    CopyStagingDir = copyResult.OutputDir,
                    StagingDirs = new[] { stagingWorkspace.WorkspaceDirectory },
                    StagingWorkspace = stagingWorkspace,
                    ArConfidence = last?.ArConfidence ?? 0,
                    ArTotal = last?.ArTotal ?? 0,
                    CtdbConfidence = last?.CtdbConfidence ?? 0,
                    CtdbTotal = last?.CtdbTotal ?? 0,
                    CtdbHasErrors = copyResult.CtdbHasErrors,
                    CtdbCanRecover = copyResult.CtdbCanRecover,
                    CtdbRepairSectors = copyResult.CtdbRepairSectors,
                    CtdbRepairRanges = copyResult.CtdbRepairRanges,
                    RepairSourceRelativePath =
                        GetRepairSourceRelativePath(copyResult),
                    Accurate = (last?.ArConfidence ?? 0) > 0,
                    OutputVerificationKnown = copyResult.OutputVerificationKnown,
                    LosslessOutput = copyResult.LosslessOutput,
                    OutputVerificationPerformed =
                        copyResult.OutputVerificationPerformed,
                    OutputVerificationDetail =
                        copyResult.OutputVerificationDetail,
                    OutputProofs = copyResult.OutputProofs,
                    CrcEvidence = BuildTestCopyCrcEvidence(reads, 1),
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
                    TrackCrc[] copyTracks = reads.Count > 1
                        ? reads[1].Tracks ?? Array.Empty<TrackCrc>()
                        : Array.Empty<TrackCrc>();
                    int trackCount = copyTracks.Length;
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
                            if (VerifyHistoryStore.SameAudioForTestAndCopy(ct, otc)) agreesAny = true;
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
                VerifyResult committedEncoded = encodedResults[whole] ??
                    throw new InvalidDataException(
                        "The selected Test & Copy read has no encoded output.");
                var (outDir, fileCount, historyRecorded, history) = AssembleAndCommit(
                    resolve, reads, stagingAlbumDirs[whole], whole, outputBaseDir, discId,
                    driveSig, offset, failedWindows, fmt, copyResult.OutputRelDir,
                    committedEncoded);
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
                    ArtifactStem = committedEncoded.ArtifactStem,
                    CorrectionQuality = rq,   // the mode the reads were really made at
                    OutputDir = outDir,
                    FileCount = fileCount,
                    ArConfidence = last?.ArConfidence ?? 0,
                    ArTotal = last?.ArTotal ?? 0,
                    CtdbConfidence = last?.CtdbConfidence ?? 0,
                    CtdbTotal = last?.CtdbTotal ?? 0,
                    CtdbHasErrors = committedEncoded.CtdbHasErrors,
                    CtdbCanRecover = committedEncoded.CtdbCanRecover,
                    CtdbRepairSectors =
                        committedEncoded.CtdbRepairSectors,
                    CtdbRepairRanges =
                        committedEncoded.CtdbRepairRanges,
                    RepairSourceRelativePath =
                        GetRepairSourceRelativePath(committedEncoded),
                    RepairSourcePath = RebindRepairSource(
                        outDir,
                        GetRepairSourceRelativePath(committedEncoded)),
                    Accurate = (last?.ArConfidence ?? 0) > 0,
                    HistoryRecorded = historyRecorded,
                    HistoryKnown = history.KnownDisc,
                    HistoryMatches = history.Matches,
                    HistoryPriorReads = history.PriorReads,
                    HistoryDiffTracks = history.DiffTrackCount,
                    OutputVerificationKnown =
                        committedEncoded.OutputVerificationKnown,
                    LosslessOutput = committedEncoded.LosslessOutput,
                    OutputVerificationPerformed =
                        committedEncoded.OutputVerificationPerformed,
                    OutputVerificationDetail =
                        committedEncoded.OutputVerificationDetail,
                    OutputProofs = committedEncoded.OutputProofs,
                    CrcEvidence = BuildTestCopyCrcEvidence(reads, whole),
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
                // Release the live lease so this recovery copy does not remain permanently immune
                // to the age-gated startup sweep. Its marker still proves cleanup ownership.
                stagingWorkspace?.PreserveForRecovery();
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
                stagingWorkspace?.Dispose();
        }
    }

    /// <summary>
    /// First-use calibration gate shared by Rip, Verify, and Test &amp; Copy. The drive signature is
    /// discovered without changing output, an absent/stale record is calibrated once, and the current
    /// record is then reused. Test &amp; Copy additionally requires a proven sized flush (or proof that
    /// the drive does not cache) because its two-read claim depends on physical independence.
    /// </summary>
    private bool EnsureCalibration(
        char drive,
        Action<double, string> onProgress,
        bool requireIndependentReads,
        out string error)
    {
        error = "";
        string sig = "";
        var reader = new CDDriveReader();
        try
        {
            bool opened;
            lock (DriveService.ScsiGate) opened = reader.Open(drive);
            if (opened) sig = (reader.ARName ?? "").Trim();
        }
        catch (Exception ex)
        {
            _log.Error("rip", "drive signature read failed", ex);
        }
        finally { try { reader.Close(); } catch { } }

        if (string.IsNullOrWhiteSpace(sig))
        {
            error = "No audio disc was ready for drive calibration.";
            return false;
        }

        DriveCalibration? cal;
        try
        {
            cal = _calStore.Get(sig);
        }
        catch (Exception ex)
        {
            _log.Warn("rip", "drive calibration load failed: " + ex.GetType().Name);
            error = "Saved drive calibration is unreadable; repair or remove it before reading.";
            return false;
        }

        if (!DriveCalibrationService.IsCurrent(cal))
        {
            onProgress(0, cal == null
                ? "Calibrating drive before its first read..."
                : "Refreshing drive calibration...");
            if (_stopRequested)
            {
                error = "Stopped.";
                return false;
            }
            cal = _calService.Calibrate(drive);
            if (_stopRequested)
            {
                error = "Stopped.";
                return false;
            }
            if (cal == null)
            {
                error = "Drive calibration failed; no rip or verify was started.";
                return false;
            }
        }

        bool independent =
            (cal.CacheDefeat ?? "").StartsWith("Flush:", StringComparison.Ordinal) ||
            string.Equals(
                cal.CacheDefeat,
                "Media re-reads (no cache)",
                StringComparison.Ordinal);
        if (requireIndependentReads && !independent)
        {
            error =
                "Calibration could not prove an independent re-read strategy, so Secure/Paranoid reading cannot start.";
            return false;
        }
        return true;
    }

    /// <summary>Assemble the committed album folder and write the Test &amp; Copy proof. Always
    /// commits ONE staged read's folder wholesale - the read <see cref="TestAndCopyResolver.FullyVerifiedReadIndex"/>
    /// found to agree with some other read on every track - so the files are track-aligned by
    /// construction and there is nothing to sort or index per track.</summary>
    private (string outDir, int fileCount, bool historyRecorded, VerifyOutcome history)
        AssembleAndCommit(TestCopyResult resolve, List<VerifyRecord> reads,
            string sourceStagingAlbumDir, int sourceReadIndex, string outputBaseDir,
            string discId, string drive, int offset, int failedWindows, string format,
            string albumRelDir, VerifyResult encodedResult)
    {
        // Re-home the staged album folder using the RENDERED relative dir, not its last path segment:
        // a multi-disc scheme renders "Artist - Album [2-CD Set]/Disc 2", so taking the last segment
        // would commit every disc 2 of every set into one shared "Disc 2" folder and overwrite.
        string albumName = string.IsNullOrWhiteSpace(albumRelDir)
            ? Path.GetFileName(sourceStagingAlbumDir) : albumRelDir;
        string realBase = string.IsNullOrWhiteSpace(outputBaseDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "CUETools")
            : outputBaseDir;
        int sourceFileCount = CountAudioFilesRequired(sourceStagingAlbumDir, format);
        if (sourceFileCount <= 0)
            throw new InvalidDataException("The verified staged read contains no audio files.");

        // Reserve the final name across processes, then copy onto the destination volume. The final
        // path remains absent until the complete folder is renamed into place.
        using var publication = AlbumOutputTransaction.Reserve(realBase, albumName,
            m => _log.Info("rip", m));
        IReadOnlyList<LosslessOutputProof>? transferProofs =
            GetTransferProofs(encodedResult);
        CopyDirectoryRecursiveVerified(
            sourceStagingAlbumDir,
            publication.StagingDirectory,
            transferProofs,
            format,
            GetKnownAudioFormats(format));
        if (CountAudioFilesRequired(publication.StagingDirectory, format) != sourceFileCount)
            throw new InvalidDataException("The Test & Copy staging transfer is incomplete.");

        // Build the committed record from the single committed read's own checksums (never a
        // per-track mix of different reads).
        var source = (sourceReadIndex >= 0 && sourceReadIndex < reads.Count) ? reads[sourceReadIndex] : null;
        var newest = reads[reads.Count - 1];
        var committedRecord = new VerifyRecord
        {
            DiscId = discId,
            Tracks = BuildTestCopyCrcEvidence(reads, sourceReadIndex),
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
            Format = format,
            OutputVerificationKnown =
                encodedResult.OutputVerificationKnown,
            LosslessOutput = encodedResult.LosslessOutput,
            OutputVerificationPerformed =
                encodedResult.OutputVerificationPerformed,
            OutputVerificationDetail =
                encodedResult.OutputVerificationDetail,
        };

        string testCopyLogName =
            AlbumArtifactNames.TestCopyLogFileName(encodedResult.ArtifactStem);
        string logText = TestAndCopyLog.Format(resolve, reads, discId, drive, offset, failedWindows);
        if (encodedResult.OutputVerificationKnown)
            logText += "\nEncoded-output verification: " +
                encodedResult.OutputVerificationDetail + "\n";
        File.WriteAllText(
            Path.Combine(publication.StagingDirectory, testCopyLogName),
            logText);
        File.WriteAllText(Path.Combine(publication.StagingDirectory, "rip.verify"),
            VerifyHistoryStore.ToJson(committedRecord));
        ThrowIfStopRequested();
        string outDir = transferProofs != null
            ? PublishProofBoundOutput(
                publication,
                format,
                transferProofs,
                committedRecord,
                Path.Combine(
                    publication.StagingDirectory,
                    testCopyLogName))
            : publication.Publish();

        // History follows publication so a failed copy cannot masquerade as a retained verified rip.
        var history = new VerifyOutcome();
        bool historyRecorded = false;
        try
        {
            history = _history.CompareAndUpsert(committedRecord);
            historyRecorded = true;
            _log.Info("verify.history", $"disc={committedRecord.DiscId} known={(history.KnownDisc ? 1 : 0)} matches={(history.Matches ? 1 : 0)} diffTracks={history.DiffTrackCount}");
        }
        catch (Exception ex) { _log.Warn("verify.history", "test&copy upsert failed: " + ex.GetType().Name); }

        return (outDir, sourceFileCount, historyRecorded, history);
    }

    /// <summary>
    /// Preserve the full-range checksum from the committed read while also carrying the named
    /// Test (R1) and Copy (R2) evidence. A confirming R3 may be the committed source, but it does
    /// not silently rename itself "Copy" in the UI.
    /// </summary>
    internal static TrackCrc[] BuildTestCopyCrcEvidence(
        IReadOnlyList<VerifyRecord> reads,
        int sourceReadIndex)
    {
        TrackCrc[] source =
            sourceReadIndex >= 0 &&
            sourceReadIndex < reads.Count
                ? reads[sourceReadIndex]?.Tracks ?? Array.Empty<TrackCrc>()
                : Array.Empty<TrackCrc>();
        TrackCrc[] test =
            reads.Count > 0
                ? reads[0]?.Tracks ?? Array.Empty<TrackCrc>()
                : Array.Empty<TrackCrc>();
        TrackCrc[] copy =
            reads.Count > 1
                ? reads[1]?.Tracks ?? Array.Empty<TrackCrc>()
                : Array.Empty<TrackCrc>();
        int count = Math.Max(source.Length, Math.Max(test.Length, copy.Length));
        var result = new TrackCrc[count];
        for (int i = 0; i < count; i++)
        {
            TrackCrc? selected = i < source.Length ? source[i] : null;
            TrackCrc? testTrack = i < test.Length ? test[i] : null;
            TrackCrc? copyTrack = i < copy.Length ? copy[i] : null;
            result[i] = new TrackCrc
            {
                ArV1 = selected?.ArV1 ?? 0,
                ArV2 = selected?.ArV2 ?? 0,
                Crc32 = selected?.Crc32 ?? 0,
                TestCrc32 =
                    testTrack != null && testTrack.Crc32 != 0
                        ? testTrack.Crc32
                        : testTrack?.TestCrc32 ?? 0,
                CopyCrc32 =
                    copyTrack != null && copyTrack.Crc32 != 0
                        ? copyTrack.Crc32
                        : copyTrack?.CopyCrc32
                            ?? selected?.CopyCrc32
                            ?? testTrack?.CopyCrc32
                            ?? 0,
            };
        }
        return result;
    }

    /// <summary>Accept a held Test &amp; Copy's Copy read into the output folder anyway, flagged not
    /// test-verified, and discard the staging. Never writes to outputBaseDir on failure. Returns the
    /// committed output directory, or "" on failure.</summary>
    public string CommitCopyReadAnyway(TestCopyRunResult held, string outputBaseDir)
    {
        // Acceptance performs filesystem validation before its try/catch. Protect both sides of the
        // copy before that validation can produce a diagnostic.
        RedactOutputRoot(outputBaseDir);
        RedactStagingRoot(held?.CopyStagingDir);
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
            string format = held.Format ?? "";
            int sourceFileCount = CountAudioFilesRequired(held.CopyStagingDir, format);
            if (sourceFileCount <= 0)
                throw new InvalidDataException("The held Copy read contains no audio files.");
            using var publication = AlbumOutputTransaction.Reserve(realBase, albumName,
                m => _log.Info("rip", m));
            IReadOnlyList<LosslessOutputProof>? transferProofs =
                GetTransferProofs(held);
            CopyDirectoryRecursiveVerified(
                held.CopyStagingDir,
                publication.StagingDirectory,
                transferProofs,
                format,
                GetKnownAudioFormats(format));
            if (CountAudioFilesRequired(publication.StagingDirectory, format) != sourceFileCount)
                throw new InvalidDataException("The held Copy staging transfer is incomplete.");

            string heldList = string.Join(", ", System.Array.ConvertAll(held.HeldTracks, x => (x + 1).ToString()));
            string log = "Test & Copy log\n\n" +
                "NOT test-verified - accepted by user without agreement.\n" +
                $"Reads used: {held.ReadsUsed}\n" +
                $"Held track(s) (no agreement): {heldList}\n" +
                (held.OutputVerificationKnown
                    ? "Encoded-output verification: " +
                        held.OutputVerificationDetail + "\n"
                    : "");
            string testCopyLogName =
                AlbumArtifactNames.TestCopyLogFileName(held.ArtifactStem);
            File.WriteAllText(
                Path.Combine(publication.StagingDirectory, testCopyLogName),
                log);
            string outDir = transferProofs != null
                ? PublishProofBoundOutput(
                    publication,
                    format,
                    transferProofs,
                    null,
                    Path.Combine(
                        publication.StagingDirectory,
                        testCopyLogName))
                : publication.Publish();

            try
            {
                _log.Info("rip", $"testcopy accept-anyway reads={held.ReadsUsed} heldTracks={held.HeldTracks.Length}");
            }
            catch { }
            try { DiscardStaging(held); }
            catch { }
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
        if (held == null) return;
        RedactStagingRoot(held.CopyStagingDir);
        if (held.StagingDirs != null)
            foreach (string dir in held.StagingDirs) RedactStagingRoot(dir);
        held.StagingWorkspace?.DeleteOwned();
        if (held.StagingDirs == null) return;
        foreach (var dir in held.StagingDirs) DeleteStagingDir(dir);
    }

    private void DeleteStagingDir(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            if (Directory.Exists(dir) &&
                !TestCopyStagingWorkspace.TryDeleteOwnedWorkspace(dir))
                _log.Warn("rip",
                    "test&copy staging cleanup skipped: ownership could not be proven");
        }
        catch (Exception ex) { _log.Warn("rip", "test&copy staging cleanup failed: " + ex.GetType().Name); }
    }

    private static IReadOnlyList<LosslessOutputProof>? GetTransferProofs(
        VerifyResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        return GetTransferProofs(
            result.OutputVerificationPerformed,
            result.OutputProofs);
    }

    private static IReadOnlyList<LosslessOutputProof>? GetTransferProofs(
        TestCopyRunResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        return GetTransferProofs(
            result.OutputVerificationPerformed,
            result.OutputProofs);
    }

    private static IReadOnlyList<LosslessOutputProof>? GetTransferProofs(
        bool verificationPerformed,
        IReadOnlyList<LosslessOutputProof>? proofs)
    {
        int count = proofs?.Count ?? 0;
        if (verificationPerformed)
        {
            if (count == 0)
            {
                throw new InvalidDataException(
                    "Encoded-output assurance has no transferable proof set.");
            }
            return proofs;
        }

        if (count != 0)
        {
            throw new InvalidDataException(
                "Encoded-output proofs exist without a performed assurance claim.");
        }
        return null;
    }

    internal static IReadOnlyList<LosslessOutputProof>
        SnapshotAndValidateOutputProofs(
            string[]? expectedPaths,
            string rootDirectory,
            IReadOnlyList<LosslessOutputProof>? proofs)
    {
        ValidateEncodedOutputs(expectedPaths, rootDirectory);
        if (proofs == null ||
            expectedPaths == null ||
            proofs.Count != expectedPaths.Length)
        {
            throw new InvalidDataException(
                "Final-output assurance does not cover the exact encoded output set.");
        }

        string root = Path.GetFullPath(rootDirectory);
        var expected = new HashSet<string>(
            expectedPaths.Length,
            StringComparer.OrdinalIgnoreCase);
        foreach (string path in expectedPaths)
            expected.Add(Path.GetFullPath(path));

        var snapshot = new List<LosslessOutputProof>(proofs.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < proofs.Count; i++)
        {
            LosslessOutputProof proof = proofs[i] ??
                throw new InvalidDataException(
                    "Final-output assurance contains an empty proof.");
            string provedPath =
                Path.GetFullPath(proof.GetConstrainedPath(root));
            if (!expected.Contains(provedPath) ||
                !seen.Add(provedPath) ||
                !string.Equals(
                    provedPath,
                    Path.GetFullPath(expectedPaths[i]),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Final-output assurance names a missing, duplicate, unexpected, or misordered output.");
            }
            proof.VerifyFile(root);
            snapshot.Add(proof);
        }
        if (seen.Count != expected.Count)
        {
            throw new InvalidDataException(
                "Final-output assurance missed an encoded output.");
        }
        return new System.Collections.ObjectModel
            .ReadOnlyCollection<LosslessOutputProof>(snapshot);
    }

    internal static void CopyDirectoryRecursiveVerified(
        string srcDir,
        string dstDir,
        IReadOnlyList<LosslessOutputProof>? outputProofs = null,
        string? provedFormat = null,
        IEnumerable<string>? knownAudioFormats = null)
    {
        string sourceRoot = Path.GetFullPath(srcDir);
        string destinationRoot = Path.GetFullPath(dstDir);
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException("The source staging directory is missing.");
        Directory.CreateDirectory(destinationRoot);
        if ((File.GetAttributes(sourceRoot) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(destinationRoot) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A staging root cannot be a reparse point.");

        Dictionary<string, LosslessOutputProof>? proofByRelativePath = null;
        HashSet<string>? copiedProofs = null;
        if (outputProofs != null)
        {
            if (string.IsNullOrWhiteSpace(provedFormat))
            {
                throw new InvalidDataException(
                    "A proved transfer requires the encoded audio format.");
            }
            ValidateProofSetAgainstAudioFiles(
                sourceRoot,
                provedFormat,
                outputProofs,
                verifyBytes: true,
                knownAudioFormats);
            proofByRelativePath =
                BuildProofPathMap(sourceRoot, outputProofs);
            copiedProofs =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var pending = new Stack<string>();
        pending.Push(sourceRoot);
        while (pending.Count != 0)
        {
            string current = pending.Pop();
            foreach (string source in Directory.EnumerateFileSystemEntries(current, "*",
                SearchOption.TopDirectoryOnly))
            {
                FileAttributes attributes = File.GetAttributes(source);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A staging payload cannot contain a reparse point.");
                string relative = Path.GetRelativePath(sourceRoot, source);
                string destination = ResolveCopyDestination(destinationRoot, relative);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.CreateDirectory(destination);
                    pending.Push(source);
                    continue;
                }

                string normalizedRelative =
                    NormalizeRelativePath(relative);
                LosslessOutputProof? proof = null;
                if (proofByRelativePath != null)
                    proofByRelativePath.TryGetValue(
                        normalizedRelative,
                        out proof);

                if (proof == null)
                {
                    File.Copy(source, destination, false);
                    VerifyCopiedFile(source, destination);
                    continue;
                }

                using (FileStream sourceLease =
                    proof.OpenVerifiedReadLease(sourceRoot))
                {
                    if (!string.Equals(
                            Path.GetFullPath(sourceLease.Name),
                            Path.GetFullPath(source),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "A proved source path changed during transfer.");
                    }
                    using (var destinationStream = new FileStream(
                        destination,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        0x10000,
                        FileOptions.SequentialScan))
                    {
                        sourceLease.CopyTo(destinationStream);
                        destinationStream.Flush(flushToDisk: true);
                    }
                }
                proof.VerifyFile(destinationRoot);
                copiedProofs!.Add(normalizedRelative);
            }
        }

        if (proofByRelativePath != null)
        {
            if (copiedProofs!.Count != proofByRelativePath.Count)
            {
                throw new InvalidDataException(
                    "The proved transfer missed an encoded output.");
            }
            ValidateProofSetAgainstAudioFiles(
                destinationRoot,
                provedFormat!,
                outputProofs!,
                verifyBytes: true,
                knownAudioFormats);
        }
    }

    private string PublishProofBoundOutput(
        AlbumOutputTransaction publication,
        string format,
        IReadOnlyList<LosslessOutputProof> proofs,
        VerifyRecord? record,
        string? verificationLogPath)
    {
        IReadOnlyCollection<string> knownAudioFormats =
            GetKnownAudioFormats(format);
        var leases = new List<FileStream>(proofs.Count);
        bool moved = false;
        string published = publication.DestinationDirectory;
        try
        {
            // Windows refuses a parent-directory rename while a child file has an active sharing
            // lease, even when delete sharing is requested. Bind the complete stage immediately
            // before the move, then re-open and freeze every proof at the destination before the
            // reservation/ownership marker is released or success is reported.
            ValidateProofSetAgainstAudioFiles(
                publication.StagingDirectory,
                format,
                proofs,
                verifyBytes: true,
                knownAudioFormats);

            published = publication.PublishPendingValidation();
            moved = true;
            AfterProofDirectoryMoveForTest?.Invoke(published);
            ValidateProofSetAgainstAudioFiles(
                published,
                format,
                proofs,
                verifyBytes: false,
                knownAudioFormats);
            foreach (LosslessOutputProof proof in proofs)
                leases.Add(proof.OpenVerifiedReadLease(published));
            ValidateProofSetAgainstAudioFiles(
                published,
                format,
                proofs,
                verifyBytes: false,
                knownAudioFormats);
            publication.CompletePublication();
            return published;
        }
        catch (Exception ex) when (moved)
        {
            for (int i = leases.Count - 1; i >= 0; i--)
                leases[i].Dispose();
            leases.Clear();
            InvalidatePublishedAssurance(
                published,
                record,
                verificationLogPath);
            string retained = published;
            try
            {
                retained =
                    publication.QuarantinePublishedProofFailure();
            }
            catch
            {
                // A non-cooperating writer may prevent the quarantine rename. The operation still
                // fails closed and never records history or reports encoded-output assurance.
            }
            throw new InvalidDataException(
                "The published album failed its final encoded-output proof boundary. " +
                "The output was retained as incomplete at: " + retained,
                ex);
        }
        finally
        {
            for (int i = leases.Count - 1; i >= 0; i--)
                leases[i].Dispose();
        }
    }

    private static void InvalidatePublishedAssurance(
        string publishedRoot,
        VerifyRecord? record,
        string? verificationLogPath)
    {
        const string detail =
            "not retained (the published files failed the final encoded-output proof boundary)";
        if (record != null)
        {
            try
            {
                record.OutputVerificationPerformed = false;
                record.OutputVerificationDetail = detail;
                string path = Path.Combine(
                    publishedRoot,
                    "rip.verify");
                using var stream = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read);
                using var writer = new StreamWriter(
                    stream,
                    new System.Text.UTF8Encoding(false),
                    1024,
                    leaveOpen: true);
                writer.Write(VerifyHistoryStore.ToJson(record));
                writer.Flush();
                stream.Flush(true);
            }
            catch
            {
                // The transaction-level failure marker and quarantine remain the primary signal.
            }
        }
        if (!string.IsNullOrWhiteSpace(verificationLogPath))
        {
            try
            {
                string relocated = Path.Combine(
                    publishedRoot,
                    Path.GetFileName(verificationLogPath));
                using var stream = new FileStream(
                    relocated,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);
                using var writer = new StreamWriter(
                    stream,
                    new System.Text.UTF8Encoding(false),
                    1024,
                    leaveOpen: true);
                writer.WriteLine();
                writer.WriteLine(
                    "Encoded-output verification invalidated: " +
                    detail);
                writer.Flush();
                stream.Flush(true);
            }
            catch
            {
            }
        }
    }

    private IReadOnlyCollection<string> GetKnownAudioFormats(
        string selectedFormat)
    {
        var formats = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(selectedFormat))
            formats.Add(selectedFormat.Trim().TrimStart('.'));
        foreach (string extension in _config.formats.Keys)
        {
            if (!string.IsNullOrWhiteSpace(extension))
                formats.Add(extension.Trim().TrimStart('.'));
        }
        foreach (IAudioEncoderSettings encoder in
            CUEProcessorPlugins.encs)
        {
            if (!string.IsNullOrWhiteSpace(encoder.Extension))
                formats.Add(encoder.Extension.Trim().TrimStart('.'));
        }
        foreach (IAudioDecoderSettings decoder in
            CUEProcessorPlugins.decs)
        {
            if (!string.IsNullOrWhiteSpace(decoder.Extension))
                formats.Add(decoder.Extension.Trim().TrimStart('.'));
        }
        return formats;
    }

    private static Dictionary<string, LosslessOutputProof>
        BuildProofPathMap(
            string rootDirectory,
            IReadOnlyList<LosslessOutputProof> proofs)
    {
        var result = new Dictionary<string, LosslessOutputProof>(
            proofs.Count,
            StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < proofs.Count; i++)
        {
            LosslessOutputProof proof = proofs[i] ??
                throw new InvalidDataException(
                    "The encoded-output proof set contains an empty entry.");
            string path = proof.GetConstrainedPath(rootDirectory);
            string relative = NormalizeRelativePath(
                Path.GetRelativePath(rootDirectory, path));
            if (!result.TryAdd(relative, proof))
            {
                throw new InvalidDataException(
                    "The encoded-output proof set contains a duplicate path.");
            }
        }
        return result;
    }

    private static void ValidateProofSetAgainstAudioFiles(
        string rootDirectory,
        string format,
        IReadOnlyList<LosslessOutputProof> proofs,
        bool verifyBytes,
        IEnumerable<string>? knownAudioFormats = null)
    {
        if (proofs == null)
            throw new ArgumentNullException(nameof(proofs));
        if (string.IsNullOrWhiteSpace(format))
            throw new InvalidDataException(
                "The encoded audio format is missing.");

        string root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(
                "The proved output root is missing.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException(
                "The proved output root cannot be a reparse point.");

        string extension = "." + format.Trim().TrimStart('.');
        var audioExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            extension
        };
        if (knownAudioFormats != null)
        {
            foreach (string knownFormat in knownAudioFormats)
            {
                if (!string.IsNullOrWhiteSpace(knownFormat))
                {
                    audioExtensions.Add(
                        "." + knownFormat.Trim().TrimStart('.'));
                }
            }
        }
        var audioPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            string current = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(
                current,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        "The proved output set cannot contain a reparse point.");
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                string entryExtension = Path.GetExtension(entry);
                if (audioExtensions.Contains(entryExtension) &&
                    !string.Equals(
                        entryExtension,
                        extension,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Encoded-output assurance found an unproved audio file in another registered format.");
                }
                if (string.Equals(
                    entryExtension,
                    extension,
                    StringComparison.OrdinalIgnoreCase))
                {
                    audioPaths.Add(NormalizeRelativePath(
                        Path.GetRelativePath(root, entry)));
                }
            }
        }

        Dictionary<string, LosslessOutputProof> proofPaths =
            BuildProofPathMap(root, proofs);
        if (proofPaths.Count != audioPaths.Count)
        {
            throw new InvalidDataException(
                "Encoded-output assurance does not cover the exact audio file set.");
        }
        foreach (KeyValuePair<string, LosslessOutputProof> entry in
            proofPaths)
        {
            if (!audioPaths.Contains(entry.Key))
            {
                throw new InvalidDataException(
                    "Encoded-output assurance names a missing or unexpected audio file.");
            }
            if (verifyBytes)
                entry.Value.VerifyFile(root);
        }
    }

    private static string NormalizeRelativePath(string relative)
    {
        return relative.Replace(
            Path.AltDirectorySeparatorChar,
            Path.DirectorySeparatorChar);
    }

    private static string ResolveCopyDestination(string destinationRoot, string relative)
    {
        string destination = Path.GetFullPath(Path.Combine(destinationRoot, relative));
        string prefix = destinationRoot.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("A staged file escapes the destination transaction.");
        return destination;
    }

    private static void VerifyCopiedFile(string source, string destination)
    {
        var sourceInfo = new FileInfo(source);
        var destinationInfo = new FileInfo(destination);
        if (sourceInfo.Length != destinationInfo.Length)
            throw new InvalidDataException("A staged file copy has the wrong length.");

        using SHA256 algorithm = SHA256.Create();
        byte[] sourceHash;
        byte[] destinationHash;
        using (FileStream stream = File.OpenRead(source))
            sourceHash = algorithm.ComputeHash(stream);
        algorithm.Initialize();
        using (FileStream stream = File.OpenRead(destination))
            destinationHash = algorithm.ComputeHash(stream);
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, destinationHash))
            throw new InvalidDataException("A staged file copy failed read-back verification.");
    }

    private static int CountAudioFilesRequired(string dir, string format)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            throw new DirectoryNotFoundException("The staged album directory is missing.");
        if (string.IsNullOrWhiteSpace(format))
            throw new InvalidDataException("The staged album format is missing.");

        string root = Path.GetFullPath(dir);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The staged album root cannot be a reparse point.");
        int count = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            string current = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(current, "*",
                SearchOption.TopDirectoryOnly))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The staged album cannot contain a reparse point.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                if (string.Equals(Path.GetExtension(entry), "." + format,
                    StringComparison.OrdinalIgnoreCase))
                    count++;
            }
        }
        return count;
    }

    internal static void ValidateEncodedOutputs(string[]? paths, string stagingDirectory)
    {
        if (paths == null || paths.Length == 0)
            throw new InvalidDataException("The encoder reported no expected audio outputs.");

        string root = Path.GetFullPath(stagingDirectory);
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Directory.Exists(root) ||
            (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The encoder staging directory is not a regular directory.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("An expected encoded audio file is missing.");
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "An expected encoded audio path escaped the output transaction.");
            if (!seen.Add(full))
                throw new InvalidDataException(
                    "The encoder reported a duplicate audio output path.");
            RequireNoReparsePointAncestry(root,
                Path.GetDirectoryName(full) ??
                    throw new InvalidDataException(
                        "An expected encoded audio path has no parent directory."));
            if (!File.Exists(full))
                throw new InvalidDataException("An expected encoded audio file is missing.");
            FileAttributes attributes = File.GetAttributes(full);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException(
                    "An expected encoded audio output is not a regular file.");
            if (new FileInfo(full).Length <= 0)
                throw new InvalidDataException("An encoded audio file is empty.");
        }
    }

    private static void RequireNoReparsePointAncestry(string root, string targetDirectory)
    {
        string relative = Path.GetRelativePath(root, targetDirectory);
        string current = root;
        foreach (string part in relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "An expected encoded audio path crosses a link or reparse point.");
        }
    }

}
