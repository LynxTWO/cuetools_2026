using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// Pins the machined window (the ComboBox), its list rows, and the vignette on the switch lamp.
///
/// The window is a recessed glass panel showing the current value with a ridged thumbwheel on its
/// right edge. The wheel is the affordance: a chevron says "menu", a wheel says "there are other
/// values behind this one".
/// </summary>
[TestClass]
public sealed class MachinedWindowTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void TheWindowKeepsItsGlassAndThumbwheel()
    {
        XElement style = ImplicitStyle("ComboBox");
        string xaml = style.ToString(SaveOptions.DisableFormatting);
        foreach (string part in new[] { "winHousing", "winGlass", "winWheel", "winValue", "winList" })
            StringAssert.Contains(xaml, part, "the window lost its " + part);

        // Five ridge lines are what make a flat rectangle read as a cylinder.
        Assert.AreEqual(
            5,
            style.Descendants(Presentation + "Border").Count(
                b => b.Attribute("Width")?.Value == "11" && b.Attribute("Height")?.Value == "1"),
            "the thumbwheel needs its five ridges");
        Assert.IsFalse(
            xaml.Contains("SystemColors", StringComparison.Ordinal),
            "the palette owns every colour in this template");
    }

    [TestMethod]
    public void TheWindowSizesItselfInsteadOfBeingPinnedTo30Pixels()
    {
        // The old fixed height would crush the glass and the wheel into each other.
        XElement style = ImplicitStyle("ComboBox");
        CollectionAssert.DoesNotContain(
            style.Elements(Presentation + "Setter")
                .Select(setter => setter.Attribute("Property")?.Value)
                .Where(property => property is not null)
                .ToList(),
            "Height",
            "the housing sizes itself from its content");
    }

    [TestMethod]
    public void TheEngagedRowCarriesADetentPip()
    {
        XElement style = ImplicitStyle("ComboBoxItem");
        string xaml = style.ToString(SaveOptions.DisableFormatting);
        foreach (string part in new[] { "rowFace", "rowPip", "rowContent" })
            StringAssert.Contains(xaml, part, "the list row lost its " + part);

        XElement selected = style
            .Descendants(Presentation + "Trigger")
            .Single(t => t.Attribute("Property")?.Value == "IsSelected"
                      && t.Attribute("Value")?.Value == "True");
        string selectedXaml = selected.ToString(SaveOptions.DisableFormatting);
        // Same detent language as the key bank: the pip says "this is the one".
        StringAssert.Contains(selectedXaml, "StatusAccent");
        StringAssert.Contains(selectedXaml, "rowPip");
        StringAssert.Contains(selectedXaml, "Effect");
    }

    [TestMethod]
    public void TheSwitchLampDiesOutBeforeItsCorners()
    {
        // The plastic thickens toward the rim, so the light has to fade inside the housing rather
        // than be clipped square at the border. The Linux head added this after the owner reported
        // a hard stop at the corners; WPF had the same glow without the mask.
        XDocument theme = Load(Path.Combine("Theme", "Theme.xaml"));
        XElement glow = theme.Descendants()
            .Single(e => e.Attribute(Xaml + "Name")?.Value == "glow");
        Assert.IsTrue(
            glow.Elements(Presentation + "Border.OpacityMask").Any(),
            "the switch lamp needs its vignette mask");
    }

    private static XElement ImplicitStyle(string targetType)
    {
        XDocument theme = Load(Path.Combine("Theme", "Theme.xaml"));
        return theme.Descendants(Presentation + "Style").Single(
            s => s.Attribute("TargetType")?.Value == targetType
              && s.Attribute(Xaml + "Key") == null);
    }

    private static XDocument Load(string relative)
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(root))
            Assert.Inconclusive("Could not locate repository root.");
        return XDocument.Load(Path.Combine(root, "CUETools.Wpf", relative));
    }
}
