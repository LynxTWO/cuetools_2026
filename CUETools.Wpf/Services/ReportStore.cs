using System;
using CUETools.Wpf.Models;

namespace CUETools.Wpf.Services;

/// <summary>Holds the most recent job's <see cref="RipReport"/> so the Rip page can publish
/// it and the Report page can render it. A singleton in DI; raises <see cref="Changed"/> on
/// each new report.</summary>
public interface IReportStore
{
    RipReport? Current { get; }
    event EventHandler? Changed;
    void Publish(RipReport report);
}

public sealed class ReportStore : IReportStore
{
    private readonly IDiagnosticLog? _log;

    public ReportStore(IDiagnosticLog? log = null)
    {
        _log = log;
    }

    public RipReport? Current { get; private set; }
    public event EventHandler? Changed;

    public void Publish(RipReport report)
    {
        Current = report;
        EventHandler? subscribers = Changed;
        if (subscribers == null) return;

        // A report observer is presentation-only. One broken page must not erase a completed
        // verification result or prevent the remaining observers from receiving it.
        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            try { ((EventHandler)subscriber)(this, EventArgs.Empty); }
            catch (Exception ex)
            {
                // Subscriber exceptions may contain presentation text. Keep diagnostics
                // structural so album/artist/track data cannot escape through an observer.
                _log?.Warn("report", "report observer failed after publication: " +
                    ex.GetType().Name);
            }
        }
    }
}
