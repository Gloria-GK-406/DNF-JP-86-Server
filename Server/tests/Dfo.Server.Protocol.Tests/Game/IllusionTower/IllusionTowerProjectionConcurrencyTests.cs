using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
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
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.IllusionTower;

public sealed class IllusionTowerProjectionConcurrencyTests
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
                new NoPartyEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
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
            var coordinator = new IllusionTowerPartyDriver(
                tower,
                commands.Parties,
                sessions,
                commands,
                Notifications(parties, sessions, town),
                delivery,
                closer,
                town,
                NullLogger<IllusionTowerPartyDriver>.Instance);

            var result = await coordinator.TryLeaveAsync(
                first, allowVirtual: false, departureResponse: null, request.Token);

            Assert.True(request.IsCancellationRequested);
            Assert.NotNull(result);
            Assert.True(result.Result.Succeeded);
            Assert.False(commands.PartyToken.CanBeCanceled);
            var remainingParty = parties.FindParty(secondParty);
            Assert.Equal(second.WireUserId, remainingParty.LeaderWireUserId);
            Assert.Equal(second.CharacterId, Assert.Single(remainingParty.Members).CharacterId);
            var remainingTower = Assert.IsType<IllusionTowerSnapshot>(tower.Find(second));
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
                new NoPartyEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
            var tower = new TowerDomain(PartyRun(Guid.NewGuid(), session));
            using var request = new CancellationTokenSource();
            var commands = new CoordinatorCommands(tower, parties, request);
            var closer = new Closer();
            var town = new NoTown();
            var coordinator = new IllusionTowerPartyDriver(
                tower, commands.Parties, sessions,
                commands,
                Notifications(parties, sessions, town),
                Delivery(tower, sessions, commands, new Sender(), closer),
                closer, town, NullLogger<IllusionTowerPartyDriver>.Instance);

            var result = await coordinator.TryLeaveAsync(
                session, allowVirtual: true, new GiveUpGameResponse(), request.Token);

            Assert.NotNull(result);
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
                new NoPartyEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
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
            var ordinaryRuns = TestParties.Runs();
            var layout = new DungeonLayout(10000, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0),
                61000, 1, new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(61000) });
            var ordinary = ordinaryRuns.PreparePartyEntry([new DungeonRunParticipant(external.AccountId, external.CharacterId, 0, true)],
                layout, 0).Run;
            Assert.Equal(ordinary.RunId, ordinaryRuns.Find(external.CharacterId)!.RunId);
            using var request = new CancellationTokenSource();
            var commands = new CoordinatorCommands(tower, parties, request);
            var closer = new Closer();
            var town = new NoTown();
            var coordinator = new IllusionTowerPartyDriver(
                tower, commands.Parties, sessions,
                commands,
                Notifications(parties, sessions, town),
                Delivery(tower, sessions, commands, new Sender(), closer),
                closer, town, NullLogger<IllusionTowerPartyDriver>.Instance, runs: ordinaryRuns);

            var result = await coordinator.TryLeaveAsync(
                external, allowVirtual: false, departureResponse: null, CancellationToken.None);

            Assert.True(result!.Result.Succeeded);
            Assert.Equal(1, commands.SynchronizeCalls);
            Assert.Null(tower.Find(external));
            Assert.Null(ordinaryRuns.Find(external.CharacterId));
            var current = Assert.IsType<IllusionTowerSnapshot>(tower.Find(successor));
            Assert.Equal(2, current.Members.Length);
            Assert.Equal(successor, current.Members.Single(member => member.IsLeader).Session);
            var currentParty = parties.FindParty(PartyCharacter(successor));
            Assert.Equal(successor.WireUserId, currentParty.LeaderWireUserId);
            Assert.DoesNotContain(currentParty.Members,
                member => member.CharacterId == external.CharacterId);
        });
    }

    [Fact]
    public async Task Party_wrapper_failure_after_its_inner_write_isolates_and_closes_both_sessions()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1);
            var second = Session(2);
            var sessions = new Sessions(first, second);
            var parties = new PartyService(
                new PartyStatus(), new PartyModeRuleService(), TimeProvider.System,
                new NoPartyEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
            Assert.True(parties.RequestInvite(PartyCharacter(first), PartyCharacter(second),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(second), PartyCharacter(first)).Succeeded);
            var tower = new TowerDomain(PartyRun(Guid.NewGuid(), first, second));
            using var request = new CancellationTokenSource();
            var commands = new CoordinatorCommands(
                tower, parties, request, throwAfterParty: true);
            var closer = new Closer();
            var town = new NoTown();
            var coordinator = new IllusionTowerPartyDriver(
                tower, commands.Parties, sessions, commands,
                Notifications(parties, sessions, town),
                Delivery(tower, sessions, commands, new Sender(), closer),
                closer, town, NullLogger<IllusionTowerPartyDriver>.Instance);

            var result = await coordinator.TryLeaveAsync(
                    first, allowVirtual: false, departureResponse: null, CancellationToken.None)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(2));

            Assert.NotNull(result);
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
                new NoPartyEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
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
            var coordinator = new IllusionTowerPartyDriver(
                tower, commands.Parties, sessions,
                commands,
                Notifications(parties, sessions, town),
                Delivery(tower, sessions, commands, new Sender(), closer),
                closer, town, NullLogger<IllusionTowerPartyDriver>.Instance);

            var toReturned = await coordinator.TryTransferAsync(
                first, targetSlot: 2, CancellationToken.None);

            Assert.True(toReturned!.Result.Succeeded);
            Assert.Equal(returned.WireUserId,
                parties.FindParty(PartyCharacter(returned)).LeaderWireUserId);
            Assert.Equal(first,
                tower.Find(first)!.Members.Single(member => member.IsLeader).Session);
            Assert.Equal(0, commands.SynchronizeCalls);

            var backInside = await coordinator.TryTransferAsync(
                returned, targetSlot: 1, CancellationToken.None);

            Assert.True(backInside!.Result.Succeeded);
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
                new NoPartyEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
            Assert.True(parties.RequestInvite(PartyCharacter(leader), PartyCharacter(member),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(member), PartyCharacter(leader)).Succeeded);
            var original = PartyRun(Guid.NewGuid(), leader, member);
            var tower = new TowerDomain(original);
            using var request = new CancellationTokenSource();
            var commands = new CoordinatorCommands(tower, parties, request);
            var closer = new Closer();
            var town = new NoTown();
            var coordinator = new IllusionTowerPartyDriver(
                tower, commands.Parties, sessions,
                commands,
                Notifications(parties, sessions, town),
                Delivery(tower, sessions, commands, new Sender(), closer),
                closer, town, NullLogger<IllusionTowerPartyDriver>.Instance);

            var unauthorized = await coordinator.TryRemoveAsync(
                member, targetSlot: 0, CancellationToken.None);
            var missing = await coordinator.TryRemoveAsync(
                leader, targetSlot: 7, CancellationToken.None);

            Assert.Equal(PartyFailureReason.NotPartyLeader, unauthorized!.Result.Failure);
            Assert.Equal(PartyFailureReason.TargetNotFound, missing!.Result.Failure);
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
            new NoPartyEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
        TestGameplay.Run(() =>
        {
            Assert.True(parties.RequestInvite(PartyCharacter(first), PartyCharacter(second),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(second), PartyCharacter(first)).Succeeded);
            Assert.True(parties.RequestInvite(PartyCharacter(first), PartyCharacter(third),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(third), PartyCharacter(first)).Succeeded);
        });
        var tower = new TowerDomain(PartyRun(Guid.NewGuid(), first, second, third));
        using var request = new CancellationTokenSource();
        var commands = new CoordinatorCommands(tower, parties, request);
        var closer = new Closer();
        var town = new NoTown();
        var coordinator = new IllusionTowerPartyDriver(
            tower, commands.Parties, sessions, commands,
            Notifications(parties, sessions, town),
            Delivery(tower, sessions, commands, new Sender(), closer),
            closer, town, NullLogger<IllusionTowerPartyDriver>.Instance);
        // The third member leaves in a CMD that completes before the transfer CMD runs.
        TestGameplay.Run(() =>
        {
            tower.Leave(third);
            parties.CleanupCharacter(third.CharacterId);
            Assert.True(sessions.ReleaseExact(third));
        });
        var result = await TestGameplay.RunAsync(async () => await coordinator.TryTransferAsync(first, targetSlot: 1, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result!.Result.Succeeded);
        Assert.Null(tower.Find(third));
        Assert.Equal(second,
            tower.Find(second)!.Members.Single(member => member.IsLeader).Session);
        var remaining = parties.FindParty(PartyCharacter(second));
        Assert.Equal(second.WireUserId, remaining.LeaderWireUserId);
        Assert.DoesNotContain(remaining.Members,
            member => member.CharacterId == third.CharacterId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Projection_failure_does_not_close_a_replacement_selected_session(bool samePhysicalConnection)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1);
            var second = Session(2);
            var replacement = second with
            {
                ConnectionId = samePhysicalConnection ? second.ConnectionId : new NetworkConnectionId("tower-2-replacement"),
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
                new TowerActionResult(true, captured), CancellationToken.None);

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
            var action = new TowerActionResult(true, run) { SettlementCreated = settlementCreated };
            var towers = new TowerDomain(run);
            var sessions = new Sessions(session);
            var sender = new Sender();
            var delivery = new Dfo.Server.Protocol.Tests.Game.CharacterDeath.DeathProjectionDriver(sessions,sender,null,towers);

            Assert.Empty(await delivery.IllusionAsync(action,session,CancellationToken.None));

            var frames = Assert.Single(sender.Deliveries).Delivery.RequiredDirectMessages;
            Assert.Equal(expectsSettlementFrames,
                frames.Any(frame => frame is IllusionTowerRankingNotification));
            Assert.Equal(expectsSettlementFrames,
                frames.Any(frame => frame is IllusionTowerRewardNotification));
            Assert.Equal(expectsSettlementFrames,
                frames.Any(frame => frame is IllusionTowerContinueNotification));
        });
    }

    [Fact]
    public async Task A_caller_whose_session_is_not_current_is_refused_at_once_without_a_tower_or_party_write()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var first = Session(1);
            var second = Session(2);
            // The caller is still a tower member but no longer the current selected session.
            var sessions = new Sessions(second);
            var parties = new PartyService(
                new PartyStatus(), new PartyModeRuleService(), TimeProvider.System,
                new NoPartyEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
            Assert.True(parties.RequestInvite(PartyCharacter(first), PartyCharacter(second),
                new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(second), PartyCharacter(first)).Succeeded);
            var tower = new TowerDomain(PartyRun(Guid.NewGuid(), first, second));
            using var request = new CancellationTokenSource();
            var commands = new CoordinatorCommands(tower, parties, request);
            var closer = new Closer();
            var town = new NoTown();
            var coordinator = new IllusionTowerPartyDriver(
                tower, commands.Parties, sessions, commands,
                Notifications(parties, sessions, town),
                Delivery(tower, sessions, commands, new Sender(), closer),
                closer, town, NullLogger<IllusionTowerPartyDriver>.Instance);

            var result = await coordinator.TryLeaveAsync(
                first, allowVirtual: false, departureResponse: null, CancellationToken.None);

            Assert.NotNull(result);
            Assert.False(result.Result.Succeeded);
            Assert.Equal(PartyFailureReason.CallerNotCurrent, result.Result.Failure);
            Assert.Null(result.Target);
            Assert.Equal(0, commands.PartyCalls);
            Assert.NotNull(tower.Find(first));
            Assert.Contains(parties.FindParty(PartyCharacter(second)).Members,
                member => member.CharacterId == first.CharacterId);
        });
    }

    /// <summary>XO-4 (spec appendix D): every member's tower entry batch is delivered before the requester's.</summary>
    [Fact]
    public async Task A_slow_member_is_delivered_the_tower_entry_before_the_requester()
    {
        var requester = Session(1);
        var member = Session(2);
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(requester, member);
        outbound.Transport.Slow(member.ConnectionId, TimeSpan.FromMilliseconds(300));

        await outbound.RunAsync(async () =>
        {
            var run = PartyRun(Guid.NewGuid(), requester, member);
            var towers = new TowerDomain(run);
            var sessions = new Sessions(requester, member);
            var views = new EntryViews(requester, member);
            var roster = EntryRoster(requester, member);
            var delivery = Dfo.Server.Protocol.Tests.Game.DungeonEntry.TowerProjectionTestFactory.Illusion(
                towers, new EntryCommands(run), sessions, new EntryParties(roster), views,
                new SameState(), null!, null!, new Intents(), outbound.Game,
                Dfo.Server.Protocol.Tests.Game.TownProjection.TownAreaProjectionTestFactory.Create(sessions, views, outbound.Game),
                TimeProvider.System, outbound.Closer,
                NullLogger<IllusionTowerProjectionDriver>.Instance, ordering: outbound.Ordering, onlineUsers: ProtocolAlgorithmFixture.QueryFor(sessions, outbound.Context));

            Assert.True(await delivery.EnterAsync(
                requester.ConnectionId, requester, roster, Dfo.Server.Abstractions.World.DungeonEntryIntent.Interactive,
                () => { }, CancellationToken.None));
        });
        await outbound.SettledAsync();

        var memberEntry = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == member.ConnectionId);
        var requesterEntry = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == requester.ConnectionId);
        Assert.True(memberEntry.Order!.Value.Sequence < requesterEntry.Order!.Value.Sequence);
        Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundDelivery.BestEffort, requesterEntry.Delivery);
        Assert.True(outbound.Delivered(requester.ConnectionId) > outbound.Delivered(member.ConnectionId),
            "the requester entered before the member");
    }

    /// <summary>
    /// DR-3, VC-3 (TASK-24 T4): a member whose tower entry fails in transport is closed by the sender alone;
    /// the requester still enters and the delivery registers no close.
    /// </summary>
    [Fact]
    public async Task A_member_whose_transport_fails_is_closed_alone_and_the_requester_still_enters_the_tower()
    {
        var requester = Session(1);
        var member = Session(2);
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(requester, member);
        outbound.Transport.Fail(member.ConnectionId);

        var entered = await outbound.RunAsync(() => EnterThroughOutboxAsync(outbound, requester, member));
        await outbound.SettledAsync();

        Assert.True(entered);
        var memberEntry = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == member.ConnectionId);
        var requesterEntry = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == requester.ConnectionId);
        Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundOutcome.Failed, outbound.Observer.OutcomeOf(memberEntry));
        Assert.True(await outbound.ClosedAsync(member.ConnectionId));
        Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundOutcome.Delivered, outbound.Observer.OutcomeOf(requesterEntry));
        Assert.False(outbound.Transport.IsClosed(requester.ConnectionId));
        Assert.DoesNotContain(outbound.Recorder.Entries, entry => entry.IsClose);
    }

    /// <summary>
    /// DR-3, the kept entry branch (TASK-24 R3): a member's entry batch cannot be registered (its encoding fails),
    /// so every isolated member and the requester are closed and the failure is thrown, as before the outbox.
    /// </summary>
    [Fact]
    public async Task A_member_entry_that_cannot_be_registered_isolates_every_member_and_the_requester()
    {
        var requester = Session(1);
        var member = Session(2);
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(requester, member);
        outbound.Encoder.Fail<Dfo.Server.Protocol.Game.DungeonSelection.EnterSelectDungeonAcknowledgement>(member.ConnectionId);

        await Assert.ThrowsAsync<InvalidDataException>(() => outbound.RunFromAsync(
            requester.ConnectionId, () => EnterThroughOutboxAsync(outbound, requester, member)));
        await outbound.SettledAsync();

        Assert.True(outbound.CloseRegistered(member.ConnectionId));
        Assert.True(outbound.CloseRegistered(requester.ConnectionId));
        Assert.Empty(outbound.Messages(member.ConnectionId));
        Assert.Empty(outbound.Messages(requester.ConnectionId));
        Assert.True(await outbound.ClosedAsync(member.ConnectionId));
        Assert.True(await outbound.ClosedAsync(requester.ConnectionId));
    }

    /// <summary>
    /// DR-3, the kept projection branch (TASK-24 R3): one remaining member's departure projection cannot be
    /// registered; only he is closed, the others keep theirs.
    /// </summary>
    [Fact]
    public async Task A_departure_projection_that_cannot_be_registered_for_one_member_closes_only_him()
    {
        var leaving = Session(1);
        var second = Session(2);
        var third = Session(3);
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(leaving, second, third);
        outbound.Encoder.Fail<Dfo.Server.Protocol.Game.DungeonSelection.UdpHostNotification>(second.ConnectionId);

        await outbound.RunAsync(async () =>
        {
            var sessions = new Sessions(leaving, second, third);
            var run = PartyRun(Guid.NewGuid(), leaving, second, third);
            var towers = new TowerDomain(run with { Room = run.Room with { Phase = TowerRoomPhase.Fighting } });
            var views = new NoViews();
            var delivery = Dfo.Server.Protocol.Tests.Game.DungeonEntry.TowerProjectionTestFactory.IllusionLegacy(
                towers, new Commands(), sessions, null!, views, null!, null!, null!, new Intents(), outbound.Game,
                global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.TownProjection.TownAreaProjectionDelivery>(null!, sessions, views, null!, null!, outbound.Game, null!),
                TimeProvider.System, outbound.Closer, NullLogger<IllusionTowerProjectionDriver>.Instance, onlineUsers: ProtocolAlgorithmFixture.QueryFor(sessions, outbound.Context));
            await delivery.ProjectCommittedDepartureAsync(towers.Leave(leaving), returning: null, response: null, CancellationToken.None);
        });
        await outbound.SettledAsync();

        Assert.True(outbound.CloseRegistered(second.ConnectionId));
        Assert.False(outbound.CloseRegistered(third.ConnectionId));
        Assert.False(outbound.CloseRegistered(leaving.ConnectionId));
        Assert.Contains(outbound.Messages(third.ConnectionId), message => message is Dfo.Server.Protocol.Game.DungeonSelection.UdpHostNotification);
    }

    private static async Task<bool> EnterThroughOutboxAsync(
        Dfo.Server.Protocol.Tests.Outbox.XoDelivery outbound, SelectedSessionIdentity requester, SelectedSessionIdentity member)
    {
        var run = PartyRun(Guid.NewGuid(), requester, member);
        var towers = new TowerDomain(run);
        var sessions = new Sessions(requester, member);
        var views = new EntryViews(requester, member);
        var roster = EntryRoster(requester, member);
        var delivery = Dfo.Server.Protocol.Tests.Game.DungeonEntry.TowerProjectionTestFactory.Illusion(
            towers, new EntryCommands(run), sessions, new EntryParties(roster), views,
            new SameState(), null!, null!, new Intents(), outbound.Game,
            Dfo.Server.Protocol.Tests.Game.TownProjection.TownAreaProjectionTestFactory.Create(sessions, views, outbound.Game),
            TimeProvider.System, outbound.Closer,
            NullLogger<IllusionTowerProjectionDriver>.Instance, ordering: outbound.Ordering, onlineUsers: ProtocolAlgorithmFixture.QueryFor(sessions, outbound.Context));
        return await delivery.EnterAsync(
            requester.ConnectionId, requester, roster, Dfo.Server.Abstractions.World.DungeonEntryIntent.Interactive, () => { }, CancellationToken.None);
    }

    private static PartySnapshot EntryRoster(params SelectedSessionIdentity[] members) => new(
        78, 1, 1, members[0].WireUserId,
        new PartySettings(new byte[PartySettings.WireLength]), PartyTitle.Empty,
        [.. members.Select((session, slot) => new PartyMemberSnapshot(PartyCharacter(session), checked((byte)slot), null, 100))]);

    private sealed class EntryViews(params SelectedSessionIdentity[] sessions) : ISelectedCharacterCurrentViewDomainService
    {
        public ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken cancellationToken)
        {
            if (!sessions.Contains(session)) return ValueTask.FromResult<SelectCharacterResult?>(null);
            var entry = Dfo.Server.Protocol.Tests.Game.SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
            return ValueTask.FromResult<SelectCharacterResult?>(entry with
            {
                Character = entry.Character with
                {
                    Identity = entry.Character.Identity with
                    {
                        CharacterId = session.CharacterId,
                        Name = [(byte)(session.WireUserId & byte.MaxValue)],
                    },
                },
            });
        }
    }

    private sealed class SameState : ICharacterStateProjectionDomainService
    {
        public ValueTask<SelectCharacterResult> RefreshAsync(SelectCharacterResult result, CancellationToken cancellationToken) =>
            ValueTask.FromResult(result);
    }

    private sealed class EntryParties(PartySnapshot snapshot) : IPartyService
    {
        public PartyMembershipSnapshot? FindByKey(PartyKey key) => null;
        public PartyKey KeyOf(CharacterId characterId) => throw new NotSupportedException();
        public PartySnapshot FindParty(PartyCharacterSnapshot character) => snapshot;
        public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => [snapshot];
        public PartyOperationResult CreateOrUpdateSettings(PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) => throw new NotSupportedException();
        public PartyOperationResult RequestInvite(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target, PartyInviteDisplayState display) => throw new NotSupportedException();
        public PartyOperationResult AcceptInvite(PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) => throw new NotSupportedException();
        public PartyOperationResult TransferLeadership(CharacterId currentLeaderCharacterId, CharacterId nextLeaderCharacterId) => throw new NotSupportedException();
        public PartyOperationResult RemoveMember(CharacterId currentLeaderCharacterId, CharacterId targetCharacterId) => throw new NotSupportedException();
        public PartyOperationResult LeaveParty(CharacterId characterId) => throw new NotSupportedException();
        public PartyOperationResult CleanupCharacter(CharacterId characterId) => throw new NotSupportedException();
    }

    private sealed class EntryCommands(IllusionTowerSnapshot run) : ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull =>
            command is Dfo.Server.Protocol.Tests.Game.DungeonEntry.IllusionEntryRouteProbe
                ? ValueTask.FromResult((TResult)(object)new TowerEntryResult(null, run, []))
                : throw new NotSupportedException(command.GetType().Name);
    }

    private static IllusionTowerProjectionDriver Delivery(
        TowerDomain towers, Sessions sessions, ICommandBus commands, Sender sender,
        INetworkConnectionCloser? closer = null)
    {
        var views = new NoViews();
        var town = global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.TownProjection.TownAreaProjectionDelivery>(null!, sessions, views, null!, null!, sender, null!);
        return Dfo.Server.Protocol.Tests.Game.DungeonEntry.TowerProjectionTestFactory.IllusionLegacy(
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
            TimeProvider.System,
            closer ?? new Closer(),
            NullLogger<IllusionTowerProjectionDriver>.Instance);
    }

    private static SelectedSessionIdentity Session(int value) => SelectedSessionIdentity.Create(
        new AccountId(value), new CharacterId(value), new NetworkConnectionId($"tower-{value}"), 1, 10011);

    private static IllusionTowerSnapshot Run(
        Guid id, SelectedSessionIdentity session, long roomVersion, TowerRoomPhase phase)
    {
        var room = new TowerRoomSnapshot(
            1, 31011, 1, roomVersion, phase, [], [],
            ImmutableHashSet<CharacterId>.Empty.Add(session.CharacterId));
        return new IllusionTowerSnapshot(
            id, 11001, [new TowerMember(session, 0, true, false)], room, [],
            new TowerCompletion(TowerEndReason.Completed, 1, 1, true, Now), Now.AddMinutes(-1));
    }

    private static IllusionTowerSnapshot PartyRun(
        Guid id, params SelectedSessionIdentity[] members)
    {
        var room = new TowerRoomSnapshot(
            1, 31011, 1, 1, TowerRoomPhase.Ready, [], [],
            members.Select(member => member.CharacterId).ToImmutableHashSet());
        return new IllusionTowerSnapshot(
            id, 11001,
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
        global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.Party.PartyNotificationDelivery>(new NoopNotifications(), new PartyProjectionStreamRegistry(), sessions, town, parties);

    private sealed class TowerDomain(params IllusionTowerSnapshot[] values) : IIllusionTowerDomainService
    {
        private readonly Dictionary<CharacterId, IllusionTowerSnapshot> runs = values
            .SelectMany(run => run.Members.Select(member => (member.Session.CharacterId, Run: run)))
            .ToDictionary(value => value.CharacterId, value => value.Run);

        public IllusionTowerSnapshot? Find(SelectedSessionIdentity session) =>
            runs.GetValueOrDefault(session.CharacterId);

        public IReadOnlyList<TowerActionContext> FindReturnsDue(DateTimeOffset now) =>
            runs.Values.DistinctBy(run => run.RunId)
                .Where(run => run.ReturnDueAt <= now)
                .Select(run => new TowerActionContext(run.Members[0].Session, run.RunId, run.Room.Version))
                .ToArray();

        public bool IsInTower(CharacterId characterId) => runs.ContainsKey(characterId);

        public TowerActionResult Leave(SelectedSessionIdentity session)
        {
            if (!runs.TryGetValue(session.CharacterId, out var current))
                return new TowerActionResult(false, null);
            var members = current.Members
                .Where(member => member.Session != session)
                .OrderBy(member => member.Slot)
                .Select((member, index) => member with { IsLeader = index == 0 })
                .ToImmutableArray();
            runs.Remove(session.CharacterId);
            var changed = current with { Members = members };
            foreach (var member in members)
                runs[member.Session.CharacterId] = changed;
            return new TowerActionResult(true, changed);
        }

        public TowerActionResult Synchronize(SelectedSessionIdentity leader)
        {
            if (!runs.TryGetValue(leader.CharacterId, out var current))
                return new TowerActionResult(false, null);
            var changed = current with
            {
                Members = current.Members
                    .Select(member => member with { IsLeader = member.Session == leader })
                    .ToImmutableArray(),
            };
            foreach (var member in changed.Members)
                runs[member.Session.CharacterId] = changed;
            return new TowerActionResult(true, changed);
        }
        public bool TryReserveOrdinaryEntry(IReadOnlyList<CharacterId> members) => false;
        public ValueTask<bool> SupportsDungeonAsync(int dungeonId, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public ValueTask<bool> CanContinueAsync(TowerActionContext context, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public ValueTask<TowerEntryResult> EnterAsync(IReadOnlyList<TowerEntryMember> members, SelectedSessionIdentity leader, uint seed, CancellationToken cancellationToken, int dungeonId = 11001) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> LoadAsync(TowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> StartAsync(TowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> KillAsync(TowerActionContext context, ushort actorUid, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> NextLevelAsync(TowerActionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> DieAsync(TowerActionContext context, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> LeaveAsync(TowerActionContext context, bool disconnected, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> SynchronizeLeaderAsync(TowerActionContext context, SelectedSessionIdentity leader, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> MarkSettledAsync(TowerActionContext context, TowerSettlementReceipt receipt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<TowerActionResult> SelectReturnAsync(TowerActionContext context, byte option, CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class Commands(Guid? failingRun = null) : ICommandBus, ITowerAlgorithmMutations
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public List<Guid> Attempts { get; } = [];

        public ValueTask<TowerActionResult> IllusionLeave(TowerActionContext context)
        {
            Attempts.Add(context.RunId);
            return context.RunId == failingRun ? ValueTask.FromException<TowerActionResult>(new IOException("closed recipient")) : ValueTask.FromResult(new TowerActionResult(false, null));
        }
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken token = default) where TResult : notnull => throw new NotSupportedException(command.GetType().Name);

    }

    private sealed class CoordinatorCommands(
        TowerDomain towers,
        IPartyService parties,
        CancellationTokenSource request,
        bool throwAfterParty = false) : ICommandBus, ITowerAlgorithmMutations
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public CancellationToken PartyToken { get; private set; }
        public IPartyService Parties => new Dfo.Server.Protocol.Tests.Game.Party.PartyMutationProbe(parties,
            (_, _, _) => { PartyToken = CancellationToken.None; PartyCalls++; }, throwAfterParty);
        public int PartyCalls { get; private set; }
        public int SynchronizeCalls { get; private set; }

        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken token = default) where TResult : notnull => throw new NotSupportedException(command.GetType().Name);
        public ValueTask<TowerActionResult> IllusionSynchronize(TowerActionContext context, SelectedSessionIdentity leader)
        {
            SynchronizeCalls++;
            return ValueTask.FromResult(towers.Synchronize(leader));
        }
        public ValueTask<TowerActionResult> IllusionLeave(TowerActionContext context)
        {
            var result = towers.Leave(context.Session);
            request.Cancel();
            return ValueTask.FromResult(result);
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

    /// <summary>
    /// What the delivery registered, rebuilt as delivery plans from the outbox of a real registration adapter; every
    /// call goes to the real protocol sender over it (TASK-25). <c>failure</c> makes the direct messages for that
    /// connection fail to encode where they are registered; <see cref="Attempts"/> lists the direct recipients of the
    /// calls that reached the registration point. A CMD or NOTI fails there as the old double refused one.
    /// </summary>
    private sealed class Sender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public Sender(Func<NetworkConnectionId, Exception?>? failure = null)
        {
            outbound.Encoder.Fail((connection, message) => outbound.IsDirect(message) ? failure?.Invoke(connection.ConnectionId) : null);
            outbound.FailAny(message => outbound.ExplicitInFlight is null && message is IGameServerMessage,
                () => new NotSupportedException());
        }

        public RecordedView<(NetworkConnectionId Connection, GameExplicitMessageDelivery Delivery)> Deliveries => new(outbound, () => outbound.GameCallDeliveries);
        public List<NetworkConnectionId> Attempts { get; } = [];
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken)
        {
            Attempts.Add(connectionId);
            return outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        }
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId connectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken)
        {
            Attempts.Add(connectionId);
            return outbound.Game.SendExplicitAsync(connectionId, delivery, cancellationToken);
        }
    }

    /// <summary>The closes a real closer registered, read from its outbox (TASK-25).</summary>
    private sealed class Closer : INetworkConnectionCloser
    {
        private readonly RecordedOutbound outbound = new();

        public List<NetworkConnectionId> Closed => [.. outbound.Closes];

        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken cancellationToken) =>
            outbound.Closer.RequestCloseExactAsync(expected, cancellationToken);

        public ValueTask RequestCloseAsync(NetworkConnectionId connectionId, CancellationToken cancellationToken) =>
            outbound.Closer.RequestCloseAsync(connectionId, cancellationToken);
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
