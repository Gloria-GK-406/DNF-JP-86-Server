using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.Dungeons;

namespace Dfo.Server.Protocol.Tests.Game.DungeonClear;

public sealed class DungeonClearProjectionDeliveryTests
{
    private static readonly SelectedSessionIdentity Leader = Session(1, "leader", 1);
    private static readonly SelectedSessionIdentity Helper = Session(2, "helper", 1);

    [Fact]
    public async Task Clear_change_reaches_every_current_frozen_participant_in_order()
    {
        var world = Build([Leader, Helper], [Leader, Helper]);
        var change = world.Runs.TryOpenClear(
            Leader.CharacterId, world.Run.RunId, world.Run.RoomVersion)!;

        var delivered = await world.Projection.DeliverAsync(Leader, change, default);

        Assert.Equal(2, delivered);
        Assert.Collection(
            world.Sender.For(Leader.ConnectionId),
            message => Assert.IsType<EnableClearDungeonNotification>(message),
            message => Assert.IsType<SecretShopNpcNotification>(message));
        Assert.Collection(
            world.Sender.For(Helper.ConnectionId),
            message => Assert.IsType<EnableClearDungeonNotification>(message),
            message => Assert.IsType<SecretShopNpcNotification>(message));
    }

    [Fact]
    public async Task Participant_who_left_the_run_is_not_notified()
    {
        var world = Build([Leader, Helper], [Leader, Helper]);
        Assert.True(world.Runs.End(Helper.CharacterId, world.Run.RunId));
        var change = world.Runs.TryOpenClear(
            Leader.CharacterId, world.Run.RunId, world.Run.RoomVersion)!;

        var delivered = await world.Projection.DeliverAsync(Leader, change, default);

        Assert.Equal(1, delivered);
        Assert.Empty(world.Sender.For(Helper.ConnectionId));
    }

    [Fact]
    public async Task Replacement_connection_cannot_receive_the_old_runs_clear()
    {
        var replacement = Helper with
        {
            ConnectionId = new NetworkConnectionId("replacement"),
            ConnectionGeneration = 2,
        };
        var world = Build([Leader, Helper], [Leader, replacement]);
        var change = world.Runs.TryOpenClear(
            Leader.CharacterId, world.Run.RunId, world.Run.RoomVersion)!;

        var delivered = await world.Projection.DeliverAsync(Leader, change, default);

        Assert.Equal(1, delivered);
        Assert.Empty(world.Sender.For(Helper.ConnectionId));
        Assert.Empty(world.Sender.For(replacement.ConnectionId));
    }

    private static TestWorld Build(
        IReadOnlyList<SelectedSessionIdentity> frozen,
        IReadOnlyList<SelectedSessionIdentity> current)
    {
        var runs = new DungeonRunDomainService();
        var run = runs.BeginParty(
            frozen.Select((session, index) => new DungeonRunParticipant(
                session.AccountId,
                session.CharacterId,
                checked((byte)index),
                IsLeader: index == 0)).ToArray(),
            Layout(),
            difficulty: 0);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, frozen);
        var sender = new RecordingSender();
        return new TestWorld(
            new DungeonClearProjectionDelivery(new Sessions(current), runs, registry, sender),
            runs,
            run,
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
        new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153)});

    private static SelectedSessionIdentity Session(int id, string connection, long generation) =>
        SelectedSessionIdentity.Create(
            new AccountId(id),
            new CharacterId(id),
            new NetworkConnectionId(connection),
            generation,
            10010);

    private sealed record TestWorld(
        DungeonClearProjectionDelivery Projection,
        DungeonRunDomainService Runs,
        DungeonRunState Run,
        RecordingSender Sender);

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
