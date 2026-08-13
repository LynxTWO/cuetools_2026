using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CUETools.Codecs;
using CUETools.Codecs.Icecast;
using CUETools.Compression;
using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class PluginManifestTrustTests
{
    [TestMethod]
    public void PluginDiscovery_RequiresCompressionProviderContract()
    {
        Assert.IsTrue(
            CUEProcessorPlugins.IsCompressionProviderType(
                typeof(ValidCompressionProvider)));
        Assert.IsFalse(
            CUEProcessorPlugins.IsCompressionProviderType(
                typeof(AttributedImpostor)));
    }

    [TestMethod]
    public void PluginDiscovery_RequiresCompleteHdcdFilterContract()
    {
        Assert.IsTrue(
            CUEProcessorPlugins.IsHdcdFilterType(
                typeof(CUETools.Codecs.HDCD.HDCDDotNet)));
        Assert.IsFalse(
            CUEProcessorPlugins.IsHdcdFilterType(
                typeof(HDCDDotNet)));
    }

    [TestMethod]
    public void PluginDiscovery_RequiresExactSharedInterfaceIdentity()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(repoRoot);
        string discovery = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "CUETools.Processor",
                "CUEProcessorPlugins.cs"));

        StringAssert.Contains(
            discovery,
            "typeof(IAudioEncoderSettings).IsAssignableFrom(type)");
        StringAssert.Contains(
            discovery,
            "typeof(IAudioDecoderSettings).IsAssignableFrom(type)");
        StringAssert.Contains(
            discovery,
            "typeof(ICDRipper).IsAssignableFrom(type)");
        Assert.IsFalse(
            discovery.Contains(
                "GetInterface(typeof(IAudioEncoderSettings).Name)",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void RejectedUserPluginTypesRestoreRegistryMembershipAndBindings()
    {
        int encoderCount = CUEProcessorPlugins.encs.Count;
        int decoderCount = CUEProcessorPlugins.decs.Count;
        int compressionProviderCount = CUEProcessorPlugins.arcp.Count;
        int compressionFormatCount = CUEProcessorPlugins.arcp_fmt.Count;
        Type hdcd = CUEProcessorPlugins.hdcd;
        Type ripper = CUEProcessorPlugins.ripper;

        Assert.ThrowsException<InvalidOperationException>(
            () => CUEProcessorPlugins.AddPluginTypesTransactionally(
                new[]
                {
                    typeof(ValidEncoderSettings),
                    typeof(ThrowingEncoderSettings)
                },
                "transaction test"));

        Assert.AreEqual(encoderCount, CUEProcessorPlugins.encs.Count);
        Assert.AreEqual(decoderCount, CUEProcessorPlugins.decs.Count);
        Assert.AreEqual(
            compressionProviderCount,
            CUEProcessorPlugins.arcp.Count);
        Assert.AreEqual(
            compressionFormatCount,
            CUEProcessorPlugins.arcp_fmt.Count);
        Assert.AreSame(hdcd, CUEProcessorPlugins.hdcd);
        Assert.AreSame(ripper, CUEProcessorPlugins.ripper);
        Assert.IsFalse(
            CUEProcessorPlugins.encs.Any(
                settings => settings is ValidEncoderSettings));
    }

    [TestMethod]
    public void Manifest_ApprovesExactRuntimeDllSet()
    {
        using var tree = new TempTree("cuetools-plugin-trust");
        string approved = tree.Write("CUETools.Approved.dll", "approved bytes");
        tree.WriteManifest((Hash(approved), "CUETools.Approved.dll"));

        IList<ApprovedPlugin> entries = PluginTrustManifest.ReadApprovedPlugins(tree.Root);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("CUETools.Approved.dll", entries[0].RelativePath);
        Assert.AreEqual(Path.GetFullPath(approved), entries[0].FullPath);
    }

    [CompressionProviderClass("valid-test")]
    private sealed class ValidCompressionProvider : ICompressionProvider
    {
        public Stream Decompress(string file) => Stream.Null;
        public void Close() { }
        public IEnumerable<string> Contents => Array.Empty<string>();
        public event EventHandler<CompressionPasswordRequiredEventArgs> PasswordRequired
        {
            add { }
            remove { }
        }
        public event EventHandler<CompressionExtractionProgressEventArgs> ExtractionProgress
        {
            add { }
            remove { }
        }
    }

    private sealed class ValidEncoderSettings
        : CUETools.Codecs.CommandLine.EncoderSettings
    {
    }

    private sealed class ThrowingEncoderSettings
        : CUETools.Codecs.CommandLine.EncoderSettings
    {
        public ThrowingEncoderSettings()
        {
            // Approved code runs in-process and can touch the legacy public registries. The
            // The loader must restore even this direct collection side effect before rejecting
            // the complete user set. Approved code is not sandboxed from other object mutation.
            CUEProcessorPlugins.encs.Add(new ValidEncoderSettings());
            throw new InvalidOperationException("deliberate constructor failure");
        }
    }

    [CompressionProviderClass("impostor-test")]
    private sealed class AttributedImpostor
    {
    }

    private sealed class HDCDDotNet
    {
        public HDCDDotNet(
            int channels,
            int sampleRate,
            int outputBits,
            bool decode)
        {
        }
    }

    [TestMethod]
    public void Manifest_RejectsUnlistedRuntimeDll()
    {
        using var tree = new TempTree("cuetools-plugin-trust");
        string approved = tree.Write("CUETools.Approved.dll", "approved bytes");
        tree.Write("x64/native-codec.dll", "unlisted native bytes");
        tree.WriteManifest((Hash(approved), "CUETools.Approved.dll"));

        Assert.ThrowsException<PluginTrustException>(
            () => PluginTrustManifest.ReadApprovedPlugins(tree.Root));
    }

    [TestMethod]
    public void Manifest_RejectsTamperedPlugin()
    {
        using var tree = new TempTree("cuetools-plugin-trust");
        string plugin = tree.Write("CUETools.Approved.dll", "approved bytes");
        tree.WriteManifest((Hash(plugin), "CUETools.Approved.dll"));
        File.AppendAllText(plugin, "tampered");

        Assert.ThrowsException<PluginTrustException>(
            () => PluginTrustManifest.ReadApprovedPlugins(tree.Root));
    }

    [TestMethod]
    public void Manifest_RejectsTraversalDuplicateUnknownDirectoryAndWrongOrder()
    {
        using var traversal = new TempTree("cuetools-plugin-trust");
        traversal.WriteManifest((new string('0', 64), "../CUETools.Escape.dll"));
        Assert.ThrowsException<PluginTrustException>(
            () => PluginTrustManifest.ReadApprovedPlugins(traversal.Root));

        using var duplicate = new TempTree("cuetools-plugin-trust");
        string plugin = duplicate.Write("CUETools.Approved.dll", "approved bytes");
        string hash = Hash(plugin);
        duplicate.WriteManifest(
            (hash, "CUETools.Approved.dll"),
            (hash, "CUETools.Approved.dll"));
        Assert.ThrowsException<PluginTrustException>(
            () => PluginTrustManifest.ReadApprovedPlugins(duplicate.Root));

        using var unknownDirectory = new TempTree("cuetools-plugin-trust");
        string other = unknownDirectory.Write("other/Other.dll", "other bytes");
        unknownDirectory.WriteManifest((Hash(other), "other/Other.dll"));
        Assert.ThrowsException<PluginTrustException>(
            () => PluginTrustManifest.ReadApprovedPlugins(unknownDirectory.Root));

        using var wrongOrder = new TempTree("cuetools-plugin-trust");
        string zulu = wrongOrder.Write("Zulu.dll", "zulu");
        string alpha = wrongOrder.Write("Alpha.dll", "alpha");
        wrongOrder.WriteManifest(
            (Hash(zulu), "Zulu.dll"),
            (Hash(alpha), "Alpha.dll"));
        Assert.ThrowsException<PluginTrustException>(
            () => PluginTrustManifest.ReadApprovedPlugins(wrongOrder.Root));
    }

    [TestMethod]
    public void Manifest_RejectsReparsePointPluginDirectory()
    {
        using var tree = new TempTree("cuetools-plugin-trust");
        string target = tree.Write("real-x64/CUETools.Linked.dll", "approved bytes");
        string link = Path.Combine(tree.Root, "x64");
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/d /c mklink /J \"" + link + "\" \"" +
                Path.GetDirectoryName(target) + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process process = Process.Start(startInfo);
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, "could not create test junction: " + output);
        try
        {
            tree.WriteManifest((Hash(target), "x64/CUETools.Linked.dll"));

            Assert.ThrowsException<PluginTrustException>(
                () => PluginTrustManifest.ReadApprovedPlugins(tree.Root));
        }
        finally
        {
            try { Directory.Delete(link); } catch { }
        }
    }

    [TestMethod]
    public void Manifest_RuntimeFilterLoadsTopLevelAndOnlyMatchingArchitecture()
    {
        using var tree = new TempTree("cuetools-plugin-trust");
        string common = tree.Write("CUETools.Common.dll", "common");
        string dependency = tree.Write("Dependency.dll", "managed dependency");
        string x64 = tree.Write("x64/CUETools.Codecs.TTA.dll", "x64");
        string x64Native = tree.Write("x64/native-codec.dll", "x64 native");
        string win32 = tree.Write("win32/CUETools.Codecs.TTA.dll", "win32");
        tree.WriteManifest(
            (Hash(common), "CUETools.Common.dll"),
            (Hash(dependency), "Dependency.dll"),
            (Hash(win32), "win32/CUETools.Codecs.TTA.dll"),
            (Hash(x64), "x64/CUETools.Codecs.TTA.dll"),
            (Hash(x64Native), "x64/native-codec.dll"));

        IList<ApprovedPlugin> entries = PluginTrustManifest.ReadApprovedPlugins(tree.Root);
        string[] approvedForX64 = entries
            .Where(entry => PluginTrustManifest.IsForRuntimeArchitecture(entry, "x64"))
            .Select(entry => entry.RelativePath)
            .ToArray();
        string[] approvedForWin32 = entries
            .Where(entry => PluginTrustManifest.IsForRuntimeArchitecture(entry, "win32"))
            .Select(entry => entry.RelativePath)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "CUETools.Common.dll",
                "Dependency.dll",
                "x64/CUETools.Codecs.TTA.dll",
                "x64/native-codec.dll",
            },
            approvedForX64);
        CollectionAssert.AreEquivalent(
            new[]
            {
                "CUETools.Common.dll",
                "Dependency.dll",
                "win32/CUETools.Codecs.TTA.dll",
            },
            approvedForWin32);

        string[] loadableForX64 = entries
            .Where(entry => PluginTrustManifest.IsForRuntimeArchitecture(entry, "x64"))
            .Where(PluginTrustManifest.IsLoadableManagedPlugin)
            .Select(entry => entry.RelativePath)
            .ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "CUETools.Common.dll", "x64/CUETools.Codecs.TTA.dll" },
            loadableForX64);
    }

    [TestMethod]
    public void Manifest_LoadRejectsPreloadedSameIdentityFromDifferentLocation()
    {
        using var tree = new TempTree("cuetools-plugin-trust");
        string source = typeof(PluginTrustManifest).Assembly.Location;
        string approved = tree.Copy("CUETools.Processor.dll", source);
        tree.WriteManifest((Hash(approved), "CUETools.Processor.dll"));
        ApprovedPlugin entry =
            PluginTrustManifest.ReadApprovedPlugins(tree.Root).Single();
        System.Reflection.MethodInfo loader = typeof(PluginTrustManifest).GetMethod(
            "LoadApprovedAssembly",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(loader);

        var invocation = Assert.ThrowsException<System.Reflection.TargetInvocationException>(
            () => loader.Invoke(null, new object[] { entry }));

        Assert.IsNotNull(invocation.InnerException);
        Assert.AreEqual(
            typeof(PluginTrustException),
            invocation.InnerException.GetType());
        StringAssert.Contains(
            invocation.InnerException.Message,
            "different location");
    }

    [TestMethod]
    public void Manifest_AcceptsByteIdenticalProjectReferenceCopyInApplicationRoot()
    {
        using var tree = new TempTree("cuetools-plugin-trust");
        string applicationRoot = Path.Combine(tree.Root, "app");
        string pluginRoot = Path.Combine(applicationRoot, "plugins");
        Directory.CreateDirectory(pluginRoot);

        string source = typeof(PluginTrustManifest).Assembly.Location;
        string rootCopy = Path.Combine(
            applicationRoot,
            "CUETools.Processor.dll");
        string approvedCopy = Path.Combine(
            pluginRoot,
            "CUETools.Processor.dll");
        File.Copy(source, rootCopy);
        File.Copy(source, approvedCopy);
        File.WriteAllText(
            Path.Combine(pluginRoot, PluginTrustManifest.ManifestFileName),
            Hash(approvedCopy) + "\tCUETools.Processor.dll" +
            Environment.NewLine);
        ApprovedPlugin entry =
            PluginTrustManifest.ReadApprovedPlugins(pluginRoot).Single();

        var loadContext = new System.Runtime.Loader.AssemblyLoadContext(
            "approved-root-copy-" + Guid.NewGuid().ToString("N"),
            isCollectible: true);
        try
        {
            System.Reflection.Assembly loaded =
                loadContext.LoadFromAssemblyPath(rootCopy);

            PluginTrustManifest.EnsureLoadedAssemblyMatchesApprovedPlugin(
                loaded,
                entry);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [TestMethod]
    public void Manifest_RequiresExactNativeDependencyForEachImportedNativePlugin()
    {
        using var tree = new TempTree("cuetools-plugin-trust");
        var records = new List<(string hash, string relativePath)>();
        foreach ((string managed, string native) in new[]
        {
            ("CUETools.Codecs.HDCD.dll", "hdcd.dll"),
            ("CUETools.Codecs.libFLAC.dll", "libFLAC_dynamic.dll"),
            ("CUETools.Codecs.libmp3lame.dll", "libmp3lame.dll"),
            ("CUETools.Codecs.libwavpack.dll", "wavpackdll.dll"),
            ("CUETools.Codecs.MACLib.dll", "MACLibDll.dll"),
        })
        {
            string managedPath = tree.Write(managed, managed);
            string nativePath = tree.Write("x64/" + native, native);
            records.Add((Hash(managedPath), managed));
            records.Add((Hash(nativePath), "x64/" + native));
        }
        string rarManaged = tree.Write(
            "x64/CUETools.Compression.Rar.dll", "managed RAR wrapper");
        string unrarNative = tree.Write(
            "x64/Unrar.dll", "UnRAR native module");
        records.Add((
            Hash(rarManaged), "x64/CUETools.Compression.Rar.dll"));
        records.Add((Hash(unrarNative), "x64/Unrar.dll"));
        tree.WriteManifest(
            records.OrderBy(record => record.relativePath, StringComparer.Ordinal)
                .ToArray());

        IList<ApprovedPlugin> approved =
            PluginTrustManifest.ReadApprovedPlugins(tree.Root);
        string[] dependencies = PluginTrustManifest
            .GetRequiredNativeDependencies(approved, "x64")
            .Select(plugin => plugin.RelativePath)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "x64/Unrar.dll",
                "x64/hdcd.dll",
                "x64/libFLAC_dynamic.dll",
                "x64/libmp3lame.dll",
                "x64/wavpackdll.dll",
                "x64/MACLibDll.dll",
            },
            dependencies);

        using var missing = new TempTree("cuetools-plugin-trust");
        string wrapper = missing.Write(
            "x64/CUETools.Compression.Rar.dll", "managed RAR wrapper");
        missing.WriteManifest(
            (Hash(wrapper), "x64/CUETools.Compression.Rar.dll"));
        IList<ApprovedPlugin> incomplete =
            PluginTrustManifest.ReadApprovedPlugins(missing.Root);
        PluginTrustException failure =
            Assert.ThrowsException<PluginTrustException>(
            () => PluginTrustManifest.GetRequiredNativeDependencies(
                incomplete, "x64"));
        StringAssert.Contains(failure.Message, "x64/unrar.dll");
    }

    [TestMethod]
    public void Manifest_NativeLoadRehashesWhileDenyingMutation()
    {
        using var tree = new TempTree("cuetools-plugin-trust");
        string native = tree.Write(
            "x64/libmp3lame.dll", "approved native bytes");
        tree.WriteManifest((Hash(native), "x64/libmp3lame.dll"));
        ApprovedPlugin approved =
            PluginTrustManifest.ReadApprovedPlugins(tree.Root).Single();
        bool writeWasDenied = false;

        IntPtr module = PluginTrustManifest.LoadApprovedNativeDependency(
            approved,
            path =>
            {
                try
                {
                    using FileStream ignored = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete);
                }
                catch (IOException)
                {
                    writeWasDenied = true;
                }
                return new IntPtr(123);
            },
            handle => approved.FullPath,
            handle => Assert.Fail("An approved module must remain loaded."));

        Assert.AreEqual(new IntPtr(123), module);
        Assert.IsTrue(
            writeWasDenied,
            "The approved native file must deny writes until the loader maps it.");

        File.AppendAllText(native, "tampered");
        bool loaderWasCalled = false;
        Assert.ThrowsException<PluginTrustException>(
            () => PluginTrustManifest.LoadApprovedNativeDependency(
                approved,
                path =>
                {
                    loaderWasCalled = true;
                    return new IntPtr(456);
                },
                handle => approved.FullPath,
                handle => { }));
        Assert.IsFalse(
            loaderWasCalled,
            "Changed bytes must be rejected before entering the native loader.");
    }

    [TestMethod]
    public void Manifest_NativeLoadRejectsPreloadedSameNameFromDifferentPath()
    {
        using var tree = new TempTree("cuetools-plugin-trust");
        string native = tree.Write(
            "x64/libmp3lame.dll", "approved native bytes");
        tree.WriteManifest((Hash(native), "x64/libmp3lame.dll"));
        ApprovedPlugin approved =
            PluginTrustManifest.ReadApprovedPlugins(tree.Root).Single();
        bool released = false;

        PluginTrustException failure =
            Assert.ThrowsException<PluginTrustException>(
                () => PluginTrustManifest.LoadApprovedNativeDependency(
                    approved,
                    path => new IntPtr(789),
                    handle => Path.Combine(
                        tree.Root, "preloaded-lookalike", "libmp3lame.dll"),
                    handle => released = true));

        StringAssert.Contains(failure.Message, "different location");
        Assert.IsTrue(
            released,
            "The extra reference to an unapproved preloaded module must be released.");
    }

    [TestMethod]
    public void NativeCodecWrappers_UseOnlyApprovedOrAssemblyRelativePaths()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(repoRoot);
        string[] wrappers =
        {
            "CUETools.Codecs.HDCD/HDCDDLL.cs",
            "CUETools.Codecs.libFLAC/FLACDLL.cs",
            "CUETools.Codecs.libmp3lame/libmp3lamedll.cs",
            "CUETools.Codecs.libwavpack/wavpackdll.cs",
            "CUETools.Codecs.MACLib/MACLibDll.cs",
        };
        foreach (string relativePath in wrappers)
        {
            string source = File.ReadAllText(Path.Combine(
                repoRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            StringAssert.Contains(
                source,
                "NativeDependencyPathRegistry.ResolvePath");
            Assert.IsFalse(
                source.Contains("Assembly.CodeBase", StringComparison.Ordinal),
                relativePath + " must not use the obsolete code-base URI path.");
            Assert.IsFalse(
                source.Contains("LoadLibrary(DllName +", StringComparison.Ordinal),
                relativePath + " must not fall back to a bare native DLL name.");
        }
    }

    [TestMethod]
    public void NativePathRegistry_PrefersApprovedPathAndRejectsConflict()
    {
        using var tree = new TempTree("cuetools-native-path-registry");
        string name = "codec-" + Guid.NewGuid().ToString("N") + ".dll";
        string approved = tree.Write(name, "approved native bytes");
        string conflicting = tree.Write("other/" + name, "different native bytes");

        NativeDependencyPathRegistry.RegisterManifestApprovedPath(name, approved);

        Assert.AreEqual(
            Path.GetFullPath(approved),
            NativeDependencyPathRegistry.ResolvePath(typeof(CUEConfig).Assembly, name),
            true);
        Assert.ThrowsException<InvalidOperationException>(() =>
            NativeDependencyPathRegistry.RegisterManifestApprovedPath(
                name,
                conflicting));
    }

    [TestMethod]
    public void NativePathRegistry_UnregisteredFallbackIsOneArchitectureSubdirectory()
    {
        string name = "unregistered-" + Guid.NewGuid().ToString("N") + ".dll";
        string expected = Path.Combine(
            Path.GetDirectoryName(typeof(CUEConfig).Assembly.Location),
            IntPtr.Size == 8 ? "x64" : "win32",
            name);

        Assert.AreEqual(
            Path.GetFullPath(expected),
            NativeDependencyPathRegistry.ResolvePath(typeof(CUEConfig).Assembly, name),
            true);
    }

    [TestMethod]
    public void ClassicManifestWriter_EmitsBothArchitecturesDeterministically()
    {
        using var tree = new TempTree("cuetools-plugin-trust");
        tree.Write("CUETools.Common.dll", "common");
        tree.Write("Dependency.dll", "dependency");
        tree.Write("x64/CUETools.Codecs.TTA.dll", "x64");
        tree.Write("x64/native-codec.dll", "native");
        tree.Write("win32/CUETools.Codecs.TTA.dll", "win32");
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(repoRoot);
        string script = Path.Combine(repoRoot, "tools", "Write-PluginManifest.ps1");

        RunPowerShell(script, tree.Root);
        string manifestPath = Path.Combine(
            tree.Root, PluginTrustManifest.ManifestFileName);
        byte[] first = File.ReadAllBytes(manifestPath);
        RunPowerShell(script, tree.Root);
        byte[] second = File.ReadAllBytes(manifestPath);

        CollectionAssert.AreEqual(first, second);
        string[] relativePaths = File.ReadAllLines(manifestPath)
            .Select(line => line.Split('\t')[1])
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "CUETools.Common.dll",
                "Dependency.dll",
                "win32/CUETools.Codecs.TTA.dll",
                "x64/CUETools.Codecs.TTA.dll",
                "x64/native-codec.dll",
            },
            relativePaths);

        string compatibilityEntryPoint = File.ReadAllText(
            Path.Combine(repoRoot, "collect_files.bat"));
        Assert.IsTrue(compatibilityEntryPoint.Contains(
            "eng\\release\\Invoke-ClassicRelease.ps1",
            StringComparison.Ordinal));
        Assert.IsFalse(compatibilityEntryPoint.Contains(
            "-File \"%~dp0eng\\release\\Collect-ClassicArtifacts.ps1",
            StringComparison.OrdinalIgnoreCase),
            "The compatibility entry point must not bypass the leased build/receipt orchestrator.");
        string collectScript = File.ReadAllText(
            Path.Combine(repoRoot, "eng", "release", "Collect-ClassicArtifacts.ps1"));
        Assert.IsTrue(collectScript.Contains(
            "tools\\Write-PluginManifest.ps1",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void UnmanifestedCompatibilityMode_RequiresExactExplicitValue()
    {
        string name = PluginTrustManifest.LocalDevelopmentEnvironmentVariable;
        string previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, null);
            Assert.IsFalse(PluginTrustManifest.IsLocalDevelopmentModeEnabled());
            Environment.SetEnvironmentVariable(name, "true");
            Assert.IsFalse(PluginTrustManifest.IsLocalDevelopmentModeEnabled());
            Environment.SetEnvironmentVariable(name, "1");
            Assert.IsTrue(PluginTrustManifest.IsLocalDevelopmentModeEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RunPowerShell(string script, string pluginDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-PluginDirectory");
        startInfo.ArgumentList.Add(pluginDirectory);

        using Process process = Process.Start(startInfo);
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, "manifest writer failed: " + output);
    }

    private sealed class TempTree : IDisposable
    {
        public TempTree(string prefix)
        {
            Root = Path.Combine(
                Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Write(string relativePath, string contents)
        {
            string path = Path.Combine(Root, relativePath);
            string parent = Path.GetDirectoryName(path);
            if (parent != null)
                Directory.CreateDirectory(parent);
            File.WriteAllText(path, contents);
            return path;
        }

        public string Copy(string relativePath, string source)
        {
            string path = Path.Combine(Root, relativePath);
            string parent = Path.GetDirectoryName(path);
            if (parent != null)
                Directory.CreateDirectory(parent);
            File.Copy(source, path);
            return path;
        }

        public void WriteManifest(params (string hash, string relativePath)[] entries)
        {
            File.WriteAllLines(
                Path.Combine(Root, PluginTrustManifest.ManifestFileName),
                entries.Select(entry => entry.hash + "\t" + entry.relativePath));
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}

[TestClass]
public sealed class ExternalEncoderTrustTests
{
    private sealed class FakeLog : IDiagnosticLog
    {
        public readonly List<string> Messages = new();
        public string LogPath => "unused.log";
        public void Info(string category, string message) =>
            Messages.Add(category + ": " + message);
        public void Warn(string category, string message) =>
            Messages.Add(category + ": " + message);
        public void Error(string category, string message, Exception ex = null) =>
            Messages.Add(category + ": " + message);
        public void Redact(params string[] sensitive) { }
    }

    [TestMethod]
    public void Import_AtomicallyPublishesAndApprovesExactManagedBytes()
    {
        using var tree = new EncoderTree();
        byte[] bytes = Encoding.ASCII.GetBytes("fake encoder v1");
        string source = tree.WriteSource(bytes);

        string error = tree.Catalog.Import(tree.Config, tree.Info, source);

        Assert.IsNull(error);
        string destination = Path.Combine(tree.ManagedDirectory, tree.Info.ExeName);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(destination));
        Assert.AreEqual(destination, tree.Catalog.ResolveExe(tree.Encoder));
        Assert.AreEqual(0, Directory.GetFiles(tree.ManagedDirectory, "*.importing.exe").Length);
        Assert.IsTrue(tree.App.TryGetExternalEncoderApproval(
            tree.Info.ExeName, out ExternalEncoderApproval approval));
        Assert.AreEqual(bytes.Length, approval.Length);
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(bytes)),
            approval.Sha256);
        var runtimeSettings =
            (CUETools.Codecs.CommandLine.EncoderSettings)tree.Encoder.Settings;
        Assert.AreEqual(approval.Sha256, runtimeSettings.ApprovedExecutableSha256);
        Assert.AreEqual(approval.Length, runtimeSettings.ApprovedExecutableLength);
        Assert.AreEqual("user-selected-local-file", approval.OriginKind);
        Assert.AreEqual(tree.Info.EncoderName, approval.EncoderName);
        Assert.AreEqual(tree.Info.Extension, approval.Extension);
    }

    [DataTestMethod]
    [DataRow("qaac.exe", "qaac64.exe")]
    [DataRow("oggenc.exe", "oggenc2.exe")]
    public void Import_AcceptsAndApprovesCuratedAlternateBasename(
        string preferredName,
        string alternateName)
    {
        using var tree = new EncoderTree();
        tree.Info.ExeName = preferredName;
        tree.Info.AcceptedExeNames =
            new[] { preferredName, alternateName };
        byte[] bytes = Encoding.ASCII.GetBytes("alternate encoder");
        string source = tree.WriteSource(bytes, alternateName);

        string error = tree.Catalog.Import(tree.Config, tree.Info, source);

        Assert.IsNull(error);
        string destination =
            Path.Combine(tree.ManagedDirectory, alternateName);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(destination));
        Assert.AreEqual(destination, tree.Catalog.ResolveExe(tree.Encoder));
        Assert.IsTrue(tree.App.TryGetExternalEncoderApproval(
            alternateName,
            out ExternalEncoderApproval approval));
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(bytes)),
            approval.Sha256);
    }

    [TestMethod]
    public void CatalogAdvertisesBothSupportedQaacAndOggEncBasenames()
    {
        using var tree = new EncoderTree();
        ExternalEncoderInfo[] snapshot =
            tree.Catalog.Snapshot(tree.Config).ToArray();
        ExternalEncoderInfo qaac = snapshot.Single(
            item => item.EncoderName == "qaac.exe (tvbr)");
        ExternalEncoderInfo ogg = snapshot.Single(
            item => item.EncoderName == "oggenc.exe");

        CollectionAssert.AreEquivalent(
            new[] { "qaac.exe", "qaac64.exe" },
            qaac.AcceptedExeNames);
        CollectionAssert.AreEquivalent(
            new[] { "oggenc.exe", "oggenc2.exe" },
            ogg.AcceptedExeNames);
    }

    [TestMethod]
    public void PackagedAlternateBasenameIsDiscoveredWithoutRenamingUpstreamBytes()
    {
        using var tree = new EncoderTree();
        tree.Catalog.EnsureRegistered(tree.Config);
        Directory.CreateDirectory(tree.BundledDirectory);
        string packaged = Path.Combine(
            tree.BundledDirectory,
            "oggenc2.exe");
        File.WriteAllText(packaged, "packaged oggenc2");
        tree.ApprovePackaged(packaged);
        AudioEncoderSettingsViewModel ogg = tree.Config.Encoders.Single(
            item => item.Name == "oggenc.exe");
        ogg.Path = "oggenc.exe";

        Assert.AreEqual(packaged, tree.Catalog.ResolveExe(ogg));
        var settings = (CUETools.Codecs.CommandLine.EncoderSettings)ogg.Settings;
        Assert.AreEqual(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packaged))),
            settings.ApprovedExecutableSha256);
        Assert.AreEqual(new FileInfo(packaged).Length, settings.ApprovedExecutableLength);
    }

    [TestMethod]
    public void TamperedPackagedEncoderIsRefusedAtResolution()
    {
        using var tree = new EncoderTree();
        Directory.CreateDirectory(tree.BundledDirectory);
        string packaged = Path.Combine(
            tree.BundledDirectory,
            tree.Info.ExeName);
        File.WriteAllText(packaged, "approved packaged encoder");
        tree.ApprovePackaged(packaged);
        File.WriteAllText(packaged, "changed packaged encoder");

        Assert.IsNull(tree.Catalog.ResolveExe(tree.Encoder));
        Assert.IsTrue(tree.Log.Messages.Any(message =>
            message.Contains(
                "packaged encoder testenc.exe refused (hash)",
                StringComparison.Ordinal)));
    }

    [TestMethod]
    public void CatalogDoesNotSubstituteAliasForTamperedConfiguredExecutable()
    {
        using var tree = new EncoderTree();
        ExternalEncoderInfo qaac = tree.Catalog.Snapshot(tree.Config)
            .Single(item => item.EncoderName == "qaac.exe (tvbr)");

        Assert.IsNull(tree.Catalog.Import(
            tree.Config,
            qaac,
            tree.WriteSource(
                Encoding.ASCII.GetBytes("approved alias"),
                "qaac64.exe")));
        Assert.IsNull(tree.Catalog.Import(
            tree.Config,
            qaac,
            tree.WriteSource(
                Encoding.ASCII.GetBytes("approved preferred"),
                "qaac.exe")));
        File.WriteAllText(
            Path.Combine(tree.ManagedDirectory, "qaac.exe"),
            "tampered preferred");

        ExternalEncoderInfo refreshed = tree.Catalog.Snapshot(tree.Config)
            .Single(item => item.EncoderName == "qaac.exe (tvbr)");

        Assert.IsFalse(refreshed.Found);
        Assert.AreEqual("", refreshed.ResolvedPath);
        Assert.IsTrue(tree.Log.Messages.Any(message =>
            message.Contains("refused (hash)", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ImportedLosslessEncoderBindsVerifierToApprovedFullExecutable()
    {
        using var tree = new EncoderTree();
        Assert.IsNull(tree.Catalog.Import(
            tree.Config,
            tree.Info,
            tree.WriteSource(Encoding.ASCII.GetBytes("self-decoding encoder"))));

        Assert.IsTrue(tree.Catalog.IsUsable(tree.Encoder));
        var settings =
            (CUETools.Codecs.CommandLine.EncoderSettings)tree.Encoder.Settings;
        Assert.AreEqual(
            Path.Combine(tree.ManagedDirectory, tree.Info.ExeName),
            settings.Path);
        Assert.IsTrue(settings.VerificationUsesEncoder);
        Assert.AreEqual("", settings.VerificationPath);
        StringAssert.Contains(settings.VerificationParameters, "%I");
    }

    [TestMethod]
    public void HashBoundUserImportOverridesPackagedEncoder()
    {
        using var tree = new EncoderTree();
        Directory.CreateDirectory(tree.BundledDirectory);
        string bundled = Path.Combine(
            tree.BundledDirectory,
            tree.Info.ExeName);
        File.WriteAllText(bundled, "packaged encoder");
        tree.ApprovePackaged(bundled);

        Assert.AreEqual(
            bundled,
            tree.Catalog.ResolveExe(tree.Encoder));

        Assert.IsNull(tree.Catalog.Import(
            tree.Config,
            tree.Info,
            tree.WriteSource(Encoding.ASCII.GetBytes("new user encoder"))));

        Assert.AreEqual(
            Path.Combine(tree.ManagedDirectory, tree.Info.ExeName),
            tree.Catalog.ResolveExe(tree.Encoder));
    }

    [TestMethod]
    public void PathResolvedLosslessEncoderFreezesAbsoluteExecutableIdentity()
    {
        using var tree = new EncoderTree();
        string executable = tree.WriteSource(
            Encoding.ASCII.GetBytes("path encoder"));
        string previousPath =
            Environment.GetEnvironmentVariable("PATH");
        string sourceDirectory =
            Path.GetDirectoryName(executable);
        try
        {
            Environment.SetEnvironmentVariable(
                "PATH",
                sourceDirectory + Path.PathSeparator +
                    (previousPath ?? ""));

            Assert.IsTrue(tree.Catalog.IsUsable(tree.Encoder));
            Assert.AreEqual(
                Path.GetFullPath(executable),
                tree.Encoder.Path,
                true,
                "PATH discovery must be replaced with one absolute identity before encoding.");
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "PATH",
                previousPath);
        }
    }

    [TestMethod]
    public void LosslessEncoderRequiresAResolvableVerificationDecoder()
    {
        using var tree = new EncoderTree();
        string external = tree.WriteSource(
            Encoding.ASCII.GetBytes("external encoder"));
        tree.Encoder.Path = external;
        var settings =
            (CUETools.Codecs.CommandLine.EncoderSettings)tree.Encoder.Settings;

        settings.VerificationUsesEncoder = false;
        settings.VerificationPath = "";
        settings.VerificationParameters = "";
        Assert.IsFalse(tree.Catalog.IsUsable(tree.Encoder));

        settings.VerificationPath = external;
        settings.VerificationParameters = "-d %I -";
        Assert.IsTrue(tree.Catalog.IsUsable(tree.Encoder));
        Assert.AreEqual(
            Path.GetFullPath(external),
            settings.VerificationPath,
            true,
            "A separately configured verifier must be frozen to one absolute executable.");

        settings.VerificationPath = Path.Combine(
            tree.ManagedDirectory, "unapproved-decoder.exe");
        Directory.CreateDirectory(tree.ManagedDirectory);
        File.WriteAllText(settings.VerificationPath, "not enrolled");
        Assert.IsFalse(
            tree.Catalog.IsUsable(tree.Encoder),
            "The separate-verifier path may not bypass managed-directory receipts.");
    }

    [TestMethod]
    public void OptimFrogUsesTheExercisedFinalizedFileSelfDecodeContract()
    {
        using var tree = new EncoderTree();
        tree.Catalog.EnsureRegistered(tree.Config);

        AudioEncoderSettingsViewModel encoder = tree.Config.Encoders.Single(
            item => item.Extension == "ofr" && item.Name == "ofr.exe");
        var settings =
            (CUETools.Codecs.CommandLine.EncoderSettings)encoder.Settings;

        Assert.IsTrue(settings.Lossless);
        Assert.AreEqual("max", settings.EncoderMode);
        Assert.AreEqual(
            "--encode --silent --overwrite --preset %M --md5 %I --output %O",
            settings.Parameters);
        Assert.IsTrue(settings.VerificationRequired);
        Assert.IsTrue(settings.VerificationUsesEncoder);
        Assert.AreEqual(
            "--decode --silent --writefreshheader %I --output -",
            settings.VerificationParameters);
        Assert.IsTrue(tree.Config.formats.ContainsKey("ofr"));
        Assert.AreSame(
            encoder,
            tree.Config.formats["ofr"].encoderLossless);
        Assert.IsFalse(
            tree.Catalog.IsUsable(encoder),
            "A registered contract must not surface until ofr.exe resolves.");
    }

    [TestMethod]
    public void ExhaleImportBecomesTheRunnableM4aLossyEncoder()
    {
        using var tree = new EncoderTree();
        tree.Catalog.EnsureRegistered(tree.Config);
        ExternalEncoderInfo exhale = tree.Catalog.Snapshot(tree.Config)
            .Single(item => item.EncoderName == "exhale.exe");

        Assert.IsNull(tree.Catalog.Import(
            tree.Config,
            exhale,
            tree.WriteSource(
                Encoding.ASCII.GetBytes("user-built exhale"),
                "exhale.exe")));

        AudioEncoderSettingsViewModel selected =
            tree.Config.formats["m4a"].encoderLossy;
        Assert.AreEqual("exhale.exe", selected.Name);
        Assert.AreEqual(
            Path.Combine(tree.ManagedDirectory, "exhale.exe"),
            tree.Catalog.ResolveExe(selected));
        Assert.AreEqual("9", selected.Settings.EncoderMode);
        Assert.AreEqual(
            "%M %O",
            ((CUETools.Codecs.CommandLine.EncoderSettings)
                selected.Settings).Parameters);
    }

    [TestMethod]
    public void RunnableEncoderPickerPersistsAnExplicitImplementation()
    {
        using var tree = new EncoderTree();
        tree.Catalog.EnsureRegistered(tree.Config);
        ExternalEncoderInfo[] known =
            tree.Catalog.Snapshot(tree.Config).ToArray();
        ExternalEncoderInfo exhale = known.Single(
            item => item.EncoderName == "exhale.exe");
        ExternalEncoderInfo qaac = known.Single(
            item => item.EncoderName == "qaac.exe (tvbr)");
        Assert.IsNull(tree.Catalog.Import(
            tree.Config,
            exhale,
            tree.WriteSource(
                Encoding.ASCII.GetBytes("user-built exhale"),
                "exhale.exe")));
        Assert.IsNull(tree.Catalog.Import(
            tree.Config,
            qaac,
            tree.WriteSource(
                Encoding.ASCII.GetBytes("user-selected qaac"),
                "qaac64.exe")));

        List<AudioEncoderSettingsViewModel> usable =
            tree.Catalog.UsableEncoders(tree.Config, "m4a", false);
        CollectionAssert.AreEquivalent(
            new[] { "exhale.exe", "qaac.exe (tvbr)" },
            usable.Select(item => item.Name).ToArray());

        AudioEncoderSettingsViewModel chosen = usable.Single(
            item => item.Name == "exhale.exe");
        tree.Catalog.SetFormatEncoder(
            tree.Config,
            "m4a",
            false,
            chosen);

        Assert.AreSame(chosen, tree.Config.formats["m4a"].encoderLossy);
    }

    [TestMethod]
    public void EveryCuratedExternalEncoderExplainsHistoryUseAndDistribution()
    {
        using var tree = new EncoderTree();
        tree.Catalog.EnsureRegistered(tree.Config);

        foreach (ExternalEncoderInfo info in tree.Catalog.Snapshot(tree.Config))
        {
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(info.Description),
                info.EncoderName + " is missing a description.");
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(info.History),
                info.EncoderName + " is missing history.");
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(info.BestUse),
                info.EncoderName + " is missing best-use guidance.");
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(info.DistributionNote),
                info.EncoderName + " is missing distribution guidance.");
        }
    }

    [TestMethod]
    public void OpusArchivalDefaultKeepsTheQualifiedSignalMargin()
    {
        using var tree = new EncoderTree();
        tree.Catalog.EnsureRegistered(tree.Config);

        tree.Catalog.ApplyArchivalDefaults(tree.Config);

        AudioEncoderSettingsViewModel opus = tree.Config.Encoders.Single(
            item => item.Extension == "opus" && item.Name == "opusenc.exe");
        Assert.AreEqual("256", opus.Settings.EncoderMode);
    }

    [TestMethod]
    public void LegacyTakProfileReceivesOnlyEvidenceBackedSelfVerifierMigration()
    {
        using var tree = new EncoderTree();
        var legacyTak = new CUETools.Codecs.CommandLine.EncoderSettings(
            "takc.exe",
            "tak",
            true,
            "2 4m",
            "2",
            "takc.exe",
            "-e -p%M -overwrite - %O");
        tree.Config.advanced.encoders.Add(legacyTak);
        tree.Config.Encoders.Add(new AudioEncoderSettingsViewModel(legacyTak));
        Assert.IsFalse(legacyTak.HasLosslessVerifier);

        tree.Catalog.EnsureRegistered(tree.Config);

        Assert.IsTrue(legacyTak.HasLosslessVerifier);
        Assert.IsTrue(legacyTak.VerificationUsesEncoder);
        Assert.AreEqual("", legacyTak.VerificationPath);
        Assert.AreEqual("-d %I -", legacyTak.VerificationParameters);
    }

    [TestMethod]
    public void ManagedExecutable_RefusesTamperAndRequiresExplicitReimport()
    {
        using var tree = new EncoderTree();
        string source = tree.WriteSource(Encoding.ASCII.GetBytes("fake encoder v1"));
        Assert.IsNull(tree.Catalog.Import(tree.Config, tree.Info, source));
        string destination = Path.Combine(tree.ManagedDirectory, tree.Info.ExeName);
        File.WriteAllBytes(destination, Encoding.ASCII.GetBytes("fake encoder v2"));

        Assert.IsNull(tree.Catalog.ResolveExe(tree.Encoder));
        Assert.IsTrue(tree.Log.Messages.Any(message =>
            message.Contains("refused (hash)", StringComparison.Ordinal)));

        string replacement = tree.WriteSource(Encoding.ASCII.GetBytes("fake encoder v2"));
        Assert.IsNull(tree.Catalog.Import(tree.Config, tree.Info, replacement));
        Assert.AreEqual(destination, tree.Catalog.ResolveExe(tree.Encoder));
    }

    [TestMethod]
    public void FailedReplacement_PreservesPreviouslyApprovedExecutable()
    {
        using var tree = new EncoderTree();
        byte[] original = Encoding.ASCII.GetBytes("fake encoder v1");
        Assert.IsNull(tree.Catalog.Import(tree.Config, tree.Info, tree.WriteSource(original)));
        string destination = Path.Combine(tree.ManagedDirectory, tree.Info.ExeName);
        string replacement = tree.WriteSource(Encoding.ASCII.GetBytes("fake encoder v2"));

        string error;
        using (new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.None))
            error = tree.Catalog.Import(tree.Config, tree.Info, replacement);

        Assert.IsNotNull(error);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(destination));
        Assert.AreEqual(destination, tree.Catalog.ResolveExe(tree.Encoder));
        Assert.AreEqual(0, Directory.GetFiles(tree.ManagedDirectory, "*.importing.exe").Length);
    }

    [TestMethod]
    public void UnapprovedManagedFileIsRefusedButExternalAbsolutePathRemainsUserManaged()
    {
        using var tree = new EncoderTree();
        Directory.CreateDirectory(tree.ManagedDirectory);
        string managed = Path.Combine(tree.ManagedDirectory, tree.Info.ExeName);
        File.WriteAllText(managed, "unapproved");
        tree.Encoder.Path = tree.Info.ExeName;

        Assert.IsNull(tree.Catalog.ResolveExe(tree.Encoder));

        string external = tree.WriteSource(Encoding.ASCII.GetBytes("external"));
        tree.Encoder.Path = external;
        Assert.AreEqual(external, tree.Catalog.ResolveExe(tree.Encoder));
    }

    [TestMethod]
    public void Import_RejectsReparsePointManagedDirectory()
    {
        using var tree = new EncoderTree();
        string actualDirectory = Path.Combine(tree.Root, "actual-managed");
        Directory.CreateDirectory(actualDirectory);
        CreateJunction(tree.ManagedDirectory, actualDirectory);
        try
        {
            string error = tree.Catalog.Import(
                tree.Config,
                tree.Info,
                tree.WriteSource(Encoding.ASCII.GetBytes("fake encoder")));

            Assert.IsNotNull(error);
            Assert.IsFalse(File.Exists(Path.Combine(actualDirectory, tree.Info.ExeName)));
        }
        finally
        {
            try { Directory.Delete(tree.ManagedDirectory); } catch { }
        }
    }

    [TestMethod]
    public void ImportApproval_RoundTripsThroughSettingsStoreAndStillAuthorizesBytes()
    {
        using var tree = new EncoderTree();
        Assert.IsNull(tree.Catalog.Import(
            tree.Config,
            tree.Info,
            tree.WriteSource(Encoding.ASCII.GetBytes("persisted encoder"))));
        string destination = Path.Combine(tree.ManagedDirectory, tree.Info.ExeName);
        string fakeApp = Path.Combine(tree.Root, "profile-host", "CUETools.Wpf.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeApp));
        var settingsStore = new SettingsStore(tree.Log, fakeApp,
            new WindowsDpapiSecretProtector(WindowsDpapiSecretProtector.ProxyPurpose),
                new WindowsDpapiSecretProtector(WindowsDpapiSecretProtector.TheAudioDbPurpose));

        settingsStore.Save(new CUEConfig(), tree.App);
        var loadedApp = new AppSettings();
        settingsStore.Load(new CUEConfig(), loadedApp);

        var loadedSettings = new CUETools.Codecs.CommandLine.EncoderSettings(
            tree.Info.EncoderName,
            tree.Info.Extension,
            true,
            "1",
            "1",
            destination,
            "- %O");
        var loadedEncoder = new AudioEncoderSettingsViewModel(loadedSettings);
        var loadedCatalog = new EncoderCatalog(
            tree.Log, loadedApp, tree.ManagedDirectory);

        Assert.AreEqual(destination, loadedCatalog.ResolveExe(loadedEncoder));
        Assert.AreEqual(tree.App.ExternalEncoderApprovals, loadedApp.ExternalEncoderApprovals);
    }

    private static void CreateJunction(string link, string target)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = "/d /c mklink /J \"" + link + "\" \"" + target + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process process = Process.Start(startInfo);
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, "could not create test junction: " + output);
    }

    private sealed class EncoderTree : IDisposable
    {
        private readonly string _sourceDirectory;
        private readonly Dictionary<string, string> _packagedHashes =
            new(StringComparer.OrdinalIgnoreCase);

        public EncoderTree()
        {
            Root = Path.Combine(
                Path.GetTempPath(), "cuetools-encoder-trust-" + Guid.NewGuid().ToString("N"));
            _sourceDirectory = Path.Combine(Root, "source");
            ManagedDirectory = Path.Combine(Root, "managed");
            BundledDirectory = Path.Combine(Root, "bundled");
            Directory.CreateDirectory(_sourceDirectory);
            App = new AppSettings();
            Log = new FakeLog();
            Catalog = new EncoderCatalog(
                Log,
                App,
                ManagedDirectory,
                BundledDirectory,
                _packagedHashes);
            Config = new CUEConfig();
            var settings = new CUETools.Codecs.CommandLine.EncoderSettings(
                "testenc.exe",
                "test",
                true,
                "1",
                "1",
                "testenc.exe",
                "- %O");
            settings.VerificationUsesEncoder = true;
            settings.VerificationParameters = "-d %I -";
            Config.advanced.encoders.Add(settings);
            Encoder = new AudioEncoderSettingsViewModel(settings);
            Config.Encoders.Add(Encoder);
            Info = new ExternalEncoderInfo
            {
                EncoderName = settings.Name,
                Extension = settings.Extension,
                FormatName = "Test",
                Lossless = true,
                ExeName = "testenc.exe",
            };
        }

        public string ManagedDirectory { get; }
        public string BundledDirectory { get; }
        public string Root { get; }
        public AppSettings App { get; }
        public FakeLog Log { get; }
        public EncoderCatalog Catalog { get; }
        public CUEConfig Config { get; }
        public AudioEncoderSettingsViewModel Encoder { get; }
        public ExternalEncoderInfo Info { get; }

        public string WriteSource(byte[] bytes)
        {
            return WriteSource(bytes, Info.ExeName);
        }

        public string WriteSource(byte[] bytes, string fileName)
        {
            string path = Path.Combine(_sourceDirectory, fileName);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public void ApprovePackaged(string path)
        {
            _packagedHashes[Path.GetFileName(path)] =
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}

[TestClass]
public sealed class IcecastTransportPolicyTests
{
    [TestMethod]
    public void DefaultEndpointsUseHttpsAndShareAuthority()
    {
        var settings = NewSettings();

        Uri source = IcecastEndpointPolicy.BuildSourceUri(settings);
        Uri metadata = IcecastEndpointPolicy.BuildMetadataUri(
            settings, "Artist & Guest", "Title/Part");

        Assert.AreEqual(Uri.UriSchemeHttps, source.Scheme);
        Assert.AreEqual(source.Authority, metadata.Authority);
        Assert.AreEqual("/live", source.AbsolutePath);
        Assert.AreEqual("/admin/metadata", metadata.AbsolutePath);
        Assert.IsTrue(metadata.Query.Contains("mount=%2Flive", StringComparison.Ordinal));
        Assert.IsTrue(
            metadata.Query.Contains("song=Artist%20%26%20Guest%20-%20Title%2FPart",
                StringComparison.Ordinal));
        Assert.IsFalse(settings.AllowInsecureHttp);
    }

    [TestMethod]
    public void HttpRequiresExplicitLegacyOptInForBothEndpoints()
    {
        var settings = NewSettings();
        settings.AllowInsecureHttp = true;

        Assert.AreEqual(
            Uri.UriSchemeHttp,
            IcecastEndpointPolicy.BuildSourceUri(settings).Scheme);
        Assert.AreEqual(
            Uri.UriSchemeHttp,
            IcecastEndpointPolicy.BuildMetadataUri(settings, "", "Track").Scheme);
        Assert.ThrowsException<InvalidOperationException>(() =>
            IcecastEndpointPolicy.EnsureCredentialTransport(
                new Uri("http://radio.example.test:8000/live"),
                allowInsecureHttp: false));
    }

    [TestMethod]
    public void EndpointBuilderRejectsAuthorityAndQueryInjection()
    {
        var settings = NewSettings();
        settings.Server = "https://attacker.example";
        Assert.ThrowsException<FormatException>(
            () => IcecastEndpointPolicy.BuildSourceUri(settings));

        settings = NewSettings();
        settings.Mount = "/live?redirect=attacker";
        Assert.ThrowsException<FormatException>(
            () => IcecastEndpointPolicy.BuildSourceUri(settings));

        settings = NewSettings();
        settings.Port = "0";
        Assert.ThrowsException<FormatException>(
            () => IcecastEndpointPolicy.BuildSourceUri(settings));
    }

    [TestMethod]
    public void LegacyUiContainsExplicitInsecureTransportWarning()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(repoRoot);
        string designer = File.ReadAllText(
            Path.Combine(repoRoot, "CUEPlayer", "IcecastSettings.Designer.cs"));
        string form = File.ReadAllText(
            Path.Combine(repoRoot, "CUEPlayer", "IcecastSettings.cs"));
        string writer = File.ReadAllText(
            Path.Combine(repoRoot, "CUETools.Codecs.Icecast", "IcecastWriter.cs"));

        Assert.IsTrue(designer.Contains(
            "Allow insecure HTTP for a legacy Icecast server",
            StringComparison.Ordinal));
        Assert.IsTrue(designer.Contains(
            "password and metadata credential will cross the network without encryption",
            StringComparison.Ordinal));
        Assert.IsTrue(form.Contains(
            "_data.AllowInsecureHttp = checkBoxAllowInsecureHttp.Checked",
            StringComparison.Ordinal));
        Assert.IsTrue(writer.Contains(
            "IcecastEndpointPolicy.BuildSourceUri(settings)",
            StringComparison.Ordinal));
        Assert.IsTrue(writer.Contains(
            "IcecastEndpointPolicy.BuildMetadataUri(settings, artist, title)",
            StringComparison.Ordinal));
        Assert.IsFalse(writer.Contains("\"http://\"", StringComparison.Ordinal));
        Assert.IsFalse(writer.Contains("throw ex;", StringComparison.Ordinal));
    }

    private static IcecastSettingsData NewSettings() => new()
    {
        Server = "radio.example.test",
        Port = "8443",
        Mount = "live",
        Password = "not-a-real-secret",
    };
}
