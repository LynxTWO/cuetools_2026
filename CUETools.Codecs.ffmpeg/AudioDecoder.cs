using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;

namespace CUETools.Codecs.ffmpegdll
{
    internal static class FFmpegHelper
    {
        public static unsafe string av_strerror(int error)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
            return message ?? $"FFmpeg error {error}";
        }

        public static int ThrowExceptionIfError(this int error)
        {
            if (error < 0) throw new ApplicationException(av_strerror(error));
            return error;
        }

        public static string RuntimeVersion => ffmpeg.av_version_info() ?? "unknown";

        public static void VerifyRuntimeCompatibility()
        {
            VerifyMajor("libavcodec", ffmpeg.LIBAVCODEC_VERSION_MAJOR, ffmpeg.avcodec_version());
            VerifyMajor("libavformat", ffmpeg.LIBAVFORMAT_VERSION_MAJOR, ffmpeg.avformat_version());
            VerifyMajor("libavutil", ffmpeg.LIBAVUTIL_VERSION_MAJOR, ffmpeg.avutil_version());
        }

        private static void VerifyMajor(string library, uint expectedMajor, uint runtimeVersion)
        {
            uint runtimeMajor = runtimeVersion >> 16;
            if (runtimeMajor != expectedMajor)
            {
                throw new NotSupportedException(
                    $"{library} ABI {runtimeMajor} is incompatible with the " +
                    $"FFmpeg 8 binding ABI {expectedMajor}.");
            }
        }
    }

    public unsafe class AudioDecoder : IAudioSource, IDisposable
    {

        public AudioDecoder(DecoderSettings settings, string path, Stream IO)
        {
            m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _path = path ?? string.Empty;
            m_stream = (IO != null) ? IO : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                ConfigureNativeSearchPath();
                FFmpegHelper.VerifyRuntimeCompatibility();
                InitializeNativeDecoder();
            }
            catch
            {
                Dispose(true);
                throw;
            }
        }

        public static string NativeVersion => FFmpegHelper.RuntimeVersion;

        private void ConfigureNativeSearchPath()
        {
            string current = System.IO.Path.GetDirectoryName(
                typeof(AudioDecoder).Assembly.Location);
            string probe = Environment.Is64BitProcess ? "x64" : "win32";
            while (current != null)
            {
                string ffmpegBinaryPath = System.IO.Path.Combine(current, probe);
                if (Directory.Exists(ffmpegBinaryPath))
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"FFmpeg binaries found in: {ffmpegBinaryPath}");
                    ffmpeg.RootPath = ffmpegBinaryPath;
                    return;
                }
                current = Directory.GetParent(current)?.FullName;
            }
        }

        private void InitializeNativeDecoder()
        {
            pkt = ffmpeg.av_packet_alloc();
            if (pkt == null)
                throw new InvalidOperationException("Could not allocate an FFmpeg packet.");

            decoded_frame = ffmpeg.av_frame_alloc();
            if (decoded_frame == null)
                throw new InvalidOperationException("Could not allocate an FFmpeg audio frame.");

#if DEBUG
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_DEBUG);
            m_log_callback = (p0, level, format, vl) =>
            {
                if (level > ffmpeg.av_log_get_level())
                    return;

                int lineSize = 1024;
                byte* lineBuffer = stackalloc byte[lineSize];
                int printPrefix = 1;
                ffmpeg.av_log_format_line(
                    p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
                System.Diagnostics.Trace.Write(
                    Marshal.PtrToStringAnsi((IntPtr)lineBuffer));
            };
            ffmpeg.av_log_set_callback(m_log_callback);
#endif

            m_read_packet_callback = readPacketCallback;
            m_seek_callback = seekCallback;

            fmt_ctx = ffmpeg.avformat_alloc_context();
            if (fmt_ctx == null)
                throw new InvalidOperationException("Could not allocate an FFmpeg format context.");

            const int avioBufferSize = 65536;
            byte* avioBuffer = (byte*)ffmpeg.av_malloc(avioBufferSize);
            if (avioBuffer == null)
                throw new OutOfMemoryException("Could not allocate the FFmpeg I/O buffer.");

            avio_ctx = ffmpeg.avio_alloc_context(
                avioBuffer,
                avioBufferSize,
                0,
                null,
                m_read_packet_callback,
                null,
                m_seek_callback);
            if (avio_ctx == null)
            {
                ffmpeg.av_free(avioBuffer);
                throw new InvalidOperationException("Could not allocate an FFmpeg I/O context.");
            }

            fmt_ctx->pb = avio_ctx;
            fmt_ctx->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;

            AVInputFormat* format = ffmpeg.av_find_input_format(m_settings.Format);
            if (format == null)
                throw new NotSupportedException($"FFmpeg input format '{m_settings.Format}' is unavailable.");

            AVFormatContext* openedContext = fmt_ctx;
            int openResult = ffmpeg.avformat_open_input(
                &openedContext, null, format, null);
            fmt_ctx = openedContext;
            CheckFfmpegResult(openResult);
            CheckFfmpegResult(ffmpeg.avformat_find_stream_info(fmt_ctx, null));

            int matchingStream = -1;
            int matchingStreams = 0;
            for (int i = 0; i < (int)fmt_ctx->nb_streams; i++)
            {
                AVStream* candidate = fmt_ctx->streams[i];
                if (candidate->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO &&
                    (m_settings.StreamId == 0 || m_settings.StreamId == candidate->id))
                {
                    matchingStream = i;
                    matchingStreams++;
                }
            }

            if (matchingStreams == 0)
                throw new InvalidDataException("No matching audio stream was found.");
            if (matchingStreams != 1)
                throw new InvalidDataException("More than one audio stream matches.");

            stream = fmt_ctx->streams[matchingStream];
            _sampleCount = -1;

            int bitsPerSample = stream->codecpar->bits_per_raw_sample != 0
                ? stream->codecpar->bits_per_raw_sample
                : stream->codecpar->bits_per_coded_sample;
            int channels = stream->codecpar->ch_layout.nb_channels;
            int sampleRate = stream->codecpar->sample_rate;
            ulong nativeMask =
                stream->codecpar->ch_layout.order == AVChannelOrder.AV_CHANNEL_ORDER_NATIVE
                    ? stream->codecpar->ch_layout.u.mask
                    : 0;
            pcm = new AudioPCMConfig(
                bitsPerSample,
                channels,
                sampleRate,
                (AudioPCMConfig.SpeakerConfig)nativeMask);

            codec = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
            if (codec == null)
                throw new NotSupportedException("The matching FFmpeg decoder is unavailable.");

            c = ffmpeg.avcodec_alloc_context3(codec);
            if (c == null)
                throw new InvalidOperationException("Could not allocate an FFmpeg codec context.");

            CheckFfmpegResult(ffmpeg.avcodec_parameters_to_context(c, stream->codecpar));
            c->request_sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S32;
            CheckFfmpegResult(ffmpeg.avcodec_open2(c, null, null));

            m_decoded_frame_offset = 0;
            m_decoded_frame_size = 0;
            _sampleOffset = 0;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (m_disposed)
                return;
            m_disposed = true;

            if (c != null)
            {
                AVCodecContext* codecContext = c;
                ffmpeg.avcodec_free_context(&codecContext);
                c = null;
            }

            if (decoded_frame != null)
            {
                AVFrame* frame = decoded_frame;
                ffmpeg.av_frame_free(&frame);
                decoded_frame = null;
            }

            if (pkt != null)
            {
                AVPacket* packet = pkt;
                ffmpeg.av_packet_free(&packet);
                pkt = null;
            }

            if (fmt_ctx != null)
            {
                AVFormatContext* formatContext = fmt_ctx;
                ffmpeg.avformat_close_input(&formatContext);
                fmt_ctx = null;
                stream = null;
            }

            if (avio_ctx != null)
            {
                if (avio_ctx->buffer != null)
                {
                    ffmpeg.av_free(avio_ctx->buffer);
                    avio_ctx->buffer = null;
                }
                AVIOContext* ioContext = avio_ctx;
                ffmpeg.avio_context_free(&ioContext);
                avio_ctx = null;
            }

            if (disposing && m_stream != null)
            {
                m_stream.Dispose();
                m_stream = null;
            }
        }

        ~AudioDecoder()
        {
            Dispose(false);
        }

        private DecoderSettings m_settings;

        public IAudioDecoderSettings Settings => m_settings;

        public AudioPCMConfig PCM => pcm;

        public string Path => _path;

        public TimeSpan Duration
        {
            get
            {
                ThrowIfDisposed();
                // Sadly, duration is unreliable for most codecs.
                if (stream->codecpar->codec_id == AVCodecID.AV_CODEC_ID_MLP)
                    return TimeSpan.Zero;
                if (stream->duration > 0)
                    return TimeSpan.FromSeconds((double)stream->duration / stream->codecpar->sample_rate);
                if (fmt_ctx->duration > 0)
                    return TimeSpan.FromSeconds((double)fmt_ctx->duration / ffmpeg.AV_TIME_BASE);
                return TimeSpan.Zero;
            }
        }

        public long Length => _sampleCount;

        public long Position
        {
            get => _sampleOffset;

            set
            {
                ThrowIfDisposed();
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                if (IsSeekableAiffPcm(stream->codecpar->codec_id))
                {
                    int res = ffmpeg.av_seek_frame(
                        fmt_ctx, stream->index, value, ffmpeg.AVSEEK_FLAG_FRAME);
                    CheckFfmpegResult(res);
                    ffmpeg.avcodec_flush_buffers(c);
                    ffmpeg.av_packet_unref(pkt);
                    ffmpeg.av_frame_unref(decoded_frame);
                    m_decoded_frame_offset = 0;
                    m_decoded_frame_size = 0;
                    m_demuxEof = false;
                    m_flushSent = false;
                    _sampleOffset = value;
                }
                else
                {
                    throw new NotSupportedException("Seeking is supported only for AIFF PCM input.");
                }
            }
        }

        private bool IsSeekableAiffPcm(AVCodecID codecId)
        {
            if (!string.Equals(
                    m_settings.Format,
                    "aiff",
                    StringComparison.Ordinal))
                return false;

            return codecId == AVCodecID.AV_CODEC_ID_PCM_S8 ||
                codecId == AVCodecID.AV_CODEC_ID_PCM_U8 ||
                codecId == AVCodecID.AV_CODEC_ID_PCM_S16BE ||
                codecId == AVCodecID.AV_CODEC_ID_PCM_S24BE ||
                codecId == AVCodecID.AV_CODEC_ID_PCM_S32BE;
        }

        public long Remaining => _sampleCount < 0 ? -1 : _sampleCount - _sampleOffset;

        public void Close()
        {
            Dispose();
        }

        byte[] _readBuffer;
        int readPacketCallback(void* @opaque, byte* @buf, int @buf_size)
        {
            try
            {
                if (@buf_size <= 0)
                    return ffmpeg.AVERROR_EXTERNAL;

                // TODO: if instead of calling ffmpeg.av_malloc for
                // the buffer we pass to ffmpeg.avio_alloc_context
                // we just pin _readBuffer, we wouldn't need to Copy.
                if (_readBuffer == null || _readBuffer.Length < @buf_size)
                    _readBuffer = new byte[Math.Max(@buf_size, 0x4000)];
                int len = m_stream.Read(_readBuffer, 0, @buf_size);
                if (len > 0)
                    Marshal.Copy(_readBuffer, 0, (IntPtr)buf, len);
                else if (len == 0)
                    return ffmpeg.AVERROR_EOF;
                return len;
            }
            catch (Exception exception)
            {
                CaptureCallbackException(exception);
                return ffmpeg.AVERROR_EXTERNAL;
            }
        }

        long seekCallback(void* @opaque, long @offset, int @whence)
        {
            try
            {
                if (@whence == ffmpeg.AVSEEK_SIZE)
                    return m_stream.Length;
                @whence &= ~ffmpeg.AVSEEK_FORCE;
                if (@whence < (int)SeekOrigin.Begin || @whence > (int)SeekOrigin.End)
                    throw new IOException($"FFmpeg requested unsupported seek mode {@whence}.");
                return m_stream.Seek(@offset, (SeekOrigin)@whence);
            }
            catch (Exception exception)
            {
                CaptureCallbackException(exception);
                return ffmpeg.AVERROR_EXTERNAL;
            }
        }

        private void CaptureCallbackException(Exception exception)
        {
            Interlocked.CompareExchange(ref m_callbackException, exception, null);
        }

        private void CheckFfmpegResult(int result)
        {
            if (result >= 0)
                return;
            ThrowCallbackExceptionIfAny();
            result.ThrowExceptionIfError();
        }

        private void ThrowCallbackExceptionIfAny()
        {
            Exception exception = Interlocked.Exchange(ref m_callbackException, null);
            if (exception != null)
                throw new IOException("FFmpeg custom I/O failed.", exception);
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
                throw new ObjectDisposedException(nameof(AudioDecoder));
        }

        private void Fill()
        {
            while (true)
            {
                if (m_decoded_frame_size > 0)
                    return;

                int ret = ffmpeg.avcodec_receive_frame(c, decoded_frame);
                if (ret == ffmpeg.AVERROR_EOF)
                    return;
                if (ret != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    CheckFfmpegResult(ret);
                    m_decoded_frame_offset = 0;
                    m_decoded_frame_size = decoded_frame->nb_samples;
                    return;
                }

                if (m_demuxEof)
                {
                    if (m_flushSent)
                        throw new InvalidDataException(
                            "FFmpeg requested more input after the decoder flush.");
                    CheckFfmpegResult(ffmpeg.avcodec_send_packet(c, null));
                    m_flushSent = true;
                    continue;
                }

                ret = ffmpeg.av_read_frame(fmt_ctx, pkt);
                if (ret != 0)
                {
                    if (ret == ffmpeg.AVERROR_EOF)
                    {
                        m_demuxEof = true;
                        continue;
                    }
                    CheckFfmpegResult(ret);
                }

                try
                {
                    if (pkt->size != 0 && pkt->stream_index == stream->index)
                    {
                        CheckFfmpegResult(ffmpeg.avcodec_send_packet(c, pkt));
                    }
                }
                finally
                {
                    ffmpeg.av_packet_unref(pkt);
                }
            }
        }

        public int Read(AudioBuffer buff, int maxLength)
        {
            ThrowIfDisposed();
            if (buff == null)
                throw new ArgumentNullException(nameof(buff));
            if (maxLength < 0)
                throw new ArgumentOutOfRangeException(nameof(maxLength));

            buff.Prepare(this, maxLength);

            long buffOffset = 0;
            long samplesNeeded = buff.Length;
            long _channelCount = pcm.ChannelCount;

            while (samplesNeeded != 0)
            {
                if (m_decoded_frame_size == 0)
                {
                    Fill();
                    if (m_decoded_frame_size == 0)
                        break;
                }
                long copyCount = Math.Min(samplesNeeded, m_decoded_frame_size);

                // TODO: if AudioBuffer supported different sample formats,
                // this would be simpler. One complication though we would still
                // need shifts.
                switch (c->sample_fmt)
                {
                    case AVSampleFormat.AV_SAMPLE_FMT_S32:
                        {
                            byte* ptr = decoded_frame->data[0u] + c->ch_layout.nb_channels * 4 * m_decoded_frame_offset;
                            int rshift = 32 - pcm.BitsPerSample;
                            int* smp = (int*)ptr;
                            fixed (int* dst_start = &buff.Samples[buffOffset, 0])
                            {
                                int* dst = dst_start;
                                int* dst_end = dst_start + copyCount * c->ch_layout.nb_channels;
                                while (dst < dst_end)
                                    *(dst++) = *(smp++) >> rshift;
                            }
                        }
                        break;
                    case AVSampleFormat.AV_SAMPLE_FMT_S16:
                        {
                            short* ptr = (short*)(decoded_frame->data[0u]) + c->ch_layout.nb_channels * m_decoded_frame_offset;
                            fixed (int* dst_start = &buff.Samples[buffOffset, 0])
                            {
                                int* dst = dst_start;
                                int* dst_end = dst_start + copyCount * c->ch_layout.nb_channels;
                                while (dst < dst_end)
                                    *(dst++) = *(ptr++);
                            }
                        }
                        break;
                    case AVSampleFormat.AV_SAMPLE_FMT_S16P:
                        for (Int32 iChan = 0; iChan < _channelCount; iChan++)
                        {
                            fixed (int* pMyBuffer = &buff.Samples[buffOffset, iChan])
                            {
                                int* pMyBufferPtr = pMyBuffer;
                                short* pFLACBuffer =
                                    (short*)decoded_frame->extended_data[iChan] +
                                    m_decoded_frame_offset;
                                short* pFLACBufferEnd = pFLACBuffer + copyCount;
                                while (pFLACBuffer < pFLACBufferEnd)
                                {
                                    *pMyBufferPtr = *pFLACBuffer;
                                    pMyBufferPtr += _channelCount;
                                    pFLACBuffer++;
                                }
                            }
                        }
                        break;
                    case AVSampleFormat.AV_SAMPLE_FMT_S32P:
                        {
                            int rshift = 32 - pcm.BitsPerSample;
                            for (Int32 iChan = 0; iChan < _channelCount; iChan++)
                            {
                                fixed (int* output = &buff.Samples[buffOffset, iChan])
                                {
                                    int* outputSample = output;
                                    int* inputSample =
                                        (int*)decoded_frame->extended_data[iChan] +
                                        m_decoded_frame_offset;
                                    int* inputEnd = inputSample + copyCount;
                                    while (inputSample < inputEnd)
                                    {
                                        *outputSample = *inputSample >> rshift;
                                        outputSample += _channelCount;
                                        inputSample++;
                                    }
                                }
                            }
                        }
                        break;
                    default:
                        throw new NotSupportedException(
                            $"FFmpeg returned unsupported audio sample format {c->sample_fmt}.");
                }

                samplesNeeded -= copyCount;
                buffOffset += copyCount;
                m_decoded_frame_offset += copyCount;
                m_decoded_frame_size -= copyCount;
                _sampleOffset += copyCount;
            }

            buff.Length = (int)buffOffset;
            // EOF
            if (buff.Length == 0)
                _sampleCount = _sampleOffset;
            return buff.Length;
        }

        AVPacket* pkt;
        AVFrame* decoded_frame;
        AVCodec* codec;
        AVCodecContext* c;
        AVFormatContext* fmt_ctx;
        AVIOContext* avio_ctx;
        AVStream* stream;

        avio_alloc_context_read_packet m_read_packet_callback;
        avio_alloc_context_seek m_seek_callback;
#if DEBUG
        av_log_set_callback_callback m_log_callback;
#endif

        long _sampleCount, _sampleOffset;
        AudioPCMConfig pcm;
        string _path;
        Stream m_stream;
        long m_decoded_frame_offset;
        long m_decoded_frame_size;
        Exception m_callbackException;
        bool m_demuxEof;
        bool m_flushSent;
        bool m_disposed;
    }
}
