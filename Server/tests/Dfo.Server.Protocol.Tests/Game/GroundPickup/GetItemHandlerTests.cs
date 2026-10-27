using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.GroundPickup;

public sealed class GetItemHandlerTests
{
    private static readonly SelectedSessionIdentity Leader = Session(1, "leader", 1);
    private static readonly SelectedSessionIdentity Helper = Session(2, "helper", 1);

    [Fact]
    public async Task Successful_pickup_removes_the_world_piece_for_every_frozen_participant()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (handler, sender) = Build(
                [Leader, Helper],
                [new GroundPickupResult(GroundPickupStatus.PickedUp, 14400, 7)]);

            await handler.HandleAsync(
                Dispatch(Leader.ConnectionId),
                new GetItemRequest(GameMessageMetadata.Empty, 3),
                CancellationToken.None);

            Assert.Collection(
                sender.Delivery!.RequiredDirectMessages,
                message => Assert.IsType<GetItemResponse>(message),
                message => Assert.IsType<GroundPickupNotification>(message));
            var helper = Assert.Single(sender.Delivery.BestEffortTargets);
            Assert.Equal(Helper.ConnectionId, helper.ConnectionId);
            var notice = Assert.IsType<GroundPickupNotification>(Assert.Single(helper.OrderedMessages));
            Assert.Equal(Leader.WireUserId, notice.PickerActorId);
            Assert.Equal((ushort)3, notice.GroundDropNumber);
        });
    }

    [Fact]
    public async Task Successful_task_item_pickup_projects_the_complete_carried_quest_progress()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (handler, sender) = Build(
                [Leader, Helper],
                [new GroundPickupResult(
                    GroundPickupStatus.PickedUp,
                    10149163,
                    7,
                    QuestProgress: new QuestProgressSnapshot(
                        [new CarriedQuest(0, 1779, 14), new CarriedQuest(1, 2000, 7)]))]);

            await handler.HandleAsync(
                Dispatch(Leader.ConnectionId),
                new GetItemRequest(GameMessageMetadata.Empty, 3),
                CancellationToken.None);

            var progress = Assert.IsType<AcceptedQuestProgressMessage>(
                sender.Delivery!.RequiredDirectMessages[2]);
            Assert.Equal([(1779, 14u), (2000, 7u)],
                progress.Quests.Select(quest => ((int)quest.QuestId, quest.TriggerValue)));
            Assert.Single(sender.Delivery.BestEffortTargets);
            Assert.IsType<GroundPickupNotification>(
                Assert.Single(sender.Delivery.BestEffortTargets[0].OrderedMessages));
        });
    }

    [Fact]
    public async Task Final_task_item_pickup_rechecks_and_projects_shared_clear_after_pickup()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var run = runs.BeginParty(
                [
                    new DungeonRunParticipant(Leader.AccountId, Leader.CharacterId, 0, true),
                    new DungeonRunParticipant(Helper.AccountId, Helper.CharacterId, 1, false),
                ],
                Layout(),
                difficulty: 0);
            var registry = new DungeonRunDeliveryRegistry();
            registry.Register(run.RunId, [Leader, Helper]);
            var sessions = new Sessions([Leader, Helper]);
            var sender = new RecordingSender();
            var evaluator = new RecordingClearEvaluator(new DungeonClearChange(
                run.RunId,
                1,
                [Leader.CharacterId, Helper.CharacterId]));
            var projection = new PickupProjectionDriver(
                sessions, runs, run, registry, sender, TimeProvider.System);
            var postCommit = new PickupClearDriver(
                evaluator,
                global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.DungeonClear.DungeonClearProjectionDriver>(sessions, runs, registry, sender));
            var result = new GroundPickupResult(
                GroundPickupStatus.PickedUp,
                10149163,
                7,
                Change: new DungeonPickupChange(
                    run.RunId,
                    1,
                    Leader.CharacterId,
                    new GroundDropId(run.CurrentCell, 3),
                    DungeonPickupKind.Item,
                    [Leader.CharacterId, Helper.CharacterId],
                    run.RoomVersion),
                QuestProgress: new QuestProgressSnapshot(
                    [new CarriedQuest(0, 1779, 15)]));

            var opened = Assert.IsType<DungeonClearChange>(
                await postCommit.EvaluateAsync(Leader, result));
            Assert.True(await projection.DeliverAsync(Leader, result, default));
            _ = await postCommit.ProjectAsync(Leader, opened, default);

            Assert.Equal((run.RunId, run.RoomVersion), evaluator.LastRequest);
            Assert.Equal(2, sender.Deliveries.Count);
            Assert.Contains(
                sender.Deliveries[0].RequiredDirectMessages,
                message => message is AcceptedQuestProgressMessage);
            Assert.Collection(
                sender.Deliveries[1].RequiredDirectMessages,
                message => Assert.IsType<EnableClearDungeonNotification>(message),
                message => Assert.IsType<SecretShopNpcNotification>(message));
            Assert.Collection(
                sender.Deliveries[1].BestEffortTargets.Single().OrderedMessages,
                message => Assert.IsType<EnableClearDungeonNotification>(message),
                message => Assert.IsType<SecretShopNpcNotification>(message));
        });
    }

    [Fact]
    public async Task Pickup_send_failure_cannot_erase_the_committed_shared_clear_fact()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = TestParties.Runs();
            var run = runs.BeginParty(
                [new DungeonRunParticipant(Leader.AccountId, Leader.CharacterId, 0, true)],
                Layout(),
                difficulty: 0);
            var registry = new DungeonRunDeliveryRegistry();
            registry.Register(run.RunId, [Leader]);
            var sessions = new Sessions([Leader]);
            var sender = new ThrowingSender();
            var clear = new DungeonClearDomainService(
                runs,
                new ClearedRoom());
            var projection = new PickupProjectionDriver(
                sessions, runs, run, registry, sender, TimeProvider.System);
            var postCommit = new PickupClearDriver(
                clear,
                global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.DungeonClear.DungeonClearProjectionDriver>(sessions, runs, registry, sender));
            var result = new GroundPickupResult(
                GroundPickupStatus.PickedUp,
                10149163,
                7,
                Change: new DungeonPickupChange(
                    run.RunId,
                    1,
                    Leader.CharacterId,
                    new GroundDropId(run.CurrentCell, 3),
                    DungeonPickupKind.Item,
                    [Leader.CharacterId],
                    run.RoomVersion),
                QuestProgress: new QuestProgressSnapshot(
                    [new CarriedQuest(0, 1779, 0)]));

            Assert.NotNull(await postCommit.EvaluateAsync(Leader, result));
            await Assert.ThrowsAsync<IOException>(async () =>
                await projection.DeliverAsync(Leader, result, default));

            Assert.True(runs.Find(Leader.CharacterId)!.ClearOpen);
            Assert.Null(runs.TryOpenClear(Leader.CharacterId, run.RunId, run.RoomVersion));
        });
    }

    [Fact]
    public async Task Replacement_connection_does_not_receive_the_old_runs_pickup()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var replacement = Helper with
            {
                ConnectionId = new NetworkConnectionId("replacement"),
                ConnectionGeneration = 2,
            };
            var (handler, sender) = Build(
                [Leader, Helper],
                [new GroundPickupResult(GroundPickupStatus.Banked, 0, 0, 90, 190)],
                current: [Leader, replacement]);

            await handler.HandleAsync(
                Dispatch(Leader.ConnectionId),
                new GetItemRequest(GameMessageMetadata.Empty, 5),
                CancellationToken.None);

            Assert.Empty(sender.Delivery!.BestEffortTargets);
            Assert.Contains(sender.Delivery.RequiredDirectMessages,
                message => message is GroundGoldPickupNotification);
        });
    }

    [Fact]
    public async Task Gold_pickup_projects_quest_progress_when_currency_changes_an_objective()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (handler, sender) = Build(
                [Leader],
                [new GroundPickupResult(
                    GroundPickupStatus.Banked, 0, 0, 90, 190,
                    QuestProgress: new QuestProgressSnapshot([new CarriedQuest(0, 1779, 0)]))]);

            await handler.HandleAsync(
                Dispatch(Leader.ConnectionId),
                new GetItemRequest(GameMessageMetadata.Empty, 5),
                CancellationToken.None);

            var progress = Assert.Single(sender.Delivery!.RequiredDirectMessages
                .OfType<AcceptedQuestProgressMessage>());
            Assert.Equal((1779, 0u),
                ((int)Assert.Single(progress.Quests).QuestId,
                    Assert.Single(progress.Quests).TriggerValue));
        });
    }

    [Fact]
    public async Task Repeated_pickup_refusal_does_not_broadcast_the_piece_twice()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (handler, sender) = Build(
                [Leader, Helper],
                [
                    new GroundPickupResult(GroundPickupStatus.PickedUp, 14400, 7),
                    GroundPickupResult.Refused(GroundPickupStatus.AlreadyTaken),
                ]);

            var request = new GetItemRequest(GameMessageMetadata.Empty, 3);
            await handler.HandleAsync(Dispatch(Leader.ConnectionId), request, CancellationToken.None);
            await handler.HandleAsync(Dispatch(Leader.ConnectionId), request, CancellationToken.None);

            Assert.Single(sender.Deliveries);
            Assert.Single(sender.Deliveries[0].BestEffortTargets);
        });
    }

    [Fact]
    public async Task Replacement_picker_does_not_receive_the_old_runs_pickup()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var replacement = Leader with
            {
                ConnectionId = new NetworkConnectionId("replacement-leader"),
                ConnectionGeneration = 2,
            };
            var (projection, sender, run) = Projection(
                [Leader, Helper], [replacement, Helper]);

            var delivered = await projection.DeliverAsync(
                Leader,
                Picked(run, Leader.CharacterId, [Leader.CharacterId, Helper.CharacterId]),
                CancellationToken.None);

            Assert.False(delivered);
            Assert.Empty(sender.Deliveries);
        });
    }

    [Fact]
    public async Task Departed_picker_is_not_projected_after_the_commit_excludes_them()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (projection, sender, run) = Projection([Leader, Helper], [Leader, Helper]);

            var delivered = await projection.DeliverAsync(
                Leader,
                Picked(run, Leader.CharacterId, [Helper.CharacterId]),
                CancellationToken.None);

            Assert.False(delivered);
            Assert.Empty(sender.Deliveries);
        });
    }

    [Fact]
    public async Task Foreign_run_change_cannot_reuse_the_current_picker_connection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (projection, sender, run) = Projection([Leader, Helper], [Leader, Helper]);
            var foreign = Picked(run, Leader.CharacterId, [Leader.CharacterId, Helper.CharacterId]) with
            {
                Change = Picked(run, Leader.CharacterId, [Leader.CharacterId, Helper.CharacterId])
                    .Change! with { RunId = DungeonRunId.New() },
            };

            var delivered = await projection.DeliverAsync(
                Leader, foreign, CancellationToken.None);

            Assert.False(delivered);
            Assert.Empty(sender.Deliveries);
        });
    }

    private static GroundPickupResult Picked(
        DungeonRunState run,
        CharacterId picker,
        IReadOnlyList<CharacterId> recipients) =>
        new(
            GroundPickupStatus.PickedUp,
            14400,
            7,
            Change: new DungeonPickupChange(
                run.RunId,
                1,
                picker,
                new GroundDropId(run.CurrentCell, 3),
                DungeonPickupKind.Item,
                recipients));

    private static (
        PickupProjectionDriver Projection,
        RecordingSender Sender,
        DungeonRunState Run) Projection(
        IReadOnlyList<SelectedSessionIdentity> frozen,
        IReadOnlyList<SelectedSessionIdentity> current)
    {
        var runs = TestParties.Runs();
        var run = runs.BeginParty(
            frozen.Select((session, index) => new DungeonRunParticipant(
                session.AccountId,
                session.CharacterId,
                checked((byte)index),
                index == 0)).ToArray(),
            Layout(),
            difficulty: 0);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, frozen);
        var sender = new RecordingSender();
        return (
            new PickupProjectionDriver(
                new Sessions(current), runs, run, registry, sender, TimeProvider.System),
            sender,
            run);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task Epic_pickup_sends_ack_removal_then_only_changed_balance_to_the_picker(int added)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var (handler, sender) = Build([Leader, Helper],
                [new GroundPickupResult(GroundPickupStatus.EpicPieceCollected, 10300000, 0,
                    EpicPiece: new(10300000, 3, added, 27))]);
            await handler.HandleAsync(Dispatch(Leader.ConnectionId), new GetItemRequest(GameMessageMetadata.Empty, 3), default);
            var messages = sender.Delivery!.RequiredDirectMessages;
            Assert.Equal(added > 0 ? 3 : 2, messages.Count);
            Assert.True(Assert.IsType<GetItemResponse>(messages[0]).Accepted);
            Assert.Equal(0, Assert.IsType<GroundPickupNotification>(messages[1]).DestinationSlot);
            if (added > 0)
            {
                var epic = Assert.IsType<EpicBookStateMessage>(messages[2]);
                Assert.Equal(1, epic.Mode);
                Assert.Equal(27, Assert.Single(epic.Pieces).Count);
            }
            Assert.IsType<GroundPickupNotification>(Assert.Single(Assert.Single(sender.Delivery.BestEffortTargets).OrderedMessages));
        });
    }

    private static (PickupHandlerDriver Handler, RecordingSender Sender) Build(
        IReadOnlyList<SelectedSessionIdentity> frozen,
        IReadOnlyList<GroundPickupResult> results,
        IReadOnlyList<SelectedSessionIdentity>? current = null)
    {
        var runs = TestParties.Runs();
        var participants = frozen.Select((session, index) => new DungeonRunParticipant(
            session.AccountId,
            session.CharacterId,
            checked((byte)index),
            index == 0)).ToArray();
        var run = runs.BeginParty(participants, Layout(), difficulty: 0);
        var deliveries = new DungeonRunDeliveryRegistry();
        deliveries.Register(run.RunId, frozen);
        var sender = new RecordingSender();
        var projection = new PickupProjectionDriver(
            new Sessions(current ?? frozen), runs, run, deliveries, sender, TimeProvider.System);
        return (
            new PickupHandlerDriver(new Sessions(current ?? frozen),runs,deliveries,results,sender),
            sender);
    }

    private static DungeonLayout Layout() => new(
        144,
        0,
        1,
        1,
        new MazeCell(0, 0),
        new MazeCell(0, 0),
        8153,
        7,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153)},
        5);

    private static SelectedSessionIdentity Session(int id, string connection, long generation) =>
        SelectedSessionIdentity.Create(
            new AccountId(id),
            new CharacterId(id),
            new NetworkConnectionId(connection),
            generation,
            10010);

    private static NetworkDispatchContext Dispatch(NetworkConnectionId connectionId) =>
        new(new NetworkConnectionContext(connectionId, NetworkEndpointKind.Game, 10010));

    // Calls the same owning whole body, with only its write outcome supplied by this projection fixture.
    // Production inbound scope/disposal and failure protection are asserted by Application's real-wire oracle.
    private sealed class PickupHandlerDriver
    {
        private readonly GameGetItemCommandHandler handler;
        internal PickupHandlerDriver(ISelectedSessionDomainService current, IDungeonRunDomainService runSource,
            DungeonRunDeliveryRegistry registry, IReadOnlyList<GroundPickupResult> results, RecordingSender sender)
        {
            var session = current.FindByConnection(Leader.ConnectionId)
                ?? throw new InvalidOperationException("The pickup fixture needs its original leader.");
            var run = runSource.Find(session.CharacterId)!;
            handler = ProtocolAlgorithmFixture.Create<GameGetItemCommandHandler>(current, runSource, registry,
                new Dfo.Server.Protocol.Tests.Support.DungeonProjectionParty(run), new RecordingClearEvaluator(null),
                new PickupResultUnit(runSource, registry, results), new DriverOutbox(sender));
        }
        internal async ValueTask HandleAsync(NetworkDispatchContext context, GetItemRequest input, CancellationToken token)
        {
            var command = new GameGetItemCommand(context, input);
            var execution = new CommandExecutionContext(Guid.NewGuid(), command.GetType(), handler.GetType())
            {
                Request = new CommandRequestContext.ProtocolNonEffect(context, input,
                    new ProtocolFrameMetadata.Game(input.Metadata), () => true),
            };
            _ = await handler.HandleAsync(execution, command, token);
        }
    }
    // This projection fixture already supplied complete settlement outcomes. It now observes
    // that same boundary through the exact existing unit; it does not prove durable settlement.
    private sealed class PickupResultUnit(IDungeonRunDomainService runs, DungeonRunDeliveryRegistry registry,
        IReadOnlyList<GroundPickupResult> results) : ICommandWorkUnits
    {
        private int index;
        private long revision;
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull
        {
            if (declaration.Name != "pick-up-ground-drop" || typeof(T) != typeof(GroundPickupResult)
                || context.Request is not CommandRequestContext.ProtocolNonEffect { Message: GetItemRequest input } request)
                throw new NotSupportedException("Only the pickup projection fixture's settlement is supplied.");
            var actor = registry.Find(runs.Find(Leader.CharacterId)!.RunId)
                .Single(value => value.ConnectionId == request.Dispatch.Connection.ConnectionId);
            var run = runs.Find(actor.CharacterId)!;
            var result = results[index++];
            if (result.Status is GroundPickupStatus.PickedUp or GroundPickupStatus.Banked or GroundPickupStatus.EpicPieceCollected)
                result = result with { Change = new DungeonPickupChange(run.RunId, ++revision, actor.CharacterId,
                    new GroundDropId(run.CurrentCell, input.GroundDropNumber),
                    result.Status is GroundPickupStatus.Banked ? DungeonPickupKind.Gold
                        : result.Status is GroundPickupStatus.EpicPieceCollected ? DungeonPickupKind.EpicPiece : DungeonPickupKind.Item,
                    registry.Find(run.RunId).Select(value => value.CharacterId).ToArray()) };
            return ValueTask.FromResult((T)(object)result);
        }
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
    }
    private sealed class DriverOutbox(IGameMessageSender sender):IGameCommandOutbox
    {
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery batch,CancellationToken ct)=>sender.SendExplicitAsync(id,batch,ct);
        public ValueTask RegisterCmdAsync(NetworkConnectionId id,IGameCmdMessage message,CancellationToken ct)=>sender.SendCmdAsync(id,message,ct);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id,IGameNotiMessage message,CancellationToken ct)=>sender.SendNotiAsync(id,message,ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext connection,IReadOnlyList<IGameServerMessage> messages,CancellationToken ct)=>sender.SendExplicitAsync(connection.ConnectionId,new(messages,[]),ct);
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

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            sessions.SingleOrDefault(value => value.ConnectionId == connectionId);

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            sessions.SingleOrDefault(value => value.CharacterId == characterId);

        public SelectedSessionIdentity? Find(AccountId accountId) =>
            sessions.SingleOrDefault(value => value.AccountId == accountId);

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            sessions.SingleOrDefault(value => value.WireUserId == wireUserId);

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
            throw new NotSupportedException();

        public bool ReleaseExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingClearEvaluator(DungeonClearChange? change)
        : IDungeonClearDomainService
    {
        public (DungeonRunId RunId, long RoomVersion)? LastRequest { get; private set; }

        public ValueTask<DungeonClearChange?> EvaluateAsync(
            AccountId accountId,
            CharacterId characterId,
            DungeonRunId expectedRunId,
            long expectedRoomVersion,
            CancellationToken cancellationToken)
        {
            LastRequest = (expectedRunId, expectedRoomVersion);
            return ValueTask.FromResult<DungeonClearChange?>(change);
        }
    }

    private sealed class ClearedRoom : IMonsterKillDomainService
    {
        public ValueTask<KillIdentification> IdentifyAsync(
            DungeonRunState run, ushort actorSequence, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<bool> IsCurrentRoomClearedAsync(
            DungeonRunState run, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
    }

    private sealed class CompleteSettlement : IDungeonSettlementRuleService
    {
        public ValueTask<DungeonSettlementRules> ForAsync(
            AccountId accountId,
            CharacterId characterId,
            DungeonRunState run,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DungeonSettlementRules(true, QuestMaze: true));
    }

    /// <summary>
    /// A real registration adapter whose every direct message fails to encode where it is registered, with the
    /// exception the old double threw (TASK-25 §6.1); every call goes to the real protocol sender over it.
    /// </summary>
    private sealed class ThrowingSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public ThrowingSender() => outbound.FailAny(outbound.IsDirect, () => new IOException("send failed"));

        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }

    /// <summary>
    /// The explicit deliveries a real registration adapter registered, read from its outbox; every call goes to the
    /// real protocol sender over it (TASK-25). A CMD or NOTI outside an explicit delivery fails where it is registered,
    /// as the old double refused one.
    /// </summary>
    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public RecordingSender() => outbound.RefuseOutsideExplicit();

        public List<GameExplicitMessageDelivery> Deliveries => [.. outbound.GameExplicits];
        public GameExplicitMessageDelivery? Delivery => Deliveries.SingleOrDefault();

        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }
}
