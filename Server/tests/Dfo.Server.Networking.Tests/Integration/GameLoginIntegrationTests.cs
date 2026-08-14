using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Infrastructure;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouchSocketSessionClient = TouchSocket.Sockets.ITcpSessionClient;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class GameLoginIntegrationTests
{
    [Fact]
    public async Task Login_TraversesTouchSocketAccountRepositoryAndOnlineUserAggregate()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-login-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var channelScriptPath = Path.Combine(directory, "channel_info.etc");
            await File.WriteAllBytesAsync(channelScriptPath, [0x01]);
            var channelPort = GetAvailablePort();
            var gamePort = GetAvailablePort();
            while (gamePort == channelPort)
            {
                gamePort = GetAvailablePort();
            }

            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Networking:Enabled"] = "true",
                ["Networking:ChannelPort"] = channelPort.ToString(),
                ["Networking:GamePorts:0"] = gamePort.ToString(),
                ["Database:Path"] = Path.Combine(directory, "login.db"),
                ["Channel:ScriptPath"] = channelScriptPath,
                ["GameLogin:ServerIp"] = "192.0.2.20",
                ["GameLogin:Environment"] = "1",
                ["GameLogin:LoginChannelPort"] = "10128",
                ["GameLogin:UnknownPort"] = "17200",
            });
            var preparedAt = new DateTimeOffset(2026, 8, 13, 2, 30, 0, TimeSpan.Zero);
            builder.Services.AddSingleton<TimeProvider>(new FixedTimeProvider(preparedAt));
            new InfrastructureModule().ConfigureServices(builder.Services, builder.Configuration);
            new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
            new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);

            using var host = builder.Build();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StartAsync(timeout.Token);
            var onlineUsers = host.Services.GetRequiredService<IOnlineUserQueryService>();
            var gameService = host.Services.GetRequiredService<IGameTcpService>();

            NetworkConnectionId connectionId;
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
                await WaitUntilAsync(() => gameService.GetClients().Count() == 1, timeout.Token);
                var session = Assert.IsAssignableFrom<TouchSocketSessionClient>(gameService.GetClients().Single());
                connectionId = new NetworkConnectionId($"Game:{session.Id}");
                await WaitUntilAsync(
                    () => onlineUsers.TryGetByConnection(connectionId, out _),
                    timeout.Token);
                Assert.True(onlineUsers.TryGetByConnection(connectionId, out var anonymous));
                Assert.Equal(OnlineUserState.Connected, anonymous!.State);

                Assert.Equal(
                    BuildExpectedInitialConnectionNotice(preparedAt, channelId: 11),
                    await ReadFrameAsync(client.GetStream(), timeout.Token));

                var requestBody = EncodeDStrings("10038", "login-hash");
                var request = EncodeClientFrame(0x01, 0x0001, requestBody);
                await client.GetStream().WriteAsync(request, timeout.Token);

                var response = await ReadFrameAsync(client.GetStream(), timeout.Token);
                Assert.Equal(BuildExpectedLoginResponse(), response);

                Assert.True(onlineUsers.TryGetByConnection(connectionId, out var authenticated));
                Assert.Equal(OnlineUserState.Authenticated, authenticated!.State);
                Assert.NotNull(authenticated.AccountId);
            }

            await WaitUntilAsync(
                () => !onlineUsers.TryGetByConnection(connectionId, out _),
                timeout.Token);
            await host.StopAsync(timeout.Token);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] EncodeClientFrame(byte command, ushort type, ReadOnlySpan<byte> body)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length];
        frame[0] = command;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        body.CopyTo(frame.AsSpan(GameClientFrameRequestInfo.HeaderLength));
        return frame;
    }

    private static byte[] EncodeDStrings(params string[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            var length = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            stream.Write(length);
            stream.Write(bytes);
        }

        return stream.ToArray();
    }

    private static byte[] BuildExpectedLoginResponse()
    {
        var serverIp = "192.0.2.20"u8.ToArray();
        var body = new byte[45 + serverIp.Length];
        new byte[] { 0x01, 18, 0x00, 0x01, 0x00 }.CopyTo(body, 0);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5), 10128);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9), serverIp.Length);
        serverIp.CopyTo(body, 13);
        var offset = 13 + serverIp.Length;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset), 17200);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset + 4), 17200);
        return GameServerFrameEncoder.Encode(0x01, 0x0001, body);
    }

    private static byte[] BuildExpectedInitialConnectionNotice(
        DateTimeOffset preparedAt,
        byte channelId)
    {
        using var body = new MemoryStream();
        body.WriteByte(0x01);
        WriteDString(body, $"ch.{channelId}");
        WriteInt32(body, 0);
        WriteInt32(body, 0);
        body.WriteByte(0x01);
        body.WriteByte(channelId);
        body.WriteByte(0x00);
        WriteInt32(body, checked((int)preparedAt.ToUnixTimeSeconds()));
        WriteInt32(body, 1);
        WriteDString(body, "192.0.2.20");
        WriteInt32(body, 12311);
        WriteInt32(body, 12312);
        body.WriteByte(0x00);
        body.WriteByte(0x00);
        WriteInt32(body, 1086);
        WriteInt32(body, 1036);
        return GameServerFrameEncoder.Encode(0x00, 0x0001, body.ToArray());
    }

    private static void WriteDString(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static async Task<byte[]> ReadFrameAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[GameServerFrameEncoder.HeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var totalLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(3, 4));
        var frame = new byte[totalLength];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(header.Length), cancellationToken);
        return frame;
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
