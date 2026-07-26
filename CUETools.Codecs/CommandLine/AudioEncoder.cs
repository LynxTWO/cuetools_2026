using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace CUETools.Codecs.CommandLine
{
    internal interface IEncoderProcessFactory
    {
        IEncoderProcess Create(ProcessStartInfo startInfo);
    }

    internal interface IEncoderProcess : IDisposable
    {
        bool Start();
        bool HasExited { get; }
        int ExitCode { get; }
        Stream StandardInput { get; }
        Stream StandardOutput { get; }
        ProcessPriorityClass PriorityClass { set; }
        bool WaitForExit(int milliseconds);
        void Kill();
    }

    internal sealed class SystemEncoderProcessFactory : IEncoderProcessFactory
    {
        public IEncoderProcess Create(ProcessStartInfo startInfo)
        {
            return new SystemEncoderProcess(startInfo);
        }
    }

    internal sealed class SystemEncoderProcess : IEncoderProcess
    {
        private readonly Process _process;

        public SystemEncoderProcess(ProcessStartInfo startInfo)
        {
            _process = new Process();
            _process.StartInfo = startInfo;
        }

        public bool Start()
        {
            return _process.Start();
        }

        public bool HasExited
        {
            get { return _process.HasExited; }
        }

        public int ExitCode
        {
            get { return _process.ExitCode; }
        }

        public Stream StandardInput
        {
            get { return _process.StandardInput.BaseStream; }
        }

        public Stream StandardOutput
        {
            get { return _process.StandardOutput.BaseStream; }
        }

        public ProcessPriorityClass PriorityClass
        {
            set { _process.PriorityClass = value; }
        }

        public bool WaitForExit(int milliseconds)
        {
            return _process.WaitForExit(milliseconds);
        }

        public void Kill()
        {
            MethodInfo killTree = typeof(Process).GetMethod(
                "Kill",
                new Type[] { typeof(bool) });
            if (killTree == null)
            {
                _process.Kill();
                return;
            }

            Exception treeFailure = null;
            try
            {
                killTree.Invoke(_process, new object[] { true });
                return;
            }
            catch (TargetInvocationException ex)
            {
                treeFailure = ex.InnerException ?? ex;
            }
            catch (Exception ex)
            {
                treeFailure = ex;
            }

            try
            {
                _process.Kill();
            }
            catch (Exception directFailure)
            {
                throw new IOException(
                    "Process-tree termination failed, and direct-child termination also failed: " +
                    directFailure.Message,
                    treeFailure);
            }

            throw new IOException(
                "Process-tree termination failed; direct-child termination was requested as a fallback.",
                treeFailure);
        }

        public void Dispose()
        {
            _process.Dispose();
        }
    }

    internal interface IEncoderFileOperations
    {
        bool Exists(string path);
        long Length(string path);
        Stream CreateNew(string path);
        void Delete(string path);
        void Move(string sourcePath, string destinationPath);
        void Replace(string sourcePath, string destinationPath);
    }

    internal sealed class SystemEncoderFileOperations : IEncoderFileOperations
    {
        public bool Exists(string path)
        {
            return File.Exists(path);
        }

        public long Length(string path)
        {
            return new FileInfo(path).Length;
        }

        public Stream CreateNew(string path)
        {
            return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }

        public void Delete(string path)
        {
            File.Delete(path);
        }

        public void Move(string sourcePath, string destinationPath)
        {
            File.Move(sourcePath, destinationPath);
        }

        public void Replace(string sourcePath, string destinationPath)
        {
            File.Replace(sourcePath, destinationPath, null);
        }
    }

    internal sealed class EncoderProcessExitException : IOException
    {
        public EncoderProcessExitException(string message)
            : base(message)
        {
        }

        public EncoderProcessExitException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    internal sealed class EncoderOutputPump
    {
        private readonly Stream _input;
        private readonly Stream _output;
        private Thread _thread;
        private Exception _failure;

        public EncoderOutputPump(Stream input, Stream output)
        {
            _input = input;
            _output = output;
            _thread = new Thread(CopyOutput);
            _thread.IsBackground = true;
            _thread.Name = "CUETools external encoder output";
            try
            {
                _thread.Start();
            }
            catch
            {
                _thread = null;
                _output.Close();
                throw;
            }
        }

        private void CopyOutput()
        {
            byte[] buffer = new byte[128 * 1024];
            try
            {
                int count;
                while ((count = _input.Read(buffer, 0, buffer.Length)) != 0)
                    _output.Write(buffer, 0, count);
                _output.Flush();
            }
            catch (Exception ex)
            {
                _failure = ex;
            }
            finally
            {
                try
                {
                    _output.Close();
                }
                catch (Exception ex)
                {
                    if (_failure == null)
                        _failure = ex;
                }
            }
        }

        public void Complete(int milliseconds)
        {
            Thread thread = _thread;
            if (thread == null)
                return;

            if (!thread.Join(milliseconds))
            {
                try
                {
                    _input.Close();
                }
                catch
                {
                }

                if (!thread.Join(ProcessTerminationWaitMilliseconds))
                    throw new TimeoutException("Timed out while draining the external encoder's standard output.");
            }

            _thread = null;
            if (_failure != null)
                throw new IOException("Failed while copying the external encoder's standard output.", _failure);
        }

        private const int ProcessTerminationWaitMilliseconds = 5000;
    }

    public class AudioEncoder : IAudioDest
    {
        private const int ProcessTerminationWaitMilliseconds = 5000;

        private readonly string _path;
        private readonly string _workPath;
        private readonly IEncoderProcessFactory _processFactory;
        private readonly IEncoderProcessFactory _verificationProcessFactory;
        private readonly IEncoderFileOperations _fileOperations;
        private readonly bool _destinationExistedAtStart;
        private readonly string _encoderName;
        private readonly string _extension;
        private readonly string _encoderExecutablePath;
        private readonly string _approvedExecutableSha256;
        private readonly long _approvedExecutableLength;
        private readonly string _encoderParameters;
        private readonly string _encoderMode;
        private readonly int _encoderPadding;
        private readonly AudioPCMConfig _pcm;
        private readonly bool _verificationRequired;
        private readonly string _verificationExecutablePath;
        private readonly string _verificationParameters;
        private readonly int _processTimeoutMilliseconds;
        private readonly object _processSync = new object();
        private readonly bool useTempFile;
        private readonly string tempFile;
        private IEncoderProcess _encoderProcess;
        private WAV.AudioEncoder wrt;
        private Stream _encoderInputStream;
        private EncoderOutputPump outputPump;
        private Timer _processTimeoutTimer;
        private long _processTimeoutDeadline;
        private bool _processStarted;
        private bool _processCompleted;
        private volatile bool _timedOut;
        private bool _timeoutCallbackActive;
        private Exception _timeoutTerminationFailure;
        private SHA256 _inputHasher;
        private long _inputSampleCount;
        private byte[] _inputDigest;
        private bool _publishedOutput;
        private FileStream _approvedExecutableLease;
        private bool closed;

        public long Position
        {
            get
            {
                return wrt.Position;
            }
        }

        public long FinalSampleCount
        {
            set { wrt.FinalSampleCount = value; }
        }

        // Must not start a %I process in the constructor, so that the temporary
        // WAV is complete before the external encoder opens it.
        private readonly EncoderSettings m_settings;
        public IAudioEncoderSettings Settings { get { return m_settings; } }

        public string Path { get { return _path; } }

        public AudioEncoder(EncoderSettings settings, string path, Stream IO = null)
            : this(
                settings,
                path,
                IO,
                new SystemEncoderProcessFactory(),
                new SystemEncoderFileOperations(),
                new SystemEncoderProcessFactory())
        {
        }

        internal AudioEncoder(EncoderSettings settings, string path, Stream IO, IEncoderProcessFactory processFactory)
            : this(
                settings,
                path,
                IO,
                processFactory,
                new SystemEncoderFileOperations(),
                new SystemEncoderProcessFactory())
        {
        }

        internal AudioEncoder(
            EncoderSettings settings,
            string path,
            Stream IO,
            IEncoderProcessFactory processFactory,
            IEncoderFileOperations fileOperations)
            : this(
                settings,
                path,
                IO,
                processFactory,
                fileOperations,
                new SystemEncoderProcessFactory())
        {
        }

        internal AudioEncoder(
            EncoderSettings settings,
            string path,
            Stream IO,
            IEncoderProcessFactory processFactory,
            IEncoderFileOperations fileOperations,
            IEncoderProcessFactory verificationProcessFactory)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            if (String.IsNullOrEmpty(path))
                throw new ArgumentNullException("path");
            if (processFactory == null)
                throw new ArgumentNullException("processFactory");
            if (fileOperations == null)
                throw new ArgumentNullException("fileOperations");
            if (verificationProcessFactory == null)
                throw new ArgumentNullException("verificationProcessFactory");
            if (settings.ProcessTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException("settings", "ProcessTimeoutMilliseconds must be greater than zero.");
            if (settings.PCM == null)
                throw new ArgumentException("PCM must be configured.", "settings");
            if (String.IsNullOrEmpty(settings.Path))
                throw new ArgumentException(
                    "An external encoder executable path is required.",
                    "settings");
            if (settings.VerificationRequired && !settings.HasLosslessVerifier)
                throw new InvalidOperationException(
                    "Lossless command-line encoders require an independent decoder verification contract.");

            m_settings = settings;
            _path = System.IO.Path.GetFullPath(path);
            _processFactory = processFactory;
            _verificationProcessFactory = verificationProcessFactory;
            _fileOperations = fileOperations;
            _destinationExistedAtStart = fileOperations.Exists(_path);
            _encoderName = settings.Name ?? String.Empty;
            _extension = settings.Extension ?? String.Empty;
            _pcm = settings.PCM;
            _encoderParameters = settings.Parameters ?? String.Empty;
            _encoderMode = settings.EncoderMode ?? String.Empty;
            _encoderPadding = settings.Padding;
            _processTimeoutMilliseconds =
                settings.ProcessTimeoutMilliseconds;
            _verificationRequired = settings.VerificationRequired;
            _encoderExecutablePath =
                processFactory is SystemEncoderProcessFactory
                    ? ResolveExecutablePath(settings.Path)
                    : settings.Path;
            _approvedExecutableSha256 =
                settings.ApprovedExecutableSha256 ?? String.Empty;
            _approvedExecutableLength =
                settings.ApprovedExecutableLength;
            ValidateApprovedExecutableContract();
            _verificationParameters =
                settings.VerificationParameters ?? String.Empty;
            if (_verificationRequired)
            {
                _verificationExecutablePath =
                    settings.VerificationUsesEncoder
                        ? _encoderExecutablePath
                        : verificationProcessFactory is
                            SystemEncoderProcessFactory
                            ? ResolveExecutablePath(
                                settings.VerificationPath)
                            : settings.VerificationPath;
            }
            else
            {
                _verificationExecutablePath = String.Empty;
            }

            useTempFile = _encoderParameters.Contains("%I");
            _workPath = CreateOwnedSiblingPath(_path, "output", null);
            tempFile = CreateOwnedSiblingPath(_path, "input", ".wav");

            if (useTempFile)
            {
                wrt = new WAV.AudioEncoder(
                    new WAV.EncoderSettings(_pcm),
                    tempFile);
                if (_verificationRequired)
                    _inputHasher = SHA256.Create();
                return;
            }

            if (_verificationRequired)
                _inputHasher = SHA256.Create();
            try
            {
                StartEncoderProcess();
                InitializeProcessStreams();
            }
            catch (Exception ex)
            {
                Exception cleanupFailure = CleanupAfterFailure();
                if (cleanupFailure != null)
                    throw CombineFailure(
                        "External encoder initialization failed.",
                        ex,
                        cleanupFailure);
                throw;
            }
        }

        internal static string ResolveExecutablePath(string configuredPath)
        {
            if (String.IsNullOrEmpty(configuredPath))
                throw new ArgumentException(
                    "An external encoder executable path is required.",
                    "configuredPath");

            bool hasDirectory =
                System.IO.Path.IsPathRooted(configuredPath) ||
                configuredPath.IndexOf(
                    System.IO.Path.DirectorySeparatorChar) >= 0 ||
                configuredPath.IndexOf(
                    System.IO.Path.AltDirectorySeparatorChar) >= 0;
            if (hasDirectory)
            {
                string explicitPath =
                    System.IO.Path.GetFullPath(configuredPath);
                if (File.Exists(explicitPath))
                    return explicitPath;
                throw new FileNotFoundException(
                    "The configured external encoder executable was not found.",
                    explicitPath);
            }

            string currentDirectoryPath =
                System.IO.Path.GetFullPath(configuredPath);
            if (File.Exists(currentDirectoryPath))
                return currentDirectoryPath;

            string applicationPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    configuredPath));
            if (File.Exists(applicationPath))
                return applicationPath;

            string environmentPath =
                Environment.GetEnvironmentVariable("PATH") ??
                String.Empty;
            string[] directories = environmentPath.Split(
                System.IO.Path.PathSeparator);
            for (int i = 0; i < directories.Length; i++)
            {
                string directory = directories[i].Trim().Trim('"');
                if (directory.Length == 0)
                    continue;
                try
                {
                    string candidate = System.IO.Path.GetFullPath(
                        System.IO.Path.Combine(
                            directory,
                            configuredPath));
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                    // Ignore malformed PATH entries.
                }
                catch (NotSupportedException)
                {
                    // Ignore malformed PATH entries.
                }
                catch (PathTooLongException)
                {
                    // Ignore malformed PATH entries.
                }
                catch (System.Security.SecurityException)
                {
                    // An inaccessible PATH entry cannot supply the exact
                    // executable identity required by verification.
                }
            }

            throw new FileNotFoundException(
                "The configured external encoder executable could not be " +
                "resolved to an exact file.",
                configuredPath);
        }

        private void ValidateApprovedExecutableContract()
        {
            if (_approvedExecutableSha256.Length == 0)
            {
                if (_approvedExecutableLength != 0)
                    throw new InvalidDataException(
                        "The approved external encoder identity is incomplete.");
                return;
            }

            if (_approvedExecutableLength <= 0 ||
                _approvedExecutableSha256.Length != 64)
                throw new InvalidDataException(
                    "The approved external encoder identity is invalid.");
            for (int i = 0; i < _approvedExecutableSha256.Length; i++)
            {
                char value = _approvedExecutableSha256[i];
                bool hex =
                    (value >= '0' && value <= '9') ||
                    (value >= 'a' && value <= 'f') ||
                    (value >= 'A' && value <= 'F');
                if (!hex)
                    throw new InvalidDataException(
                        "The approved external encoder identity is invalid.");
            }
        }

        private static string CreateOwnedSiblingPath(
            string requestedPath,
            string purpose,
            string extension)
        {
            string fullPath = System.IO.Path.GetFullPath(requestedPath);
            string directory = System.IO.Path.GetDirectoryName(fullPath);
            string requestedExtension = System.IO.Path.GetExtension(fullPath);
            string requestedName = System.IO.Path.GetFileNameWithoutExtension(fullPath);
            string workExtension = extension ?? requestedExtension;
            return System.IO.Path.Combine(
                directory,
                "." + requestedName + ".cuetools-" + purpose + "-" +
                Guid.NewGuid().ToString("N") + workExtension);
        }

        private ProcessStartInfo CreateStartInfo()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = _encoderExecutablePath;
            startInfo.Arguments = _encoderParameters
                .Replace("%O", "\"" + _workPath + "\"")
                .Replace("%M", _encoderMode)
                .Replace("%P", _encoderPadding.ToString())
                .Replace("%I", "\"" + tempFile + "\"");
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardInput = !useTempFile;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput =
                !_encoderParameters.Contains("%O");
            return startInfo;
        }

        private void StartEncoderProcess()
        {
            Exception startException = null;
            bool started = false;

            AcquireApprovedExecutableLease();
            _encoderProcess = _processFactory.Create(CreateStartInfo());
            try
            {
                started = _encoderProcess.Start();
            }
            catch (Exception ex)
            {
                startException = ex;
            }

            if (!started)
            {
                IOException failure = new IOException(
                    _encoderExecutablePath + ": " +
                    (startException == null ? "please check the path" : startException.Message),
                    startException);
                Exception disposeFailure = DisposeProcess();
                if (disposeFailure != null)
                    throw CombineFailure(
                        "The external encoder could not be started or disposed.",
                        failure,
                        disposeFailure);
                throw PrepareRethrow(failure);
            }

            lock (_processSync)
            {
                _processStarted = true;
                _processTimeoutDeadline =
                    ProcessTimeoutDeadline.FromNow(_processTimeoutMilliseconds);
                _processTimeoutTimer = new Timer(
                    ProcessTimedOut, null, Timeout.Infinite, Timeout.Infinite);
                _processTimeoutTimer.Change(
                    _processTimeoutMilliseconds, Timeout.Infinite);
            }
            SetProcessPriority();
        }

        private void AcquireApprovedExecutableLease()
        {
            if (_approvedExecutableSha256.Length == 0 ||
                _approvedExecutableLease != null)
                return;

            string sourcePath =
                System.IO.Path.GetFullPath(_encoderExecutablePath);
            string directory = System.IO.Path.GetDirectoryName(sourcePath);
            if (String.IsNullOrEmpty(directory))
                throw new InvalidDataException(
                    "The approved external encoder has no containing directory.");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException(
                    "The approved external encoder directory became a reparse point.");

            FileStream lease = null;
            try
            {
                lease = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                if ((File.GetAttributes(sourcePath) &
                    FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException(
                        "The approved external encoder became a reparse point.");
                RequireApprovedIdentity(lease);

                // Keep this exact, verified file object read-only and non-replaceable until the
                // encoder and any self-verifier have exited. CreateProcess reads the same path,
                // while FileShare.Read denies content changes, rename, and deletion.
                _approvedExecutableLease = lease;
                lease = null;
            }
            catch
            {
                if (lease != null)
                    lease.Close();
                throw;
            }
        }

        private void RequireApprovedIdentity(Stream stream)
        {
            SHA256 hasher = SHA256.Create();
            try
            {
                stream.Position = 0;
                byte[] digest = hasher.ComputeHash(stream);
                RequireApprovedIdentity(stream.Length, ToHex(digest));
                stream.Position = 0;
            }
            finally
            {
                hasher.Clear();
            }
        }

        private void RequireApprovedIdentity(long length, string sha256)
        {
            if (length != _approvedExecutableLength ||
                !String.Equals(
                    sha256,
                    _approvedExecutableSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "The managed external encoder changed after approval; launch was refused.");
        }

        private static string ToHex(byte[] bytes)
        {
            const string Hex = "0123456789abcdef";
            char[] result = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                result[i * 2] = Hex[bytes[i] >> 4];
                result[i * 2 + 1] = Hex[bytes[i] & 15];
            }
            return new String(result);
        }

        private void SetProcessPriority()
        {
            try
            {
                using (Process currentProcess = Process.GetCurrentProcess())
                    _encoderProcess.PriorityClass = currentProcess.PriorityClass;
            }
            catch
            {
                // Priority inheritance is best effort and must not fail an encode.
            }
        }

        private void InitializeProcessStreams()
        {
            if (!_encoderParameters.Contains("%O"))
            {
                Stream outputStream = _fileOperations.CreateNew(_workPath);
                outputPump = new EncoderOutputPump(_encoderProcess.StandardOutput, outputStream);
            }

            if (!useTempFile)
            {
                _encoderInputStream = _encoderProcess.StandardInput;
                wrt = new WAV.AudioEncoder(
                    new WAV.EncoderSettings(_pcm),
                    _path,
                    _encoderInputStream);
            }
        }

        private void ProcessTimedOut(object state)
        {
            lock (_processSync)
            {
                if (_processTimeoutTimer == null ||
                    _processCompleted)
                    return;
                int remainingMilliseconds =
                    ProcessTimeoutDeadline.RemainingMilliseconds(
                        _processTimeoutDeadline);
                if (remainingMilliseconds > 0)
                {
                    // A callback for the previous deadline may already have been queued before
                    // progress called Change. Re-arm it against the current monotonic deadline.
                    _processTimeoutTimer.Change(
                        remainingMilliseconds, Timeout.Infinite);
                    return;
                }
                _timedOut = true;
                _timeoutCallbackActive = true;
            }

            Exception terminationFailure = null;
            try
            {
                terminationFailure = TerminateProcess(false);
            }
            finally
            {
                lock (_processSync)
                {
                    _timeoutTerminationFailure = CombineSecondaryFailure(
                        _timeoutTerminationFailure,
                        terminationFailure);
                    _timeoutCallbackActive = false;
                    Monitor.PulseAll(_processSync);
                }
            }
        }

        private void ResetProcessTimeout()
        {
            lock (_processSync)
            {
                if (!_processStarted || _processCompleted || _processTimeoutTimer == null)
                    return;
                _processTimeoutDeadline =
                    ProcessTimeoutDeadline.FromNow(_processTimeoutMilliseconds);
                _processTimeoutTimer.Change(_processTimeoutMilliseconds,
                    Timeout.Infinite);
            }
        }

        public void Close()
        {
            if (closed)
                return;
            closed = true;

            Exception operationFailure = null;
            try
            {
                WAV.AudioEncoder writer = wrt;
                wrt = null;
                if (writer != null)
                    writer.Close();

                if (useTempFile)
                {
                    StartEncoderProcess();
                    InitializeProcessStreams();
                }

                WaitForEncoderExit();
                CloseOutputPump();
                ValidateOutput();
                VerifyLosslessOutput();
            }
            catch (Exception ex)
            {
                operationFailure = NormalizeProcessFailure(ex);
            }

            Exception cleanupFailure = CleanupResources();
            if (operationFailure != null)
            {
                Exception ownedCleanupFailure = CleanupOwnedWorkFiles();
                cleanupFailure = CombineSecondaryFailure(
                    cleanupFailure,
                    ownedCleanupFailure);
                if (cleanupFailure != null)
                    throw CombineFailure(
                        "External encoder failure was followed by cleanup or termination failure.",
                        operationFailure,
                        cleanupFailure);
                throw PrepareRethrow(operationFailure);
            }

            if (cleanupFailure != null)
            {
                Exception ownedCleanupFailure = CleanupOwnedWorkFiles();
                cleanupFailure = CombineSecondaryFailure(
                    cleanupFailure,
                    ownedCleanupFailure);
                throw new IOException(
                    "The external encoder succeeded, but its resources could not be cleaned up; output was not published.",
                    cleanupFailure);
            }

            try
            {
                PublishOutput();
            }
            catch (Exception ex)
            {
                Exception ownedCleanupFailure = CleanupOwnedWorkFiles();
                if (ownedCleanupFailure != null)
                    throw CombineFailure(
                        "External encoder output publication failed.",
                        ex,
                        ownedCleanupFailure);
                throw;
            }
        }

        private void WaitForEncoderExit()
        {
            bool exited = _encoderProcess.HasExited ||
                _encoderProcess.WaitForExit(
                    _processTimeoutMilliseconds);

            if (!exited)
            {
                Exception terminationFailure = TerminateProcess(true);
                terminationFailure = CombineSecondaryFailure(
                    terminationFailure,
                    GetTimeoutTerminationFailure());
                throw CreateTimeoutException(terminationFailure);
            }

            StopProcessTimer();
            if (_timedOut)
                throw CreateTimeoutException(GetTimeoutTerminationFailure());

            int exitCode = _encoderProcess.ExitCode;
            if (exitCode != 0)
                throw new EncoderProcessExitException(String.Format(
                    "{0} returned error code {1}",
                    _encoderExecutablePath,
                    exitCode));
        }

        private void ValidateOutput()
        {
            if (!_fileOperations.Exists(_workPath))
                throw new IOException(String.Format(
                    "{0} exited successfully but did not create output file \"{1}\".",
                    _encoderExecutablePath,
                    _path));

            if (_fileOperations.Length(_workPath) == 0)
                throw new IOException(String.Format(
                    "{0} exited successfully but created an empty output file \"{1}\".",
                    _encoderExecutablePath,
                    _path));
        }

        private void VerifyLosslessOutput()
        {
            if (!_verificationRequired)
                return;

            if (_inputDigest == null)
            {
                _inputHasher.TransformFinalBlock(new byte[0], 0, 0);
                _inputDigest = _inputHasher.Hash;
            }
            LosslessOutputVerifier.Verify(
                _encoderName,
                _extension,
                _verificationExecutablePath,
                _verificationParameters,
                _processTimeoutMilliseconds,
                _pcm,
                _workPath,
                _inputSampleCount,
                _inputDigest,
                _verificationProcessFactory);
        }

        private void PublishOutput()
        {
            if (_destinationExistedAtStart)
            {
                _fileOperations.Replace(_workPath, _path);
            }
            else
            {
                // Move is intentionally create-only. If a competing writer creates the requested
                // path while encoding, fail publication without replacing that writer's bytes.
                _fileOperations.Move(_workPath, _path);
            }

            _publishedOutput = true;
        }

        private TimeoutException CreateTimeoutException(Exception innerException)
        {
            return new TimeoutException(String.Format(
                "{0} did not exit within {1} milliseconds; termination was requested{2}.",
                _encoderExecutablePath,
                _processTimeoutMilliseconds,
                innerException == null
                    ? String.Empty
                    : ", but termination did not complete cleanly: " + innerException.Message),
                innerException);
        }

        private Exception NormalizeProcessFailure(Exception failure)
        {
            bool timedOut = _timedOut;
            int exitCode;
            bool nonzeroExit = TryGetExitedCode(out exitCode) && exitCode != 0;
            Exception terminationFailure = TerminateProcess(false);
            terminationFailure = CombineSecondaryFailure(
                terminationFailure,
                GetTimeoutTerminationFailure());

            Exception normalized = failure;
            if (!(failure is TimeoutException) &&
                !(failure is EncoderProcessExitException))
            {
                if (timedOut)
                    normalized = CreateTimeoutException(failure);
                else if (nonzeroExit)
                    normalized = new EncoderProcessExitException(String.Format(
                        "{0} returned error code {1}",
                        _encoderExecutablePath,
                        exitCode),
                        failure);
            }

            if (terminationFailure != null)
                normalized = CombineFailure(
                    "The external encoder operation failed and process termination was incomplete.",
                    normalized,
                    terminationFailure);
            return normalized;
        }

        private bool TryGetExitedCode(out int exitCode)
        {
            exitCode = 0;
            if (!_processStarted || _encoderProcess == null)
                return false;

            try
            {
                if (!_encoderProcess.HasExited)
                    return false;
                exitCode = _encoderProcess.ExitCode;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private Exception TerminateProcess(bool timedOut)
        {
            Exception failure = null;
            bool terminationNeeded = false;
            IEncoderProcess process = null;
            lock (_processSync)
            {
                if (_processStarted && !_processCompleted && _encoderProcess != null)
                {
                    process = _encoderProcess;
                    try
                    {
                        terminationNeeded = !process.HasExited;
                    }
                    catch (Exception ex)
                    {
                        // If exit state cannot be read, still request termination.
                        terminationNeeded = true;
                        failure = ex;
                    }

                    if (terminationNeeded)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception ex)
                        {
                            failure = CombineSecondaryFailure(failure, ex);
                        }
                    }
                }

                if (timedOut)
                    _timedOut = true;
            }

            if (terminationNeeded && process != null)
            {
                bool exited = false;
                try
                {
                    exited = process.HasExited ||
                        process.WaitForExit(ProcessTerminationWaitMilliseconds);
                }
                catch (Exception ex)
                {
                    failure = CombineSecondaryFailure(failure, ex);
                }

                if (!exited)
                    failure = CombineSecondaryFailure(
                        failure,
                        new IOException(
                            "The external encoder did not exit after termination was requested."));
            }

            StopProcessTimer();
            return failure;
        }

        private Exception GetTimeoutTerminationFailure()
        {
            lock (_processSync)
            {
                while (_timeoutCallbackActive)
                    Monitor.Wait(_processSync);
                return _timeoutTerminationFailure;
            }
        }

        private void StopProcessTimer()
        {
            Timer timer = null;
            lock (_processSync)
            {
                _processCompleted = true;
                timer = _processTimeoutTimer;
                _processTimeoutTimer = null;
            }
            if (timer != null)
                timer.Dispose();
        }

        private Exception CloseInputStream()
        {
            Stream inputStream = _encoderInputStream;
            _encoderInputStream = null;
            if (inputStream == null)
                return null;

            try
            {
                inputStream.Close();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private void CloseOutputPump()
        {
            EncoderOutputPump pump = outputPump;
            outputPump = null;
            if (pump != null)
                pump.Complete(_processTimeoutMilliseconds);
        }

        private Exception DisposeProcess()
        {
            IEncoderProcess process = _encoderProcess;
            _encoderProcess = null;
            if (process != null)
            {
                try
                {
                    process.Dispose();
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }
            return null;
        }

        private Exception CleanupAfterFailure()
        {
            Exception failure = TerminateProcess(false);
            failure = CombineSecondaryFailure(failure, CloseInputStream());
            try
            {
                CloseOutputPump();
            }
            catch (Exception ex)
            {
                failure = CombineSecondaryFailure(failure, ex);
            }
            StopProcessTimer();
            failure = CombineSecondaryFailure(failure, DisposeProcess());
            failure = CombineSecondaryFailure(failure, DisposeInputHasher());
            failure = CombineSecondaryFailure(
                failure,
                ReleaseApprovedExecutableLease());
            failure = CombineSecondaryFailure(failure, CleanupOwnedWorkFiles());
            return failure;
        }

        private Exception CleanupResources()
        {
            Exception failure = CloseInputStream();
            try
            {
                CloseOutputPump();
            }
            catch (Exception ex)
            {
                failure = CombineSecondaryFailure(failure, ex);
            }

            if (useTempFile)
            {
                try
                {
                    if (_fileOperations.Exists(tempFile))
                        _fileOperations.Delete(tempFile);
                }
                catch (Exception ex)
                {
                    failure = CombineSecondaryFailure(
                        failure,
                        new IOException(
                            "The owned temporary input file could not be deleted: " +
                            ex.Message,
                            ex));
                }
            }

            StopProcessTimer();
            failure = CombineSecondaryFailure(failure, DisposeProcess());
            failure = CombineSecondaryFailure(failure, DisposeInputHasher());
            failure = CombineSecondaryFailure(
                failure,
                ReleaseApprovedExecutableLease());
            return failure;
        }

        private Exception DisposeInputHasher()
        {
            SHA256 hasher = _inputHasher;
            _inputHasher = null;
            if (hasher == null)
                return null;
            try
            {
                hasher.Clear();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private Exception CleanupOwnedWorkFiles()
        {
            Exception failure = null;
            failure = CombineSecondaryFailure(
                failure,
                ReleaseApprovedExecutableLease());
            failure = CombineSecondaryFailure(
                failure,
                TryDeleteOwnedFile(_workPath, "encoder work output"));
            if (useTempFile)
                failure = CombineSecondaryFailure(
                    failure,
                    TryDeleteOwnedFile(tempFile, "temporary input"));
            return failure;
        }

        private Exception ReleaseApprovedExecutableLease()
        {
            FileStream lease = _approvedExecutableLease;
            _approvedExecutableLease = null;
            if (lease == null)
                return null;
            try
            {
                lease.Close();
                return null;
            }
            catch (Exception ex)
            {
                return new IOException(
                    "The approved encoder executable lease could not be released: " +
                    ex.Message,
                    ex);
            }
        }

        private Exception TryDeleteOwnedFile(string path, string description)
        {
            if (String.IsNullOrEmpty(path))
                return null;

            try
            {
                if (_fileOperations.Exists(path))
                    _fileOperations.Delete(path);
                return null;
            }
            catch (Exception ex)
            {
                return new IOException(
                    "The owned " + description + " file could not be deleted: " +
                    ex.Message,
                    ex);
            }
        }

        private static Exception CombineSecondaryFailure(
            Exception first,
            Exception second)
        {
            if (first == null)
                return second;
            if (second == null)
                return first;
            return new IOException(
                first.Message + " Additional failure: " + second.Message,
                first);
        }

        private static IOException CombineFailure(
            string context,
            Exception primary,
            Exception secondary)
        {
            return new IOException(
                context + " Primary failure: " + primary.Message +
                " Secondary failure: " + secondary.Message,
                primary);
        }

        private static Exception PrepareRethrow(Exception failure)
        {
            // ExceptionDispatchInfo is unavailable on the net20 target. Wrap the original so its
            // stack remains available as InnerException instead of using `throw failure`.
            if (failure is TimeoutException)
                return new TimeoutException(failure.Message, failure);
            if (failure is EncoderProcessExitException)
                return new EncoderProcessExitException(failure.Message, failure);
            return new IOException(failure.Message, failure);
        }

        public void Delete()
        {
            Exception abortFailure = null;
            if (!closed)
            {
                // Delete is the abort half of IAudioDest.  It must not call Close(), because Close
                // validates and publishes the work file.  In particular, publishing here could
                // replace a pre-existing requested path while a caller is unwinding a failed rip.
                closed = true;
                WAV.AudioEncoder writer = wrt;
                wrt = null;
                if (writer != null)
                {
                    try
                    {
                        // Close the WAV stream so a child blocked on stdin can observe EOF.  The
                        // process is still terminated below and its output is never published.
                        writer.Close();
                    }
                    catch (Exception ex)
                    {
                        abortFailure = ex;
                    }
                }

                abortFailure = CombineSecondaryFailure(
                    abortFailure,
                    CleanupAfterFailure());
            }

            Exception deleteFailure = CleanupOwnedWorkFiles();
            if (_publishedOutput)
            {
                try
                {
                    if (_fileOperations.Exists(_path))
                        _fileOperations.Delete(_path);
                    _publishedOutput = false;
                }
                catch (Exception ex)
                {
                    deleteFailure = CombineSecondaryFailure(deleteFailure, ex);
                }
            }

            if (abortFailure != null && deleteFailure != null)
                throw CombineFailure(
                    "External encoder abort and owned-output deletion both failed.",
                    abortFailure,
                    deleteFailure);
            if (abortFailure != null)
                throw PrepareRethrow(abortFailure);
            if (deleteFailure != null)
                throw new IOException(
                    "External encoder owned-output deletion failed.",
                    deleteFailure);
        }

        public void Write(AudioBuffer buff)
        {
            if (closed)
                throw new InvalidOperationException("The encoder is already closed.");

            try
            {
                if (_verificationRequired)
                {
                    // Bind verification to an owned snapshot. The caller's reusable AudioBuffer
                    // must not be able to change between the bytes sent to the process/temp WAV and
                    // the bytes added to the source fingerprint.
                    buff.Prepare(this);
                    byte[] sourceBytes = buff.Bytes;
                    byte[] ownedBytes = new byte[buff.ByteLength];
                    if (ownedBytes.Length != 0)
                        Buffer.BlockCopy(
                            sourceBytes,
                            0,
                            ownedBytes,
                            0,
                            ownedBytes.Length);
                    AudioBuffer ownedBuffer = new AudioBuffer(
                        _pcm,
                        ownedBytes,
                        buff.Length);
                    wrt.Write(ownedBuffer);
                    if (ownedBytes.Length != 0)
                        _inputHasher.TransformBlock(
                            ownedBytes,
                            0,
                            ownedBytes.Length,
                            ownedBytes,
                            0);
                    _inputSampleCount = checked(
                        _inputSampleCount + buff.Length);
                }
                else
                {
                    wrt.Write(buff);
                }
                // Streaming encoders legitimately live for the duration of a full-disc rip. Treat
                // the limit as an inactivity/finalization watchdog, not a total wall-clock cap.
                ResetProcessTimeout();
            }
            catch (Exception ex)
            {
                closed = true;
                WAV.AudioEncoder writer = wrt;
                wrt = null;
                Exception writerCleanupFailure = null;
                if (writer != null)
                {
                    try { writer.Close(); }
                    catch (Exception closeFailure)
                    {
                        writerCleanupFailure = closeFailure;
                    }
                }

                Exception operationFailure = NormalizeProcessFailure(ex);
                Exception cleanupFailure = writerCleanupFailure;
                cleanupFailure = CombineSecondaryFailure(
                    cleanupFailure,
                    CleanupResources());
                cleanupFailure = CombineSecondaryFailure(
                    cleanupFailure,
                    CleanupOwnedWorkFiles());
                if (cleanupFailure != null)
                    throw CombineFailure(
                        "External encoder write failed and cleanup was incomplete.",
                        operationFailure,
                        cleanupFailure);
                throw PrepareRethrow(operationFailure);
            }
        }
    }
}
