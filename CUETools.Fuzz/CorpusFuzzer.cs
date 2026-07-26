using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CUETools.Codecs;
using CUETools.Processor;

namespace CUETools.Fuzz;

// Deterministic parser corpus lanes. These do not claim coverage-guided fuzzing: each lane replays
// checked-in valid fixtures plus fixed corruptions, asserts invariants on every accepted result,
// and permits only the parser's documented/current rejection exception types for corrupt inputs.
internal static class CorpusFuzzer
{
    private static readonly string CorpusRoot = Path.Combine(AppContext.BaseDirectory, "Corpus");

    internal static void Run()
    {
        RunCueCorpus();
        RunFlacCorpus();
        RunAlacCorpus();
        RunArchiveCorpus();
        RunTagCorpus();
    }

    internal static int RunChild(string codec, string path)
    {
        bool accepted;
        string? detail;
        switch (codec)
        {
            case "flac":
                accepted = TryDecodeFlac(path, out detail);
                break;
            case "alac":
                accepted = TryDecodeAlac(path, out detail);
                break;
            default:
                Console.Error.WriteLine($"Unknown corpus child codec '{codec}'.");
                return 1;
        }

        if (accepted)
            return 0;
        if (IsExpectedCodecRejection(detail))
            return 10;
        Console.Error.WriteLine(detail);
        return 1;
    }

    private static void RunCueCorpus()
    {
        string cuePath = RequireCorpusFile("Cue", "Amarok.cue");
        bool valid = false;
        try
        {
            var sheet = new CUESheet(new CUEConfig());
            sheet.Open(cuePath);
            valid = sheet.TOC.TrackCount == 1
                && sheet.TOC.AudioTracks == 1
                && sheet.TOC[1].Start == 12
                && sheet.TOC[1].Length > 0;
        }
        catch (Exception ex)
        {
            Program.Report("CUE corpus", false, $"valid fixture threw {ex.GetType().Name}: {ex.Message}");
            return;
        }

        int rejected = 0;
        string tempRoot = CreateTempDirectory("cue");
        try
        {
            string noTracks = Path.Combine(tempRoot, "no-tracks.cue");
            File.WriteAllText(noTracks, "REM deterministic malformed corpus\n", new UTF8Encoding(false));
            if (ExpectExactRejection(
                () => new CUESheet(new CUEConfig()).Open(noTracks),
                ex => ex.GetType() == typeof(Exception) && ex.Message == "File must contain at least one audio track.",
                out string? noTracksDetail))
                rejected++;
            else
                Console.WriteLine($"    CUE no-tracks result: {noTracksDetail}");

            string missingAudio = Path.Combine(tempRoot, "missing-audio.cue");
            File.WriteAllText(
                missingAudio,
                "FILE \"absent.dummy\" WAVE\n  TRACK 01 AUDIO\n    INDEX 01 00:00:00\n",
                new UTF8Encoding(false));
            if (ExpectExactRejection(
                () => new CUESheet(new CUEConfig()).Open(missingAudio),
                ex => ex.GetType() == typeof(Exception)
                    && (ex.Message.StartsWith("Unable to locate file ", StringComparison.Ordinal)
                        || ex.Message == "unable to locate the audio files"),
                out string? missingAudioDetail))
                rejected++;
            else
                Console.WriteLine($"    CUE missing-audio result: {missingAudioDetail}");
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }

        Program.Report("CUE corpus", valid && rejected == 2, $"1 valid fixture with TOC invariants; {rejected}/2 malformed fixtures rejected as expected");
    }

