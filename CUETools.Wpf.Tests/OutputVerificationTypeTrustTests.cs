using System;
using System.Reflection;
using System.Reflection.Emit;
using CUETools.Codecs;
using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class OutputVerificationTypeTrustTests
{
    [TestMethod]
    public void ManifestCapabilityBindsExactTypeNotSpoofedAssemblyAndTypeNames()
    {
        Type trustedType = BuildFlacclNamedType();
        Type spoofType = BuildFlacclNamedType();
        Assert.AreEqual(trustedType.FullName, spoofType.FullName);
        Assert.AreEqual(
            trustedType.Assembly.GetName().Name,
            spoofType.Assembly.GetName().Name);
        Assert.AreNotSame(trustedType, spoofType);

        var trusted =
            (IAudioEncoderSettings)Activator.CreateInstance(trustedType)!;
        var spoof =
            (IAudioEncoderSettings)Activator.CreateInstance(spoofType)!;

        using (CUEProcessorPlugins.RegisterOutputVerificationTypeForTest(
            trustedType,
            packagedManifestSource: true))
        {
            OutputVerificationAssurance accepted =
                OutputVerificationAssuranceEvaluator.Evaluate(
                    trusted,
                    lossy: false);
            OutputVerificationAssurance rejected =
                OutputVerificationAssuranceEvaluator.Evaluate(
                    spoof,
                    lossy: false);

            Assert.IsTrue(accepted.Performed);
            Assert.IsFalse(rejected.Performed);
            StringAssert.Contains(
                rejected.Detail,
                "no trusted output-verification contract");
        }

        Assert.IsFalse(
            OutputVerificationAssuranceEvaluator.Evaluate(
                trusted,
                lossy: false).Performed,
            "The test capability must not survive its bounded scope.");
    }

    [TestMethod]
    public void OnlyPackagedRegistrationSourceCanEnrollVerificationContract()
    {
        Type packagedType = BuildFlacclNamedType();
        using (CUEProcessorPlugins.RegisterOutputVerificationTypeForTest(
            packagedType,
            packagedManifestSource: true))
        {
            Assert.IsTrue(
                CUEProcessorPlugins.HasTrustedOutputVerificationContract(
                    packagedType));
        }

        Type developmentOrUserType = BuildFlacclNamedType();
        using (CUEProcessorPlugins.RegisterOutputVerificationTypeForTest(
            developmentOrUserType,
            packagedManifestSource: false))
        {
            Assert.IsFalse(
                CUEProcessorPlugins.HasTrustedOutputVerificationContract(
                    developmentOrUserType));
            var settings =
                (IAudioEncoderSettings)Activator.CreateInstance(
                    developmentOrUserType)!;
            Assert.IsFalse(
                OutputVerificationAssuranceEvaluator.Evaluate(
                    settings,
                    lossy: false).Performed);
        }
    }

    private static Type BuildFlacclNamedType()
    {
        var name = new AssemblyName("CUETools.Codecs.FLACCL");
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            name,
            AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(
            "test-" + Guid.NewGuid().ToString("N"));
        TypeBuilder type = module.DefineType(
            "CUETools.Codecs.FLACCL.EncoderSettings",
            TypeAttributes.Public | TypeAttributes.Class,
            typeof(FakeFlacclSettingsBase));
        return type.CreateType()!;
    }
}

public class FakeFlacclSettingsBase : IAudioEncoderSettings
{
    public bool DoVerify { get; set; } = true;
    public string Name => "test FLACCL contract";
    public string Extension => "flac";
    public Type EncoderType => typeof(object);
    public bool Lossless => true;
    public int Priority => 1;
    public string SupportedModes => "8";
    public string DefaultMode => "8";
    public string EncoderMode { get; set; } = "8";
    public AudioPCMConfig PCM { get; set; } = AudioPCMConfig.RedBook;
    public int BlockSize { get; set; }
    public int Padding { get; set; }
    public IAudioEncoderSettings Clone() =>
        (IAudioEncoderSettings)MemberwiseClone();
}
