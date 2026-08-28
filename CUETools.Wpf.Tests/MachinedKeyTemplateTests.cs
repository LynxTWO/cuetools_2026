using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CUETools.Wpf.Controls;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// Pins the machined key: the console-key button ported from the Linux head, replacing the
/// floating rubber cap that stood here before.
///
/// Before this suite existed, nothing in the WPF tests walked a control template or named a single
/// template part, so any of these layers could be renamed or deleted with the whole suite green.
/// The button is also the one control whose metrics move page layout, and the rail and queue
/// column tests assert measured constants rather than measuring, so a silent change there goes
/// wrong without going red.
/// </summary>
[TestClass]
public sealed class MachinedKeyTemplateTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>Back to front: the housing wall, the seam lamp, the cap, the three layers that
    /// make the cap read as domed, the label, and the legend strip.</summary>
    private static readonly string[] KeyLayers =
    {
        "keyRecess", "keySeam", "keyFace", "keyShoulder", "keyCrown", "keyLip",
        "keyContent", "legend",
    };

    [TestMethod]
    public void TheKeyTemplateKeepsEveryLayerThatBuildsTheCap()
    {
        XElement template = KeyTemplate();
        foreach (string layer in KeyLayers)
            Assert.IsTrue(
                template.Descendants().Any(e => e.Attribute(Xaml + "Name")?.Value == layer),
                "the key template lost its " + layer + " layer");
    }

    [TestMethod]
    public void TheKeyIsToldByLightAndSinksWhenPressed()
    {
        string xaml = KeyTemplate().ToString(SaveOptions.DisableFormatting);

        // Hover and press are told by the housing lamp, not by an outline.
        StringAssert.Contains(xaml, "KeySeamColor");
        StringAssert.Contains(xaml, "\"0.34\"", "hover should bring the seam lamp to a low glow");
        StringAssert.Contains(xaml, "\"0.85\"", "press should take the seam lamp up");
        // The cap sinks into its housing rather than squashing in place.
        StringAssert.Contains(xaml, "TranslateTransform");
        StringAssert.Contains(xaml, "\"1.2\"", "press should sink the cap 1.2 pixels");
        StringAssert.Contains(xaml, "ButtonPressed");
    }

    [TestMethod]
    public void ADeadKeyStaysReadableAtStandbyRatherThanFading()
    {
        string xaml = KeyTemplate().ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(xaml, "KeyStandby",
            "a dead key's label is lit at standby current, not dimmed to nothing");
        Assert.IsFalse(
            xaml.Contains("Property=\"Opacity\" Value=\"0.4\"", StringComparison.Ordinal),
            "blanket opacity on the whole control is what the standby treatment replaced");
    }

    [TestMethod]
    public void TransportRolesAreDrivenByTheAttachedPropertyInsideTheOneTemplate()
    {
        // Avalonia reaches into another style's template with a /template/ selector. WPF cannot,
        // and mixing style setters with template TargetName setters is what let the Classic
        // theme's own trigger beat the palette under high contrast (D14). So the variant sets an
        // attached property and the single template triggers on it.
        string xaml = KeyTemplate().ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(xaml, "KeyStyle.Role");
        StringAssert.Contains(xaml, "TransportPrimary");
        Assert.IsFalse(
            xaml.Contains("SystemColors", StringComparison.Ordinal),
            "no system brush may enter the key template; the palette owns every colour");

        // Read the registered default rather than constructing a Button: this suite runs off an
        // STA thread, and the question is what an unmarked key inherits, not what one instance holds.
        Assert.AreEqual(
            KeyRole.Normal,
            KeyStyle.RoleProperty.DefaultMetadata.DefaultValue,
            "a plain button carries no legend strip");
    }

    [TestMethod]
    public void TheAccentKeyRestylesTheOneTemplateInsteadOfDeclaringASecond()
    {
        XDocument theme = LoadTheme();
        XElement accent = theme.Descendants(Presentation + "Style").Single(
            s => s.Attribute(Xaml + "Key")?.Value == "Accent");

        StringAssert.Contains(
            accent.Attribute("BasedOn")?.Value ?? "",
            "x:Type Button",
            "the accent key must inherit the one key template, or its states drift from it");
        Assert.IsFalse(
            accent.Descendants(Presentation + "ControlTemplate").Any(),
            "a second template is a second set of state behaviours to keep in sync");
        // An unpowered accent key is off, not a teal key with dim text.
        string xaml = accent.ToString(SaveOptions.DisableFormatting);
        StringAssert.Contains(xaml, "KeyStandby");
        StringAssert.Contains(xaml, "AccentKeyTopColor");
    }

    [TestMethod]
    public void TheRunGroupIsABankOfTransportKeys()
    {
        XDocument rip = LoadView("RipView.xaml");
        (string Content, string Role)[] expected =
        {
            ("Test &amp; Copy", "Transport"),
            ("Rip", "TransportPrimary"),
            ("Verify", "Transport"),
            ("Stop", "Transport"),
        };

        foreach ((string content, string role) in expected)
        {
            XElement button = rip.Descendants(Presentation + "Button").SingleOrDefault(
                b => b.Attribute("Content")?.Value == System.Net.WebUtility.HtmlDecode(content));
            Assert.IsNotNull(button, "the RUN group lost its " + content + " key");
            Assert.AreEqual(
                role,
                button.Attribute(Presentation + "KeyStyle.Role")?.Value
                    ?? button.Attributes().FirstOrDefault(
                        a => a.Name.LocalName == "KeyStyle.Role")?.Value,
                content + " should carry the " + role + " legend");
            Assert.IsNull(
                button.Attribute("Style"),
                content + " is a transport key: accent is for keys that commit a dialog");
        }
    }

    [TestMethod]
    public void BothPalettesCarryEveryKeyToken()
    {
        // ThemeService turns "...Color" keys into Colors and everything else into brushes, and the
        // template asks for both kinds. A key missing from one table throws only when that theme
        // is applied, which is the sort of thing a light-theme user finds first.
        string[] tokens =
        {
            "KeyStandby", "KeySeamColor", "KeyStandbyGlowColor",
            "KeyShoulderMidColor", "KeyShoulderEdgeColor",
            "KeyCrownCoreColor", "KeyCrownMidColor",
            "KeyLipTopColor", "KeyLipBottomColor",
            "AccentKeyTopColor", "AccentKeyBottomColor", "AccentKeyText",
        };

        foreach (AppTheme theme in new[] { AppTheme.Dark, AppTheme.Light })
        {
            var dictionary = new System.Windows.ResourceDictionary();
            ThemeService.Swap(dictionary, theme);
            var palette = dictionary.MergedDictionaries.Last();
            foreach (string token in tokens)
                Assert.IsTrue(palette.Contains(token), theme + " palette is missing " + token);
        }
    }

    private static XElement KeyTemplate()
    {
        XDocument theme = LoadTheme();
        XElement style = theme.Descendants(Presentation + "Style").Single(
            s => s.Attribute("TargetType")?.Value == "Button" && s.Attribute(Xaml + "Key") == null);
        return style.Descendants(Presentation + "ControlTemplate").Single();
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
