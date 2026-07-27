using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace CUETools.Codecs.Icecast
{
    /// <summary>
    /// The status line returned by Icecast when a source connection is opened.
    /// The response body is intentionally not exposed because a successful source
    /// connection changes immediately into a long-lived upload stream.
    /// </summary>
    public sealed class IcecastResponse
    {
        internal IcecastResponse(HttpStatusCode statusCode, string statusDescription)
        {
            StatusCode = statusCode;
            StatusDescription = NormalizeStatusDescription(statusDescription);
        }

        public HttpStatusCode StatusCode { get; private set; }

        public string StatusDescription { get; private set; }

        internal static string NormalizeStatusDescription(string value)
        {
            if (String.IsNullOrEmpty(value))
                return "";

            StringBuilder safe = new StringBuilder();
            for (int i = 0; i < value.Length && safe.Length < 128; i++)
            {
                char c = value[i];
                if (c >= ' ' && c != '\u007f')
                    safe.Append(c);
            }
            return safe.ToString();
        }
    }

    /// <summary>
    /// A credential-safe protocol failure returned by an Icecast endpoint.
    /// </summary>
    public sealed class IcecastProtocolException : IOException
    {
        internal IcecastProtocolException(
            string operation,
            HttpStatusCode statusCode,
            string statusDescription)
            : base(String.Format(
                CultureInfo.InvariantCulture,
                "Icecast {0} failed with HTTP {1}.",
                operation,
                (int)statusCode))
        {
            StatusCode = statusCode;
            StatusDescription =
                IcecastResponse.NormalizeStatusDescription(statusDescription);
        }

        public HttpStatusCode StatusCode { get; private set; }

        public string StatusDescription { get; private set; }
    }

    /// <summary>
    /// Opens a full-duplex Icecast SOURCE upload without relying on private
    /// HttpWebRequest fields. SOURCE/HTTP 1.0 acknowledges authentication after
    /// the headers and delimits the live MP3 body by closing the connection.
    /// </summary>
    internal sealed class IcecastSourceConnection : IDisposable
    {
        private const int MaximumResponseHeaderBytes = 64 * 1024;
        private readonly TcpClient client;
        private readonly Stream transport;
        private bool closed;

        private IcecastSourceConnection(
            TcpClient client,
            Stream transport,
            IcecastResponse response)
        {
            this.client = client;
            this.transport = transport;
            Response = response;
        }

        internal IcecastResponse Response { get; private set; }

        internal Stream RequestStream
        {
            get { return transport; }
        }

        internal static IcecastSourceConnection Open(
            Uri uri,
            IcecastSettingsData settings,
            int timeoutMilliseconds)
        {
            if (uri == null)
                throw new ArgumentNullException("uri");
            if (settings == null)
                throw new ArgumentNullException("settings");

            byte[] requestHeaders = BuildRequestHeaders(uri, settings);
            TcpClient client = new TcpClient();
            Stream transport = null;
            try
            {
                ConnectWithTimeout(client, uri.Host, uri.Port, timeoutMilliseconds);
                client.NoDelay = true;
                client.ReceiveTimeout = timeoutMilliseconds;
                client.SendTimeout = timeoutMilliseconds;

                NetworkStream networkStream = client.GetStream();
                SetStreamTimeouts(networkStream, timeoutMilliseconds);
                transport = networkStream;

                if (String.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
                {
                    SslStream tlsStream = new SslStream(networkStream, false);
                    tlsStream.AuthenticateAsClient(uri.Host);
                    SetStreamTimeouts(tlsStream, timeoutMilliseconds);
                    transport = tlsStream;
                }

                transport.Write(requestHeaders, 0, requestHeaders.Length);
                transport.Flush();

                IcecastResponse response = ReadResponse(transport);
                if ((int)response.StatusCode < 200 ||
                    (int)response.StatusCode >= 300)
                    throw new IcecastProtocolException(
                        "source connection",
                        response.StatusCode,
                        response.StatusDescription);

                return new IcecastSourceConnection(
                    client,
                    transport,
                    response);
            }
            catch
            {
                try
                {
                    if (transport != null)
                        transport.Close();
                }
                catch
                {
                }
                try
                {
                    client.Close();
                }
                catch
                {
                }
                throw;
            }
        }

        public void Dispose()
        {
            Close();
        }

        internal void Close()
        {
            if (closed)
                return;
            closed = true;

            Exception failure = null;
            try
            {
                transport.Close();
            }
            catch (Exception ex)
            {
                if (failure == null)
                    failure = ex;
            }
            try
            {
                client.Close();
            }
            catch (Exception ex)
            {
                if (failure == null)
                    failure = ex;
            }

            if (failure != null)
                throw failure;
        }

        private static byte[] BuildRequestHeaders(
            Uri uri,
            IcecastSettingsData settings)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("SOURCE ");
            builder.Append(uri.PathAndQuery);
            builder.Append(" HTTP/1.0\r\n");
            AppendHeader(builder, "Host", uri.Authority);
            AppendHeader(
                builder,
                "Authorization",
                "Basic " + Convert.ToBase64String(
                    Encoding.ASCII.GetBytes(
                        "source:" + (settings.Password ?? ""))));
            AppendHeader(builder, "Content-Type", "audio/mpeg");
            AppendHeader(builder, "Connection", "close");
            AppendHeader(builder, "Ice-Name", settings.Name ?? "no name");
            AppendHeader(builder, "Ice-Public", "1");
            if (!String.IsNullOrEmpty(settings.Url))
                AppendHeader(builder, "Ice-Url", settings.Url);
            if (!String.IsNullOrEmpty(settings.Genre))
                AppendHeader(builder, "Ice-Genre", settings.Genre);
            if (!String.IsNullOrEmpty(settings.Description))
                AppendHeader(builder, "Ice-Description", settings.Description);
            builder.Append("\r\n");
            return Encoding.ASCII.GetBytes(builder.ToString());
        }

        private static void AppendHeader(
            StringBuilder builder,
            string name,
            string value)
        {
            if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                throw new ArgumentException(
                    "Icecast header values cannot contain line breaks.");
            builder.Append(name);
            builder.Append(": ");
            builder.Append(value);
            builder.Append("\r\n");
        }

        private static void ConnectWithTimeout(
            TcpClient client,
            string host,
            int port,
            int timeoutMilliseconds)
        {
            IAsyncResult result = client.BeginConnect(host, port, null, null);
            try
            {
                if (!result.AsyncWaitHandle.WaitOne(timeoutMilliseconds, false))
                    throw new TimeoutException(
                        "The Icecast connection timed out.");
                client.EndConnect(result);
            }
            finally
            {
                result.AsyncWaitHandle.Close();
            }
        }

        private static void SetStreamTimeouts(
            Stream stream,
            int timeoutMilliseconds)
        {
            if (!stream.CanTimeout)
                return;
            stream.ReadTimeout = timeoutMilliseconds;
            stream.WriteTimeout = timeoutMilliseconds;
        }

        private static IcecastResponse ReadResponse(Stream stream)
        {
            MemoryStream headerBytes = new MemoryStream();
            int matched = 0;
            while (headerBytes.Length < MaximumResponseHeaderBytes)
            {
                int value = stream.ReadByte();
                if (value < 0)
                    throw new EndOfStreamException(
                        "Icecast closed the connection before returning a response.");

                headerBytes.WriteByte((byte)value);
                switch (matched)
                {
                    case 0:
                        matched = value == '\r' ? 1 : 0;
                        break;
                    case 1:
                        matched = value == '\n' ? 2 : 0;
                        break;
                    case 2:
                        matched = value == '\r' ? 3 : 0;
                        break;
                    case 3:
                        matched = value == '\n' ? 4 : 0;
                        break;
                }
                if (matched == 4)
                    break;
            }

            if (matched != 4)
                throw new InvalidDataException(
                    "Icecast returned an oversized or malformed response header.");

            string headers = Encoding.ASCII.GetString(headerBytes.ToArray());
            int lineEnd = headers.IndexOf("\r\n", StringComparison.Ordinal);
            if (lineEnd <= 0)
                throw new InvalidDataException(
                    "Icecast returned a malformed HTTP status line.");

            string statusLine = headers.Substring(0, lineEnd);
            if (!statusLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) &&
                !statusLine.StartsWith("ICY ", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Icecast returned an unsupported response protocol.");

            int firstSpace = statusLine.IndexOf(' ');
            int secondSpace = firstSpace < 0
                ? -1
                : statusLine.IndexOf(' ', firstSpace + 1);
            string statusText = secondSpace < 0
                ? statusLine.Substring(firstSpace + 1)
                : statusLine.Substring(firstSpace + 1, secondSpace - firstSpace - 1);
            int statusCode;
            if (firstSpace < 0 ||
                !Int32.TryParse(
                    statusText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out statusCode) ||
                statusCode < 100 ||
                statusCode > 999)
                throw new InvalidDataException(
                    "Icecast returned an invalid HTTP status code.");

            string statusDescription = secondSpace < 0
                ? ""
                : statusLine.Substring(secondSpace + 1).Trim();
            return new IcecastResponse(
                (HttpStatusCode)statusCode,
                statusDescription);
        }
    }

}
