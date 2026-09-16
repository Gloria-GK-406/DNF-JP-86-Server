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
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.GroundPickup;

public sealed class GetItemHandlerTests
{
    private static readonly SelectedSessionIdentity Leader = Session(1, "leader", 1);
    private static readonly SelectedSessionIdentity Helper = Session(2, "helper", 1);

    [Fact]
    public async Task Successful_pickup_removes_the_world_piece_for_every_frozen_participant()
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
    }

    [Fact]
    public async Task Successful_task_item_pickup_projects_the_complete_carried_quest_progress()
    {
        var (handler, sender) = Build(
            [Leader, Helper],
            [new GroundPickupResult(
                GroundPickupStatus.PickedUp,
                10149163,
                7,
                QuestProgress: new SeekingQuestProgressSynchronization(
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
    }

    [Fact]
    public async Task Replacement_connection_does_not_receive_the_old_runs_pickup()
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
    }

    [Fact]
    public async Task Repeated_pickup_refusal_does_not_broadcast_the_piece_twice()
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
    }

    [Fact]
    public async Task Replacement_picker_does_not_receive_the_old_runs_pickup()
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
    }

    [Fact]
    public async Task Departed_picker_is_not_projected_after_the_commit_excludes_them()
    {
        var (projection, sender, run) = Projection([Leader, Helper], [Leader, Helper]);

        var delivered = await projection.DeliverAsync(
            Leader,
            Picked(run, Leader.CharacterId, [Helper.CharacterId]),
            CancellationToken.None);

        Assert.False(delivered);
        Assert.Empty(sender.Deliveries);
    }

    [Fact]
    public async Task Foreign_run_change_cannot_reuse_the_current_picker_connection()
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
        DungeonPickupProjectionDelivery Projection,
        RecordingSender Sender,
        DungeonRunState Run) Projection(
        IReadOnlyList<SelectedSessionIdentity> frozen,
        IReadOnlyList<SelectedSessionIdentity> current)
    {
        var runs = new DungeonRunDomainService();
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
            new DungeonPickupProjectionDelivery(
                new Sessions(current), registry, sender, TimeProvider.System),
            sender,
            run);
    }

    private static (GetItemHandler Handler, RecordingSender Sender) Build(
        IReadOnlyList<SelectedSessionIdentity> frozen,
        IReadOnlyList<GroundPickupResult> results,
        IReadOnlyList<SelectedSessionIdentity>? current = null)
    {
        var runs = new DungeonRunDomainService();
        var participants = frozen.Select((session, index) => new DungeonRunParticipant(
            session.AccountId,
            session.CharacterId,
            checked((byte)index),
            index == 0)).ToArray();
        var run = runs.BeginParty(participants, Layout(), difficulty: 0);
        var deliveries = new DungeonRunDeliveryRegistry();
        deliveries.Register(run.RunId, frozen);
        var sender = new RecordingSender();
        var projection = new DungeonPickupProjectionDelivery(
            new Sessions(current ?? frozen), deliveries, sender, TimeProvider.System);
        return (
            new GetItemHandler(
                new Sessions(current ?? frozen),
                runs,
                new PickupBus(results, run, frozen.Select(value => value.CharacterId).ToArray()),
                projection,
                NullLogger<GetItemHandler>.Instance),
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
        new Dictionary<MazeCell, int> { [new MazeCell(0, 0)] = 8153 },
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

    private sealed class PickupBus(
        IReadOnlyList<GroundPickupResult> results,
        DungeonRunState run,
        IReadOnlyList<CharacterId> recipients) : ICommandBus
    {
        private int index;
        private long revision;

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default) where TResult : notnull
        {
            var pickup = Assert.IsType<PickUpGroundDropCommand>(command);
            var result = results[index++];
            if (result.Status is GroundPickupStatus.PickedUp or GroundPickupStatus.Banked)
            {
                result = result with
                {
                    Change = new DungeonPickupChange(
                        run.RunId,
                        ++revision,
                        pickup.CharacterId,
                        new GroundDropId(run.CurrentCell, pickup.GroundDropNumber),
                        result.Status is GroundPickupStatus.Banked
                            ? DungeonPickupKind.Gold
                            : DungeonPickupKind.Item,
                        recipients),
                };
            }
            return ValueTask.FromResult((TResult)(object)result);
        }
    }

    private sealed class Sessions(IReadOnlyList<SelectedSessionIdentity> sessions)
        : ISelectedSessionDomainService
    {
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

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<GameExplicitMessageDelivery> Deliveries { get; } = [];
        public GameExplicitMessageDelivery? Delivery => Deliveries.SingleOrDefault();

        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            Deliveries.Add(delivery);
            return ValueTask.CompletedTask;
        }
    }
}
