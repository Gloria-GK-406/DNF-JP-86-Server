using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Login;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouchSocketSessionClient = TouchSocket.Sockets.ITcpSessionClient;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class GameLoginIntegrationTests
{
    // Host startup, schema migration and password hashing share this guard on hosted CI.
    // Authentication expiry is driven by the fixture clock, not by this wall-clock budget.
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);
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

            var preparedAt = new DateTimeOffset(2026, 8, 13, 2, 30, 0, TimeSpan.Zero);
            var databasePath = Path.Combine(directory, "login.db");
            using var host = BuildHost(
                channelPort,
                gamePort,
                databasePath,
                channelScriptPath,
                preparedAt);
            using var timeout = new CancellationTokenSource(TestTimeout);
            await host.StartAsync(timeout.Token);
            var onlineUsers = host.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            var gameService = host.Services.GetRequiredService<IGameTcpService>();
            var commands = host.Services.GetRequiredService<ICommandBus>();
            var registration = await commands.SendAsync(
                new RegisterAccountCommand("10038", "integration password"),
                timeout.Token);
            Assert.Equal(AuthenticationStatus.Success, registration.Status);
            var issued = await commands.SendAsync(
                new AuthenticateAndIssueTicketCommand("10038", "integration password"),
                timeout.Token);
            Assert.Equal(AuthenticationStatus.Success, issued.Status);
            Assert.NotNull(issued.Ticket);

            byte[] savedMain = Enumerable.Range(0,150).Select(i => (byte)i).ToArray();
            byte[] savedKeys = [65,9,49,10,0];
            byte[] savedChat = Enumerable.Repeat((byte)123,300).ToArray();
            static byte[] Blob(byte[] value)
            {
                var body = new byte[value.Length+4];
                BinaryPrimitives.WriteInt32LittleEndian(body,value.Length); value.CopyTo(body,4); return body;
            }
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

                var requestBody = EncodeDStrings(issued.MId!, issued.Ticket);
                var request = EncodeClientFrame(0x01, 0x0001, requestBody);
                await client.GetStream().WriteAsync(request, timeout.Token);

                var response = await ReadFrameAsync(client.GetStream(), timeout.Token);
                Assert.Equal(BuildExpectedLoginResponse(), response);

                Assert.True(onlineUsers.TryGetByConnection(connectionId, out var authenticated));
                Assert.Equal(OnlineUserState.Authenticated, authenticated!.State);
                Assert.NotNull(authenticated.AccountId);

                ushort[] accountStateTypes = [0x00AD, 0x01C7, 0x00B7, 0x00B7, 0x0289, 0x01A1];
                foreach (var expectedType in accountStateTypes)
                {
                    var state = await ReadFrameAsync(client.GetStream(), timeout.Token);
                    Assert.Equal(0, state[0]);
                    Assert.Equal(expectedType, BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(1)));
                }

                // Opaque settings traverse actual TCP framing, decoding and command transactions.
                await client.GetStream().WriteAsync(EncodeClientFrame(1,0x00C5,Blob(savedMain)),timeout.Token);
                await client.GetStream().WriteAsync(EncodeClientFrame(1,0x00C6,Blob(savedKeys)),timeout.Token);
                await client.GetStream().WriteAsync(EncodeClientFrame(1,0x0170,[0,..Blob(savedChat)]),timeout.Token);
                await client.GetStream().WriteAsync(EncodeClientFrame(1,0x0170,[1,..Blob(savedChat)]),timeout.Token);
                while (await ScalarAsync(databasePath,
                    "SELECT COUNT(*) FROM account_settings WHERE length(main_game_option)=150 AND length(quickchat_bank0)=300 AND length(quickchat_bank1)=300;") != 1
                    || await ScalarAsync(databasePath,"SELECT COUNT(*) FROM account_hotkey_options WHERE key_type=0;") != 1)
                    await Task.Delay(10,timeout.Token);

                // Real decoder -> command transaction -> seen-notice table. There is no ACK.
                await client.GetStream().WriteAsync(EncodeClientFrame(1, 0x01DE, [8,0,0,0]), timeout.Token);
                await client.GetStream().WriteAsync(EncodeClientFrame(1, 0x01DE, [8,0,0,0]), timeout.Token);
                while (await ScalarAsync(databasePath, "SELECT COUNT(*) FROM notice_states WHERE owner_type=0 AND notice_group=0 AND notice_id=8;") != 1)
                    await Task.Delay(10, timeout.Token);

                // Returning from the roster itself is idempotent and preserves authentication.
                await client.GetStream().WriteAsync(EncodeClientFrame(1, 7, []), timeout.Token);
                var returned = await ReadFrameAsync(client.GetStream(), timeout.Token);
                Assert.Equal(1, returned[0]);
                Assert.Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(returned.AsSpan(1)));
                Assert.Equal(new byte[] { 1 }, returned[15..]);
                var roster = await ReadFrameAsync(client.GetStream(), timeout.Token);
                Assert.Equal(0, roster[0]);
                Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(roster.AsSpan(1)));
                Assert.Equal(0, roster[7]);
                Assert.Equal(2, roster[15]);
                var honor = await ReadFrameAsync(client.GetStream(), timeout.Token);
                Assert.Equal(0x0289, BinaryPrimitives.ReadUInt16LittleEndian(honor.AsSpan(1)));
                Assert.True(onlineUsers.TryGetByConnection(connectionId, out var stillLoggedIn));
                Assert.Equal(authenticated.AccountId, stillLoggedIn!.AccountId);
                Assert.Equal(OnlineUserState.Authenticated, stillLoggedIn.State);

                // Shop slices reach the real dispatcher and return framed refusals
                // before character selection. SQLite settlement is covered in NpcShopPurchaseTests.
                await client.GetStream().WriteAsync(EncodeClientFrame(1, 0x0015,
                    Convert.FromHexString("4CDC341D010000000F00000002000000")), timeout.Token);
                var refusedBuy = await ReadFrameAsync(client.GetStream(), timeout.Token);
                Assert.Equal(GameServerFrameEncoder.Encode(1, 0x0015, new byte[] { 0, 0 }), refusedBuy);
                await client.GetStream().WriteAsync(EncodeClientFrame(1, 0x02CC,
                    new byte[] { 2, 0, 0, 0 }), timeout.Token);
                var refusedCount = await ReadFrameAsync(client.GetStream(), timeout.Token);
                Assert.Equal(GameServerFrameEncoder.Encode(1, 0x02CC, new byte[] { 0, 0 }), refusedCount);
                await client.GetStream().WriteAsync(EncodeClientFrame(1, 0x0016,
                    Convert.FromHexString("0009000100020000000A000000")), timeout.Token);
                var refusedSale = await ReadFrameAsync(client.GetStream(), timeout.Token);
                Assert.Equal(GameServerFrameEncoder.Encode(1, 0x0016, new byte[] { 0, 1 }), refusedSale);
            }

            await WaitUntilAsync(
                () => !onlineUsers.TryGetByConnection(connectionId, out _),
                timeout.Token);
            // A new TCP login receives the saved set, rather than the previous empty baseline.
            var reissued = await commands.SendAsync(
                new AuthenticateAndIssueTicketCommand("10038", "integration password"), timeout.Token);
            using (var reconnected = new TcpClient())
            {
                await reconnected.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
                _ = await ReadFrameAsync(reconnected.GetStream(), timeout.Token);
                await reconnected.GetStream().WriteAsync(EncodeClientFrame(1, 1,
                    EncodeDStrings(reissued.MId!, reissued.Ticket!)), timeout.Token);
                _ = await ReadFrameAsync(reconnected.GetStream(), timeout.Token);
                foreach (var expectedType in new ushort[] { 0x00AD,0x01C7,0x00B7,0x00B7,0x0289,0x01A1 })
                {
                    var frame = await ReadFrameAsync(reconnected.GetStream(), timeout.Token);
                    Assert.Equal(expectedType, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
                    if (expectedType == 0x01A1) Assert.Equal(new byte[] { 1,8 }, frame[15..]);
                    if (expectedType == 0x01C7) Assert.Equal(new byte[] { 0 }.Concat(Blob(savedKeys)).ToArray(),frame[15..]);
                    if (expectedType == 0x00AD) Assert.Equal(Blob(savedMain).Concat(Blob(savedChat)).Concat(Blob(savedChat)).ToArray(),frame[15..]);
                }
            }
            Assert.Equal(1L, await ScalarAsync(databasePath, "SELECT COUNT(*) FROM accounts;"));
            Assert.Equal(
                0L,
                await ScalarAsync(
                    databasePath,
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='login_tickets';"));
            await host.StopAsync(timeout.Token);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidTicketClosesWithoutSuccessAndTheOriginalTicketCanStillBeRedeemedOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-login-reject-{Guid.NewGuid():N}");
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

            var preparedAt = new DateTimeOffset(2026, 8, 13, 2, 30, 0, TimeSpan.Zero);
            var databasePath = Path.Combine(directory, "login.db");
            using var host = BuildHost(
                channelPort,
                gamePort,
                databasePath,
                channelScriptPath,
                preparedAt);
            using var timeout = new CancellationTokenSource(TestTimeout);
            await host.StartAsync(timeout.Token);
            var onlineUsers = host.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            var gameService = host.Services.GetRequiredService<IGameTcpService>();
            var commands = host.Services.GetRequiredService<ICommandBus>();
            _ = await commands.SendAsync(
                new RegisterAccountCommand("player", "integration password"),
                timeout.Token);
            // Repeat immediately: a recycled receive core must not retain the previous
            // connection's in-flight SocketAsyncEventArgs on a two-core Linux runner.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var issued = await commands.SendAsync(
                    new AuthenticateAndIssueTicketCommand("player", "integration password"),
                    timeout.Token);
                Assert.NotNull(issued.Ticket);
                var invalidTicket = (issued.Ticket![0] == 'A' ? "B" : "A") + issued.Ticket[1..];

                NetworkConnectionId rejectedConnectionId;
                using (var rejected = new TcpClient())
                {
                    await rejected.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
                    rejectedConnectionId = await FindOnlyConnectionAsync(
                        gameService,
                        onlineUsers,
                        timeout.Token);
                    _ = await ReadFrameAsync(rejected.GetStream(), timeout.Token);
                    await rejected.GetStream().WriteAsync(
                        EncodeClientFrame(0x01, 0x0001, EncodeDStrings(issued.MId!, invalidTicket)),
                        timeout.Token);

                    await Assert.ThrowsAsync<EndOfStreamException>(async () =>
                        await ReadFrameAsync(rejected.GetStream(), timeout.Token));
                }

                await WaitUntilAsync(
                    () => !onlineUsers.TryGetByConnection(rejectedConnectionId, out _),
                    timeout.Token);
                await WaitUntilAsync(() => gameService.GetClients().Count() == 0, timeout.Token);
                NetworkConnectionId acceptedConnectionId;
                using (var accepted = new TcpClient())
                {
                    await accepted.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
                    acceptedConnectionId = await FindOnlyConnectionAsync(
                        gameService,
                        onlineUsers,
                        timeout.Token);
                    _ = await ReadFrameAsync(accepted.GetStream(), timeout.Token);
                    await accepted.GetStream().WriteAsync(
                        EncodeClientFrame(0x01, 0x0001, EncodeDStrings(issued.MId!, issued.Ticket)),
                        timeout.Token);

                    Assert.Equal(
                        BuildExpectedLoginResponse(),
                        await ReadFrameAsync(accepted.GetStream(), timeout.Token));
                    for (var index = 0; index < 6; index++)
                    {
                        _ = await ReadFrameAsync(accepted.GetStream(), timeout.Token);
                    }

                    Assert.True(onlineUsers.TryGetByConnection(acceptedConnectionId, out var authenticated));
                    Assert.Equal(OnlineUserState.Authenticated, authenticated!.State);
                }

                await WaitUntilAsync(
                    () => !onlineUsers.TryGetByConnection(acceptedConnectionId, out _),
                    timeout.Token);
            }
            Assert.Equal(1L, await ScalarAsync(databasePath, "SELECT COUNT(*) FROM accounts;"));
            await host.StopAsync(timeout.Token);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoginDeliveryFailureAfterCommitCleansIdentityAndAllowsReconnect()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-login-delivery-{Guid.NewGuid():N}");
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

            var databasePath = Path.Combine(directory, "login.db");
            using var host = BuildHost(
                channelPort,
                gamePort,
                databasePath,
                channelScriptPath,
                new DateTimeOffset(2026, 8, 13, 2, 30, 0, TimeSpan.Zero),
                failFirstLoginDelivery: true);
            using var timeout = new CancellationTokenSource(TestTimeout);
            await host.StartAsync(timeout.Token);
            var onlineUsers = host.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            var gameService = host.Services.GetRequiredService<IGameTcpService>();
            var commands = host.Services.GetRequiredService<ICommandBus>();
            _ = await commands.SendAsync(
                new RegisterAccountCommand("delivery-player", "integration password"),
                timeout.Token);
            var issued = await commands.SendAsync(
                new AuthenticateAndIssueTicketCommand(
                    "delivery-player", "integration password"),
                timeout.Token);
            Assert.NotNull(issued.Ticket);

            NetworkConnectionId failedConnectionId;
            using (var failed = new TcpClient())
            {
                await failed.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
                failedConnectionId = await FindOnlyConnectionAsync(
                    gameService,
                    onlineUsers,
                    timeout.Token);
                _ = await ReadFrameAsync(failed.GetStream(), timeout.Token);
                await failed.GetStream().WriteAsync(
                    EncodeClientFrame(
                        0x01,
                        0x0001,
                        EncodeDStrings(issued.MId!, issued.Ticket)),
                    timeout.Token);

                await Assert.ThrowsAsync<EndOfStreamException>(async () =>
                    await ReadFrameAsync(failed.GetStream(), timeout.Token));
            }

            await WaitUntilAsync(
                () => !onlineUsers.TryGetByConnection(failedConnectionId, out _),
                timeout.Token);
            await WaitUntilAsync(() => gameService.GetClients().Count() == 0, timeout.Token);
            NetworkConnectionId replayConnectionId;
            using (var replay = new TcpClient())
            {
                await replay.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
                replayConnectionId = await FindOnlyConnectionAsync(
                    gameService,
                    onlineUsers,
                    timeout.Token);
                _ = await ReadFrameAsync(replay.GetStream(), timeout.Token);
                await replay.GetStream().WriteAsync(
                    EncodeClientFrame(
                        0x01,
                        0x0001,
                        EncodeDStrings(issued.MId!, issued.Ticket)),
                    timeout.Token);

                var accepted = await ReadFrameAsync(replay.GetStream(), timeout.Token);
                Assert.Equal(BuildExpectedLoginResponse(), accepted);
            }

            await WaitUntilAsync(
                () => !onlineUsers.TryGetByConnection(replayConnectionId, out _),
                timeout.Token);
            await host.StopAsync(timeout.Token);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public Task CommitPathFailureAfterRealTicketConsumptionRollsBackAndAllowsRetry() =>
        VerifyPrepareFaultRollsBackTicketAsync(PrepareFaultKind.Failure);

    [Fact]
    public Task PreCommitCancellationAfterRealTicketConsumptionRollsBackAndAllowsRetry() =>
        VerifyPrepareFaultRollsBackTicketAsync(PrepareFaultKind.Cancellation);

    private static async Task VerifyPrepareFaultRollsBackTicketAsync(PrepareFaultKind kind)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"dfo-login-precommit-{kind}-{Guid.NewGuid():N}");
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

            var databasePath = Path.Combine(directory, "login.db");
            var fault = new TransactionPrepareFaultProbe(kind);
            using var host = BuildHost(
                channelPort,
                gamePort,
                databasePath,
                channelScriptPath,
                new DateTimeOffset(2026, 8, 13, 2, 30, 0, TimeSpan.Zero),
                preCommitFault: fault);
            using var timeout = new CancellationTokenSource(TestTimeout);
            await host.StartAsync(timeout.Token);
            var onlineUsers = host.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            var gameService = host.Services.GetRequiredService<IGameTcpService>();
            var commands = host.Services.GetRequiredService<ICommandBus>();
            var loginId = $"precommit-{kind}";
            _ = await commands.SendAsync(
                new RegisterAccountCommand(loginId, "integration password"),
                timeout.Token);
            var issued = await commands.SendAsync(
                new AuthenticateAndIssueTicketCommand(loginId, "integration password"),
                timeout.Token);
            Assert.NotNull(issued.Ticket);

            NetworkConnectionId failedConnectionId;
            using (var failed = new TcpClient())
            {
                await failed.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
                failedConnectionId = await FindOnlyConnectionAsync(
                    gameService,
                    onlineUsers,
                    timeout.Token);
                _ = await ReadFrameAsync(failed.GetStream(), timeout.Token);
                Assert.True(onlineUsers.TryGetByConnection(failedConnectionId, out var anonymous));
                Assert.Equal(OnlineUserState.Connected, anonymous!.State);
                Assert.Null(anonymous.AccountId);

                await failed.GetStream().WriteAsync(
                    EncodeClientFrame(
                        0x01,
                        0x0001,
                        EncodeDStrings(issued.MId!, issued.Ticket)),
                    timeout.Token);

                await Assert.ThrowsAsync<EndOfStreamException>(async () =>
                    await ReadFrameAsync(failed.GetStream(), timeout.Token));
            }

            Assert.True(fault.WasTriggered);
            Assert.Equal(OnlineUserState.Connected, fault.ObservedState);
            Assert.Null(fault.ObservedAccountId);
            await WaitUntilAsync(
                () => !onlineUsers.TryGetByConnection(failedConnectionId, out _),
                timeout.Token);
            await WaitUntilAsync(() => gameService.GetClients().Count() == 0, timeout.Token);
            NetworkConnectionId acceptedConnectionId;
            using (var accepted = new TcpClient())
            {
                await accepted.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
                acceptedConnectionId = await FindOnlyConnectionAsync(
                    gameService,
                    onlineUsers,
                    timeout.Token);
                _ = await ReadFrameAsync(accepted.GetStream(), timeout.Token);
                await accepted.GetStream().WriteAsync(
                    EncodeClientFrame(
                        0x01,
                        0x0001,
                        EncodeDStrings(issued.MId!, issued.Ticket)),
                    timeout.Token);

                Assert.Equal(
                    BuildExpectedLoginResponse(),
                    await ReadFrameAsync(accepted.GetStream(), timeout.Token));
                ushort[] accountStateTypes = [0x00AD, 0x01C7, 0x00B7, 0x00B7, 0x0289, 0x01A1];
                foreach (var expectedType in accountStateTypes)
                {
                    var state = await ReadFrameAsync(accepted.GetStream(), timeout.Token);
                    Assert.Equal(0, state[0]);
                    Assert.Equal(
                        expectedType,
                        BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(1)));
                }

                Assert.True(
                    onlineUsers.TryGetByConnection(acceptedConnectionId, out var authenticated));
                Assert.Equal(OnlineUserState.Authenticated, authenticated!.State);
                Assert.NotNull(authenticated.AccountId);
            }

            await WaitUntilAsync(
                () => !onlineUsers.TryGetByConnection(acceptedConnectionId, out _),
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

    /// <summary>
    /// The login answer this test expects to arrive, rebuilt here rather than transcribed.
    /// </summary>
    /// <remarks>
    /// Like the notice above, a second implementation of the same layout cannot prove the layout.
    /// It proves the answer reaches a real socket in one frame after the request. The layout is
    /// held by the codec test, whose expectation is transcribed from a session the client
    /// accepted.
    /// </remarks>
    private static byte[] BuildExpectedLoginResponse()
    {
        var serverIp = "192.0.2.20"u8.ToArray();
        var body = new byte[45 + serverIp.Length];
        new byte[] { 0x01, 20, 0x00, 0x01, 0x00 }.CopyTo(body, 0);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5), 10128);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9), serverIp.Length);
        serverIp.CopyTo(body, 13);
        var offset = 13 + serverIp.Length;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset), 17200);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset + 4), 17201);
        return GameServerFrameEncoder.Encode(0x01, 0x0001, body);
    }

    /// <summary>
    /// The notice this test expects to arrive, rebuilt here rather than transcribed.
    /// </summary>
    /// <remarks>
    /// Being a second implementation of the same layout, this cannot prove the layout: change the
    /// codec and this in the same way and both agree on something wrong. It proves delivery -- that
    /// a real socket receives the whole notice, in one frame, before anything else. The layout
    /// itself is held by the codec test, whose expectation is transcribed from a session the
    /// client accepted.
    /// </remarks>
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
        WriteInt32(body, 0);
        body.WriteByte((byte)'0');
        body.WriteByte((byte)'0');
        WriteInt32(body, 1271);
        WriteInt32(body, 1218);
        WriteInt32(body, 0);
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

    private static async Task<NetworkConnectionId> FindOnlyConnectionAsync(
        IGameTcpService gameService,
        IOnlineUserQueryDomainService onlineUsers,
        CancellationToken cancellationToken)
    {
        await WaitUntilAsync(() => gameService.GetClients().Count() == 1, cancellationToken);
        var session = Assert.IsAssignableFrom<TouchSocketSessionClient>(
            gameService.GetClients().Single());
        var connectionId = new NetworkConnectionId($"Game:{session.Id}");
        await WaitUntilAsync(
            () => onlineUsers.TryGetByConnection(connectionId, out _),
            cancellationToken);
        return connectionId;
    }

    private static IHost BuildHost(
        int channelPort,
        int gamePort,
        string databasePath,
        string channelScriptPath,
        DateTimeOffset preparedAt,
        bool failFirstLoginDelivery = false,
        TransactionPrepareFaultProbe? preCommitFault = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Networking:Enabled"] = "true",
            ["Networking:ChannelPort"] = channelPort.ToString(),
            ["Networking:GamePorts:0"] = gamePort.ToString(),
            ["Database:Path"] = databasePath,
            ["Channel:ScriptPath"] = channelScriptPath,
            ["Game:EndpointHost"] = "192.0.2.20",
            ["GameLogin:Environment"] = "1",
            ["GameLogin:ConnectionPort"] = "10128",
            ["GameLogin:EndpointPortA"] = "17200",
            ["GameLogin:EndpointPortB"] = "17201",
        });
        builder.Services.AddSingleton<TimeProvider>(new FixedTimeProvider(preparedAt));
        new InfrastructureModule().ConfigureServices(builder.Services, builder.Configuration);
        new PersistenceModule().ConfigureServices(builder.Services, builder.Configuration);
        new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
        new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
        new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);
        if (failFirstLoginDelivery)
        {
            DecorateSingleton<IGameMessageSender>(
                builder.Services,
                (_, sender) => new FailFirstLoginDelivery(sender));
        }
        if (preCommitFault is { } fault)
        {
            DecorateSingleton<IGameLoginAuthenticationDomainService>(
                builder.Services,
                (provider, authentication) => new PrepareFaultingAuthentication(
                    authentication,
                    provider.GetRequiredService<IDomainTransactionAccessor>(),
                    provider.GetRequiredService<IOnlineUserQueryDomainService>(),
                    fault));
        }
        return builder.Build();
    }

    private static void DecorateSingleton<TService>(
        IServiceCollection services,
        Func<IServiceProvider, TService, TService> decorate)
        where TService : class
    {
        var descriptor = services.Last(service => service.ServiceType == typeof(TService));
        services.Remove(descriptor);
        services.AddSingleton<TService>(provider => decorate(
            provider,
            descriptor.ImplementationInstance as TService
                ?? (descriptor.ImplementationFactory is not null
                    ? (TService)descriptor.ImplementationFactory(provider)
                    : (TService)ActivatorUtilities.CreateInstance(
                        provider,
                        descriptor.ImplementationType!))));
    }

    private static async Task<long> ScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
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

    private sealed class FailFirstLoginDelivery(IGameMessageSender inner) : IGameMessageSender
    {
        private int failNext = 1;

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken)
        {
            if (message is GameLoginResponse
                && Interlocked.Exchange(ref this.failNext, 0) == 1)
            {
                throw new IOException("test-injected login delivery failure");
            }

            return inner.SendCmdAsync(connectionId, message, cancellationToken);
        }

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            inner.SendNotiAsync(connectionId, message, cancellationToken);

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken) =>
            inner.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }

    private enum PrepareFaultKind
    {
        Failure,
        Cancellation,
    }

    private sealed class TransactionPrepareFaultProbe(PrepareFaultKind kind)
    {
        private int armed = 1;

        public PrepareFaultKind Kind { get; } = kind;

        public bool WasTriggered { get; set; }

        public OnlineUserState? ObservedState { get; set; }

        public AccountId? ObservedAccountId { get; set; }

        public bool TryClaim() => Interlocked.Exchange(ref this.armed, 0) == 1;
    }

    private sealed class PrepareFaultingAuthentication(
        IGameLoginAuthenticationDomainService inner,
        IDomainTransactionAccessor transactions,
        IOnlineUserQueryDomainService onlineUsers,
        TransactionPrepareFaultProbe fault) : IGameLoginAuthenticationDomainService
    {
        public async ValueTask<AccountId?> AuthenticateSessionAsync(
            OnlineUserId onlineUserId,
            string loginId,
            string ticket,
            CancellationToken cancellationToken)
        {
            var accountId = await inner.AuthenticateSessionAsync(
                onlineUserId,
                loginId,
                ticket,
                cancellationToken).ConfigureAwait(false);
            if (accountId is not null && fault.TryClaim())
            {
                var transaction = transactions.Current
                    ?? throw new InvalidOperationException("Expected an ambient transaction.");
                transaction.Enlist(
                    new object(),
                    () => new PrepareFaultParticipant(onlineUserId, onlineUsers, fault));
            }

            return accountId;
        }
    }

    private sealed class PrepareFaultParticipant(
        OnlineUserId onlineUserId,
        IOnlineUserQueryDomainService onlineUsers,
        TransactionPrepareFaultProbe fault) : ITransactionParticipant
    {
        public bool CommitCanFail => false;

        public ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            var snapshot = onlineUsers.GetRequired(onlineUserId);
            fault.ObservedState = snapshot.State;
            fault.ObservedAccountId = snapshot.AccountId;
            fault.WasTriggered = true;
            if (fault.Kind == PrepareFaultKind.Cancellation)
            {
                throw new OperationCanceledException(
                    "test-injected precommit cancellation",
                    cancellationToken);
            }

            throw new InvalidOperationException("test-injected commit-path failure");
        }

        public ValueTask CommitAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask RollbackAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
