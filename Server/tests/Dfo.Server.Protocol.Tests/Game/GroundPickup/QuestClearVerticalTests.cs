using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Quests;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.GroundPickup;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.GroundPickup;

public sealed class QuestClearVerticalTests
{
    private static readonly AccountId LeaderAccount = new(6);
    private static readonly CharacterId Leader = new(60);
    private static readonly AccountId HelperAccount = new(7);
    private static readonly CharacterId Helper = new(70);
    private static readonly ContainerId Main =
        ContainerId.ForCharacter(Leader, InventoryContainerType.Main);
    private static readonly SelectedSessionIdentity LeaderSession = Session(
        LeaderAccount, Leader, "leader", 1);
    private static readonly SelectedSessionIdentity HelperSession = Session(
        HelperAccount, Helper, "helper", 1);

    [Theory]
    [InlineData(PostCommitTransition.None)]
    [InlineData(PostCommitTransition.PickerReplaced)]
    [InlineData(PostCommitTransition.PickerDeparted)]
    public async Task Boss_death_opens_clear_before_the_last_sqlite_task_item_is_picked_up(
        PostCommitTransition transition)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-pickup-clear-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            await SeedCharactersAndQuestAsync(path);
            var itemStore = new SqliteItemDomainStore(path);
            await SeedItemsAsync(itemStore);

            var questStore = new SqliteQuestDomainStore(path);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddCommandDispatching();
            services.AddSingleton<IDungeonRunDomainService, DungeonRunDomainService>();
            services.AddSingleton<IQuestDomainStore>(questStore);
            services.AddSingleton<IItemDomainStore>(itemStore);
            services.AddSingleton<IQuestContentRuleService>(
                new QuestRules(SeekingDefinition()));
            services.AddSingleton<ICharacterDomainService>(CurrentCharacterStub.Domain());
            services.AddSingleton<ICharacterQuestLogDomainService>(provider =>
                new CharacterQuestLogDomainService(
                    questStore,
                    new AmbientStoreAccess(
                        provider.GetRequiredService<IDomainTransactionAccessor>())));
            services.AddSingleton<ICharacterContainerDomainService>(provider =>
            {
                var containers = new CharacterContainerDomainService();
                containers.Register(Leader, new PersistentItemContainer(
                    Main,
                    new ContainerShape(InventoryContainerType.Main, 56),
                    itemStore,
                    provider.GetRequiredService<IDomainTransactionAccessor>(),
                    PlacementRules(),
                    new ItemSlotRuleService()));
                return containers;
            });
            services.AddSingleton<ISeekingQuestProgressDomainService>(provider =>
                new SeekingQuestProgressDomainService(
                    provider.GetRequiredService<IQuestContentRuleService>(),
                    provider.GetRequiredService<ICharacterQuestLogDomainService>(),
                    provider.GetRequiredService<ICharacterContainerDomainService>(),
                    provider.GetRequiredService<ICharacterDomainService>()));
            services.AddSingleton<IQuestProgressDomainService>(provider =>
                new SeekingFactAdapter(
                    provider.GetRequiredService<ISeekingQuestProgressDomainService>()));
            services.AddSingleton<IGroundPickupDomainService>(new GroundPickupDomainService());
            services.AddSingleton<IItemGrantDomainService>(provider =>
                new Dfo.Server.Protocol.Tests.Support.OrdinaryItemGrantDomainService(PlacementRules(),
                    new ItemLifecycleDomainService(itemStore,
                        provider.GetRequiredService<IDomainTransactionAccessor>(), PlacementRules())));
            services.AddCommandHandler<
                PickUpGroundDropCommand,
                GroundPickupResult,
                PickUpGroundDropCommandHandler>(CommandExecutionKind.Transactional);
            await using var provider = services.BuildServiceProvider();

            var runs = provider.GetRequiredService<IDungeonRunDomainService>();
            DungeonRunParticipant[] participants =
            [
                new DungeonRunParticipant(LeaderAccount, Leader, 0, IsLeader: true),
                new DungeonRunParticipant(HelperAccount, Helper, 1, IsLeader: false),
            ];
            var layout = QuestLayout();
            var run = runs.PreparePartyEntry(
                participants,
                layout,
                difficulty: 0,
                new DungeonCellSeed(
                    layout.StartCell,
                    layout.StartRoomMapId,
                    FirstActorSequence: 30001,
                    Actors:
                    [
                        new DungeonCellActorState(
                            Sequence: 30001,
                            new DungeonRoomActor(1, 4, 0),
                            IsAiCharacter: false,
                            IsDead: false,
                            PendingLoot:
                            [
                                new RolledDrop(
                                    10149163,
                                    1,
                                    DropRarity.Common,
                                    DropItemKind.Stackable,
                                    OwnerCharacterId: Leader),
                            ]),
                    ],
                    GroundItems: []),
                DungeonRunId.New(),
                DateTimeOffset.UnixEpoch).Run;
            var death = Assert.IsType<DungeonActorDeathClaim>(
                runs.TryClaimActorDeath(Leader, 30001));
            var afterDeath = Assert.IsType<DungeonRunState>(runs.CommitActorDeath(death));
            var clear = new DungeonClearDomainService(
                runs,
                new ClearedBossRoom());
            var bossClear = Assert.IsType<DungeonClearChange>(await clear.EvaluateAsync(
                LeaderAccount, Leader, run.RunId, run.RoomVersion, default));

