using System;
using CUETools.Processor;

namespace CUETools.Wpf.Services
{
    /// <summary>Builds a NamingContext for one track from the shared CUEMetadata release, using only
    /// fields the model already holds. Type/status (phase 3) are left empty so their tokens render
    /// blank. Pure - no I/O.</summary>
    public static class NamingContextMapper
    {
        public static NamingContext FromMetadata(CUEMetadata? m, int trackIndex, int totalTracks)
        {
            string trackArtist = "", trackTitle = "", isrc = "";
            if (m?.Tracks != null && trackIndex >= 0 && trackIndex < m.Tracks.Count)
            {
                trackArtist = m.Tracks[trackIndex].Artist ?? "";
                trackTitle = m.Tracks[trackIndex].Title ?? "";
                isrc = m.Tracks[trackIndex].ISRC ?? "";
            }
            string albumArtist = m?.Artist ?? "";
            return new NamingContext
            {
                AlbumArtist = albumArtist,
                Artist = string.IsNullOrWhiteSpace(trackArtist) ? albumArtist : trackArtist,
                Album = m?.Title ?? "",
                Title = trackTitle,
                Year = m?.Year ?? "",
                Genre = m?.Genre ?? "",
                DiscNumber = ParseInt(m?.DiscNumber, 1),
                TotalDiscs = ParseInt(m?.TotalDiscs, 1),
                DiscSubtitle = m?.DiscName ?? "",
                Label = m?.Label ?? "",
                Catalog = m?.LabelNo ?? "",
                Barcode = m?.Barcode ?? "",
                Country = m?.Country ?? "",
                Isrc = isrc,
                OriginalYear = Year4(m?.ReleaseDate) ?? (m?.Year ?? ""),
                TrackNumber = trackIndex + 1,
                TotalTracks = totalTracks,
                // phase 3 fills these; empty now so %releasetype%/%releasestatus% render blank
                PrimaryType = "",
                ReleaseStatus = "",
                SecondaryTypes = Array.Empty<string>(),
            };
        }

        private static int ParseInt(string? s, int dflt) =>
            int.TryParse((s ?? "").Trim(), out int v) && v > 0 ? v : dflt;

        private static string? Year4(string? date)
        {
            date = (date ?? "").Trim();
            return date.Length >= 4 && int.TryParse(date.Substring(0, 4), out _) ? date.Substring(0, 4) : null;
        }
    }
}