    private static void RunFlacCorpus()
    {
        string[] fixtures = { RequireCorpusFile("Codecs", "test.flac") };
        int accepted = 0;
        int rejectedCorruptions = 0;
        int rejectedShortMetadataRegression = 0;
        int unexpected = 0;

        foreach (string fixture in fixtures)
        {
            if (TryDecodeFlac(fixture, out string? detail))
                accepted++;
            else
            {
                unexpected++;
                Console.WriteLine($"    FLAC valid fixture failure: {Path.GetFileName(fixture)}: {detail}");
            }
        }

        foreach (byte[] corruption in FixedCorruptions(File.ReadAllBytes(fixtures[0])))
        {
            string path = WriteTemporaryFile("flac", ".flac", corruption);
            try
            {
                int result = RunCorruptionChild("flac", path, out string? detail);
                if (result == 0)
                    accepted++; // A fixed mutation can still be a structurally valid FLAC.
                else if (result == 10)
                    rejectedCorruptions++;
                else
                {
                    unexpected++;
                    Console.WriteLine($"    unexpected FLAC corruption result: {detail}");
                }
            }
            finally { File.Delete(path); }
        }

        // This exact prefix previously made decode_metadata spin forever after the declared
        // 34-byte STREAMINFO block reached EOF. Keep it separate from the generic mutations so a
        // future refactor cannot accidentally drop the nontermination regression.
        byte[] shortMetadata = File.ReadAllBytes(fixtures[0]).Take(16).ToArray();
        string shortMetadataPath = WriteTemporaryFile("flac-short-metadata", ".flac", shortMetadata);
        try
        {
            int result = RunCorruptionChild("flac", shortMetadataPath, out string? detail);
            if (result == 10)
                rejectedShortMetadataRegression++;
            else
            {
                unexpected++;
                Console.WriteLine($"    FLAC 16-byte regression result: {detail}");
            }
        }
        finally { File.Delete(shortMetadataPath); }

        Program.Report(
            "FLAC corpus",
            unexpected == 0
                && accepted >= fixtures.Length
                && rejectedShortMetadataRegression == 1,
            $"{accepted} accepted with full-decode invariants; {rejectedCorruptions} fixed corruptions rejected; " +
            $"{rejectedShortMetadataRegression}/1 short-metadata regression rejected within 5 seconds; {unexpected} unexpected");
    }

    private static void RunAlacCorpus()
    {
        string fixture = RequireCorpusFile("Codecs", "alac.m4a");
        int accepted = 0;
        int rejectedCorruptions = 0;
        int unexpected = 0;

        if (TryDecodeAlac(fixture, out string? validDetail))
            accepted++;
        else
        {
            unexpected++;
            Console.WriteLine($"    ALAC valid fixture failure: {validDetail}");
        }

        foreach (byte[] corruption in FixedCorruptions(File.ReadAllBytes(fixture)))
        {
            string path = WriteTemporaryFile("alac", ".m4a", corruption);
            try
            {
                int result = RunCorruptionChild("alac", path, out string? detail);
                if (result == 0)
                    accepted++;
                else if (result == 10)
                    rejectedCorruptions++;
                else
                {
                    unexpected++;
                    Console.WriteLine($"    unexpected ALAC corruption result: {detail}");
                }
            }
            finally { File.Delete(path); }
        }

        Program.Report("ALAC corpus", unexpected == 0 && accepted >= 1, $"{accepted} accepted with full-decode invariants; {rejectedCorruptions} corruptions rejected; {unexpected} unexpected");
    }

