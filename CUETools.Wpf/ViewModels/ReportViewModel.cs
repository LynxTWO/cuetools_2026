using CUETools.Wpf.Models;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Report page. Renders the most recent job (from <see cref="IReportStore"/>) as a
/// receipt: the outcome, its independent-read and database assurance sources, the full
/// rip-log text, and a reproducible SHA-256 checksum over the displayed canonical form.
/// </summary>
public sealed class ReportViewModel : PageViewModel
{
    private readonly IReportStore _store;

    public ReportViewModel(IReportStore store)
    {
        Title = "Report";
        Group = "Session";
        Subtitle = "The per-job accuracy report, with a reproducible SHA-256 checksum.";
        _store = store;
        _store.Changed += (_, __) => Refresh();
        Refresh();
    }

    private bool _hasReport;
    public bool HasReport { get => _hasReport; private set => Set(ref _hasReport, value); }

    private bool _confirmed;
    public bool Confirmed { get => _confirmed; private set => Set(ref _confirmed, value); }

    private string _headline = "";
    public string Headline { get => _headline; private set => Set(ref _headline, value); }

    private string _album = "";
    public string Album { get => _album; private set => Set(ref _album, value); }

    private string _artist = "";
    public string Artist { get => _artist; private set => Set(ref _artist, value); }

    private string _subhead = "";
    public string Subhead { get => _subhead; private set => Set(ref _subhead, value); }

    private string _arText = "";
    public string ArText { get => _arText; private set => Set(ref _arText, value); }

    private string _ctdbText = "";
    public string CtdbText { get => _ctdbText; private set => Set(ref _ctdbText, value); }

    private string _logBody = "";
    public string LogBody { get => _logBody; private set => Set(ref _logBody, value); }

    private string _digestFooter = "";
    public string DigestFooter { get => _digestFooter; private set => Set(ref _digestFooter, value); }

    private string _integrityLine = "";
    public string IntegrityLine { get => _integrityLine; private set => Set(ref _integrityLine, value); }

    private void Refresh()
    {
        RipReport? r = _store.Current;
        HasReport = r != null;
        if (r == null)
        {
            Confirmed = false;
            Headline = "";
            return;
        }

        Confirmed = r.Verified;
        // Damage outranks every verification headline: the log says CONSISTENT for these jobs,
        // and the certificate must never say more than the log (R109). A salvage capture says
        // so unless a real database match verified the bytes anyway (R119).
        Headline = r.Damaged
            ? (r.Salvaged ? "Salvaged capture - damage recorded" : "Consistent - damage recorded")
            : r.DatabaseConfirmed
                ? "Accurately ripped"
                : r.Salvaged
                    ? "Salvaged capture - not verified"
                    : r.IndependentReadsVerified ? "Verified by independent reads" : "Not confirmed";
        Album = r.Album;
        Artist = string.IsNullOrWhiteSpace(r.Year) ? r.Artist : $"{r.Artist}  ({r.Year})";
        Subhead = $"{r.Mode}  .  {r.CorrectionQualityName}  .  {r.Timestamp:yyyy-MM-dd HH:mm}";

        ArText = r.Accurate ? $"confidence {r.ArConfidence}" : (r.ArTotal > 0 ? $"{r.ArConfidence} / {r.ArTotal}" : "not in database");
        CtdbText = r.CtdbConfidence > 0 ? $"confidence {r.CtdbConfidence}" : "not found";

        string body = r.BuildLogBody();
        LogBody = body;
        string digest = LogIntegrity.ComputeDigest(body);
        DigestFooter = LogIntegrity.Footer(digest);

        // Name each assurance source independently. Test & Copy agreement is real evidence but
        // must never be presented as an AccurateRip or CTDB match.
        string against = r.Accurate && r.CtdbConfidence > 0
            ? $"CTDB (conf {r.CtdbConfidence}) and AccurateRip (conf {r.ArConfidence})"
            : r.CtdbConfidence > 0 ? $"CTDB (conf {r.CtdbConfidence})"
            : r.Accurate ? $"AccurateRip (conf {r.ArConfidence})"
            : "";
        string evidence = r.IndependentReadsVerified
            ? $"verified after {r.OpticalReadsUsed} optical reads; every track agreed across "
                + $"at least {r.MinimumAgreeingReads} reads"
                + (against.Length > 0 ? $"; also confirmed against {against}" : "; not found in AR/CTDB")
            : against.Length > 0 ? $"confirmed against {against}"
            : "not independently confirmed";
        string outputEvidence = !r.OutputVerificationKnown
            ? " Final encoded-output verification: not recorded."
            : !r.LosslessOutput
                ? " Final encoded-output verification: not applicable to lossy output."
                : r.OutputVerificationPerformed
                    ? " Final encoded-output verification: "
                        + (string.IsNullOrWhiteSpace(r.OutputVerificationDetail)
                            ? "performed after metadata finalization"
                            : r.OutputVerificationDetail)
                        + "."
                    : " WARNING: final lossless output was not independently decoded and compared.";
        IntegrityLine = "Report checksum calculated over the text above (not a signature; " +
            $"compare it with a previously retained digest). Read evidence: {evidence}." +
            outputEvidence;
    }
}
