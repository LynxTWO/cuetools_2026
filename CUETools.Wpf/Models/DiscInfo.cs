using System;
using System.Collections.Generic;

namespace CUETools.Wpf.Models;

/// <summary>Result of reading a disc's TOC and looking up its metadata.</summary>
public sealed class DiscInfo
{
    public string DriveName { get; init; } = "";
    public string Album { get; init; } = "Unknown album";
    public string Artist { get; init; } = "";
    public string Year { get; init; } = "";
    public int TrackCount { get; init; }
    public int AudioTracks { get; init; }
    public IReadOnlyList<TrackItem> Tracks { get; init; } = Array.Empty<TrackItem>();
    public TimeSpan TotalLength { get; init; }
    public string TocId { get; init; } = "";
    public IReadOnlyList<string> ReleaseMatches { get; init; } = Array.Empty<string>();
}
