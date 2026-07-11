using System;
using System.IO;
using CUETools.AccurateRip;
using CUETools.CDImage;
using CUETools.CTDB;
using CUETools.Processor;
using CUETools.Ripper;
using CUETools.Ripper.SCSI;

static class Program
{
    static int Main()
    {
        Console.WriteLine("CUETools verify-pipeline probe (.NET 8)  -  Action = Verify (no files written)");
        var drives = CDDrivesList.DrivesAvailable();
        if (drives.Length == 0) { Console.WriteLine("No optical drive."); return 0; }
        char drive = drives[0];

        var config = new CUEConfig();
        config.createEACLOG = false; // the detailed EAC log assumes secure-mode per-sector data; use the plain log for this burst probe
        var reader = new CDDriveReader();
        try
        {
            if (!reader.Open(drive)) { Console.WriteLine("No disc."); return 0; }

            // correct read offset from the AccurateRip drive DB (so a match is possible); burst for speed
            int offset = 0;
            try { AccurateRipVerify.FindDriveReadOffset(reader.ARName, out offset); } catch { }
            reader.DriveOffset = offset;
            reader.CorrectionQuality = 0; // burst = single pass, fastest; secure would re-read

            var cue = new CUESheet(config);
            cue.OpenCD(reader);
            try { var rel = cue.LookupAlbumInfo(false, false, true, CTDBMetadataSearch.Fast); if (rel.Count > 0) cue.CopyMetadata(((CUEMetadataEntry)rel[0]).metadata); } catch { }
            Console.WriteLine($"Disc     : {cue.Metadata.Artist} - {cue.Metadata.Title}  ({cue.TOC.AudioTracks} tracks, offset {offset})");

            cue.UseCUEToolsDB("RipProbe " + CUESheet.CUEToolsVersion, reader.ARName, false, CTDBMetadataSearch.Fast);
            cue.UseAccurateRip();

            cue.ArTestVerify = null; // mirror the ripper's non-test path
            cue.OutputStyle = CUEStyle.GapsAppended;
            cue.Action = CUEAction.Verify;
            string tmp = Path.Combine(Path.GetTempPath(), "cueverify", "verify.cue");
            cue.GenerateFilenames(AudioEncoderType.Lossless, "flac", tmp);

            int lastP = -1;
            reader.ReadProgress += (s, e) =>
            {
                if (e.PassEnd <= e.PassStart) return;
                int p = (int)(100.0 * (e.Position - e.PassStart) / (e.PassEnd - e.PassStart));
                if (p != lastP && p % 4 == 0)
                {
                    Console.Write($"\r  reading track region {p,3}%   errors so far {e.ErrorsCount}     ");
                    Console.Out.Flush();
                    lastP = p;
                }
            };

            Console.WriteLine("Running Go() - reading the whole disc and checking AccurateRip...");
            var start = DateTime.UtcNow;
            string status = cue.Go();
            var secs = (DateTime.UtcNow - start).TotalSeconds;

            Console.WriteLine();
            Console.WriteLine($"\nGo() completed in {secs:F0}s. Status: {status}");
            var ar = cue.ArVerify;
            Console.WriteLine($"AccurateRip: worst confidence {ar.WorstConfidence()} / {ar.WorstTotal()}");
            Console.WriteLine($"CTDB       : confidence {cue.CTDB.Confidence} / {cue.CTDB.Total}");
            Console.WriteLine("\nSUCCESS - the real CUESheet verify pipeline ran on .NET 8 end to end.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("\nFAILED: " + ex);
            return 2;
        }
        finally { try { reader.Close(); } catch { } }
        return 0;
    }
}
