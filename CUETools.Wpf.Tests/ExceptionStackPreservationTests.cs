using System;
using System.IO;
using System.Runtime.CompilerServices;
using CUETools.Codecs;
using CUETools.Processor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class ExceptionStackPreservationTests
{
    [TestMethod]
    public void AudioDestinationConstructorFailure_PreservesOriginalThrowSite()
    {
        var config = new CUEConfig();
        var settings = new ThrowingEncoderSettings();
        config.formats.Add("stacktest", new CUEToolsFormat(
            "stacktest",
            CUEToolsTagger.TagLibSharp,
            true,
            false,
            false,
            false,
            new AudioEncoderSettingsViewModel(settings),
            null,
            null));

        InvalidOperationException exception = Assert.ThrowsException<InvalidOperationException>(() =>
            AudioReadWrite.GetAudioDest(
                AudioEncoderType.Lossless,
                "unused.stacktest",
                AudioPCMConfig.RedBook,
                1,
                0,
                ".stacktest",
                config));

        StringAssert.Contains(exception.StackTrace, nameof(ThrowingEncoder.ThrowOriginal));
    }

    private sealed class ThrowingEncoderSettings : IAudioEncoderSettings
    {
        public string Name => "stack-test";
        public string Extension => "stacktest";
        public Type EncoderType => typeof(ThrowingEncoder);
        public bool Lossless => true;
        public int Priority => 0;
        public string SupportedModes => "default";
        public string DefaultMode => "default";
        public string EncoderMode { get; set; } = "default";
        public AudioPCMConfig PCM { get; set; } = AudioPCMConfig.RedBook;
        public int BlockSize { get; set; }
        public int Padding { get; set; }
        public IAudioEncoderSettings Clone() => new ThrowingEncoderSettings();
    }

    private sealed class ThrowingEncoder
    {
        public ThrowingEncoder(IAudioEncoderSettings settings, string path, Stream output)
        {
            ThrowOriginal();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowOriginal()
        {
            throw new InvalidOperationException("constructor failure");
        }
    }
}
