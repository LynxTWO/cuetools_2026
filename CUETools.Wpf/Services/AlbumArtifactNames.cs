using System;
using System.IO;
using CUETools.Processor;

namespace CUETools.Wpf.Services;

/// <summary>
/// Gives human-facing album sidecars a portable identity that survives being copied away from
/// their album folder. Machine-owned transaction and verification marker names remain stable.
/// </summary>
public static class AlbumArtifactNames
{
    internal const int MaximumStemLength = 180;

    public static string CreateStem(
        CUEMetadata? metadata,
        Func<string, string> cleanse,
        string fallback = "")
    {
        if (cleanse == null)
            throw new ArgumentNullException(nameof(cleanse));

        string artist = metadata?.Artist ?? "";
        string title = metadata?.Title ?? "";
        string stem =
            string.IsNullOrWhiteSpace(artist) && string.IsNullOrWhiteSpace(title)
                ? (string.IsNullOrWhiteSpace(fallback) ? "Unknown Album" : fallback)
                : (artist + " - " + title).Trim(' ', '-');

        if (!string.IsNullOrWhiteSpace(metadata?.Year))
            stem += " (" + metadata.Year.Trim() + ")";
        if (!string.IsNullOrWhiteSpace(metadata?.DiscNumberAndName))
            stem += " (Disc " + metadata.DiscNumberAndName.Trim() + ")";

        stem = cleanse(stem) ?? "";
        foreach (char invalid in Path.GetInvalidFileNameChars())
            stem = stem.Replace(invalid, '_');
        stem = stem.Trim().TrimEnd('.');
        if (stem.Length > MaximumStemLength)
            stem = stem.Substring(0, MaximumStemLength).TrimEnd(' ', '.');
        return string.IsNullOrWhiteSpace(stem) ? "Unknown Album" : stem;
    }

    public static string CueFileName(string stem) => RequireStem(stem) + ".cue";

    public static string TestCopyLogFileName(string stem) =>
        RequireStem(stem) + " - Test & Copy.log";

    private static string RequireStem(string stem) =>
        string.IsNullOrWhiteSpace(stem) ? "Unknown Album" : stem;
}
