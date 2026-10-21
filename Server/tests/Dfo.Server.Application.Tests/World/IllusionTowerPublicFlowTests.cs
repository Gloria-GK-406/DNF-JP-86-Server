using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.IllusionTowerStage;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Tests.World;

public sealed class IllusionTowerPublicFlowTests
{
    private static readonly int[] RealFloorMaps =
        [31001, 31002, 31003, 31004, 31005, 31007, 31008, 31010, 31011, 31006, 31019, 31015, 31020, 31024, 31025];

    [Dfo.Server.Testing.PvfFact]
    public async Task Public_handlers_complete_real_pvf_tower_return_to_town_and_reenter()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = CreateFixture();
            await PrepareSelectedCharacterAsync(fixture, tickets: 1);
            var beforeExperience = fixture.Scalar("SELECT exp FROM characters WHERE character_id=1");
            var beforeGold = fixture.Gold;

            await EnterAsync(fixture);

            AssertEntryDebitPrecedesTowerMode(fixture.Sender.Messages);

            var towers = fixture.Services.GetRequiredService<IIllusionTowerDomainService>();
            var firstRun = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
            Assert.Equal(0, fixture.Count(4183));
            Assert.Null(fixture.Services.GetRequiredService<ITownAreaDomainService>().Find(fixture.Session.CharacterId));
            Assert.Equal(
                [ServerMessageType.GameIllusionTowerInfo, ServerMessageType.GameIllusionTowerMap],
                fixture.Sender.Messages
                    .Where(message => message.MessageType is ServerMessageType.GameIllusionTowerInfo
                        or ServerMessageType.GameIllusionTowerMap)
                    .Select(message => message.MessageType));

            List<int> visitedMaps = [];
            for (var floor = 1; floor <= RealFloorMaps.Length; floor++)
            {
                var loading = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
                Assert.Equal(floor, loading.Room.Level);
                Assert.Equal(TowerRoomPhase.Loading, loading.Room.Phase);
                Assert.NotEmpty(loading.Room.Actors);
                visitedMaps.Add(loading.Room.MapId);

                await fixture.Services.GetRequiredService<FinishLoadingHandler>().HandleAsync(
                    new(fixture.Connection), new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0), default);
                await fixture.Services.GetRequiredService<IllusionTowerStageHandler>().HandleAsync(
                    new(fixture.Connection), new IllusionTowerStageRequest(GameMessageMetadata.Empty, 1, []), default);

                if (floor == 1)
                {
                    var fighting = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
                    await fixture.Services.GetRequiredService<IllusionTowerStageHandler>().HandleAsync(
                        new(fixture.Connection), new IllusionTowerStageRequest(GameMessageMetadata.Empty, 2, []), default);
                    var refused = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
                    Assert.Equal(fighting.Room.Version, refused.Room.Version);
                    Assert.Equal(fighting.Room.Level, refused.Room.Level);
                }

