using System;
using System.IO;
using System.Security.Cryptography;

namespace CUETools.Codecs.CommandLine
{
    /// <summary>
    /// Proves a command-line lossless encode by decoding the complete owned work file through an
    /// independent process and comparing canonical PCM format, sample count, and SHA-256.
    /// </summary>
    internal static class LosslessOutputVerifier
    {
        private const int VerificationBufferSamples = 65536;

        internal static void Verify(
            string encoderName,
            string extension,
            string decoderPath,
            string verificationParameters,
            int processTimeoutMilliseconds,
            AudioPCMConfig expectedPcm,
            string encodedPath,
            long expectedSampleCount,
            byte[] expectedDigest,
            IEncoderProcessFactory processFactory)
        {
            if (String.IsNullOrEmpty(decoderPath))
                throw new ArgumentException(
                    "The independent lossless verification decoder path is missing.",
                    "decoderPath");
            if (String.IsNullOrEmpty(verificationParameters) ||
                !verificationParameters.Contains("%I"))
                throw new ArgumentException(
                    "Independent lossless verification arguments must contain %I.",
                    "verificationParameters");
            if (processTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(
                    "processTimeoutMilliseconds");
            if (expectedPcm == null)
                throw new ArgumentNullException("expectedPcm");
            if (expectedDigest == null)
                throw new ArgumentNullException("expectedDigest");

            DecoderSettings decoderSettings = new DecoderSettings(
                (encoderName ?? String.Empty) + " verifier",
                extension ?? String.Empty,
                decoderPath,
                verificationParameters);
            decoderSettings.ProcessTimeoutMilliseconds =
                processTimeoutMilliseconds;

            AudioDecoder decoder = null;
            Exception operationFailure = null;
            try
            {
                decoder = new AudioDecoder(
                    decoderSettings,
                    encodedPath,
                    null,
                    processFactory);
                VerifyDecodedPcm(
                    decoder,
                    expectedPcm,
                    expectedSampleCount,
                    expectedDigest);
            }
            catch (Exception ex)
            {
                operationFailure = ex;
            }

            Exception cleanupFailure = null;
            if (decoder != null)
            {
                try
                {
                    decoder.Close();
                }
                catch (Exception ex)
                {
                    cleanupFailure = ex;
                }
            }

            if (operationFailure != null && cleanupFailure != null)
            {
                IOException combined = new IOException(
                    "Independent lossless verification failed and decoder cleanup was incomplete. " +
                    "Primary failure: " + operationFailure.Message +
                    " Secondary failure: " + cleanupFailure.Message,
                    operationFailure);
                combined.Data["SecondaryFailure"] = cleanupFailure;
                throw combined;
            }
            if (operationFailure != null)
            {
                ExceptionRelay.Throw(operationFailure);
                return;
            }
            if (cleanupFailure != null)
                throw new IOException(
                    "Independent lossless verification succeeded, but decoder cleanup was incomplete.",
                    cleanupFailure);
        }

        private static void VerifyDecodedPcm(
            AudioDecoder decoder,
            AudioPCMConfig expectedPcm,
            long expectedSampleCount,
            byte[] expectedDigest)
        {
            AudioPCMConfig actualPcm = decoder.PCM;
            if (!FormatsMatch(expectedPcm, actualPcm))
                throw new IOException(String.Format(
                    "Independent lossless verification found a PCM format mismatch. " +
                    "Expected {0}-bit/{1}-channel/{2} Hz/mask {3}; decoded " +
                    "{4}-bit/{5}-channel/{6} Hz/mask {7}.",
                    expectedPcm.BitsPerSample,
                    expectedPcm.ChannelCount,
                    expectedPcm.SampleRate,
                    (int)NormalizeChannelMask(expectedPcm),
                    actualPcm.BitsPerSample,
                    actualPcm.ChannelCount,
                    actualPcm.SampleRate,
                    (int)NormalizeChannelMask(actualPcm)));

            long actualSampleCount = 0;
            byte[] actualDigest;
            using (SHA256 hasher = SHA256.Create())
            {
                AudioBuffer buffer = new AudioBuffer(
                    decoder,
                    VerificationBufferSamples);
                int count;
                while ((count = decoder.Read(
                    buffer,
                    VerificationBufferSamples)) != 0)
                {
                    try
                    {
                        actualSampleCount = checked(
                            actualSampleCount + count);
                    }
                    catch (OverflowException ex)
                    {
                        throw new IOException(
                            "Independent lossless verification sample count overflowed.",
                            ex);
                    }

                    byte[] bytes = buffer.Bytes;
                    hasher.TransformBlock(
                        bytes,
                        0,
                        buffer.ByteLength,
                        bytes,
                        0);
                }
                hasher.TransformFinalBlock(new byte[0], 0, 0);
                actualDigest = hasher.Hash;
            }

            if (actualSampleCount != expectedSampleCount)
                throw new IOException(String.Format(
                    "Independent lossless verification found a sample-count mismatch: " +
                    "expected {0}, decoded {1}.",
                    expectedSampleCount,
                    actualSampleCount));
            if (!DigestsEqual(expectedDigest, actualDigest))
                throw new IOException(
                    "Independent lossless verification found a decoded PCM SHA-256 mismatch.");
        }

        private static bool FormatsMatch(
            AudioPCMConfig expected,
            AudioPCMConfig actual)
        {
            return expected != null &&
                actual != null &&
                expected.BitsPerSample == actual.BitsPerSample &&
                expected.ChannelCount == actual.ChannelCount &&
                expected.SampleRate == actual.SampleRate &&
                NormalizeChannelMask(expected) ==
                    NormalizeChannelMask(actual);
        }

        private static AudioPCMConfig.SpeakerConfig NormalizeChannelMask(
            AudioPCMConfig pcm)
        {
            return pcm.ChannelMask == AudioPCMConfig.SpeakerConfig.DIRECTOUT
                ? AudioPCMConfig.GetDefaultChannelMask(pcm.ChannelCount)
                : pcm.ChannelMask;
        }

        private static bool DigestsEqual(byte[] expected, byte[] actual)
        {
            if (expected == null || actual == null ||
                expected.Length != actual.Length)
                return false;
            int difference = 0;
            for (int i = 0; i < expected.Length; i++)
                difference |= expected[i] ^ actual[i];
            return difference == 0;
        }
    }
}
