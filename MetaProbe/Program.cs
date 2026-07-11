using System;
using CUETools.CDImage;
using CUETools.CTDB;
using CUETools.Processor;
using CUETools.Ripper;
using CUETools.Ripper.SCSI;

static class Program
{
    static int Main()
    {
        Console.WriteLine("CUETools metadata probe (.NET 8)");
        var drives = CDDrivesList.DrivesAvailable();
        if (drives.Length == 0) { Console.WriteLine("No optical drive."); return 0; }
        char drive = drives[0];
        Console.WriteLine($"Drive {drive}:");

        CUEConfig config;
        try { config = new CUEConfig(); }
        catch (Exception ex) { Console.WriteLine("CUEConfig() failed: " + ex); return 1; }

        var reader = new CDDriveReader();
        try
        {
            if (!reader.Open(drive)) { Console.WriteLine("Open returned false."); return 0; }

            var cue = new CUESheet(config);
            cue.OpenCD(reader);
            Console.WriteLine($"TOC: {cue.TOC.AudioTracks} audio tracks, disc id {cue.TOC.TOCID}");
            Console.WriteLine("Looking up metadata (CTDB, then freedb)...");

            var releases = cue.LookupAlbumInfo(false, false, true, CTDBMetadataSearch.Extensive);
            Console.WriteLine($"\n{releases.Count} release match(es):");
            foreach (var r in releases)
            {
                var e = (CUEMetadataEntry)r;
                Console.WriteLine($"  [{e.ImageKey,-6}] {e}");
            }

            if (releases.Count > 0)
            {
                var top = (CUEMetadataEntry)releases[0];
                cue.CopyMetadata(top.metadata);
                Console.WriteLine();
                Console.WriteLine($"Applied top match: {cue.Metadata.Artist} - {cue.Metadata.Title}  ({cue.Metadata.Year})");
                var tracks = cue.Metadata.Tracks;
                for (int i = 0; i < tracks.Count; i++)
                    Console.WriteLine($"  {i + 1:00}  {tracks[i].Title}");
                Console.WriteLine("\nSUCCESS - real album + track names came back from the database.");
            }
            else
            {
                Console.WriteLine("\nNo metadata match (disc not in CTDB/freedb). The read path still works; titles stay generic.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAILED: " + ex.GetType().Name + ": " + ex.Message);
            return 2;
        }
        finally
        {
            try { reader.Close(); } catch { }
        }
        return 0;
    }
}
