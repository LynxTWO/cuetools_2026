using System;
using System.Runtime.InteropServices;

namespace CUETools.Codecs
{
    /// <summary>
    /// Cross-platform preload for the native codec wrappers' fail-fast static
    /// constructors: kernel32 on Windows, libdl elsewhere. Only the exact
    /// path resolved through <see cref="NativeDependencyPathRegistry"/> is
    /// ever loaded; this helper adds no search of its own.
    /// </summary>
    internal static class NativePreload
    {
        internal static bool IsWindows
        {
            get
            {
                PlatformID platform = Environment.OSVersion.Platform;
                return platform != PlatformID.Unix && platform != PlatformID.MacOSX;
            }
        }

        internal static string SharedLibrarySuffix
        {
            get { return IsWindows ? ".dll" : ".so"; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

        private const int RTLD_NOW = 2;

        [DllImport("libdl.so.2", EntryPoint = "dlopen")]
        private static extern IntPtr DlOpen(string fileName, int flags);

        [DllImport("libdl.so.2", EntryPoint = "dlsym")]
        private static extern IntPtr DlSym(IntPtr handle, string symbol);

        internal static IntPtr Load(string path)
        {
            return IsWindows ? LoadLibrary(path) : DlOpen(path, RTLD_NOW);
        }

        internal static IntPtr GetSymbol(IntPtr module, string name)
        {
            return IsWindows ? GetProcAddress(module, name) : DlSym(module, name);
        }
    }
}