    private static void RunArchiveCorpus()
    {
        string zipPath = WriteTemporaryFile("archive", ".zip", Array.Empty<byte>());
        int rejected = 0;
        try
        {
            byte[] expected = Encoding.UTF8.GetBytes("deterministic archive payload\n");
            using (var file = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                var entry = archive.CreateEntry("album/test.txt", CompressionLevel.Optimal);
                entry.LastWriteTime = new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);
                using Stream output = entry.Open();
                output.Write(expected, 0, expected.Length);
            }

            bool valid = false;
            var provider = new CUETools.Compression.Zip.ZipCompressionProvider(zipPath);
            try
            {
                string[] contents = provider.Contents.ToArray();
                using Stream input = provider.Decompress("album/test.txt");
                using var copy = new MemoryStream();
                input.CopyTo(copy);
                valid = contents.SequenceEqual(new[] { "album/test.txt" })
                    && copy.ToArray().SequenceEqual(expected)
                    && input.CanSeek;
            }
            finally { provider.Close(); }

            string corruptPath = WriteTemporaryFile("archive-corrupt", ".zip", Encoding.ASCII.GetBytes("not a zip"));
            try
            {
                if (ExpectExactRejection(
                    () =>
                    {
                        var corrupt = new CUETools.Compression.Zip.ZipCompressionProvider(corruptPath);
                        try { _ = corrupt.Contents.ToArray(); }
                        finally { corrupt.Close(); }
                    },
                    ex => ex.GetType().FullName == "ICSharpCode.SharpZipLib.Zip.ZipException",
                    out _))
                    rejected++;
            }
            finally { File.Delete(corruptPath); }

            Program.Report("ZIP corpus", valid && rejected == 1, $"valid entry/content/seek invariants; {rejected}/1 corrupt fixtures rejected as expected");
        }
        catch (Exception ex)
        {
            Program.Report("ZIP corpus", false, $"unexpected {ex.GetType().Name}: {ex.Message}");
        }
        finally { File.Delete(zipPath); }
    }

    private static void RunTagCorpus()
    {
        string[] fixtures =
        {
            RequireCorpusFile("Tags", "sample.mp3"),
            RequireCorpusFile("Codecs", "test.flac"),
            RequireCorpusFile("Codecs", "alac.m4a")
        };
        int accepted = 0;
        int unexpected = 0;
        foreach (string fixture in fixtures)
        {
            try
            {
                using TagLib.File file = TagLib.File.Create(fixture);
                if (file.Tag != null && file.Properties != null && file.Properties.Duration >= TimeSpan.Zero)
                    accepted++;
                else
                    unexpected++;
            }
            catch (Exception ex)
            {
                unexpected++;
                Console.WriteLine($"    tag valid fixture failure: {Path.GetFileName(fixture)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        int rejected = 0;
        string invalidPath = WriteTemporaryFile("tag", ".bin", Encoding.ASCII.GetBytes("not a supported tagged media file"));
        try
        {
            if (ExpectExactRejection(
                () =>
                {
                    using TagLib.File _ = TagLib.File.Create(invalidPath);
                },
                ex => ex.GetType() == typeof(TagLib.UnsupportedFormatException)
                    || ex.GetType() == typeof(TagLib.CorruptFileException),
                out _))
                rejected++;
        }
        finally { File.Delete(invalidPath); }

        Program.Report("tag corpus", unexpected == 0 && accepted == fixtures.Length && rejected == 1, $"{accepted}/{fixtures.Length} valid formats accepted with invariants; {rejected}/1 unsupported fixture rejected");
    }

    private static bool TryDecodeFlac(string path, out string? detail)
    {
        CUETools.Codecs.Flake.AudioDecoder? decoder = null;
        try
        {
            decoder = new CUETools.Codecs.Flake.AudioDecoder(new CUETools.Codecs.Flake.DecoderSettings(), path);
            return ValidateDecodedAudio(decoder, out detail);
        }
        catch (Exception ex)
        {
            detail = $"REJECT:{ex.GetType().FullName}:{ex.Message}";
            return false;
        }
        finally
        {
            try { decoder?.Close(); } catch { }
        }
    }

    private static bool TryDecodeAlac(string path, out string? detail)
    {
        CUETools.Codecs.ALAC.AudioDecoder? decoder = null;
        try
        {
            decoder = new CUETools.Codecs.ALAC.AudioDecoder(new CUETools.Codecs.ALAC.DecoderSettings(), path);
            return ValidateDecodedAudio(decoder, out detail);
        }
        catch (Exception ex)
        {
            detail = $"REJECT:{ex.GetType().FullName}:{ex.Message}";
            return false;
        }
        finally
        {
            try { decoder?.Close(); } catch { }
        }
    }

    private static bool ValidateDecodedAudio(IAudioSource decoder, out string? detail)
    {
        AudioPCMConfig pcm = decoder.PCM;
        if (pcm.SampleRate <= 0 || pcm.ChannelCount <= 0 || pcm.BitsPerSample <= 0 || decoder.Length < 0)
        {
            detail = "INVARIANT:invalid PCM format or negative length";
            return false;
        }

        var buffer = new AudioBuffer(decoder, 4096);
        long samples = 0;
        int reads = 0;
        while (true)
        {
            int read = decoder.Read(buffer, 4096);
            if (read < 0 || read > 4096)
            {
                detail = $"INVARIANT:read returned {read}";
                return false;
            }
            if (read == 0) break;
            samples += read;
            reads++;
            if (samples > decoder.Length || reads > 1_000_000)
            {
                detail = "INVARIANT:decoder exceeded declared length or termination bound";
                return false;
            }
        }

        if (samples != decoder.Length)
        {
            detail = $"INVARIANT:decoded {samples} samples, declared {decoder.Length}";
            return false;
        }
        detail = null;
        return true;
    }

    private static IEnumerable<byte[]> FixedCorruptions(byte[] source)
    {
        yield return Array.Empty<byte>();
        yield return source.Take(Math.Max(1, source.Length / 2)).ToArray();
        if (source.Length > 0)
        {
            byte[] badMagic = (byte[])source.Clone();
            badMagic[0] ^= 0xff;
            yield return badMagic;
        }
    }

    private static bool IsExpectedCodecRejection(string? detail)
    {
        if (detail == null || !detail.StartsWith("REJECT:", StringComparison.Ordinal))
            return false;
        string type = detail.Substring(7).Split(':')[0];
        return type == typeof(Exception).FullName
            || type == typeof(EndOfStreamException).FullName
            || type == typeof(InvalidDataException).FullName
            || type == typeof(IOException).FullName
            || detail.StartsWith(
                "REJECT:System.IndexOutOfRangeException:BitReader.read_rice_block: read past end of buffer (corrupt or truncated stream)",
                StringComparison.Ordinal);
    }

    private static int RunCorruptionChild(string codec, string path, out string? detail)
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            detail = "could not resolve the fuzz harness executable";
            return 1;
        }

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--corpus-child");
        start.ArgumentList.Add(codec);
        start.ArgumentList.Add(path);

        using var process = Process.Start(start);
        if (process == null)
        {
            detail = "failed to launch isolated corpus child";
            return 1;
        }

        if (!process.WaitForExit(5000))
        {
            string terminationDetail = "";
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                terminationDetail = "; process-tree kill failed: " + ex.GetType().Name;
            }

            try
            {
                if (!process.WaitForExit(5000))
                    terminationDetail += "; child did not exit within the 5 second kill deadline";
            }
            catch (Exception ex)
            {
                terminationDetail += "; child termination wait failed: " + ex.GetType().Name;
            }

            detail = "isolated decoder exceeded the 5 second time/memory-safety boundary" +
                terminationDetail;
            return 1;
        }

        string output = (process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()).Trim();
        detail = string.IsNullOrWhiteSpace(output) ? $"child exit {process.ExitCode}" : output;
        return process.ExitCode;
    }

    private static bool ExpectExactRejection(Action action, Func<Exception, bool> predicate, out string? detail)
    {
        try
        {
            action();
            detail = "unexpected acceptance";
            return false;
        }
        catch (Exception ex)
        {
            bool expected = predicate(ex);
            detail = expected ? null : $"unexpected {ex.GetType().FullName}: {ex.Message}";
            return expected;
        }
    }

    private static string RequireCorpusFile(params string[] parts)
    {
        string path = parts.Aggregate(CorpusRoot, Path.Combine);
        if (!File.Exists(path))
            throw new FileNotFoundException("Required deterministic fuzz corpus file is missing.", path);
        return path;
    }

    private static string CreateTempDirectory(string lane)
    {
        string path = Path.Combine(Path.GetTempPath(), $"cuetools-fuzz-{lane}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string WriteTemporaryFile(string lane, string extension, byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"cuetools-fuzz-{lane}-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
