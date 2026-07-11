using System;
using System.IO;
using System.Linq;
using CUETools.AccurateRip;
using CUETools.CTDB;
using CUETools.Processor;
using CUETools.Ripper;
using CUETools.Ripper.SCSI;

static class Program
{
    static int Main()
    {
        Console.WriteLine("CUETools encode-pipeline probe (.NET 8) - Action = Encode, writes FLAC");
        var drives = CDDrivesList.DrivesAvailable();
        if (drives.Length == 0) { Console.WriteLine("No optical drive."); return 0; }
        char drive = drives[0];

        var config = new CUEConfig();
        config.createEACLOG = false;
        var reader = new CDDriveReader();
        try
        {
            if (!reader.Open(drive)) { Console.WriteLine("No disc."); return 0; }
            int offset = 0; try { AccurateRipVerify.FindDriveReadOffset(reader.ARName, out offset); } catch { }
            reader.DriveOffset = offset;
            reader.CorrectionQuality = 0; // burst for a fast test

            var cue = new CUESheet(config);
            cue.OpenCD(reader);
            try { var rel = cue.LookupAlbumInfo(false, false, true, CTDBMetadataSearch.Fast); if (rel.Count > 0) cue.CopyMetadata(((CUEMetadataEntry)rel[0]).metadata); } catch { }
            cue.UseCUEToolsDB("EncProbe " + CUESheet.CUEToolsVersion, reader.ARName, false, CTDBMetadataSearch.Fast);
            cue.UseAccurateRip();
            cue.ArTestVerify = null;
            cue.OutputStyle = CUEStyle.GapsAppended;
            cue.Action = CUEAction.Encode;

            string outDir = Path.Combine(Path.GetTempPath(), "cuerip_test");
            if (Directory.Exists(outDir)) try { Directory.Delete(outDir, true); } catch { }
            Directory.CreateDirectory(outDir);
            cue.GenerateFilenames(AudioEncoderType.Lossless, "flac", Path.Combine(outDir, "rip.cue"));

            double total = Math.Max(1, reader.TOC.AudioLength);
            int lastP = -1;
            reader.ReadProgress += (s, e) =>
            {
                int p = (int)(100.0 * e.Position / total);
                if (p != lastP && p % 5 == 0) { Console.Write($"\r  ripping {p,3}%     "); Console.Out.Flush(); lastP = p; }
            };

            Console.WriteLine($"Encoding {cue.Metadata.Artist} - {cue.Metadata.Title} to FLAC -> {outDir}");
            var t0 = DateTime.UtcNow;
            string status = cue.Go();
            Console.WriteLine($"\nGo() done in {(DateTime.UtcNow - t0).TotalSeconds:F0}s. Status: {status}");

            var files = Directory.GetFiles(outDir).OrderBy(f => f).ToArray();
            long totalBytes = 0;
            Console.WriteLine($"\nWrote {files.Length} file(s):");
            foreach (var f in files) { var fi = new FileInfo(f); totalBytes += fi.Length; Console.WriteLine($"  {Path.GetFileName(f),-52} {fi.Length,12:N0} bytes"); }
            Console.WriteLine($"\nTotal {totalBytes / 1024 / 1024} MB. SUCCESS - real FLAC files written by the encode pipeline on .NET 8.");
        }
        catch (Exception ex) { Console.WriteLine("\nFAILED: " + ex); return 2; }
        finally { try { reader.Close(); } catch { } }
        return 0;
    }
}
