using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class MultiDriveWindowTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [TestMethod]
    public void SecondaryDriveArgumentsAreStrictlyNonSensitive()
    {
        AppLaunchOptions parsed = AppLaunchOptions.Parse(
            new[] { "--secondary-drive-window", "--drive", "k:" });
        Assert.IsTrue(parsed.IsSecondaryDriveWindow);
        Assert.AreEqual('K', parsed.PreferredDrive);

        var start = DriveWindowLauncher.Create(
            'h',
            processPath: @"C:\Program Files\CUETools\CUETools.Wpf.exe");
        CollectionAssert.AreEqual(
            new[] { "--secondary-drive-window", "--drive", "H" },
            start.ArgumentList.ToArray());
        Assert.AreEqual(
            @"C:\Program Files\CUETools\CUETools.Wpf.exe",
            start.FileName);
        Assert.IsFalse(start.UseShellExecute);

        string joined = string.Join(" ", start.ArgumentList);
        Assert.IsFalse(
            joined.Contains("album", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(joined.Contains('\\'));
        Assert.IsFalse(joined.Contains('/'));

        ProcessStartInfo developer = DriveWindowLauncher.Create(
            'k',
            processPath: @"C:\Program Files\dotnet\dotnet.exe",
            assemblyPath: @"C:\dev\cuetools\CUETools.Wpf.dll");
        CollectionAssert.AreEqual(
            new[]
            {
                @"C:\dev\cuetools\CUETools.Wpf.dll",
                "--secondary-drive-window",
                "--drive",
                "K",
            },
            developer.ArgumentList.ToArray());
        Assert.AreEqual(@"C:\dev\cuetools", developer.WorkingDirectory);
    }

    [TestMethod]
    public void MalformedDriveArgumentsDoNotSelectHardware()
    {
        AppLaunchOptions parsed = AppLaunchOptions.Parse(
            new[]
            {
                "--drive=K:\\Music",
                "--drive",
                "..",
                "--drive=7",
                "--secondary-drive-window",
                "--drive=\u00c5",
            });
        Assert.AreEqual('\0', parsed.PreferredDrive);
        Assert.IsFalse(parsed.IsSecondaryDriveWindow);
    }

    [TestMethod]
    public void RipPageOffersOnlyOtherDrivesThroughIsolatedWindowCommand()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(repoRoot))
            Assert.Inconclusive(
                "Could not locate repository root from " + AppContext.BaseDirectory);

        XDocument rip = XDocument.Load(
            Path.Combine(repoRoot, "CUETools.Wpf", "Views", "RipView.xaml"));
        XElement drivePicker = rip
            .Descendants(Presentation + "ComboBox")
            .Single(element =>
                element.Attribute("ItemsSource")?.Value ==
                "{Binding ParallelDrives}");
        Assert.AreEqual(
            "{Binding ParallelDrive, Mode=TwoWay}",
            drivePicker.Attribute("SelectedItem")?.Value);

        XElement launcher = rip
            .Descendants(Presentation + "Button")
            .Single(element =>
                element.Attribute("Command")?.Value ==
                "{Binding OpenParallelDriveCommand}");
        Assert.AreEqual("Open window", launcher.Attribute("Content")?.Value);
        Assert.AreEqual(
            "{Binding ShowParallelDriveLauncher, Converter={StaticResource BoolVis}}",
            launcher.Parent?.Attribute("Visibility")?.Value);
    }
}
