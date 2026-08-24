using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Mutation.VerificationDiscovery.Tests;

// This profile-only test exercises the optional codec-registry branch through the minimal
// contract seam. Test-MutationHarness.ps1 verifies every behavior-bearing seam member against
// the production processor types before this assembly is allowed to run.
[TestClass]
public sealed class ConfiguredFormatTests
{
    [TestMethod]
    public void ConfiguredLosslessDecoderMakesAnExtensionDiscoverable()
    {
        using var folder = new TemporaryFolder();
        string audio = Path.Combine(folder.Path, "album.CUSTOM");
        File.WriteAllBytes(audio, new byte[] { 1 });
        CUEConfig config = Configured(lossless: true, decoderValid: true);

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery(config).Discover(new[] { audio });

        Assert.IsTrue(result.Ok, result.Error);
        Assert.AreEqual(audio, result.SourceSet!.Discs[0].Path);
    }

    [DataTestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void ConfiguredExtensionRequiresLosslessAndAValidDecoder(
        bool lossless,
        bool decoderValid)
    {
        using var folder = new TemporaryFolder();
        string audio = Path.Combine(folder.Path, "album.custom");
        File.WriteAllBytes(audio, new byte[] { 1 });

        VerificationSourceDiscoveryResult result =
            new VerificationSourceDiscovery(Configured(lossless, decoderValid))
                .Discover(new[] { audio });

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Error, "supported lossless audio");
    }

    private static CUEConfig Configured(bool lossless, bool decoderValid)
    {
        var config = new CUEConfig();
        config.formats["custom"] = new CUEToolsFormat
        {
            allowLossless = lossless,
            decoder = new MutationDecoder { IsValid = decoderValid }
        };
        return config;
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "verify-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
