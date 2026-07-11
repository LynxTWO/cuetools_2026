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
    public RipReport? Current { get; private set; }
    public event EventHandler? Changed;

    public void Publish(RipReport report)
    {
        Current = report;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
