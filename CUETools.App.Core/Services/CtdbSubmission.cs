using System;
using CUETools.CTDB;
using CUETools.Processor;

namespace CUETools.Wpf.Services;

/// <summary>
/// Why a disc may not be submitted to the CUETools Database. Ineligible is not an error:
/// it is the normal answer for most discs, and nothing is uploaded for any of these.
/// </summary>
public enum CtdbSubmissionBlock
{
    /// <summary>The disc may be submitted, once the user consents.</summary>
    None,

    /// <summary>The run did not produce a usable result.</summary>
    RunIncomplete,

    /// <summary>
    /// The lookup never completed, so the database's own view of this disc is unknown.
    /// Submitting here risks duplicating an entry the service already holds.
    /// </summary>
    LookupFailed,

    /// <summary>
    /// D-070: the read carried windows that exhausted every reread, so the audio is
    /// repeatable rather than proven clean.
    /// </summary>
    UnrecoverableWindows,

    /// <summary>D-070: a salvage capture proves the drive is stable, never the disc.</summary>
    Salvaged,

    /// <summary>
    /// D-070: a Test and Copy whose confirmation failed. The staged copy is kept for the
    /// user, but it is not evidence of a good read.
    /// </summary>
    Held,

    /// <summary>The user answered no, and asked to be remembered.</summary>
    DeclinedPreviously
}

/// <summary>What the user said when asked to submit.</summary>
public sealed class CtdbSubmissionConsent
{
    public bool Submit { get; init; }

    /// <summary>Remember this answer and stop asking (the classic heads' checkbox).</summary>
    public bool Remember { get; init; }
}

/// <summary>
/// Asks the user whether to submit. A head that does not implement this can never submit,
/// which is the intended default: silence means no upload.
/// </summary>
public interface ICtdbSubmissionPrompt
{
    CtdbSubmissionConsent Ask(string album);
}

/// <summary>
/// The facts a submission decision needs, lifted off a verify or rip result so the policy
/// stays testable without a database, a disc, or a network.
/// </summary>
public sealed class CtdbSubmissionCandidate
{
    public bool RunCompleted { get; init; }
    public bool LookupFailed { get; init; }

    /// <summary>Recovery windows that exhausted every reread (rips only; zero for a verify).</summary>
    public int FailedWindows { get; init; }

    public bool Salvaged { get; init; }
    public bool Held { get; init; }
    public string Album { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Barcode { get; init; } = "";

    /// <summary>The album's worst per-track AccurateRip confidence, sent as the submission's own.</summary>
    public int Confidence { get; init; }
}

public sealed class CtdbSubmissionDecision
{
    public required CtdbSubmissionBlock Block { get; init; }
    public bool Eligible => Block == CtdbSubmissionBlock.None;

    /// <summary>True when the user must be asked before anything is uploaded.</summary>
    public bool NeedsPrompt { get; init; }
}

/// <summary>
/// The submission rules, with no I/O of any kind.
///
/// D-069: the first eligible disc raises one consent prompt; the answer is remembered in
/// <see cref="CUEConfigAdvanced.CTDBSubmit"/> and stops the asking via
/// <see cref="CUEConfigAdvanced.CTDBAsk"/>. Both keys already round-trip through the
/// settings store, so remembering costs no format change.
///
/// D-070: only reads without unrecoverable windows may be submitted. Salvaged captures and
/// held Test and Copy results never qualify, whatever the databases said about them.
/// </summary>
public static class CtdbSubmissionPolicy
{
    /// <summary>The quality value classic sends. D-070 keeps the constant and gates who may send it.</summary>
    public const int CleanReadQuality = 100;

    public static CtdbSubmissionDecision Decide(CtdbSubmissionCandidate candidate, CUEConfigAdvanced advanced)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(advanced);

