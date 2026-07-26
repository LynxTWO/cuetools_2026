using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CUETools.Codecs.CommandLine
{
    /// <summary>
    /// Reads a WAV stream produced by an external decoder. The process watchdog is an inactivity
    /// bound, and EOF is not accepted until the child exits successfully.
    /// </summary>
    public class AudioDecoder : IAudioSource
    {
        private const int ProcessTerminationWaitMilliseconds = 5000;

        private readonly string _path;
        private readonly DecoderSettings _settings;
        private readonly IEncoderProcessFactory _processFactory;
        private readonly object _processSync = new object();
        private IEncoderProcess _decoderProcess;
        private WAV.AudioDecoder _reader;
        private Timer _processTimeoutTimer;
        private long _processTimeoutDeadline;
        private bool _processStarted;
        private bool _processExited;
        private bool _initialized;
        private bool _completed;
        private bool _closed;
        private volatile bool _timedOut;
        private bool _timeoutCallbackActive;
        private Exception _timeoutTerminationFailure;

        public IAudioDecoderSettings Settings
        {
            get { return _settings; }
        }

        public long Position
        {
            get
            {
                Initialize();
                return _reader.Position;
            }
            set
            {
                Initialize();
                if (value != _reader.Position)
                    throw new NotSupportedException(
                        "Seeking is not supported by a command-line decoder stream.");
            }
        }

        public TimeSpan Duration
        {
            get
            {
                Initialize();
                return _reader.Duration;
            }
        }

        public long Length
        {
            get
            {
                Initialize();
                return _reader.Length;
            }
        }

        public long Remaining
        {
            get
            {
                Initialize();
                return _reader.Remaining;
            }
        }

        public AudioPCMConfig PCM
        {
            get
            {
                Initialize();
                return _reader.PCM;
            }
        }

        public string Path
        {
            get { return _path; }
        }

        public AudioDecoder(DecoderSettings settings, string path, Stream IO)
            : this(settings, path, IO, new SystemEncoderProcessFactory())
        {
        }

        internal AudioDecoder(
            DecoderSettings settings,
            string path,
            Stream IO,
            IEncoderProcessFactory processFactory)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            if (String.IsNullOrEmpty(path))
                throw new ArgumentNullException("path");
            if (IO != null)
                throw new NotSupportedException(
                    "Command-line decoders require a file path and cannot consume an arbitrary stream.");
            if (processFactory == null)
                throw new ArgumentNullException("processFactory");
            if (settings.ProcessTimeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(
                    "settings",
                    "ProcessTimeoutMilliseconds must be greater than zero.");
            if (String.IsNullOrEmpty(settings.Parameters) ||
                !settings.Parameters.Contains("%I"))
                throw new ArgumentException(
                    "Command-line decoder parameters must contain %I.",
                    "settings");

            _settings = settings;
            _path = System.IO.Path.GetFullPath(path);
            _processFactory = processFactory;
        }

        private ProcessStartInfo CreateStartInfo()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = _settings.Path;
            startInfo.Arguments = _settings.Parameters.Replace(
                "%I", "\"" + _path + "\"");
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.UseShellExecute = false;
            return startInfo;
        }

        private void Initialize()
        {
            if (_closed)
                throw new InvalidOperationException("The decoder is already closed.");
            if (_initialized)
                return;

            _decoderProcess = _processFactory.Create(CreateStartInfo());
            bool started = false;
            Exception startFailure = null;
            try
            {
                started = _decoderProcess.Start();
            }
            catch (Exception ex)
            {
                startFailure = ex;
            }

            if (!started)
            {
                IOException failure = new IOException(
                    _settings.Path + ": " +
                    (startFailure == null
                        ? "please check the path"
                        : startFailure.Message),
                    startFailure);
                Exception disposeFailure = DisposeProcess();
                if (disposeFailure != null)
                    failure = CombineFailure(
                        "The external decoder could not be started or disposed.",
                        failure,
                        disposeFailure);
                ExceptionRelay.Throw(failure);
                return;
            }

            lock (_processSync)
            {
                _processStarted = true;
                _processTimeoutDeadline = ProcessTimeoutDeadline.FromNow(
                    _settings.ProcessTimeoutMilliseconds);
                _processTimeoutTimer = new Timer(
                    ProcessTimedOut, null, Timeout.Infinite, Timeout.Infinite);
                _processTimeoutTimer.Change(
                    _settings.ProcessTimeoutMilliseconds, Timeout.Infinite);
            }
            SetProcessPriority();

            try
            {
                _reader = new WAV.AudioDecoder(
                    new WAV.DecoderSettings(),
                    _path,
                    _decoderProcess.StandardOutput);
                _initialized = true;
                ResetProcessTimeout();
            }
            catch (Exception ex)
            {
                Exception failure = NormalizeProcessFailure(ex);
                Exception cleanupFailure = CleanupResources();
                if (cleanupFailure != null)
                    failure = CombineFailure(
                        "External decoder initialization failed and cleanup was incomplete.",
                        failure,
                        cleanupFailure);
                _closed = true;
                ExceptionRelay.Throw(failure);
            }
        }

        private void SetProcessPriority()
        {
            try
            {
                using (Process currentProcess = Process.GetCurrentProcess())
                    _decoderProcess.PriorityClass = currentProcess.PriorityClass;
            }
            catch
            {
                // Priority inheritance is best effort and must not fail a decode.
            }
        }

        private void ProcessTimedOut(object state)
        {
            lock (_processSync)
            {
                if (_processTimeoutTimer == null || _processExited)
                    return;
                int remainingMilliseconds =
                    ProcessTimeoutDeadline.RemainingMilliseconds(
                        _processTimeoutDeadline);
                if (remainingMilliseconds > 0)
                {
                    // Change cannot retract a callback that was already queued for the previous
                    // deadline. Only the current monotonic deadline may authorize termination.
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
                if (!_processStarted || _processExited ||
                    _processTimeoutTimer == null)
                    return;
                _processTimeoutDeadline = ProcessTimeoutDeadline.FromNow(
                    _settings.ProcessTimeoutMilliseconds);
                _processTimeoutTimer.Change(
                    _settings.ProcessTimeoutMilliseconds,
                    Timeout.Infinite);
            }
        }

        public int Read(AudioBuffer buff, int maxLength)
        {
            if (buff == null)
                throw new ArgumentNullException("buff");
            if (maxLength == 0)
                return 0;
            Initialize();
            if (_completed)
                return 0;

            try
            {
                int count = _reader.Read(buff, maxLength);
                if (count > 0)
                {
                    ResetProcessTimeout();
                    return count;
                }

                CompleteProcess();
                _completed = true;
                return 0;
            }
            catch (Exception ex)
            {
                Exception failure = NormalizeProcessFailure(ex);
                Exception cleanupFailure = CleanupResources();
                if (cleanupFailure != null)
                    failure = CombineFailure(
                        "External decoder read failed and cleanup was incomplete.",
                        failure,
                        cleanupFailure);
                _closed = true;
                ExceptionRelay.Throw(failure);
                return 0;
            }
        }

        private void CompleteProcess()
        {
            bool exited = _decoderProcess.HasExited ||
                _decoderProcess.WaitForExit(_settings.ProcessTimeoutMilliseconds);
            if (!exited)
            {
                Exception terminationFailure = TerminateProcess(true);
                terminationFailure = CombineSecondaryFailure(
                    terminationFailure,
                    GetTimeoutTerminationFailure());
                throw CreateTimeoutException(terminationFailure);
            }

            lock (_processSync)
                _processExited = true;
            StopProcessTimer();

            if (_timedOut)
                throw CreateTimeoutException(GetTimeoutTerminationFailure());

            int exitCode = _decoderProcess.ExitCode;
            if (exitCode != 0)
                throw new EncoderProcessExitException(String.Format(
                    "{0} returned decoder error code {1}",
                    _settings.Path,
                    exitCode));

            Exception cleanupFailure = CloseReader();
            cleanupFailure = CombineSecondaryFailure(
                cleanupFailure,
                DisposeProcess());
            if (cleanupFailure != null)
                throw new IOException(
                    "The external decoder succeeded, but its resources could not be cleaned up.",
                    cleanupFailure);
        }

        public void Close()
        {
            if (_closed)
                return;
            _closed = true;

            if (!_processStarted)
                return;
            if (_completed)
            {
                Exception completedCleanupFailure = CleanupResources();
                if (completedCleanupFailure != null)
                    throw new IOException(
                        "External decoder cleanup failed.",
                        completedCleanupFailure);
                return;
            }

            Exception operationFailure = null;
            try
            {
                if (_decoderProcess.HasExited)
                {
                    lock (_processSync)
                        _processExited = true;
                    int exitCode = _decoderProcess.ExitCode;
                    if (exitCode != 0)
                        operationFailure = new EncoderProcessExitException(
                            String.Format(
                                "{0} returned decoder error code {1}",
                                _settings.Path,
                                exitCode));
                }
                else
                {
                    operationFailure = TerminateProcess(false);
                }
            }
            catch (Exception ex)
            {
                operationFailure = ex;
            }

            Exception cleanupFailure = CleanupResources();
            if (operationFailure != null && cleanupFailure != null)
                throw CombineFailure(
                    "External decoder close and cleanup both failed.",
                    operationFailure,
                    cleanupFailure);
            if (operationFailure != null)
            {
                ExceptionRelay.Throw(operationFailure);
                return;
            }
            if (cleanupFailure != null)
                throw new IOException(
                    "External decoder cleanup failed.",
                    cleanupFailure);
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
                    normalized = new EncoderProcessExitException(
                        String.Format(
                            "{0} returned decoder error code {1}",
                            _settings.Path,
                            exitCode),
                        failure);
            }

            if (terminationFailure != null)
                normalized = CombineFailure(
                    "The external decoder operation failed and process termination was incomplete.",
                    normalized,
                    terminationFailure);
            return normalized;
        }

        private bool TryGetExitedCode(out int exitCode)
        {
            exitCode = 0;
            if (!_processStarted || _decoderProcess == null)
                return false;
            try
            {
                if (!_decoderProcess.HasExited)
                    return false;
                exitCode = _decoderProcess.ExitCode;
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
                if (timedOut)
                    _timedOut = true;

                if (_processStarted && !_processExited &&
                    _decoderProcess != null)
                {
                    process = _decoderProcess;
                    try
                    {
                        terminationNeeded = !process.HasExited;
                        if (!terminationNeeded)
                            _processExited = true;
                    }
                    catch (Exception ex)
                    {
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

                if (exited)
                {
                    lock (_processSync)
                        _processExited = true;
                }
                else
                {
                    failure = CombineSecondaryFailure(
                        failure,
                        new IOException(
                            "The external decoder did not exit after termination was requested."));
                }
            }

            StopProcessTimer();
            return failure;
        }

        private TimeoutException CreateTimeoutException(Exception innerException)
        {
            return new TimeoutException(
                String.Format(
                    "{0} made no decode or exit progress for {1} milliseconds; termination was requested{2}.",
                    _settings.Path,
                    _settings.ProcessTimeoutMilliseconds,
                    innerException == null
                        ? String.Empty
                        : ", but termination did not complete cleanly: " +
                            innerException.Message),
                innerException);
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
            Timer timer;
            lock (_processSync)
            {
                timer = _processTimeoutTimer;
                _processTimeoutTimer = null;
            }
            if (timer != null)
                timer.Dispose();
        }

        private Exception CloseReader()
        {
            WAV.AudioDecoder reader = _reader;
            _reader = null;
            if (reader == null)
                return null;
            try
            {
                reader.Close();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private Exception DisposeProcess()
        {
            IEncoderProcess process = _decoderProcess;
            _decoderProcess = null;
            if (process == null)
                return null;
            try
            {
                process.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        private Exception CleanupResources()
        {
            StopProcessTimer();
            Exception failure = CloseReader();
            failure = CombineSecondaryFailure(failure, DisposeProcess());
            return failure;
        }

        private static Exception CombineSecondaryFailure(
            Exception first,
            Exception second)
        {
            if (first == null)
                return second;
            if (second == null)
                return first;
            IOException combined = new IOException(
                first.Message + " Additional failure: " + second.Message,
                first);
            combined.Data["SecondaryFailure"] = second;
            return combined;
        }

        private static IOException CombineFailure(
            string context,
            Exception primary,
            Exception secondary)
        {
            IOException combined = new IOException(
                context + " Primary failure: " + primary.Message +
                " Secondary failure: " + secondary.Message,
                primary);
            combined.Data["SecondaryFailure"] = secondary;
            return combined;
        }
    }
}
