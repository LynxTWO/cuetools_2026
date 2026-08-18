using CUETools.Wpf.Mvvm;

namespace CUETools.Wpf.Models;

/// <summary>One job in the batch queue: a source path plus what to do with it. Observable so the
/// list updates live as each item runs.</summary>
public sealed class QueueItem : ViewModelBase
{
    public string Source { get; init; } = "";
    public string Action { get; init; } = "Verify";   // "Verify" | "Convert"
    public string Format { get; init; } = "";           // for Convert
    public string CodecStableId { get; init; } = "";    // exact format face + implementation

    /// <summary>Where this item's converted files go, frozen when it was added. Empty means
    /// the album's own usual location. Frozen for the same reason as CodecStableId: changing
    /// the Queue's folder must steer what is queued next, not relocate work already waiting.</summary>
    public string OutputDir { get; init; } = "";

    private string _status = "Pending";
    public string Status { get => _status; set => Set(ref _status, value); }

    private string _result = "";
    public string Result { get => _result; set => Set(ref _result, value); }

    private bool _running;
    public bool Running { get => _running; set => Set(ref _running, value); }

    public string Display => System.IO.Path.GetFileName(Source.TrimEnd('\\', '/'));
}
