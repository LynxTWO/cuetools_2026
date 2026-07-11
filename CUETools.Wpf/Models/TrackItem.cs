using System;
using CUETools.Wpf.Mvvm;

namespace CUETools.Wpf.Models;

/// <summary>One audio track shown in the Rip track list. Editable title now; the
/// per-track rip Quality / AccurateRip / C2 columns get added when ripping (Phase 3 cont).</summary>
public sealed class TrackItem : ViewModelBase
{
    public int Number { get; init; }

    private string _title = "";
    public string Title { get => _title; set => Set(ref _title, value); }

    public TimeSpan Length { get; init; }
    public string LengthText => $"{(int)Length.TotalMinutes}:{Length.Seconds:00}";

    private bool _include = true;
    public bool Include { get => _include; set => Set(ref _include, value); }
}

/// <summary>A row in the "recently ripped" list on the eject/no-disc screen.</summary>
public sealed class RecentRip
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string When { get; init; } = "";
    public string Result { get; init; } = "";
}
