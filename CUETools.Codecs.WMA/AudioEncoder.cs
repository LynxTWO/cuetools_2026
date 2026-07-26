using System;
using System.IO;
using System.Runtime.InteropServices;
using WindowsMediaLib;
using WindowsMediaLib.Defs;

namespace CUETools.Codecs.WMA
{
    public class AudioEncoder : IAudioDest
    {
        IWMWriter m_pEncoder;
        private string outputPath;
        private readonly WmaOutputTransaction outputTransaction;
        private bool closed = false;
        private bool outputWasRequested = false;
        private bool writingBegan = false;
        private long sampleCount, finalSampleCount;
        private bool finalSampleCountSet;
        private readonly bool verifyLossless;
        private PcmFingerprint inputFingerprint;

        public long FinalSampleCount
        {
            set
            {
                this.finalSampleCount = value;
                this.finalSampleCountSet = true;
            }
        }

        public string Path
        {
            get { return this.outputPath; }
        }

        EncoderSettings m_settings;

        public IAudioEncoderSettings Settings => m_settings;

        public AudioEncoder(EncoderSettings settings, string path, Stream IO = null)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");
            if (settings.PCM == null)
                throw new ArgumentException("PCM must be configured.", "settings");
            if (IO != null)
                throw new NotSupportedException(
                    "WMA encoding requires a file path and cannot write to an arbitrary stream.");

            this.m_settings = settings;
            this.outputTransaction = new WmaOutputTransaction(path);
            this.outputPath = outputTransaction.RequestedPath;

            try
            {
                m_pEncoder = settings.GetWriter();
                int cInputs;
                m_pEncoder.GetInputCount(out cInputs);
                if (cInputs < 1) throw new InvalidOperationException();
                IWMInputMediaProps pInput;
                m_pEncoder.GetInputProps(0, out pInput);
                try
                {
                    int cbType = 0;
                    AMMediaType pMediaType = null;
                    pInput.GetMediaType(pMediaType, ref cbType);
                    pMediaType = new AMMediaType();
                    pMediaType.formatSize = cbType - Marshal.SizeOf(typeof(AMMediaType));
                    pInput.GetMediaType(pMediaType, ref cbType);
                    try
                    {
                        var wfe = new WaveFormatExtensible(m_settings.PCM);
                        Marshal.FreeCoTaskMem(pMediaType.formatPtr);
                        pMediaType.formatPtr = IntPtr.Zero;
                        pMediaType.formatSize = 0;
                        pMediaType.formatPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(wfe));
                        pMediaType.formatSize = Marshal.SizeOf(wfe);
                        Marshal.StructureToPtr(wfe, pMediaType.formatPtr, false);
                        pInput.SetMediaType(pMediaType);
                        m_pEncoder.SetInputProps(0, pInput);
                    }
                    finally
                    {
                        WMUtils.FreeWMMediaType(pMediaType);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(pInput);
                }

                var losslessSettings = settings as LosslessEncoderSettings;
                verifyLossless = losslessSettings != null && losslessSettings.DoVerify;
                if (verifyLossless)
                    inputFingerprint = new PcmFingerprint();
            }
            catch
            {
                if (m_pEncoder != null)
                {
                    Marshal.ReleaseComObject(m_pEncoder);
                    m_pEncoder = null;
                }
                throw;
            }
        }

        public void Close()
        {
            if (this.closed)
                return;

            try
            {
                outputTransaction.Complete(
                    outputWasRequested,
                    FinishWritingAndVerify);
            }
            finally
            {
                if (inputFingerprint != null)
                {
                    inputFingerprint.Dispose();
                    inputFingerprint = null;
                }
            }
        }

