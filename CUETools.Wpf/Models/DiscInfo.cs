using System;
using System.Collections.Generic;

namespace CUETools.Wpf.Models;

/// <summary>Result of reading a disc's table of contents through the ripper.</summary>
public sealed class DiscInfo
{
    public string DriveName { get; init; } = "";
    public int TrackCount { get; init; }
    public int AudioTracks { get; init; }
    public IReadOnlyList<TrackItem> Tracks { get; init; } = Array.Empty<TrackItem>();
    public TimeSpan TotalLength { get; init; }
    public string TocId { get; init; } = "";
}
