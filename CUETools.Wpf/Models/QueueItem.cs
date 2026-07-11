using CUETools.Wpf.Mvvm;

namespace CUETools.Wpf.Models;

/// <summary>One job in the batch queue: a source path plus what to do with it. Observable so the
/// list updates live as each item runs.</summary>
public sealed class QueueItem : ViewModelBase
{
    public string Source { get; init; } = "";
    public string Action { get; init; } = "Verify";   // "Verify" | "Convert"
    public string Format { get; init; } = "";           // for Convert

    private string _status = "Pending";
    public string Status { get => _status; set => Set(ref _status, value); }

    private string _result = "";
    public string Result { get => _result; set => Set(ref _result, value); }

    private bool _running;
    public bool Running { get => _running; set => Set(ref _running, value); }

    public string Display => System.IO.Path.GetFileName(Source.TrimEnd('\\', '/'));
}
