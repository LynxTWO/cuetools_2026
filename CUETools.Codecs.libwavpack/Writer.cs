using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CUETools.Codecs;
using Newtonsoft.Json;

namespace CUETools.Codecs.libwavpack
{
    [JsonObject(MemberSerialization.OptIn)]
    public class EncoderSettings : IAudioEncoderSettings
    {
        #region IAudioEncoderSettings implementation
        [Browsable(false)]
        public string Extension => "wv";

        [Browsable(false)]
        public string Name => "libwavpack";

        [Browsable(false)]
        public Type EncoderType => typeof(AudioEncoder);

        [Browsable(false)]
        public bool Lossless => true;

        [Browsable(false)]
        public int Priority => 1;

        [Browsable(false)]
        public string SupportedModes => "fast normal high high+";

        [Browsable(false)]
        public string DefaultMode => "normal";

        [Browsable(false)]
        [DefaultValue("")]
        [JsonProperty]
        public string EncoderMode { get; set; }

        [Browsable(false)]
        public AudioPCMConfig PCM { get; set; }

        [Browsable(false)]
        public int BlockSize { get; set; }

        [Browsable(false)]
        [DefaultValue(4096)]
        public int Padding { get; set; }

        public IAudioEncoderSettings Clone()
        {
            return MemberwiseClone() as IAudioEncoderSettings;
        }
        #endregion

        public EncoderSettings()
        {
            this.Init();
        }

        [DefaultValue(0)]
		[DisplayName("ExtraMode")]
        [JsonProperty]
        public int ExtraMode { 
            get => m_extraMode; 
            set {
				if ((value < 0) || (value > 6)) {
					throw new Exception("Invalid extra mode.");
				}
				m_extraMode = value;
            }
        }

        [DefaultValue(true)]
        [DisplayName("MD5")]
        [Description("Calculate MD5 hash for audio stream")]
        [JsonProperty]
        public bool MD5Sum { get; set; }

        [DefaultValue(true)]
        [DisplayName("Verify")]
        [Description("Decode the completed file and compare it with the encoder input")]
        [JsonProperty]
        public bool Verify { get; set; }

        [DefaultValue(0)]
        [DisplayName("CPUThreads")]
        [Description("Utilize multiple threads for compression (0..15). 0 means disabled")]
        [JsonProperty]
        public int CPUThreads { 
            get => m_workerThreads; 
            set {
                if ((value < 0) || (value > 15))
                {
                    throw new Exception("CPUThreads must be between 0..15");
                }
                m_workerThreads = value;
            }
        }

        [DisplayName("Version")]
        [Description("Library version")]
        public string Version => Marshal.PtrToStringAnsi(wavpackdll.WavpackGetLibraryVersionString());

        private int m_extraMode;
        private int m_workerThreads;
    };

    public unsafe class AudioEncoder : IAudioDest
    {
        public AudioEncoder(EncoderSettings settings, string path, Stream output = null)
        {
            m_path = path;
            m_stream = output;
            m_settings = settings;
            m_streamGiven = output != null;
            m_verify = settings.Verify;
            m_outputTransaction = output == null ? new LosslessFileOutputTransaction(path) : null;
            m_verificationFingerprint = m_verify ? new LosslessPcmFingerprint() : null;
            m_initialized = false;
            m_finalSampleCount = 0;
            m_samplesWritten = 0;
            m_blockOutput = BlockOutputCallback;
            if (m_settings.PCM.BitsPerSample < 16 || m_settings.PCM.BitsPerSample > 24)
                throw new Exception("bits per sample must be 16..24");
            if (m_streamGiven && m_verify && !m_stream.CanSeek)
                throw new InvalidOperationException("WavPack verification requires a seekable output stream.");
        }

        public IAudioEncoderSettings Settings => m_settings;

        public string Path { get => m_path; }

        public long FinalSampleCount
        {
            get => m_finalSampleCount;
            set
            {
                if (value < 0)
                    throw new Exception("invalid final sample count");
                if (m_initialized)
                    throw new Exception("final sample count cannot be changed after encoding begins");
                m_finalSampleCount = value;
            }
        }

        public void Close()
        {
            if (m_closed)
                return;
            m_closed = true;
            try
            {
                if (m_outputTransaction != null)
                    m_outputTransaction.Complete(FinalizeAndVerify);
                else
                    FinalizeAndVerify();
            }
            finally
            {
                // Verification normally consumes and closes a caller-supplied seekable stream.
                // If finalization fails before VerifyOutput, close it here without masking the
                // primary native or sample-count exception.
                CloseStreamNoThrow();
                if (m_verificationFingerprint != null)
                {
                    m_verificationFingerprint.Dispose();
                    m_verificationFingerprint = null;
                }
                if (_md5hasher != null)
                {
                    _md5hasher.Clear();
                    _md5hasher = null;
                }
            }
        }

        private void FinalizeAndVerify()
        {
            FinalizeEncoder();
            if (m_verify)
                VerifyOutput();
        }

