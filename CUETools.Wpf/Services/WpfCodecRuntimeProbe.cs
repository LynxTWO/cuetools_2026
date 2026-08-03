using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CUETools.Codecs;
using CUETools.Processor;

namespace CUETools.Wpf.Services;

/// <summary>
/// Release-only child-process probe. It deliberately loads the managed native wrappers from the
/// WPF application root before plugin discovery, matching the layout that exposed the production
/// failure. Plugin discovery must then bind those wrappers to manifest-approved plugins/x64 bytes.
/// </summary>
internal static class WpfCodecRuntimeProbe
{
    private static readonly (string Assembly, string SettingsType, string Key)[] NativeEncoders =
    {
        ("CUETools.Codecs.libFLAC.dll", "CUETools.Codecs.libFLAC.EncoderSettings", "libFLAC"),
        ("CUETools.Codecs.libwavpack.dll", "CUETools.Codecs.libwavpack.EncoderSettings", "WavPack"),
        ("CUETools.Codecs.MACLib.dll", "CUETools.Codecs.MACLib.EncoderSettings", "MonkeyAudio"),
        ("CUETools.Codecs.libmp3lame.dll", "CUETools.Codecs.libmp3lame.VBREncoderSettings", "LAME"),
    };

    internal static int WriteReceipt(string outputPath)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            throw new DirectoryNotFoundException(
                "The codec probe output directory does not exist.");

        var lines = new List<string> { "CUETools.Wpf.CodecProbe.v1" };
        try
        {
            foreach (var native in NativeEncoders)
            {
                string assemblyPath = Path.Combine(
                    AppContext.BaseDirectory,
                    native.Assembly);
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                string loadedDirectory = Path.GetDirectoryName(
                    Path.GetFullPath(assembly.Location)) ?? "";
                string appDirectory = AppContext.BaseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                if (!string.Equals(
                        loadedDirectory.TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar),
                        appDirectory,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "A native wrapper was not root-loaded by the WPF process probe.");
            }

            // This first registry access runs manifest validation and native preloading after the
            // root wrappers above have already entered the load context.
            object[] registrations = CUEProcessorPlugins.encs.Cast<object>().ToArray();
            foreach (var native in NativeEncoders)
            {
                object settings = registrations.Single(registration =>
                    string.Equals(
                        registration.GetType().FullName,
                        native.SettingsType,
                        StringComparison.Ordinal));
                PropertyInfo versionProperty = settings.GetType().GetProperty(
                    "Version",
                    BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new MissingMemberException(native.SettingsType, "Version");
                string version = versionProperty.GetValue(settings)?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(version))
                    throw new InvalidDataException(
                        native.Key + " returned no version identity.");
                lines.Add("PASS\t" + native.Key + "\t" + version.Trim());
            }

            ProbeRealEncodes(parent);

            Type hdcdType = CUEProcessorPlugins.hdcd
                ?? throw new InvalidDataException("HDCD was not registered.");
            object? hdcd = null;
            try
            {
                hdcd = Activator.CreateInstance(
                    hdcdType,
                    new object[] { 2, 44100, 24, false });
                if (hdcd == null)
                    throw new InvalidDataException("HDCD returned no instance.");
            }
            finally
            {
                if (hdcd != null)
                    hdcdType.GetMethod("Close")?.Invoke(hdcd, null);
            }
            lines.Add("PASS\tHDCD\tconstructor");
        }
        catch (Exception ex)
        {
            Exception failure = ex is TargetInvocationException { InnerException: not null }
                ? ex.InnerException
                : ex;
            lines.Add("FAIL\t" + failure.GetType().Name);
        }

        using (var stream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
        {
            foreach (string line in lines)
                writer.WriteLine(line);
        }
        return lines.Any(line => line.StartsWith("FAIL\t", StringComparison.Ordinal))
            ? 2
            : 0;
    }

    /// <summary>
    /// Exercises actual initialization, writes, finalization, and native lossless verification in
    /// the production WPF process. Version getters alone cannot prove that every required encoder
    /// entry point is present or that the final flush succeeds.
    /// </summary>
    private static void ProbeRealEncodes(string outputDirectory)
    {
        AudioPCMConfig pcm = AudioPCMConfig.RedBook;
        const int sampleCount = 2048;
        var samples = new int[sampleCount, 2];
        for (int i = 0; i < sampleCount; i++)
        {
            samples[i, 0] = ((i * 7919) & 0xffff) - 32768;
            samples[i, 1] = ((i * 3571) & 0xffff) - 32768;
        }
        var buffer = new AudioBuffer(pcm, samples, sampleCount);
        string workDirectory = Path.Combine(
            outputDirectory,
            "codec-probe-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(workDirectory))
            throw new IOException("The native codec probe work directory already exists.");
        Directory.CreateDirectory(workDirectory);
        string flacPath = Path.Combine(workDirectory, "probe.flac");
        string wavPackPath = Path.Combine(workDirectory, "probe.wv");
        string monkeyPath = Path.Combine(workDirectory, "probe.ape");
        string lamePath = Path.Combine(workDirectory, "probe.mp3");
        string[] outputs = { flacPath, wavPackPath, monkeyPath, lamePath };
        try
        {
            var flac = new CUETools.Codecs.libFLAC.Encoder(
                new CUETools.Codecs.libFLAC.EncoderSettings
                {
                    PCM = pcm,
                    EncoderMode = "5",
                    Verify = true,
                    MD5Sum = true,
                },
                flacPath);
            flac.FinalSampleCount = sampleCount;
            flac.Write(buffer);
            flac.Close();

            var wavPack = new CUETools.Codecs.libwavpack.AudioEncoder(
                new CUETools.Codecs.libwavpack.EncoderSettings
                {
                    PCM = pcm,
                    EncoderMode = "normal",
                    Verify = true,
                    MD5Sum = true,
                },
                wavPackPath);
            wavPack.FinalSampleCount = sampleCount;
            wavPack.Write(buffer);
            wavPack.Close();

            var monkey = new CUETools.Codecs.MACLib.AudioEncoder(
                new CUETools.Codecs.MACLib.EncoderSettings
                {
                    PCM = pcm,
                    EncoderMode = "high",
                    Verify = true,
                },
                monkeyPath);
            monkey.FinalSampleCount = sampleCount;
            monkey.Write(buffer);
            monkey.Close();

            var lame = new CUETools.Codecs.libmp3lame.AudioEncoder(
                new CUETools.Codecs.libmp3lame.VBREncoderSettings
                {
                    PCM = pcm,
                    EncoderMode = "V2",
                },
                lamePath);
            lame.FinalSampleCount = sampleCount;
            lame.Write(buffer);
            lame.Close();

            foreach (string output in outputs)
                if (!File.Exists(output) || new FileInfo(output).Length == 0)
                    throw new InvalidDataException(
                        "A native codec probe produced no finalized output: " +
                        Path.GetExtension(output));
        }
        finally
        {
            foreach (string output in outputs)
            {
                try
                {
                    if (File.Exists(output))
                        File.Delete(output);
                }
                catch { }
            }
            try
            {
                if (Directory.Exists(workDirectory))
                    Directory.Delete(workDirectory, recursive: false);
            }
            catch { }
        }
    }
}
