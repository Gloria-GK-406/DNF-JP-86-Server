using Dfo.Server.Application.Tests.Support;
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
        var returnUnits = new List<Dfo.Server.Application.Commands.CommandWorkUnitDeclaration>();
        using var fixture = CreateFixture(returnUnits: returnUnits);
        var towers = fixture.Services.GetRequiredService<IIllusionTowerDomainService>();
        IllusionTowerSnapshot firstRun = null!;
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await PrepareSelectedCharacterAsync(fixture, tickets: 1);
            var beforeExperience = fixture.Scalar("SELECT exp FROM characters WHERE character_id=1");
            var beforeGold = fixture.Gold;

            await EnterAsync(fixture);

            AssertEntryDebitPrecedesTowerMode(fixture.Sender.Messages);

            firstRun = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
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

                await MapLoadingRouteDriver.RunAsync(fixture.Services,
                    new(fixture.Connection), new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0), default);
                await MapLoadingRouteDriver.RunAsync(fixture.Services,
                    new(fixture.Connection), new IllusionTowerStageRequest(GameMessageMetadata.Empty, 1, []), default);

                if (floor == 1)
                {
                    var fighting = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
                    await MapLoadingRouteDriver.RunAsync(fixture.Services,
                        new(fixture.Connection), new IllusionTowerStageRequest(GameMessageMetadata.Empty, 2, []), default);
                    var refused = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
                    Assert.Equal(fighting.Room.Version, refused.Room.Version);
                    Assert.Equal(fighting.Room.Level, refused.Room.Level);
                }

                foreach (var actor in Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session)).Room.Actors)
                {
                    await TestEffectFootprints.NonEffect(fixture.Services, GameMessageMetadata.Empty, () => MonsterDeathRouteDriver.KillAsync(fixture.Services,fixture.Session.ConnectionId,
                        GameMessageMetadata.Empty,actor.Uid,fixture.Session.WireUserId,default));
                }

                var cleared = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
                if (floor == RealFloorMaps.Length)
                {
                    Assert.NotNull(cleared.Completion);
                    break;
                }

                Assert.Equal(TowerRoomPhase.Cleared, cleared.Room.Phase);
                await MapLoadingRouteDriver.RunAsync(fixture.Services,
                    new(fixture.Connection), new IllusionTowerStageRequest(GameMessageMetadata.Empty, 2, []), default);
                Assert.Equal(floor, towers.Find(fixture.Session)!.Room.Level);
                await MapLoadingRouteDriver.RunAsync(fixture.Services,
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

        });
        await fixture.Services.GetRequiredService<BackToVillageHandler>().HandleAsync(
            new(fixture.Connection), new BackToVillageRequest(GameMessageMetadata.Empty), default);
        var returned = Assert.Single(returnUnits, unit => unit.Name == "party-leave-illusion-tower");
        Assert.Equal(Dfo.Server.Application.Commands.CommandWorkUnitKind.Ordinary, returned.Kind);
        Assert.Empty(returned.Participants);
        Assert.Equal(Dfo.Server.Application.Commands.CommandFootprintMode.DeclaredNonRequest, returned.Footprint);
        Assert.Equal(new[] { fixture.Session }, returned.FootprintOwners!);
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {

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

    /// <summary>
    /// Successor of <c>Sweep_captured_for_an_old_run_cannot_return_or_reward_its_replacement</c>,
    /// deferred to T-004c: a whole sweep is now one timer CMD, so it cannot be overtaken between
    /// capturing a candidate and acting on it. What remains is that a real tick, run after the old
    /// run's return was due and the run was replaced, judges the current state and leaves the
    /// replacement alone.
    /// </summary>
    [Dfo.Server.Testing.PvfFact]
    public async Task A_sweep_tick_after_the_old_runs_return_was_due_leaves_its_replacement_alone()
    {
        using var fixture = CreateFixture();
        await PrepareSelectedCharacterAsync(fixture, tickets: 1);
        var towers = fixture.Services.GetRequiredService<IIllusionTowerDomainService>();
        var oldRunId = await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await EnterAsync(fixture);
            await MapLoadingRouteDriver.RunAsync(fixture.Services,
                new(fixture.Connection), new FinishLoadingRequest(GameMessageMetadata.Empty, 0, 0), default);
            await MapLoadingRouteDriver.RunAsync(fixture.Services,
                new(fixture.Connection), new IllusionTowerStageRequest(GameMessageMetadata.Empty, 1, []), default);
            await CharacterDeathRouteDriver.RunAsync(fixture.Services,
                new(fixture.Connection), new CharacterDeathRequest(GameMessageMetadata.Empty, 0, 0, null), default);
            var oldRun = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
            Assert.True(oldRun.Completion!.Settled);
            var due = Assert.IsType<DateTimeOffset>(oldRun.ReturnDueAt);
            if (due >= fixture.Clock.GetUtcNow()) fixture.Clock.Advance(due - fixture.Clock.GetUtcNow() + TimeSpan.FromSeconds(1));

            return oldRun.RunId;
        });
        await fixture.Services.GetRequiredService<BackToVillageHandler>().HandleAsync(
            new(fixture.Connection), new BackToVillageRequest(GameMessageMetadata.Empty), default);
        var (replacement, messages, settlementFrames, experience, gold) = await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await fixture.Seed(65, 4183, 1);
            fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>()
                .Record(fixture.Session, DungeonEntryIntent.Interactive);
            await EnterAsync(fixture);
            var entered = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
            Assert.NotEqual(oldRunId, entered.RunId);
            return (entered, fixture.Sender.Messages.Count, SettlementFrames(fixture),
                fixture.Scalar("SELECT exp FROM characters WHERE character_id=1"), fixture.Gold);
        });

        await fixture.Services.GetRequiredService<IllusionTowerReturnSweepService>().TickAsync(default);

        TestGameplay.Run(fixture.Services, () =>
        {
            var current = Assert.IsType<IllusionTowerSnapshot>(towers.Find(fixture.Session));
            Assert.Equal(replacement.RunId, current.RunId);
            Assert.Equal(replacement.Room.Version, current.Room.Version);
            Assert.Null(current.Completion);
            Assert.Equal(messages, fixture.Sender.Messages.Count);
            Assert.Equal(settlementFrames, SettlementFrames(fixture));
            Assert.Equal(experience, fixture.Scalar("SELECT exp FROM characters WHERE character_id=1"));
            Assert.Equal(gold, fixture.Gold);
            Assert.Null(fixture.Services.GetRequiredService<ITownAreaDomainService>().Find(fixture.Session.CharacterId));
        });
    }

    private static int SettlementFrames(ShopFixture fixture) => fixture.Sender.Messages.Count(message =>
        message.MessageType is ServerMessageType.GameIllusionTowerRanking
            or ServerMessageType.GameIllusionTowerReward or ServerMessageType.GameIllusionTowerContinue);

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

    private static ShopFixture CreateFixture(bool gateSweepFind = false, List<Dfo.Server.Application.Commands.CommandWorkUnitDeclaration>? returnUnits = null) => new(services =>
    {
        if (returnUnits is not null) Task42UnitObserver.Install(services, returnUnits);
        services.AddSingleton<IDungeonExperienceProgressionPolicySource, PvfDungeonExperienceProgressionPolicySource>();
        services.RemoveAll<IItemPlacementDefinitionSource>();
        services.AddSingleton<IItemPlacementDefinitionSource, PvfItemPlacementDefinitionSource>();
        services.RemoveAll<ICharacterSkillPointDomainService>();
        services.AddSingleton<ICharacterSkillPointDomainService, CharacterSkillPointDomainService>();
        if (gateSweepFind)
            DecorateTowerForSweepRace(services);
    }, useArchive: true);

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

    // The fixture already owns an executor job; exercise the actual whole body without nested dispatch.
    private static async ValueTask EnterAsync(ShopFixture fixture) =>
        _ = await DungeonEntryRouteDriver.WholeBodyAsync(fixture.Services, fixture.Session);

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
            Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken) => inner.KillAsync(context, actorUid, footprint, cancellationToken);
        public ValueTask<TowerActionResult> NextLevelAsync(TowerActionContext context,
            CancellationToken cancellationToken) => inner.NextLevelAsync(context, cancellationToken);
        public ValueTask<TowerActionResult> DieAsync(TowerActionContext context,
            Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken) => inner.DieAsync(context, footprint, cancellationToken);
        public ValueTask<TowerActionResult> LeaveAsync(TowerActionContext context, bool disconnected,
            Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken) => inner.LeaveAsync(context, disconnected, footprint, cancellationToken);
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
