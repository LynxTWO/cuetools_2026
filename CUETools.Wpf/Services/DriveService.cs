using System;
using System.Collections.Generic;
using CUETools.CDImage;
using CUETools.Ripper;
using CUETools.Ripper.SCSI;
using CUETools.Wpf.Models;

namespace CUETools.Wpf.Services;

/// <summary>
/// Real drive access via the CUETools SCSI ripper (proven to run on .NET 8 in Phase 1).
/// Blocking SCSI calls - callers marshal this onto a background thread.
/// </summary>
public sealed class DriveService : IDriveService
{
    public IReadOnlyList<char> GetDrives()
    {
        try { return CDDrivesList.DrivesAvailable(); }
        catch { return Array.Empty<char>(); }
    }

    public DiscInfo? ReadDisc(char drive)
    {
        var reader = new CDDriveReader();
        try
        {
            if (!reader.Open(drive)) return null; // Open reads INQUIRY + TOC; throws if no audio disc

            CDImageLayout toc = reader.TOC;
            var tracks = new List<TrackItem>();
            for (int i = toc.FirstAudio; i < toc.FirstAudio + (int)toc.AudioTracks; i++)
            {
                CDTrack t = toc[i];
                tracks.Add(new TrackItem
                {
                    Number = (int)t.Number,
                    Title = $"Track {t.Number:00}",
                    Length = TimeSpan.FromSeconds(t.Length / 75.0)
                });
            }

            return new DiscInfo
            {
                DriveName = (reader.ARName ?? "").Trim(),
                TrackCount = toc.TrackCount,
                AudioTracks = (int)toc.AudioTracks,
                Tracks = tracks,
                TotalLength = TimeSpan.FromSeconds(toc.AudioLength / 75.0),
                TocId = toc.ToString() ?? ""
            };
        }
        catch
        {
            // no disc / not ready / data disc - the caller shows the empty state
            return null;
        }
        finally
        {
            try { reader.Dispose(); } catch { }
        }
    }
}
