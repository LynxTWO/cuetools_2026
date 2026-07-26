using System;
using System.Globalization;

namespace CUETools.Codecs.Icecast
{
    /// <summary>
    /// Builds every credential-bearing Icecast endpoint from one transport policy. HTTPS is the
    /// default. HTTP is possible only after the user explicitly enables the legacy insecure mode.
    /// </summary>
    public static class IcecastEndpointPolicy
    {
        public static Uri BuildSourceUri(IcecastSettingsData settings)
        {
            UriBuilder builder = CreateBaseBuilder(settings);
            builder.Path = NormalizeMount(settings.Mount);
            builder.Query = "";
            Uri uri = builder.Uri;
            EnsureCredentialTransport(uri, settings.AllowInsecureHttp);
            return uri;
        }

        public static Uri BuildMetadataUri(
            IcecastSettingsData settings,
            string artist,
            string title)
        {
            UriBuilder builder = CreateBaseBuilder(settings);
            builder.Path = "/admin/metadata";

            string song = !String.IsNullOrEmpty(artist) && !String.IsNullOrEmpty(title)
                ? artist + " - " + title
                : title ?? "";
            string query = "mode=updinfo&mount=" +
                Uri.EscapeDataString(NormalizeMount(settings.Mount));
            if (!String.IsNullOrEmpty(song))
                query += "&song=" + Uri.EscapeDataString(song);
            builder.Query = query;

            Uri uri = builder.Uri;
            EnsureCredentialTransport(uri, settings.AllowInsecureHttp);
            return uri;
        }

        public static void EnsureCredentialTransport(Uri uri, bool allowInsecureHttp)
        {
            if (uri == null)
                throw new ArgumentNullException("uri");
            if (String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return;
            if (String.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                allowInsecureHttp)
                return;
            throw new InvalidOperationException(
                "Icecast credentials require HTTPS unless insecure HTTP is explicitly enabled.");
        }

        private static UriBuilder CreateBaseBuilder(IcecastSettingsData settings)
        {
            if (settings == null)
                throw new ArgumentNullException("settings");

            string server = (settings.Server ?? "").Trim();
            if (server.StartsWith("[", StringComparison.Ordinal) &&
                server.EndsWith("]", StringComparison.Ordinal) &&
                server.Length > 2)
                server = server.Substring(1, server.Length - 2);

            if (server.Length == 0 ||
                server.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                server.IndexOf('/') >= 0 ||
                server.IndexOf('\\') >= 0 ||
                server.IndexOf('@') >= 0 ||
                server.IndexOf('?') >= 0 ||
                server.IndexOf('#') >= 0 ||
                Uri.CheckHostName(server) == UriHostNameType.Unknown)
                throw new FormatException("The Icecast server must be a host name or IP address.");

            int port;
            if (!Int32.TryParse(
                settings.Port,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out port) ||
                port < 1 ||
                port > 65535)
                throw new FormatException("The Icecast port must be between 1 and 65535.");

            string scheme = settings.AllowInsecureHttp
                ? Uri.UriSchemeHttp
                : Uri.UriSchemeHttps;
            return new UriBuilder(scheme, server, port);
        }

        private static string NormalizeMount(string mount)
        {
            string value = (mount ?? "").Trim();
            if (value.Length == 0)
                throw new FormatException("An Icecast mount is required.");
            if (value.IndexOf('?') >= 0 ||
                value.IndexOf('#') >= 0 ||
                value.IndexOf('\\') >= 0)
                throw new FormatException("The Icecast mount contains invalid URI characters.");
            return value.StartsWith("/", StringComparison.Ordinal) ? value : "/" + value;
        }
    }
}