                foreach (var actor in Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session)).Room.Actors)
                {
                    await fixture.Services.GetRequiredService<MonsterDeathHandler>().HandleAsync(
                        new(fixture.Connection),
                        new MonsterDeathRequest(GameMessageMetadata.Empty, actor.Uid, fixture.Session.WireUserId),
                        default);
                }

                var cleared = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
                if (floor == RealFloorMaps.Length)
                {
                    Assert.NotNull(cleared.Completion);
                    break;
                }

                Assert.Equal(TowerRoomPhase.Cleared, cleared.Room.Phase);
                await fixture.Services.GetRequiredService<IllusionTowerStageHandler>().HandleAsync(
                    new(fixture.Connection), new IllusionTowerStageRequest(GameMessageMetadata.Empty, 2, []), default);
                Assert.Equal(floor, towers.Find(fixture.Session)!.Room.Level);
                await fixture.Services.GetRequiredService<MoveMapHandler>().HandleAsync(
                    new(fixture.Connection), new MoveMapRequest(GameMessageMetadata.Empty, 0, 0, 0, 0), default);
                Assert.Equal(floor + 1, towers.Find(fixture.Session)!.Room.Level);
            }

            Assert.Equal(RealFloorMaps, visitedMaps);
            var completed = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
            Assert.Equal(firstRun.RunId, completed.RunId);
            Assert.True(completed.Completion!.Settled);
            var member = Assert.Single(completed.Completion.Receipt!.Members);
            var afterExperience = fixture.Scalar("SELECT exp FROM characters WHERE character_id=1");
            Assert.True(afterExperience - beforeExperience >= member.Experience);
            Assert.True(fixture.Sender.Messages.Count(message =>
                message.MessageType == ServerMessageType.GameExperience) > RealFloorMaps.Length);
            Assert.Equal(member.Gold, fixture.Gold - beforeGold);
            foreach (var rewards in member.Items.GroupBy(reward => reward.ItemId))
                Assert.Equal(rewards.Sum(reward => reward.Count), fixture.Count(rewards.Key));
            Assert.Equal(
                [ServerMessageType.GameIllusionTowerRanking, ServerMessageType.GameIllusionTowerReward,
                    ServerMessageType.GameIllusionTowerContinue],
                fixture.Sender.Messages
                    .Where(message => message.MessageType is ServerMessageType.GameIllusionTowerRanking
                        or ServerMessageType.GameIllusionTowerReward or ServerMessageType.GameIllusionTowerContinue)
                    .Select(message => message.MessageType));

            await fixture.Services.GetRequiredService<BackToVillageHandler>().HandleAsync(
                new(fixture.Connection), new BackToVillageRequest(GameMessageMetadata.Empty), default);

            Assert.Null(towers.Find(fixture.Session));
            Assert.NotNull(fixture.Services.GetRequiredService<ITownAreaDomainService>().Find(fixture.Session.CharacterId));

            fixture.Sender.Messages.Clear();
            await fixture.Seed(65, 4183, 1);
            fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>()
                .Record(fixture.Session, DungeonEntryIntent.Interactive);
            await EnterAsync(fixture);

            var secondRun = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
            AssertEntryDebitPrecedesTowerMode(fixture.Sender.Messages);
            Assert.NotEqual(firstRun.RunId, secondRun.RunId);
            Assert.Equal(0, fixture.Count(4183));
            Assert.Equal(
                [ServerMessageType.GameIllusionTowerInfo, ServerMessageType.GameIllusionTowerMap],
                fixture.Sender.Messages
                    .Where(message => message.MessageType is ServerMessageType.GameIllusionTowerInfo
                        or ServerMessageType.GameIllusionTowerMap)
                    .Select(message => message.MessageType));
        });
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Sweep_captured_for_an_old_run_cannot_return_or_reward_its_replacement()
    {
        using var fixture = CreateFixture(gateSweepFind: true);
        await PrepareSelectedCharacterAsync(fixture, tickets: 1);
        await EnterAsync(fixture);
        await fixture.Services.GetRequiredService<FinishLoadingHandler>().HandleAsync(
            new(fixture.Connection), new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0), default);
        await fixture.Services.GetRequiredService<IllusionTowerStageHandler>().HandleAsync(
            new(fixture.Connection), new IllusionTowerStageRequest(GameMessageMetadata.Empty, 1, []), default);
        await fixture.Services.GetRequiredService<CharacterDeathHandler>().HandleAsync(
            new(fixture.Connection), new CharacterDeathRequest(GameMessageMetadata.Empty, 0, 0, null), default);

        var towers = fixture.Services.GetRequiredService<IIllusionTowerDomainService>();
        var oldRun = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
        Assert.True(oldRun.Completion!.Settled);
        var due = Assert.IsType<DateTimeOffset>(oldRun.ReturnDueAt);
        var gate = fixture.Services.GetRequiredService<SweepFindGate>();
        var delivery = fixture.Services.GetRequiredService<IllusionTowerProjectionDelivery>();

        var sweep = Task.Run(async () => await delivery.SweepAsync(due, default));
        Assert.True(gate.Captured.Wait(TimeSpan.FromSeconds(10)));

        await fixture.Services.GetRequiredService<BackToVillageHandler>().HandleAsync(
            new(fixture.Connection), new BackToVillageRequest(GameMessageMetadata.Empty), default);
        Assert.Null(towers.Find(fixture.Session));
        await fixture.Seed(65, 4183, 1);
        fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>()
            .Record(fixture.Session, DungeonEntryIntent.Interactive);
        await EnterAsync(fixture);

        var replacement = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
        Assert.NotEqual(oldRun.RunId, replacement.RunId);
        var messagesBeforeRelease = fixture.Sender.Messages.Count;
        var experienceBeforeRelease = fixture.Scalar("SELECT exp FROM characters WHERE character_id=1");
        var goldBeforeRelease = fixture.Gold;
        var settlementFramesBeforeRelease = fixture.Sender.Messages.Count(message =>
            message.MessageType is ServerMessageType.GameIllusionTowerRanking
                or ServerMessageType.GameIllusionTowerReward or ServerMessageType.GameIllusionTowerContinue);

        gate.Release.Set();
        await sweep;

        var current = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
        Assert.Equal(replacement.RunId, current.RunId);
        Assert.Equal(replacement.Room.Version, current.Room.Version);
        Assert.Null(current.Completion);
        Assert.Equal(messagesBeforeRelease, fixture.Sender.Messages.Count);
        Assert.Equal(settlementFramesBeforeRelease, fixture.Sender.Messages.Count(message =>
            message.MessageType is ServerMessageType.GameIllusionTowerRanking
                or ServerMessageType.GameIllusionTowerReward or ServerMessageType.GameIllusionTowerContinue));
        Assert.Equal(experienceBeforeRelease, fixture.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(goldBeforeRelease, fixture.Gold);
        Assert.Null(fixture.Services.GetRequiredService<ITownAreaDomainService>().Find(fixture.Session.CharacterId));
    }

    internal static void AssertEntryDebitPrecedesTowerMode(IEnumerable<IServerNetworkMessage> messages, int receivers = 1)
    {
        var types = messages.Select(message => message.MessageType).ToArray();
        Assert.Equal(
            Enumerable.Range(0, receivers).SelectMany(_ => new[]
            {
                ServerMessageType.GameDeleteItem,
                ServerMessageType.GameIllusionTowerInfo, ServerMessageType.GameIllusionTowerMap
            }),
            types.Where(type => type is ServerMessageType.GameDeleteItem
                or ServerMessageType.GameIllusionTowerInfo or ServerMessageType.GameIllusionTowerMap));
        foreach (var index in Enumerable.Range(0, types.Length)
            .Where(index => types[index] == ServerMessageType.GameDeleteItem))
        {
            Assert.True(index + 1 < types.Length);
            Assert.Equal(ServerMessageType.GameStateSynchronization, types[index + 1]);
        }
    }

    private static ShopFixture CreateFixture(bool gateSweepFind = false) => new(services =>
    {
        services.AddSingleton<IDungeonExperienceProgressionPolicySource, PvfDungeonExperienceProgressionPolicySource>();
        services.RemoveAll<IItemPlacementDefinitionSource>();
        services.AddSingleton<IItemPlacementDefinitionSource, PvfItemPlacementDefinitionSource>();
        services.RemoveAll<ICharacterSkillPointDomainService>();
        services.AddSingleton<ICharacterSkillPointDomainService, CharacterSkillPointDomainService>();
        services.AddSingleton<INetworkConnectionCloser, NoopConnectionCloser>();
        if (gateSweepFind)
            DecorateTowerForSweepRace(services);
    });

    private static void DecorateTowerForSweepRace(IServiceCollection services)
    {
        var descriptor = services.Last(value => value.ServiceType == typeof(IIllusionTowerDomainService));
        var implementation = descriptor.ImplementationType
            ?? throw new InvalidOperationException("The production tower service must have an implementation type.");
        services.Remove(descriptor);
        services.AddSingleton(implementation);
        services.AddSingleton(provider => new SweepFindGate(
            (IIllusionTowerDomainService)provider.GetRequiredService(implementation)));
        services.AddSingleton<IIllusionTowerDomainService>(provider =>
            provider.GetRequiredService<SweepFindGate>());
    }

    private static async Task PrepareSelectedCharacterAsync(ShopFixture fixture, int tickets)
    {
        fixture.Sql($"""
            UPDATE characters SET level=50,exp={fixture.ExperienceForLevel(50)} WHERE character_id=1;
            INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0),(2,0);
            INSERT INTO character_pvp(character_id) VALUES(1),(2);
            INSERT INTO character_settings(character_id) VALUES(1),(2);
            INSERT INTO character_expert_job(character_id) VALUES(1),(2);
            """);
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            fixture.Services.GetRequiredService<ISelectedSessionDomainService>().EnsureInWorld(fixture.Session);
            fixture.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>()
                .Begin(fixture.Session, new CharacterPositionState(1, 0, 0, 0, 0), 100);
            await fixture.Seed(65, 4183, tickets);
            fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>()
                .Record(fixture.Session, DungeonEntryIntent.Interactive);
        });
    }

    private static ValueTask EnterAsync(ShopFixture fixture) =>
        fixture.Services.GetRequiredService<SelectDungeonHandler>().HandleAsync(
            new(fixture.Connection),
            new SelectDungeonRequest(GameMessageMetadata.Empty, 11001, DungeonDifficulty.Normal, 0, 0),
            default);

    private sealed class NoopConnectionCloser : INetworkConnectionCloser
    {
        public ValueTask RequestCloseAsync(NetworkConnectionId connectionId,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class SweepFindGate(IIllusionTowerDomainService inner) : IIllusionTowerDomainService
    {
        private int armedThread = -1;
        private int blockNextFind;

        public ManualResetEventSlim Captured { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);

        public IllusionTowerSnapshot? Find(SelectedSessionIdentity session)
        {
            if (Environment.CurrentManagedThreadId == Volatile.Read(ref armedThread)
                && Interlocked.Exchange(ref blockNextFind, 0) == 1)
            {
                Captured.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The sweep race was not released.");
            }
            return inner.Find(session);
        }

        public IReadOnlyList<TowerActionContext> FindReturnsDue(DateTimeOffset now)
        {
            var due = inner.FindReturnsDue(now);
            if (due.Count > 0)
            {
                Volatile.Write(ref armedThread, Environment.CurrentManagedThreadId);
                Volatile.Write(ref blockNextFind, 1);
            }
            return due;
        }

        public bool IsInTower(CharacterId characterId) => inner.IsInTower(characterId);
        public bool TryReserveOrdinaryEntry(IReadOnlyList<CharacterId> members) =>
            inner.TryReserveOrdinaryEntry(members);
        public ValueTask<TowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members,
            SelectedSessionIdentity leader, uint seed, CancellationToken cancellationToken, int dungeonId = 11001) =>
            inner.EnterAsync(members, leader, seed, cancellationToken, dungeonId);
        public ValueTask<TowerActionResult> LoadAsync(TowerActionContext context,
            CancellationToken cancellationToken) => inner.LoadAsync(context, cancellationToken);
        public ValueTask<TowerActionResult> StartAsync(TowerActionContext context,
            CancellationToken cancellationToken) => inner.StartAsync(context, cancellationToken);
        public ValueTask<TowerActionResult> KillAsync(TowerActionContext context, ushort actorUid,
            CancellationToken cancellationToken) => inner.KillAsync(context, actorUid, cancellationToken);
        public ValueTask<TowerActionResult> NextLevelAsync(TowerActionContext context,
            CancellationToken cancellationToken) => inner.NextLevelAsync(context, cancellationToken);
        public ValueTask<TowerActionResult> DieAsync(TowerActionContext context,
            CancellationToken cancellationToken) => inner.DieAsync(context, cancellationToken);
        public ValueTask<TowerActionResult> LeaveAsync(TowerActionContext context, bool disconnected,
            CancellationToken cancellationToken) => inner.LeaveAsync(context, disconnected, cancellationToken);
        public ValueTask<TowerActionResult> SynchronizeLeaderAsync(TowerActionContext context,
            SelectedSessionIdentity leader, CancellationToken cancellationToken) =>
            inner.SynchronizeLeaderAsync(context, leader, cancellationToken);
        public ValueTask<bool> CanContinueAsync(TowerActionContext context,
            CancellationToken cancellationToken) => inner.CanContinueAsync(context, cancellationToken);
        public ValueTask<TowerActionResult> MarkSettledAsync(TowerActionContext context,
            TowerSettlementReceipt receipt, CancellationToken cancellationToken) =>
            inner.MarkSettledAsync(context, receipt, cancellationToken);
        public ValueTask<TowerActionResult> SelectReturnAsync(TowerActionContext context, byte option,
            CancellationToken cancellationToken) => inner.SelectReturnAsync(context, option, cancellationToken);
        public ValueTask<bool> SupportsDungeonAsync(int dungeonId, CancellationToken cancellationToken) =>
            inner.SupportsDungeonAsync(dungeonId, cancellationToken);
    }
}
