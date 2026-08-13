using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CUETools.Codecs
{
    /// <summary>
    /// Joins the host's manifest-approved native path to a managed codec wrapper that may have
    /// been unified with an application-root assembly copy. Packaged hosts register a path only
    /// after hashing, loading, and confirming the exact Windows module location. Classic hosts
    /// retain the historical assembly-relative architecture directory.
    /// </summary>
    public static class NativeDependencyPathRegistry
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> ApprovedPaths =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registration entry for packaged non-plugin hosts (the Linux head).
        /// The host owns the same obligations PluginTrustManifest fulfills on
        /// Windows: hash-validate the file against its packaged manifest
        /// BEFORE registering, and never register an app-root, PATH, or
        /// bare-name guess. Same single-binding rule as the manifest path.
        /// </summary>
        public static void RegisterHostValidatedPath(
            string nativeFileName,
            string approvedFullPath)
        {
            RegisterManifestApprovedPath(nativeFileName, approvedFullPath);
        }

        internal static void RegisterManifestApprovedPath(
            string nativeFileName,
            string approvedFullPath)
        {
            ValidateSimpleFileName(nativeFileName);
            if (String.IsNullOrEmpty(approvedFullPath) ||
                !Path.IsPathRooted(approvedFullPath))
                throw new ArgumentException(
                    "The approved native dependency path must be absolute.",
                    "approvedFullPath");

            string fullPath = Path.GetFullPath(approvedFullPath);
            if (!String.Equals(
                    Path.GetFileName(fullPath),
                    nativeFileName,
                    StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    "The approved native dependency name does not match its path.",
                    "approvedFullPath");
            if (!File.Exists(fullPath))
                throw new FileNotFoundException(
                    "The approved native dependency no longer exists.",
                    nativeFileName);

            lock (Sync)
            {
                string current;
                if (ApprovedPaths.TryGetValue(nativeFileName, out current))
                {
                    if (!String.Equals(
                            current,
                            fullPath,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "A different approved path is already bound for " +
                            nativeFileName + ".");
                    return;
                }
                ApprovedPaths.Add(nativeFileName, fullPath);
            }
        }

        /// <summary>
        /// Returns the manifest-approved full path when a packaged host supplied one. Otherwise
        /// returns exactly the wrapper assembly's architecture subdirectory path. It never searches
        /// the application root, the current directory, or PATH.
        /// </summary>
        public static string ResolvePath(
            Assembly wrapperAssembly,
            string nativeFileName)
        {
            if (wrapperAssembly == null)
                throw new ArgumentNullException("wrapperAssembly");
            ValidateSimpleFileName(nativeFileName);

            lock (Sync)
            {
                string approved;
                if (ApprovedPaths.TryGetValue(nativeFileName, out approved))
                    return approved;
            }

            string assemblyPath = wrapperAssembly.Location;
            if (String.IsNullOrEmpty(assemblyPath))
                throw new DllNotFoundException(
                    "The managed codec assembly location is unavailable for " +
                    nativeFileName + ".");
            string assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            if (String.IsNullOrEmpty(assemblyDirectory))
                throw new DllNotFoundException(
                    "The managed codec directory is unavailable for " +
                    nativeFileName + ".");

            string architectureDirectory = IntPtr.Size == 8 ? "x64" : "win32";
            return Path.GetFullPath(
                Path.Combine(
                    Path.Combine(assemblyDirectory, architectureDirectory),
                    nativeFileName));
        }

        private static void ValidateSimpleFileName(string nativeFileName)
        {
            if (String.IsNullOrEmpty(nativeFileName) ||
                !String.Equals(
                    nativeFileName,
                    Path.GetFileName(nativeFileName),
                    StringComparison.Ordinal) ||
                (!nativeFileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                 !nativeFileName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException(
                    "The native dependency name must be one .dll or .so file name.",
                    "nativeFileName");
        }
    }
}
