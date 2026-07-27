using System;
using System.Globalization;
using System.Net;
using System.IO;
using CUETools.Codecs;

namespace CUETools.Codecs.Icecast
{
	public class IcecastWriter: IAudioDest
	{
		private const int NetworkTimeoutMilliseconds = 30000;
		private long _sampleOffset = 0;
		private long _bytesWritten = 0;
        private Codecs.WAV.EncoderSettings m_settings;
		private libmp3lame.AudioEncoder encoder = null;
		private IcecastSourceConnection sourceConnection = null;
		private IcecastResponse resp = null;
		private Stream reqStream;
		private IcecastSettingsData settings = null;

		public IAudioDest Encoder
		{
			get
			{
				return encoder;
			}
		}

		public IcecastWriter(AudioPCMConfig pcm, IcecastSettingsData settings)
		{
            this.m_settings = new Codecs.WAV.EncoderSettings(pcm);
			this.settings = settings;
		}

		#region IAudioDest Members

		[Obsolete(
			"Version 3 uses a raw full-duplex transport and has no HttpWebResponse. " +
			"Use ProtocolResponse; rejected connections throw IcecastProtocolException.")]
		public HttpWebResponse Response
		{
			get
			{
				if (resp == null)
					return null;
				throw new NotSupportedException(
					"The Icecast 3 streaming transport has no HttpWebResponse. " +
					"Use ProtocolResponse.");
			}
		}

		public IcecastResponse ProtocolResponse
		{
			get
			{
				return resp;
			}
		}

		// Opens an Icecast source stream. Two things to know before touching this:
		// 1) HTTP Basic is only an encoding. IcecastEndpointPolicy therefore selects HTTPS by
		//    default and permits HTTP only through the persisted explicit legacy opt-in.
		// 2) Source uploads are full-duplex: Icecast returns the authentication result while
		//    the connection-delimited MP3 body must remain open. IcecastSourceConnection
		//    implements that protocol directly instead of mutating private HttpWebRequest state.
		public void Connect()
		{
			if (sourceConnection != null)
				throw new InvalidOperationException("The Icecast writer is already connected.");

			try
			{
				// Reject local configuration errors before sending credentials or opening a
				// network connection.
				if (!IcecastSettingsData.IsSupportedBitrate(settings.Bitrate))
					throw new ArgumentOutOfRangeException(
						"settings",
						"Icecast MP3 bitrate must be 96, 128, 192, 256, or 320 kbps.");
				Uri uri = IcecastEndpointPolicy.BuildSourceUri(settings);
				sourceConnection = IcecastSourceConnection.Open(
					uri,
					settings,
					NetworkTimeoutMilliseconds);
				resp = sourceConnection.Response;
				reqStream = sourceConnection.RequestStream;

				var encoderSettings =
					new CUETools.Codecs.libmp3lame.CBREncoderSettings()
					{
						PCM = AudioPCMConfig.RedBook,
						EncoderMode = settings.Bitrate.ToString(
							CultureInfo.InvariantCulture),
						StereoMode = settings.JointStereo
							? CUETools.Codecs.libmp3lame.LameStereoMode.JointStereo
							: CUETools.Codecs.libmp3lame.LameStereoMode.Stereo,
					};
				encoder = new CUETools.Codecs.libmp3lame.AudioEncoder(encoderSettings, "", reqStream);
			}
			catch
			{
				// Authentication and handshake failures must not attempt to flush
				// more audio into a connection the server has already rejected.
				Cleanup(true);
				throw;
			}
		}

