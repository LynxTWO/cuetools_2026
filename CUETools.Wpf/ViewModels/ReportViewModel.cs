using CUETools.Wpf.Models;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Report page. Renders the most recent job (from <see cref="IReportStore"/>) as a
/// certificate: the outcome, the AccurateRip / CTDB confidences, the full rip-log text, and
/// the model-B self-check - a SHA-256 over the log's canonical form, recomputed live so it is
/// a real integrity footer rather than a decorative string.
/// </summary>
public sealed class ReportViewModel : PageViewModel
{
    private readonly IReportStore _store;

    public ReportViewModel(IReportStore store)
    {
        Title = "Report";
        Group = "Session";
        Subtitle = "The per-job accuracy log, with a tamper-evident checksum.";
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

        Confirmed = r.Confirmed;
        Headline = r.Confirmed ? "Accurately ripped" : "Not confirmed";
        Album = r.Album;
        Artist = string.IsNullOrWhiteSpace(r.Year) ? r.Artist : $"{r.Artist}  ({r.Year})";
        Subhead = $"{r.Mode}  .  {r.CorrectionQualityName}  .  {r.Timestamp:yyyy-MM-dd HH:mm}";

        ArText = r.Accurate ? $"confidence {r.ArConfidence}" : (r.ArTotal > 0 ? $"{r.ArConfidence} / {r.ArTotal}" : "not in database");
        CtdbText = r.CtdbConfidence > 0 ? $"confidence {r.CtdbConfidence}" : "not found";

        string body = r.BuildLogBody();
        LogBody = body;
        string digest = LogIntegrity.ComputeDigest(body);
        DigestFooter = LogIntegrity.Footer(digest);

        // Honest wording: the self-check is always local; the "confirmed against" clause only
        // names a database when that database actually returned a positive confidence.
        string against = r.Accurate && r.CtdbConfidence > 0
            ? $"CTDB (conf {r.CtdbConfidence}) and AccurateRip (conf {r.ArConfidence})"
            : r.CtdbConfidence > 0 ? $"CTDB (conf {r.CtdbConfidence})"
            : r.Accurate ? $"AccurateRip (conf {r.ArConfidence})"
            : "";
        IntegrityLine = against.Length > 0
            ? $"Log integrity: self-check OK; results confirmed against {against}."
            : "Log integrity: self-check OK; results not independently confirmed (disc not in the databases).";
    }
}
