using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.IllusionTowerStage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouchSocketSessionClient = TouchSocket.Sockets.ITcpSessionClient;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class IllusionTowerStageFrameIntegrationTests
{
    [Theory]
    [InlineData(11001)]
    [InlineData(11000)]
    public async Task Raw_tower_start_and_death_pickup_frames_reach_registered_decoders_and_real_run(int dungeonId)
    {
        var channelPort = AvailablePort();
        var gamePort = AvailablePort();
        while (gamePort == channelPort) gamePort = AvailablePort();
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Networking:Enabled"] = "true",
            ["Networking:ChannelPort"] = channelPort.ToString(),
            ["Networking:GamePorts:0"] = gamePort.ToString(),
            ["Game:EndpointHost"] = "127.0.0.1",
        });
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IServerConfigService>(new ServerConfigService(builder.Configuration));
        builder.Services.AddSingleton<IChannelCatalog>(StaticChannelCatalog.ForGamePort(gamePort));
        builder.Services.AddSingleton<IChannelScriptSource>(new NoChannelScript());
        builder.Services.AddSingleton(ChannelEntryOptions.Legacy);
        new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
        new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
        new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);
        // Keep persistence and the production tower aggregate/command pipeline real. Only the
        // archive-derived floor declaration and unrelated character repository are deterministic
        // seams so this test does not depend on a player's PVF or database.
        builder.Services.AddSingleton<IIllusionTowerRuleService>(new TowerRules());
        builder.Services.AddSingleton<ICharacterDomainService>(new Characters());
        builder.Services.AddSingleton<IItemPlacementDefinitionSource>(new Placements());
        builder.Services.RemoveAll<IIllusionTowerRewardDomainService>();

        using var host = TestInfrastructure.Build(builder);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
            var stream = client.GetStream();
            _ = await ReadFrameAsync(stream, timeout.Token);

            var connectionId = await ConnectedIdAsync(
                host.Services.GetRequiredService<IGameTcpService>(),
                host.Services.GetRequiredService<IOnlineUserQueryDomainService>(), timeout.Token);
            var selected = SelectedSessionIdentity.Create(
                new AccountId(7401), new CharacterId(7401), connectionId, 1, gamePort);
            host.Services.GetRequiredService<ISelectedSessionDomainService>().Select(selected);

            var containers = host.Services.GetRequiredService<ICharacterContainerDomainService>();
            if (dungeonId == 11000)
            {
                containers.Register(selected.CharacterId, new EmptyDefault(ContainerId.ForCharacter(selected.CharacterId, InventoryContainerType.Main), InventoryContainerType.Main));
                containers.Register(selected.CharacterId, new EmptyDefault(ContainerId.ForAccount(selected.AccountId, InventoryContainerType.SharedMaterial), InventoryContainerType.SharedMaterial));
                containers.Register(selected.CharacterId, new EmptyDefault(new ContainerId("tower:empty:network-test"), InventoryContainerType.QuickSlot));
            }
            var bus = host.Services.GetRequiredService<ICommandBus>();
            var entered = await bus.SendAsync(
                new EnterIllusionTowerCommand([new TowerEntryMember(selected, 0)], selected, 0x12345678, dungeonId),
                timeout.Token);
            var run = Assert.IsType<IllusionTowerSnapshot>(entered.Run);
            var loaded = await bus.SendAsync(
                new LoadIllusionTowerCommand(new TowerActionContext(selected, run.RunId, run.Room.Version)),
                timeout.Token);
            Assert.Equal(TowerRoomPhase.Ready, loaded.Run?.Room.Phase);

            await stream.WriteAsync(ClientFrame(0x009F, [1], sequence: 1), timeout.Token);

            var towers = host.Services.GetRequiredService<IIllusionTowerDomainService>();
            await WaitUntilAsync(
                () => towers.Find(selected)?.Room.Phase == TowerRoomPhase.Fighting,
                timeout.Token);
            Assert.Equal(run.RunId, towers.Find(selected)?.RunId);
            if (dungeonId == 11000)
            {
                var killed = await bus.SendAsync(new KillIllusionTowerActorCommand(
                    new(selected, run.RunId, run.Room.Version), run.Room.Actors[0].Uid), timeout.Token);
                var drop = Assert.Single(killed.Drops);
                var body = new byte[18];
                BinaryPrimitives.WriteUInt16LittleEndian(body, drop.Uid);
                await stream.WriteAsync(ClientFrame(0x002B, body, sequence: 2), timeout.Token);
                await WaitUntilAsync(() => towers.Find(selected)!.Room.Items.Single().Claimed, timeout.Token);
                var acknowledged = false;
                for (var count = 0; count < 8 && !acknowledged; count++)
                {
                    var frame = await ReadFrameAsync(stream, timeout.Token);
                    acknowledged = frame[0] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x002B;
                    if (acknowledged) Assert.Equal(1, frame[GameServerFrameEncoder.HeaderLength]);
                }
                Assert.True(acknowledged);
                var pickup = await ReadFrameAsync(stream, timeout.Token);
                Assert.Equal(0x0027, BinaryPrimitives.ReadUInt16LittleEndian(pickup.AsSpan(1)));
                var update = await ReadFrameAsync(stream, timeout.Token);
                Assert.Equal(0x000E, BinaryPrimitives.ReadUInt16LittleEndian(update.AsSpan(1)));
                Assert.Equal(29, update[GameServerFrameEncoder.HeaderLength]);
                Assert.Equal(3, BinaryPrimitives.ReadInt16LittleEndian(update.AsSpan(GameServerFrameEncoder.HeaderLength + 3)));
                var towerBag = containers.GetContainer(selected.CharacterId, InventoryContainerType.QuickSlot);
                Assert.False(towerBag.IsPersistent);
                Assert.Equal(6521, Assert.Single(await towerBag.GetAllAsync(timeout.Token)).ItemId);
                Assert.Empty(await containers.GetContainer(selected.CharacterId, InventoryContainerType.Main, isDefault: true).GetAllAsync(timeout.Token));
            }
            client.Dispose();
            await WaitUntilAsync(() => towers.Find(selected) is null, timeout.Token);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private static byte[] ClientFrame(ushort type, ReadOnlySpan<byte> body, ushort sequence)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length];
        frame[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), checked((uint)frame.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), sequence);
        body.CopyTo(frame.AsSpan(GameClientFrameRequestInfo.HeaderLength));
        return frame;
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken token)
    {
        var header = new byte[GameServerFrameEncoder.HeaderLength];
        await stream.ReadExactlyAsync(header, token);
        var frame = new byte[checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3)))];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(header.Length), token);
        return frame;
    }

    private static async Task<NetworkConnectionId> ConnectedIdAsync(
        IGameTcpService gameService, IOnlineUserQueryDomainService onlineUsers, CancellationToken token)
    {
        await WaitUntilAsync(() => gameService.GetClients().Count() == 1, token);
        var client = Assert.IsAssignableFrom<TouchSocketSessionClient>(gameService.GetClients().Single());
        var connectionId = new NetworkConnectionId($"Game:{client.Id}");
        await WaitUntilAsync(() => onlineUsers.TryGetByConnection(connectionId, out _), token);
        return connectionId;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(10, token);
    }

    private static int AvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class TowerRules : IIllusionTowerRuleService
    {
        public ValueTask<IllusionTowerDefinition> GetDefinitionAsync(CancellationToken cancellationToken, int dungeonId = 11001) =>
            ValueTask.FromResult(new IllusionTowerDefinition(
                dungeonId, 1, 40, [],
                [new TowerRoomDefinition(1, 60001, [], 0, [], [])], [],
                new IllusionTowerRewardParameters([1], [0], [100], 0, 0, 0),
                new IllusionTowerParameters(0, 0, 0, 0, [], [], [])));

        public ValueTask<TowerRoomSeed> CreateRoomAsync(
            int level, uint seed, CancellationToken cancellationToken, int dungeonId = 11001) =>
            ValueTask.FromResult(new TowerRoomSeed(
                level, 60000 + level, seed,
                [new TowerActorSeed(0, 608, 40, 4, true, new TowerPosition(1, 2, 0)) { Items = dungeonId == 11000 ? [6521] : [] }], []));
    }

    private sealed class Placements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken ct) =>
            ValueTask.FromResult<ItemPlacement?>(new(2, 1000, new(BusinessInventorySpace.Consumable, 0, 55)));
    }
    private sealed class EmptyDefault(ContainerId id, InventoryContainerType type) : IItemContainer
    {
        public ContainerId Id => id;
        public InventoryContainerType Type => type;
        public ContainerShape Shape => new(type, 0);
        public bool IsDefault => true;
        public bool IsRemovable => false;
        public ValueTask<ItemReference?> GetAsync(int index, CancellationToken ct) => ValueTask.FromResult<ItemReference?>(null);
        public ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<ItemReference>>([]);
        public ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ItemReference> PutAsync(int index, ItemRecord record, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask FlushAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }

    private sealed class Characters : ICharacterDomainService
    {
        private readonly ICharacter character = new Character();
        public ICharacter Get(CharacterId characterId) => character;
        public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(AccountId accountId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<AccountCharacterRoster> GetAccountRosterWithAppearanceAsync(AccountId accountId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ActiveCharacterState> LoadForSelectionAsync(AccountId accountId, ushort slotIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ActiveCharacterState> LoadByRosterPositionAsync(AccountId accountId, ushort rosterIndex, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<CharacterNameStatus> CheckNameAsync(byte[] rawName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<CharacterCreationResult> CreateCharacterAsync(AccountId accountId, byte job, byte[] rawName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<CharacterDeletionResult> DeleteCharacterAsync(AccountId accountId, ushort slotIndex, byte[] rawName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Character : ICharacter
    {
        public CharacterId Id => new(7401);
        public ValueTask<CharacterProgressionState?> ProgressAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<CharacterProgressionState?>(new CharacterProgressionState(60, 0));
        public ValueTask<CharacterBaseStats?> BaseStatsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<CharacterLearnedSkillPage>?> LearnedSkillsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<CharacterSkillPointLedger?> SkillPointLedgerAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ItemUsability> CanUseAsync(int itemId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ItemUseCandidate?> ItemUseFactsAsync(ItemCooldownKey cooldown, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<CharacterIdentityState?> IdentityAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<CharacterSkillProtocolMirrorState?> RemainingSkillPointsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<int> GoldAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<int> AddGoldAsync(int amount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<int?> TrySpendGoldAsync(int amount, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