		public void UpdateMetadata(string artist, string title)
		{
			Uri uri = IcecastEndpointPolicy.BuildMetadataUri(settings, artist, title);
			HttpWebRequest req2 = (HttpWebRequest)WebRequest.Create(uri);
			req2.Method = "GET";
			req2.Credentials = new NetworkCredential("source", settings.Password);
			req2.PreAuthenticate = true;
			// Never replay source credentials through a redirect. The configured endpoint must
			// authenticate and answer directly; operators can update the saved host/port instead.
			req2.AllowAutoRedirect = false;
			req2.Timeout = NetworkTimeoutMilliseconds;
			req2.ReadWriteTimeout = NetworkTimeoutMilliseconds;
			try
			{
				using (HttpWebResponse metadataResponse =
					(HttpWebResponse)req2.GetResponse())
				{
					if ((int)metadataResponse.StatusCode < 200 ||
						(int)metadataResponse.StatusCode >= 300)
						throw new IcecastProtocolException(
							"metadata update",
							metadataResponse.StatusCode,
							metadataResponse.StatusDescription);
				}
			}
			catch (WebException ex)
			{
				HttpWebResponse errorResponse = ex.Response as HttpWebResponse;
				if (ex.Status == WebExceptionStatus.ProtocolError &&
					errorResponse != null)
				{
					HttpStatusCode statusCode;
					string statusDescription;
					using (errorResponse)
					{
						statusCode = errorResponse.StatusCode;
						statusDescription = errorResponse.StatusDescription;
					}
					throw new IcecastProtocolException(
						"metadata update",
						statusCode,
						statusDescription);
				}
				throw new IOException("Icecast metadata update failed.", ex);
			}
		}

		public void Close()
		{
			Exception failure = Cleanup(false);
			if (failure != null)
				throw new IOException("Icecast stream cleanup failed.", failure);
		}

		public void Delete()
		{
			Exception failure = Cleanup(true);
			if (failure != null)
				throw new IOException("Icecast stream abort cleanup failed.", failure);
		}

		private Exception Cleanup(bool deleteEncoder)
		{
			Exception failure = null;
			libmp3lame.AudioEncoder currentEncoder = encoder;
			Stream currentStream = reqStream;
			IcecastSourceConnection currentConnection = sourceConnection;
			if (currentEncoder != null)
				_bytesWritten = currentEncoder.BytesWritten;
			encoder = null;
			reqStream = null;
			resp = null;
			sourceConnection = null;

			try
			{
				if (currentEncoder != null)
				{
					if (deleteEncoder) currentEncoder.Abort();
					else currentEncoder.Close();
					_bytesWritten = currentEncoder.BytesWritten;
				}
			}
			catch (Exception ex) { failure = ex; }
			try { if (currentStream != null) currentStream.Close(); }
			catch (Exception ex) { if (failure == null) failure = ex; }
			try { if (currentConnection != null) currentConnection.Close(); }
			catch (Exception ex) { if (failure == null) failure = ex; }
			return failure;
		}

		AudioBuffer tmp;

		public void Write(AudioBuffer src)
		{
			if (encoder == null)
				throw new Exception("not connected");

			if (tmp == null || tmp.Size < src.Size)
				tmp = new AudioBuffer(AudioPCMConfig.RedBook, src.Size);
			tmp.Prepare(-1);
			Buffer.BlockCopy(src.Float, 0, tmp.Float, 0, src.Length * 8);
			tmp.Length = src.Length;
			encoder.Write(tmp);
			_sampleOffset += src.Length;
		}

        public long Position => _sampleOffset;

		public long FinalSampleCount
		{
			set { ; }
		}

        public IAudioEncoderSettings Settings => m_settings;

        public string Path => null;
		#endregion

		public long BytesWritten
		{
			get
			{
				return encoder == null ? _bytesWritten : encoder.BytesWritten;
			}
		}
	}

	public class IcecastSettingsData
	{
		public IcecastSettingsData()
		{
			Port = "8000";
			Bitrate = 192;
			JointStereo = true;
			AllowInsecureHttp = false;
		}

		private string server;
		private string password;
		private string mount;
		private string name;
		private string description;
		private string url;
		private string genre;

		public string Server { get { return server; } set { server = value; } }
		public string Port { get; set; }
		public string Password { get { return password; } set { password = value; } }
		public string Mount { get { return mount; } set { mount = value; } }
		public string Name { get { return name; } set { name = value; } }
		public string Description { get { return description; } set { description = value; } }
		public string Url { get { return url; } set { url = value; } }
		public string Genre { get { return genre; } set { genre = value; } }
		public int    Bitrate { get; set; }
		public bool   JointStereo { get; set; }
		public bool   AllowInsecureHttp { get; set; }

		public static bool IsSupportedBitrate(int bitrate)
		{
			foreach (int supported in
				CUETools.Codecs.libmp3lame.CBREncoderSettings.bps_table)
				if (supported == bitrate)
					return true;
			return false;
		}
	}
}
