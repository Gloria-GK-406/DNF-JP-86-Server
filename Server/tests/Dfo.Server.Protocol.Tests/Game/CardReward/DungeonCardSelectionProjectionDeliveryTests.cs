using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.Dungeons;

namespace Dfo.Server.Protocol.Tests.Game.CardReward;

public sealed class DungeonCardSelectionProjectionDeliveryTests
{
    private static readonly SelectedSessionIdentity Leader = Session(1, "leader", 1);
    private static readonly SelectedSessionIdentity Helper = Session(2, "helper", 1);

    [Fact]
    public async Task Bonus_inventory_projection_is_private_even_when_the_card_board_is_shared()
    {
        var world = Build([Leader, Helper]);
        var change = new DungeonCardSelectionChange(world.Run.RunId, 8, Leader.CharacterId,
            [new(ClearRewardCardKind.Free, 0, Leader.CharacterId, 0)],
            [Leader.CharacterId, Helper.CharacterId]);
        var result = new SelectCardResult(SelectCardStatus.Flipped, ClearRewardCardKind.Free,
            0, [], StaticChange: change)
        {
            BlackDiamondDelivered = new(Dfo.Server.Data.Inventory.InventoryContainerType.Main,
                [], DateTimeOffset.UnixEpoch),
        };
        await world.Projection.DeliverAsync(Leader, result, DateTimeOffset.UnixEpoch, default);
        Assert.Single(world.Sender.For(Leader.ConnectionId).OfType<InventoryDeltaMessage>());
        Assert.Empty(world.Sender.For(Helper.ConnectionId).OfType<InventoryDeltaMessage>());
        Assert.Single(world.Sender.For(Helper.ConnectionId).OfType<SelectCardResponse>());
    }

    [Fact]
    public async Task First_party_confirmation_does_not_reveal_a_partial_board()
    {
        var world = Build([Leader, Helper]);
        var change = new DungeonCardSelectionChange(
            world.Run.RunId,
            Revision: 7,
            Leader.CharacterId,
            [],
            [Leader.CharacterId, Helper.CharacterId]);

        await world.Projection.DeliverAsync(
            Leader,
            new SelectCardResult(
                SelectCardStatus.Flipped,
                ClearRewardCardKind.Free,
                0,
                [],
                StaticChange: change),
            DateTimeOffset.UnixEpoch,
            default);

        Assert.IsType<Dfo.Server.Protocol.Game.StateSynchronization.PremiumServiceStateMessage>(Assert.Single(world.Sender.For(Leader.ConnectionId)));
        Assert.Empty(world.Sender.For(Helper.ConnectionId));
    }

    [Fact]
    public async Task Shared_snapshot_reaches_both_current_generations()
    {
        var world = Build([Leader, Helper]);
        var change = new DungeonCardSelectionChange(
            world.Run.RunId,
            Revision: 8,
            Leader.CharacterId,
            [
                new DungeonCardSelection(
                    ClearRewardCardKind.Free, 0, Leader.CharacterId, Seat: 0),
                new DungeonCardSelection(
                    ClearRewardCardKind.Free, 1, Helper.CharacterId, Seat: 1),
            ],
            [Leader.CharacterId, Helper.CharacterId]);
        var result = new SelectCardResult(
            SelectCardStatus.Flipped,
            ClearRewardCardKind.Free,
            0,
            [],
            StaticChange: change);

        var delivered = await world.Projection.DeliverAsync(
            Leader, result, DateTimeOffset.UnixEpoch, default);

        Assert.Equal(2, delivered);
        Assert.Equal(
            change.Selections,
            Assert.IsType<SelectCardResponse>(Assert.Single(
                world.Sender.For(Leader.ConnectionId).OfType<SelectCardResponse>())).Selections);
        Assert.IsType<Dfo.Server.Protocol.Game.StateSynchronization.PremiumServiceStateMessage>(world.Sender.For(Leader.ConnectionId).Last());
        Assert.Equal(
            change.Selections,
            Assert.IsType<SelectCardResponse>(Assert.Single(
                world.Sender.For(Helper.ConnectionId))).Selections);
    }

    [Fact]
    public async Task Departed_and_replacement_generations_receive_no_old_card_snapshot()
    {
        var replacement = Helper with
        {
            ConnectionId = new NetworkConnectionId("replacement"),
            ConnectionGeneration = 2,
        };
        var world = Build([Leader, replacement]);
        Assert.True(world.Runs.End(Helper.CharacterId, world.Run.RunId));
        var change = new DungeonCardSelectionChange(
            world.Run.RunId,
            Revision: 9,
            Leader.CharacterId,
            [new DungeonCardSelection(
                ClearRewardCardKind.Free, 0, Leader.CharacterId, Seat: 0)],
            [Leader.CharacterId, Helper.CharacterId]);

        await world.Projection.DeliverAsync(
            Leader,
            new SelectCardResult(
                SelectCardStatus.Flipped,
                ClearRewardCardKind.Free,
                0,
                [],
                StaticChange: change),
            DateTimeOffset.UnixEpoch,
            default);

        Assert.Equal(2, world.Sender.For(Leader.ConnectionId).Count);
        Assert.Empty(world.Sender.For(Helper.ConnectionId));
        Assert.Empty(world.Sender.For(replacement.ConnectionId));
    }

    private static World Build(IReadOnlyList<SelectedSessionIdentity> current)
    {
        var runs = new DungeonRunDomainService();
        var run = runs.BeginParty(
            [
                new DungeonRunParticipant(
                    Leader.AccountId, Leader.CharacterId, Slot: 0, IsLeader: true),
                new DungeonRunParticipant(
                    Helper.AccountId, Helper.CharacterId, Slot: 1, IsLeader: false),
            ],
            new DungeonLayout(
                144, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0), 8153, 7,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153)}),
            difficulty: 0);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, [Leader, Helper]);
        var sessions = new Sessions(current);
        var sender = new RecordingSender();
        return new World(
            new DungeonCardSelectionProjectionDelivery(sessions, runs, registry, sender),
            runs,
            run,
            sender);
    }

    private static SelectedSessionIdentity Session(int id, string connection, long generation) =>
        SelectedSessionIdentity.Create(
            new AccountId(id),
            new CharacterId(id),
            new NetworkConnectionId(connection),
            generation,
            10010);

    private sealed record World(
        DungeonCardSelectionProjectionDelivery Projection,
        DungeonRunDomainService Runs,
        DungeonRunState Run,
        RecordingSender Sender);

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
            this.sent.TryGetValue(connectionId, out var frames) ? frames : [];

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
            this.Add(requiredDirectConnectionId, delivery.RequiredDirectMessages);
            foreach (var target in delivery.BestEffortTargets)
                this.Add(target.ConnectionId, target.OrderedMessages);
            return ValueTask.CompletedTask;
        }

        private void Add(
            NetworkConnectionId connectionId,
            IReadOnlyList<IGameServerMessage> frames)
        {
            if (!this.sent.TryGetValue(connectionId, out var received))
                this.sent[connectionId] = received = [];
            received.AddRange(frames);
        }
    }
}