            var drop = Assert.Single(afterDeath.CurrentCellState.GroundItems);
            var pickup = await provider.GetRequiredService<ICommandBus>().SendAsync(
                new PickUpGroundDropCommand(
                    LeaderAccount,
                    Leader,
                    drop.Number,
                    run.RunId,
                    run.RoomVersion),
                CancellationToken.None);

            var replacement = Session(LeaderAccount, Leader, "replacement", 2);
            IReadOnlyList<SelectedSessionIdentity> current = transition switch
            {
                PostCommitTransition.PickerReplaced => [replacement, HelperSession],
                PostCommitTransition.PickerDeparted => [HelperSession],
                _ => [LeaderSession, HelperSession],
            };
            if (transition is PostCommitTransition.PickerDeparted)
                Assert.True(runs.End(Leader, run.RunId));

            var sessions = new Sessions(current);
            var registry = new DungeonRunDeliveryRegistry();
            registry.Register(run.RunId, [LeaderSession, HelperSession]);
            var sender = new RecordingSender();
            var projection = new DungeonPickupProjectionDelivery(
                sessions, registry, sender, TimeProvider.System);
            var postCommit = new DungeonPickupPostCommitCoordinator(
                clear,
                new DungeonClearProjectionDelivery(sessions, runs, registry, sender));
            Assert.Null(await postCommit.EvaluateAsync(LeaderSession, pickup));
            var pickupDelivered = await projection.DeliverAsync(
                LeaderSession, pickup, default);
            _ = await postCommit.ProjectAsync(LeaderSession, bossClear, default);

