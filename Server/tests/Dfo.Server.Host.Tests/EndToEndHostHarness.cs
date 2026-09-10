using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// Runs the packaged Host and owns the only mapping from advertised public tuples to loopback
/// listeners. Production configuration is deliberately not used as a client-side routing source.
/// </summary>
internal sealed class EndToEndHostHarness : IAsyncDisposable
{
    private const string ChannelHost = "203.0.113.10";
    private const string GameHost = "198.51.100.24";
    private const string FollowUpHost = "192.0.2.20";
    private const int AdvertisedChannelPort = 37101;
    private const int AdvertisedGamePort = 37102;
    private const int FollowUpConnectionPort = 37103;
    private const int FollowUpPortA = 37104;
    private const int FollowUpPortB = 37105;

    private readonly string directory;
    private readonly Process process;
    private readonly StringBuilder output;
    private readonly HttpClient http;
    private readonly IReadOnlyDictionary<AdvertisedEndpoint, IPEndPoint> routes;
    private readonly CancellationTokenSource timeout;

    private EndToEndHostHarness(
        string directory,
        Process process,
        StringBuilder output,
        HttpClient http,
        IReadOnlyDictionary<AdvertisedEndpoint, IPEndPoint> routes,
        CancellationTokenSource timeout)
    {
        this.directory = directory;
        this.process = process;
        this.output = output;
        this.http = http;
        this.routes = routes;
        this.timeout = timeout;
    }

