using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CUETools.Codecs;
using Newtonsoft.Json;

namespace CUETools.Codecs.libFLAC
{
    [JsonObject(MemberSerialization.OptIn)]
    public class EncoderSettings : IAudioEncoderSettings, IVerifyOnEncodeSettings
    {
        #region IAudioEncoderSettings implementation
        [Browsable(false)]
        public string Extension => "flac";

        [Browsable(false)]
        public string Name => "libFLAC";

        [Browsable(false)]
        public Type EncoderType => typeof(Encoder);

        [Browsable(false)]
        public bool Lossless => true;

        [Browsable(false)]
        public int Priority => 2;

        [Browsable(false)]
        public string SupportedModes => "0 1 2 3 4 5 6 7 8";

        [Browsable(false)]
        public string DefaultMode => "5";

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
            // New profiles verify by default. The false serialization default is retained so a
            // later explicit opt-out remains omitted; CUEConfig's one-time versioned migration
            // enables historical omissions once without re-enabling that later opt-out.
            Verify = true;
        }

        [DefaultValue(false)]
        [DisplayName("Verify")]
        [Description("Decode each frame and compare with original")]
        [JsonProperty]
        public bool Verify { get; set; }

        // Typed seam for CUEConfig's lossless-verification migration
        // (IVerifyOnEncodeSettings); maps onto the serialized property so the
        // settings surface is unchanged.
        bool IVerifyOnEncodeSettings.VerifyOnEncode
        {
            get => Verify;
            set => Verify = value;
        }

        [DefaultValue(true)]
        [DisplayName("MD5")]
        [Description("Calculate MD5 hash for audio stream")]
        [JsonProperty]
        public bool MD5Sum { get; set; }