        public void Delete()
        {
            if (this.outputPath == null)
                throw new InvalidOperationException("This writer was not created from file.");

            Exception finishFailure = null;
            try
            {
                if (!this.closed)
                    FinishWriting();
            }
            catch (Exception ex)
            {
                finishFailure = ex;
            }

            Exception cleanupFailure = null;
            try
            {
                outputTransaction.CleanupWork();
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
            }

            if (outputTransaction.Published)
            {
                try
                {
                    WmaOutputSafety.RemoveOrQuarantine(this.outputPath);
                }
                catch (Exception ex)
                {
                    cleanupFailure = CombineFailure(cleanupFailure, ex);
                }
            }

            if (inputFingerprint != null)
            {
                inputFingerprint.Dispose();
                inputFingerprint = null;
            }

            if (finishFailure != null && cleanupFailure != null)
                throw new IOException(
                    "WMA finalization failed, and owned output cleanup also failed: " +
                    cleanupFailure.Message,
                    finishFailure);
            if (finishFailure != null)
                throw new IOException(
                    "WMA finalization failed during deletion.",
                    finishFailure);
            if (cleanupFailure != null)
                throw new IOException(
                    "WMA owned output cleanup failed during deletion.",
                    cleanupFailure);
        }

        public void Write(AudioBuffer buffer)
        {
            if (this.closed)
                throw new InvalidOperationException("Writer already closed.");

            if (!this.outputWasRequested)
            {
                this.m_pEncoder.SetOutputFilename(outputTransaction.WorkPath);
                this.outputWasRequested = true;
            }
            if (!this.writingBegan)
            {
                this.m_pEncoder.BeginWriting();
                this.writingBegan = true;
            }

            buffer.Prepare(this);
            INSSBuffer pSample = null;
            try
            {
                m_pEncoder.AllocateSample(buffer.ByteLength, out pSample);
                IntPtr pdwBuffer;
                pSample.GetBuffer(out pdwBuffer);
                pSample.SetLength(buffer.ByteLength);
                Marshal.Copy(buffer.Bytes, 0, pdwBuffer, buffer.ByteLength);
                long cnsSampleTime = sampleCount * 10000000L / Settings.PCM.SampleRate;
                m_pEncoder.WriteSample(0, cnsSampleTime, SampleFlag.CleanPoint, pSample);
            }
            finally
            {
                if (pSample != null)
                    Marshal.ReleaseComObject(pSample);
            }

            if (inputFingerprint != null)
                inputFingerprint.Append(buffer.Bytes, buffer.ByteLength);
            sampleCount = checked(sampleCount + buffer.Length);
        }

        private void FinishWriting()
        {
            try
            {
                if (this.writingBegan)
                {
                    m_pEncoder.EndWriting();
                    this.writingBegan = false;
                }
            }
            finally
            {
                if (m_pEncoder != null)
                {
                    Marshal.ReleaseComObject(m_pEncoder);
                    m_pEncoder = null;
                }

                this.closed = true;
            }
        }

        private void FinishWritingAndVerify()
        {
            // EndWriting closes the ASF file, and releasing the COM writer ensures the decoder never
            // verifies buffered data through a still-live writer handle.
            FinishWriting();

            ValidateExpectedSampleCount(
                finalSampleCountSet,
                finalSampleCount,
                sampleCount);
            if (!verifyLossless || !outputWasRequested)
                return;

            WmaLosslessVerification.Verify(
                outputTransaction.WorkPath,
                Settings.PCM,
                sampleCount,
                inputFingerprint.Complete());
        }

        internal static void ValidateExpectedSampleCount(
            bool expectedCountWasSet,
            long expectedSampleCount,
            long actualSampleCount)
        {
            if (expectedCountWasSet && expectedSampleCount != actualSampleCount)
                throw new InvalidDataException(Properties.Resources.ExceptionSampleCount);
        }

        private static Exception CombineFailure(Exception first, Exception second)
        {
            if (first == null)
                return second;
            if (second == null)
                return first;
            return new IOException(
                first.Message + " Additional cleanup failure: " + second.Message,
                first);
        }
    }
}
