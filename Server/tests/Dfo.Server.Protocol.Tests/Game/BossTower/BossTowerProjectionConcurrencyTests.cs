using Dfo.Server.Abstractions.DomainEvents;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Parties;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.BossTowerContinue;

using Dfo.Server.Protocol.Game.BossTowerReward;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.BossTower;

public sealed partial class BossTowerProjectionConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Cancellation_after_tower_commit_cannot_skip_the_direct_party_departure()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1);
            var second = Session(2);
            var sessions = new Sessions(first, second);
            var parties = new PartyService(
                new PartyStatus(), new PartyModeRuleService(), TimeProvider.System,
                new NoPartyEndpoints());
            var firstParty = PartyCharacter(first);
            var secondParty = PartyCharacter(second);
            Assert.True(parties.RequestInvite(firstParty, secondParty,
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(secondParty, firstParty).Succeeded);
            var tower = new TowerDomain(PartyRun(Guid.NewGuid(), first, second));
            using var request = new CancellationTokenSource();
            var commands = new CoordinatorCommands(tower, parties, request);
            var sender = new Sender();
            var closer = new Closer();
            var town = new NoTown();
            var delivery = Delivery(tower, sessions, commands, sender, closer);
            var coordinator = new BossTowerPartyCoordinator(
                tower,
                parties,
                sessions,
                new SelectedSessionTransitionGate(),
                new BossTowerProjectionGate(),
                commands,
                Notifications(parties, sessions, town),
                delivery,
                closer,
                town,
                NullLogger<BossTowerPartyCoordinator>.Instance);

            var result = await coordinator.TryLeaveAsync(
                first, allowVirtual: false, departureResponse: null, request.Token);

            Assert.True(request.IsCancellationRequested);
            Assert.True(result.Handled);
            Assert.True(result.Result.Succeeded);
            Assert.False(commands.PartyToken.CanBeCanceled);
            var remainingParty = parties.FindParty(secondParty);
            Assert.Equal(second.WireUserId, remainingParty.LeaderWireUserId);
            Assert.Equal(second.CharacterId, Assert.Single(remainingParty.Members).CharacterId);
            var remainingTower = Assert.IsType<BossTowerSnapshot>(tower.Find(second));
            Assert.Null(tower.Find(first));
            Assert.Equal(second, Assert.Single(remainingTower.Members).Session);
            Assert.True(remainingTower.Members[0].IsLeader);
        });
    }

    [Fact]
    public async Task Virtual_give_up_leaves_only_the_tower_without_dispatching_a_party_write()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = Session(1);
            var sessions = new Sessions(session);
            var parties = new PartyService(
                new PartyStatus(), new PartyModeRuleService(), TimeProvider.System,
                new NoPartyEndpoints());
            var tower = new TowerDomain(PartyRun(Guid.NewGuid(), session));
            using var request = new CancellationTokenSource();
            var commands = new CoordinatorCommands(tower, parties, request);
            var closer = new Closer();
            var town = new NoTown();
            var coordinator = new BossTowerPartyCoordinator(
                tower, parties, sessions, new SelectedSessionTransitionGate(),
                new BossTowerProjectionGate(), commands,
                Notifications(parties, sessions, town),
                Delivery(tower, sessions, commands, new Sender(), closer),
                closer, town, NullLogger<BossTowerPartyCoordinator>.Instance);

            var result = await coordinator.TryLeaveAsync(
                session, allowVirtual: true, new GiveUpGameResponse(), request.Token);

            Assert.True(result.Handled);
            Assert.True(result.Result.Succeeded);
            Assert.Equal(0, commands.PartyCalls);
            Assert.Null(tower.Find(session));
            Assert.True(parties.FindParty(PartyCharacter(session)).IsVirtual);
        });
    }

    [Fact]
    public async Task External_party_leader_departure_synchronizes_the_partial_tower_cohort()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var external = Session(1);
            var successor = Session(2);
            var towerLeader = Session(3);
            var sessions = new Sessions(external, successor, towerLeader);
            var parties = new PartyService(
                new PartyStatus(), new PartyModeRuleService(), TimeProvider.System,
                new NoPartyEndpoints());
            Assert.True(parties.RequestInvite(PartyCharacter(external), PartyCharacter(successor),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(successor), PartyCharacter(external)).Succeeded);
            Assert.True(parties.RequestInvite(PartyCharacter(external), PartyCharacter(towerLeader),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(towerLeader), PartyCharacter(external)).Succeeded);
            var initial = PartyRun(Guid.NewGuid(), successor, towerLeader) with
            {
                Members =
                [
                    new TowerMember(successor, 0, false, false),
                    new TowerMember(towerLeader, 1, true, false),
                ],
            };
            var tower = new TowerDomain(initial);
            using var request = new CancellationTokenSource();
            var commands = new CoordinatorCommands(tower, parties, request);
            var closer = new Closer();
            var town = new NoTown();
            var coordinator = new BossTowerPartyCoordinator(
                tower, parties, sessions, new SelectedSessionTransitionGate(),
                new BossTowerProjectionGate(), commands,
                Notifications(parties, sessions, town),
                Delivery(tower, sessions, commands, new Sender(), closer),
                closer, town, NullLogger<BossTowerPartyCoordinator>.Instance);

            var result = await coordinator.TryLeaveAsync(
                external, allowVirtual: false, departureResponse: null, CancellationToken.None);

            Assert.True(result.Result.Succeeded);
            Assert.Equal(1, commands.SynchronizeCalls);
            Assert.Null(tower.Find(external));
            var current = Assert.IsType<BossTowerSnapshot>(tower.Find(successor));
            Assert.Equal(2, current.Members.Length);
            Assert.Equal(successor, current.Members.Single(member => member.IsLeader).Session);
            var currentParty = parties.FindParty(PartyCharacter(successor));
            Assert.Equal(successor.WireUserId, currentParty.LeaderWireUserId);
            Assert.DoesNotContain(currentParty.Members,
                member => member.CharacterId == external.CharacterId);
        });
    }

    [Fact]
    public async Task Party_wrapper_failure_after_its_inner_write_isolated_sessions_after_gates_release()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1);
            var second = Session(2);
            var sessions = new Sessions(first, second);
            var parties = new PartyService(
                new PartyStatus(), new PartyModeRuleService(), TimeProvider.System,
                new NoPartyEndpoints());
            Assert.True(parties.RequestInvite(PartyCharacter(first), PartyCharacter(second),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(second), PartyCharacter(first)).Succeeded);
            var tower = new TowerDomain(PartyRun(Guid.NewGuid(), first, second));
            using var request = new CancellationTokenSource();
            var commands = new CoordinatorCommands(
                tower, parties, request, throwAfterParty: true);
            var gate = new SelectedSessionTransitionGate();
            var closer = new ReentrantCloser(gate, first.AccountId);
            var town = new NoTown();
            var coordinator = new BossTowerPartyCoordinator(
                tower, parties, sessions, gate, new BossTowerProjectionGate(), commands,
                Notifications(parties, sessions, town),
                Delivery(tower, sessions, commands, new Sender(), closer),
                closer, town, NullLogger<BossTowerPartyCoordinator>.Instance);

            var result = await coordinator.TryLeaveAsync(
                    first, allowVirtual: false, departureResponse: null, CancellationToken.None)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(result.Handled);
            Assert.False(result.Result.Succeeded);
            Assert.Null(tower.Find(first));
            Assert.DoesNotContain(parties.FindParty(PartyCharacter(second)).Members,
                member => member.CharacterId == first.CharacterId);
            Assert.Equal([first.ConnectionId, second.ConnectionId],
                closer.Closed.OrderBy(value => value.Value));
        });
    }

    [Fact]
    public async Task Leadership_transfer_to_a_returned_member_keeps_the_real_tower_host_then_can_resynchronize()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1);
            var second = Session(2);
            var returned = Session(3);
            var sessions = new Sessions(first, second, returned);
            var parties = new PartyService(
                new PartyStatus(), new PartyModeRuleService(), TimeProvider.System,
                new NoPartyEndpoints());
            Assert.True(parties.RequestInvite(PartyCharacter(first), PartyCharacter(second),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(second), PartyCharacter(first)).Succeeded);
            Assert.True(parties.RequestInvite(PartyCharacter(first), PartyCharacter(returned),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(returned), PartyCharacter(first)).Succeeded);
            var tower = new TowerDomain(PartyRun(Guid.NewGuid(), first, second));
            using var request = new CancellationTokenSource();
            var commands = new CoordinatorCommands(tower, parties, request);
            var closer = new Closer();
            var town = new NoTown();
            var coordinator = new BossTowerPartyCoordinator(
                tower, parties, sessions, new SelectedSessionTransitionGate(),
                new BossTowerProjectionGate(), commands,
                Notifications(parties, sessions, town),
                Delivery(tower, sessions, commands, new Sender(), closer),
                closer, town, NullLogger<BossTowerPartyCoordinator>.Instance);

            var toReturned = await coordinator.TryTransferAsync(
                first, targetSlot: 2, CancellationToken.None);

            Assert.True(toReturned.Result.Succeeded);
            Assert.Equal(returned.WireUserId,
                parties.FindParty(PartyCharacter(returned)).LeaderWireUserId);
            Assert.Equal(first,
                tower.Find(first)!.Members.Single(member => member.IsLeader).Session);
            Assert.Equal(0, commands.SynchronizeCalls);

            var backInside = await coordinator.TryTransferAsync(
                returned, targetSlot: 1, CancellationToken.None);

            Assert.True(backInside.Result.Succeeded);
            Assert.Equal(second.WireUserId,
                parties.FindParty(PartyCharacter(second)).LeaderWireUserId);
            Assert.Equal(second,
                tower.Find(second)!.Members.Single(member => member.IsLeader).Session);
            Assert.Equal(1, commands.SynchronizeCalls);
        });
    }

    [Fact]
    public async Task Predictable_party_refusals_do_not_mutate_the_tower()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var leader = Session(1);
            var member = Session(2);
            var sessions = new Sessions(leader, member);
            var parties = new PartyService(
                new PartyStatus(), new PartyModeRuleService(), TimeProvider.System,
                new NoPartyEndpoints());
            Assert.True(parties.RequestInvite(PartyCharacter(leader), PartyCharacter(member),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(member), PartyCharacter(leader)).Succeeded);
            var original = PartyRun(Guid.NewGuid(), leader, member);
            var tower = new TowerDomain(original);
            using var request = new CancellationTokenSource();
            var commands = new CoordinatorCommands(tower, parties, request);
            var closer = new Closer();
            var town = new NoTown();
            var coordinator = new BossTowerPartyCoordinator(
                tower, parties, sessions, new SelectedSessionTransitionGate(),
                new BossTowerProjectionGate(), commands,
                Notifications(parties, sessions, town),
                Delivery(tower, sessions, commands, new Sender(), closer),
                closer, town, NullLogger<BossTowerPartyCoordinator>.Instance);

            var unauthorized = await coordinator.TryRemoveAsync(
                member, targetSlot: 0, CancellationToken.None);
            var missing = await coordinator.TryRemoveAsync(
                leader, targetSlot: 7, CancellationToken.None);

            Assert.Equal(PartyFailureReason.NotPartyLeader, unauthorized.Result.Failure);
            Assert.Equal(PartyFailureReason.TargetNotFound, missing.Result.Failure);
            Assert.Equal(original, tower.Find(leader));
            Assert.Equal(0, commands.PartyCalls);
            Assert.Equal(0, commands.SynchronizeCalls);
        });
    }

    [Fact]
    public async Task A_third_member_disconnect_during_capture_revalidates_and_completes()
    {
        var first = Session(1);
        var second = Session(2);
        var third = Session(3);
        var sessions = new Sessions(first, second, third);
        var parties = new PartyService(
            new PartyStatus(), new PartyModeRuleService(), TimeProvider.System,
            new NoPartyEndpoints());
        Assert.True(parties.RequestInvite(PartyCharacter(first), PartyCharacter(second),
            new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
        Assert.True(parties.AcceptInvite(PartyCharacter(second), PartyCharacter(first)).Succeeded);
        Assert.True(parties.RequestInvite(PartyCharacter(first), PartyCharacter(third),
            new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
        Assert.True(parties.AcceptInvite(PartyCharacter(third), PartyCharacter(first)).Succeeded);
        var tower = new TowerDomain(PartyRun(Guid.NewGuid(), first, second, third));
        using var request = new CancellationTokenSource();
        var commands = new CoordinatorCommands(tower, parties, request);
        var gate = new SelectedSessionTransitionGate();
        var closer = new Closer();
        var town = new NoTown();
        var coordinator = new BossTowerPartyCoordinator(
            tower, parties, sessions, gate, new BossTowerProjectionGate(), commands,
            Notifications(parties, sessions, town),
            Delivery(tower, sessions, commands, new Sender(), closer),
            closer, town, NullLogger<BossTowerPartyCoordinator>.Instance);
        var thirdLease = await gate.AcquireAsync(
            third.AccountId, UserAccessMode.Exclusive, CancellationToken.None);
        Task<BossTowerPartyAttempt> pending;
        try
        {
            pending = coordinator.TryTransferAsync(
                first, targetSlot: 1, CancellationToken.None).AsTask();
            tower.Leave(third);
            parties.CleanupCharacter(third.CharacterId);
            Assert.True(sessions.ReleaseExact(third));
        }
        finally
        {
            await thirdLease.DisposeAsync();
        }

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Result.Succeeded);
        Assert.Null(tower.Find(third));
        Assert.Equal(second,
            tower.Find(second)!.Members.Single(member => member.IsLeader).Session);
        var remaining = parties.FindParty(PartyCharacter(second));
        Assert.Equal(second.WireUserId, remaining.LeaderWireUserId);
        Assert.DoesNotContain(remaining.Members,
            member => member.CharacterId == third.CharacterId);
    }

    [Fact]
    public async Task Projection_failure_does_not_close_a_replacement_selected_session()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1);
            var second = Session(2);
            var replacement = second with
            {
                ConnectionId = new NetworkConnectionId("tower-2-replacement"),
                ConnectionGeneration = 2,
            };
            var sessions = new Sessions(first, second);
            var tower = new TowerDomain(PartyRun(Guid.NewGuid(), first, second));
            var closer = new Closer();
            var sender = new Sender(connection =>
            {
                if (connection != second.ConnectionId)
                    return null;
                sessions.Replace(replacement);
                return new IOException("projection failed after replacement");
            });
            var delivery = Delivery(tower, sessions, new Commands(), sender, closer);
            var departure = tower.Leave(first);

            await delivery.ProjectCommittedDepartureAsync(
                departure, first, response: null, CancellationToken.None);

            Assert.Contains(second.ConnectionId, sender.Attempts);
            Assert.Equal(replacement, sessions.FindByCharacter(second.CharacterId));
            Assert.DoesNotContain(second.ConnectionId, closer.Closed);
            Assert.DoesNotContain(replacement.ConnectionId, closer.Closed);
        });
    }

    [Fact]
    public async Task Delayed_disconnect_projection_does_not_publish_an_old_room_into_the_next_room()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = Session(1);
            var captured = Run(Guid.NewGuid(), session, roomVersion: 4, TowerRoomPhase.Ready);
            var current = captured with
            {
                Room = captured.Room with { Version = 5, Phase = TowerRoomPhase.Loading },
            };
            var towers = new TowerDomain(current);
            var sessions = new Sessions(session);
            var sender = new Sender();
            var delivery = Delivery(towers, sessions, new Commands(), sender);

            await delivery.ProjectDisconnectedActionAsync(
                new BossTowerActionResult(true, captured), CancellationToken.None);

            Assert.Empty(sender.Deliveries);
        });
    }

    [Fact]
    public async Task Failed_due_run_does_not_prevent_a_later_due_run_from_being_processed()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1);
            var second = Session(2);
            var firstRun = Run(Guid.NewGuid(), first, roomVersion: 7, TowerRoomPhase.Cleared) with
            {
                ReturnDueAt = Now.AddSeconds(-1),
            };
            var secondRun = Run(Guid.NewGuid(), second, roomVersion: 11, TowerRoomPhase.Cleared) with
            {
                ReturnDueAt = Now.AddSeconds(-1),
            };
            var towers = new TowerDomain(firstRun, secondRun);
            var commands = new Commands(firstRun.RunId);
            var delivery = Delivery(towers, new Sessions(first, second), commands, new Sender());

            await delivery.SweepAsync(Now, CancellationToken.None);

            Assert.Equal([firstRun.RunId, secondRun.RunId], commands.Attempts);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task A_settled_receipt_is_projected_only_by_the_action_that_created_it(
        bool settlementCreated, bool expectsSettlementFrames)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = Session(1);
            var receipt = new TowerSettlementReceipt(
                [new TowerMemberSettlementReceipt(session.CharacterId, 12, 0, [])
                {
                    Session = session,
                    Slot = 0,
                    Candidates = [],
                }]);
            var run = Run(Guid.NewGuid(), session, roomVersion: 3, TowerRoomPhase.Fighting) with
            {
                Completion = new TowerCompletion(TowerEndReason.Defeated, 0, 1, true, Now, receipt),
            };
            var action = new BossTowerActionResult(true, run) { SettlementCreated = settlementCreated };
            var towers = new TowerDomain(run);
            var sessions = new Sessions(session);
            var sender = new Sender();
            var delivery = Delivery(towers, sessions, new ResultCommands(action), sender);

            Assert.True(await delivery.DieAsync(
                session.ConnectionId, GameMessageMetadata.Empty, CancellationToken.None));

            var frames = Assert.Single(sender.Deliveries).Delivery.RequiredDirectMessages;
            Assert.Equal(expectsSettlementFrames,
                frames.Any(frame => frame is BossTowerRewardNotification));
            Assert.Equal(expectsSettlementFrames,
                frames.Any(frame => frame is BossTowerContinueNotification));
        });
    }

    private static BossTowerProjectionDelivery Delivery(
        TowerDomain towers, Sessions sessions, ICommandBus commands, Sender sender,
        INetworkConnectionCloser? closer = null)
    {
        var views = new NoViews();
        var town = new TownAreaProjectionDelivery(
            null!, sessions, views, null!, null!, sender, null!);
        return new BossTowerProjectionDelivery(
            towers,
            commands,
            sessions,
            null!,
            views,
            null!,
            null!,
            null!,
            new Intents(),
            sender,
            town,
            new SelectedSessionTransitionGate(),
            new BossTowerProjectionGate(),
            new BossTowerEntryGate(),
            TimeProvider.System,
            closer ?? new Closer(),
            NullLogger<BossTowerProjectionDelivery>.Instance);
    }

    private static SelectedSessionIdentity Session(int value) => SelectedSessionIdentity.Create(
        new AccountId(value), new CharacterId(value), new NetworkConnectionId($"tower-{value}"), 1, 10011);

    private static BossTowerSnapshot Run(
        Guid id, SelectedSessionIdentity session, long roomVersion, TowerRoomPhase phase)
    {
        var room = new TowerRoomSnapshot(
            1, 31011, 1, roomVersion, phase, [], [],
            ImmutableHashSet<CharacterId>.Empty.Add(session.CharacterId));
        return new BossTowerSnapshot(
            id, 3505, [new TowerMember(session, 0, true, false)], room, [],
            new TowerCompletion(TowerEndReason.Completed, 1, 1, true, Now), Now.AddMinutes(-1));
    }

    private static BossTowerSnapshot PartyRun(
        Guid id, params SelectedSessionIdentity[] members)
    {
        var room = new TowerRoomSnapshot(
            1, 31011, 1, 1, TowerRoomPhase.Ready, [], [],
            members.Select(member => member.CharacterId).ToImmutableHashSet());
        return new BossTowerSnapshot(
            id, 3505,
            [.. members.Select((member, index) =>
                new TowerMember(member, checked((byte)index), index == 0, false))],
            room, [], null, Now.AddMinutes(-1));
    }

    private static PartyCharacterSnapshot PartyCharacter(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId,
            new PartyDirectoryScope(11, 1, 1));

    private static PartyNotificationDelivery Notifications(
        IPartyService parties, ISelectedSessionDomainService sessions,
        ITownAreaDomainService town) =>
        new(new NoopNotifications(), new PartyProjectionStreamRegistry(),
            sessions, town, parties);

    private sealed class TowerDomain(params BossTowerSnapshot[] values) : IBossTowerDomainService
    {
        private readonly Dictionary<CharacterId, BossTowerSnapshot> runs = values
            .SelectMany(run => run.Members.Select(member => (member.Session.CharacterId, Run: run)))
            .ToDictionary(value => value.CharacterId, value => value.Run);

        public BossTowerSnapshot? Find(SelectedSessionIdentity session) =>
            runs.GetValueOrDefault(session.CharacterId) is { } run && run.Members.Any(member => member.Session == session) ? run : null;
        public void Put(BossTowerSnapshot run)
        {
            foreach (var member in run.Members) runs[member.Session.CharacterId] = run;
        }

        public IReadOnlyList<BossTowerActionContext> FindReturnsDue(DateTimeOffset now) =>
            runs.Values.DistinctBy(run => run.RunId)
                .Where(run => run.ReturnDueAt <= now)
                .Select(run => new BossTowerActionContext(run.Members[0].Session, run.RunId, run.Room.Version))
                .ToArray();

        public bool IsInTower(CharacterId characterId) => runs.ContainsKey(characterId);

        public BossTowerActionResult Leave(SelectedSessionIdentity session)
        {
            if (!runs.TryGetValue(session.CharacterId, out var current))
                return new BossTowerActionResult(false, null);
            var members = current.Members
                .Where(member => member.Session != session)
                .OrderBy(member => member.Slot)
                .Select((member, index) => member with { IsLeader = index == 0 })
                .ToImmutableArray();
            runs.Remove(session.CharacterId);
            var changed = current with { Members = members };
            foreach (var member in members)
                runs[member.Session.CharacterId] = changed;
            return new BossTowerActionResult(true, changed);
        }

        public BossTowerActionResult Synchronize(SelectedSessionIdentity leader)
        {
            if (!runs.TryGetValue(leader.CharacterId, out var current))
                return new BossTowerActionResult(false, null);
            var changed = current with
            {
                Members = current.Members
                    .Select(member => member with { IsLeader = member.Session == leader })
                    .ToImmutableArray(),
            };
            foreach (var member in changed.Members)
                runs[member.Session.CharacterId] = changed;
            return new BossTowerActionResult(true, changed);
        }
        public bool UnusedReserve(IReadOnlyList<CharacterId> members) => false;
        public bool Supports(int dungeonId) => dungeonId == 3505;
        public bool CanShop(SelectedSessionIdentity session, int npcId, int shopId) => false;
        public ValueTask<bool> CanRetryAsync(BossTowerActionContext context, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public ValueTask<BossTowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members, SelectedSessionIdentity leader, uint seed, CancellationToken cancellationToken, byte requestMode = 0, byte difficulty = 0, byte practiceMode = 0) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> LoadAsync(BossTowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> StartAsync(BossTowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> KillAsync(BossTowerActionContext context, ushort actorUid, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> NextLevelAsync(BossTowerActionContext context, byte targetX, byte targetY, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> DieAsync(BossTowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> LeaveAsync(BossTowerActionContext context, bool disconnected, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> SynchronizeLeaderAsync(BossTowerActionContext context, SelectedSessionIdentity leader, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> ReleaseForSelectionAsync(BossTowerActionContext context, int requestedDungeonId, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<BossTowerPickupResult> PickupAsync(BossTowerActionContext context, ushort uid, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<BossTowerActionResult> SelectResultAsync(BossTowerActionContext context, byte option, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Sessions(params SelectedSessionIdentity[] values) : ISelectedSessionDomainService
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

        private readonly List<SelectedSessionIdentity> values = [.. values];

        public void Replace(SelectedSessionIdentity session)
        {
            values.RemoveAll(value => value.AccountId == session.AccountId);
            values.Add(session);
        }

        public SelectedSessionIdentity? Find(AccountId accountId) => values.SingleOrDefault(value => value.AccountId == accountId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => values.SingleOrDefault(value => value.CharacterId == characterId);
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => values.SingleOrDefault(value => value.WireUserId == wireUserId);
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => values.SingleOrDefault(value => value.ConnectionId == connectionId);
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session)
        {
            var previous = values.SingleOrDefault(value => value.AccountId == session.AccountId);
            Replace(session);
            return previous;
        }

        public bool ReleaseExact(SelectedSessionIdentity session) => values.Remove(session);
    }

    private sealed class Commands(Guid? failingRun = null) : ICommandBus
    {
        public List<Guid> Attempts { get; } = [];

        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command,
            CancellationToken cancellationToken = default) where TResult : notnull
        {
            var leave = Assert.IsType<LeaveBossTowerCommand>(command);
            Attempts.Add(leave.Context.RunId);
            if (leave.Context.RunId == failingRun)
                return ValueTask.FromException<TResult>(new IOException("closed recipient"));
            return ValueTask.FromResult((TResult)(object)new BossTowerActionResult(false, null));
        }
    }

    private sealed class ResultCommands(BossTowerActionResult result) : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command,
            CancellationToken cancellationToken = default) where TResult : notnull
        {
            Assert.IsType<DieInBossTowerCommand>(command);
            return ValueTask.FromResult((TResult)(object)result);
        }
    }

    private sealed class CoordinatorCommands(
        TowerDomain towers,
        IPartyService parties,
        CancellationTokenSource request,
        bool throwAfterParty = false) : ICommandBus
    {
        public CancellationToken PartyToken { get; private set; }
        public int PartyCalls { get; private set; }
        public int SynchronizeCalls { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command,
            CancellationToken cancellationToken = default) where TResult : notnull
        {
            object result = command switch
            {
                LeaveBossTowerCommand leave => LeaveTower(leave),
                SynchronizeBossTowerLeaderCommand synchronize =>
                    Synchronize(synchronize),
                LeavePartyCommand leave => LeaveParty(leave, cancellationToken),
                TransferPartyLeadershipCommand transfer =>
                    TransferParty(transfer, cancellationToken),
                RemovePartyMemberCommand remove => RemoveParty(remove, cancellationToken),
                _ => throw new NotSupportedException(command.GetType().Name),
            };
            return ValueTask.FromResult((TResult)result);
        }

        private BossTowerActionResult Synchronize(
            SynchronizeBossTowerLeaderCommand command)
        {
            SynchronizeCalls++;
            return towers.Synchronize(command.Leader);
        }

        private BossTowerActionResult LeaveTower(LeaveBossTowerCommand command)
        {
            var result = towers.Leave(command.Context.Session);
            request.Cancel();
            return result;
        }

        private PartyOperationResult LeaveParty(
            LeavePartyCommand command, CancellationToken cancellationToken)
        {
            PartyToken = cancellationToken;
            PartyCalls++;
            var result = parties.LeaveParty(command.CharacterId);
            return throwAfterParty
                ? throw new IOException("wrapper failed after party write")
                : result;
        }

        private PartyOperationResult TransferParty(
            TransferPartyLeadershipCommand command, CancellationToken cancellationToken)
        {
            PartyToken = cancellationToken;
            PartyCalls++;
            return parties.TransferLeadership(
                command.CurrentLeaderCharacterId, command.NextLeaderCharacterId);
        }

        private PartyOperationResult RemoveParty(
            RemovePartyMemberCommand command, CancellationToken cancellationToken)
        {
            PartyToken = cancellationToken;
            PartyCalls++;
            return parties.RemoveMember(
                command.CurrentLeaderCharacterId, command.TargetCharacterId);
        }
    }

    private sealed class NoViews : ISelectedCharacterCurrentViewDomainService
    {
        public ValueTask<SelectCharacterResult?> TryLoadAsync(
            SelectedSessionIdentity session, CancellationToken cancellationToken) => ValueTask.FromResult<SelectCharacterResult?>(null);
    }

    private sealed class Intents : IDungeonEntryIntentDomainService
    {
        public void Record(SelectedSessionIdentity session, DungeonEntryIntent intent) { }
        public DungeonEntryIntent? Consume(SelectedSessionIdentity session) => null;
        public void RestoreIfAbsent(SelectedSessionIdentity session, DungeonEntryIntent intent) { }
        public bool ClearExact(SelectedSessionIdentity session) => false;
    }

    private sealed class Sender(Func<NetworkConnectionId, Exception?>? failure = null)
        : IGameMessageSender
    {
        public List<(NetworkConnectionId Connection, GameExplicitMessageDelivery Delivery)> Deliveries { get; } = [];
        public List<NetworkConnectionId> Attempts { get; } = [];
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) => SendExplicitAsync(connectionId, new GameExplicitMessageDelivery([message], []), cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(NetworkConnectionId connectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken)
        {
            Attempts.Add(connectionId);
            if (failure?.Invoke(connectionId) is { } error)
                return ValueTask.FromException(error);
            Deliveries.Add((connectionId, delivery));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Closer : INetworkConnectionCloser
    {
        public List<NetworkConnectionId> Closed { get; } = [];

        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken cancellationToken)
        {
            Closed.Add(expected.ConnectionId);
            return ValueTask.CompletedTask;
        }

        public ValueTask RequestCloseAsync(NetworkConnectionId connectionId, CancellationToken cancellationToken)
        {
            Closed.Add(connectionId);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReentrantCloser(
        SelectedSessionTransitionGate gate,
        AccountId account) : INetworkConnectionCloser
    {
        public List<NetworkConnectionId> Closed { get; } = [];

        public ValueTask RequestCloseExactAsync(
            NetworkConnectionContext expected, CancellationToken cancellationToken) =>
            RequestCloseAsync(expected.ConnectionId, cancellationToken);

        public async ValueTask RequestCloseAsync(
            NetworkConnectionId connectionId, CancellationToken cancellationToken)
        {
            await using var lease = await gate.AcquireAsync(
                account, UserAccessMode.Exclusive, cancellationToken);
            Closed.Add(connectionId);
        }
    }

    private sealed class NoopNotifications : Dfo.Server.Testing.ProjectionEventService
    {
        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new NotificationDelivery([]));
    }

    private sealed class NoTown : ITownAreaDomainService
    {
        public TownAreaMembershipChange Enter(TownAreaMember member) => throw new NotSupportedException();
        public TownAreaMembershipChange? LeaveExact(CharacterId characterId, TownAreaMembershipLease lease, TownAreaDepartureReason reason) => null;
        public TownAreaMembershipChange? TransferExact(CharacterId characterId, TownAreaMembershipLease lease, TownAreaKey destination, CharacterPositionState position, bool sharesTownPresence) => null;
        public TownAreaMovement? MoveExact(CharacterId characterId, TownAreaMembershipLease lease, CharacterPositionState position) => null;
        public TownAreaMember? Find(CharacterId characterId) => null;
        public TownAreaSnapshot? Find(TownAreaKey key) => null;
        public IReadOnlyList<TownAreaSnapshot> Query(TownAreaFilter filter) => [];
    }

    private sealed class PartyStatus : IPartyMemberStatusDomainService
    {
        public ValueTask<PartyInviteDisplayState> InvitationOfAsync(
            PartyCharacterSnapshot character, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PartyInviteDisplayState(0, 0, 0, 0));

        public byte CurrentHpPercentOf(CharacterId characterId) => 100;
    }

    private sealed class NoPartyEndpoints : IPartyMemberSessionEndpointSource
    {
        public SessionEndpoint? FindThroughCurrentSession(CharacterId characterId) => null;
    }
}
