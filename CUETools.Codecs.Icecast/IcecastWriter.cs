using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.IO;
using CUETools.Codecs;

namespace CUETools.Codecs.Icecast
{
	public class IcecastWriter: IAudioDest
	{
		private const int NetworkTimeoutMilliseconds = 30000;
		private long _sampleOffset = 0;
        private Codecs.WAV.EncoderSettings m_settings;
		private libmp3lame.AudioEncoder encoder = null;
		private HttpWebRequest req = null;
		private HttpWebResponse resp = null;
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

		public HttpWebResponse Response
		{
			get
			{
				return resp;
			}
		}

		// Opens an Icecast SOURCE stream. Two things to know before touching this:
		// 1) HTTP Basic is only an encoding. IcecastEndpointPolicy therefore selects HTTPS by
		//    default and permits HTTP only through the persisted explicit legacy opt-in.
		// 2) The reflection below pokes private HttpWebRequest/HttpWebResponse internals to
		//    force the legacy SOURCE/chunked streaming behavior. This is tightly coupled to
		//    .NET Framework's internal field names and will not survive the move to modern
		//    .NET (HttpClient) - it is a known migration landmine, not a stable API.
		public void Connect()
		{
			try
			{
				Uri uri = IcecastEndpointPolicy.BuildSourceUri(settings);
				req = (HttpWebRequest)WebRequest.Create(uri);
				//req.Proxy = proxy;
				//req.UserAgent = userAgent;
				req.ProtocolVersion = HttpVersion.Version10; // new Version("ICE/1.0");
				req.Method = "SOURCE";
				req.ContentType = "audio/mpeg";
				req.Headers.Add("ice-name", settings.Name ?? "no name");
				req.Headers.Add("ice-public", "1");
				if ((settings.Url ?? "") != "") req.Headers.Add("ice-url", settings.Url);
				if ((settings.Genre ?? "") != "") req.Headers.Add("ice-genre", settings.Genre);
				if ((settings.Description ?? "") != "") req.Headers.Add("ice-description", settings.Description);
				req.Headers.Add("Authorization", string.Format("Basic {0}", Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("source:{0}", settings.Password)))));
				// Streaming itself has no wall-clock limit, but connection, response, and stalled
				// writes must remain interruptible. Infinite timeouts strand the player thread when
				// a server accepts TCP and then stops speaking.
				req.Timeout = NetworkTimeoutMilliseconds;
				req.ReadWriteTimeout = NetworkTimeoutMilliseconds;
				//req.ContentLength = 999999999;
				req.KeepAlive = false;
				req.SendChunked = true;
				req.AllowWriteStreamBuffering = false;
				req.CachePolicy = new System.Net.Cache.HttpRequestCachePolicy(System.Net.Cache.HttpRequestCacheLevel.BypassCache);

				System.Reflection.PropertyInfo pi = typeof(ServicePoint).GetProperty("HttpBehaviour", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				if (pi == null || pi.PropertyType.GetField("Unknown") == null)
					throw new PlatformNotSupportedException("The legacy Icecast HTTP streaming hook is unavailable.");
				pi.SetValue(req.ServicePoint, pi.PropertyType.GetField("Unknown").GetValue(null), null);

				reqStream = req.GetRequestStream();

				System.Reflection.FieldInfo fi = reqStream.GetType().GetField("m_HttpWriteMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				System.Reflection.MethodInfo mi = reqStream.GetType().GetMethod("CallDone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new Type[0], null);
				if (fi == null || fi.FieldType.GetField("Buffer") == null || mi == null)
					throw new PlatformNotSupportedException("The legacy Icecast request-stream hook is unavailable.");
				fi.SetValue(reqStream, fi.FieldType.GetField("Buffer").GetValue(null));
				mi.Invoke(reqStream, null);

				resp = req.GetResponse() as HttpWebResponse;
				if (resp.StatusCode == HttpStatusCode.OK)
				{
                    var encoderSettings = new CUETools.Codecs.libmp3lame.CBREncoderSettings() { PCM = AudioPCMConfig.RedBook };
                    //encoderSettings.StereoMode = settings.JointStereo ?
                    //    CUETools.Codecs.LAME.Interop.MpegMode.JOINT_STEREO :
                    //    CUETools.Codecs.LAME.Interop.MpegMode.STEREO;
                    //encoderSettings.CustomBitrate = settings.Bitrate;
                    encoder = new CUETools.Codecs.libmp3lame.AudioEncoder(encoderSettings, "", reqStream);
				}
			}
			catch (WebException ex)
			{
				if (ex.Status == WebExceptionStatus.ProtocolError)
					resp = ex.Response as HttpWebResponse;
				else
				{
					Cleanup(false);
					throw;
				}
			}
			catch
			{
				Cleanup(false);
				throw;
			}
		}

		public void UpdateMetadata(string artist, string title)
		{
			Uri uri = IcecastEndpointPolicy.BuildMetadataUri(settings, artist, title);
			HttpWebRequest req2 = (HttpWebRequest)WebRequest.Create(uri);
			req2.Method = "GET";
			req2.Credentials = new NetworkCredential("source", settings.Password);
			req2.Timeout = NetworkTimeoutMilliseconds;
			req2.ReadWriteTimeout = NetworkTimeoutMilliseconds;
			//req.Proxy = proxy;
			//req.UserAgent = userAgent;
			//req2.Headers.Add("Authorization", string.Format("Basic {0}", Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Format("source:{0}", settings.Password)))));
			HttpStatusCode accResult = HttpStatusCode.OK;
			try
			{
				using (HttpWebResponse metadataResponse =
					(HttpWebResponse)req2.GetResponse())
				{
					accResult = metadataResponse.StatusCode;
				}
				if (accResult == HttpStatusCode.OK)
				{
				}
			}
			catch (WebException ex)
			{
				HttpWebResponse errorResponse = ex.Response as HttpWebResponse;
				if (ex.Status == WebExceptionStatus.ProtocolError &&
					errorResponse != null)
				{
					using (errorResponse)
						accResult = errorResponse.StatusCode;
				}
				else
					accResult = HttpStatusCode.BadRequest;
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
			HttpWebResponse currentResponse = resp;
			HttpWebRequest currentRequest = req;
			encoder = null;
			reqStream = null;
			resp = null;
			req = null;

			try
			{
				if (currentEncoder != null)
				{
					if (deleteEncoder) currentEncoder.Delete();
					else currentEncoder.Close();
				}
			}
			catch (Exception ex) { failure = ex; }
			try { if (currentStream != null) currentStream.Close(); }
			catch (Exception ex) { if (failure == null) failure = ex; }
			try { if (currentResponse != null) currentResponse.Close(); }
			catch (Exception ex) { if (failure == null) failure = ex; }
			try { if (currentRequest != null) currentRequest.Abort(); }
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
				return encoder == null ? 0 : encoder.BytesWritten;
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
	}
}
