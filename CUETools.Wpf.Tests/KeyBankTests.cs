using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// Pins the key bank: a short fixed set of choices laid out as pressable keys instead of hidden
/// behind a menu, and the rule that keeps it from restyling the navigation rail.
/// </summary>
[TestClass]
public sealed class KeyBankTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void TheBankItemStyleIsKeyedSoItCannotReachTheNavigationRail()
    {
        // An implicit ListBoxItem style would restyle the left rail into a keypad. The rail uses
        // its own keyed NavItem through ItemContainerStyle for the same reason.
        XDocument theme = LoadTheme();
        Assert.IsFalse(
            theme.Descendants(Presentation + "Style").Any(
                s => s.Attribute("TargetType")?.Value == "ListBoxItem"
                  && s.Attribute(Xaml + "Key") == null),
            "no ListBoxItem style may be implicit");

        XElement bank = Keyed("Bank");
        Assert.AreEqual(
            "{StaticResource BankKey}",
            bank.Elements(Presentation + "Setter")
                .Single(s => s.Attribute("Property")?.Value == "ItemContainerStyle")
                .Attribute("Value")?.Value,
            "the bank applies its key style through ItemContainerStyle");
    }

    [TestMethod]
    public void TheBankWrapsRatherThanClippingItsLastKey()
    {
        XElement bank = Keyed("Bank");
        string xaml = bank.ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(xaml, "WrapPanel",
            "a four-key bank does not always fit one line in a narrow rail");
    }

    [TestMethod]
    public void SelectionAndTouchGetTwoDifferentLights()
    {
        // The teal detent pip says "this is the engaged one"; the amber seam lamp says "your
        // pointer is here". The seam deliberately does not light the selected key.
        XElement item = Keyed("BankKey");
        string xaml = item.ToString(SaveOptions.DisableFormatting);
        foreach (string part in new[] { "bankSeam", "bankKey", "bankPip", "bankKeyContent" })
            StringAssert.Contains(xaml, part, "the bank key lost its " + part);

        XElement selected = item.Descendants(Presentation + "Trigger").Single(
            t => t.Attribute("Property")?.Value == "IsSelected"
              && t.Attribute("Value")?.Value == "True");
        string selectedXaml = selected.ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(selectedXaml, "bankPip");
        StringAssert.Contains(selectedXaml, "StatusAccent");
        Assert.IsFalse(
            selectedXaml.Contains("bankSeam", StringComparison.Ordinal),
            "the seam lamp answers a different question from the detent pip");
    }

    [TestMethod]
    public void ADeadBankStillShowsWhichKeyIsEngaged()
    {
        XElement item = Keyed("BankKey");
        XElement deadSelected = item.Descendants(Presentation + "MultiTrigger").Single();
        string xaml = deadSelected.ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(xaml, "KeyStandby", "the pip drops to standby rather than going out");
        StringAssert.Contains(xaml, "bankPip");
    }

    [TestMethod]
    public void ShortFixedChoicesBecameBanksAndRealListsStayedWindows()
    {
        // Bank: every option visible and one press away. Window: a genuine list, or labels too
        // long to lay side by side.
        (string View, string Binding)[] banks =
        {
            ("RipView.xaml", "CorrectionQuality"),
            ("RipView.xaml", "OutputLayouts"),
            ("AdvancedView.xaml", "CoversSearches"),
            ("AdvancedView.xaml", "MetadataSearches"),
            ("AdvancedView.xaml", "ProxyModes"),
            ("QueueView.xaml", "Actions"),
            ("SettingsView.xaml", "RipOutputLayouts"),
        };
        foreach ((string view, string binding) in banks)
        {
            XDocument document = LoadView(view);
            Assert.IsTrue(
                document.Descendants(Presentation + "ListBox").Any(
                    l => l.ToString().Contains(binding, StringComparison.Ordinal)
                      && (l.Attribute("Style")?.Value ?? "").Contains("Bank", StringComparison.Ordinal)),
                binding + " in " + view + " should be a key bank");
        }

        // The drive and release selectors stay windows: their contents are discovered at runtime.
        XDocument rip = LoadView("RipView.xaml");
        foreach (string binding in new[] { "Drives", "ParallelDrives", "Releases" })
            Assert.IsTrue(
                rip.Descendants(Presentation + "ComboBox").Any(
                    c => c.ToString().Contains("{Binding " + binding + "}", StringComparison.Ordinal)),
                binding + " is a runtime list and stays a window");
    }

    private static XElement Keyed(string key)
    {
        XDocument theme = LoadTheme();
        return theme.Descendants(Presentation + "Style").Single(
            s => s.Attribute(Xaml + "Key")?.Value == key);
    }

    private static XDocument LoadTheme() => Load(Path.Combine("Theme", "Theme.xaml"));

    private static XDocument LoadView(string file) => Load(Path.Combine("Views", file));

    private static XDocument Load(string relative)
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(root))
            Assert.Inconclusive("Could not locate repository root.");
        return XDocument.Load(Path.Combine(root, "CUETools.Wpf", relative));
    }
}
