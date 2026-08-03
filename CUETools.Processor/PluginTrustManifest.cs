using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CUETools.Processor
{
    /// <summary>
    /// Raised when a packaged plugin manifest or one of the files it binds fails validation.
    /// This is an integrity failure, so callers must not turn it into a best-effort plugin skip.
    /// </summary>
    public sealed class PluginTrustException : IOException
    {
        public PluginTrustException(string message) : base(message) { }
    }

    public sealed class ApprovedPlugin
    {
        internal ApprovedPlugin(string relativePath, string fullPath, string sha256)
        {
            RelativePath = relativePath;
            FullPath = fullPath;
            Sha256 = sha256;
        }

        public string RelativePath { get; private set; }
        public string FullPath { get; private set; }
        public string Sha256 { get; private set; }
    }

    /// <summary>
    /// Validates the deterministic hash allowlist emitted beside packaged plugins.
    /// The manifest detects missing, substituted, and unexpected-to-the-build bytes. It is not a
    /// publisher signature: a principal that can replace both the manifest and plugin directory
    /// can approve different bytes.
    /// </summary>
    public static class PluginTrustManifest
    {
        public const string ManifestFileName = "CUETools.PluginManifest.v1";
        public const string LocalDevelopmentEnvironmentVariable =
            "CUETOOLS_ALLOW_UNMANIFESTED_PLUGINS";
        public const string UserPluginDirectoryName = "plugins";

        private const long MaximumManifestBytes = 64 * 1024;
        private const int MaximumEntries = 128;
        private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
        private const uint LoadLibrarySearchDefaultDirs = 0x00001000;
        private const uint LoadWithAlteredSearchPath = 0x00000008;
        private const int ErrorInvalidParameter = 87;

        private static readonly string[,] NativePluginDependencies =
        {
            { "CUETools.Compression.Rar.dll", "unrar.dll" },
            { "CUETools.Codecs.HDCD.dll", "hdcd.dll" },
            { "CUETools.Codecs.libFLAC.dll", "libFLAC_dynamic.dll" },
            { "CUETools.Codecs.libmp3lame.dll", "libmp3lame.dll" },
            { "CUETools.Codecs.libwavpack.dll", "wavpackdll.dll" },
            { "CUETools.Codecs.MACLib.dll", "MACLibDll.dll" },
        };

        // Native plugin modules deliberately remain loaded for the process lifetime. Releasing
        // these handles would let a later same-name P/Invoke bind a different module.
        private static readonly List<IntPtr> ApprovedNativeModuleHandles =
            new List<IntPtr>();

        public static bool IsLocalDevelopmentModeEnabled()
        {
            return string.Equals(
                Environment.GetEnvironmentVariable(LocalDevelopmentEnvironmentVariable),
                "1",
                StringComparison.Ordinal);
        }

        /// <summary>
        /// User-installed plugins live outside the packaged manifest in a per-user trust zone.
        /// That directory has its own exact hash manifest, so extending CUETools does not require
        /// weakening or rewriting the build-produced package allowlist.
        /// </summary>
        public static string GetUserPluginDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "CUETools2026",
                UserPluginDirectoryName);
        }

        public static string GetRuntimeArchitecture()
        {
            if (Type.GetType("Mono.Runtime", false) != null)
                return "mono";
            return Marshal.SizeOf(typeof(IntPtr)) == 8 ? "x64" : "win32";
        }

        /// <summary>
        /// Top-level managed plugins apply to every runtime. A plugin under an architecture
        /// directory applies only to that exact runtime; this keeps the x86 and x64 C++/CLI TTA
        /// assemblies in one package without ever attempting to load the wrong one.
        /// </summary>
        public static bool IsForRuntimeArchitecture(
            ApprovedPlugin plugin,
            string runtimeArchitecture)
        {
            if (plugin == null)
                throw new ArgumentNullException("plugin");
            if (runtimeArchitecture != "x64" &&
                runtimeArchitecture != "win32" &&
                runtimeArchitecture != "mono")
                throw new ArgumentException("Unknown plugin runtime architecture.", "runtimeArchitecture");

            int slash = plugin.RelativePath.IndexOf('/');
            if (slash < 0)
                return true;
            return slash == runtimeArchitecture.Length &&
                plugin.RelativePath.StartsWith(
                    runtimeArchitecture + "/", StringComparison.OrdinalIgnoreCase) &&
                plugin.RelativePath.IndexOf('/', slash + 1) < 0;
        }

        /// <summary>
        /// The manifest binds every runtime DLL in the package, including native codec
        /// dependencies. Only CUETools.*.dll records are managed plugin entry points.
        /// </summary>
        public static bool IsLoadableManagedPlugin(ApprovedPlugin plugin)
        {
            if (plugin == null)
                throw new ArgumentNullException("plugin");

            string fileName = Path.GetFileName(plugin.RelativePath);
            return fileName.StartsWith("CUETools.", StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }

        public static IList<ApprovedPlugin> ReadApprovedPlugins(string pluginDirectory)
        {
            if (string.IsNullOrWhiteSpace(pluginDirectory))
                throw new ArgumentException("A plugin directory is required.", "pluginDirectory");

            string root = Path.GetFullPath(pluginDirectory);
            EnsureDirectoryIsNotReparsePoint(root, "plugin directory");

            string manifestPath = Path.Combine(root, ManifestFileName);
            EnsureRegularFile(manifestPath, "plugin manifest");
            var manifestInfo = new FileInfo(manifestPath);
            if (manifestInfo.Length > MaximumManifestBytes)
                throw new PluginTrustException("The plugin manifest exceeds the size limit.");

            string[] lines = ReadManifestLines(manifestPath);

            if (lines.Length == 0 || lines.Length > MaximumEntries)
                throw new PluginTrustException("The plugin manifest has an invalid entry count.");

            string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenExact = new HashSet<string>(StringComparer.Ordinal);
            var approved = new List<ApprovedPlugin>(lines.Length);
            string previousRelativePath = null;

            foreach (string line in lines)
            {
                string[] fields = line.Split('\t');
                if (fields.Length != 2 || !IsSha256(fields[0]))
                    throw new PluginTrustException("The plugin manifest contains an invalid entry.");

                string relativePath = fields[1];
                ValidateRelativePluginPath(relativePath);
                if (previousRelativePath != null &&
                    StringComparer.Ordinal.Compare(previousRelativePath, relativePath) >= 0)
                    throw new PluginTrustException(
                        "The plugin manifest paths are not in strict ordinal order.");
                previousRelativePath = relativePath;

                if (!seenAliases.Add(relativePath) || !seenExact.Add(relativePath))
                    throw new PluginTrustException("The plugin manifest contains a duplicate path.");

                string platformRelativePath =
                    relativePath.Replace('/', Path.DirectorySeparatorChar);
                string fullPath = Path.GetFullPath(Path.Combine(root, platformRelativePath));
                if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new PluginTrustException("A plugin manifest path escapes the plugin directory.");

                EnsurePathComponentsAreNotReparsePoints(root, platformRelativePath);
                EnsureRegularFile(fullPath, "packaged plugin");

                string actualHash = ComputeSha256(fullPath);
                if (!string.Equals(actualHash, fields[0], StringComparison.OrdinalIgnoreCase))
                    throw new PluginTrustException(
                        "A packaged plugin does not match its manifest hash: " + relativePath);

                approved.Add(new ApprovedPlugin(relativePath, fullPath, fields[0].ToUpperInvariant()));
            }

            HashSet<string> packagedDlls = EnumeratePackagedRuntimeDlls(root);
            if (packagedDlls.Count != seenExact.Count || !packagedDlls.SetEquals(seenExact))
                throw new PluginTrustException(
                    "The plugin manifest does not exactly describe the packaged runtime DLLs.");

            return approved;
        }

        internal static Assembly LoadApprovedAssembly(ApprovedPlugin plugin)
        {
            if (plugin == null)
                throw new ArgumentNullException("plugin");

            EnsureRegularFile(plugin.FullPath, "packaged plugin");

            // Deny write/delete sharing while hashing and asking the loader to open the same path.
            // This closes the practical replacement window on Windows, the supported desktop
            // runtime. The manifest still is not a signature or a defense against a writer that
            // controls the entire install tree.
            using (var stream = new FileStream(
                plugin.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                string actualHash = ComputeSha256(stream);
                if (!string.Equals(actualHash, plugin.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new PluginTrustException(
                        "A packaged plugin changed after manifest validation: " + plugin.RelativePath);

                try
                {
                    Assembly assembly = Assembly.LoadFrom(plugin.FullPath);
                    EnsureLoadedAssemblyMatchesApprovedPlugin(assembly, plugin);
                    return assembly;
                }
                catch (PluginTrustException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new PluginTrustException(
                        "An approved packaged plugin could not be loaded: " +
                        plugin.RelativePath + " (" + ex.GetType().Name + ").");
                }
            }
        }

        /// <summary>
        /// Loads the native dependency paired with each approved managed codec before any codec
        /// type is instantiated. The approved file remains non-writable/non-replaceable while
        /// it is hashed and passed to the Windows loader. The returned module path is then
        /// checked because Windows can otherwise satisfy a full-path LoadLibrary request with
        /// an already-loaded same-name module from another directory.
        /// </summary>
        internal static void PreloadApprovedNativeDependencies(
            IList<ApprovedPlugin> plugins,
            string runtimeArchitecture)
        {
            if (plugins == null)
                throw new ArgumentNullException("plugins");

            // These wrappers are Windows P/Invoke plugins. Mono and non-Windows consumers retain
            // their existing best-effort registration behavior rather than invoking kernel32.
            if (runtimeArchitecture == "mono" || Path.DirectorySeparatorChar != '\\')
                return;

            IList<ApprovedPlugin> dependencies =
                GetRequiredNativeDependencies(plugins, runtimeArchitecture);
            foreach (ApprovedPlugin dependency in dependencies)
            {
                IntPtr module = LoadApprovedNativeDependency(
                    dependency,
                    LoadNativeLibrary,
                    GetNativeModulePath,
                    handle => NativeMethods.FreeLibrary(handle));
                try
                {
                    CUETools.Codecs.NativeDependencyPathRegistry.
                        RegisterManifestApprovedPath(
                            Path.GetFileName(dependency.FullPath),
                            dependency.FullPath);
                    ApprovedNativeModuleHandles.Add(module);
                }
                catch (Exception ex)
                {
                    NativeMethods.FreeLibrary(module);
                    throw new PluginTrustException(
                        "An approved native dependency could not be bound to its managed " +
                        "wrapper: " + dependency.RelativePath + " (" +
                        ex.GetType().Name + ").");
                }
            }
        }

        internal static IList<ApprovedPlugin> GetRequiredNativeDependencies(
            IList<ApprovedPlugin> plugins,
            string runtimeArchitecture)
        {
            if (plugins == null)
                throw new ArgumentNullException("plugins");
            if (runtimeArchitecture != "x64" &&
                runtimeArchitecture != "win32" &&
                runtimeArchitecture != "mono")
                throw new ArgumentException(
                    "Unknown plugin runtime architecture.", "runtimeArchitecture");

            var required = new List<ApprovedPlugin>();
            if (runtimeArchitecture == "mono")
                return required;

            for (int dependencyIndex = 0;
                dependencyIndex < NativePluginDependencies.GetLength(0);
                dependencyIndex++)
            {
                string managedFileName =
                    NativePluginDependencies[dependencyIndex, 0];
                string nativeFileName =
                    NativePluginDependencies[dependencyIndex, 1];
                bool managedPluginIsPresent = false;
                foreach (ApprovedPlugin plugin in plugins)
                {
                    if (IsForRuntimeArchitecture(plugin, runtimeArchitecture) &&
                        string.Equals(
                            Path.GetFileName(plugin.RelativePath),
                            managedFileName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        managedPluginIsPresent = true;
                        break;
                    }
                }

                if (!managedPluginIsPresent)
                    continue;

                string requiredRelativePath =
                    runtimeArchitecture + "/" + nativeFileName;
                ApprovedPlugin dependency = null;
                foreach (ApprovedPlugin plugin in plugins)
                {
                    if (string.Equals(
                        plugin.RelativePath,
                        requiredRelativePath,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        dependency = plugin;
                        break;
                    }
                }

                if (dependency == null)
                    throw new PluginTrustException(
                        "An approved managed plugin is missing its architecture-specific " +
                        "native dependency: " + requiredRelativePath);
                required.Add(dependency);
            }

            return required;
        }

        internal static IntPtr LoadApprovedNativeDependency(
            ApprovedPlugin plugin,
            Func<string, IntPtr> loadLibrary,
            Func<IntPtr, string> getModulePath,
            Action<IntPtr> releaseLibrary)
        {
            if (plugin == null)
                throw new ArgumentNullException("plugin");
            if (loadLibrary == null)
                throw new ArgumentNullException("loadLibrary");
            if (getModulePath == null)
                throw new ArgumentNullException("getModulePath");
            if (releaseLibrary == null)
                throw new ArgumentNullException("releaseLibrary");

            EnsureRegularFile(plugin.FullPath, "packaged native dependency");
            IntPtr module = IntPtr.Zero;
            try
            {
                // FileShare.Read denies write/delete sharing until the loader has acquired and
                // mapped this path. Hashing and loading therefore describe one stable file.
                using (var stream = new FileStream(
                    plugin.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    string actualHash = ComputeSha256(stream);
                    if (!string.Equals(
                        actualHash,
                        plugin.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                        throw new PluginTrustException(
                            "A native dependency changed after manifest validation: " +
                            plugin.RelativePath);

                    module = loadLibrary(plugin.FullPath);
                    if (module == IntPtr.Zero)
                        throw new PluginTrustException(
                            "An approved native dependency could not be loaded: " +
                            plugin.RelativePath);

                    string loadedPath = getModulePath(module);
                    if (string.IsNullOrEmpty(loadedPath) ||
                        !PathsReferToSameLocation(plugin.FullPath, loadedPath))
                        throw new PluginTrustException(
                            "The runtime resolved an approved native dependency from a " +
                            "different location: " + plugin.RelativePath);
                }

                return module;
            }
            catch (PluginTrustException)
            {
                if (module != IntPtr.Zero)
                    releaseLibrary(module);
                throw;
            }
            catch (Exception ex)
            {
                if (module != IntPtr.Zero)
                    releaseLibrary(module);
                throw new PluginTrustException(
                    "An approved native dependency could not be verified: " +
                    plugin.RelativePath + " (" + ex.GetType().Name + ").");
            }
        }

        private static IntPtr LoadNativeLibrary(string path)
        {
            IntPtr module = NativeMethods.LoadLibraryEx(
                path,
                IntPtr.Zero,
                LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
            if (module != IntPtr.Zero)
                return module;

            // LOAD_LIBRARY_SEARCH_* is unavailable on unpatched Windows 7. Preserve the net47
            // application's legacy compatibility there while still binding the target module's
            // path and bytes. Modern WPF runs use the restricted dependency search above.
            if (Marshal.GetLastWin32Error() == ErrorInvalidParameter)
                module = NativeMethods.LoadLibraryEx(
                    path, IntPtr.Zero, LoadWithAlteredSearchPath);
            return module;
        }

        private static string GetNativeModulePath(IntPtr module)
        {
            var path = new StringBuilder(32768);
            uint length = NativeMethods.GetModuleFileName(
                module, path, path.Capacity);
            if (length == 0 || length >= path.Capacity)
                return null;
            return path.ToString();
        }

        private static bool PathsReferToSameLocation(string expected, string actual)
        {
            string expectedPath = NormalizeWindowsExtendedPath(
                Path.GetFullPath(expected));
            string actualPath = NormalizeWindowsExtendedPath(
                Path.GetFullPath(actual));
            return string.Equals(
                expectedPath, actualPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeWindowsExtendedPath(string path)
        {
            const string ExtendedUncPrefix = @"\\?\UNC\";
            const string ExtendedPrefix = @"\\?\";
            if (path.StartsWith(ExtendedUncPrefix, StringComparison.OrdinalIgnoreCase))
                return @"\\" + path.Substring(ExtendedUncPrefix.Length);
            if (path.StartsWith(ExtendedPrefix, StringComparison.OrdinalIgnoreCase))
                return path.Substring(ExtendedPrefix.Length);
            return path;
        }

        private static class NativeMethods
        {
            [DllImport(
                "kernel32.dll",
                CharSet = CharSet.Unicode,
                SetLastError = true,
                EntryPoint = "LoadLibraryExW")]
            internal static extern IntPtr LoadLibraryEx(
                string fileName,
                IntPtr file,
                uint flags);

            [DllImport(
                "kernel32.dll",
                CharSet = CharSet.Unicode,
                SetLastError = true,
                EntryPoint = "GetModuleFileNameW")]
            internal static extern uint GetModuleFileName(
                IntPtr module,
                StringBuilder fileName,
                int size);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool FreeLibrary(IntPtr module);
        }

        internal static void EnsureLoadedAssemblyMatchesApprovedPlugin(
            Assembly assembly,
            ApprovedPlugin plugin)
        {
            string loadedPath;
            try
            {
                if (string.IsNullOrEmpty(assembly.Location))
                    throw new PluginTrustException(
                        "An approved packaged plugin loaded without a verifiable location: " +
                        plugin.RelativePath);
                loadedPath = Path.GetFullPath(assembly.Location);
            }
            catch (PluginTrustException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PluginTrustException(
                    "The loaded plugin location could not be verified: " +
                    plugin.RelativePath + " (" + ex.GetType().Name + ").");
            }

            StringComparison pathComparison =
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            string approvedPath = Path.GetFullPath(plugin.FullPath);
            if (!string.Equals(approvedPath, loadedPath, pathComparison) &&
                !IsApprovedApplicationRootDuplicate(
                    approvedPath,
                    loadedPath,
                    pathComparison))
                throw new PluginTrustException(
                    "The runtime resolved an approved packaged plugin from a different location: " +
                    plugin.RelativePath);

            EnsureRegularFile(loadedPath, "loaded packaged plugin");
            string loadedHash;
            try
            {
                loadedHash = ComputeSha256(loadedPath);
            }
            catch (Exception ex)
            {
                throw new PluginTrustException(
                    "The loaded plugin bytes could not be verified: " +
                    plugin.RelativePath + " (" + ex.GetType().Name + ").");
            }

            if (!string.Equals(
                loadedHash,
                plugin.Sha256,
                StringComparison.OrdinalIgnoreCase))
                throw new PluginTrustException(
                    "The loaded plugin bytes do not match the approved manifest hash: " +
                    plugin.RelativePath);
        }

        private static bool IsApprovedApplicationRootDuplicate(
            string approvedPath,
            string loadedPath,
            StringComparison pathComparison)
        {
            string pluginDirectory = Path.GetDirectoryName(approvedPath);
            if (string.IsNullOrEmpty(pluginDirectory) ||
                !string.Equals(
                    Path.GetFileName(pluginDirectory),
                    "plugins",
                    pathComparison))
                return false;

            string applicationDirectory = Path.GetDirectoryName(pluginDirectory);
            if (string.IsNullOrEmpty(applicationDirectory))
                return false;

            // SDK-style project references put a second copy in the app root before the
            // plugin registrar runs. Assembly.LoadFrom then returns that already-loaded
            // assembly. Accept only that exact sibling layout; the hash check below still
            // requires the loaded bytes to match the approved plugins\ copy.
            string expectedRootCopy = Path.Combine(
                applicationDirectory,
                Path.GetFileName(approvedPath));
            return string.Equals(
                Path.GetFullPath(expectedRootCopy),
                loadedPath,
                pathComparison);
        }

        private static void ValidateRelativePluginPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) ||
                Path.IsPathRooted(relativePath) ||
                relativePath.IndexOf('\\') >= 0)
                throw new PluginTrustException("The plugin manifest contains an invalid path.");

            string[] segments = relativePath.Split('/');
            if (segments.Length < 1 || segments.Length > 2)
                throw new PluginTrustException("The plugin manifest contains an invalid path.");
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment.IndexOfAny(new[] { ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                    throw new PluginTrustException("The plugin manifest contains an invalid path.");
            }

            if (segments.Length == 2 &&
                segments[0] != "mono" &&
                segments[0] != "win32" &&
                segments[0] != "x64")
                throw new PluginTrustException(
                    "The plugin manifest contains an unknown runtime directory.");

            string fileName = segments[segments.Length - 1];
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
                throw new PluginTrustException("The plugin manifest contains a non-runtime file.");
        }

        private static string[] ReadManifestLines(string manifestPath)
        {
            try
            {
                byte[] bytes;
                using (var stream = new FileStream(
                    manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length <= 0 || stream.Length > MaximumManifestBytes)
                        throw new PluginTrustException(
                            "The plugin manifest has an invalid byte length.");

                    bytes = new byte[(int)stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0)
                            throw new EndOfStreamException();
                        offset += read;
                    }
                }

                if (bytes.Length >= 3 &&
                    bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
                    throw new PluginTrustException(
                        "The plugin manifest must be UTF-8 without a byte-order mark.");

                string text = new UTF8Encoding(false, true).GetString(bytes);
                var lines = new List<string>();
                using (var reader = new StringReader(text))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        lines.Add(line);
                }
                return lines.ToArray();
            }
            catch (PluginTrustException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PluginTrustException(
                    "The plugin manifest could not be read (" + ex.GetType().Name + ").");
            }
        }

        private static HashSet<string> EnumeratePackagedRuntimeDlls(string root)
        {
            var candidates = new HashSet<string>(StringComparer.Ordinal);
            AddRuntimeDllCandidates(root, null, candidates);
            AddRuntimeDllCandidates(root, "mono", candidates);
            AddRuntimeDllCandidates(root, "win32", candidates);
            AddRuntimeDllCandidates(root, "x64", candidates);
            return candidates;
        }

        private static void AddRuntimeDllCandidates(
            string root,
            string architecture,
            HashSet<string> candidates)
        {
            string directory = architecture == null
                ? root
                : Path.Combine(root, architecture);
            if (!Directory.Exists(directory))
                return;

            EnsureDirectoryIsNotReparsePoint(
                directory,
                architecture == null ? "plugin directory" : "plugin architecture directory");

            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                throw new PluginTrustException(
                    "The packaged runtime DLLs could not be enumerated (" +
                    ex.GetType().Name + ").");
            }

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                EnsureRegularFile(file, "packaged runtime DLL");
                string relativePath = architecture == null
                    ? fileName
                    : architecture + "/" + fileName;
                if (!candidates.Add(relativePath))
                    throw new PluginTrustException(
                        "The packaged runtime DLL set contains a duplicate path.");
            }
        }

        private static void EnsurePathComponentsAreNotReparsePoints(
            string root,
            string platformRelativePath)
        {
            string current = root;
            string[] segments = platformRelativePath.Split(Path.DirectorySeparatorChar);
            for (int i = 0; i < segments.Length - 1; i++)
            {
                current = Path.Combine(current, segments[i]);
                EnsureDirectoryIsNotReparsePoint(current, "plugin path");
            }
        }

        private static void EnsureDirectoryIsNotReparsePoint(string path, string description)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (Exception ex)
            {
                throw new PluginTrustException(
                    "The " + description + " is unavailable (" + ex.GetType().Name + ").");
            }

            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
                throw new PluginTrustException(
                    "The " + description + " must be a regular directory.");
        }

        private static void EnsureRegularFile(string path, string description)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (Exception ex)
            {
                throw new PluginTrustException(
                    "The " + description + " is unavailable (" + ex.GetType().Name + ").");
            }

            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new PluginTrustException("The " + description + " must be a regular file.");
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') ||
                      (c >= 'a' && c <= 'f') ||
                      (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return ComputeSha256(stream);
        }

        private static string ComputeSha256(Stream stream)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                char[] result = new char[hash.Length * 2];
                const string Hex = "0123456789ABCDEF";
                for (int i = 0; i < hash.Length; i++)
                {
                    result[i * 2] = Hex[hash[i] >> 4];
                    result[i * 2 + 1] = Hex[hash[i] & 0x0f];
                }
                return new string(result);
            }
        }
    }
}
