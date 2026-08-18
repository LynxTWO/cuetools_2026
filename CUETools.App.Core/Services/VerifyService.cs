using System;
using CUETools.CTDB;
using CUETools.Processor;

namespace CUETools.Wpf.Services;

public sealed class VerifyTrackResult
{
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Crc32 { get; init; } = "";
    public int ArConfidence { get; init; }
    public int ArTotal { get; init; }
    public int CtdbConfidence { get; init; }
}

public sealed class VerifyFilesResult
{
    /// <summary>
    /// What a CTDB submission offer did, as one line for the status bar, or empty when
    /// none was made. Settable rather than init because the offer happens after this
    /// result exists: the candidate it needs is built from the metadata Gather extracts.
    /// </summary>
    public string SubmissionStatus { get; set; } = "";

    public bool Ok { get; init; }
    public string Error { get; init; } = "";
    public string Status { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string Year { get; init; } = "";
    public string Genre { get; init; } = "";
    public string Label { get; init; } = "";
    public string CatalogNumber { get; init; } = "";
    public string Barcode { get; init; } = "";
    public string Country { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public string DiscNumber { get; init; } = "";
    public string TotalDiscs { get; init; } = "";
    public string DiscName { get; init; } = "";
    public string TocId { get; init; } = "";
    public long DurationSeconds { get; init; }
    public int TrackCount { get; init; }
    public VerifyTrackResult[] Tracks { get; init; } = Array.Empty<VerifyTrackResult>();
    public int ArConfidence { get; init; }
    public int ArTotal { get; init; }
    public int CtdbConfidence { get; init; }
    public int CtdbTotal { get; init; }
    public bool Accurate { get; init; }
    public bool HasErrors { get; init; }
    public bool CanRecover { get; init; }
    /// <summary>
    /// The lookup did not complete: a transport error, or an HTTP status other than
    /// NotFound. A database that answered "this disc is not here" is NOT a failure and
    /// leaves these false. Without the distinction both cases arrive as a zero total and
    /// the UI reports "not in database" for a service that never answered.
    /// </summary>
    public bool ArLookupFailed { get; init; }
    public bool CtdbLookupFailed { get; init; }
    public string Source { get; init; } = "";
    /// <summary>The published repaired-copy directory. Empty for verification and failed repair.</summary>
    public string OutputPath { get; init; } = "";

    // CTDB Reed-Solomon repair detail, read straight off the matching DBEntry.repair after the verify
    // pass (the same data a repair copy would apply). All real: RepairSamples is the count of
    // 16-bit samples the parity can/did reconstruct, RepairSectorMap is a downsampled view of exactly
    // which sectors were damaged (from AffectedSectorArray), RepairNpar the parity depth used.
    public int RepairSamples { get; init; }
    public int RepairSectors { get; init; }
    public int RepairTotalSectors { get; init; }
    public int RepairNpar { get; init; }
    /// <summary>Repair headroom (R115): the fix's worst per-stripe error count vs the npar/2
    /// stripe capacity. At capacity, one more error in that stripe would have been
    /// unrecoverable - the honest basis for a re-rip vs repair decision. Zero when unknown.</summary>
    public int RepairWorstStripeErrors { get; init; }
    public int RepairStripeCapacity { get; init; }
    public double[] RepairSectorMap { get; init; } = System.Array.Empty<double>();
    public string RepairRanges { get; init; } = "";
    public bool RepairApplied { get; init; }   // true only after a repaired copy was verified and published
}

/// <summary>Verify and repair existing audio files (a .cue, an .m3u, or a supported lossless file) against
/// AccurateRip and CTDB - the file-based twin of the disc rip. Blocking + network, so callers
/// marshal it onto a background thread.</summary>
public interface IVerifyService
{
    VerifyFilesResult Verify(string path, Action<double, string> onProgress);

    /// <summary>Drive the engine's "repair" script into an isolated sibling staging directory,
    /// independently verify the repaired files, then atomically publish a new sibling directory.
    /// The source files are never replaced.</summary>
    VerifyFilesResult Repair(string path, Action<double, string> onProgress);
}

public sealed class VerifyService : IVerifyService
{
    private readonly CUEConfig _config;
    private readonly IDiagnosticLog _log;
    private readonly IRepairEngine _repairEngine;

    public VerifyService(CUEConfig config, IDiagnosticLog log)
        : this(config, log, new CueRepairEngine(log))
    {
    }

    internal VerifyService(CUEConfig config, IDiagnosticLog log, IRepairEngine repairEngine)
    {
        _config = config;
        _log = log;
        _repairEngine = repairEngine;
    }

    /// <summary>
    /// Optional (SLICE-012). Null means no submission is ever offered, which is what every
    /// head did before the consent dialog existed. Settable rather than constructor-injected
    /// so the many existing call sites keep working and keep their old behaviour.
    /// </summary>
    public CtdbSubmissionService? Submissions { get; set; }

    /// <summary>
    /// Offers this verify's result to the CUETools Database, if a head has wired a consent
    /// surface. Never throws: a submission must not change a verify verdict, and the result
    /// has already been built by the time this runs.
    ///
    /// FailedWindows is zero because a verify reads files, not a disc. The unrecoverable
    /// window count that D-070 gates on belongs to a rip, and the rip path supplies its own.
    /// </summary>
    private void OfferSubmission(CUESheet cue, VerifyFilesResult result)
    {
        if (Submissions == null) return;
        try
        {
            CtdbSubmissionOutcome outcome = Submissions.Offer(cue.CTDB, new CtdbSubmissionCandidate
            {
                RunCompleted = result.Ok,
                // Either database failing to answer blocks a submission. Uploading a rip
                // whose verdict came from one source claims more than the run established.
                LookupFailed = result.ArLookupFailed || result.CtdbLookupFailed,
                FailedWindows = 0,
                Salvaged = false,
                Held = false,
                Album = result.Album,
                Artist = result.Artist,
                Barcode = result.Barcode,
                Confidence = result.ArConfidence,
            });
            // Silence after a consent is indistinguishable from a refusal, and a user who
            // said yes and saw nothing would reasonably believe they had contributed.
            result.SubmissionStatus = outcome.StatusText;
        }
        catch (Exception ex)
        {
            _log.Warn("ctdb", "submission offer failed, verdict unchanged: " + ex.GetType().Name);
        }
    }

    /// <summary>Root the sheet's output at the verified source so an AccurateRip log written during a
    /// verify lands next to those files. Best-effort: a verify must never fail because of its log.</summary>
    private void TrySetVerifyLogTarget(CUESheet cue, string source)
    {
        try
        {
            string dir = System.IO.Directory.Exists(source)
                ? source
                : System.IO.Path.GetDirectoryName(source) ?? "";
            if (dir.Length == 0) return;
            string stem = System.IO.Path.GetFileNameWithoutExtension(
                System.IO.Directory.Exists(source) ? dir : source);
            if (string.IsNullOrWhiteSpace(stem)) stem = "verify";
            cue.GenerateFilenames(AudioEncoderType.Lossless, "flac", System.IO.Path.Combine(dir, stem + ".cue"));
        }
        catch (Exception ex) { _log.Warn("verify", "could not set the AR-log target: " + ex.GetType().Name); }
    }

    private void RedactSourcePath(string? path)
    {
        // Register the raw selection before any normalization or filesystem call can fail. Also
        // register the absolute file and its containing root because framework/native exceptions
        // commonly report either form.
        try { _log.Redact(path); } catch { }
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            string full = System.IO.Path.GetFullPath(path);
            string? parent = System.IO.Directory.Exists(full)
                ? full
                : System.IO.Path.GetDirectoryName(full);
            _log.Redact(full, parent);
        }
        catch { }
    }

    public VerifyFilesResult Verify(string path, Action<double, string> onProgress)
    {
        RedactSourcePath(path);
        CUESheet? cue = null;
        try
        {
            cue = new CUESheet(_config);
            cue.CUEToolsProgress += (s, e) => onProgress(Clamp(e.percent), e.status);
            cue.Open(path);

            // Fast on purpose, same reason as the rip path: this call is the CTDB VERIFICATION
            // contact, not disc identification. A file being verified already carries its metadata,
            // so a broader sweep here would only add latency. The "Metadata search" setting governs
            // identification instead.
            cue.UseCUEToolsDB("CUETools 2026", null, true, CTDBMetadataSearch.Fast);
            cue.UseAccurateRip();
            cue.Action = CUEAction.Verify;
            // Give the sheet an output location rooted at the SOURCE, so "Write AccurateRip log on
            // verify" has somewhere real to put the .accurip - next to the files that were verified.
            // Without this the sheet has no output path at all: the setting used to crash the verify
            // (CreateDirectory(null)) and, once guarded, silently wrote nothing. Action is already
            // Verify here, so no audio is produced by naming an output path.
            TrySetVerifyLogTarget(cue, path);

            onProgress(0, "Verifying against AccurateRip + CTDB...");
            string status = cue.Go();
            onProgress(1, status);

            VerifyFilesResult result =
                Gather(cue, status, path, ok: true, error: "", applied: false, _log);
            // Offered here, while cue.CTDB is the live object from this run. Its parity and
            // checksums are what upload, so a submission cannot be replayed from the stored
            // verdict later. Null Submissions, or a head with no consent surface, means the
            // offer is never made and nothing leaves the machine.
            OfferSubmission(cue, result);
            return result;
        }
        catch (Exception ex)
        {
            _log.Error("verify", "file verify failed", ex);
            return new VerifyFilesResult { Error = ex.Message, Source = path };
        }
        finally
        {
            if (cue != null)
            {
                try { cue.Close(); }
                catch (Exception ex)
                {
                    _log.Warn("verify", "could not close verify resources: " +
                        ex.GetType().Name);
                }
            }
        }
    }

    public VerifyFilesResult Repair(string path, Action<double, string> onProgress)
    {
        RedactSourcePath(path);
        RepairWorkspace? workspace = null;
        try
        {
            if (System.IO.Directory.Exists(path))
                return Failure(path, "Choose a .cue, .m3u, or supported lossless file; folder input is not supported.");
            if (!System.IO.File.Exists(path))
                return Failure(path, "The selected source file does not exist.");

            CUEConfig repairConfig = CreateRepairConfig(_config);
            workspace = RepairWorkspace.Create(path);
            // Repair uses a generated sibling staging/output path. Register it before the repair
            // engine or a progress callback can include it in an exception.
            try { _log.Redact(workspace.StagingDirectory); } catch { }

            onProgress(0, "Building an isolated repaired copy...");
            RepairEngineResult repaired = _repairEngine.Repair(
                path, workspace.StagingDirectory, repairConfig, onProgress);

            if (!repaired.Result.Ok || !repaired.Applied || !repaired.Result.RepairApplied)
                throw new InvalidOperationException(
                    "CTDB did not apply a repair. The source may already match or no recoverable entry was selected.");

            workspace.RequireNonemptyOutputs(repaired.StagedCuePath, repaired.ExpectedAudioPaths);

            onProgress(0.9, "Independently verifying the repaired copy...");
            RepairVerificationResult verified = _repairEngine.Verify(
                repaired.StagedCuePath, new CUEConfig(repairConfig), onProgress);
            if (!verified.Result.Ok || !verified.Verified)
                throw new InvalidOperationException(
                    "The repaired copy could not be independently verified against AccurateRip or CTDB.");

            RepairReceipt receipt = workspace.SealEvidence(
                repaired.SourceProofs,
                verified.VerifiedOutputProofs,
                verified.AccurateRipLogPath,
                repaired.Result,
                verified.Result);
            string published = workspace.Publish();
            workspace = null;
            string status =
                "Repaired copy independently verified with durable evidence and saved to " +
                published;
            try
            {
                _log.Info(
                    "verify",
                    "repair published applied=1 independently_verified=1 " +
                    $"tracks={receipt.TrackCount} samples={receipt.RepairSamples} " +
                    $"sectors={receipt.RepairSectors} " +
                    $"ar_conf={receipt.AccurateRipConfidence}/{receipt.AccurateRipTotal} " +
                    $"ctdb_conf={receipt.CtdbConfidence}/{receipt.CtdbTotal} " +
                    $"source_files={receipt.SourceFiles.Length} " +
                    $"output_files={receipt.OutputFiles.Length} evidence=1");
            }
            catch
            {
                // Publication is committed. A diagnostic sink must not turn a verified repair into
                // a reported failure or invite a duplicate repair.
            }
            // Publication already happened. A UI progress callback must not turn that committed
            // success into a reported failure.
            try { onProgress(1, status); }
            catch (Exception ex)
            {
                try
                {
                    _log.Warn("verify", "repair completion callback failed: " +
                        ex.GetType().Name);
                }
                catch { }
            }
            return PublishedRepair(repaired.Result, verified.Result, path, published, status);
        }
        catch (Exception ex)
        {
            _log.Error("verify", "repair failed", ex);
            return Failure(path, ex.Message);
        }
        finally
        {
            if (workspace != null)
            {
                try { workspace.Dispose(); }
                catch (Exception ex)
                {
                    _log.Warn("verify", "could not remove owned repair staging directory: " + ex.GetType().Name);
                }
            }
        }
    }

    internal static CUEConfig CreateRepairConfig(CUEConfig source)
    {
        var copy = new CUEConfig(source)
        {
            writeArTagsOnVerify = false,
            writeArLogOnVerify = false,
            writeArLogOnConvert = false,
            arLogToSourceFolder = false,
            // Repair is a source-preserving operation, not a normal conversion
            // preset. CUESheet constrains these to Path.GetFileName(...) before
            // combining them with the owned staging directory.
            keepOriginalFilenames = true,
            trackFilenameFormat = "%tracknumber%",
            singleFilenameFormat = "album",
            // Existing files are authoritative during repair. The cue can contain
            // stale or normalized text that differs from the user's exact tags.
            writeBasicTagsFromCUEData = false,
            copyBasicTags = true,
            copyUnknownTags = true,
            CopyAlbumArt = true,
            // AccurateRip/CTDB results describe the source payload and become stale
            // when CTDB changes samples. They must be recomputed by the independent
            // post-repair verify, never copied or generated during the repair encode.
            writeArTagsOnEncode = false,
            // CopyAlbumArt loads and writes embedded source pictures. Leaving
            // embedAlbumArt off avoids turning repair into a folder-art scan or
            // remote metadata-art import.
            embedAlbumArt = false,
            extractAlbumArt = false,
            extractLog = false,
            createM3U = false,
            createCUEFileInTracksMode = true,
            createCUEFileWhenEmbedded = true
        };
        copy.advanced.WriteCTDBTagsOnEncode = false;
        copy.advanced.WriteCTDBTagsOnVerify = false;
        // Preserve the source's exact disc-identity tag. Regenerating CDTOC here can
        // normalize it or append a second value even though repair must preserve tags.
        copy.advanced.WriteCDTOCTag = false;
        copy.advanced.CreateTOC = false;
        return copy;
    }

    private static VerifyFilesResult Failure(string path, string error)
        => new() { Error = error, Source = path };

    private static VerifyFilesResult PublishedRepair(
        VerifyFilesResult repaired,
        VerifyFilesResult verified,
        string source,
        string outputPath,
        string status)
    {
        return new VerifyFilesResult
        {
            Ok = true,
            Status = status,
            Artist = verified.Artist.Length == 0 ? repaired.Artist : verified.Artist,
            Album = verified.Album.Length == 0 ? repaired.Album : verified.Album,
            Year = verified.Year.Length == 0 ? repaired.Year : verified.Year,
            Genre = verified.Genre.Length == 0 ? repaired.Genre : verified.Genre,
            Label = verified.Label.Length == 0 ? repaired.Label : verified.Label,
            CatalogNumber = verified.CatalogNumber.Length == 0
                ? repaired.CatalogNumber
                : verified.CatalogNumber,
            Barcode = verified.Barcode.Length == 0 ? repaired.Barcode : verified.Barcode,
            Country = verified.Country.Length == 0 ? repaired.Country : verified.Country,
            ReleaseDate = verified.ReleaseDate.Length == 0
                ? repaired.ReleaseDate
                : verified.ReleaseDate,
            DiscNumber = verified.DiscNumber.Length == 0
                ? repaired.DiscNumber
                : verified.DiscNumber,
            TotalDiscs = verified.TotalDiscs.Length == 0
                ? repaired.TotalDiscs
                : verified.TotalDiscs,
            DiscName = verified.DiscName.Length == 0 ? repaired.DiscName : verified.DiscName,
            TocId = verified.TocId.Length == 0 ? repaired.TocId : verified.TocId,
            DurationSeconds = verified.DurationSeconds == 0
                ? repaired.DurationSeconds
                : verified.DurationSeconds,
            TrackCount = verified.TrackCount == 0 ? repaired.TrackCount : verified.TrackCount,
            Tracks = verified.Tracks.Length == 0 ? repaired.Tracks : verified.Tracks,
            ArConfidence = verified.ArConfidence,
            ArTotal = verified.ArTotal,
            CtdbConfidence = verified.CtdbConfidence,
            CtdbTotal = verified.CtdbTotal,
            Accurate = verified.Accurate,
            HasErrors = false,
            CanRecover = false,
            ArLookupFailed = verified.ArLookupFailed,
            CtdbLookupFailed = verified.CtdbLookupFailed,
            Source = source,
            OutputPath = outputPath,
            RepairSamples = repaired.RepairSamples,
            RepairSectors = repaired.RepairSectors,
            RepairTotalSectors = repaired.RepairTotalSectors,
            RepairNpar = repaired.RepairNpar,
            RepairWorstStripeErrors = repaired.RepairWorstStripeErrors,
            RepairStripeCapacity = repaired.RepairStripeCapacity,
            RepairSectorMap = repaired.RepairSectorMap,
            RepairRanges = repaired.RepairRanges,
            RepairApplied = true
        };
    }

    internal static VerifyFilesResult Gather(
        CUESheet cue,
        string status,
        string path,
        bool ok,
        string error,
        bool applied,
        IDiagnosticLog log)
    {
        int arConf = 0, arTotal = 0;
        bool arFailed = false;
        // a throw here must not be reported as "not in database" - that is a different fact
        try { arConf = (int)cue.ArVerify.WorstConfidence(); arTotal = (int)cue.ArVerify.WorstTotal(); }
        catch (Exception ex)
        {
            arFailed = true;
            log.Warn("verify", "AccurateRip result read failed: " + ex.GetType().Name);
        }
        // A service that answered "not here" is a real answer; anything else is a failed
        // lookup, and the two must not share the "not in database" wording.
        try { arFailed |= LookupFailed(cue.ArVerify.ExceptionStatus, cue.ArVerify.ResponseStatus); }
        catch (Exception ex) { log.Warn("verify", "AccurateRip status read failed: " + ex.GetType().Name); }

        int ctConf = 0, ctTotal = 0;
        bool hasErrors = false, canRecover = false, ctdbFailed = false;
        DBEntry? rep = null;
        try
        {
            ctConf = cue.CTDB.Confidence;
            ctTotal = cue.CTDB.Total;
            ctdbFailed = LookupFailed(cue.CTDB.QueryExceptionStatus, cue.CTDB.QueryResponseStatus);
            foreach (DBEntry e in cue.CTDB.Entries)
            {
                if (e.hasErrors) hasErrors = true;
                if (e.canRecover) canRecover = true;
                // the entry we can actually reconstruct from (has real corrections computed)
                if (rep == null && e.hasErrors && e.canRecover && e.repair != null) rep = e;
            }
            if (rep == null) { var se = cue.CTDB.SelectedEntry; if (se != null && se.repair != null && se.hasErrors) rep = se; }

            // CTDB can contain multiple legitimate pressings. If any entry is an exact match,
            // differing alternatives are not damage in this source and must not enable Repair.
            if (ctConf > 0)
            {
                hasErrors = false;
                canRecover = false;
                rep = null;
            }
        }
        // if this walk throws, CanRepair would silently never enable - repairable damage unreported
        catch (Exception ex) { log.Warn("verify", "CTDB entries walk failed (repair state may be understated): " + ex.GetType().Name); }

        // Pull the real Reed-Solomon repair detail off the chosen entry: how many samples parity can
        // reconstruct, the parity depth, and the exact damaged-sector map (downsampled for drawing).
        int repSamples = 0, repSectors = 0, repTotal = 0, repNpar = 0;
        int repWorstStripe = 0, repStripeCapacity = 0;
        double[] repMap = System.Array.Empty<double>();
        string repRanges = "";
        if (rep != null)
        {
            try
            {
                var fx = rep.repair;
                repSamples = fx.CorrectableErrors;
                repNpar = rep.Npar;
                repWorstStripe = fx.WorstStripeErrors;
                repStripeCapacity = fx.StripeCapacity;
                // Two different depths meet here and have been read as one. rep.Npar is the
                // entry's own parity depth from the first lookup; the fix ran at whatever
                // depth the escalation (4, 8, 16) actually recovered at, which is
                // StripeCapacity * 2 (CDRepair.cs:182). A "worst stripe 4 of 4" therefore
                // does NOT mean the disc sat at the limit of what parity could do, unless
                // the fetched depth already equals the entry's. Recorded so any repair
                // settles that on its own; see needs-verification entry 13 in the Linux
                // repository and findings F-19/F-20.
                log.Info(
                    "verify",
                    $"ctdb parity: entry npar={rep.Npar}, fix ran at npar={fx.StripeCapacity * 2}, " +
                    $"worst stripe {fx.WorstStripeErrors}/{fx.StripeCapacity}" +
                    (fx.StripeCapacity * 2 < rep.Npar
                        ? " (deeper parity was still available)"
                        : " (this was the entry's full depth)"));
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
            catch (Exception ex) { log.Warn("verify", "repair detail extraction failed: " + ex.GetType().Name); }
        }

        VerifyTrackResult[] tracks = new VerifyTrackResult[cue.TrackCount];
        bool trackEvidenceIncomplete = false;
        for (int track = 0; track < tracks.Length; track++)
        {
            int trackArConfidence = 0;
            int trackArTotal = 0;
            int trackCtdbConfidence = 0;
            string crc32 = "";
            try
            {
                trackArConfidence = (int)cue.ArVerify.Confidence(track);
                trackArTotal = (int)cue.ArVerify.Total(track);
                trackCtdbConfidence = cue.CTDB.GetConfidence(track);
                crc32 = cue.ArVerify.CRC32(track + 1).ToString("X8");
            }
            catch
            {
                trackEvidenceIncomplete = true;
            }

            string title = "";
            string artist = "";
            if (cue.Metadata?.Tracks != null && track < cue.Metadata.Tracks.Count)
            {
                title = cue.Metadata.Tracks[track].Title ?? "";
                artist = cue.Metadata.Tracks[track].Artist ?? "";
            }
            tracks[track] = new VerifyTrackResult
            {
                Number = track + 1,
                Title = title,
                Artist = artist,
                Crc32 = crc32,
                ArConfidence = trackArConfidence,
                ArTotal = trackArTotal,
                CtdbConfidence = trackCtdbConfidence
            };
        }
        if (trackEvidenceIncomplete)
            log.Warn("verify", "one or more per-track evidence fields could not be read");

        var metadata = cue.Metadata;

        return new VerifyFilesResult
        {
            Ok = ok,
            Error = error,
            Status = status,
            Artist = metadata?.Artist ?? "",
            Album = metadata?.Title ?? "",
            Year = metadata?.Year ?? "",
            Genre = metadata?.Genre ?? "",
            Label = metadata?.Label ?? "",
            CatalogNumber = metadata?.LabelNo ?? "",
            Barcode = metadata?.Barcode ?? "",
            Country = metadata?.Country ?? "",
            ReleaseDate = metadata?.ReleaseDate ?? "",
            DiscNumber = metadata?.DiscNumber ?? "",
            TotalDiscs = metadata?.TotalDiscs ?? "",
            DiscName = metadata?.DiscName ?? "",
            TocId = cue.TOC.AudioTracks > 0 ? cue.TOC.TOCID : "",
            DurationSeconds = cue.TOC.AudioLength / 75,
            TrackCount = cue.TrackCount,
            Tracks = tracks,
            ArConfidence = arConf,
            ArTotal = arTotal,
            CtdbConfidence = ctConf,
            CtdbTotal = ctTotal,
            Accurate = arConf > 0,
            HasErrors = hasErrors,
            CanRecover = canRecover,
            ArLookupFailed = arFailed,
            CtdbLookupFailed = ctdbFailed,
            Source = path,
            RepairSamples = repSamples,
            RepairSectors = repSectors,
            RepairTotalSectors = repTotal,
            RepairNpar = repNpar,
            RepairWorstStripeErrors = repWorstStripe,
            RepairStripeCapacity = repStripeCapacity,
            RepairSectorMap = repMap,
            RepairRanges = repRanges,
            // "Applied" is the explicit engine-state proof (CTDB-fix branch, processed encode).
            // Repair detail may be cleared when an exact CTDB match suppresses alternate-pressing
            // errors, so it must not erase that already-observed execution fact.
            RepairApplied = applied
        };
    }

    /// <summary>
    /// Both databases report a miss the same way: ProtocolError plus HTTP 404. Every other
    /// non-success status is the lookup itself failing, which is a different fact and must
    /// not be shown as "not in database".
    ///
    /// Internal rather than private so the rip path asks the same question the same way.
    /// Two definitions of "the lookup failed" would drift.
    /// </summary>
    internal static bool LookupFailed(
        System.Net.WebExceptionStatus exceptionStatus,
        System.Net.HttpStatusCode responseStatus)
        => exceptionStatus != System.Net.WebExceptionStatus.Success
            && !(exceptionStatus == System.Net.WebExceptionStatus.ProtocolError
                && responseStatus == System.Net.HttpStatusCode.NotFound);

    private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
