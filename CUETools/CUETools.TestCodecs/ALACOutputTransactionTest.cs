using System;
using System.IO;
using CUETools.Codecs;
using CUETools.Codecs.ALAC;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.TestCodecs
{
    [TestClass]
    public class ALACOutputTransactionTest
    {
        private string root;

        [TestInitialize]
        public void SetUp()
        {
            root = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cuetools-alac-output-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TestCleanup]
        public void CleanUp()
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch
            {
            }
        }

        [TestMethod]
        public void RequestedPathIsNotVisibleUntilFinalization()
        {
            string path = System.IO.Path.Combine(root, "new.m4a");
            int[,] samples = MakeSamples(257);
            var encoder = CreateEncoder(path, samples.GetLength(0));

            encoder.Write(new AudioBuffer(
                AudioPCMConfig.RedBook,
                samples,
                samples.GetLength(0)));

            Assert.IsFalse(File.Exists(path));
            Assert.AreEqual(1, WorkFiles().Length);

            encoder.Close();

            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(new FileInfo(path).Length > 0);
            Assert.AreEqual(0, WorkFiles().Length);
        }

        [TestMethod]
        public void SampleCountFailurePreservesExistingDestinationAndCleansWork()
        {
            string path = System.IO.Path.Combine(root, "existing.m4a");
            byte[] original = { 0x43, 0x55, 0x45, 0x54 };
            File.WriteAllBytes(path, original);
            int[,] samples = MakeSamples(31);
            var encoder = CreateEncoder(path, samples.GetLength(0) + 1);
            encoder.Write(new AudioBuffer(
                AudioPCMConfig.RedBook,
                samples,
                samples.GetLength(0)));

            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.ThrowsException<Exception>(() => encoder.Close());
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            Assert.AreEqual(0, WorkFiles().Length);
            Assert.IsFalse(File.Exists(path + ".tmp"));
        }

        [TestMethod]
        public void DestinationCreatedAfterStartWinsPublicationRace()
        {
            string path = System.IO.Path.Combine(root, "race.m4a");
            byte[] competitor = { 9, 8, 7, 6 };
            int[,] samples = MakeSamples(63);
            var encoder = CreateEncoder(path, samples.GetLength(0));
            encoder.Write(new AudioBuffer(
                AudioPCMConfig.RedBook,
                samples,
                samples.GetLength(0)));

            File.WriteAllBytes(path, competitor);

            Assert.ThrowsException<IOException>(() => encoder.Close());
            CollectionAssert.AreEqual(competitor, File.ReadAllBytes(path));
            Assert.AreEqual(0, WorkFiles().Length);
        }

        [TestMethod]
        public void SuccessfulCloseReplacesDestinationThatExistedAtStart()
        {
            string path = System.IO.Path.Combine(root, "replace.m4a");
            byte[] original = { 1, 2, 3, 4 };
            File.WriteAllBytes(path, original);
            int[,] samples = MakeSamples(127);
            var encoder = CreateEncoder(path, samples.GetLength(0));
            encoder.Write(new AudioBuffer(
                AudioPCMConfig.RedBook,
                samples,
                samples.GetLength(0)));

            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            encoder.Close();
            encoder.Close();

            CollectionAssert.AreNotEqual(original, File.ReadAllBytes(path));
            Assert.AreEqual(0, WorkFiles().Length);
        }

        private AudioEncoder CreateEncoder(string path, int finalSampleCount)
        {
            var settings = new EncoderSettings
            {
                PCM = AudioPCMConfig.RedBook,
                DoVerify = true
            };
            var encoder = new AudioEncoder(settings, path, null);
            encoder.FinalSampleCount = finalSampleCount;
            return encoder;
        }

        private string[] WorkFiles()
        {
            return Directory.GetFiles(root, "*.cuetools-lossless-*");
        }

        private static int[,] MakeSamples(int count)
        {
            var samples = new int[count, AudioPCMConfig.RedBook.ChannelCount];
            for (int sample = 0; sample < count; sample++)
            {
                samples[sample, 0] = (sample * 97) & 0x7fff;
                samples[sample, 1] = -((sample * 193) & 0x7fff);
            }
            return samples;
        }
    }
}
