using CUETools.Codecs;
using CUETools.Codecs.CommandLine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace CUETools.TestCodecs
{
    [TestClass]
    public class CommandLineEncoderTest
    {
        [TestMethod]
        public void CloseAcceptsNonemptyOutputAndClosesProcessResources()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.OnStart = delegate
            {
                File.WriteAllBytes(
                    QuotedArgument(process.StartInfo.Arguments, 0),
                    new byte[] { 1, 2, 3 });
            };

            try
            {
                AudioEncoder encoder = new AudioEncoder(
                    CreateSettings("%O"), outputPath, null, new FakeEncoderProcessFactory(process));

                encoder.Close();

                Assert.IsTrue(process.StartInfo.RedirectStandardInput);
                Assert.IsFalse(process.StartInfo.RedirectStandardOutput);
                Assert.IsTrue(process.InputClosedBeforeDispose);
                Assert.IsTrue(process.Disposed);
                Assert.IsFalse(process.Killed);
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(outputPath));
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void ApprovedExecutableIsRecheckedAndLockedThroughLaunch()
        {
            string outputPath = NewOutputPath();
            string deferredOutputPath = NewOutputPath();
            string executablePath = NewOutputPath() + ".exe";
            byte[] approvedBytes = new byte[] { 1, 3, 3, 7 };
            File.WriteAllBytes(executablePath, approvedBytes);
            EncoderSettings settings = CreateSettings("%O");
            settings.Path = executablePath;
            settings.ApprovedExecutableLength = approvedBytes.Length;
            using (SHA256 hasher = SHA256.Create())
                settings.ApprovedExecutableSha256 =
                    BitConverter.ToString(
                        hasher.ComputeHash(approvedBytes))
                    .Replace("-", "");
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.OnStart = delegate
            {
                Assert.AreEqual(
                    executablePath,
                    process.StartInfo.FileName);
                Assert.IsTrue(File.Exists(executablePath));
                Assert.ThrowsException<IOException>(delegate
                {
                    using (new FileStream(
                        executablePath,
                        FileMode.Open,
                        FileAccess.Write,
                        FileShare.ReadWrite))
                    {
                    }
                });
                File.WriteAllBytes(
                    QuotedArgument(process.StartInfo.Arguments, 0),
                    new byte[] { 9 });
            };

            try
            {
                AudioEncoder encoder = new AudioEncoder(
                    settings,
                    outputPath,
                    null,
                    new FakeEncoderProcessFactory(process));
                encoder.Close();

                Assert.IsTrue(File.Exists(executablePath));
                CollectionAssert.AreEqual(
                    new byte[] { 9 },
                    File.ReadAllBytes(outputPath));

                File.WriteAllBytes(
                    executablePath,
                    new byte[] { 2, 4, 6, 8 });
                FakeEncoderProcess tamperedProcess =
                    new FakeEncoderProcess();
                InvalidDataException exception =
                    Assert.ThrowsException<InvalidDataException>(delegate
                    {
                        new AudioEncoder(
                            settings,
                            NewOutputPath(),
                            null,
                            new FakeEncoderProcessFactory(
                                tamperedProcess));
                    });
                StringAssert.Contains(
                    exception.Message,
                    "changed after approval");
                Assert.IsFalse(tamperedProcess.Started);

                File.WriteAllBytes(executablePath, approvedBytes);
                settings.Parameters = "%I %O";
                FakeEncoderProcess deferredProcess =
                    new FakeEncoderProcess();
                AudioEncoder deferredEncoder = new AudioEncoder(
                    settings,
                    deferredOutputPath,
                    null,
                    new FakeEncoderProcessFactory(deferredProcess));
                deferredEncoder.FinalSampleCount = 1;
                AudioBuffer deferredInput =
                    new AudioBuffer(AudioPCMConfig.RedBook, 1);
                deferredInput.Prepare(
                    new int[,] { { 0, 0 } },
                    1);
                deferredEncoder.Write(deferredInput);
                File.WriteAllBytes(
                    executablePath,
                    new byte[] { 8, 6, 4, 2 });
                IOException deferredFailure =
                    Assert.ThrowsException<IOException>(delegate
                    {
                        deferredEncoder.Close();
                    });
                StringAssert.Contains(
                    deferredFailure.ToString(),
                    "changed after approval");
                Assert.IsFalse(deferredProcess.Started);
                Assert.IsFalse(File.Exists(deferredOutputPath));
            }
            finally
            {
                DeleteIfExists(executablePath);
                DeleteIfExists(outputPath);
                DeleteIfExists(deferredOutputPath);
            }
        }

        [TestMethod]
        public void CloseCopiesRedirectedStandardOutput()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.Output = new MemoryStream(new byte[] { 4, 5, 6 });

            try
            {
                AudioEncoder encoder = new AudioEncoder(
                    CreateSettings("-"), outputPath, null, new FakeEncoderProcessFactory(process));

                encoder.Close();

                Assert.IsTrue(process.StartInfo.RedirectStandardOutput);
                CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, File.ReadAllBytes(outputPath));
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void CloseSurfacesRedirectedOutputCopyFailure()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.Output = new ThrowingReadStream();

            try
            {
                IOException exception = Assert.ThrowsException<IOException>(delegate
                {
                    AudioEncoder encoder = new AudioEncoder(
                        CreateSettings("-"), outputPath, null, new FakeEncoderProcessFactory(process));
                    encoder.Close();
                });

                StringAssert.Contains(exception.Message, "copying the external encoder's standard output");
                Assert.IsTrue(process.InputClosedBeforeDispose);
                Assert.IsTrue(process.Disposed);
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void CloseRejectsMissingOutputAfterSuccessfulExit()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();

            try
            {
                IOException exception = Assert.ThrowsException<IOException>(delegate
                {
                    AudioEncoder encoder = new AudioEncoder(
                        CreateSettings("%O"), outputPath, null, new FakeEncoderProcessFactory(process));
                    encoder.Close();
                });

                StringAssert.Contains(exception.Message, "did not create output file");
                Assert.IsTrue(process.InputClosedBeforeDispose);
                Assert.IsTrue(process.Disposed);
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void MissingDirectOutputDoesNotAcceptOrAlterStaleDestination()
        {
            string outputPath = NewOutputPath();
            byte[] original = new byte[] { 9, 8, 7, 6 };
            File.WriteAllBytes(outputPath, original);
            FakeEncoderProcess process = new FakeEncoderProcess();

            try
            {
                IOException exception = Assert.ThrowsException<IOException>(delegate
                {
                    AudioEncoder encoder = new AudioEncoder(
                        CreateSettings("%O"),
                        outputPath,
                        null,
                        new FakeEncoderProcessFactory(process));
                    encoder.Close();
                });

                StringAssert.Contains(exception.Message, "did not create output file");
                CollectionAssert.AreEqual(original, File.ReadAllBytes(outputPath));
                Assert.AreNotEqual(
                    Path.GetFullPath(outputPath),
                    QuotedArgument(process.StartInfo.Arguments, 0),
                    "The external process must never write directly to the requested path.");
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void SuccessfulDirectOutputAtomicallyReplacesExistingDestination()
        {
            string outputPath = NewOutputPath();
            File.WriteAllBytes(outputPath, new byte[] { 9, 9, 9 });
            FakeEncoderProcess process = new FakeEncoderProcess();
            string workPath = null;
            process.OnStart = delegate
            {
                workPath = QuotedArgument(process.StartInfo.Arguments, 0);
                File.WriteAllBytes(workPath, new byte[] { 3, 2, 1 });
            };

            try
            {
                AudioEncoder encoder = new AudioEncoder(
                    CreateSettings("%O"),
                    outputPath,
                    null,
                    new FakeEncoderProcessFactory(process));
                encoder.Close();

                CollectionAssert.AreEqual(new byte[] { 3, 2, 1 }, File.ReadAllBytes(outputPath));
                Assert.AreEqual(Path.GetExtension(outputPath), Path.GetExtension(workPath));
                Assert.IsFalse(File.Exists(workPath));
            }
            finally
            {
                DeleteIfExists(workPath);
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void DirectOutputDoesNotReplaceDestinationCreatedAfterStart()
        {
            string outputPath = NewOutputPath();
            byte[] competitor = new byte[] { 9, 8, 7, 6 };
            FakeEncoderProcess process = new FakeEncoderProcess();
            string workPath = null;
            process.OnStart = delegate
            {
                workPath = QuotedArgument(process.StartInfo.Arguments, 0);
                File.WriteAllBytes(workPath, new byte[] { 3, 2, 1 });
                File.WriteAllBytes(outputPath, competitor);
            };

            try
            {
                AudioEncoder encoder = new AudioEncoder(
                    CreateSettings("%O"),
                    outputPath,
                    null,
                    new FakeEncoderProcessFactory(process));

                Assert.ThrowsException<IOException>(delegate { encoder.Close(); });

                CollectionAssert.AreEqual(competitor, File.ReadAllBytes(outputPath));
                Assert.IsFalse(File.Exists(workPath));
                Assert.IsTrue(process.InputClosedBeforeDispose);
                Assert.IsTrue(process.Disposed);
            }
            finally
            {
                DeleteIfExists(workPath);
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void CloseRejectsEmptyOutputAfterSuccessfulExit()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.OnStart = delegate
            {
                File.WriteAllBytes(
                    QuotedArgument(process.StartInfo.Arguments, 0),
                    new byte[0]);
            };

            try
            {
                IOException exception = Assert.ThrowsException<IOException>(delegate
                {
                    AudioEncoder encoder = new AudioEncoder(
                        CreateSettings("%O"), outputPath, null, new FakeEncoderProcessFactory(process));
                    encoder.Close();
                });

                StringAssert.Contains(exception.Message, "empty output file");
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void CloseReportsNonzeroExitCode()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.ExitCodeValue = 17;
            process.OnStart = delegate
            {
                File.WriteAllBytes(
                    QuotedArgument(process.StartInfo.Arguments, 0),
                    new byte[] { 1 });
            };

            try
            {
                EncoderProcessExitException exception = Assert.ThrowsException<EncoderProcessExitException>(delegate
                {
                    AudioEncoder encoder = new AudioEncoder(
                        CreateSettings("%O"), outputPath, null, new FakeEncoderProcessFactory(process));
                    encoder.Close();
                });

                StringAssert.Contains(exception.Message, "returned error code 17");
                Assert.IsTrue(process.InputClosedBeforeDispose);
                Assert.IsTrue(process.Disposed);
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void CloseTerminatesAndCleansUpTimedOutProcess()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.WaitResult = false;

            try
            {
                EncoderSettings settings = CreateSettings("%O");
                settings.ProcessTimeoutMilliseconds = 1000;
                TimeoutException exception = Assert.ThrowsException<TimeoutException>(delegate
                {
                    AudioEncoder encoder = new AudioEncoder(
                        settings, outputPath, null, new FakeEncoderProcessFactory(process));
                    encoder.Close();
                });

                StringAssert.Contains(exception.Message, "1000 milliseconds");
                Assert.IsTrue(process.Killed);
                Assert.IsTrue(process.InputClosedBeforeDispose);
                Assert.IsTrue(process.Disposed);
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void TimeoutSurfacesKillFailure()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.WaitResult = false;
            process.KillException = new IOException("synthetic kill failure");

            try
            {
                EncoderSettings settings = CreateSettings("%O");
                settings.ProcessTimeoutMilliseconds = 1000;
                TimeoutException exception = Assert.ThrowsException<TimeoutException>(delegate
                {
                    AudioEncoder encoder = new AudioEncoder(
                        settings,
                        outputPath,
                        null,
                        new FakeEncoderProcessFactory(process));
                    encoder.Close();
                });

                StringAssert.Contains(exception.ToString(), "synthetic kill failure");
                StringAssert.Contains(exception.ToString(), "did not exit after termination");
                Assert.IsTrue(process.Killed);
                Assert.IsTrue(process.Disposed);
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void TimeoutSurfacesProcessThatRemainsAfterKill()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.WaitResult = false;
            process.KillLeavesRunning = true;

            try
            {
                EncoderSettings settings = CreateSettings("%O");
                settings.ProcessTimeoutMilliseconds = 1000;
                TimeoutException exception = Assert.ThrowsException<TimeoutException>(delegate
                {
                    AudioEncoder encoder = new AudioEncoder(
                        settings,
                        outputPath,
                        null,
                        new FakeEncoderProcessFactory(process));
                    encoder.Close();
                });

                StringAssert.Contains(exception.ToString(), "did not exit after termination");
                Assert.IsTrue(process.Killed);
                Assert.IsTrue(process.Disposed);
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void RuntimeTimerTerminatesProcessThatDoesNotFinish()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.WaitUntilKilled = true;

            try
            {
                EncoderSettings settings = CreateSettings("%O");
                settings.ProcessTimeoutMilliseconds = 100;
                Assert.ThrowsException<TimeoutException>(delegate
                {
                    AudioEncoder encoder = new AudioEncoder(
                        settings, outputPath, null, new FakeEncoderProcessFactory(process));
                    encoder.Close();
                });

                Assert.IsTrue(process.Killed);
                Assert.IsTrue(process.Disposed);
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void StaleEncoderTimeoutCallbackDoesNotKillAfterWatchdogRearm()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.OnStart = delegate
            {
                File.WriteAllBytes(
                    QuotedArgument(process.StartInfo.Arguments, 0),
                    new byte[] { 1, 2, 3 });
            };
            EncoderSettings settings = CreateSettings("%O");
            settings.ProcessTimeoutMilliseconds = 30000;
            AudioEncoder encoder = null;

            try
            {
                encoder = new AudioEncoder(
                    settings,
                    outputPath,
                    null,
                    new FakeEncoderProcessFactory(process));

                InvokeTimeoutCallback(encoder);

                Assert.IsFalse(
                    process.Killed,
                    "A callback queued for an older deadline must not terminate an active encoder.");
                encoder.Close();
                encoder = null;
            }
            finally
            {
                if (encoder != null)
                    encoder.Close();
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void StreamingWritesResetTheInactivityWatchdog()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.OnStart = delegate
            {
                File.WriteAllBytes(
                    QuotedArgument(process.StartInfo.Arguments, 0),
                    new byte[] { 1 });
            };

            try
            {
                EncoderSettings settings = CreateSettings("%O");
                settings.ProcessTimeoutMilliseconds = 150;
                AudioEncoder encoder = new AudioEncoder(
                    settings, outputPath, null, new FakeEncoderProcessFactory(process));
                AudioBuffer buffer = new AudioBuffer(AudioPCMConfig.RedBook, 1);
                buffer.Prepare(new int[,] { { 0, 0 } }, 1);

                for (int i = 0; i < 5; i++)
                {
                    encoder.Write(buffer);
                    Thread.Sleep(60);
                }
                encoder.Close();

                Assert.IsFalse(process.Killed,
                    "active streaming must not be treated as a total-runtime timeout");
                Assert.IsTrue(process.Disposed);
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void WriteFailureTerminatesAndDisposesProcessImmediately()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess process = new FakeEncoderProcess();

            try
            {
                AudioEncoder encoder = new AudioEncoder(
                    CreateSettings("%O"), outputPath, null,
                    new FakeEncoderProcessFactory(process));
                process.Input.ThrowOnWrite = true;
                AudioBuffer buffer = new AudioBuffer(AudioPCMConfig.RedBook, 1);
                buffer.Prepare(new int[,] { { 0, 0 } }, 1);

                IOException exception = Assert.ThrowsException<IOException>(
                    delegate { encoder.Write(buffer); });

                StringAssert.Contains(exception.Message, "synthetic input failure");
                Assert.IsTrue(process.Killed);
                Assert.IsTrue(process.InputClosedBeforeDispose);
                Assert.IsTrue(process.Disposed);
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void TemporaryInputLaunchIsDeferredAndTemporaryFileIsDeleted()
        {
            string outputPath = NewOutputPath();
            string temporaryPath = null;
            bool temporaryInputWasReady = false;
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.OnStart = delegate
            {
                temporaryPath = QuotedArgument(process.StartInfo.Arguments, 0);
                temporaryInputWasReady = File.Exists(temporaryPath) &&
                    new FileInfo(temporaryPath).Length > 0;
                File.WriteAllBytes(
                    QuotedArgument(process.StartInfo.Arguments, 1),
                    new byte[] { 7 });
            };

            try
            {
                AudioEncoder encoder = new AudioEncoder(
                    CreateSettings("%I %O"), outputPath, null, new FakeEncoderProcessFactory(process));
                Assert.IsFalse(process.Started);

                AudioBuffer buffer = new AudioBuffer(AudioPCMConfig.RedBook, 1);
                buffer.Prepare(new int[,] { { 0, 0 } }, 1);
                encoder.FinalSampleCount = 1;
                encoder.Write(buffer);
                encoder.Close();

                Assert.IsTrue(process.Started);
                Assert.IsTrue(temporaryInputWasReady);
                Assert.AreEqual(".wav", Path.GetExtension(temporaryPath));
                Assert.IsFalse(File.Exists(temporaryPath));
            }
            finally
            {
                DeleteIfExists(temporaryPath);
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void FailurePathSurfacesTemporaryInputDeletionFailure()
        {
            string outputPath = NewOutputPath();
            byte[] original = new byte[] { 4, 4, 4 };
            File.WriteAllBytes(outputPath, original);
            string temporaryPath = null;
            string workPath = null;
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.ExitCodeValue = 17;
            process.OnStart = delegate
            {
                temporaryPath = QuotedArgument(process.StartInfo.Arguments, 0);
                workPath = QuotedArgument(process.StartInfo.Arguments, 1);
                File.WriteAllBytes(workPath, new byte[] { 7 });
            };
            ThrowingDeleteFileOperations fileOperations =
                new ThrowingDeleteFileOperations(".wav");

            try
            {
                AudioEncoder encoder = new AudioEncoder(
                    CreateSettings("%I %O"),
                    outputPath,
                    null,
                    new FakeEncoderProcessFactory(process),
                    fileOperations);
                AudioBuffer buffer = new AudioBuffer(AudioPCMConfig.RedBook, 1);
                buffer.Prepare(new int[,] { { 0, 0 } }, 1);
                encoder.FinalSampleCount = 1;
                encoder.Write(buffer);

                IOException exception = Assert.ThrowsException<IOException>(
                    delegate { encoder.Close(); });

                StringAssert.Contains(exception.ToString(), "returned error code 17");
                StringAssert.Contains(exception.ToString(), "synthetic delete failure");
                CollectionAssert.AreEqual(original, File.ReadAllBytes(outputPath));
                Assert.IsFalse(File.Exists(workPath));
                Assert.IsTrue(File.Exists(temporaryPath));
            }
            finally
            {
                DeleteIfExists(temporaryPath);
                DeleteIfExists(workPath);
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void DeleteAbortsStreamingEncoderWithoutReplacingExistingDestination()
        {
            string outputPath = NewOutputPath();
            byte[] original = new byte[] { 9, 8, 7, 6 };
            File.WriteAllBytes(outputPath, original);
            string workPath = null;
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.OnStart = delegate
            {
                workPath = QuotedArgument(process.StartInfo.Arguments, 0);
                File.WriteAllBytes(workPath, new byte[] { 1, 2, 3 });
            };

            try
            {
                AudioEncoder encoder = new AudioEncoder(
                    CreateSettings("%O"),
                    outputPath,
                    null,
                    new FakeEncoderProcessFactory(process));

                encoder.Delete();

                CollectionAssert.AreEqual(original, File.ReadAllBytes(outputPath));
                Assert.IsTrue(process.Killed);
                Assert.IsTrue(process.InputClosedBeforeDispose);
                Assert.IsTrue(process.Disposed);
                Assert.IsFalse(File.Exists(workPath));
            }
            finally
            {
                DeleteIfExists(workPath);
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void DeleteDoesNotLaunchDeferredInputEncoder()
        {
            string outputPath = NewOutputPath();
            byte[] original = new byte[] { 4, 3, 2, 1 };
            File.WriteAllBytes(outputPath, original);
            FakeEncoderProcess process = new FakeEncoderProcess();

            try
            {
                AudioEncoder encoder = new AudioEncoder(
                    CreateSettings("%I %O"),
                    outputPath,
                    null,
                    new FakeEncoderProcessFactory(process));
                AudioBuffer buffer = new AudioBuffer(AudioPCMConfig.RedBook, 1);
                buffer.Prepare(new int[,] { { 0, 0 } }, 1);
                encoder.Write(buffer);

                encoder.Delete();

                Assert.IsFalse(process.Started);
                Assert.IsFalse(process.Disposed,
                    "No process object is created until a deferred-input encode is launched.");
                CollectionAssert.AreEqual(original, File.ReadAllBytes(outputPath));
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void ConstructorRejectsUnboundedTimeout()
        {
            EncoderSettings settings = CreateSettings("%O");
            settings.ProcessTimeoutMilliseconds = 0;

            Assert.ThrowsException<ArgumentOutOfRangeException>(delegate
            {
                new AudioEncoder(settings, NewOutputPath(), null,
                    new FakeEncoderProcessFactory(new FakeEncoderProcess()));
            });
        }

        [TestMethod]
        public void ConstructorDisposesProcessWhenLaunchFails()
        {
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.StartResult = false;

            Assert.ThrowsException<IOException>(delegate
            {
                new AudioEncoder(CreateSettings("%O"), NewOutputPath(), null,
                    new FakeEncoderProcessFactory(process));
            });

            Assert.IsTrue(process.Disposed);
            Assert.IsFalse(process.Killed);
        }

        [TestMethod]
        public void SettingsDefaultToFiniteTenMinuteTimeout()
        {
            Assert.AreEqual(600000, new EncoderSettings().ProcessTimeoutMilliseconds);
            Assert.AreEqual(600000, new DecoderSettings().ProcessTimeoutMilliseconds);
            Assert.AreEqual(
                600000,
                new DecoderSettings("fake", "fake", "fake.exe", "-d %I -")
                    .ProcessTimeoutMilliseconds);
        }

        [TestMethod]
        public void BuiltInLosslessCommandEncodersAllHaveSelfBoundVerifiers()
        {
            CUEToolsCodecsConfig config = new CUEToolsCodecsConfig();
            config.Init(
                new List<IAudioEncoderSettings>(),
                new List<IAudioDecoderSettings>());

            bool foundFlac = false;
            bool foundTak = false;
            bool foundFfmpeg = false;
            foreach (IAudioEncoderSettings item in config.encoders)
            {
                EncoderSettings command = item as EncoderSettings;
                if (command == null)
                    continue;

                Assert.AreNotEqual(
                    "flake.exe",
                    command.Name,
                    "The decoder-less historical command wrapper must not be advertised.");
                if (!command.Lossless)
                    continue;
                Assert.IsTrue(
                    command.HasLosslessVerifier,
                    command.Name + " has no independent lossless verifier.");
                Assert.IsTrue(
                    command.VerificationUsesEncoder,
                    command.Name + " must bind verification to its exact encoder executable.");
                Assert.AreEqual("", command.VerificationPath);

                foundFlac |= command.Name == "flac.exe";
                foundTak |= command.Name == "takc.exe";
                foundFfmpeg |= command.Name == "ffmpeg.exe";
            }

            Assert.IsTrue(foundFlac);
            Assert.IsTrue(foundTak);
            Assert.IsTrue(foundFfmpeg);
        }

        [TestMethod]
        public void LosslessVerifierContractRequiresOneDecoderIdentityAndInputToken()
        {
            EncoderSettings settings = CreateLosslessSettings("%O");
            Assert.IsTrue(settings.HasLosslessVerifier);

            settings.VerificationPath = "other-decoder.exe";
            Assert.IsFalse(
                settings.HasLosslessVerifier,
                "Self-bound and separate decoder identities may not both be selected.");
            settings.VerificationUsesEncoder = false;
            Assert.IsTrue(settings.HasLosslessVerifier);

            settings.VerificationParameters = "-d fixed-name.fake -";
            Assert.IsFalse(
                settings.HasLosslessVerifier,
                "The owned work path must be substituted explicitly.");
        }

        [TestMethod]
        public void LosslessVerificationRequirementCannotBeToggledOffInLiveSettings()
        {
            EncoderSettings settings = CreateLosslessSettings("%O");
            Assert.IsTrue(settings.VerificationRequired);

            settings.Lossless = false;

            Assert.IsTrue(
                settings.VerificationRequired,
                "A mutable type label must not disable verification for a configured lossless face.");
            Assert.IsTrue(settings.HasLosslessVerifier);
        }

        [TestMethod]
        public void VersionedOrConfiguredJsonCannotDisableLosslessVerificationRequirement()
        {
            EncoderSettings legacy = JsonConvert.DeserializeObject<EncoderSettings>(
                "{\"Lossless\":true,\"Path\":\"fake.exe\"," +
                "\"VerificationUsesEncoder\":true," +
                "\"VerificationParameters\":\"-d %I -\"," +
                "\"ApprovedExecutableSha256\":\"" +
                new String('a', 64) + "\"," +
                "\"ApprovedExecutableLength\":123}");
            Assert.IsTrue(legacy.VerificationRequired);
            Assert.IsTrue(legacy.HasLosslessVerifier);
            Assert.IsTrue(String.IsNullOrEmpty(
                legacy.ApprovedExecutableSha256));
            Assert.AreEqual(0, legacy.ApprovedExecutableLength);

            EncoderSettings downgrade =
                JsonConvert.DeserializeObject<EncoderSettings>(
                    "{\"Lossless\":true,\"VerificationContractVersion\":1," +
                    "\"VerificationRequired\":false," +
                    "\"Path\":\"fake.exe\",\"VerificationUsesEncoder\":true," +
                    "\"VerificationParameters\":\"-d %I -\"}");
            Assert.IsTrue(
                downgrade.VerificationRequired,
                "A later JSON property may not downgrade the sticky lossless contract.");
        }

        [TestMethod]
        public void PreContractCustomLosslessEncoderRemainsUsableButExplicitlyUnverified()
        {
            EncoderSettings legacy =
                JsonConvert.DeserializeObject<EncoderSettings>(
                    "{\"Name\":\"custom.exe\",\"Extension\":\"custom\"," +
                    "\"Lossless\":true,\"Path\":\"custom.exe\"," +
                    "\"Parameters\":\"%O\"}");

            Assert.IsFalse(legacy.VerificationRequired);
            Assert.IsFalse(legacy.HasLosslessVerifier);
            Assert.IsTrue(legacy.UsesLegacyUnverifiedCompatibility);

            legacy.VerificationUsesEncoder = true;
            legacy.VerificationParameters = "-d %I -";

            Assert.IsTrue(legacy.VerificationRequired);
            Assert.IsTrue(legacy.HasLosslessVerifier);
            Assert.IsFalse(legacy.UsesLegacyUnverifiedCompatibility);
            Assert.AreEqual(1, legacy.VerificationContractVersion);
        }

        [TestMethod]
        public void LosslessEncoderWithoutIndependentVerifierFailsBeforeProcessStart()
        {
            FakeEncoderProcess process = new FakeEncoderProcess();
            EncoderSettings settings = CreateSettings("%O");
            settings.Lossless = true;

            InvalidOperationException exception =
                Assert.ThrowsException<InvalidOperationException>(delegate
                {
                    new AudioEncoder(
                        settings,
                        NewOutputPath(),
                        null,
                        new FakeEncoderProcessFactory(process));
                });

            StringAssert.Contains(exception.Message, "independent decoder");
            Assert.IsFalse(process.Started);
        }

        [TestMethod]
        public void LosslessEncoderPublishesOnlyAfterExactDecodedPcmMatch()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess encoderProcess = CreateOutputProcess();
            FakeEncoderProcess verifierProcess = new FakeEncoderProcess();
            AudioBuffer source = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 1, -2 }, { 300, -400 }, { 32767, -32768 } });
            verifierProcess.Output = new MemoryStream(CreateWaveBytes(source));

            try
            {
                AudioEncoder encoder = CreateLosslessEncoder(
                    outputPath,
                    encoderProcess,
                    verifierProcess);
                encoder.Write(source);
                encoder.Close();

                Assert.IsTrue(File.Exists(outputPath));
                CollectionAssert.AreEqual(
                    new byte[] { 1, 2, 3 },
                    File.ReadAllBytes(outputPath));
                Assert.AreEqual(
                    "fake-encoder.exe",
                    verifierProcess.StartInfo.FileName,
                    "Self-verification must use the exact configured encoder path.");
                Assert.IsTrue(verifierProcess.Disposed);
                Assert.IsFalse(verifierProcess.Killed);
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void LosslessProcessAndVerifierContractIsFrozenAtConstruction()
        {
            string outputPath = NewOutputPath();
            FakeEncoderProcess encoderProcess =
                new FakeEncoderProcess();
            FakeEncoderProcess verifierProcess =
                new FakeEncoderProcess();
            AudioBuffer source = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 1, -2 }, { 300, -400 } });
            verifierProcess.Output =
                new MemoryStream(CreateWaveBytes(source));
            encoderProcess.OnStart = delegate
            {
                Assert.IsTrue(
                    File.Exists(
                        QuotedArgument(
                            encoderProcess.StartInfo.Arguments,
                            0)),
                    "The frozen %I contract must point at the completed owned WAV.");
                File.WriteAllBytes(
                    QuotedArgument(
                        encoderProcess.StartInfo.Arguments,
                        1),
                    new byte[] { 7, 8, 9 });
            };
            EncoderSettings settings =
                CreateLosslessSettings("%I %O");

            try
            {
                AudioEncoder encoder = new AudioEncoder(
                    settings,
                    outputPath,
                    null,
                    new FakeEncoderProcessFactory(encoderProcess),
                    new SystemEncoderFileOperations(),
                    new FakeEncoderProcessFactory(verifierProcess));

                settings.Path = "swapped-encoder.exe";
                settings.Parameters = "swapped arguments";
                settings.EncoderMode = "swapped mode";
                settings.Padding = 99;
                settings.VerificationUsesEncoder = false;
                settings.VerificationPath = "swapped-decoder.exe";
                settings.VerificationParameters =
                    "swapped verification arguments %I";
                settings.ProcessTimeoutMilliseconds = 1;

                encoder.FinalSampleCount = source.Length;
                encoder.Write(source);
                encoder.Close();

                Assert.AreEqual(
                    "fake-encoder.exe",
                    encoderProcess.StartInfo.FileName);
                Assert.AreEqual(
                    "fake-encoder.exe",
                    verifierProcess.StartInfo.FileName,
                    "Self-verification must retain the exact encoder identity selected at construction.");
                StringAssert.StartsWith(
                    verifierProcess.StartInfo.Arguments,
                    "-d ");
                Assert.IsFalse(
                    verifierProcess.StartInfo.Arguments.Contains(
                        "swapped"));
                CollectionAssert.AreEqual(
                    new byte[] { 7, 8, 9 },
                    File.ReadAllBytes(outputPath));
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void DecodedPcmHashMismatchPreservesStaleDestination()
        {
            AudioBuffer source = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 1, 2 }, { 3, 4 } });
            AudioBuffer altered = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 1, 2 }, { 3, 5 } });
            AssertLosslessVerificationFailurePreservesDestination(
                source,
                CreateWaveBytes(altered),
                "SHA-256 mismatch");
        }

        [TestMethod]
        public void DecodedPcmSampleCountMismatchPreservesStaleDestination()
        {
            AudioBuffer source = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 1, 2 }, { 3, 4 } });
            AudioBuffer truncated = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 1, 2 } });
            AssertLosslessVerificationFailurePreservesDestination(
                source,
                CreateWaveBytes(truncated),
                "sample-count mismatch");
        }

        [TestMethod]
        public void DecodedPcmFormatMismatchPreservesStaleDestination()
        {
            AudioBuffer source = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 1, 2 }, { 3, 4 } });
            AudioPCMConfig wrongRate = new AudioPCMConfig(16, 2, 48000);
            AudioBuffer decoded = CreatePcmBuffer(
                wrongRate,
                new int[,] { { 1, 2 }, { 3, 4 } });
            AssertLosslessVerificationFailurePreservesDestination(
                source,
                CreateWaveBytes(decoded),
                "PCM format mismatch");
        }

        [TestMethod]
        public void NonzeroVerifierExitPreventsPublication()
        {
            AudioBuffer source = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 11, -11 } });
            FakeEncoderProcess verifierProcess = new FakeEncoderProcess();
            verifierProcess.Output = new MemoryStream(CreateWaveBytes(source));
            verifierProcess.ExitCodeValue = 23;

            AssertVerifierProcessFailurePreservesDestination(
                source,
                verifierProcess,
                "decoder error code 23");
        }

        [TestMethod]
        public void VerifierFinalizationTimeoutTerminatesProcessAndPreventsPublication()
        {
            AudioBuffer source = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 11, -11 } });
            FakeEncoderProcess verifierProcess = new FakeEncoderProcess();
            verifierProcess.Output = new MemoryStream(CreateWaveBytes(source));
            verifierProcess.WaitResult = false;

            AssertVerifierProcessFailurePreservesDestination(
                source,
                verifierProcess,
                "made no decode or exit progress");
            Assert.IsTrue(verifierProcess.Killed);
            Assert.IsTrue(verifierProcess.Disposed);
        }

        [TestMethod]
        public void VerifierCleanupFailurePreventsPublication()
        {
            AudioBuffer source = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 11, -11 } });
            FakeEncoderProcess verifierProcess = new FakeEncoderProcess();
            verifierProcess.Output = new MemoryStream(CreateWaveBytes(source));
            verifierProcess.DisposeException =
                new IOException("synthetic verifier dispose failure");

            AssertVerifierProcessFailurePreservesDestination(
                source,
                verifierProcess,
                "synthetic verifier dispose failure");
            Assert.IsTrue(verifierProcess.Disposed);
        }

        [TestMethod]
        public void CommandDecoderRejectsUnboundedTimeoutAndMissingInputToken()
        {
            DecoderSettings noTimeout = new DecoderSettings(
                "fake", "fake", "fake.exe", "-d %I -");
            noTimeout.ProcessTimeoutMilliseconds = 0;
            Assert.ThrowsException<ArgumentOutOfRangeException>(delegate
            {
                new AudioDecoder(
                    noTimeout,
                    NewOutputPath(),
                    null,
                    new FakeEncoderProcessFactory(new FakeEncoderProcess()));
            });

            DecoderSettings noInput = new DecoderSettings(
                "fake", "fake", "fake.exe", "-d input.fake -");
            Assert.ThrowsException<ArgumentException>(delegate
            {
                new AudioDecoder(
                    noInput,
                    NewOutputPath(),
                    null,
                    new FakeEncoderProcessFactory(new FakeEncoderProcess()));
            });
        }

        [TestMethod]
        public void RealFfmpegAlacSelfVerificationRunsWhenExecutableIsAvailable()
        {
            string ffmpeg = FindExecutableOnPath("ffmpeg.exe");
            if (ffmpeg == null)
                Assert.Inconclusive(
                    "ffmpeg.exe is not installed; external ALAC runtime capability was not exercised.");

            string outputPath = Path.Combine(
                Path.GetTempPath(),
                "cuetools-command-line-" + Guid.NewGuid().ToString("N") +
                    ".m4a");
            int[,] samples = new int[4096, 2];
            for (int i = 0; i < samples.GetLength(0); i++)
            {
                samples[i, 0] = (i * 97) % 65536 - 32768;
                samples[i, 1] = (i * 193) % 65536 - 32768;
            }
            AudioBuffer source = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                samples);
            EncoderSettings settings = new EncoderSettings(
                "ffmpeg.exe",
                "m4a",
                true,
                "",
                "",
                "ffmpeg.exe",
                "-hide_banner -loglevel error -i - -f ipod -acodec alac -y %O");
            settings.PCM = AudioPCMConfig.RedBook;
            settings.VerificationUsesEncoder = true;
            settings.VerificationParameters =
                "-hide_banner -v error -i %I -f wav -";
            settings.ProcessTimeoutMilliseconds = 30000;
            settings.ApprovedExecutableLength =
                new FileInfo(ffmpeg).Length;
            using (SHA256 hasher = SHA256.Create())
            using (FileStream executable = File.OpenRead(ffmpeg))
                settings.ApprovedExecutableSha256 =
                    BitConverter.ToString(
                        hasher.ComputeHash(executable))
                    .Replace("-", "");

            try
            {
                Assert.AreEqual(
                    Path.GetFullPath(ffmpeg),
                    AudioEncoder.ResolveExecutablePath(
                        settings.Path),
                    true,
                    "Production command encoders must resolve a bare PATH name to one exact executable.");
                AudioEncoder encoder = new AudioEncoder(settings, outputPath);
                encoder.FinalSampleCount = source.Length;
                encoder.Write(source);
                encoder.Close();

                Assert.IsTrue(File.Exists(outputPath));
                Assert.IsTrue(new FileInfo(outputPath).Length > 0);
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        [TestMethod]
        public void ClosingPartiallyConsumedCommandDecoderTerminatesAndDisposesProcess()
        {
            AudioBuffer decoded = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 1, 2 }, { 3, 4 } });
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.Output = new MemoryStream(CreateWaveBytes(decoded));
            DecoderSettings settings = new DecoderSettings(
                "fake", "fake", "fake-decoder.exe", "-d %I -");
            AudioDecoder decoder = new AudioDecoder(
                settings,
                NewOutputPath(),
                null,
                new FakeEncoderProcessFactory(process));

            Assert.AreEqual(2, decoder.Length);
            decoder.Close();

            Assert.IsTrue(process.Killed);
            Assert.IsTrue(process.Disposed);
            Assert.IsTrue(process.StartInfo.RedirectStandardOutput);
            Assert.IsFalse(process.StartInfo.RedirectStandardError,
                "stderr is inherited, not left as an unconsumed redirected pipe");
        }

        [TestMethod]
        public void CommandDecoderForwardSeekConsumesWaveStdoutAndRejectsBackwardSeek()
        {
            AudioBuffer decoded = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,]
                {
                    { 10, -10 },
                    { 20, -20 },
                    { 30, -30 },
                    { 40, -40 },
                    { 50, -50 }
                });
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.Output = new NonSeekableReadStream(CreateWaveBytes(decoded));
            DecoderSettings settings = new DecoderSettings(
                "fake", "fake", "fake-decoder.exe", "-d %I -");
            AudioDecoder decoder = new AudioDecoder(
                settings,
                NewOutputPath(),
                null,
                new FakeEncoderProcessFactory(process));

            try
            {
                decoder.Position = 2;
                Assert.AreEqual(2L, decoder.Position);

                AudioBuffer actual = new AudioBuffer(decoder.PCM, 2);
                Assert.AreEqual(2, decoder.Read(actual, 2));
                Assert.AreEqual(4L, decoder.Position);
                Assert.AreEqual(30, actual.Samples[0, 0]);
                Assert.AreEqual(-30, actual.Samples[0, 1]);
                Assert.AreEqual(40, actual.Samples[1, 0]);
                Assert.AreEqual(-40, actual.Samples[1, 1]);

                NotSupportedException backward =
                    Assert.ThrowsException<NotSupportedException>(
                        delegate { decoder.Position = 1; });
                StringAssert.Contains(backward.Message, "Backward seeking");
                Assert.AreEqual(
                    4L,
                    decoder.Position,
                    "A rejected backward seek must not mutate the logical stream position.");

                AudioBuffer tail = new AudioBuffer(decoder.PCM, 1);
                Assert.AreEqual(1, decoder.Read(tail, 1));
                Assert.AreEqual(50, tail.Samples[0, 0]);
                Assert.AreEqual(-50, tail.Samples[0, 1]);
                Assert.AreEqual(5L, decoder.Position);
                Assert.AreEqual(0, decoder.Read(tail, 1));
            }
            finally
            {
                decoder.Close();
            }

            Assert.IsFalse(process.Killed);
            Assert.IsTrue(process.Disposed);
        }

        [TestMethod]
        public void StaleDecoderTimeoutCallbackDoesNotKillAfterWatchdogRearm()
        {
            AudioBuffer decoded = CreatePcmBuffer(
                AudioPCMConfig.RedBook,
                new int[,] { { 1, 2 }, { 3, 4 } });
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.Output = new MemoryStream(CreateWaveBytes(decoded));
            DecoderSettings settings = new DecoderSettings(
                "fake", "fake", "fake-decoder.exe", "-d %I -");
            settings.ProcessTimeoutMilliseconds = 30000;
            AudioDecoder decoder = new AudioDecoder(
                settings,
                NewOutputPath(),
                null,
                new FakeEncoderProcessFactory(process));

            try
            {
                AudioPCMConfig ignored = decoder.PCM;
                InvokeTimeoutCallback(decoder);

                Assert.IsFalse(
                    process.Killed,
                    "A callback queued for an older deadline must not terminate an active decoder.");
            }
            finally
            {
                decoder.Close();
            }
        }

        [TestMethod]
        public void CommandDecoderHeaderInactivityWatchdogUnblocksReadAndTerminates()
        {
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.Output = new KillAwareBlockingReadStream();
            DecoderSettings settings = new DecoderSettings(
                "fake", "fake", "fake-decoder.exe", "-d %I -");
            settings.ProcessTimeoutMilliseconds = 100;
            AudioDecoder decoder = new AudioDecoder(
                settings,
                NewOutputPath(),
                null,
                new FakeEncoderProcessFactory(process));
            Stopwatch stopwatch = Stopwatch.StartNew();

            TimeoutException exception =
                Assert.ThrowsException<TimeoutException>(
                    delegate { AudioPCMConfig ignored = decoder.PCM; });

            stopwatch.Stop();
            StringAssert.Contains(exception.Message, "made no decode or exit progress");
            Assert.IsTrue(process.Killed);
            Assert.IsTrue(process.Disposed);
            Assert.IsTrue(
                stopwatch.Elapsed < TimeSpan.FromSeconds(5),
                "The watchdog must break a blocked stdout read within a finite bound.");
        }

        [TestMethod]
        public void CommandDecoderPreservesReadFailureWhenTerminationAlsoFails()
        {
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.Output = new ThrowingReadStream();
            process.KillException =
                new IOException("synthetic decoder kill failure");
            DecoderSettings settings = new DecoderSettings(
                "fake", "fake", "fake-decoder.exe", "-d %I -");
            AudioDecoder decoder = new AudioDecoder(
                settings,
                NewOutputPath(),
                null,
                new FakeEncoderProcessFactory(process));

            IOException exception = Assert.ThrowsException<IOException>(
                delegate { AudioPCMConfig ignored = decoder.PCM; });

            StringAssert.Contains(exception.ToString(), "synthetic read failure");
            StringAssert.Contains(exception.ToString(), "synthetic decoder kill failure");
            Assert.IsNotNull(exception.InnerException);
            Assert.IsTrue(process.Disposed);
        }

        private static AudioEncoder CreateLosslessEncoder(
            string outputPath,
            FakeEncoderProcess encoderProcess,
            FakeEncoderProcess verifierProcess)
        {
            return new AudioEncoder(
                CreateLosslessSettings("%O"),
                outputPath,
                null,
                new FakeEncoderProcessFactory(encoderProcess),
                new SystemEncoderFileOperations(),
                new FakeEncoderProcessFactory(verifierProcess));
        }

        private static EncoderSettings CreateLosslessSettings(
            string parameters)
        {
            EncoderSettings settings = new EncoderSettings(
                "fake",
                "fake",
                true,
                "",
                "",
                "fake-encoder.exe",
                parameters);
            settings.PCM = AudioPCMConfig.RedBook;
            settings.VerificationUsesEncoder = true;
            settings.VerificationParameters = "-d %I -";
            settings.ProcessTimeoutMilliseconds = 1000;
            return settings;
        }

        private static FakeEncoderProcess CreateOutputProcess()
        {
            FakeEncoderProcess process = new FakeEncoderProcess();
            process.OnStart = delegate
            {
                File.WriteAllBytes(
                    QuotedArgument(process.StartInfo.Arguments, 0),
                    new byte[] { 1, 2, 3 });
            };
            return process;
        }

        private static AudioBuffer CreatePcmBuffer(
            AudioPCMConfig pcm,
            int[,] samples)
        {
            AudioBuffer buffer = new AudioBuffer(pcm, samples.GetLength(0));
            buffer.Prepare(samples, samples.GetLength(0));
            return buffer;
        }

        private static byte[] CreateWaveBytes(AudioBuffer buffer)
        {
            MemoryStream stream = new MemoryStream();
            CUETools.Codecs.WAV.AudioEncoder writer =
                new CUETools.Codecs.WAV.AudioEncoder(
                    new CUETools.Codecs.WAV.EncoderSettings(buffer.PCM),
                    "",
                    stream);
            writer.FinalSampleCount = buffer.Length;
            writer.Write(buffer);
            writer.Close();
            return stream.ToArray();
        }

        private static void AssertLosslessVerificationFailurePreservesDestination(
            AudioBuffer source,
            byte[] decodedWave,
            string expectedMessage)
        {
            FakeEncoderProcess verifierProcess = new FakeEncoderProcess();
            verifierProcess.Output = new MemoryStream(decodedWave);
            AssertVerifierProcessFailurePreservesDestination(
                source,
                verifierProcess,
                expectedMessage);
        }

        private static void AssertVerifierProcessFailurePreservesDestination(
            AudioBuffer source,
            FakeEncoderProcess verifierProcess,
            string expectedMessage)
        {
            string outputPath = NewOutputPath();
            byte[] original = new byte[] { 9, 8, 7, 6 };
            File.WriteAllBytes(outputPath, original);
            FakeEncoderProcess encoderProcess = CreateOutputProcess();

            try
            {
                Exception exception = null;
                try
                {
                    AudioEncoder encoder = CreateLosslessEncoder(
                        outputPath,
                        encoderProcess,
                        verifierProcess);
                    encoder.Write(source);
                    encoder.Close();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }

                Assert.IsNotNull(exception, "Verification failure must prevent publication.");
                StringAssert.Contains(exception.ToString(), expectedMessage);
                CollectionAssert.AreEqual(
                    original,
                    File.ReadAllBytes(outputPath));
                Assert.AreEqual(
                    0,
                    Directory.GetFiles(
                        Path.GetDirectoryName(outputPath),
                        "." + Path.GetFileNameWithoutExtension(outputPath) +
                            ".cuetools-output-*").Length,
                    "A rejected work output must be deleted, never published or stranded.");
            }
            finally
            {
                DeleteIfExists(outputPath);
            }
        }

        private static EncoderSettings CreateSettings(string parameters)
        {
            return new EncoderSettings(
                "fake", "fake", false, "", "", "fake-encoder.exe", parameters)
            {
                PCM = AudioPCMConfig.RedBook
            };
        }

        private static string NewOutputPath()
        {
            return System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "cuetools-command-line-" + Guid.NewGuid().ToString("N") + ".bin");
        }

        private static string FindExecutableOnPath(string fileName)
        {
            foreach (string directory in
                (Environment.GetEnvironmentVariable("PATH") ?? "")
                    .Split(Path.PathSeparator))
            {
                if (String.IsNullOrEmpty(directory))
                    continue;
                try
                {
                    string candidate = Path.Combine(directory.Trim(), fileName);
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch
                {
                    // Ignore malformed PATH entries; capability remains unexercised if none work.
                }
            }
            return null;
        }

        private static void DeleteIfExists(string path)
        {
            if (path != null && File.Exists(path))
                File.Delete(path);
        }

        private static string QuotedArgument(string arguments, int index)
        {
            int found = -1;
            int position = 0;
            while (position < arguments.Length)
            {
                int start = arguments.IndexOf('"', position);
                if (start < 0)
                    break;
                int end = arguments.IndexOf('"', start + 1);
                if (end < 0)
                    break;
                found++;
                if (found == index)
                    return arguments.Substring(start + 1, end - start - 1);
                position = end + 1;
            }
            throw new AssertFailedException("Quoted argument " + index + " was not found.");
        }

        private static void InvokeTimeoutCallback(object owner)
        {
            MethodInfo callback = owner.GetType().GetMethod(
                "ProcessTimedOut",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(callback);
            callback.Invoke(owner, new object[] { null });
        }

        private sealed class FakeEncoderProcessFactory : IEncoderProcessFactory
        {
            private readonly FakeEncoderProcess _process;

            public FakeEncoderProcessFactory(FakeEncoderProcess process)
            {
                _process = process;
            }

            public IEncoderProcess Create(ProcessStartInfo startInfo)
            {
                _process.StartInfo = startInfo;
                return _process;
            }
        }

        private sealed class FakeEncoderProcess : IEncoderProcess
        {
            private bool _hasExited;
            private readonly ManualResetEvent _killSignal = new ManualResetEvent(false);

            public ProcessStartInfo StartInfo { get; set; }
            public TrackingWriteStream Input { get; private set; }
            public Stream Output { get; set; }
            public Action OnStart { get; set; }
            public bool WaitResult { get; set; }
            public bool StartResult { get; set; }
            public bool WaitUntilKilled { get; set; }
            public int ExitCodeValue { get; set; }
            public bool Killed { get; private set; }
            public bool KillLeavesRunning { get; set; }
            public Exception KillException { get; set; }
            public Exception DisposeException { get; set; }
            public bool Started { get; private set; }
            public bool Disposed { get; private set; }
            public bool InputClosedBeforeDispose { get; private set; }

            public FakeEncoderProcess()
            {
                Input = new TrackingWriteStream();
                Output = new MemoryStream();
                WaitResult = true;
                StartResult = true;
            }

            public bool Start()
            {
                if (!StartResult)
                    return false;
                Started = true;
                if (OnStart != null)
                    OnStart();
                return true;
            }

            public bool HasExited
            {
                get { return _hasExited; }
            }

            public int ExitCode
            {
                get { return ExitCodeValue; }
            }

            public Stream StandardInput
            {
                get { return Input; }
            }

            public Stream StandardOutput
            {
                get { return Output; }
            }

            public ProcessPriorityClass PriorityClass
            {
                set { }
            }

            public bool WaitForExit(int milliseconds)
            {
                if (WaitUntilKilled)
                {
                    bool killed = _killSignal.WaitOne(milliseconds + 1000);
                    _hasExited = killed;
                    return killed;
                }
                if (WaitResult)
                    _hasExited = true;
                return WaitResult;
            }

            public void Kill()
            {
                Killed = true;
                if (KillException != null)
                    throw KillException;
                if (!KillLeavesRunning)
                {
                    _hasExited = true;
                    _killSignal.Set();
                    KillAwareBlockingReadStream blocking =
                        Output as KillAwareBlockingReadStream;
                    if (blocking != null)
                        blocking.ReleaseAfterKill();
                }
            }

            public void Dispose()
            {
                InputClosedBeforeDispose = Input.IsClosed;
                Disposed = true;
                Input.Close();
                Output.Close();
                _killSignal.Close();
                if (DisposeException != null)
                    throw DisposeException;
            }
        }

        private sealed class NonSeekableReadStream : Stream
        {
            private readonly MemoryStream _inner;

            internal NonSeekableReadStream(byte[] bytes)
            {
                _inner = new MemoryStream(bytes, false);
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override void Flush() { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return _inner.Read(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override void Close()
            {
                _inner.Close();
                base.Close();
            }
        }

        private sealed class ThrowingDeleteFileOperations : IEncoderFileOperations
        {
            private readonly string _throwingExtension;
            private readonly SystemEncoderFileOperations _inner =
                new SystemEncoderFileOperations();

            internal ThrowingDeleteFileOperations(string throwingExtension)
            {
                _throwingExtension = throwingExtension;
            }

            public bool Exists(string path)
            {
                return _inner.Exists(path);
            }

            public long Length(string path)
            {
                return _inner.Length(path);
            }

            public Stream CreateNew(string path)
            {
                return _inner.CreateNew(path);
            }

            public void Delete(string path)
            {
                if (String.Equals(
                    Path.GetExtension(path),
                    _throwingExtension,
                    StringComparison.OrdinalIgnoreCase))
                    throw new IOException("synthetic delete failure");
                _inner.Delete(path);
            }

            public void Move(string sourcePath, string destinationPath)
            {
                _inner.Move(sourcePath, destinationPath);
            }

            public void Replace(string sourcePath, string destinationPath)
            {
                _inner.Replace(sourcePath, destinationPath);
            }
        }

        private sealed class TrackingWriteStream : Stream
        {
            private readonly MemoryStream _inner = new MemoryStream();

            public bool IsClosed { get; private set; }
            public bool ThrowOnWrite { get; set; }

            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override void Flush()
            {
                _inner.Flush();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (ThrowOnWrite)
                    throw new IOException("synthetic input failure");
                _inner.Write(buffer, offset, count);
            }

            public override void Close()
            {
                IsClosed = true;
                _inner.Close();
                base.Close();
            }
        }

        private sealed class ThrowingReadStream : Stream
        {
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new IOException("synthetic read failure");
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class KillAwareBlockingReadStream : Stream
        {
            private readonly ManualResetEvent _killed =
                new ManualResetEvent(false);

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public void ReleaseAfterKill()
            {
                _killed.Set();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                _killed.WaitOne();
                return 0;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }
            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }
            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
            public override void Close()
            {
                _killed.Set();
                _killed.Close();
                base.Close();
            }
        }
    }
}