    public static async Task<EndToEndHostHarness> StartAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-auth-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var ports = GetAvailablePorts(4);
        var httpPort = ports[0];
        var channelListenerPort = ports[1];
        var gameListenerPort = ports[2];
        var secondGameListenerPort = ports[3];
        var output = new StringBuilder();
        var process = StartHost(
            directory,
            httpPort,
            channelListenerPort,
            gameListenerPort,
            secondGameListenerPort,
            output);
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{httpPort}") };
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var routes = new Dictionary<AdvertisedEndpoint, IPEndPoint>
        {
            [new(ChannelHost, AdvertisedChannelPort)] =
                new(IPAddress.Loopback, channelListenerPort),
            [new(GameHost, AdvertisedGamePort)] =
                new(IPAddress.Loopback, gameListenerPort),
        };
        var harness = new EndToEndHostHarness(
            directory,
            process,
            output,
            http,
            routes,
            timeout);
        try
        {
            await harness.WaitForBootstrapAsync();
            return harness;
        }
        catch
        {
            await harness.DisposeAsync();
            throw;
        }
    }

    public async Task VerifySuccessfulAdvertisedRouteAsync()
    {
        const string loginId = "whole-route-player";
        const string password = "whole route password";
        using var registration = await this.http.PostAsJsonAsync(
            "/api/launcher/v1/register",
            new { loginId, password },
            this.timeout.Token);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var login = await this.http.PostAsJsonAsync(
            "/api/launcher/v1/login",
            new { loginId, password },
            this.timeout.Token);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginJson = JsonDocument.Parse(
            await login.Content.ReadAsStreamAsync(this.timeout.Token));
        var root = loginJson.RootElement;
        var ticket = root.GetProperty("ticket").GetString();
        Assert.NotNull(ticket);
        var advertisedChannel = ReadEndpoint(root.GetProperty("channel"));
        Assert.Equal(new AdvertisedEndpoint(ChannelHost, AdvertisedChannelPort), advertisedChannel);
        Assert.False(IPAddress.IsLoopback(IPAddress.Parse(advertisedChannel.Host)));

        using var channel = await this.ConnectThroughAdvertisedRouteAsync(advertisedChannel);
        var channelStream = channel.GetStream();
        await SendChannelRequestAsync(channelStream, 0x0B, this.timeout.Token);
        var connected = await ReadChannelFrameAsync(channelStream, this.timeout.Token);
        Assert.Equal(0x0C, connected.MessageNumber);
        var key = Encoding.ASCII.GetString(connected.Body, 4, 16).TrimEnd('\0');

        await SendChannelRequestAsync(channelStream, 0x09, this.timeout.Token);
        var script = await ReadChannelFrameAsync(channelStream, this.timeout.Token);
        Assert.Equal(0x0A, script.MessageNumber);
        Assert.Contains((byte)'[', DecryptChannelPayload(script.Body, key));

        await SendChannelRequestAsync(channelStream, 0x11, this.timeout.Token);
        var info = await ReadChannelFrameAsync(channelStream, this.timeout.Token);
        Assert.Equal(0x12, info.MessageNumber);
        var infoPlaintext = DecryptChannelPayload(info.Body, key);
        Assert.True(BinaryPrimitives.ReadInt32LittleEndian(infoPlaintext.AsSpan(2, 4)) >= 1);
        var advertisedGame = new AdvertisedEndpoint(
            ReadFixedAscii(infoPlaintext.AsSpan(34, 16)),
            BinaryPrimitives.ReadInt32LittleEndian(infoPlaintext.AsSpan(50, 4)));
        Assert.Equal(new AdvertisedEndpoint(GameHost, AdvertisedGamePort), advertisedGame);
        Assert.False(IPAddress.IsLoopback(IPAddress.Parse(advertisedGame.Host)));

        // The only value allowed to choose this connection is the tuple decoded above. The
        // listener port is private to routes and is not exposed by this method.
        using var game = await this.ConnectThroughAdvertisedRouteAsync(advertisedGame);
        var gameStream = game.GetStream();
        var initialNotice = await ReadGameFrameAsync(gameStream, this.timeout.Token);
        Assert.Equal((byte)0x00, initialNotice.Command);
        Assert.Equal((ushort)0x0001, initialNotice.Type);

        await gameStream.WriteAsync(
            EncodeGameClientFrame(0x01, 0x0001, EncodeDStrings(loginId, ticket!)),
            this.timeout.Token);
        var loginResponse = await ReadGameFrameAsync(gameStream, this.timeout.Token);
        Assert.Equal((byte)0x01, loginResponse.Command);
        Assert.Equal((ushort)0x0001, loginResponse.Type);
        Assert.Equal((byte)0x01, loginResponse.Body[0]);
        Assert.Equal(FollowUpConnectionPort,
            BinaryPrimitives.ReadInt32LittleEndian(loginResponse.Body.AsSpan(5, 4)));
        var followUpHostLength = BinaryPrimitives.ReadInt32LittleEndian(
            loginResponse.Body.AsSpan(9, 4));
        var followUpHost = Encoding.ASCII.GetString(
            loginResponse.Body, 13, followUpHostLength);
        Assert.Equal(FollowUpHost, followUpHost);
        var followUpOffset = 13 + followUpHostLength;
        Assert.Equal(FollowUpPortA,
            BinaryPrimitives.ReadInt32LittleEndian(loginResponse.Body.AsSpan(followUpOffset, 4)));
        Assert.Equal(FollowUpPortB,
            BinaryPrimitives.ReadInt32LittleEndian(loginResponse.Body.AsSpan(followUpOffset + 4, 4)));
        Assert.NotEqual(ChannelHost, GameHost);
        Assert.NotEqual(GameHost, followUpHost);
        Assert.NotEqual(ChannelHost, followUpHost);

        ushort[] orderedNotifications = [0x00AD, 0x01C7, 0x00B7, 0x00B7, 0x0289, 0x01A1];
        foreach (var expectedType in orderedNotifications)
        {
            var notification = await ReadGameFrameAsync(gameStream, this.timeout.Token);
            Assert.Equal((byte)0x00, notification.Command);
            Assert.Equal(expectedType, notification.Type);
        }

        // This request can only succeed by resolving the AccountId published by the ticket
        // transaction for this same connection. It therefore supplies consumer-facing continuity
        // evidence instead of inspecting the registry implementation directly.
        await gameStream.WriteAsync(
            EncodeGameClientFrame(0x01, 0x0008, [0xFF, 0xFF, 0x02]),
            this.timeout.Token);
        var roster = await ReadGameFrameAsync(gameStream, this.timeout.Token);
        Assert.Equal((byte)0x00, roster.Command);
        Assert.Equal((ushort)0x0002, roster.Type);
        var hidden = await ReadGameFrameAsync(gameStream, this.timeout.Token);
        Assert.Equal((byte)0x01, hidden.Command);
        Assert.Equal((ushort)0x0286, hidden.Type);
        var honor = await ReadGameFrameAsync(gameStream, this.timeout.Token);
        Assert.Equal((byte)0x00, honor.Command);
        Assert.Equal((ushort)0x0289, honor.Type);
    }

    public async ValueTask DisposeAsync()
    {
        this.timeout.Cancel();
        this.http.Dispose();
        this.timeout.Dispose();
        if (!this.process.HasExited)
        {
            this.process.Kill(entireProcessTree: true);
            await this.process.WaitForExitAsync();
        }

        this.process.Dispose();
        Directory.Delete(this.directory, recursive: true);
    }

    private async Task<TcpClient> ConnectThroughAdvertisedRouteAsync(
        AdvertisedEndpoint advertised)
    {
        Assert.True(
            this.routes.TryGetValue(advertised, out var local),
            $"No test route exists for advertised endpoint {advertised.Host}:{advertised.Port}.");
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(local!.Address, local.Port, this.timeout.Token);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task WaitForBootstrapAsync()
    {
        while (true)
        {
            if (this.process.HasExited)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Host exited with code {this.process.ExitCode}.{Environment.NewLine}{Snapshot(this.output)}");
            }

            try
            {
                using var response = await this.http.GetAsync(
                    "/api/launcher/v1/bootstrap",
                    this.timeout.Token);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Cold-start: the listener is not ready yet.
            }

            await Task.Delay(25, this.timeout.Token);
        }
    }

    private static Process StartHost(
        string directory,
        int httpPort,
        int channelListenerPort,
        int gameListenerPort,
        int secondGameListenerPort,
        StringBuilder output)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(Program).Assembly.Location);
        startInfo.Environment["LauncherAuth__Enabled"] = "true";
        startInfo.Environment["LauncherAuth__ListenUrl"] = $"http://127.0.0.1:{httpPort}";
        startInfo.Environment["LauncherAuth__PublicChannel__Host"] = ChannelHost;
        startInfo.Environment["LauncherAuth__PublicChannel__Port"] = AdvertisedChannelPort.ToString();
        startInfo.Environment["Networking__Enabled"] = "true";
        startInfo.Environment["Networking__ChannelPort"] = channelListenerPort.ToString();
        startInfo.Environment["Networking__GamePorts__0"] = gameListenerPort.ToString();
        startInfo.Environment["Networking__GamePorts__1"] = secondGameListenerPort.ToString();
        startInfo.Environment["Channel__PublicServerIp"] = GameHost;
        startInfo.Environment["Channel__Channels__0__Host"] = GameHost;
        startInfo.Environment["Channel__Channels__0__Port"] = AdvertisedGamePort.ToString();
        startInfo.Environment["Channel__Channels__1__Host"] = GameHost;
        startInfo.Environment["Channel__Channels__1__Port"] = (AdvertisedGamePort + 1).ToString();
        startInfo.Environment["Game__EndpointHost"] = FollowUpHost;
        startInfo.Environment["GameLogin__ConnectionPort"] = FollowUpConnectionPort.ToString();
        startInfo.Environment["GameLogin__EndpointPortA"] = FollowUpPortA.ToString();
        startInfo.Environment["GameLogin__EndpointPortB"] = FollowUpPortB.ToString();
        startInfo.Environment["Database__Path"] = Path.Combine(directory, "whole-route.db");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Host process could not be started.");
        process.OutputDataReceived += (_, args) => Append(output, args.Data);
        process.ErrorDataReceived += (_, args) => Append(output, args.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static AdvertisedEndpoint ReadEndpoint(JsonElement endpoint) => new(
        endpoint.GetProperty("host").GetString()!,
        endpoint.GetProperty("port").GetInt32());

    private static async Task SendChannelRequestAsync(
        NetworkStream stream,
        byte messageNumber,
        CancellationToken cancellationToken)
    {
        var frame = new byte[11];
        frame[0] = 0x7C;
        frame[1] = messageNumber;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(2, 4), (uint)frame.Length);
        frame[10] = 1;
        await stream.WriteAsync(frame, cancellationToken);
    }

    private static async Task<(byte MessageNumber, byte[] Body)> ReadChannelFrameAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[11];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var totalLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2, 4));
        var body = new byte[checked((int)totalLength - header.Length)];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return (header[1], body);
    }

    private static byte[] DecryptChannelPayload(byte[] body, string key)
    {
        using var input = new MemoryStream(body);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var encrypted = new MemoryStream();
        zlib.CopyTo(encrypted);
        using var aes = Aes.Create();
        aes.Key = Encoding.ASCII.GetBytes(key.PadRight(16, '\0'));
        return aes.DecryptEcb(encrypted.ToArray(), PaddingMode.None);
    }

    private static string ReadFixedAscii(ReadOnlySpan<byte> bytes)
    {
        var terminator = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(terminator < 0 ? bytes : bytes[..terminator]);
    }

    private static byte[] EncodeGameClientFrame(
        byte command,
        ushort type,
        ReadOnlySpan<byte> body)
    {
        const int headerLength = 14;
        var frame = new byte[headerLength + body.Length];
        frame[0] = command;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1, 2), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3, 4), (uint)frame.Length);
        body.CopyTo(frame.AsSpan(headerLength));
        return frame;
    }

    private static byte[] EncodeDStrings(params string[] values)
    {
        using var stream = new MemoryStream();
        Span<byte> length = stackalloc byte[4];
        foreach (var value in values)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            stream.Write(length);
            stream.Write(bytes);
        }

        return stream.ToArray();
    }

    private async Task<GameFrame> ReadGameFrameAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            const int headerLength = 15;
            var header = new byte[headerLength];
            await stream.ReadExactlyAsync(header, cancellationToken);
            var totalLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(3, 4));
            var body = new byte[totalLength - headerLength];
            await stream.ReadExactlyAsync(body, cancellationToken);
            return new GameFrame(
                header[0],
                BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(1, 2)),
                body);
        }
        catch (OperationCanceledException) when (this.timeout.IsCancellationRequested)
        {
            throw new Xunit.Sdk.XunitException(
                "Timed out reading a Game frame. Host output:" + Environment.NewLine
                + Snapshot(this.output));
        }
    }

    private static IReadOnlyList<int> GetAvailablePorts(int count)
    {
        var ports = new List<int>(count);
        while (ports.Count < count)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (!ports.Contains(port))
            {
                ports.Add(port);
            }
        }

        return ports;
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null) return;
        lock (output) output.AppendLine(line);
    }

    private static string Snapshot(StringBuilder output)
    {
        lock (output) return output.ToString();
    }

    private readonly record struct AdvertisedEndpoint(string Host, int Port);
    private readonly record struct GameFrame(byte Command, ushort Type, byte[] Body);
}
