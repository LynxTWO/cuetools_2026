using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CUETools.Codecs;
using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class CodecPickerAndHealthTests
{
    [TestMethod]
    public void StaleLossyCommandProfile_DropsLosslessVerificationContract()
    {
        const string json = "{\"Name\":\"oggenc.exe\",\"Extension\":\"ogg\"," +
            "\"Lossless\":false,\"VerificationRequired\":true," +
            "\"VerificationContractVersion\":1,\"VerificationUsesEncoder\":true," +
            "\"VerificationPath\":\"decoder.exe\"," +
            "\"VerificationParameters\":\"-d %I -\"}";

        var settings = JsonConvert.DeserializeObject<
            CUETools.Codecs.CommandLine.EncoderSettings>(json);

        Assert.IsNotNull(settings);
        Assert.IsFalse(settings.Lossless);
        Assert.IsFalse(settings.VerificationRequired);
        Assert.AreEqual(0, settings.VerificationContractVersion);
        Assert.IsFalse(settings.VerificationUsesEncoder);
        Assert.AreEqual("", settings.VerificationPath);
        Assert.AreEqual("", settings.VerificationParameters);
        PropertyDescriptor lossless = TypeDescriptor.GetProperties(settings)["Lossless"];
        Assert.IsNotNull(lossless);
        Assert.IsFalse(lossless.IsBrowsable);
    }

    [TestMethod]
    public void LossyCatalogNormalization_ClearsStructuralVerifierState()
    {
        var settings = new CUETools.Codecs.CommandLine.EncoderSettings(
            "example.exe", "example", true, "1", "1", "example.exe", "- %O")
        {
            VerificationUsesEncoder = true,
            VerificationParameters = "-d %I -",
        };

        settings.Lossless = false;

        Assert.IsTrue(settings.VerificationRequired,
            "A mutable type label must not weaken a live verification contract.");
        Assert.IsTrue(settings.NormalizeVerificationContract());

        Assert.IsFalse(settings.VerificationRequired);
        Assert.AreEqual(0, settings.VerificationContractVersion);
        Assert.IsFalse(settings.VerificationUsesEncoder);
        Assert.AreEqual("", settings.VerificationParameters);
    }

    [TestMethod]
    public void Catalog_KeepsUnavailableCodecVisibleButUnselectable()
    {
        using var tree = new CatalogTree();
        var settings = new CUETools.Codecs.CommandLine.EncoderSettings(
            "missing.exe",
            "missingcodec",
            false,
            "1",
            "1",
            "missing.exe",
            "- %O");
        var encoder = new AudioEncoderSettingsViewModel(settings);
        tree.Config.advanced.encoders.Add(settings);
        tree.Config.Encoders.Add(encoder);
        tree.Config.formats.Add(
            "missingcodec",
            new CUEToolsFormat(
                "missingcodec",
                CUEToolsTagger.APEv2,
                false,
                true,
                false,
                false,
                null,
                encoder,
                null));

        CodecChoice choice = tree.Catalog.BuildChoices(tree.Config).Single(item =>
            item.Extension == "missingcodec");

        Assert.AreEqual("Lossy", choice.Category);
        Assert.AreEqual("Setup required", choice.Status);
        Assert.IsFalse(choice.CanSelect);
        Assert.IsFalse(string.IsNullOrWhiteSpace(choice.HealthDetail));
        Assert.ThrowsException<InvalidOperationException>(() =>
            tree.Catalog.SelectCodec(tree.Config, choice));
    }

    [TestMethod]
    public void Catalog_ChoicesCarryNamesOriginsGuidanceAndDeterministicGroups()
    {
        using var tree = new CatalogTree();
        tree.Catalog.EnsureRegistered(tree.Config);

        CodecChoice[] choices = tree.Catalog.BuildChoices(tree.Config).ToArray();

        Assert.IsTrue(choices.Length > 0);
        Assert.IsTrue(choices.Any(choice => choice.Category == "Lossless"));
        Assert.IsTrue(choices.Any(choice => choice.Category == "Lossy"));
        Assert.IsTrue(choices.TakeWhile(choice => choice.Category == "Lossless").Any());
        foreach (CodecChoice choice in choices)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(choice.FormatName));
            Assert.IsFalse(string.IsNullOrWhiteSpace(choice.ExtensionLabel));
            Assert.IsFalse(string.IsNullOrWhiteSpace(choice.Implementation));
            Assert.IsFalse(string.IsNullOrWhiteSpace(choice.Origin));
            Assert.IsFalse(string.IsNullOrWhiteSpace(choice.Description));
            Assert.IsFalse(string.IsNullOrWhiteSpace(choice.BestUse));
        }
    }

    [TestMethod]
    public void Catalog_SelectionRetainsTheExactImplementationWithinOneFormat()
    {
        using var tree = new CatalogTree();
        string firstPath = Path.Combine(tree.Root, "first.exe");
        string secondPath = Path.Combine(tree.Root, "second.exe");
        File.WriteAllText(firstPath, "first");
        File.WriteAllText(secondPath, "second");
        var firstSettings = new CUETools.Codecs.CommandLine.EncoderSettings(
            "first.exe", "dualtest", false, "1", "1", firstPath, "- %O");
        var secondSettings = new CUETools.Codecs.CommandLine.EncoderSettings(
            "second.exe", "dualtest", false, "1", "1", secondPath, "- %O");
        var first = new AudioEncoderSettingsViewModel(firstSettings);
        var second = new AudioEncoderSettingsViewModel(secondSettings);
        tree.Config.advanced.encoders.Add(firstSettings);
        tree.Config.advanced.encoders.Add(secondSettings);
        tree.Config.Encoders.Add(first);
        tree.Config.Encoders.Add(second);
        tree.Config.formats.Add(
            "dualtest",
            new CUEToolsFormat(
                "dualtest",
                CUEToolsTagger.APEv2,
                false,
                true,
                false,
                false,
                null,
                first,
                null));
        CodecChoice[] choices = tree.Catalog.BuildChoices(tree.Config)
            .Where(choice => choice.Extension == "dualtest" && choice.CanSelect)
            .ToArray();
        Assert.AreEqual(2, choices.Length);
        CodecChoice selected = choices.Single(choice =>
            choice.Implementation == "second.exe");

        tree.Catalog.SelectCodec(tree.Config, selected);

        Assert.AreEqual(
            selected.StableId,
            tree.Catalog.GetSelectedChoice(tree.Config, "dualtest")?.StableId);
    }

    [TestMethod]
    public void RipView_ValidatesCodecBeforeMutatingTheOpticalJobState()
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(root);
        string source = File.ReadAllText(Path.Combine(
            root,
            "CUETools.App.Core",
            "ViewModels",
            "RipViewModel.cs"));

        AssertPreflightOrder(source, "private async Task RunJobAsync(bool encode)");
        AssertPreflightOrder(source, "private async Task RunTestCopyAsync()");
    }

    [TestMethod]
    public void RipService_ValidatesCodecBeforeClaimingTheDrive()
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(root);
        string source = File.ReadAllText(Path.Combine(
            root,
            "CUETools.App.Core",
            "Services",
            "RipService.cs"));

        AssertServicePreflightOrder(source, "public VerifyResult RunEncode(");
        AssertServicePreflightOrder(source, "public TestCopyRunResult RunTestAndCopy(");
    }

    [TestMethod]
    public void OutputPages_UseRichPickerInsteadOfRawExtensionComboBox()
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(root);
        string ripView = File.ReadAllText(Path.Combine(
            root,
            "CUETools.Wpf",
            "Views",
            "RipView.xaml"));
        string convertView = File.ReadAllText(Path.Combine(
            root,
            "CUETools.Wpf",
            "Views",
            "ConvertView.xaml"));
        string queueView = File.ReadAllText(Path.Combine(
            root,
            "CUETools.Wpf",
            "Views",
            "QueueView.xaml"));
        string picker = File.ReadAllText(Path.Combine(
            root,
            "CUETools.Wpf",
            "Views",
            "CodecPickerWindow.xaml"));
        string pickerCode = File.ReadAllText(Path.Combine(
            root,
            "CUETools.Wpf",
            "Views",
            "CodecPickerWindow.xaml.cs"));

        Assert.IsFalse(ripView.Contains(
            "ItemsSource=\"{Binding Formats}\" SelectedItem=\"{Binding SelectedFormat",
            StringComparison.Ordinal));
        Assert.IsFalse(convertView.Contains(
            "ItemsSource=\"{Binding Formats}\" SelectedItem=\"{Binding SelectedFormat",
            StringComparison.Ordinal));
        Assert.IsFalse(queueView.Contains(
            "ItemsSource=\"{Binding Formats}\" SelectedItem=\"{Binding SelectedFormat",
            StringComparison.Ordinal));
        StringAssert.Contains(ripView, "CodecPicker_Click");
        StringAssert.Contains(convertView, "CodecPicker_Click");
        StringAssert.Contains(queueView, "CodecPicker_Click");
        StringAssert.Contains(ripView,
            "Click=\"EncoderSettings_Click\" IsEnabled=\"{Binding CodecUnlocked}\"");
        StringAssert.Contains(convertView,
            "Click=\"CodecPicker_Click\" IsEnabled=\"{Binding CodecUnlocked}\"");
        StringAssert.Contains(queueView,
            "Click=\"CodecPicker_Click\" IsEnabled=\"{Binding CodecPickerEnabled}\"");
        StringAssert.Contains(picker, "Show unavailable");
        StringAssert.Contains(pickerCode, "Compression guidance");
        StringAssert.Contains(pickerCode, "Perceptual efficiency");
        StringAssert.Contains(picker, "{DynamicResource Ground}");
        StringAssert.Contains(picker, "{DynamicResource Panel}");
    }

    [TestMethod]
    public void MonkeyAudioFinalizer_ContainsNativeCleanupFailure()
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(root);
        string source = File.ReadAllText(Path.Combine(
            root,
            "CUETools.Codecs.MACLib",
            "AudioEncoder.cs"));

        StringAssert.Contains(source, "IntPtr compressor = pAPECompress;");
        StringAssert.Contains(source, "pAPECompress = IntPtr.Zero;");
        StringAssert.Contains(source, "try { MACLibDll.c_APECompress_Destroy(compressor); }");
        StringAssert.Contains(source, "catch { }");
    }

    private static void AssertPreflightOrder(string source, string method)
    {
        int start = source.IndexOf(method, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, method);
        int next = source.IndexOf("private async Task", start + method.Length,
            StringComparison.Ordinal);
        string body = next < 0 ? source.Substring(start) : source.Substring(start, next - start);
        int validation = body.IndexOf("ValidateSelectedCodec", StringComparison.Ordinal);
        int persistence = body.IndexOf("PersistPrimarySettingsSnapshot", StringComparison.Ordinal);
        int ownership = body.IndexOf("IsRipping = true", StringComparison.Ordinal);
        Assert.IsTrue(validation >= 0 && validation < persistence && validation < ownership,
            method + " must refuse an unhealthy codec before settings publication and optical ownership.");
    }

    private static void AssertServicePreflightOrder(string source, string method)
    {
        int start = source.IndexOf(method, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, method);
        int next = source.IndexOf("\n    public ", start + method.Length,
            StringComparison.Ordinal);
        string body = next < 0 ? source.Substring(start) : source.Substring(start, next - start);
        int validation = body.IndexOf("ValidateCodecBeforeOpticalRead", StringComparison.Ordinal);
        int ownership = body.IndexOf("DriveService.TryEnterRip", StringComparison.Ordinal);
        Assert.IsTrue(validation >= 0 && validation < ownership,
            method + " must reject an unhealthy codec before claiming the optical drive.");
    }

    private sealed class CatalogTree : IDisposable
    {
        public CatalogTree()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "cuetools-codec-picker-" + Guid.NewGuid().ToString("N"));
            Managed = Path.Combine(Root, "managed");
            Bundled = Path.Combine(Root, "bundled");
            Directory.CreateDirectory(Managed);
            Directory.CreateDirectory(Bundled);
            Config = new CUEConfig();
            Catalog = new EncoderCatalog(
                new FakeLog(),
                new AppSettings(),
                Managed,
                Bundled);
        }

        public string Root { get; }
        public string Managed { get; }
        public string Bundled { get; }
        public CUEConfig Config { get; }
        public EncoderCatalog Catalog { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    private sealed class FakeLog : IDiagnosticLog
    {
        public string LogPath => "unused.log";
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message, Exception ex = null) { }
        public void Redact(params string[] sensitive) { }
    }
}
