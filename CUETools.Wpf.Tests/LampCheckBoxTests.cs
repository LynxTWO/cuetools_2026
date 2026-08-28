using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// Pins the lamp checkbox: a recessed housing with a lamp behind a lens and an etched tick.
///
/// Before this, the app had no CheckBox style at all and its two checkboxes rendered OS default -
/// the pair recorded in docs/evidence/2026-08-27-selector-high-contrast as adopting Windows
/// high-contrast styling inside an otherwise palette-coloured window. D14 chose to keep the custom
/// palette on purpose, so an explicit template is the consistent answer.
/// </summary>
[TestClass]
public sealed class LampCheckBoxTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void TheLampTemplateKeepsItsHousingLensAndTick()
    {
        XElement template = LampTemplate();
        foreach (string layer in new[] { "housing", "lens", "tick" })
            Assert.IsTrue(
                template.Descendants().Any(e => e.Attribute(Xaml + "Name")?.Value == layer),
                "the lamp checkbox lost its " + layer + " layer");

        string xaml = template.ToString(SaveOptions.DisableFormatting);
        Assert.IsFalse(
            xaml.Contains("SystemColors", StringComparison.Ordinal),
            "an explicit template exists precisely so no system brush enters this control");
    }

    [TestMethod]
    public void TheLampWarmsFastAndCoolsSlow()
    {
        // A filament does not switch; it warms and cools. 0.18s on, 0.34s off.
        string xaml = LampTemplate().ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(xaml, "0:0:0.18");
        StringAssert.Contains(xaml, "0:0:0.34");
        StringAssert.Contains(xaml, "LampCore");
    }

    [TestMethod]
    public void ADeadLampKeepsItsStateButNotItsHalo()
    {
        // The lens carries state: a disabled option the user cannot change must still say whether
        // it is on. The halo carries power, so it goes out, exactly as a dead key's seam does.
        XElement template = LampTemplate();
        XElement disabled = template
            .Descendants(Presentation + "Trigger")
            .Single(t => t.Attribute("Property")?.Value == "IsEnabled"
                      && t.Attribute("Value")?.Value == "False");

        string xaml = disabled.ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(xaml, "KeyStandby", "the label drops to standby with every dead control");
        StringAssert.Contains(xaml, "Effect", "the housing halo goes out when the control is dead");
        Assert.IsFalse(
            xaml.Contains("TargetName=\"lens\"", StringComparison.Ordinal),
            "dimming the lens would hide whether a disabled option is on");
    }

    [TestMethod]
    public void TheRipOptionsAreLampsWithLabelsAReaderCanUnderstand()
    {
        XDocument rip = LoadView("RipView.xaml");
        (string Content, string Binding)[] expected =
        {
            ("cue sheet", "CreateCue"),
            ("rip log", "WriteLog"),
            ("cover art", "EmbedArt"),
        };

        foreach ((string content, string binding) in expected)
        {
            XElement box = rip.Descendants(Presentation + "CheckBox").SingleOrDefault(
                c => c.Attribute("Content")?.Value == content);
            Assert.IsNotNull(box, "the Rip options lost its " + content + " lamp");
            StringAssert.Contains(box.Attribute("IsChecked")?.Value ?? "", binding);
            Assert.IsNotNull(box.Attribute("ToolTip"), content + " needs its full meaning on hover");
            Assert.AreEqual(
                "WrapPanel",
                box.Parent?.Name.LocalName,
                "the lamps wrap on a narrow rail instead of stacking into full-width rows");
        }

        // These three moved off the switch: a switch is a persistent setting, a lamp is an option
        // on the job about to run.
        Assert.IsFalse(
            rip.Descendants(Presentation + "ToggleButton").Any(
                t => (t.Attribute("IsChecked")?.Value ?? "").Contains("CreateCue")),
            "the rip options should no longer be switches");
    }

    [TestMethod]
    public void BothPalettesCarryTheTickTokens()
    {
        // The Linux head hardcodes both tick colours, which only works on a dark console.
        foreach (AppTheme theme in new[] { AppTheme.Dark, AppTheme.Light })
        {
            var dictionary = new System.Windows.ResourceDictionary();
            ThemeService.Swap(dictionary, theme);
            var palette = dictionary.MergedDictionaries.Last();
            foreach (string token in new[] { "LampTickOff", "LampTickOn" })
                Assert.IsTrue(palette.Contains(token), theme + " palette is missing " + token);
        }
    }

    private static XElement LampTemplate()
    {
        XDocument theme = Load(Path.Combine("Theme", "Theme.xaml"));
        XElement style = theme.Descendants(Presentation + "Style").Single(
            s => s.Attribute("TargetType")?.Value == "CheckBox"
              && s.Attribute(Xaml + "Key") == null);
        return style.Descendants(Presentation + "ControlTemplate").Single();
    }

    private static XDocument LoadView(string file) => Load(Path.Combine("Views", file));

    private static XDocument Load(string relative)
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(root))
            Assert.Inconclusive("Could not locate repository root.");
        return XDocument.Load(Path.Combine(root, "CUETools.Wpf", relative));
    }
}