        private void FinalizeEncoder()
        {
            // These three calls all return int, 0 on failure, exactly like WavpackPackSamples which
            // Write() already checks. Discarding them meant the encode's LAST block could fail to be
            // written and the file would still close as if it had succeeded - a silently truncated rip.
            // The message must be read before WavpackCloseFile frees the context, and the throw is
            // deferred to the end so the context is always closed and the stream always released.
            string failure = null;
            try
            {
                if (m_initialized)
                {
                    if (0 == wavpackdll.WavpackFlushSamples(_wpc))
                        failure = "flushing the final samples failed: " + wavpackdll.WavpackGetErrorMessage(_wpc);
                    if (failure == null && m_settings.MD5Sum)
                    {
                        _md5hasher.TransformFinalBlock (new byte[1], 0, 0);
                        fixed (byte* md5_digest = &_md5hasher.Hash[0])
                            if (0 == wavpackdll.WavpackStoreMD5Sum (_wpc, md5_digest))
                                failure = "storing the MD5 sum failed: " + wavpackdll.WavpackGetErrorMessage(_wpc);
                        // Call WavpackFlushSamples() again after writing MD5 sum
                        if (failure == null && 0 == wavpackdll.WavpackFlushSamples(_wpc))
                            failure = "flushing the MD5 sum failed: " + wavpackdll.WavpackGetErrorMessage(_wpc);
                    }
                }
            }
            catch
            {
                CloseEncoderNoThrow();
                CloseStreamNoThrow();
                throw;
            }

            if (failure != null)
            {
                CloseEncoderNoThrow();
                CloseStreamNoThrow();
                throw new Exception("An error occurred while closing the encoder: " + failure);
            }

            try
            {
                CloseEncoder();
            }
            catch
            {
                CloseStreamNoThrow();
                throw;
            }

            if (m_stream != null)
            {
                m_stream.Flush();
                if (!m_streamGiven || !m_verify)
                {
                    m_stream.Close();
                    m_stream = null;
                }
            }
            if ((m_finalSampleCount != 0) && (m_samplesWritten != m_finalSampleCount))
                throw new Exception("samples written differs from the expected sample count");
        }

        private void VerifyOutput()
        {
            byte[] expectedDigest = m_verificationFingerprint.Complete();
            if (m_outputTransaction != null)
            {
                string workPath = m_outputTransaction.WorkPath;
                LosslessPcmVerifier.Verify(
                    "WavPack",
                    m_settings.PCM,
                    m_samplesWritten,
                    expectedDigest,
                    delegate { return new AudioDecoder(new DecoderSettings(), workPath, null); });
                return;
            }

            Stream verificationStream = m_stream;
            try
            {
                verificationStream.Position = 0;
                LosslessPcmVerifier.Verify(
                    "WavPack",
                    m_settings.PCM,
                    m_samplesWritten,
                    expectedDigest,
                    delegate { return new AudioDecoder(new DecoderSettings(), m_path, verificationStream); });
            }
            finally
            {
                verificationStream.Close();
                m_stream = null;
            }
        }

        public void Delete()
        {
            m_closed = true;
            CloseEncoderNoThrow();
            CloseStreamNoThrow();
            if (m_verificationFingerprint != null)
            {
                m_verificationFingerprint.Dispose();
                m_verificationFingerprint = null;
            }
            if (_md5hasher != null)
            {
                _md5hasher.Clear();
                _md5hasher = null;
            }
            if (m_outputTransaction != null)
            {
                if (m_outputTransaction.Published)
                    File.Delete(m_outputTransaction.RequestedPath);
                else
                    m_outputTransaction.CleanupWork();
            }
            else if (!m_streamGiven && m_path != "")
            {
                File.Delete(m_path);
            }
        }

		private void UpdateHash(byte[] buff, int len) 
		{
            if (!m_settings.MD5Sum) throw new Exception("MD5 not enabled.");
            if (!m_initialized) Initialize();
			_md5hasher.TransformBlock (buff, 0, len,  buff, 0);
		}

        public void Write(AudioBuffer sampleBuffer)
        {
            if (m_closed)
                throw new InvalidOperationException("The encoder is already closed.");
            if (!m_initialized) Initialize();

            sampleBuffer.Prepare(this);

			if (m_settings.MD5Sum)
				UpdateHash(sampleBuffer.Bytes, sampleBuffer.ByteLength);

            int[,] samples = sampleBuffer.Samples;
            if ((m_settings.PCM.BitsPerSample & 7) != 0)
            {
                if (_shiftedSampleBuffer == null || _shiftedSampleBuffer.GetLength(0) < sampleBuffer.Length)
                    _shiftedSampleBuffer = new int[sampleBuffer.Length, m_settings.PCM.ChannelCount];
                int shift = 8 - (m_settings.PCM.BitsPerSample & 7);
                int ch = m_settings.PCM.ChannelCount;
                for (int i = 0; i < sampleBuffer.Length; i++)
                    for (int c = 0; c < ch; c++)
                        _shiftedSampleBuffer[i, c] = sampleBuffer.Samples[i, c] << shift;
                samples = _shiftedSampleBuffer;
            }

            fixed (int* pSampleBuffer = &samples[0, 0])
                if (0 == wavpackdll.WavpackPackSamples(_wpc, pSampleBuffer, (uint)sampleBuffer.Length))
                    throw new Exception("An error occurred while encoding: " + wavpackdll.WavpackGetErrorMessage(_wpc));

            if (m_verificationFingerprint != null)
                m_verificationFingerprint.Append(sampleBuffer);
            m_samplesWritten += sampleBuffer.Length;
        }

