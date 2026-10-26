using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
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
using Dfo.Server.Protocol.Game.EplpRechallenge;
using Dfo.Server.Protocol.Game.QuestProgress;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.DungeonClear;

public sealed class DungeonClearProjectionDeliveryTests
{
    private static readonly SelectedSessionIdentity Leader = Session(1, "leader", 1);
    private static readonly SelectedSessionIdentity Helper = Session(2, "helper", 1);

    [Fact]
    public async Task Cycle_clear_invites_the_native_client_to_submit_after_clear_and_shop()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build([Leader, Helper], [Leader, Helper], cycle: true);
            var change = world.Runs.TryOpenClear(Leader.CharacterId, world.Run.RunId, world.Run.RoomVersion)!;

            await world.Projection.DeliverAsync(Leader, change, default);

            foreach (var session in new[] { Leader, Helper })
                Assert.Equal(new uint[] { 0x001F, 0x0117, 0x0105 },
                    world.Sender.For(session.ConnectionId).Select(message => (uint)message.MessageType));
        });
    }

    [Fact]
    public async Task Ready_cycle_resynchronizes_all_accepted_tasks_before_the_native_check()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var queries = new Queries([new(new(0, 14836, 0), []), new(new(1, 1234, 7), [])]);
            var world = Build([Leader], [Leader], cycle: true, queries);
            var change = world.Runs.TryOpenClear(Leader.CharacterId, world.Run.RunId, world.Run.RoomVersion)!;

            await world.Projection.DeliverAsync(Leader, change, default);

            var frames = world.Sender.For(Leader.ConnectionId);
            Assert.Equal(new uint[] { 0x001F, 0x0117, (uint)ServerMessageType.GameStateSynchronization, 0x0105 },
                frames.Select(x => (uint)x.MessageType));
            var progress = Assert.IsType<AcceptedQuestProgressMessage>(frames[2]);
            Assert.Equal(new[] { (14836, 0u), (1234, 7u) },
                progress.Quests.Select(x => ((int)x.QuestId, x.TriggerValue)));
        });
    }

    [Fact]
    public async Task Progress_that_finishes_after_clear_offers_a_second_native_check_after_the_new_snapshot()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var queries = new Queries([new(new(0, 14836, 1), [])]);
            var world = Build([Leader], [Leader], cycle: true, queries);
            var change = world.Runs.TryOpenClear(Leader.CharacterId, world.Run.RunId, world.Run.RoomVersion)!;
            await world.Projection.DeliverAsync(Leader, change, default);
            Assert.Equal(new uint[] { 0x001F, 0x0117, 0x0105 },
                world.Sender.For(Leader.ConnectionId).Select(x => (uint)x.MessageType));

            queries.Accepted = [new(new(0, 14836, 0), [])];
            var handler = new QuestProgressViewChangedHandler(queries, new Sessions([Leader]), world.Runs);
            var frames = await handler.ComposeAsync(new(Leader.CharacterId, Leader.ConnectionId),
                new(Leader.CharacterId), default);

            Assert.Equal(new uint[] { (uint)ServerMessageType.GameStateSynchronization, 0x0105 },
                frames.Select(x => (uint)x.MessageType));
            Assert.Equal(0u, Assert.Single(Assert.IsType<AcceptedQuestProgressMessage>(frames[0]).Quests).TriggerValue);
        });
    }

    [Theory]
    [InlineData(false, true, 14836, 0)]
    [InlineData(true, false, 14836, 0)]
    [InlineData(true, true, 14836, 1)]
    [InlineData(true, true, 1234, 0)]
    public async Task Progress_outside_a_ready_bound_cycle_clear_does_not_request_submission(
        bool cycle, bool cleared, int questId, int trigger)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var queries = new Queries([new(new(0, questId, trigger), [])]);
            var world = Build([Leader], [Leader], cycle, queries);
            if (cleared) world.Runs.TryOpenClear(Leader.CharacterId, world.Run.RunId, world.Run.RoomVersion);
            var handler = new QuestProgressViewChangedHandler(queries, new Sessions([Leader]), world.Runs);

            var frames = await handler.ComposeAsync(new(Leader.CharacterId, Leader.ConnectionId),
                new(Leader.CharacterId), default);

            Assert.IsType<AcceptedQuestProgressMessage>(Assert.Single(frames));
        });
    }

    [Fact]
    public async Task Leaving_while_the_accepted_snapshot_is_read_suppresses_the_old_clear()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var queries = new Queries([new(new(0, 14836, 0), [])]);
            var world = Build([Leader], [Leader], cycle: true, queries);
            var change = world.Runs.TryOpenClear(Leader.CharacterId, world.Run.RunId, world.Run.RoomVersion)!;
            queries.BeforeRead = () => world.Runs.End(Leader.CharacterId, world.Run.RunId);

            Assert.Equal(0, await world.Projection.DeliverAsync(Leader, change, default));
            Assert.Empty(world.Sender.For(Leader.ConnectionId));
        });
    }

    [Fact]
    public async Task Progress_read_cannot_carry_an_old_run_snapshot_into_town()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var queries = new Queries([new(new(0, 14836, 0), [])]);
            var world = Build([Leader], [Leader], cycle: true, queries);
            world.Runs.TryOpenClear(Leader.CharacterId, world.Run.RunId, world.Run.RoomVersion);
            queries.BeforeRead = () => world.Runs.End(Leader.CharacterId, world.Run.RunId);
            var handler = new QuestProgressViewChangedHandler(queries, new Sessions([Leader]), world.Runs);

            var frames = await handler.ComposeAsync(new(Leader.CharacterId, Leader.ConnectionId),
                new(Leader.CharacterId), default);

            Assert.Empty(frames);
        });
    }

    [Fact]
    public async Task Clear_change_reaches_every_current_frozen_participant_in_order()
    {
        await TestGameplay.RunAsync(async () =>
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
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Participant_who_left_the_run_is_not_notified(bool cycle)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = Build([Leader, Helper], [Leader, Helper], cycle);
            Assert.True(world.Runs.End(Helper.CharacterId, world.Run.RunId));
            var change = world.Runs.TryOpenClear(
                Leader.CharacterId, world.Run.RunId, world.Run.RoomVersion)!;

            var delivered = await world.Projection.DeliverAsync(Leader, change, default);

            Assert.Equal(1, delivered);
            Assert.Empty(world.Sender.For(Helper.ConnectionId));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_connection_cannot_receive_the_old_runs_clear(bool cycle)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var replacement = Helper with
            {
                ConnectionId = new NetworkConnectionId("replacement"),
                ConnectionGeneration = 2,
            };
            var world = Build([Leader, Helper], [Leader, replacement], cycle);
            var change = world.Runs.TryOpenClear(
                Leader.CharacterId, world.Run.RunId, world.Run.RoomVersion)!;

            var delivered = await world.Projection.DeliverAsync(Leader, change, default);

            Assert.Equal(1, delivered);
            Assert.Empty(world.Sender.For(Helper.ConnectionId));
            Assert.Empty(world.Sender.For(replacement.ConnectionId));
        });
    }

    private static TestWorld Build(
        IReadOnlyList<SelectedSessionIdentity> frozen,
        IReadOnlyList<SelectedSessionIdentity> current,
        bool cycle = false,
        IQuestQueryDomainService? queries = null)
    {
        var runs = new DungeonRunDomainService();
        var run = runs.BeginParty(
            frozen.Select((session, index) => new DungeonRunParticipant(
                session.AccountId,
                session.CharacterId,
                checked((byte)index),
                IsLeader: index == 0)).ToArray(),
            cycle ? Layout() with { Purpose = new(DungeonRunPurposeKind.Cycle, 14836, null) } : Layout(),
            difficulty: 0);
        var registry = new DungeonRunDeliveryRegistry();
        registry.Register(run.RunId, frozen);
        var sender = new RecordingSender();
        return new TestWorld(
            global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.DungeonClear.DungeonClearProjectionDriver>(new Sessions(current), runs, registry, sender, queries),
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
        DungeonClearProjectionDriver Projection,
        DungeonRunDomainService Runs,
        DungeonRunState Run,
        RecordingSender Sender);

    private sealed class Queries(IReadOnlyList<AcceptedQuestState> accepted) : IQuestQueryDomainService
    {
        internal IReadOnlyList<AcceptedQuestState> Accepted { get; set; } = accepted;
        internal Action? BeforeRead { get; set; }
        public ValueTask<IReadOnlyList<AcceptedQuestState>> ReadAcceptedAsync(AccountId account,
            CharacterId character, CancellationToken ct)
        {
            BeforeRead?.Invoke();
            return ValueTask.FromResult(Accepted);
        }
        public ValueTask<QuestStatus> QueryAsync(AccountId account, CharacterId character, int id, CancellationToken ct) =>
            throw new NotSupportedException();
        public ValueTask<AvailableQuestEntryState> ReadAvailableAsync(AccountId account, CharacterId character, CancellationToken ct) =>
            throw new NotSupportedException();
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

    /// <summary>
    /// What was registered for each connection, read from the outbox of a real registration adapter; every call goes
    /// to the real protocol sender over it (TASK-25). A CMD or NOTI outside an explicit delivery fails where it is
    /// registered, as the old double refused one.
    /// </summary>
    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public RecordingSender() => outbound.RefuseOutsideExplicit();

        public IReadOnlyList<IGameServerMessage> For(NetworkConnectionId connectionId) =>
            [.. outbound.MessagesFor(connectionId).Cast<IGameServerMessage>()];

        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }
}
