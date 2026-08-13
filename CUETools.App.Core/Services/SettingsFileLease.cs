using System;
using System.IO;
using System.Threading;

namespace CUETools.Wpf.Services;

/// <summary>
/// Serializes settings publication across explicitly launched drive windows and any manually
/// started app instances. SettingsWriter already publishes atomically; this lock prevents two
/// writers from replacing the same destination at once.
/// </summary>
internal sealed class SettingsFileLease : IDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);
    private readonly FileStream _stream;

    private SettingsFileLease(FileStream stream) => _stream = stream;

    internal static SettingsFileLease Acquire(string settingsPath)
    {
        string lockPath = GetLockPath(settingsPath);
        string? directory = Path.GetDirectoryName(lockPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new IOException("Settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        DateTime deadline = DateTime.UtcNow + WaitTimeout;
        IOException? lastSharingFailure = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                return new SettingsFileLease(
                    new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None));
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                lastSharingFailure = ex;
                Thread.Sleep(25);
            }
        }

        throw new TimeoutException(
            "Timed out waiting for another CUETools window to save settings.",
            lastSharingFailure);
    }

    internal static string GetLockPath(string settingsPath) =>
        Path.GetFullPath(settingsPath) + ".lock";

    private static bool IsSharingViolation(IOException ex)
    {
        int code = ex.HResult & 0xffff;
        return code == 32 || code == 33;
    }

    public void Dispose() => _stream.Dispose();
}