        private int BlockOutputCallback(void* id, byte[] data, int bcount)
        {
            m_stream.Write(data, 0, bcount);
            return 1;
        }

        void Initialize()
        {
            try
			{
                if (m_stream == null)
                    m_stream = new FileStream(m_outputTransaction.WorkPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 0x10000);

                WavpackConfig config = new WavpackConfig();
			    config.bits_per_sample = m_settings.PCM.BitsPerSample;
			    config.bytes_per_sample = (m_settings.PCM.BitsPerSample + 7) / 8;
			    config.num_channels = m_settings.PCM.ChannelCount;
			    config.channel_mask = (int)m_settings.PCM.ChannelMask;
			    config.sample_rate = m_settings.PCM.SampleRate;
                config.flags = ConfigFlags.CONFIG_COMPATIBLE_WRITE;
                Int32 _compressionMode = m_settings.GetEncoderModeIndex();
                if (_compressionMode == 0) config.flags |= ConfigFlags.CONFIG_FAST_FLAG;
			    if (_compressionMode == 2) config.flags |= ConfigFlags.CONFIG_HIGH_FLAG;
			    if (_compressionMode == 3) config.flags |= ConfigFlags.CONFIG_HIGH_FLAG | ConfigFlags.CONFIG_VERY_HIGH_FLAG;
			    if (m_settings.ExtraMode != 0)
			    {
			        config.flags |= ConfigFlags.CONFIG_EXTRA_MODE;
			        config.xmode = m_settings.ExtraMode;
			    }
			    if (m_settings.MD5Sum)
			    {
			        _md5hasher = new MD5CryptoServiceProvider ();
			        config.flags |= ConfigFlags.CONFIG_MD5_CHECKSUM;
			    }
			    config.block_samples = (int)m_settings.BlockSize;
			    if (m_settings.BlockSize > 0 && m_settings.BlockSize < 2048)
				    config.flags |= ConfigFlags.CONFIG_MERGE_BLOCKS;
                if (m_settings.CPUThreads != 0)
                    config.worker_threads = m_settings.CPUThreads;

                _wpc = wavpackdll.WavpackOpenFileOutput(m_blockOutput, null, null);
                if (_wpc == null)
                    throw new Exception("Unable to create the encoder.");
                if (0 == wavpackdll.WavpackSetConfiguration64(
                    _wpc,
                    &config,
                    (m_finalSampleCount == 0) ? -1 : m_finalSampleCount,
                    null))
                {
                    string error = wavpackdll.WavpackGetErrorMessage(_wpc);
				    throw new Exception("Invalid configuration setting:" + error);
                }
			    if (0 == wavpackdll.WavpackPackInit(_wpc))
                {
                    string error = wavpackdll.WavpackGetErrorMessage(_wpc);
				    throw new Exception("Unable to initialize the encoder: " + error);
                }

			    m_initialized = true;
            }
            catch
            {
                // WavpackOpenFileOutput returns an owned context before configuration and
                // PackInit. Any later failure must close that partial context immediately.
                CloseEncoderNoThrow();
                CloseStreamNoThrow();
                throw;
            }
        }

        private void CloseEncoder()
        {
            if (_wpc == null)
            {
                m_initialized = false;
                return;
            }

            WavpackContext* context = _wpc;
            _wpc = null;
            m_initialized = false;
            wavpackdll.WavpackCloseFile(context);
        }

        private void CloseEncoderNoThrow()
        {
            try
            {
                CloseEncoder();
            }
            catch
            {
            }
        }

        private void CloseStreamNoThrow()
        {
            if (m_stream == null)
                return;
            Stream stream = m_stream;
            m_stream = null;
            try
            {
                stream.Close();
            }
            catch
            {
            }
        }

        int[,] _shiftedSampleBuffer;
        WavpackContext* _wpc;
        EncoderSettings m_settings;
        Stream m_stream;
        MD5 _md5hasher;
        LosslessPcmFingerprint m_verificationFingerprint;
        LosslessFileOutputTransaction m_outputTransaction;
        bool m_streamGiven;
        bool m_verify;
        bool m_initialized;
        bool m_closed;
        string m_path;
        Int64 m_finalSampleCount, m_samplesWritten;
        EncoderBlockOutput m_blockOutput;
    }
}