            Assert.Equal(GroundPickupStatus.PickedUp, pickup.Status);
            Assert.Equal(0, Assert.Single(pickup.QuestProgress!.Quests).TriggerValue);
            Assert.NotNull(pickup.CommittedChange);
            Assert.Equal(15, await StoredQuantityAsync(itemStore));
            Assert.Equal(0, await TriggerAsync(path));
            Assert.Equal(transition is PostCommitTransition.None, pickupDelivered);
            Assert.True(runs.Find(run.RunId)!.ClearOpen);
            Assert.Null(await clear.EvaluateAsync(
                LeaderAccount, Leader, run.RunId, run.RoomVersion, default));
            Assert.Equal(
                transition is PostCommitTransition.None ? 1 : 0,
                sender.For(LeaderSession.ConnectionId)
                    .OfType<EnableClearDungeonNotification>().Count());
            if (transition is not PostCommitTransition.None)
                Assert.Empty(sender.For(LeaderSession.ConnectionId));
            Assert.Empty(sender.For(replacement.ConnectionId));
            Assert.Single(sender.For(HelperSession.ConnectionId)
                .OfType<EnableClearDungeonNotification>());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A pooled test connection may outlive the assertion process briefly.
            }
        }
    }

    public enum PostCommitTransition
    {
        None,
        PickerReplaced,
        PickerDeparted,
    }

    private static IItemPlacementRuleService PlacementRules() =>
        new ItemPlacementRuleService(new QuestItemPlacement(), new NoWearables());

    private static async Task SeedCharactersAndQuestAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts(account_id,m_id,password_hash)
            VALUES(6,'pickup','x'),(7,'helper','x');
            INSERT INTO characters(character_id,account_id,name)
            VALUES(60,6,'pickup'),(70,7,'helper');
            INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value)
            VALUES(60,0,1779,1);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedItemsAsync(SqliteItemDomainStore store)
    {
        var transaction = new DomainTransaction();
        var session = store.SessionIn(transaction);
        var item = await session.CreateAsync(ItemSnapshot.FromCore(QuestItem(14), true), default);
        await session.PutAsync(Main, 9, item, default);
        await transaction.CommitAsync(CancellationToken.None);
    }

    private static async Task<int> StoredQuantityAsync(SqliteItemDomainStore store)
    {
        var transaction = new DomainTransaction();
        var stored = await store.SessionIn(transaction).GetAsync(Main, 9, default);
        await transaction.CommitAsync(CancellationToken.None);
        return stored!.Snapshot.Count;
    }

    private static async Task<int> TriggerAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT trigger_value FROM character_active_quests WHERE character_id=60 AND slot=0";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static InventoryItemCoreState QuestItem(int count) =>
        new(6, 10149163, count, 0, 0, 0, 0, 0, 0, 0, -1,
            new ChronicleOptionState(0, 0, 0, 0, 0),
            new ChronicleOptionState(0, 0, 0, 0, 0),
            0, 0, 0, 0, 0,
            new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0),
            0, byte.MaxValue, 0,
            new RandomOptionState(0, 0, 0),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static DungeonLayout QuestLayout() => new(
        145,
        0,
        1,
        1,
        new MazeCell(0, 0),
        new MazeCell(0, 0),
        8154,
        1,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8154)},
        15,
        Purpose: DungeonRunPurpose.QuestLinked(1779, DungeonQuestConnectionState.Active));

    private static QuestDefinition SeekingDefinition() => new(
        1779, "quest", null, null, null, null, null, null, [], "seeking", "",
        null, null, null, null, null, null, null, [], [], [], [],
        SeekingItems: [new SeekingItemRequirement(10149163, 15)]);

    private static SelectedSessionIdentity Session(
        AccountId accountId,
        CharacterId characterId,
        string connection,
        long generation) =>
        SelectedSessionIdentity.Create(
            accountId,
            characterId,
            new NetworkConnectionId(connection),
            generation,
            10010);

    private sealed class QuestRules(QuestDefinition definition) : IQuestContentRuleService
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([definition]);
        public ValueTask<QuestDefinition?> FindAsync(
            int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<QuestDefinition?>(questId == definition.QuestId ? definition : null);
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
        public QuestParameters Parameters() => throw new NotSupportedException();
    }

    private sealed class QuestItemPlacement : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(
            int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(itemId == 10149163
                ? new ItemPlacement(6, 15, new(BusinessInventorySpace.CarriedEquipment, 0, 55))
                : null);
    }

    private sealed class NoWearables : IEquipmentSlotDefinitionSource
    {
        public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BusinessSlot?>(null);
    }

    private sealed class NoRoomActors : IDungeonRoomActorSource
    {
        public ValueTask<IReadOnlyList<DungeonRoomActor>> GetAsync(
            int mapId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DungeonRoomActor>>([]);
    }

    private sealed class NoRoomDrops : IDungeonRoomDropSource
    {
        public ValueTask<IReadOnlyList<ObjectDropDefinition>> GetAsync(
            int dungeonId, int mapId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<ObjectDropDefinition>>([]);
    }

    private sealed class SeekingFactAdapter(ISeekingQuestProgressDomainService seeking)
        : IQuestProgressDomainService
    {
        public async ValueTask<QuestProgressReport> ReportAsync(
            AccountId accountId, CharacterId characterId,
            IReadOnlyList<QuestProgressFact> facts, CancellationToken cancellationToken)
        {
            Assert.All(facts, fact => Assert.IsType<QuestHoldingsChanged>(fact));
            var changed = await seeking.SynchronizeAsync(
                accountId, characterId, cancellationToken);
            return new QuestProgressReport(
                null, [], changed is null ? null : new QuestProgressSnapshot(changed.Quests));
        }
    }

    private sealed class ClearedBossRoom : IMonsterKillDomainService
    {
        public ValueTask<KillIdentification> IdentifyAsync(
            DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<bool> IsCurrentRoomClearedAsync(
            DungeonRunState run, CancellationToken cancellationToken) =>
            ValueTask.FromResult(run.CurrentCellState.IsCleared);
    }

    private sealed class Sessions(IReadOnlyList<SelectedSessionIdentity> sessions)
        : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        public SelectedSessionIdentity? Find(AccountId accountId) =>
            sessions.SingleOrDefault(value => value.AccountId == accountId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            sessions.SingleOrDefault(value => value.CharacterId == characterId);
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            sessions.SingleOrDefault(value => value.WireUserId == wireUserId);
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            sessions.SingleOrDefault(value => value.ConnectionId == connectionId);
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
            throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly Dictionary<NetworkConnectionId, List<IGameServerMessage>> sent = [];

        public IReadOnlyList<IGameServerMessage> For(NetworkConnectionId connectionId) =>
            sent.TryGetValue(connectionId, out var frames) ? frames : [];
        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            Add(requiredDirectConnectionId, delivery.RequiredDirectMessages);
            foreach (var target in delivery.BestEffortTargets)
                Add(target.ConnectionId, target.OrderedMessages);
            return ValueTask.CompletedTask;
        }

        private void Add(
            NetworkConnectionId connectionId,
            IReadOnlyList<IGameServerMessage> frames)
        {
            if (!sent.TryGetValue(connectionId, out var received))
                sent[connectionId] = received = [];
            received.AddRange(frames);
        }
    }
}
