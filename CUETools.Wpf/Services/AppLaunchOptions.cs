using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CUETools.Wpf.Services;

/// <summary>
/// Non-sensitive startup contract for an explicitly launched drive window. Only a drive
/// letter and the secondary-window role cross the process boundary; album metadata, output
/// paths, credentials, and encoder arguments never appear in a command line.
/// </summary>
public sealed class AppLaunchOptions
{
    internal char PreferredDrive { get; private init; }
    internal bool IsSecondaryDriveWindow { get; private init; }

    internal static AppLaunchOptions Parse(IReadOnlyList<string> args)
    {
        char drive = '\0';
        bool secondary = false;
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i] ?? "";
            if (string.Equals(
                    arg,
                    "--secondary-drive-window",
                    StringComparison.OrdinalIgnoreCase))
            {
                secondary = true;
                continue;
            }

            string value = "";
            if (arg.StartsWith("--drive=", StringComparison.OrdinalIgnoreCase))
                value = arg.Substring("--drive=".Length);
            else if (string.Equals(
                         arg,
                         "--drive",
                         StringComparison.OrdinalIgnoreCase) &&
                     i + 1 < args.Count)
                value = args[++i] ?? "";

            value = value.Trim().TrimEnd(':');
            if (value.Length == 1 && IsDriveLetter(value[0]))
                drive = char.ToUpperInvariant(value[0]);
        }

        return new AppLaunchOptions
        {
            PreferredDrive = drive,
            // A secondary role without an exact hardware selector would silently fall
            // back to the first enumerated drive and also suppress settings publication.
            IsSecondaryDriveWindow = secondary && drive != '\0',
        };
    }

    private static bool IsDriveLetter(char value)
    {
        char normalized = char.ToUpperInvariant(value);
        return normalized >= 'A' && normalized <= 'Z';
    }
}

/// <summary>Builds the same-executable launch without shell-assembled arguments.</summary>
internal static class DriveWindowLauncher
{
    internal static ProcessStartInfo Create(
        char drive,
        string? processPath = null,
        string? assemblyPath = null)
    {
        char normalized = char.ToUpperInvariant(drive);
        if (normalized < 'A' || normalized > 'Z')
            throw new ArgumentOutOfRangeException(nameof(drive));

        string executable = string.IsNullOrWhiteSpace(processPath)
            ? Environment.ProcessPath ?? ""
            : processPath;
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException(
                "The CUETools executable path is unavailable.");

        // `dotnet CUETools.Wpf.dll` is used by developer runs; published builds are native
        // apphosts and need no assembly argument.
        bool dotnetHost = string.Equals(
            Path.GetFileNameWithoutExtension(executable),
            "dotnet",
            StringComparison.OrdinalIgnoreCase);
        string dll = dotnetHost
            ? string.IsNullOrWhiteSpace(assemblyPath)
                ? typeof(AppLaunchOptions).Assembly.Location
                : assemblyPath
            : "";
        string workingPath = dotnetHost ? dll : executable;
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            // Developer launches use the assembly directory, not the global dotnet host
            // directory. Relative plugin and asset resolution then matches the apphost build.
            WorkingDirectory =
                Path.GetDirectoryName(Path.GetFullPath(workingPath)) ??
                Environment.CurrentDirectory,
        };

        if (dotnetHost)
            start.ArgumentList.Add(dll);
        start.ArgumentList.Add("--secondary-drive-window");
        start.ArgumentList.Add("--drive");
        start.ArgumentList.Add(normalized.ToString());
        return start;
    }
}
