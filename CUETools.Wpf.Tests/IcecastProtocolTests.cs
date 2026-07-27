using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CUETools.Codecs;
using CUETools.Codecs.Icecast;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class IcecastProtocolTests
{
    private const string Password = "throwaway-test-password";
    private static readonly AudioPCMConfig Cd = AudioPCMConfig.RedBook;

    [TestMethod]
    public void LameCbrMissingStereoSettingPreservesAutomaticMode()
    {
        var fresh = new CUETools.Codecs.libmp3lame.CBREncoderSettings();
        Assert.AreEqual(
            CUETools.Codecs.libmp3lame.LameStereoMode.Auto,
            fresh.StereoMode);

        var restored =
            Newtonsoft.Json.JsonConvert.DeserializeObject<
                CUETools.Codecs.libmp3lame.CBREncoderSettings>("{}");
        Assert.IsNotNull(restored);
        Assert.AreEqual(
            CUETools.Codecs.libmp3lame.LameStereoMode.Auto,
            restored.StereoMode,
            "profiles written before StereoMode existed must retain LAME auto mode");
    }

    [TestMethod]
    public async Task SourceConnection_StreamsMp3UntilCleanClose()
    {
        using var server = new FakeHttpServer(
            HttpStatusCode.OK,
            readSourceBody: true);
        var writer = new IcecastWriter(Cd, NewSettings(server.Port));

        writer.Connect();
        Assert.IsNotNull(writer.ProtocolResponse);
        Assert.AreEqual(HttpStatusCode.OK, writer.ProtocolResponse.StatusCode);
        var lameSettings =
            writer.Encoder.Settings as CUETools.Codecs.libmp3lame.CBREncoderSettings;
        Assert.IsNotNull(lameSettings);
        Assert.AreEqual("192", lameSettings.EncoderMode);
        Assert.AreEqual(
            CUETools.Codecs.libmp3lame.LameStereoMode.JointStereo,
            lameSettings.StereoMode);
        TargetInvocationException legacyFailure =
            Assert.ThrowsException<TargetInvocationException>(
                () => typeof(IcecastWriter)
                    .GetProperty("Response")
                    ?.GetValue(writer));
        Assert.IsInstanceOfType<NotSupportedException>(
            legacyFailure.InnerException);

        var buffer = new AudioBuffer(Cd, 4096);
        buffer.Prepare(4096);
        _ = buffer.Float;
        writer.Write(buffer);
        long bytesBeforeClose = writer.BytesWritten;

        writer.Close();
        writer.Close();
        long finalBytesWritten = writer.BytesWritten;

        FakeHttpExchange exchange =
            await server.Exchange.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(
            exchange.Headers.StartsWith(
                "SOURCE /cuetools-test.mp3 HTTP/1.0\r\n",
                StringComparison.Ordinal));
        Assert.IsFalse(
            exchange.Headers.Contains(
                "\r\nTransfer-Encoding:",
                StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(
            exchange.Headers,
            "\r\nAuthorization: Basic " +
            Convert.ToBase64String(
                Encoding.ASCII.GetBytes("source:" + Password)) +
            "\r\n");
        Assert.IsTrue(bytesBeforeClose > 0, "LAME must emit MP3 bytes.");
        Assert.IsTrue(
            exchange.Body.Length >= bytesBeforeClose,
            "the server must receive all encoded bytes, including the clean flush");
        Assert.AreEqual(
            finalBytesWritten,
            exchange.Body.LongLength,
            "every MP3 byte counted after Close must reach the server");
        Assert.AreEqual(4096L, writer.Position);
        Assert.IsTrue(writer.BytesWritten >= bytesBeforeClose);
    }

    [TestMethod]
    public async Task SourceConnection_RejectsHttpErrorWithoutLeakingCredential()
    {
        string untrustedReason = new string('X', 256) + Password;
        using var server = new FakeHttpServer(
            HttpStatusCode.Unauthorized,
            readSourceBody: false,
            responseReason: untrustedReason);
        var writer = new IcecastWriter(Cd, NewSettings(server.Port));

        IcecastProtocolException failure =
            Assert.ThrowsException<IcecastProtocolException>(writer.Connect);
        Assert.AreEqual(HttpStatusCode.Unauthorized, failure.StatusCode);
        Assert.IsFalse(
            failure.Message.Contains(Password, StringComparison.Ordinal));
        Assert.IsFalse(
            failure.Message.Contains("XXXX", StringComparison.Ordinal));
        Assert.AreEqual(128, failure.StatusDescription.Length);
        Assert.IsNull(writer.ProtocolResponse);
        writer.Close();

        FakeHttpExchange exchange =
            await server.Exchange.WaitAsync(TimeSpan.FromSeconds(10));
        StringAssert.Contains(exchange.Headers, "Authorization: Basic ");
    }

    [TestMethod]
    public async Task MetadataUpdate_SurfacesNonSuccessStatus()
    {
        using var server = new FakeHttpServer(
            HttpStatusCode.NotFound,
            readSourceBody: false);
        var writer = new IcecastWriter(Cd, NewSettings(server.Port));

        IcecastProtocolException failure =
            Assert.ThrowsException<IcecastProtocolException>(
                () => writer.UpdateMetadata("Artist", "Title"));
        Assert.AreEqual(HttpStatusCode.NotFound, failure.StatusCode);
        Assert.IsFalse(
            failure.Message.Contains(Password, StringComparison.Ordinal));

        FakeHttpExchange exchange =
            await server.Exchange.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(
            exchange.Headers.StartsWith(
                "GET /admin/metadata?",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MetadataUpdate_DoesNotFollowRedirectsWithSourceCredentials()
    {
        using var server = new FakeHttpServer(
            HttpStatusCode.Redirect,
            readSourceBody: false,
            extraResponseHeaders: "Location: http://127.0.0.1:1/credential-trap\r\n");
        var writer = new IcecastWriter(Cd, NewSettings(server.Port));

        IcecastProtocolException failure =
            Assert.ThrowsException<IcecastProtocolException>(
                () => writer.UpdateMetadata("Artist", "Title"));
        Assert.AreEqual(HttpStatusCode.Redirect, failure.StatusCode);

        FakeHttpExchange exchange =
            await server.Exchange.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(
            exchange.Headers.StartsWith(
                "GET /admin/metadata?",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void SourceHeaders_RejectLineBreakInjectionBeforeConnecting()
    {
        IcecastSettingsData settings = NewSettings(1);
        settings.Name = "station\r\nInjected: true";
        var writer = new IcecastWriter(Cd, settings);

        Assert.ThrowsException<ArgumentException>(writer.Connect);
        writer.Close();
    }

    [TestMethod]
    public void UnsupportedBitrateIsRejectedBeforeConnecting()
    {
        IcecastSettingsData settings = NewSettings(1);
        settings.Bitrate = 191;
        var writer = new IcecastWriter(Cd, settings);

        Assert.ThrowsException<ArgumentOutOfRangeException>(writer.Connect);
        Assert.IsNull(writer.ProtocolResponse);
        writer.Close();
    }

    [TestMethod]
    public void ResponseApi_PreservesMemberShapeForRecompiledConsumers()
    {
        Assert.AreEqual(
            typeof(HttpWebResponse),
            typeof(IcecastWriter).GetProperty("Response")?.PropertyType);
        Assert.AreEqual(
            typeof(IcecastResponse),
            typeof(IcecastWriter).GetProperty("ProtocolResponse")?.PropertyType);
        Assert.AreEqual(
            1,
            typeof(IcecastWriter)
                .GetProperty("Response")
                ?.GetCustomAttributes(
                    typeof(ObsoleteAttribute),
                    inherit: false)
                .Length);
        Version version = typeof(IcecastWriter).Assembly.GetName().Version;
        Assert.IsNotNull(version);
        Assert.AreEqual(3, version.Major);
        Assert.AreEqual(0, version.Minor);
    }

    [TestMethod]
    public void LegacyConsumer_ContainsMetadataFailureWithoutLoggingSecrets()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(repoRoot);
        string player = File.ReadAllText(
            Path.Combine(repoRoot, "CUEPlayer", "Icecast.cs"));

        StringAssert.Contains(
            player,
            "Icecast metadata update failed: ");
        StringAssert.Contains(
            player,
            "metadataException.GetType().Name");
        Assert.IsFalse(
            player.Contains(
                "metadataException.Message",
                StringComparison.Ordinal));
        Assert.IsFalse(
            player.Contains(
                "metadataException.ToString",
                StringComparison.Ordinal));
        StringAssert.Contains(player, "if (abort)");
        StringAssert.Contains(player, "writer.Delete();");
        StringAssert.Contains(player, "writer.Close();");
    }

    [TestMethod]
    public void LegacyConsumer_ReflectsEveryStreamFailureInTransmitUi()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(repoRoot);
        string player = File.ReadAllText(
            Path.Combine(repoRoot, "CUEPlayer", "Icecast.cs"));

        Assert.AreEqual(
            4,
            player.Split(
                new[] { "SetTransmitStoppedState(" },
                StringSplitOptions.None).Length - 1,
            "the helper declaration plus write, rejected-connect, and exceptional-connect paths must remain");
        StringAssert.Contains(player, "BeginInvoke((MethodInvoker)delegate");
        StringAssert.Contains(player, "ApplyTransmitStoppedState(");
        StringAssert.Contains(player, "checkBoxTransmit.Checked = false;");
        StringAssert.Contains(
            player,
            "!Object.ReferenceEquals(_icecastWriter, expectedWriter)");
        StringAssert.Contains(
            player,
            "The Icecast stream stopped after a network or encoder write failure.");
        StringAssert.Contains(
            player,
            "Connection failed. Check the server and credential settings.");
    }

    [TestMethod]
    public void LegacySettings_HonorMp3ChoicesAndCancelUsesDraft()
    {
        string repoRoot = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
        Assert.IsNotNull(repoRoot);
        string dialog = File.ReadAllText(
            Path.Combine(repoRoot, "CUEPlayer", "IcecastSettings.cs"));
        string designer = File.ReadAllText(
            Path.Combine(repoRoot, "CUEPlayer", "IcecastSettings.Designer.cs"));
        string writer = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "CUETools.Codecs.Icecast",
                "IcecastWriter.cs"));

        StringAssert.Contains(dialog, "_data = Copy(data);");
        StringAssert.Contains(dialog, "Apply(_data, _target);");
        Assert.IsFalse(
            dialog.Contains(
                "icecastSettingsDataBindingSource.DataSource = data;",
                StringComparison.Ordinal));
        StringAssert.Contains(designer, "\"Bitrate\", true");
        StringAssert.Contains(designer, "\"JointStereo\", true");
        Assert.IsFalse(
            designer.Contains("\"MP3Options\"", StringComparison.Ordinal));
        StringAssert.Contains(writer, "EncoderMode = settings.Bitrate.ToString(");
        StringAssert.Contains(writer, "settings.JointStereo");

        string consumer = File.ReadAllText(
            Path.Combine(repoRoot, "CUEPlayer", "Icecast.cs"));
        StringAssert.Contains(
            consumer,
            "IcecastSettingsData original =");
        StringAssert.Contains(
            consumer,
            "IcecastSettings.Apply(original, _icecastSettings);");
        string credentialStore = File.ReadAllText(
            Path.Combine(
                repoRoot,
                "CUEPlayer",
                "IcecastCredentialStore.cs"));
        StringAssert.Contains(
            credentialStore,
            "originalProtectedValue");
        StringAssert.Contains(
            credentialStore,
            "IcecastCredentialsProtected =");
    }

    private static IcecastSettingsData NewSettings(int port) => new()
    {
        Server = "127.0.0.1",
        Port = port.ToString(CultureInfo.InvariantCulture),
        Mount = "/cuetools-test.mp3",
        Password = Password,
        Name = "CUETools protocol test",
        Description = "Disposable localhost test",
        Genre = "Test",
        AllowInsecureHttp = true,
    };

    private sealed class FakeHttpServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly HttpStatusCode responseStatus;
        private readonly bool readSourceBody;
        private readonly string responseReason;
        private readonly string extraResponseHeaders;

        internal FakeHttpServer(
            HttpStatusCode responseStatus,
            bool readSourceBody,
            string responseReason = null,
            string extraResponseHeaders = "")
        {
            this.responseStatus = responseStatus;
            this.readSourceBody = readSourceBody;
            this.responseReason = responseReason;
            this.extraResponseHeaders = extraResponseHeaders;
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Exchange = Task.Run(AcceptOne);
        }

        internal int Port { get; }

        internal Task<FakeHttpExchange> Exchange { get; }

        public void Dispose()
        {
            listener.Stop();
        }

        private FakeHttpExchange AcceptOne()
        {
            using TcpClient client = listener.AcceptTcpClient();
            using NetworkStream stream = client.GetStream();
            stream.ReadTimeout = 10000;
            stream.WriteTimeout = 10000;

            string headers = ReadHeaders(stream);
            string description = responseReason ?? responseStatus.ToString();
            byte[] response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 " +
                ((int)responseStatus).ToString(CultureInfo.InvariantCulture) +
                " " +
                description +
                "\r\n" +
                extraResponseHeaders +
                "Content-Length: 0\r\nConnection: close\r\n\r\n");
            stream.Write(response, 0, response.Length);
            stream.Flush();

            byte[] body = readSourceBody
                ? ReadToEnd(stream)
                : Array.Empty<byte>();
            return new FakeHttpExchange(headers, body);
        }

        private static string ReadHeaders(Stream stream)
        {
            using var bytes = new MemoryStream();
            int matched = 0;
            while (bytes.Length < 64 * 1024)
            {
                int value = stream.ReadByte();
                if (value < 0)
                    throw new EndOfStreamException();
                bytes.WriteByte((byte)value);
                matched = (matched, value) switch
                {
                    (0, '\r') => 1,
                    (1, '\n') => 2,
                    (2, '\r') => 3,
                    (3, '\n') => 4,
                    (_, '\r') => 1,
                    _ => 0,
                };
                if (matched == 4)
                    return Encoding.ASCII.GetString(bytes.ToArray());
            }
            throw new InvalidDataException("request headers exceeded test limit");
        }

        private static byte[] ReadToEnd(Stream stream)
        {
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    return body.ToArray();
                body.Write(buffer, 0, read);
            }
        }
    }

    private sealed class FakeHttpExchange
    {
        internal FakeHttpExchange(string headers, byte[] body)
        {
            Headers = headers;
            Body = body;
        }

        internal string Headers { get; }

        internal byte[] Body { get; }
    }
}
