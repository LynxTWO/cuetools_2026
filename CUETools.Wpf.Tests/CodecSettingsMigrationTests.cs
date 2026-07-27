using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CUETools.Codecs;
using CUETools.Processor;
using CUETools.Processor.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using AlacSettings = CUETools.Codecs.ALAC.EncoderSettings;
using FlacclSettings = CUETools.Codecs.FLACCL.EncoderSettings;
using FlacSettings = CUETools.Codecs.libFLAC.EncoderSettings;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class CodecSettingsMigrationTests
    {
        private sealed class IsolatedProfile : IDisposable
        {
            private const string AppName = "CUEToolsCodecMigrationTest";
            private const string FileName = "settings.txt";
            private readonly string directoryPath;
            private readonly string appPath;

            public IsolatedProfile()
            {
                directoryPath = Path.Combine(
                    Path.GetTempPath(),
                    "cuetools-codec-migration-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directoryPath);
                appPath = Path.Combine(directoryPath, "CUETools.exe");
            }

            private string SettingsPath =>
                Path.Combine(directoryPath, AppName, FileName);

            public SettingsWriter NewWriter()
            {
                return new SettingsWriter(AppName, FileName, appPath);
            }

            public SettingsReader NewReader()
            {
                return new SettingsReader(AppName, FileName, appPath);
            }

            public string ReadSettings()
            {
                return File.ReadAllText(SettingsPath);
            }

            public void RemoveVerificationMigrationMarker()
            {
                string prefix =
                    CUEConfig.LosslessVerificationDefaultsVersionSetting + "=";
                string[] retainedLines = File.ReadAllLines(SettingsPath)
                    .Where(line => !line.StartsWith(
                        prefix,
                        StringComparison.Ordinal))
                    .ToArray();
                File.WriteAllLines(SettingsPath, retainedLines);
            }

            public void Dispose()
            {
                try
                {
                    Directory.Delete(directoryPath, true);
                }
                catch
                {
                    // Best-effort test cleanup.
                }
            }
        }

        [TestMethod]
        public void NewAlacAndLibFlacProfilesVerifyByDefault()
        {
            Assert.IsTrue(new AlacSettings().DoVerify);
            Assert.IsTrue(new FlacSettings().Verify);
        }

        [TestMethod]
        public void FreshAppProfileEnablesFlacclContractType()
        {
            var standaloneSettings = new FlacclSettings();
            Assert.IsFalse(
                standaloneSettings.DoVerify,
                "The contract double must begin at FLACCL's standalone --verify opt-in default.");

            CUEConfig appConfig;
            lock (CUEProcessorPlugins.encs)
            {
                CUEProcessorPlugins.encs.Add(standaloneSettings);
                try
                {
                    appConfig = new CUEConfig();
                }
                finally
                {
                    CUEProcessorPlugins.encs.Remove(standaloneSettings);
                }
            }

            Assert.IsTrue(
                Find<FlacclSettings>(appConfig).DoVerify,
                "Fresh app profiles must apply the stronger archival verification policy.");
        }

        [TestMethod]
        public void HistoricalOmittedVerifyValuesAreEnabledByOneTimeMigration()
        {
            using (var profile = new IsolatedProfile())
            {
                CUEConfig historical = CreateConfigWithMigrationCodecs();
                Find<AlacSettings>(historical).DoVerify = false;
                Find<FlacSettings>(historical).Verify = false;
                Save(profile, historical);

                // ALAC/libFLAC use false as their JSON serialization default, so this is the
                // exact shape of a historical profile: both false values are omitted and no
                // migration marker exists.
                profile.RemoveVerificationMigrationMarker();

                CUEConfig migrated = CreateConfigWithMigrationCodecs();
                migrated.Load(profile.NewReader());

                Assert.IsFalse(
                    migrated.AdvancedSettingsRejected,
                    "The historical Advanced payload must remain loadable.");
                Assert.IsTrue(Find<AlacSettings>(migrated).DoVerify);
                Assert.IsTrue(Find<FlacSettings>(migrated).Verify);
                Assert.IsTrue(Find<FlacclSettings>(migrated).DoVerify);
            }
        }

        [TestMethod]
        public void ExplicitOptOutAfterMigrationSurvivesLaterLoads()
        {
            using (var profile = new IsolatedProfile())
            {
                CUEConfig historical = CreateConfigWithMigrationCodecs();
                Find<AlacSettings>(historical).DoVerify = false;
                Find<FlacSettings>(historical).Verify = false;
                Save(profile, historical);
                profile.RemoveVerificationMigrationMarker();

                CUEConfig migrated = CreateConfigWithMigrationCodecs();
                migrated.Load(profile.NewReader());
                Assert.IsTrue(Find<AlacSettings>(migrated).DoVerify);
                Assert.IsTrue(Find<FlacSettings>(migrated).Verify);
                Assert.IsTrue(Find<FlacclSettings>(migrated).DoVerify);

                Find<AlacSettings>(migrated).DoVerify = false;
                Find<FlacSettings>(migrated).Verify = false;
                Find<FlacclSettings>(migrated).DoVerify = false;
                Save(profile, migrated);

                StringAssert.Contains(
                    profile.ReadSettings(),
                    CUEConfig.LosslessVerificationDefaultsVersionSetting + "=" +
                    CUEConfig.CurrentLosslessVerificationDefaultsVersion);

                CUEConfig reloaded = CreateConfigWithMigrationCodecs();
                reloaded.Load(profile.NewReader());

                Assert.IsFalse(
                    Find<AlacSettings>(reloaded).DoVerify,
                    "The migration marker must prevent re-enabling a later ALAC opt-out.");
                Assert.IsFalse(
                    Find<FlacSettings>(reloaded).Verify,
                    "The migration marker must prevent re-enabling a later libFLAC opt-out.");
                Assert.IsFalse(
                    Find<FlacclSettings>(reloaded).DoVerify,
                    "The migration marker must prevent re-enabling a later FLACCL opt-out.");
            }
        }

        private static CUEConfig CreateConfigWithMigrationCodecs()
        {
            var config = new CUEConfig();
            AddIfMissing(config, new AlacSettings());
            AddIfMissing(config, new FlacSettings());
            AddIfMissing(config, new FlacclSettings());
            return config;
        }

        private static void AddIfMissing(
            CUEConfig config,
            IAudioEncoderSettings settings)
        {
            if (!config.advanced.encoders.Any(
                    encoder => encoder.GetType() == settings.GetType()))
            {
                config.advanced.encoders.Add(settings);
            }
        }

        private static T Find<T>(CUEConfig config)
            where T : class, IAudioEncoderSettings
        {
            T settings = config.advanced.encoders.OfType<T>().FirstOrDefault();
            Assert.IsNotNull(
                settings,
                "Test setup did not register " + typeof(T).FullName + ".");
            return settings;
        }

        private static void Save(IsolatedProfile profile, CUEConfig config)
        {
            using (SettingsWriter writer = profile.NewWriter())
            {
                config.Save(writer);
                writer.Close();
            }
        }
    }
}

namespace CUETools.Codecs.FLACCL
{
    // The production FLACCL project targets classic .NET only, while this migration suite
    // targets net8. This serialization-compatible contract double lets the net8 app test
    // exercise CUEConfig's exact type-name policy without loading an incompatible framework
    // assembly or changing FLACCL's standalone constructor.
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class EncoderSettings : IAudioEncoderSettings
    {
        public string Name => "FLACCL migration contract";
        public string Extension => "flac";
        public Type EncoderType => typeof(object);
        public bool Lossless => true;
        public int Priority => 1;
        public string SupportedModes => "5";
        public string DefaultMode => "5";
        public string EncoderMode { get; set; } = "5";
        public AudioPCMConfig PCM { get; set; }
        public int BlockSize { get; set; }
        public int Padding { get; set; }

        [DefaultValue(false)]
        [JsonProperty]
        public bool DoVerify { get; set; }

        public IAudioEncoderSettings Clone()
        {
            return (IAudioEncoderSettings)MemberwiseClone();
        }
    }
}
