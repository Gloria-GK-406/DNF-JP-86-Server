using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging.Abstractions;
using EnteredDungeon = Dfo.Server.Abstractions.World.DungeonEntry;

namespace Dfo.Server.Protocol.Tests.Game.DungeonEntry;

public sealed class PartyMoveMapHandlerTests
{
    [Fact]
    public async Task The_leaders_room_change_is_sent_to_every_frozen_current_generation()
    {
        var sender = new Sender();
        var visits = new Visits();
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(visits.RunId, [Leader, Member]);
        var handler = new MoveMapHandler(
            new Sessions(Leader, Member),
            new Runs(visits.Start),
            visits,
            registry,
            sender,
            NullLogger<MoveMapHandler>.Instance);

        await handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        var leaderRoom = Assert.IsType<StartMapNotification>(
            Assert.Single(sender.Messages(Leader.ConnectionId)));
        var memberRoom = Assert.IsType<StartMapNotification>(
            Assert.Single(sender.Messages(Member.ConnectionId)));
        Assert.Equal(0, leaderRoom.Run.Participant!.Slot);
        Assert.Equal(1, memberRoom.Run.Participant!.Slot);
        Assert.Equal(1, visits.Moves);
    }

    [Fact]
    public async Task A_replacement_connection_does_not_inherit_the_old_runs_room_change()
    {
        var replacement = Member with
        {
            ConnectionId = new NetworkConnectionId("member-replacement"),
            ConnectionGeneration = Member.ConnectionGeneration + 1,
        };
        var sender = new Sender();
        var visits = new Visits();
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(visits.RunId, [Leader, Member]);
        var handler = new MoveMapHandler(
            new Sessions(Leader, replacement),
            new Runs(visits.Start),
            visits,
            registry,
            sender,
            NullLogger<MoveMapHandler>.Instance);

        await handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        Assert.Single(sender.Messages(Leader.ConnectionId));
        Assert.Empty(sender.Messages(Member.ConnectionId));
        Assert.Empty(sender.Messages(replacement.ConnectionId));
    }

    [Fact]
    public async Task A_nonleader_cannot_advance_the_shared_room()
    {
        var sender = new Sender();
        var visits = new Visits();
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(visits.RunId, [Leader, Member]);
        var handler = new MoveMapHandler(
            new Sessions(Leader, Member),
            new Runs(visits.StartFor(Member.CharacterId)),
            visits,
            registry,
            sender,
            NullLogger<MoveMapHandler>.Instance);

        await handler.HandleAsync(Dispatch(Member), Request(), CancellationToken.None);

        Assert.Empty(sender.Messages(Leader.ConnectionId));
        Assert.Empty(sender.Messages(Member.ConnectionId));
        Assert.Equal(1, visits.Moves);
    }

    [Fact]
    public async Task A_replaced_requester_cannot_commit_a_room_change()
    {
        var replacement = Leader with
        {
            ConnectionId = new NetworkConnectionId("leader-replacement"),
            ConnectionGeneration = Leader.ConnectionGeneration + 1,
        };
        var sessions = new Sessions(Leader, Member);
        sessions.Replace(Leader.CharacterId, replacement);
        var sender = new Sender();
        var visits = new Visits();
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(visits.RunId, [Leader, Member]);
        var handler = new MoveMapHandler(
            sessions,
            new Runs(visits.Start),
            visits,
            registry,
            sender,
            NullLogger<MoveMapHandler>.Instance);

        await handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        Assert.Equal(0, visits.Moves);
        Assert.Empty(sender.Messages(Leader.ConnectionId));
        Assert.Empty(sender.Messages(replacement.ConnectionId));
        Assert.Empty(sender.Messages(Member.ConnectionId));
    }

    [Fact]
    public async Task If_the_requester_is_replaced_after_commit_the_room_reaches_remaining_members()
    {
        var replacement = Leader with
        {
            ConnectionId = new NetworkConnectionId("leader-replacement"),
            ConnectionGeneration = Leader.ConnectionGeneration + 1,
        };
        var sessions = new Sessions(Leader, Member);
        var sender = new Sender();
        var visits = new Visits(() => sessions.Replace(Leader.CharacterId, replacement));
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(visits.RunId, [Leader, Member]);
        var handler = new MoveMapHandler(
            sessions,
            new Runs(visits.Start),
            visits,
            registry,
            sender,
            NullLogger<MoveMapHandler>.Instance);

        await handler.HandleAsync(Dispatch(Leader), Request(), CancellationToken.None);

        Assert.Equal(1, visits.Moves);
        Assert.Empty(sender.Messages(Leader.ConnectionId));
        Assert.Empty(sender.Messages(replacement.ConnectionId));
        Assert.IsType<StartMapNotification>(Assert.Single(sender.Messages(Member.ConnectionId)));
    }

    private static MoveMapRequest Request() =>
        new(GameMessageMetadata.Empty, 1, 0, 10, 20);

    private static NetworkDispatchContext Dispatch(SelectedSessionIdentity session) =>
        new(new NetworkConnectionContext(
            session.ConnectionId, NetworkEndpointKind.Game, session.ListenerPort));

    private static readonly SelectedSessionIdentity Leader = SelectedSessionIdentity.Create(
        new AccountId(1), new CharacterId(1001), new NetworkConnectionId("leader"), 11, 10010);
    private static readonly SelectedSessionIdentity Member = SelectedSessionIdentity.Create(
        new AccountId(2), new CharacterId(1002), new NetworkConnectionId("member"), 21, 10011);

