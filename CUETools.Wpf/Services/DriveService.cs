using System;
using System.Collections.Generic;
using CUETools.CDImage;
using CUETools.CTDB;
using CUETools.Processor;
using CUETools.Ripper;
using CUETools.Ripper.SCSI;
using CUETools.Wpf.Models;

namespace CUETools.Wpf.Services;

/// <summary>
/// Real drive access via the CUETools SCSI ripper (proven to run on .NET 8 in Phase 1) and
/// the CUESheet metadata lookup (CTDB / MusicBrainz / Discogs / freedb). Blocking SCSI +
/// network calls - callers marshal this onto a background thread.
/// </summary>
public sealed class DriveService : IDriveService
{
    private readonly CUEConfig _config;

    public DriveService(CUEConfig config) => _config = config;

    public IReadOnlyList<char> GetDrives()
    {
        try { return CDDrivesList.DrivesAvailable(); }
        catch { return Array.Empty<char>(); }
    }

    public DiscInfo? ReadDisc(char drive)
    {
        var reader = new CDDriveReader();
        bool opened = false;
        try
        {
            if (!reader.Open(drive)) return null; // Open reads INQUIRY + TOC; throws if no audio disc
            opened = true;

            int driveOffset = 0;
            try { CUETools.AccurateRip.AccurateRipVerify.FindDriveReadOffset(reader.ARName, out driveOffset); } catch { }

            var cue = new CUESheet(_config);
            cue.OpenCD(reader);
            CDImageLayout toc = reader.TOC;

            string album = "Unknown album", artist = "", year = "";
            var releaseNames = new List<string>();

            // metadata lookup is best-effort: keep generic names if the disc isn't found or we're offline
            try
            {
                var releases = cue.LookupAlbumInfo(false, false, true, CTDBMetadataSearch.Extensive);
                foreach (var r in releases)
                    if (r is CUEMetadataEntry e) releaseNames.Add(e.ToString());

                if (releaseNames.Count > 0 && releases[0] is CUEMetadataEntry top)
                {
                    cue.CopyMetadata(top.metadata);
                    artist = cue.Metadata?.Artist ?? "";
                    if (!string.IsNullOrEmpty(cue.Metadata?.Title)) album = cue.Metadata!.Title;
                    year = cue.Metadata?.Year ?? "";
                }
            }
            catch { /* lookup failed / offline - generic names */ }

            var metaTracks = cue.Metadata?.Tracks;
            var tracks = new List<TrackItem>();
            for (int i = toc.FirstAudio; i < toc.FirstAudio + (int)toc.AudioTracks; i++)
            {
                CDTrack t = toc[i];
                int idx = i - toc.FirstAudio;
                string title = (metaTracks != null && idx < metaTracks.Count && !string.IsNullOrEmpty(metaTracks[idx].Title))
                    ? metaTracks[idx].Title
                    : $"Track {t.Number:00}";
                tracks.Add(new TrackItem { Number = (int)t.Number, Title = title, Length = TimeSpan.FromSeconds(t.Length / 75.0) });
            }

            var info = new DiscInfo
            {
                DriveName = (reader.ARName ?? "").Trim(),
                Offset = driveOffset,
                Album = album,
                Artist = artist,
                Year = year,
                TrackCount = toc.TrackCount,
                AudioTracks = (int)toc.AudioTracks,
                Tracks = tracks,
                TotalLength = TimeSpan.FromSeconds(toc.AudioLength / 75.0),
                TocId = toc.ToString() ?? "",
                ReleaseMatches = releaseNames
            };

            cue.Close();
            opened = false;
            return info;
        }
        catch
        {
            return null; // no disc / not ready / data disc - caller shows the empty state
        }
        finally
        {
            try { if (opened) reader.Close(); else reader.Dispose(); } catch { }
        }
    }
}
