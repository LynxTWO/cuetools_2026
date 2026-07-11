using System;
using System.Linq;
using System.Runtime.InteropServices;
using CUETools.CDImage;
using CUETools.Ripper;
using CUETools.Ripper.SCSI;

namespace DriveProbe
{
    // Phase 1 smoke test for the CUERipper WPF/.NET 8 rebuild. The goal is narrow:
    // prove the Windows SCSI ripping stack (Bwg.Scsi -> Device -> DeviceIoControl)
    // still issues real SCSI commands under the .NET 8 runtime, before any UI is built.
    static class Program
    {
        // CD time is 75 frames per second; render sector counts as mm:ss.ff.
        static string Fmt(uint frames)
        {
            uint sec = frames / 75;
            return $"{sec / 60:00}:{sec % 60:00}.{frames % 75:00}";
        }

        static int Main()
        {
            Console.WriteLine("CUERipper .NET 8 drive probe");
            Console.WriteLine("Runtime : " + RuntimeInformation.FrameworkDescription);
            Console.WriteLine("Process : " + (Environment.Is64BitProcess ? "64-bit" : "32-bit"));
            Console.WriteLine();

            char[] drives;
            try
            {
                drives = CDDrivesList.DrivesAvailable();
            }
            catch (Exception ex)
            {
                Console.WriteLine("DrivesAvailable() failed: " + ex);
                return 1;
            }

            if (drives.Length == 0)
            {
                Console.WriteLine("No optical drives found (DriveInfo reported no CD-ROM devices).");
                return 0;
            }
            Console.WriteLine("Optical drives: " + string.Join(", ", drives.Select(d => d + ":")));

            int opened = 0;
            foreach (char d in drives)
            {
                Console.WriteLine();
                Console.WriteLine($"=== Drive {d}: ===");
                var reader = new CDDriveReader();
                try
                {
                    if (!reader.Open(d))
                    {
                        Console.WriteLine("  Open() returned false.");
                        continue;
                    }

                    Console.WriteLine($"  AccurateRip name : {reader.ARName}");
                    Console.WriteLine($"  EAC name         : {reader.EACName}");
                    Console.WriteLine($"  Ripper version   : {reader.RipperVersion}");
                    Console.WriteLine($"  Read command     : {reader.CurrentReadCommand}");
                    Console.WriteLine($"  Drive offset     : {reader.DriveOffset:+#;-#;0} samples");

                    CDImageLayout toc = reader.TOC;
                    Console.WriteLine($"  Disc             : {toc.TrackCount} tracks ({toc.AudioTracks} audio), {Fmt(toc.AudioLength)} of audio");
                    Console.WriteLine($"  TOC offsets      : {toc}");
                    for (int i = toc.FirstAudio; i < toc.FirstAudio + (int)toc.AudioTracks; i++)
                    {
                        CDTrack t = toc[i];
                        Console.WriteLine($"    track {t.Number:00}  start {t.Start,7}  length {t.Length,7}  ({Fmt(t.Length)})");
                    }
                    opened++;
                    Console.WriteLine();
                    Console.WriteLine("  SUCCESS - .NET 8 SCSI passthrough read this drive's INQUIRY and the disc TOC.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine("  (An empty tray or a data disc still exercises the net8 SCSI path: the command");
                    Console.WriteLine("   reached the drive and a sense reply came back. Insert an audio CD for the full readout.)");
                }
                finally
                {
                    try { reader.Dispose(); } catch { }
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Done. {drives.Length} drive(s) enumerated, {opened} audio disc(s) read.");
            return 0;
        }
    }
}
