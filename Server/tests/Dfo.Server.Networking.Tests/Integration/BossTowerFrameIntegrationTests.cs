using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Protocol.Game.Login;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouchSocketSessionClient = TouchSocket.Sockets.ITcpSessionClient;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class BossTowerFrameIntegrationTests
{
    [Fact]
    public async Task Raw_move_loading_kill_and_result_hover_use_registered_boss_frames()
    {
        var channelPort = AvailablePort(); var gamePort = AvailablePort();
        while (gamePort == channelPort) gamePort = AvailablePort();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Networking:Enabled"] = "true", ["Networking:ChannelPort"] = channelPort.ToString(),
            ["Networking:GamePorts:0"] = gamePort.ToString(), ["Game:EndpointHost"] = "127.0.0.1",
        });
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IServerConfigService>(new ServerConfigService(builder.Configuration));
        builder.Services.AddSingleton<IChannelCatalog>(StaticChannelCatalog.ForGamePort(gamePort));
        builder.Services.AddSingleton<IChannelScriptSource>(new NoChannelScript());
        builder.Services.AddSingleton(ChannelEntryOptions.Legacy);
        new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
        new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
        new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);
        var tower = new Tower();
        builder.Services.AddSingleton<IBossTowerDomainService>(tower);
        using var host = TestInfrastructure.Build(builder);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await host.StartAsync(timeout.Token);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
            var stream = client.GetStream();
            _ = await ReadFrame(stream, timeout.Token);
            var game = host.Services.GetRequiredService<IGameTcpService>();
            while (!game.GetClients().Any()) await Task.Delay(10, timeout.Token);
            var connection = new NetworkConnectionId($"Game:{Assert.IsAssignableFrom<TouchSocketSessionClient>(game.GetClients().Single()).Id}");
            var online = host.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            while (!online.TryGetByConnection(connection, out _)) await Task.Delay(10, timeout.Token);
            var commands = host.Services.GetRequiredService<ICommandBus>();
            await commands.SendAsync(new RegisterAccountCommand("bosswire", "test password"), timeout.Token);
            var issued = await commands.SendAsync(new AuthenticateAndIssueTicketCommand("bosswire", "test password"), timeout.Token);
            Assert.True(online.TryGetByConnection(connection, out var anonymous));
            var redeemed = await commands.SendAsync(new RedeemGameLoginCommand(anonymous!.OnlineUserId, issued.MId!, issued.Ticket!), timeout.Token);
            Assert.True(redeemed.Succeeded);
            var authenticated = online.GetRequired(anonymous.OnlineUserId);
            var session = SelectedSessionIdentity.Create(redeemed.AccountId!.Value, new CharacterId(2), connection, authenticated.Generation, gamePort);
            host.Services.GetRequiredService<ISelectedSessionDomainService>().Select(session);
            host.Services.GetRequiredService<IActiveCharacterDomainService>().Publish(session);
            tower.Seed(session);

            // A21 tower next-stage marker: success is the dedicated map push, with no ordinary CMD ACK.
            var move = new byte[10]; move[0] = byte.MaxValue; move[1] = byte.MaxValue;
            await stream.WriteAsync(ClientFrame(0x002D, move, 1), timeout.Token);
            var map = await ReadFrame(stream, timeout.Token);
            Assert.Equal(0, map[0]); Assert.Equal(0x0334, BinaryPrimitives.ReadUInt16LittleEndian(map.AsSpan(1)));
            Assert.Equal(26, map.Length - GameServerFrameEncoder.HeaderLength);
            Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(map.AsSpan(GameServerFrameEncoder.HeaderLength)));
            await stream.WriteAsync(ClientFrame(0x0025, new byte[8], 2), timeout.Token);
            var loaded = await ReadFrame(stream, timeout.Token);
            Assert.Equal(0x001E, BinaryPrimitives.ReadUInt16LittleEndian(loaded.AsSpan(1)));
            Assert.Equal(TowerRoomPhase.Fighting, tower.Current!.Room.Phase);

            var death = new byte[62]; death[0] = 33; death[4] = 1;
            await stream.WriteAsync(ClientFrame(0x0027, death, 3), timeout.Token);
            var killed = await ReadFrame(stream, timeout.Token);
            Assert.Equal(0x0026, BinaryPrimitives.ReadUInt16LittleEndian(killed.AsSpan(1)));
            var reward = await ReadFrame(stream, timeout.Token);
            Assert.Equal(0x0335, BinaryPrimitives.ReadUInt16LittleEndian(reward.AsSpan(1)));
            Assert.Equal("7B00000001FFFFFFFF0000000000000000000000", Convert.ToHexString(reward.AsSpan(GameServerFrameEncoder.HeaderLength)));
            var retry = await ReadFrame(stream, timeout.Token);
            Assert.Equal(0x0336, BinaryPrimitives.ReadUInt16LittleEndian(retry.AsSpan(1)));
            Assert.Equal(0, retry[GameServerFrameEncoder.HeaderLength]);
            await stream.WriteAsync(ClientFrame(0x0048, [2, 0], 4), timeout.Token);
            var hover = await ReadFrame(stream, timeout.Token);
            Assert.Equal(0x0048, BinaryPrimitives.ReadUInt16LittleEndian(hover.AsSpan(1)));
            Assert.Equal("010200", Convert.ToHexString(hover.AsSpan(GameServerFrameEncoder.HeaderLength)));
            Assert.Equal(0, tower.Choices);
            Assert.Equal(1, tower.Kills);
        }
        finally { await host.StopAsync(CancellationToken.None); }
    }

    private static int AvailablePort()
    { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    private static byte[] ClientFrame(ushort type, byte[] body, ushort sequence)
    {
        var result = new byte[14 + body.Length]; result[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(3), (uint)result.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), sequence); body.CopyTo(result, 14); return result;
    }
    private static async Task<byte[]> ReadFrame(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[GameServerFrameEncoder.HeaderLength]; await stream.ReadExactlyAsync(header, ct);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(3)); var frame = new byte[length];
        header.CopyTo(frame, 0); await stream.ReadExactlyAsync(frame.AsMemory(header.Length), ct); return frame;
    }
    private sealed class Tower : IBossTowerDomainService
    {
        public BossTowerSnapshot? Current { get; private set; }
        public int Choices { get; private set; }
        public int Kills { get; private set; }
        public void Seed(SelectedSessionIdentity session) => Current = new(Guid.NewGuid(), 3505, [new(session, 0, true, false)],
            new(1, 25044, 7, 1, TowerRoomPhase.Cleared, [new(33, new(0, 608, 70, 3, true, new(0, 0, 0)), false)], [],
                ImmutableHashSet<CharacterId>.Empty), [1], null, DateTimeOffset.UtcNow);
        public bool Supports(int id) => id == 3505;
        public bool IsInTower(CharacterId id) => Current?.Members.Any(value => value.Session.CharacterId == id) == true;
        public BossTowerSnapshot? Find(SelectedSessionIdentity session) => Current?.Members.Any(value => value.Session == session) == true ? Current : null;
        public ValueTask<BossTowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members, SelectedSessionIdentity leader, uint seed, CancellationToken ct, byte requestMode = 0, byte difficulty = 0, byte practiceMode = 0) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> NextLevelAsync(BossTowerActionContext context, byte x, byte y, CancellationToken ct)
        { Current = Current! with { Room = Current.Room with { Level = 2, MapId = 25045, Version = 2, Phase = TowerRoomPhase.Loading } }; return Result(); }
        public ValueTask<BossTowerActionResult> LoadAsync(BossTowerActionContext context, CancellationToken ct)
        { Current = Current! with { Room = Current.Room with { Phase = TowerRoomPhase.Ready } }; return Result(); }
        public ValueTask<BossTowerActionResult> StartAsync(BossTowerActionContext context, CancellationToken ct)
        { Current = Current! with { Room = Current.Room with { Phase = TowerRoomPhase.Fighting } }; return Result(); }
        public ValueTask<BossTowerActionResult> KillAsync(BossTowerActionContext context, ushort uid, CancellationToken ct)
        {
            Kills++; var member = Current!.Members[0];
            Current = Current with { Completion = new(TowerEndReason.Completed, 8, 8, true, DateTimeOffset.UtcNow,
                new([new(member.Session.CharacterId, 123, 0, []) { Session = member.Session, Candidates = [new(-1, 0)] }])) };
            return ValueTask.FromResult(new BossTowerActionResult(true, Current) { SettlementCreated = true });
        }
        public ValueTask<BossTowerActionResult> LeaveAsync(BossTowerActionContext context, bool disconnected, CancellationToken ct)
        { var previous = Current; Current = null; return ValueTask.FromResult(new BossTowerActionResult(true, previous! with { Members = [] })); }
        public ValueTask<BossTowerActionResult> DieAsync(BossTowerActionContext context, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<BossTowerPickupResult> PickupAsync(BossTowerActionContext context, ushort uid, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> SynchronizeLeaderAsync(BossTowerActionContext context, SelectedSessionIdentity leader, CancellationToken ct) => Result();
        public ValueTask<bool> CanRetryAsync(BossTowerActionContext context, CancellationToken ct) => ValueTask.FromResult(false);
        public ValueTask<BossTowerActionResult> SelectResultAsync(BossTowerActionContext context, byte option, CancellationToken ct) { Choices++; return Result(); }
        public ValueTask<BossTowerActionResult> ReleaseForSelectionAsync(BossTowerActionContext context, int dungeonId, CancellationToken ct) => throw new NotSupportedException();
        public IReadOnlyList<BossTowerActionContext> FindReturnsDue(DateTimeOffset now) => [];
        public bool CanShop(SelectedSessionIdentity session, int npcId, int shopId) => false;
        private ValueTask<BossTowerActionResult> Result() => ValueTask.FromResult(new BossTowerActionResult(true, Current));
    }
}
