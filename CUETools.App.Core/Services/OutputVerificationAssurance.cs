using System;
using CUETools.Codecs;

namespace CUETools.Wpf.Services;

/// <summary>
/// Truth label for the final encoded file, separate from AccurateRip/CTDB and Test &amp; Copy
/// evidence about the optical read. A perfect read does not prove that an encoder and later
/// metadata writes preserved those samples. Performed is reportable only after CUESheet has used
/// a trusted codec contract and re-decoded the metadata-complete output.
/// </summary>
public readonly struct OutputVerificationAssurance
{
    public OutputVerificationAssurance(
        bool known,
        bool lossless,
        bool performed,
        string detail)
    {
        Known = known;
        Lossless = lossless;
        Performed = performed;
        Detail = detail ?? "";
    }

    public bool Known { get; }
    public bool Lossless { get; }
    public bool Performed { get; }
    public string Detail { get; }
}

public static class OutputVerificationAssuranceEvaluator
{
    public static OutputVerificationAssurance Evaluate(
        IAudioEncoderSettings? settings,
        bool lossy)
    {
        if (lossy)
            return new OutputVerificationAssurance(
                known: true,
                lossless: false,
                performed: false,
                detail: "not applicable (lossy output)");

        if (settings == null)
            return Unverified(
                "not performed (the selected lossless encoder was unavailable)");

        Type type = settings.GetType();
        if (type == typeof(CUETools.Codecs.CommandLine.EncoderSettings))
        {
            var command =
                (CUETools.Codecs.CommandLine.EncoderSettings)settings;
            if (command.VerificationRequired &&
                command.HasLosslessVerifier)
                return Verified();
            if (command.UsesLegacyUnverifiedCompatibility)
                return Unverified(
                    "not performed (legacy external encoder has no decoder contract)");
            return Unverified(
                "not performed (external encoder verification is not configured)");
        }

        // Require exact bundled settings identities. A user-approved plugin subclass can change
        // EncoderType/behavior while inheriting a conveniently named Verify property; treating
        // that shape as proof would overstate an unknown implementation.
        if (type == typeof(CUETools.Codecs.Flake.EncoderSettings))
            return FromSwitch(
                ((CUETools.Codecs.Flake.EncoderSettings)settings).DoVerify);
        if (type == typeof(CUETools.Codecs.ALAC.EncoderSettings))
            return FromSwitch(
                ((CUETools.Codecs.ALAC.EncoderSettings)settings).DoVerify);
        // WMA Lossless is a Windows-only project and deliberately not an
        // App.Core reference. Gate on the exact name identities - the FLACCL
        // precedent: FullName plus assembly name, then a typed contract - and
        // read through the verify-on-encode seam instead of a compile-time
        // type.
        if (string.Equals(type.FullName,
                "CUETools.Codecs.WMA.LosslessEncoderSettings",
                StringComparison.Ordinal) &&
            string.Equals(type.Assembly.GetName().Name,
                "CUETools.Codecs.WMA", StringComparison.Ordinal) &&
            settings is IVerifyOnEncodeSettings wmaVerifiable)
            return FromSwitch(wmaVerifiable.VerifyOnEncode);
        if (type == typeof(CUETools.Codecs.libFLAC.EncoderSettings))
            return FromSwitch(
                ((CUETools.Codecs.libFLAC.EncoderSettings)settings).Verify);
        if (type == typeof(CUETools.Codecs.libwavpack.EncoderSettings))
            return FromSwitch(
                ((CUETools.Codecs.libwavpack.EncoderSettings)settings).Verify);
        if (type == typeof(CUETools.Codecs.MACLib.EncoderSettings))
            return FromSwitch(
                ((CUETools.Codecs.MACLib.EncoderSettings)settings).Verify);

        // FLACCL is a classic/OpenCL plugin and is deliberately not a net8 project reference.
        // The package-manifest loader enrolls its exact runtime Type as a capability. Assembly and
        // type names are insufficient because another load context can present the same names.
        if (CUETools.Processor.CUEProcessorPlugins
            .HasTrustedOutputVerificationContract(type))
        {
            object? value = type.GetProperty("DoVerify")?.GetValue(settings);
            if (value is bool enabled)
                return FromSwitch(enabled);
        }

        return Unverified(
            "not performed (this encoder exposes no trusted output-verification contract)");
    }

    private static OutputVerificationAssurance Verified() =>
        new OutputVerificationAssurance(
            known: true,
            lossless: true,
            performed: true,
            detail: "performed after metadata finalization (final encoded audio was decoded "
                + "and compared with the PCM delivered to the encoder)");

    private static OutputVerificationAssurance FromSwitch(bool enabled) =>
        enabled
            ? Verified()
            : Unverified(
                "not performed (encoder verification is disabled)");

    private static OutputVerificationAssurance Unverified(string detail) =>
        new OutputVerificationAssurance(
            known: true,
            lossless: true,
            performed: false,
            detail: detail);
}