    private sealed class Visits : IDungeonVisitDomainService
    {
        private readonly DungeonLayout layout;
        private readonly IReadOnlyList<DungeonRunParticipant> participants;
        private readonly Action? afterMove;

        public Visits(Action? afterMove = null)
        {
            this.afterMove = afterMove;
            RunId = DungeonRunId.New();
            layout = new DungeonLayout(
                10000,
                0,
                2,
                1,
                new MazeCell(0, 0),
                new MazeCell(1, 0),
                61000,
                123,
                new Dictionary<MazeCell, int>
                {
                    [new MazeCell(0, 0)] = 61000,
                    [new MazeCell(1, 0)] = 61001,
                });
            participants =
            [
                new DungeonRunParticipant(Leader.AccountId, Leader.CharacterId, 0, true),
                new DungeonRunParticipant(Member.AccountId, Member.CharacterId, 1, false),
            ];
            Start = StartFor(Leader.CharacterId);
        }

        public DungeonRunId RunId { get; }
        public DungeonRunState Start { get; }
        public int Moves { get; private set; }

        public DungeonRunState StartFor(CharacterId characterId) => new(
            layout,
            layout.StartCell,
            layout.StartRoomMapId,
            new HashSet<MazeCell> { layout.StartCell },
            DateTimeOffset.UnixEpoch,
            RunId: RunId,
            Participant: participants.Single(value => value.CharacterId == characterId),
            Participants: participants,
            CurrentCellState: DungeonCellSeed.CompleteEmpty(
                layout.StartCell, layout.StartRoomMapId).Snapshot());

        public bool DefersSelectionState(byte characterLevel) => false;
        public ValueTask<EnteredDungeon?> EnterAsync(CharacterId characterId, int dungeonId, byte difficulty, DungeonEntryFacts facts, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId characterId, DungeonRunState current, MazeCell target, CancellationToken cancellationToken)
        {
            Moves++;
            if (characterId != Leader.CharacterId)
                return ValueTask.FromResult<DungeonRoomVisit?>(null);
            var moved = current with
            {
                CurrentCell = target,
                CurrentRoomMapId = 61001,
                RoomVersion = current.RoomVersion + 1,
                CurrentCellState = DungeonCellSeed.CompleteEmpty(target, 61001).Snapshot(),
            };
            this.afterMove?.Invoke();
            return ValueTask.FromResult<DungeonRoomVisit?>(
                new DungeonRoomVisit(moved, true, [], [], 1));
        }
    }

    private sealed class Runs(DungeonRunState state) : IDungeonRunDomainService
    {
        private readonly IReadOnlyDictionary<CharacterId, DungeonRunState> states =
            state.Participants.ToDictionary(
                participant => participant.CharacterId,
                participant => state with { Participant = participant });

        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, byte difficulty) => throw new NotSupportedException();
        public DungeonRunState? Find(CharacterId characterId) => states.GetValueOrDefault(characterId);
        public DungeonRunState? TryMove(CharacterId characterId, MazeCell target) => throw new NotSupportedException();
        public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber) => throw new NotSupportedException();
        public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence) => throw new NotSupportedException();
        public DungeonRunState? TrySettle(CharacterId characterId) => throw new NotSupportedException();
        public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(CharacterId characterId, int declaredDropCount, ushort actorSequence, IReadOnlyList<RolledDrop> rolled) => throw new NotSupportedException();
        public DungeonRunState? FixClearRewardCards(CharacterId characterId, ClearRewardCardFace free, ClearRewardCardFace paid, int? paidFlipCost) => throw new NotSupportedException();
        public ClearRewardCardSlot? TryClaimClearRewardCard(CharacterId characterId, ClearRewardCardKind kind, int index) => throw new NotSupportedException();
        public bool End(CharacterId characterId) => throw new NotSupportedException();
    }

    private sealed class Sessions : ISelectedSessionDomainService
    {
        private readonly List<SelectedSessionIdentity> connections;
        private readonly Dictionary<CharacterId, SelectedSessionIdentity> currentByCharacter;

        public Sessions(params SelectedSessionIdentity[] sessions)
        {
            connections = [.. sessions];
            currentByCharacter = sessions.ToDictionary(value => value.CharacterId);
        }

        public void Replace(CharacterId characterId, SelectedSessionIdentity replacement)
        {
            currentByCharacter[characterId] = replacement;
            connections.Add(replacement);
        }

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => connections.SingleOrDefault(value => value.ConnectionId == connectionId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => currentByCharacter.GetValueOrDefault(characterId);
        public SelectedSessionIdentity? Find(AccountId accountId) => currentByCharacter.Values.SingleOrDefault(value => value.AccountId == accountId);
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => currentByCharacter.Values.SingleOrDefault(value => value.WireUserId == wireUserId);
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class Sender : IGameMessageSender
    {
        private readonly Dictionary<NetworkConnectionId, List<IGameServerMessage>> sent = [];
        public IReadOnlyList<IGameServerMessage> Messages(NetworkConnectionId connectionId) => sent.TryGetValue(connectionId, out var messages) ? messages : [];
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken)
        {
            Add(requiredDirectConnectionId, delivery.RequiredDirectMessages);
            foreach (var target in delivery.BestEffortTargets)
                Add(target.ConnectionId, target.OrderedMessages);
            return ValueTask.CompletedTask;
        }
        private void Add(NetworkConnectionId connectionId, IReadOnlyList<IGameServerMessage> messages)
        {
            if (!sent.TryGetValue(connectionId, out var destination))
            {
                destination = [];
                sent.Add(connectionId, destination);
            }
            destination.AddRange(messages);
        }
    }
}