        if (!candidate.RunCompleted)
            return new CtdbSubmissionDecision { Block = CtdbSubmissionBlock.RunIncomplete };
        if (candidate.LookupFailed)
            return new CtdbSubmissionDecision { Block = CtdbSubmissionBlock.LookupFailed };
        if (candidate.Salvaged)
            return new CtdbSubmissionDecision { Block = CtdbSubmissionBlock.Salvaged };
        if (candidate.Held)
            return new CtdbSubmissionDecision { Block = CtdbSubmissionBlock.Held };
        if (candidate.FailedWindows > 0)
            return new CtdbSubmissionDecision { Block = CtdbSubmissionBlock.UnrecoverableWindows };

        // The remembered "no" is checked last so the reason a disc is skipped is always the
        // strongest one: a salvaged rip is ineligible whatever the user once answered.
        if (!advanced.CTDBAsk && !advanced.CTDBSubmit)
            return new CtdbSubmissionDecision { Block = CtdbSubmissionBlock.DeclinedPreviously };

        return new CtdbSubmissionDecision
        {
            Block = CtdbSubmissionBlock.None,
            NeedsPrompt = advanced.CTDBAsk
        };
    }

    /// <summary>
    /// Applies a remembered answer. Called only when the user checked "remember", so an
    /// unremembered answer leaves the prompt armed for the next disc.
    /// </summary>
    public static void Remember(CUEConfigAdvanced advanced, bool submit)
    {
        ArgumentNullException.ThrowIfNull(advanced);
        advanced.CTDBSubmit = submit;
        advanced.CTDBAsk = false;
    }
}

/// <summary>
/// Runs a submission for one disc: policy, then consent, then the upload itself.
///
/// The database object must be the live one from the run that produced the result. Its
/// parity and checksums are what get uploaded, so a submission cannot be replayed later
/// from a stored verdict.
/// </summary>
public sealed class CtdbSubmissionService
{
    private readonly CUEConfig _config;
    private readonly ICtdbSubmissionPrompt? _prompt;
    private readonly IDiagnosticLog _log;

    public CtdbSubmissionService(CUEConfig config, ICtdbSubmissionPrompt? prompt, IDiagnosticLog log)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _prompt = prompt;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Returns the block that stopped a submission, or None when one was uploaded.
    /// Never throws: a submission failure must not change a verify or rip verdict.
    /// </summary>
    public CtdbSubmissionBlock Offer(CUEToolsDB database, CtdbSubmissionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(candidate);

        CtdbSubmissionDecision decision = CtdbSubmissionPolicy.Decide(candidate, _config.advanced);
        if (!decision.Eligible)
        {
            _log.Info("ctdb", "submission skipped: " + decision.Block);
            return decision.Block;
        }

        if (decision.NeedsPrompt)
        {
            // No prompt implementation means no way to consent, so nothing is uploaded.
            if (_prompt == null)
            {
                _log.Info("ctdb", "submission skipped: no consent surface on this head");
                return CtdbSubmissionBlock.DeclinedPreviously;
            }

            CtdbSubmissionConsent consent = _prompt.Ask(candidate.Album);
            if (consent.Remember)
                CtdbSubmissionPolicy.Remember(_config.advanced, consent.Submit);
            if (!consent.Submit)
            {
                _log.Info("ctdb", "submission declined by the user");
                return CtdbSubmissionBlock.DeclinedPreviously;
            }
        }

        try
        {
            // The server echoes what it was sent, so the album and artist have to be
            // scrubbable before the response is written to a log the user may share.
            _log.Redact(candidate.Album, candidate.Artist, candidate.Barcode);
            database.Submit(
                candidate.Confidence,
                CtdbSubmissionPolicy.CleanReadQuality,
                candidate.Artist,
                candidate.Album,
                candidate.Barcode);
            _log.Info("ctdb", "submitted: " + (database.SubStatus ?? "no server message"));
        }
        catch (Exception ex)
        {
            _log.Warn("ctdb", "submission failed, verdict unchanged: " + ex.GetType().Name);
        }

        return CtdbSubmissionBlock.None;
    }
}