        [DisplayName("Version")]
        [Description("Library version")]
        public string Version => FLACDLL.GetVersion;
    };

    public unsafe class Encoder : IAudioDest
    {
        public Encoder(EncoderSettings settings, string path, Stream output = null)
        {
            m_path = path;
            m_stream = output;
            m_outputTransaction = output == null ? new LosslessFileOutputTransaction(path) : null;
            m_settings = settings;
            m_verify = settings.Verify;
            m_streamGiven = output != null;
            m_initialized = false;
            m_finalSampleCount = -1;
            m_samplesWritten = 0;
            // These delegates are handed to native libFLAC and stored in instance fields on
            // purpose: the GC must not collect them while the native encoder can still call
            // back. A local variable would be reclaimed and the next native callback would
            // jump into freed memory. Keep them field-rooted for the encoder's whole lifetime.
            m_write_callback = StreamEncoderWriteCallback;
            m_seek_callback = StreamEncoderSeekCallback;
            m_tell_callback = StreamEncoderTellCallback;

            if (m_settings.PCM.BitsPerSample < 16 || m_settings.PCM.BitsPerSample > 24)
                throw new Exception("bits per sample must be 16..24");

            m_encoder = FLACDLL.FLAC__stream_encoder_new();
            if (m_encoder == IntPtr.Zero)
                throw new Exception("unable to create the encoder");

            try
            {
                FLACDLL.FLAC__stream_encoder_set_bits_per_sample(m_encoder, (uint)m_settings.PCM.BitsPerSample);
                FLACDLL.FLAC__stream_encoder_set_channels(m_encoder, (uint)m_settings.PCM.ChannelCount);
                FLACDLL.FLAC__stream_encoder_set_sample_rate(m_encoder, (uint)m_settings.PCM.SampleRate);
            }
            catch
            {
                ReleaseEncoderAndMetadataNoThrow();
                throw;
            }
        }

        public IAudioEncoderSettings Settings => m_settings;

        public string Path { get => m_path; }

        public long FinalSampleCount
        {
            get => m_finalSampleCount;
            set
            {
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
            if (m_outputTransaction != null)
                m_outputTransaction.Complete(FinalizeEncoder);
            else
                FinalizeEncoder();
        }

        private void FinalizeEncoder()
        {
            // finish() is where libFLAC reports a verify mismatch in the FINAL block, and where it
            // flushes the last frames and rewrites STREAMINFO. Discarding its result meant a failed
            // encode - including a caught verify mismatch - closed silently and looked successful.
            // The failure is captured here but thrown at the END, so the encoder is always deleted and
            // the stream always closed first: throwing early would leak the native encoder and leave a
            // half-written file locked.
            string failure = null;
            try
            {
                if (m_initialized)
                {
                    if (0 == FLACDLL.FLAC__stream_encoder_finish(m_encoder))
                        failure = EncoderStateDetail();
                }
            }
            catch
            {
                ReleaseEncoderAndMetadataNoThrow();
                CloseStreamNoThrow();
                throw;
            }

            if (failure != null)
            {
                ReleaseEncoderAndMetadataNoThrow();
                CloseStreamNoThrow();
                throw new Exception("an error occurred while finishing the encode: " + failure);
            }

            try
            {
                ReleaseEncoderAndMetadata();
            }
            catch
            {
                CloseStreamNoThrow();
                throw;
            }
            CloseStream();
            if ((m_finalSampleCount > 0) && (m_samplesWritten != m_finalSampleCount))
                throw new Exception("samples written differs from the expected sample count");
        }

        public void Delete()
        {
            m_closed = true;
            ReleaseEncoderAndMetadataNoThrow();
            CloseStreamNoThrow();
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

        /// <summary>The encoder's failure state, with libFLAC's verify-mismatch detail when it has any.
        /// Shared by Write and Close: a verify mismatch in the FINAL block is only reported by
        /// FLAC__stream_encoder_finish, so both call sites need the same diagnosis.</summary>
        private string EncoderStateDetail()
        {
            var state = FLACDLL.FLAC__stream_encoder_get_state(m_encoder);
            string status = state.ToString();
            if (state == FLAC__StreamEncoderState.FLAC__STREAM_ENCODER_VERIFY_MISMATCH_IN_AUDIO_DATA)
            {
                ulong absolute_sample;
                uint frame_number;
                uint channel;
                uint sample;
                int expected, got;
                FLACDLL.FLAC__stream_encoder_get_verify_decoder_error_stats(m_encoder, out absolute_sample, out frame_number, out channel, out sample, out expected, out got);
                status = status + String.Format("({0:x} instead of {1:x} @{2:x})", got, expected, absolute_sample);
            }
            return status;
        }

        public void Write(AudioBuffer sampleBuffer)
        {
            if (m_closed)
                throw new InvalidOperationException("The encoder is already closed.");
            if (!m_initialized) Initialize();

            sampleBuffer.Prepare(this);

            fixed (int* pSampleBuffer = &sampleBuffer.Samples[0, 0])
            {
                if (0 == FLACDLL.FLAC__stream_encoder_process_interleaved(m_encoder,
                    pSampleBuffer, sampleBuffer.Length))
                    throw new Exception("an error occurred while encoding: " + EncoderStateDetail());
            }

            m_samplesWritten += sampleBuffer.Length;
        }

        internal FLAC__StreamEncoderWriteStatus StreamEncoderWriteCallback(IntPtr encoder, byte[] buffer, UIntPtr bytes, int samples, int current_frame, void* client_data)
        {
            try
            {
                m_stream.Write(buffer, 0, (int)bytes);
            }
            catch (Exception)
            {
                return FLAC__StreamEncoderWriteStatus.FLAC__STREAM_ENCODER_WRITE_STATUS_FATAL_ERROR;
            }
            return FLAC__StreamEncoderWriteStatus.FLAC__STREAM_ENCODER_WRITE_STATUS_OK;
        }

        internal FLAC__StreamEncoderSeekStatus StreamEncoderSeekCallback(IntPtr encoder, long absolute_byte_offset, void* client_data)
        {
            if (!m_stream.CanSeek) return  FLAC__StreamEncoderSeekStatus.FLAC__STREAM_ENCODER_SEEK_STATUS_UNSUPPORTED;
            try
            {
                m_stream.Position = absolute_byte_offset;
            }
            catch (Exception)
            {
                return FLAC__StreamEncoderSeekStatus.FLAC__STREAM_ENCODER_SEEK_STATUS_ERROR;
            }
            return FLAC__StreamEncoderSeekStatus.FLAC__STREAM_ENCODER_SEEK_STATUS_OK;
        }

        internal FLAC__StreamEncoderTellStatus StreamEncoderTellCallback(IntPtr encoder, out long absolute_byte_offset, void* client_data)
        {
            if (!m_stream.CanSeek)
            {
                absolute_byte_offset = -1;
                return FLAC__StreamEncoderTellStatus.FLAC__STREAM_ENCODER_TELL_STATUS_UNSUPPORTED;
            }
            try
            {
                absolute_byte_offset = m_stream.Position;
            }
            catch (Exception)
            {
                absolute_byte_offset = -1;
                return FLAC__StreamEncoderTellStatus.FLAC__STREAM_ENCODER_TELL_STATUS_ERROR;
            }
            return FLAC__StreamEncoderTellStatus.FLAC__STREAM_ENCODER_TELL_STATUS_OK;
        }

        void Initialize()
        {
            try
            {
                if (m_encoder == IntPtr.Zero)
                    throw new InvalidOperationException("The encoder is unavailable after a failed initialization.");
                if (m_stream == null)
                    m_stream = new FileStream(m_outputTransaction.WorkPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 0x10000);

                var metadata = stackalloc FLAC__StreamMetadata*[4];
                int metadataCount = 0;

                if (m_finalSampleCount > 0)
                {
                    m_seekTableMetadata = CreateMetadata(
                        FLAC__MetadataType.FLAC__METADATA_TYPE_SEEKTABLE);
                    FLACDLL.FLAC__metadata_object_seektable_template_append_spaced_points_by_samples(
                        m_seekTableMetadata, m_settings.PCM.SampleRate * 10, m_finalSampleCount);
                    FLACDLL.FLAC__metadata_object_seektable_template_sort(m_seekTableMetadata, 1);
                    metadata[metadataCount++] = m_seekTableMetadata;
                }

                m_vorbisCommentMetadata = CreateMetadata(
                    FLAC__MetadataType.FLAC__METADATA_TYPE_VORBIS_COMMENT);
                metadata[metadataCount++] = m_vorbisCommentMetadata;

                if (m_settings.Padding != 0)
                {
                    m_paddingMetadata = CreateMetadata(
                        FLAC__MetadataType.FLAC__METADATA_TYPE_PADDING);
                    m_paddingMetadata->length = (uint)m_settings.Padding;
                    metadata[metadataCount++] = m_paddingMetadata;
                }

                // libFLAC borrows these metadata pointers rather than copying them. The fields
                // above retain ownership until finish completes; ReleaseEncoderAndMetadata then
                // deletes every object after the encoder can no longer reference it.
                FLACDLL.FLAC__stream_encoder_set_metadata(m_encoder, metadata, metadataCount);
                FLACDLL.FLAC__stream_encoder_set_verify(m_encoder, m_verify ? 1 : 0);
                FLACDLL.FLAC__stream_encoder_set_do_md5(m_encoder, m_settings.MD5Sum ? 1 : 0);
                FLACDLL.FLAC__stream_encoder_set_compression_level(m_encoder, m_settings.GetEncoderModeIndex());
                if (m_finalSampleCount > 0)
                    FLACDLL.FLAC__stream_encoder_set_total_samples_estimate(m_encoder, m_finalSampleCount);
                if (m_settings.BlockSize > 0)
                    FLACDLL.FLAC__stream_encoder_set_blocksize(m_encoder, m_settings.BlockSize);

                FLAC__StreamEncoderInitStatus st = FLACDLL.FLAC__stream_encoder_init_stream(
                    m_encoder, m_write_callback, m_stream.CanSeek ? m_seek_callback : null,
                    m_stream.CanSeek ? m_tell_callback : null, null, null);
                if (st != FLAC__StreamEncoderInitStatus.FLAC__STREAM_ENCODER_INIT_STATUS_OK)
                    throw new Exception(string.Format("unable to initialize the encoder: {0}", st));

                m_initialized = true;
            }
            catch
            {
                // A failed init makes this native encoder unusable. Release it immediately,
                // including any partially-created metadata, but preserve the original failure.
                ReleaseEncoderAndMetadataNoThrow();
                CloseStreamNoThrow();
                throw;
            }
        }

        private FLAC__StreamMetadata* CreateMetadata(FLAC__MetadataType type)
        {
            FLAC__StreamMetadata* metadata = FLACDLL.FLAC__metadata_object_new(type);
            if (metadata == null)
                throw new OutOfMemoryException("Unable to allocate libFLAC metadata.");
            return metadata;
        }

        private void ReleaseEncoderAndMetadata()
        {
            try
            {
                if (m_encoder != IntPtr.Zero)
                    FLACDLL.FLAC__stream_encoder_delete(m_encoder);
            }
            finally
            {
                m_encoder = IntPtr.Zero;
                m_initialized = false;
                DeleteMetadataObjects();
            }
        }

        private void ReleaseEncoderAndMetadataNoThrow()
        {
            try
            {
                if (m_encoder != IntPtr.Zero)
                    FLACDLL.FLAC__stream_encoder_delete(m_encoder);
            }
            catch
            {
            }
            m_encoder = IntPtr.Zero;
            m_initialized = false;

            DeleteMetadataObjectNoThrow(ref m_seekTableMetadata);
            DeleteMetadataObjectNoThrow(ref m_vorbisCommentMetadata);
            DeleteMetadataObjectNoThrow(ref m_paddingMetadata);
        }

        private void DeleteMetadataObjects()
        {
            DeleteMetadataObject(ref m_seekTableMetadata);
            DeleteMetadataObject(ref m_vorbisCommentMetadata);
            DeleteMetadataObject(ref m_paddingMetadata);
        }

        private static void DeleteMetadataObject(ref FLAC__StreamMetadata* metadata)
        {
            if (metadata == null)
                return;
            try
            {
                FLACDLL.FLAC__metadata_object_delete(metadata);
            }
            finally
            {
                metadata = null;
            }
        }

        private static void DeleteMetadataObjectNoThrow(ref FLAC__StreamMetadata* metadata)
        {
            try
            {
                DeleteMetadataObject(ref metadata);
            }
            catch
            {
            }
        }

        private void CloseStream()
        {
            if (m_stream == null)
                return;
            Stream stream = m_stream;
            m_stream = null;
            stream.Close();
        }

        private void CloseStreamNoThrow()
        {
            try
            {
                CloseStream();
            }
            catch
            {
            }
        }

        EncoderSettings m_settings;
        Stream m_stream;
        bool m_streamGiven;
        bool m_verify;
        LosslessFileOutputTransaction m_outputTransaction;
        IntPtr m_encoder;
        FLAC__StreamMetadata* m_seekTableMetadata;
        FLAC__StreamMetadata* m_vorbisCommentMetadata;
        FLAC__StreamMetadata* m_paddingMetadata;
        bool m_initialized;
        bool m_closed;
        string m_path;
        Int64 m_finalSampleCount, m_samplesWritten;
        FLACDLL.FLAC__StreamEncoderWriteCallback m_write_callback;
        FLACDLL.FLAC__StreamEncoderSeekCallback m_seek_callback;
        FLACDLL.FLAC__StreamEncoderTellCallback m_tell_callback;
    }
}
