using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// What the server remembers about a run in progress.
/// </summary>
/// <remarks>
/// Deliberately little: the maze the character was given, which room they are in, and which of the
/// pieces lying on the floor have been taken. The last of those is here because what a room holds
/// is read back out of the archive every time the room is described -- without a record of what
/// has gone, a player could walk out of a room and back in and be handed the box again.
/// </remarks>
public sealed class DungeonRunDomainServiceTests
{
    private static readonly CharacterId Character = new(1);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Clear_return_expires_once_and_never_ends_a_replacement(bool replace)
    {
        var service = new DungeonRunDomainService();
        DungeonRunParticipant[] members = [new(new(1), new(1), 0, true), new(new(2), new(2), 1, false)];
        var run = service.PreparePartyEntry(members, Tutorial, DungeonDifficulty.Normal).Run;
        var now = DateTimeOffset.UnixEpoch;
        service.ScheduleClearReturn(Character, run.RunId, now);
        Assert.Empty(service.FindExpiredClearReturns(now.AddMinutes(2)));
        service.TryOpenClear(Character, run.RunId, run.RoomVersion);
        var free = new ClearRewardCardFace(ClearRewardCardKind.Free, [new(0, null)], 1);
        var paid = new ClearRewardCardFace(ClearRewardCardKind.Paid, [new(0, null)], 2);
        foreach (var member in members)
        {
            service.FixClearRewardCards(member.CharacterId, run.RunId, free, paid, 3);
            service.CommitSettlement(service.TryClaimSettlement(member.CharacterId, run.RunId)!);
            service.TryOpenCardLayout(member.CharacterId, run.RunId);
        }
        service.ScheduleClearReturn(Character, run.RunId, now);
        service.ScheduleClearReturn(Character, run.RunId, now.AddSeconds(20));
        Assert.Null(service.TryEndClearReturn(run.RunId, now.AddSeconds(59)));
        if (replace)
        {
            var next = service.PreparePartyEntry(members, Tutorial, DungeonDifficulty.Normal).Run;
            Assert.Null(service.TryEndClearReturn(run.RunId, now.AddSeconds(61)));
            Assert.Equal(next.RunId, service.Find(Character)!.RunId);
        }
        else
        {
            Assert.Equal([run.RunId], service.FindExpiredClearReturns(now.AddSeconds(60)));
            Assert.Equal(2, service.TryEndClearReturn(run.RunId, now.AddSeconds(60))!.Participants.Count);
            Assert.Null(service.Find(Character));
            Assert.Null(service.TryEndClearReturn(run.RunId, now.AddSeconds(61)));
        }
        Assert.Empty(service.FindExpiredClearReturns(now.AddMinutes(2)));
    }

    [Fact]
    public void Party_wipe_waits_for_last_death_and_ten_seconds_then_ends_every_member_once()
    {
        var service = new DungeonRunDomainService();
        var leader = new DungeonRunParticipant(new AccountId(1), new CharacterId(1), 0, true);
        var member = new DungeonRunParticipant(new AccountId(2), new CharacterId(2), 1, false);
        var run = service.PreparePartyEntry([leader, member], Tutorial, DungeonDifficulty.Normal).Run;
        service.TryMarkParticipantReady(leader.CharacterId, run.RunId, run.RoomVersion);
        service.TryMarkParticipantReady(member.CharacterId, run.RunId, run.RoomVersion);
        var now = DateTimeOffset.UnixEpoch;
        service.TryMarkParticipantDead(leader.CharacterId, run.RunId, run.RoomVersion);
        Assert.Empty(service.FindExpiredPartyWipes(now));
        Assert.Empty(service.FindExpiredPartyWipes(now.AddSeconds(20)));
        service.TryMarkParticipantDead(member.CharacterId, run.RunId, run.RoomVersion);
        Assert.Empty(service.FindExpiredPartyWipes(now.AddSeconds(20)));
        Assert.Null(service.TryEndPartyWipe(run.RunId, now.AddSeconds(29)));
        Assert.Empty(service.FindExpiredPartyWipes(now.AddSeconds(29)));
        Assert.Equal([run.RunId], service.FindExpiredPartyWipes(now.AddSeconds(30)));
        var ended = service.TryEndPartyWipe(run.RunId, now.AddSeconds(30));
        Assert.NotNull(ended);
        Assert.Equal(2, ended.Participants.Count);
        Assert.Null(service.Find(leader.CharacterId));
        Assert.Null(service.Find(member.CharacterId));
        Assert.Null(service.TryEndPartyWipe(run.RunId, now.AddSeconds(31)));
        Assert.Empty(service.FindExpiredPartyWipes(now.AddSeconds(31)));
    }

    [Fact]
    public void Old_wipe_cannot_end_a_replacement_run()
    {
        var service = new DungeonRunDomainService();
        var leader = new DungeonRunParticipant(new AccountId(1), Character, 0, true);
        var old = service.PreparePartyEntry([leader], Tutorial, DungeonDifficulty.Normal).Run;
        service.TryMarkParticipantReady(Character, old.RunId, old.RoomVersion);
        service.TryMarkParticipantDead(Character, old.RunId, old.RoomVersion);
        service.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch);
        var next = service.PreparePartyEntry([leader], Tutorial, DungeonDifficulty.Normal).Run;
        Assert.Null(service.TryEndPartyWipe(old.RunId, DateTimeOffset.UnixEpoch.AddSeconds(11)));
        Assert.Empty(service.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));
        Assert.Equal(next.RunId, service.Find(Character)!.RunId);
    }

    [Fact]
    public void Leaving_alive_member_starts_wipe_for_remaining_dead_member()
    {
        var service = new DungeonRunDomainService();
        var leader = new DungeonRunParticipant(new AccountId(1), Character, 0, true);
        var member = new DungeonRunParticipant(new AccountId(2), new CharacterId(2), 1, false);
        var run = service.PreparePartyEntry([leader, member], Tutorial, DungeonDifficulty.Normal).Run;
        service.TryMarkParticipantReady(Character, run.RunId, run.RoomVersion);
        service.TryMarkParticipantDead(Character, run.RunId, run.RoomVersion);
        Assert.Empty(service.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch));
        service.End(member.CharacterId, run.RunId);
        Assert.Empty(service.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(20)));
        var ended = service.TryEndPartyWipe(run.RunId, DateTimeOffset.UnixEpoch.AddSeconds(30));
        Assert.NotNull(ended);
        Assert.Equal(Character, Assert.Single(ended.Participants).CharacterId);
    }


    [Fact]
    public void Death_is_shared_idempotent_and_rejects_stale_or_unloaded_reports()
    {
        var leader = new DungeonRunParticipant(new AccountId(101), new CharacterId(1), 0, true);
        var member = new DungeonRunParticipant(new AccountId(202), new CharacterId(2), 1, false);
        var service = new DungeonRunDomainService();
        var run = service.PreparePartyEntry([leader, member], Tutorial, difficulty: DungeonDifficulty.Normal).Run;
        Assert.Null(service.TryMarkParticipantDead(leader.CharacterId, run.RunId, run.RoomVersion));
        service.TryMarkParticipantReady(leader.CharacterId, run.RunId, run.RoomVersion);
        service.TryMarkParticipantReady(member.CharacterId, run.RunId, run.RoomVersion);
        Assert.Null(service.TryMarkParticipantDead(leader.CharacterId, new DungeonRunId(Guid.NewGuid()), run.RoomVersion));
        Assert.Null(service.TryMarkParticipantDead(leader.CharacterId, run.RunId, run.RoomVersion + 1));
        Assert.Null(service.TryMarkParticipantDead(new CharacterId(99), run.RunId, run.RoomVersion));
        var first = service.TryMarkParticipantDead(leader.CharacterId, run.RunId, run.RoomVersion);
        Assert.NotNull(first);
        Assert.Equal([leader.CharacterId], service.Find(member.CharacterId)!.DeadParticipants);
        Assert.Null(service.TryMarkParticipantDead(leader.CharacterId, run.RunId, run.RoomVersion));
        Assert.Equal(first.RuntimeRevision, service.Find(member.CharacterId)!.RuntimeRevision);
        var second = service.TryMarkParticipantDead(member.CharacterId, run.RunId, run.RoomVersion);
        Assert.NotNull(second);
        Assert.Equal(2, second.DeadParticipants.Count);
        Assert.Single(first.DeadParticipants); // snapshots cannot mutate behind their reader
        var next = service.PreparePartyEntry([leader, member], Tutorial, difficulty: DungeonDifficulty.Normal).Run;
        Assert.Empty(next.DeadParticipants);
        Assert.Null(service.TryMarkParticipantDead(leader.CharacterId, run.RunId, run.RoomVersion));
    }


    [Fact]
    public void Party_entry_changes_share_one_instance_and_readiness_is_monotonic_and_idempotent()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var service = new DungeonRunDomainService();

        var prepared = service.PreparePartyEntry([leader, member], Tutorial, difficulty: (DungeonDifficulty)2);

        Assert.Equal(DungeonEntryChangeKind.Prepared, prepared.Kind);
        Assert.Equal(1, prepared.Revision);
        Assert.Equal([leader.CharacterId, member.CharacterId], prepared.Recipients);
        Assert.Empty(prepared.Run.ReadyParticipants);
        Assert.Equal(prepared.Run.RunId, service.Find(member.CharacterId)!.RunId);

        var memberReady = service.TryMarkParticipantReady(
            member.CharacterId,
            prepared.Run.RunId,
            prepared.Run.RoomVersion);

        Assert.NotNull(memberReady);
        Assert.Equal(DungeonEntryChangeKind.ParticipantReady, memberReady.Kind);
        Assert.Equal(2, memberReady.Revision);
        Assert.Equal([leader.CharacterId, member.CharacterId], memberReady.Recipients);
        Assert.Equal([member.CharacterId], memberReady.Run.ReadyParticipants);

        Assert.Null(service.TryMarkParticipantReady(
            member.CharacterId,
            prepared.Run.RunId,
            prepared.Run.RoomVersion));

        var leaderReady = service.TryMarkParticipantReady(
            leader.CharacterId,
            prepared.Run.RunId,
            prepared.Run.RoomVersion);

        Assert.NotNull(leaderReady);
        Assert.Equal(3, leaderReady.Revision);
        Assert.Equal(
            [leader.CharacterId, member.CharacterId],
            leaderReady.Run.ReadyParticipants.OrderBy(value => value.Value));
    }

    [Fact]
    public void Leader_then_member_loading_produces_the_same_monotonic_ready_set()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        var prepared = service.PreparePartyEntry([leader, member], Tutorial, difficulty: (DungeonDifficulty)2);

        var leaderReady = service.TryMarkParticipantReady(
            leader.CharacterId, prepared.Run.RunId, prepared.Run.RoomVersion);
        var memberReady = service.TryMarkParticipantReady(
            member.CharacterId, prepared.Run.RunId, prepared.Run.RoomVersion);

        Assert.NotNull(leaderReady);
        Assert.Equal([leader.CharacterId], leaderReady.Run.ReadyParticipants);
        Assert.NotNull(memberReady);
        Assert.Equal(prepared.Revision + 2, memberReady.Revision);
        Assert.Equal(
            [leader.CharacterId, member.CharacterId],
            memberReady.Run.ReadyParticipants.OrderBy(value => value.Value));
    }

    [Fact]
    public void Entry_readiness_rejects_the_wrong_run_room_and_departed_participant()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        var prepared = service.PreparePartyEntry([leader, member], Tutorial, difficulty: (DungeonDifficulty)2);

        Assert.Null(service.TryMarkParticipantReady(
            member.CharacterId,
            DungeonRunId.New(),
            prepared.Run.RoomVersion));
        Assert.Null(service.TryMarkParticipantReady(
            member.CharacterId,
            prepared.Run.RunId,
            prepared.Run.RoomVersion + 1));

        Assert.True(service.End(member.CharacterId, prepared.Run.RunId));
        Assert.Null(service.TryMarkParticipantReady(
            member.CharacterId,
            prepared.Run.RunId,
            prepared.Run.RoomVersion));

        var leaderReady = service.TryMarkParticipantReady(
            leader.CharacterId,
            prepared.Run.RunId,
            prepared.Run.RoomVersion);

        Assert.NotNull(leaderReady);
        Assert.Equal([leader.CharacterId], leaderReady.Recipients);
    }

    [Fact]
    public void A_participant_replaced_by_a_new_run_cannot_ready_the_old_instance()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        var oldEntry = service.PreparePartyEntry([leader, member], Tutorial, difficulty: (DungeonDifficulty)2);

        var replacement = service.PreparePartyEntry(
            [member with { Slot = 0, IsLeader = true }],
            Tutorial,
            difficulty: (DungeonDifficulty)2);

        Assert.NotEqual(oldEntry.Run.RunId, replacement.Run.RunId);
        Assert.Null(service.TryMarkParticipantReady(
            member.CharacterId,
            oldEntry.Run.RunId,
            oldEntry.Run.RoomVersion));
        Assert.NotNull(service.TryMarkParticipantReady(
            member.CharacterId,
            replacement.Run.RunId,
            replacement.Run.RoomVersion));
        Assert.Equal(oldEntry.Run.RunId, service.Find(leader.CharacterId)!.RunId);
    }

    [Fact]
    public void Exact_entry_compensation_aborts_the_whole_prepared_instance_atomically()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        var prepared = service.PreparePartyEntry([leader, member], Tutorial, difficulty: (DungeonDifficulty)2);

        Assert.True(service.TryAbortEntry(
            prepared.Run.RunId,
            prepared.Revision,
            prepared.Recipients));

        Assert.Null(service.Find(leader.CharacterId));
        Assert.Null(service.Find(member.CharacterId));
    }

    [Fact]
    public void Entry_compensation_cannot_destroy_a_newer_revision_of_the_same_instance()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        var prepared = service.PreparePartyEntry([leader, member], Tutorial, difficulty: (DungeonDifficulty)2);
        Assert.NotNull(service.TryMarkParticipantReady(
            member.CharacterId,
            prepared.Run.RunId,
            prepared.Run.RoomVersion));

        Assert.False(service.TryAbortEntry(
            prepared.Run.RunId,
            prepared.Revision,
            prepared.Recipients));

        var latest = service.FindEntryChange(leader.CharacterId);
        Assert.NotNull(latest);
        Assert.Equal(prepared.Run.RunId, latest.Run.RunId);
        Assert.Equal(prepared.Revision + 1, latest.Revision);
        Assert.Contains(member.CharacterId, latest.Run.ReadyParticipants);
    }

    [Fact]
    public void Incomplete_or_mismatched_initial_cells_are_rejected_before_publication()
    {
        var service = new DungeonRunDomainService();
        var participant = new DungeonRunParticipant(
            new AccountId(1), Character, Slot: 0, IsLeader: true);
        var incomplete = new DungeonCellSeed(
            Tutorial.StartCell,
            Tutorial.StartRoomMapId,
            FirstSequence,
            [],
            [],
            IsComplete: false);
        var wrongIdentity = DungeonCellSeed.CompleteEmpty(
            new MazeCell(1, 1),
            Tutorial.Rooms[new MazeCell(1, 1)]);

        Assert.Throws<ArgumentException>(() => service.PreparePartyEntry(
            [participant],
            Tutorial,
            difficulty: 0,
            incomplete,
            DungeonRunId.New(),
            DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => service.PreparePartyEntry(
            [participant],
            Tutorial,
            difficulty: 0,
            wrongIdentity,
            DungeonRunId.New(),
            DateTimeOffset.UtcNow));
        Assert.Null(service.Find(Character));
    }

    [Fact]
    public void Unprepared_moves_and_unknown_runtime_identities_cannot_invent_cell_content()
    {
        var service = new DungeonRunDomainService();
        var run = BeginWithCell(service);
        var target = new MazeCell(1, 1);
        var incompleteTarget = new DungeonCellSeed(
            target,
            Tutorial.Rooms[target],
            FirstSequence,
            [],
            [],
            IsComplete: false);

        Assert.Null(service.TryMove(
            Character, run.RunId, run.RoomVersion, target));
        Assert.Null(service.TryMove(
            Character, run.RunId, run.RoomVersion, target, incompleteTarget));
        Assert.Null(service.TryClaimActorDeath(Character, FirstSequence));
        Assert.Null(service.TryClaimGroundDrop(
            Character, run.RunId, run.RoomVersion, groundDropNumber: 1));

        var after = service.Find(Character)!;
        Assert.Equal(Tutorial.StartCell, after.CurrentCell);
        Assert.Empty(after.CurrentCellState.Actors);
        Assert.Empty(after.CurrentCellState.GroundItems);
        Assert.Equal(0, after.CurrentCellState.Revision);
    }

    [Fact]
    public void Two_members_share_world_facts_but_keep_rewards_and_lifetime_per_participant()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        DungeonRunParticipant[] party = [leader, member];
        var service = new DungeonRunDomainService();

        var started = BeginPartyWithCell(
            service,
            party,
            actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0))],
            difficulty: (DungeonDifficulty)2);
        var leaderRun = service.Find(leader.CharacterId);
        var memberRun = service.Find(member.CharacterId);

        Assert.NotEqual(default, started.RunId);
        Assert.NotNull(leaderRun);
        Assert.NotNull(memberRun);
        Assert.Equal(started.RunId, leaderRun.RunId);
        Assert.Equal(started.RunId, memberRun.RunId);
        Assert.Same(leaderRun.Layout, memberRun.Layout);
        Assert.Equal(2, leaderRun.PartyMemberCount);
        Assert.Equal(party, leaderRun.Participants);
        Assert.Equal(leader, leaderRun.Participant);
        Assert.Equal(member, memberRun.Participant);

        Assert.Null(service.TryMove(member.CharacterId, new MazeCell(1, 1)));
        var moved = service.TryMove(
            leader.CharacterId,
            leaderRun.RunId,
            leaderRun.RoomVersion,
            new MazeCell(1, 1),
            CellSeed(
                new MazeCell(1, 1),
                Tutorial.Rooms[new MazeCell(1, 1)],
                actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0))]));
        Assert.NotNull(moved);
        Assert.Equal(moved.CurrentCell, service.Find(member.CharacterId)!.CurrentCell);
        Assert.Equal(moved.RoomVersion, service.Find(member.CharacterId)!.RoomVersion);
        Assert.Null(service.TryMove(
            leader.CharacterId, leaderRun.RunId, leaderRun.RoomVersion, Tutorial.BossCell));

        var death = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(member.CharacterId, FirstSequence));
        Assert.NotNull(service.CommitActorDeath(death));
        Assert.Null(service.TryClaimActorDeath(leader.CharacterId, FirstSequence));
        Assert.True(service.Find(leader.CharacterId)!.CurrentCellState
            .FindActor(FirstSequence)!.IsDead);

        var beforeSettlement = service.Find(leader.CharacterId)!;
        Assert.NotNull(service.TryOpenClear(
            leader.CharacterId, beforeSettlement.RunId, beforeSettlement.RoomVersion));
        Assert.NotNull(service.TrySettle(leader.CharacterId));
        Assert.NotNull(service.TrySettle(member.CharacterId));
        Assert.Null(service.TrySettle(leader.CharacterId));
        Assert.True(service.Find(leader.CharacterId)!.IsSettled);
        Assert.True(service.Find(member.CharacterId)!.IsSettled);

        var free = new ClearRewardCardFace(
            ClearRewardCardKind.Free, [new ClearRewardCardSlot(0, null)], Gold: 1);
        var paid = new ClearRewardCardFace(
            ClearRewardCardKind.Paid, [new ClearRewardCardSlot(0, null)], Gold: 2);
        Assert.NotNull(service.FixClearRewardCards(leader.CharacterId, free, paid, 3));
        Assert.NotNull(service.FixClearRewardCards(member.CharacterId, free, paid, 3));
        Assert.NotNull(service.TryClaimClearRewardCard(
            leader.CharacterId, ClearRewardCardKind.Free, 0));
        Assert.NotNull(service.TryClaimClearRewardCard(
            member.CharacterId, ClearRewardCardKind.Free, 0));

        Assert.True(service.End(leader.CharacterId));
        Assert.Null(service.Find(leader.CharacterId));
        Assert.Equal(started.RunId, service.Find(member.CharacterId)!.RunId);
        Assert.True(service.End(member.CharacterId));
        Assert.Null(service.Find(member.CharacterId));
    }

    [Fact]
    public void Successor_can_advance_the_shared_run_after_the_original_leader_departs()
    {
        var leader = new DungeonRunParticipant(new AccountId(101), new CharacterId(1), 0, true);
        var member = new DungeonRunParticipant(new AccountId(202), new CharacterId(2), 1, false);
        var parties = new PartyService(
            new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
            TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance);
        var scope = new PartyDirectoryScope(11, 1, 1);
        var first = new PartyCharacterSnapshot(
            leader.AccountId, leader.CharacterId, 1, scope);
        var second = new PartyCharacterSnapshot(
            member.AccountId, member.CharacterId, 2, scope);
        Assert.True(parties.RequestInvite(first, second,
            new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
        Assert.True(parties.AcceptInvite(second, first).Succeeded);
        var service = new DungeonRunDomainService(parties);
        var run = BeginPartyWithCell(
            service, [leader, member],
            actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0))],
            difficulty: (DungeonDifficulty)2);

        Assert.True(parties.LeaveParty(leader.CharacterId).Succeeded);
        var current = Assert.IsType<DungeonRunState>(service.Find(member.CharacterId));
        Assert.Equal(parties.FindParty(second).Key, current.PartyKey);
        Assert.Equal(leader.CharacterId, current.EntryPurposeOwner!.CharacterId);
        Assert.Equal(member.CharacterId, Assert.Single(current.Participants).CharacterId);
        Assert.True(Assert.Single(current.Participants).IsLeader);
        Assert.Null(service.Find(leader.CharacterId));
        Assert.True(service.End(leader.CharacterId, run.RunId));
        var moved = service.TryMove(
            member.CharacterId, current.RunId, current.RoomVersion,
            new MazeCell(1, 1),
            CellSeed(new MazeCell(1, 1), Tutorial.Rooms[new MazeCell(1, 1)],
                actors: []));

        Assert.NotNull(moved);
        Assert.Equal(new MazeCell(1, 1), moved.CurrentCell);
    }

    [Fact]
    public void A_member_joining_the_party_after_entry_is_not_admitted_to_the_existing_run()
    {
        var leader = new DungeonRunParticipant(new AccountId(101), new CharacterId(1), 0, true);
        var member = new DungeonRunParticipant(new AccountId(202), new CharacterId(2), 1, false);
        var outsider = new PartyCharacterSnapshot(
            new AccountId(303), new CharacterId(3), 3, new PartyDirectoryScope(11, 1, 1));
        var parties = new PartyService(
            new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
            TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance);
        var first = new PartyCharacterSnapshot(
            leader.AccountId, leader.CharacterId, 1, outsider.DirectoryScope);
        var second = new PartyCharacterSnapshot(
            member.AccountId, member.CharacterId, 2, outsider.DirectoryScope);
        Assert.True(parties.RequestInvite(first, second,
            new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
        Assert.True(parties.AcceptInvite(second, first).Succeeded);
        var runs = new DungeonRunDomainService(parties);
        var run = BeginPartyWithCell(runs, [leader, member]);

        Assert.True(parties.RequestInvite(first, outsider,
            new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
        Assert.True(parties.AcceptInvite(outsider, first).Succeeded);

        Assert.Null(runs.Find(outsider.CharacterId));
        Assert.Equal(2, runs.Find(run.RunId)!.Participants.Count);
        Assert.Equal([leader.CharacterId, member.CharacterId],
            runs.Find(run.RunId)!.Participants.Select(value => value.CharacterId));
    }

    private static readonly DungeonLayout Tutorial = new(
        DungeonId: 10000,
        MazeIndex: 0,
        Width: 3,
        Height: 2,
        StartCell: new MazeCell(0, 1),
        BossCell: new MazeCell(2, 1),
        StartRoomMapId: 61000,
        Seed: 0x0BADF00D,
        Rooms: new Dictionary<MazeCell, int>
        {
            [new MazeCell(0, 1)] = 61000,
            [new MazeCell(1, 1)] = 61001,
            [new MazeCell(2, 1)] = 61005,
        });

    [Fact]
    public void A_run_opens_with_nothing_taken_off_its_floor()
    {
        var service = new DungeonRunDomainService();

        var run = BeginWithCell(service, groundItems: [DeclaredItem(1)]);

        Assert.Single(run.CurrentCellState.GroundItems);
    }

    [Fact]
    public void A_piece_can_be_taken_once()
    {
        var service = new DungeonRunDomainService();
        var run = BeginWithCell(service, groundItems: [DeclaredItem(1)]);
        var afterTaking = Take(service, run, 1);

        Assert.NotNull(afterTaking);
        Assert.Empty(afterTaking.CurrentCellState.GroundItems);
        Assert.Null(service.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, 1));
    }

    /// <summary>
    /// Walking out of a cleared room and back into it does not put the piece back on the floor.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the run remembers anything about the floor. The room's own
    /// declaration is unchanged -- the archive still says the box holds a shoulder -- so a revisit
    /// that consulted only the archive would describe the room as it was before the player emptied
    /// it, and hand over a second shoulder for the asking.
    /// </remarks>
    [Fact]
    public void A_revisited_room_does_not_get_its_floor_back()
    {
        var service = new DungeonRunDomainService();
        var started = BeginWithCell(service, groundItems: [DeclaredItem(1)]);
        _ = Take(service, started, 1);

        _ = MoveToEmptyCell(service, service.Find(Character)!, new MazeCell(1, 1));
        var returned = service.TryMove(Character, new MazeCell(0, 1));

        Assert.NotNull(returned);
        Assert.Empty(returned.CurrentCellState.GroundItems);
        Assert.Null(service.TryClaimGroundDrop(
            Character, returned.RunId, returned.RoomVersion, 1));
    }

    /// <summary>Emptying one room's floor leaves the next room's alone.</summary>
    [Fact]
    public void The_next_room_still_has_what_it_declares()
    {
        var service = new DungeonRunDomainService();
        var started = BeginWithCell(service, groundItems: [DeclaredItem(1)]);
        _ = Take(service, started, 1);

        var target = new MazeCell(1, 1);
        var moved = service.TryMove(
            Character,
            started.RunId,
            started.RoomVersion,
            target,
            CellSeed(target, Tutorial.Rooms[target], groundItems: [DeclaredItem(1)]));

        Assert.NotNull(moved);
        Assert.Single(moved.CurrentCellState.GroundItems);
        Assert.Equal((ushort)2, Assert.Single(moved.CurrentCellState.GroundItems).Number);
        Assert.NotNull(Take(service, moved, 2));
    }

    /// <summary>
    /// A room is clear when every monster it declares has been reported dead -- allies excepted.
    /// </summary>
    /// <remarks>
    /// This is what the dungeon's exit waits on. The tutorial's first room holds one monster and
    /// one escort, and a room whose clearing waited on the escort would never clear at all.
    /// </remarks>
    [Fact]
    public void A_room_is_clear_once_its_monsters_are_dead_and_its_allies_are_not_counted()
    {
        var service = new DungeonRunDomainService();
        var run = BeginWithCell(
            service,
            actors:
            [
                CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0)),
                CellActor((ushort)(FirstSequence + 1), new DungeonRoomActor(461, 80, 5), isAi: true),
            ]);

        Assert.False(run.IsCleared);

        var claim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        var afterKill = service.CommitActorDeath(claim);

        Assert.NotNull(afterKill);
        Assert.True(afterKill.IsCleared);
        Assert.Null(service.TryClaimActorDeath(Character, FirstSequence));
    }

    [Fact]
    public void AFailedDurableKillCanReleaseItsDeathClaimForOneRetry()
    {
        var service = new DungeonRunDomainService();
        _ = BeginWithCell(
            service,
            actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0))]);
        var claimed = service.TryClaimActorDeath(Character, FirstSequence);
        Assert.NotNull(claimed);

        Assert.True(service.ReleaseActorDeath(claimed!));
        var retry = service.TryClaimActorDeath(Character, FirstSequence);
        Assert.NotNull(retry);
        Assert.False(service.ReleaseActorDeath(claimed));
        Assert.NotNull(service.CommitActorDeath(retry!));
        Assert.Null(service.TryClaimActorDeath(Character, FirstSequence));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Committing_actor_death_moves_its_pending_loot_into_the_same_cell(bool apc)
    {
        var service = new DungeonRunDomainService();
        var participant = new DungeonRunParticipant(
            new AccountId(1), Character, Slot: 0, IsLeader: true);
        var pending = new RolledDrop(
            99, 2, DropRarity.Rare, DropItemKind.Stackable);
        var initial = new DungeonGroundItem(
            1,
            14400,
            1,
            25,
            IsEquipment: false,
            DungeonGroundItemSource.Declared(0));
        var seed = new DungeonCellSeed(
            Tutorial.StartCell,
            Tutorial.StartRoomMapId,
            FirstSequence,
            [new DungeonCellActorState(
                FirstSequence,
                new DungeonRoomActor(61670, 1, apc ? (byte)5 : (byte)0),
                IsAiCharacter: apc,
                IsDead: false,
                [pending], IsMonster: !apc)],
            [initial]);
        var runId = DungeonRunId.New();
        service.PreparePartyEntry(
            [participant], Tutorial, difficulty: 0, seed, runId, DateTimeOffset.UtcNow);

        var claim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        var change = Assert.IsType<DungeonActorDeathChange>(
            service.CommitActorDeathChange(claim));
        var committed = change.Run;

        Assert.True(committed.CurrentCellState.FindActor(FirstSequence)!.IsDead);
        Assert.Equal(1, committed.CurrentCellState.Revision);
        Assert.Equal(committed.RuntimeRevision, change.Revision);
        Assert.Equal(committed.CurrentCellState.Revision, change.CellRevision);
        var dropped = Assert.Single(change.AddedGroundItems);
        Assert.Equal((ushort)2, dropped.Number);
        Assert.Equal(99, dropped.ItemId);
        Assert.Equal(2, dropped.Count);
        Assert.Same(
            dropped,
            Assert.Single(
                committed.CurrentCellState.GroundItems,
                item => item.Source == DungeonGroundItemSource.FromActor(FirstSequence)));
    }

    [Fact]
    public void Death_materializes_shared_loot_but_skips_a_departed_members_personal_loot()
    {
        var leader = new DungeonRunParticipant(new AccountId(101), new CharacterId(1), 0, true);
        var member = new DungeonRunParticipant(new AccountId(202), new CharacterId(2), 1, false);
        var parties = new PartyService(
            new StubPartyMemberStatusDomainService(), new PartyModeRuleService(),
            TimeProvider.System, EmptyPartyMemberSessionEndpointSource.Instance);
        var scope = new PartyDirectoryScope(11, 1, 1);
        var first = new PartyCharacterSnapshot(leader.AccountId, leader.CharacterId, 1, scope);
        var second = new PartyCharacterSnapshot(member.AccountId, member.CharacterId, 2, scope);
        Assert.True(parties.RequestInvite(first, second,
            new PartyInviteDisplayState(0, 0, 0, 0)).Succeeded);
        Assert.True(parties.AcceptInvite(second, first).Succeeded);
        var service = new DungeonRunDomainService(parties);
        _ = BeginPartyWithCell(service, [leader, member],
            actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0),
                pendingLoot:
                [
                    new RolledDrop(1001, 1, DropRarity.Common, DropItemKind.Stackable,
                        OwnerCharacterId: leader.CharacterId),
                    new RolledDrop(1002, 1, DropRarity.Common, DropItemKind.Stackable),
                ])]);

        Assert.True(parties.LeaveParty(leader.CharacterId).Succeeded);
        var claim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(member.CharacterId, FirstSequence));
        var change = Assert.IsType<DungeonActorDeathChange>(service.CommitActorDeathChange(claim));

        Assert.Equal(1002, Assert.Single(change.AddedGroundItems).ItemId);
        Assert.Equal(member.CharacterId, Assert.Single(change.Recipients));
        Assert.Null(service.Find(leader.CharacterId));
    }

    [Fact]
    public void Direct_quest_item_stays_off_the_floor_when_its_actor_dies()
    {
        var service = new DungeonRunDomainService();
        _ = BeginWithCell(
            service,
            actors:
            [
                CellActor(
                    FirstSequence,
                    new DungeonRoomActor(65601, 1, 0),
                    pendingLoot:
                    [
                        new RolledDrop(
                            10149163, 1, DropRarity.Common, DropItemKind.Stackable,
                            OwnerCharacterId: Character,
                            Delivery: RolledDropDelivery.DirectQuestItem,
                            QuestId: 1779,
                            GrantLimit: 15),
                    ]),
            ]);

        var claim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        var change = Assert.IsType<DungeonActorDeathChange>(service.CommitActorDeathChange(claim));

        Assert.Empty(change.AddedGroundItems);
        Assert.Empty(change.Run.CurrentCellState.GroundItems);
        Assert.Null(service.TryClaimActorDeath(Character, FirstSequence));
    }

    [Fact]
    public void Boss_death_clears_remaining_monsters_without_paying_their_pending_loot()
    {
        var service = new DungeonRunDomainService();
        var run = BeginWithCell(service);
        run = Assert.IsType<DungeonRunState>(MoveToEmptyCell(
            service, run, new MazeCell(1, 1)));
        var boss = FirstSequence;
        var remaining = checked((ushort)(FirstSequence + 1));
        run = Assert.IsType<DungeonRunState>(service.TryMove(
            Character, run.RunId, run.RoomVersion, Tutorial.BossCell,
            CellSeed(Tutorial.BossCell, Tutorial.Rooms[Tutorial.BossCell],
            [
                CellActor(boss, new DungeonRoomActor(65303, 1, (byte)MonsterRank.Boss)),
                CellActor(remaining, new DungeonRoomActor(65601, 1, 0),
                    pendingLoot: [new RolledDrop(10088621, 1, DropRarity.Common,
                        DropItemKind.Stackable)]),
            ])));

        var claim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, boss));
        var change = Assert.IsType<DungeonActorDeathChange>(service.CommitActorDeathChange(claim));

        Assert.True(change.IsCellCleared);
        Assert.Equal([remaining], change.AutoKilledActorSequences);
        Assert.True(change.Run.CurrentCellState.FindActor(remaining)!.IsDead);
        Assert.Empty(change.Run.CurrentCellState.GroundItems);
        Assert.Null(service.TryClaimActorDeath(Character, remaining));
        Assert.Null(service.TryClaimActorDeath(Character, boss));
        var recorded = Assert.Single(change.Run.Kills);
        Assert.Equal(new RoomActorId(Tutorial.BossCell, boss), recorded.Actor);
        Assert.Equal(65303, recorded.MonsterCode);
        Assert.Equal(MonsterRank.Boss, recorded.Rank);
        Assert.Equal(1, change.Run.KillSummary.Boss);
        Assert.Equal(0, change.Run.KillSummary.Ordinary);
    }

    [Fact]
    public void Boss_death_leaves_an_already_claimed_kill_to_finish_its_own_effects()
    {
        var service = new DungeonRunDomainService();
        var run = BeginWithCell(service);
        run = Assert.IsType<DungeonRunState>(MoveToEmptyCell(
            service, run, new MazeCell(1, 1)));
        var other = checked((ushort)(FirstSequence + 1));
        run = Assert.IsType<DungeonRunState>(service.TryMove(
            Character, run.RunId, run.RoomVersion, Tutorial.BossCell,
            CellSeed(Tutorial.BossCell, Tutorial.Rooms[Tutorial.BossCell],
            [
                CellActor(FirstSequence,
                    new DungeonRoomActor(65303, 1, (byte)MonsterRank.Boss)),
                CellActor(other, new DungeonRoomActor(65601, 1, 0)),
            ])));

        var otherClaim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, other));
        var bossClaim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        var bossChange = Assert.IsType<DungeonActorDeathChange>(
            service.CommitActorDeathChange(bossClaim));

        Assert.Empty(bossChange.AutoKilledActorSequences);
        Assert.False(bossChange.IsCellCleared);
        var otherChange = Assert.IsType<DungeonActorDeathChange>(
            service.CommitActorDeathChange(otherClaim));
        Assert.True(otherChange.IsCellCleared);
    }

    [Fact]
    public void Removing_a_drop_never_reuses_its_cell_identity_for_a_later_death()
    {
        var service = new DungeonRunDomainService();
        var run = BeginWithCell(
            service,
            actors:
            [
                CellActor(
                    FirstSequence,
                    new DungeonRoomActor(61670, 1, 0),
                    pendingLoot: [new RolledDrop(1001, 1, DropRarity.Common, DropItemKind.Stackable)]),
                CellActor(
                    (ushort)(FirstSequence + 1),
                    new DungeonRoomActor(61671, 1, 0),
                    pendingLoot: [new RolledDrop(1002, 1, DropRarity.Common, DropItemKind.Stackable)]),
            ],
            groundItems: [DeclaredItem(1)]);
        var first = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        var firstChange = Assert.IsType<DungeonActorDeathChange>(
            service.CommitActorDeathChange(first));
        var firstDrop = Assert.Single(firstChange.AddedGroundItems);
        var pickup = Assert.IsType<DungeonGroundDropClaim>(service.TryClaimGroundDrop(
            Character, run.RunId, run.RoomVersion, firstDrop.Number));
        Assert.NotNull(service.CommitGroundDrop(pickup, DungeonPickupKind.Item));

        var second = Assert.IsType<DungeonActorDeathClaim>(service.TryClaimActorDeath(
            Character, (ushort)(FirstSequence + 1)));
        var secondChange = Assert.IsType<DungeonActorDeathChange>(
            service.CommitActorDeathChange(second));

        Assert.Equal((ushort)2, firstDrop.Number);
        Assert.Equal((ushort)3, Assert.Single(secondChange.AddedGroundItems).Number);
    }

    [Fact]
    public void Committed_pickups_publish_one_monotonic_change_to_active_participants()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        var run = BeginPartyWithCell(
            service,
            [leader, member],
            groundItems: [DeclaredItem(1), DeclaredItem(2)]);
        var first = Assert.IsType<DungeonGroundDropClaim>(
            service.TryClaimGroundDrop(leader.CharacterId, run.RunId, run.RoomVersion, 1));

        var item = Assert.IsType<DungeonPickupChange>(
            service.CommitGroundDrop(first, DungeonPickupKind.Item));

        Assert.Equal(run.RunId, item.RunId);
        Assert.Equal(1, item.Revision);
        Assert.Equal(leader.CharacterId, item.Picker);
        Assert.Equal(new GroundDropId(run.CurrentCell, 1), item.Drop);
        Assert.Equal(DungeonPickupKind.Item, item.Kind);
        Assert.Equal([leader.CharacterId, member.CharacterId], item.Recipients);
        Assert.Null(service.TryClaimGroundDrop(
            member.CharacterId, run.RunId, run.RoomVersion, 1));

        var second = Assert.IsType<DungeonGroundDropClaim>(
            service.TryClaimGroundDrop(member.CharacterId, run.RunId, run.RoomVersion, 2));
        Assert.True(service.End(leader.CharacterId, run.RunId));

        var gold = Assert.IsType<DungeonPickupChange>(
            service.CommitGroundDrop(second, DungeonPickupKind.Gold));

        Assert.Equal(2, gold.Revision);
        Assert.Equal([member.CharacterId], gold.Recipients);
    }

    [Fact]
    public void Committed_pickup_removes_the_item_from_the_current_cell_only_after_commit()
    {
        var service = new DungeonRunDomainService();
        var participant = new DungeonRunParticipant(
            new AccountId(1), Character, Slot: 0, IsLeader: true);
        var item = new DungeonGroundItem(
            1,
            14400,
            1,
            25,
            IsEquipment: true,
            DungeonGroundItemSource.Declared(0),
            Kind: DropItemKind.Equipment);
        var seed = new DungeonCellSeed(
            Tutorial.StartCell,
            Tutorial.StartRoomMapId,
            FirstSequence,
            [],
            [item]);
        var runId = DungeonRunId.New();
        var started = service.PreparePartyEntry(
            [participant], Tutorial, difficulty: 0, seed, runId, DateTimeOffset.UtcNow).Run;

        var claim = Assert.IsType<DungeonGroundDropClaim>(
            service.TryClaimGroundDrop(Character, runId, started.RoomVersion, 1));
        Assert.NotNull(service.Find(Character)!.CurrentCellState.FindGroundItem(1));

        var change = Assert.IsType<DungeonPickupChange>(
            service.CommitGroundDrop(claim, DungeonPickupKind.Item));

        Assert.Null(service.Find(Character)!.CurrentCellState.FindGroundItem(1));
        Assert.Equal(1, service.Find(Character)!.CurrentCellState.Revision);
        Assert.Equal(1, change.CellRevision);
        Assert.Null(service.TryClaimGroundDrop(Character, runId, started.RoomVersion, 1));
    }

    [Fact]
    public void A_retried_party_death_claims_only_the_participant_whose_effect_is_unfinished()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        _ = BeginPartyWithCell(
            service,
            [leader, member],
            actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0))]);

        var firstDeath = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(leader.CharacterId, FirstSequence));
        var leaderEffect = Assert.IsType<DungeonParticipantKillEffectClaim>(
            service.TryClaimParticipantKillEffect(firstDeath, leader.CharacterId));
        var award = new ExperienceAward(new ExperienceBreakdown(Base: 90, Channel: 9), 1, 99, false);
        Assert.True(service.CommitParticipantKillEffect(leaderEffect, award));
        Assert.False(service.CommitParticipantKillEffect(leaderEffect, award));
        Assert.Equal(90u, service.Find(leader.CharacterId)!.KillExperience.Ordinary);
        Assert.Equal(0u, service.Find(member.CharacterId)!.KillExperience.Ordinary);
        var failedMemberEffect = Assert.IsType<DungeonParticipantKillEffectClaim>(
            service.TryClaimParticipantKillEffect(firstDeath, member.CharacterId));
        Assert.True(service.ReleaseParticipantKillEffect(failedMemberEffect));
        Assert.True(service.ReleaseActorDeath(firstDeath));

        var retry = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(member.CharacterId, FirstSequence));
        Assert.Null(service.TryClaimParticipantKillEffect(retry, leader.CharacterId));
        var retriedMember = Assert.IsType<DungeonParticipantKillEffectClaim>(
            service.TryClaimParticipantKillEffect(retry, member.CharacterId));
        Assert.True(service.CommitParticipantKillEffect(retriedMember,
            new ExperienceAward(new ExperienceBreakdown(Base: 45), 1, 45, false)));
        Assert.Equal(90u, service.Find(leader.CharacterId)!.KillExperience.Ordinary);
        Assert.Equal(45u, service.Find(member.CharacterId)!.KillExperience.Ordinary);
        Assert.NotNull(service.CommitActorDeath(retry));
        Assert.Null(service.TryClaimActorDeath(leader.CharacterId, FirstSequence));
    }

    [Fact]
    public void Actor_pending_loot_is_fixed_once_even_when_the_death_attempt_is_retried()
    {
        var service = new DungeonRunDomainService();
        var first = new RolledDrop(1001, 1, DropRarity.Common, DropItemKind.Stackable);
        _ = BeginWithCell(
            service,
            actors: [CellActor(
                FirstSequence,
                new DungeonRoomActor(61670, 1, 0),
                pendingLoot: [first])]);

        var failed = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        Assert.True(service.ReleaseActorDeath(failed));
        var retry = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        var committed = Assert.IsType<DungeonRunState>(service.CommitActorDeath(retry));

        Assert.Equal(1001, Assert.Single(committed.CurrentCellState.GroundItems).ItemId);
        Assert.Null(service.TryClaimActorDeath(Character, FirstSequence));
    }

    [Fact]
    public void A_failed_settlement_claim_can_retry_but_a_committed_one_cannot()
    {
        var service = new DungeonRunDomainService();
        var run = service.Begin(Character, Tutorial, difficulty: 0);
        Assert.NotNull(service.TryOpenClear(Character, run.RunId, run.RoomVersion));

        var failed = Assert.IsType<DungeonSettlementClaim>(
            service.TryClaimSettlement(Character));
        Assert.True(service.ReleaseSettlement(failed));
        var retry = Assert.IsType<DungeonSettlementClaim>(
            service.TryClaimSettlement(Character));
        Assert.NotNull(service.CommitSettlement(retry));

        Assert.Null(service.TryClaimSettlement(Character));
        Assert.True(service.Find(Character)!.IsSettled);
    }

    [Fact]
    public void A_move_must_be_one_orthogonal_step()
    {
        var service = new DungeonRunDomainService();
        var run = service.Begin(Character, Tutorial, difficulty: 0);

        Assert.Null(service.TryMove(
            Character, run.RunId, run.RoomVersion, Tutorial.BossCell));
        Assert.NotNull(service.TryMove(
            Character,
            run.RunId,
            run.RoomVersion,
            new MazeCell(1, 1),
            CellSeed(new MazeCell(1, 1), Tutorial.Rooms[new MazeCell(1, 1)])));
    }

    [Fact]
    public void Exact_end_does_not_remove_a_replacement_run()
    {
        var service = new DungeonRunDomainService();
        var old = service.Begin(Character, Tutorial, difficulty: 0);
        var replacement = service.Begin(Character, Tutorial, difficulty: (DungeonDifficulty)1);
        Assert.False(service.End(Character, old.RunId));
        Assert.Equal(replacement.RunId, service.Find(Character)!.RunId);
        Assert.True(service.End(Character, replacement.RunId));
    }

    [Fact]
    public void Pending_death_is_not_clear_and_blocks_room_movement_until_commit_or_release()
    {
        var service = new DungeonRunDomainService();
        var run = BeginWithCell(
            service,
            actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0))]);
        var claim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));

        Assert.False(service.Find(Character)!.CurrentCellState
            .FindActor(FirstSequence)!.IsDead);
        Assert.Null(service.TryMove(
            Character, run.RunId, run.RoomVersion, new MazeCell(1, 1)));

        Assert.True(service.ReleaseActorDeath(claim));
        Assert.NotNull(service.TryMove(
            Character,
            run.RunId,
            run.RoomVersion,
            new MazeCell(1, 1),
            CellSeed(new MazeCell(1, 1), Tutorial.Rooms[new MazeCell(1, 1)])));
    }

    [Fact]
    public void Room_clear_linearizes_on_the_last_committed_death_not_on_pending_claims()
    {
        var service = new DungeonRunDomainService();
        _ = BeginWithCell(
            service,
            actors:
            [
                CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0)),
                CellActor((ushort)(FirstSequence + 1), new DungeonRoomActor(61671, 1, 0)),
            ]);
        var first = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        var second = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, (ushort)(FirstSequence + 1)));

        var firstCommitted = Assert.IsType<DungeonRunState>(service.CommitActorDeath(first));
        Assert.False(firstCommitted.IsCleared);
        Assert.True(service.ReleaseActorDeath(second));
        Assert.False(service.Find(Character)!.IsCleared);

        var retry = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, (ushort)(FirstSequence + 1)));
        var lastCommitted = Assert.IsType<DungeonRunState>(service.CommitActorDeath(retry));
        Assert.True(lastCommitted.IsCleared);
    }

    [Fact]
    public void An_empty_pending_loot_plan_stays_empty_after_death()
    {
        var service = new DungeonRunDomainService();
        _ = BeginWithCell(
            service,
            actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0))]);

        var claim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        var committed = Assert.IsType<DungeonRunState>(service.CommitActorDeath(claim));

        Assert.Empty(committed.CurrentCellState.GroundItems);
    }

    [Fact]
    public void Exact_settlement_cannot_claim_a_replacement_run()
    {
        var service = new DungeonRunDomainService();
        var old = service.Begin(Character, Tutorial, difficulty: 0);
        var replacement = service.Begin(Character, Tutorial, difficulty: (DungeonDifficulty)1);
        Assert.NotNull(service.TryOpenClear(
            Character, replacement.RunId, replacement.RoomVersion));

        Assert.Null(service.TryClaimSettlement(Character, old.RunId));
        Assert.NotNull(service.TryClaimSettlement(Character, replacement.RunId));
    }

    [Fact]
    public void Ground_drop_reservation_releases_for_retry_and_commits_once()
    {
        var service = new DungeonRunDomainService();
        var run = BeginWithCell(service, groundItems: [DeclaredItem(1)]);
        var first = Assert.IsType<DungeonGroundDropClaim>(
            service.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, 1));

        Assert.Null(service.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, 1));
        Assert.True(service.ReleaseGroundDrop(first));
        var retry = Assert.IsType<DungeonGroundDropClaim>(
            service.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, 1));
        Assert.NotNull(service.CommitGroundDrop(retry, DungeonPickupKind.Item));
        Assert.Null(service.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, 1));
    }

    [Fact]
    public void A_ground_claim_linearizes_room_movement_until_it_commits_or_releases()
    {
        var service = new DungeonRunDomainService();
        var run = BeginWithCell(service, groundItems: [DeclaredItem(1)]);
        var target = new MazeCell(1, 1);
        var claim = Assert.IsType<DungeonGroundDropClaim>(service.TryClaimGroundDrop(
            Character, run.RunId, run.RoomVersion, groundDropNumber: 1));

        Assert.Null(service.TryMove(
            Character,
            run.RunId,
            run.RoomVersion,
            target,
            CellSeed(target, Tutorial.Rooms[target], groundItems: [DeclaredItem(1)])));

        var change = Assert.IsType<DungeonPickupChange>(
            service.CommitGroundDrop(claim, DungeonPickupKind.Item));
        Assert.Equal(Tutorial.StartCell, change.Drop.Cell);
        Assert.Equal(run.RoomVersion, change.RoomVersion);
        Assert.Null(service.Find(Character)!.CurrentCellState.FindGroundItem(1));

        var moved = Assert.IsType<DungeonRunState>(service.TryMove(
            Character,
            run.RunId,
            run.RoomVersion,
            target,
            CellSeed(target, Tutorial.Rooms[target], groundItems: [DeclaredItem(1)])));
        Assert.Equal(target, moved.CurrentCell);
        Assert.Null(moved.CurrentCellState.FindGroundItem(1));
        Assert.NotNull(moved.CurrentCellState.FindGroundItem(2));
    }

    [Fact]
    public void Source_neutral_ground_addition_uses_the_same_cell_collection_and_revision()
    {
        var service = new DungeonRunDomainService();
        var run = BeginWithCell(service);
        DungeonGroundItemDraft[] drafts =
        [
            new(
                ItemId: 3020,
                Count: 2,
                Durability: 0,
                IsEquipment: false,
                DungeonGroundItemSource.FromPlayer()),
        ];

        var change = Assert.IsType<DungeonGroundItemsAddedChange>(service.TryAddGroundItems(
            Character,
            run.RunId,
            run.RoomVersion,
            run.CurrentCellState.Revision,
            drafts));

        var item = Assert.Single(change.AddedGroundItems);
        Assert.Equal(DungeonGroundItemSourceKind.PlayerDrop, item.Source.Kind);
        Assert.Same(item, Assert.Single(change.Run.CurrentCellState.GroundItems));
        Assert.Equal(1, change.CellRevision);
        Assert.Null(service.TryAddGroundItems(
            Character,
            run.RunId,
            run.RoomVersion,
            expectedCellRevision: 0,
            drafts));
    }

    [Fact]
    public void Cell_adoption_and_snapshots_defensively_copy_pending_loot()
    {
        var pending = new List<RolledDrop>
        {
            new(1001, 1, DropRarity.Common, DropItemKind.Stackable),
        };
        var service = new DungeonRunDomainService();
        _ = BeginWithCell(
            service,
            actors:
            [
                CellActor(
                    FirstSequence,
                    new DungeonRoomActor(61670, 1, 0),
                    pendingLoot: pending),
            ]);

        pending.Add(new RolledDrop(1002, 1, DropRarity.Common, DropItemKind.Stackable));
        var first = service.Find(Character)!.CurrentCellState.FindActor(FirstSequence)!;
        Assert.Single(first.PendingLoot);
        ((RolledDrop[])first.PendingLoot)[0] = new RolledDrop(
            9999, 1, DropRarity.Unique, DropItemKind.Stackable);

        var reread = service.Find(Character)!.CurrentCellState.FindActor(FirstSequence)!;
        Assert.Equal(1001, Assert.Single(reread.PendingLoot).ItemId);
    }

    [Fact]
    public void A_durable_ground_drop_claim_can_commit_after_the_picker_leaves_a_shared_run()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        var run = BeginPartyWithCell(
            service,
            [leader, member],
            groundItems: [DeclaredItem(1)]);
        var claim = Assert.IsType<DungeonGroundDropClaim>(
            service.TryClaimGroundDrop(
                leader.CharacterId, run.RunId, run.RoomVersion, groundDropNumber: 1));

        Assert.True(service.End(leader.CharacterId, run.RunId));
        Assert.NotNull(service.CommitGroundDrop(claim, DungeonPickupKind.Item));
        Assert.Null(service.TryClaimGroundDrop(
            member.CharacterId, run.RunId, run.RoomVersion, groundDropNumber: 1));
    }

    [Fact]
    public void Clear_card_reservation_releases_for_retry_and_commits_once()
    {
        var service = new DungeonRunDomainService();
        var run = service.Begin(Character, Tutorial, difficulty: 0);
        var free = new ClearRewardCardFace(
            ClearRewardCardKind.Free, [new ClearRewardCardSlot(0, null)], Gold: 1);
        var paid = new ClearRewardCardFace(
            ClearRewardCardKind.Paid, [new ClearRewardCardSlot(0, null)], Gold: 2);
        service.FixClearRewardCards(Character, run.RunId, free, paid, 3);
        Assert.NotNull(service.TryOpenClear(Character, run.RunId, run.RoomVersion));
        var settlement = Assert.IsType<DungeonSettlementClaim>(
            service.TryClaimSettlement(Character, run.RunId));
        Assert.NotNull(service.CommitSettlement(settlement));
        Assert.NotNull(service.TryOpenCardLayout(Character, run.RunId));

        var first = Assert.IsType<DungeonClearRewardCardClaim>(
            service.TryReserveClearRewardCard(
                Character, run.RunId, ClearRewardCardKind.Free, 0));
        Assert.Null(service.TryReserveClearRewardCard(
            Character, run.RunId, ClearRewardCardKind.Free, 0));
        Assert.True(service.ReleaseClearRewardCard(first));
        var retry = Assert.IsType<DungeonClearRewardCardClaim>(
            service.TryReserveClearRewardCard(
                Character, run.RunId, ClearRewardCardKind.Free, 0));
        Assert.NotNull(service.CommitClearRewardCard(retry).Change);
        Assert.Null(service.TryReserveClearRewardCard(
            Character, run.RunId, ClearRewardCardKind.Free, 0));
    }

    [Fact]
    public async Task Clear_card_selection_is_published_only_after_the_transaction_commits()
    {
        var scope = new DomainTransactionScope();
        var service = new DungeonRunDomainService(scope);
        var run = PrepareCardPanel(service);

        await using var transaction = scope.Begin();
        var claim = Assert.IsType<DungeonClearRewardCardClaim>(
            service.TryReserveClearRewardCard(
                Character, run.RunId, ClearRewardCardKind.Free, 0));
        var receipt = service.CommitClearRewardCard(claim);

        Assert.Null(receipt.Change);
        Assert.Empty(service.Find(Character)!.CardSelections);

        await transaction.CommitAsync(CancellationToken.None);

        Assert.NotNull(receipt.Change);
        Assert.Single(service.Find(Character)!.CardSelections);
    }

    [Fact]
    public async Task Durable_commit_failure_rolls_back_the_card_selection_and_allows_one_retry()
    {
        var scope = new DomainTransactionScope();
        var service = new DungeonRunDomainService(scope);
        var run = PrepareCardPanel(service);

        await using (var transaction = scope.Begin())
        {
            var claim = Assert.IsType<DungeonClearRewardCardClaim>(
                service.TryReserveClearRewardCard(
                    Character, run.RunId, ClearRewardCardKind.Free, 0));
            var receipt = service.CommitClearRewardCard(claim);
            _ = scope.Current!.Enlist("failing durable store", static () => new FailingCommit());

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await transaction.CommitAsync(CancellationToken.None));

            Assert.Null(receipt.Change);
            Assert.Empty(service.Find(Character)!.CardSelections);
        }

        var retry = Assert.IsType<DungeonClearRewardCardClaim>(
            service.TryReserveClearRewardCard(
                Character, run.RunId, ClearRewardCardKind.Free, 0));
        Assert.NotNull(service.CommitClearRewardCard(retry).Change);
        Assert.Null(service.TryReserveClearRewardCard(
            Character, run.RunId, ClearRewardCardKind.Free, 0));
    }

    [Fact]
    public async Task Prepared_card_keeps_its_exact_reservation_when_actor_leaves_during_durable_commit()
    {
        var scope = new DomainTransactionScope();
        var service = new DungeonRunDomainService(scope);
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var run = PreparePartyCardPanel(service, leader, member);
        DungeonClearRewardCardClaim? stolen = null;

        DungeonCardSelectionCommit receipt;
        await using (var transaction = scope.Begin())
        {
            var claim = Assert.IsType<DungeonClearRewardCardClaim>(
                service.TryReserveClearRewardCard(
                    leader.CharacterId, run.RunId, ClearRewardCardKind.Free, 0));
            receipt = service.CommitClearRewardCard(claim);
            _ = scope.Current!.Enlist(
                "durable store",
                () => new CallbackCommit(() =>
                {
                    Assert.True(service.End(leader.CharacterId, run.RunId));
                    stolen = service.TryReserveClearRewardCard(
                        member.CharacterId, run.RunId, ClearRewardCardKind.Free, 0);
                }));

            await transaction.CommitAsync(CancellationToken.None);
        }

        Assert.NotNull(stolen);
        var committed = Assert.IsType<DungeonCardSelectionChange>(receipt.Change);
        Assert.Equal([member.CharacterId], committed.Recipients);
        Assert.Empty(committed.Selections);

        var memberChange = Assert.IsType<DungeonCardSelectionChange>(
            service.CommitClearRewardCard(stolen!).Change);
        Assert.Equal(member.CharacterId, Assert.Single(memberChange.Selections).CharacterId);
    }

    [Fact]
    public void Party_members_can_both_confirm_free_card_zero_before_the_ordered_result_is_revealed()
    {
        var service = new DungeonRunDomainService();
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var run = PreparePartyCardPanel(service, leader, member);

        var first = Assert.IsType<DungeonClearRewardCardClaim>(
            service.TryReserveClearRewardCard(
                leader.CharacterId, run.RunId, ClearRewardCardKind.Free, 0));
        var firstChange = Assert.IsType<DungeonCardSelectionChange>(
            service.CommitClearRewardCard(first).Change);
        Assert.Empty(firstChange.Selections);
        Assert.Empty(service.Find(member.CharacterId)!.CardSelections);

        var second = Assert.IsType<DungeonClearRewardCardClaim>(
            service.TryReserveClearRewardCard(
                member.CharacterId, run.RunId, ClearRewardCardKind.Free, 0));
        var finalChange = Assert.IsType<DungeonCardSelectionChange>(
            service.CommitClearRewardCard(second).Change);
        Assert.Collection(finalChange.Selections,
            selection => Assert.Equal((leader.CharacterId, 0, (byte)0),
                (selection.CharacterId, selection.Index, selection.Seat)),
            selection => Assert.Equal((member.CharacterId, 1, (byte)1),
                (selection.CharacterId, selection.Index, selection.Seat)));
        Assert.Equal(finalChange.Selections, service.Find(leader.CharacterId)!.CardSelections);
        Assert.Null(service.TryReserveClearRewardCard(
            leader.CharacterId, run.RunId, ClearRewardCardKind.Free, 0));
    }

    [Fact]
    public void Shared_card_columns_are_unique_and_changes_carry_the_complete_party_snapshot()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        var run = BeginPartyWithCell(
            service,
            [leader, member],
            actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0))]);
        var free = new ClearRewardCardFace(
            ClearRewardCardKind.Free,
            [new ClearRewardCardSlot(0, null), new ClearRewardCardSlot(1, null)],
            Gold: 1);
        var paid = new ClearRewardCardFace(
            ClearRewardCardKind.Paid,
            [new ClearRewardCardSlot(0, null), new ClearRewardCardSlot(1, null)],
            Gold: 2);

        Assert.NotNull(service.TryOpenClear(leader.CharacterId, run.RunId, run.RoomVersion));
        foreach (var participant in new[] { leader, member })
        {
            Assert.NotNull(service.FixClearRewardCards(
                participant.CharacterId, run.RunId, free, paid, paidFlipCost: 3));
            var settlement = Assert.IsType<DungeonSettlementClaim>(
                service.TryClaimSettlement(participant.CharacterId, run.RunId));
            Assert.NotNull(service.CommitSettlement(settlement));
            Assert.NotNull(service.TryOpenCardLayout(participant.CharacterId, run.RunId));
        }

        var leaderClaim = Assert.IsType<DungeonClearRewardCardClaim>(
            service.TryReserveClearRewardCard(
                leader.CharacterId, run.RunId, ClearRewardCardKind.Free, 0));
        var leaderChange = Assert.IsType<DungeonCardSelectionChange>(
            service.CommitClearRewardCard(leaderClaim).Change);
        Assert.Empty(leaderChange.Selections);
        Assert.Equal([leader.CharacterId, member.CharacterId], leaderChange.Recipients);
        Assert.Null(service.TryReserveClearRewardCard(
            leader.CharacterId, run.RunId, ClearRewardCardKind.Free, 1));

        var memberClaim = Assert.IsType<DungeonClearRewardCardClaim>(
            service.TryReserveClearRewardCard(
                member.CharacterId, run.RunId, ClearRewardCardKind.Free, 0));
        var memberChange = Assert.IsType<DungeonCardSelectionChange>(
            service.CommitClearRewardCard(memberClaim).Change);
        Assert.Collection(
            memberChange.Selections,
            selection => Assert.Equal((0, (byte)0), (selection.Index, selection.Seat)),
            selection => Assert.Equal((1, (byte)1), (selection.Index, selection.Seat)));
        Assert.Equal(memberChange.Selections, service.Find(leader.CharacterId)!.CardSelections);

        Assert.True(service.End(leader.CharacterId, run.RunId));
        var remainingClaim = Assert.IsType<DungeonClearRewardCardClaim>(
            service.TryReserveClearRewardCard(
                member.CharacterId, run.RunId, ClearRewardCardKind.Paid, 0));
        var afterDeparture = Assert.IsType<DungeonCardSelectionChange>(
            service.CommitClearRewardCard(remainingClaim).Change);
        Assert.Equal([member.CharacterId], afterDeparture.Recipients);
        Assert.Null(service.Find(leader.CharacterId));
        Assert.Equal(run.RunId, service.Find(member.CharacterId)!.RunId);
    }

    private static DungeonRunState PrepareCardPanel(DungeonRunDomainService service)
    {
        var run = service.Begin(Character, Tutorial, difficulty: 0);
        var free = new ClearRewardCardFace(
            ClearRewardCardKind.Free, [new ClearRewardCardSlot(0, null)], Gold: 1);
        var paid = new ClearRewardCardFace(
            ClearRewardCardKind.Paid, [new ClearRewardCardSlot(0, null)], Gold: 2);
        Assert.NotNull(service.FixClearRewardCards(Character, run.RunId, free, paid, 3));
        Assert.NotNull(service.TryOpenClear(Character, run.RunId, run.RoomVersion));
        var settlement = Assert.IsType<DungeonSettlementClaim>(
            service.TryClaimSettlement(Character, run.RunId));
        Assert.NotNull(service.CommitSettlement(settlement));
        Assert.NotNull(service.TryOpenCardLayout(Character, run.RunId));
        return run;
    }

    private static DungeonRunState PreparePartyCardPanel(
        DungeonRunDomainService service,
        params DungeonRunParticipant[] participants)
    {
        var run = service.BeginParty(participants, Tutorial, difficulty: 0);
        var free = new ClearRewardCardFace(
            ClearRewardCardKind.Free,
            [new ClearRewardCardSlot(0, null), new ClearRewardCardSlot(1, null)],
            Gold: 1);
        var paid = new ClearRewardCardFace(
            ClearRewardCardKind.Paid,
            [new ClearRewardCardSlot(0, null), new ClearRewardCardSlot(1, null)],
            Gold: 2);
        Assert.NotNull(service.TryOpenClear(
            participants[0].CharacterId, run.RunId, run.RoomVersion));
        foreach (var participant in participants)
        {
            Assert.NotNull(service.FixClearRewardCards(
                participant.CharacterId, run.RunId, free, paid, 3));
            var settlement = Assert.IsType<DungeonSettlementClaim>(
                service.TryClaimSettlement(participant.CharacterId, run.RunId));
            Assert.NotNull(service.CommitSettlement(settlement));
            Assert.NotNull(service.TryOpenCardLayout(participant.CharacterId, run.RunId));
        }
        return run;
    }

    [Fact]
    public void Settlement_is_refused_until_the_shared_clear_is_open()
    {
        var service = new DungeonRunDomainService();
        var run = service.Begin(Character, Tutorial, difficulty: 0);

        Assert.Null(service.TryClaimSettlement(Character, run.RunId));
        Assert.NotNull(service.TryOpenClear(Character, run.RunId, run.RoomVersion));
        Assert.NotNull(service.TryClaimSettlement(Character, run.RunId));
    }

    [Fact]
    public void Departed_party_member_cannot_claim_an_old_run_kill_effect()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        var run = BeginPartyWithCell(
            service,
            [leader, member],
            actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0))]);
        var death = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(leader.CharacterId, FirstSequence));

        Assert.True(service.End(member.CharacterId, run.RunId));
        Assert.Null(service.TryClaimParticipantKillEffect(death, member.CharacterId));
    }

    /// <summary>Every cell of the maze is one of the three kinds, and only one.</summary>
    [Fact]
    public void The_layout_says_what_each_of_its_rooms_is_for()
    {
        Assert.Equal(DungeonRoomKind.Start, Tutorial.KindOf(Tutorial.StartCell));
        Assert.Equal(DungeonRoomKind.Boss, Tutorial.KindOf(Tutorial.BossCell));
        Assert.Equal(DungeonRoomKind.Ordinary, Tutorial.KindOf(new MazeCell(1, 1)));
    }

    /// <summary>
    /// The boss room may not be left, and the rooms before it may.
    /// </summary>
    /// <remarks>
    /// A live session on 2026-08-26 walked back out of the boss room after clearing it and through
    /// rooms it had already emptied, which the game does not allow. The rule is asked of the room
    /// rather than of the run's phase: the boss room's doors shut behind you whether or not the
    /// boss is dead, and a dungeon that later grows a room with its own answer brings that answer
    /// with it instead of adding a case.
    /// </remarks>
    [Fact]
    public void The_boss_room_may_not_be_left_and_the_rooms_before_it_may()
    {
        var service = new DungeonRunDomainService();

        var start = service.Begin(Character, Tutorial, difficulty: 0);
        Assert.False(start.IsBossRoom);
        Assert.True(start.MayLeaveCurrentRoom);

        var ordinary = MoveToEmptyCell(service, start, new MazeCell(1, 1));
        Assert.NotNull(ordinary);
        Assert.True(ordinary.MayLeaveCurrentRoom);

        var boss = service.TryMove(
            Character,
            ordinary.RunId,
            ordinary.RoomVersion,
            Tutorial.BossCell,
            CellSeed(Tutorial.BossCell, Tutorial.Rooms[Tutorial.BossCell]));
        Assert.NotNull(boss);
        Assert.True(boss.IsBossRoom);
        Assert.False(boss.MayLeaveCurrentRoom);
    }

    /// <summary>A run ends, and the character is no longer in one.</summary>
    /// <remarks>
    /// Nothing called this until 2026-08-26. A character who had walked back to town was still, as
    /// far as the server knew, standing in a dungeon.
    /// </remarks>
    [Fact]
    public void Ending_a_run_leaves_the_character_in_none()
    {
        var service = new DungeonRunDomainService();
        service.Begin(Character, Tutorial, difficulty: 0);

        Assert.True(service.End(Character));
        Assert.Null(service.Find(Character));
        Assert.False(service.End(Character));
    }

    /// <summary>What died in one room has not died in the next.</summary>
    [Fact]
    public void A_kill_in_one_room_does_not_clear_another()
    {
        var service = new DungeonRunDomainService();
        var started = BeginWithCell(
            service,
            actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0))]);
        var death = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        _ = service.CommitActorDeath(death);

        var target = new MazeCell(1, 1);
        var moved = service.TryMove(
            Character,
            started.RunId,
            started.RoomVersion,
            target,
            CellSeed(
                target,
                Tutorial.Rooms[target],
                actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0))]));

        Assert.NotNull(moved);
        Assert.False(moved.IsCleared);
    }

    [Fact]
    public void Committed_monster_deaths_form_one_idempotent_run_ledger_across_cells()
    {
        var service = new DungeonRunDomainService();
        var firstActor = new DungeonRoomActor(
            61670, 4, (byte)MonsterRank.Ordinary);
        var run = BeginWithCell(
            service,
            actors: [CellActor(FirstSequence, firstActor)]);

        var firstClaim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        run = Assert.IsType<DungeonRunState>(service.CommitActorDeath(firstClaim));
        Assert.Null(service.TryClaimActorDeath(Character, FirstSequence));

        var target = new MazeCell(1, 1);
        var secondActor = new DungeonRoomActor(
            firstActor.Code, 5, (byte)MonsterRank.Champion);
        run = Assert.IsType<DungeonRunState>(service.TryMove(
            Character,
            run.RunId,
            run.RoomVersion,
            target,
            CellSeed(
                target,
                Tutorial.Rooms[target],
                actors: [CellActor(FirstSequence, secondActor)])));
        var secondClaim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        run = Assert.IsType<DungeonRunState>(service.CommitActorDeath(secondClaim));

        Assert.Collection(
            run.Kills,
            first =>
            {
                Assert.Equal(new RoomActorId(Tutorial.StartCell, FirstSequence), first.Actor);
                Assert.Equal(firstActor.Code, first.MonsterCode);
                Assert.Equal(Tutorial.LevelOf(firstActor.Level), first.MonsterLevel);
                Assert.Equal(MonsterRank.Ordinary, first.Rank);
            },
            second =>
            {
                Assert.Equal(new RoomActorId(target, FirstSequence), second.Actor);
                Assert.Equal(secondActor.Code, second.MonsterCode);
                Assert.Equal(Tutorial.LevelOf(secondActor.Level), second.MonsterLevel);
                Assert.Equal(MonsterRank.Champion, second.Rank);
            });
        Assert.Equal(2, run.KillSummary.Total);
        Assert.Equal(1, run.KillSummary.Ordinary);
        Assert.Equal(1, run.KillSummary.Champion);
        Assert.Equal(0, run.KillSummary.SuperChampion);
        Assert.Equal(0, run.KillSummary.Boss);
        Assert.Equal(2, run.KillSummary.ByMonsterCode[firstActor.Code]);
    }

    [Fact]
    public void An_ai_character_death_is_not_a_monster_kill()
    {
        var service = new DungeonRunDomainService();
        _ = BeginWithCell(
            service,
            actors:
            [
                CellActor(
                    FirstSequence,
                    new DungeonRoomActor(32001, 5, (byte)MonsterRank.Ordinary),
                    isAi: true),
            ]);

        var claim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        var run = Assert.IsType<DungeonRunState>(service.CommitActorDeath(claim));

        Assert.Empty(run.Kills);
        Assert.Equal(0, run.KillSummary.Total);
    }

    /// <summary>A run is timed from the moment it begins.</summary>
    /// <remarks>
    /// The result screen shows how long the player took, and nothing the client sends carries it --
    /// the reference server measures it too.
    /// </remarks>
    [Fact]
    public void A_run_records_when_it_began()
    {
        var before = DateTimeOffset.UtcNow;

        var run = new DungeonRunDomainService().Begin(Character, Tutorial, difficulty: 0);

        Assert.InRange(run.StartedAt, before, DateTimeOffset.UtcNow);
    }

    /// <summary>An arbitrary sequence base; these tests are about what is counted, not its number.</summary>
    private const ushort FirstSequence = 4728;

    [Fact]
    public void A_character_who_is_not_in_a_run_takes_nothing()
    {
        var service = new DungeonRunDomainService();

        Assert.Null(service.TryClaimGroundDrop(
            Character, DungeonRunId.New(), expectedRoomVersion: 0, groundDropNumber: 1));
    }

    /// <summary>A run may be settled once, and a repeat is refused.</summary>
    /// <remarks>
    /// The same shape as a repeated death report, and needed for the same reason: the client says
    /// when a run is over, and saying so pays experience. Nothing else refuses a second report --
    /// the run stays in progress until the character walks out.
    /// </remarks>
    [Fact]
    public void ARunIsSettledOnceAndARepeatIsRefused()
    {
        var runs = new DungeonRunDomainService();
        var run = runs.Begin(Character, Tutorial, difficulty: 0);
        Assert.NotNull(runs.TryOpenClear(Character, run.RunId, run.RoomVersion));

        var settled = runs.TrySettle(Character);

        Assert.NotNull(settled);
        Assert.True(settled.IsSettled);
        Assert.Null(runs.TrySettle(Character));
    }

    /// <summary>Settling does not end the run.</summary>
    /// <remarks>
    /// Between the result screen and the door the character is still in the dungeon, and the frames
    /// that follow read the run for its layout. Ending it here would answer the walk out with "no
    /// run in progress".
    /// </remarks>
    [Fact]
    public void SettlingDoesNotEndTheRun()
    {
        var runs = new DungeonRunDomainService();
        var run = runs.Begin(Character, Tutorial, difficulty: 0);
        Assert.NotNull(runs.TryOpenClear(Character, run.RunId, run.RoomVersion));
        runs.TrySettle(Character);

        Assert.NotNull(runs.Find(Character));
        Assert.True(runs.End(Character));
    }

    /// <summary>A character in no run has nothing to settle.</summary>
    [Fact]
    public void ACharacterInNoRunHasNothingToSettle() =>
        Assert.Null(new DungeonRunDomainService().TrySettle(Character));

    [Fact]
    public void Shared_clear_opens_once_with_one_revision_and_current_recipients()
    {
        var helper = new CharacterId(61);
        var runs = new DungeonRunDomainService();
        var started = runs.BeginParty(
            [
                new DungeonRunParticipant(new AccountId(6), Character, 0, IsLeader: true),
                new DungeonRunParticipant(new AccountId(7), helper, 1, IsLeader: false),
            ],
            Tutorial,
            difficulty: 0);

        var opened = runs.TryOpenClear(Character, started.RunId, started.RoomVersion);

        Assert.NotNull(opened);
        Assert.Equal(1, opened.Revision);
        Assert.Equal([Character, helper], opened.Recipients);
        Assert.True(runs.Find(Character)!.ClearOpen);
        Assert.True(runs.Find(helper)!.ClearOpen);
        Assert.Null(runs.TryOpenClear(helper, started.RunId, started.RoomVersion));
    }

    [Fact]
    public void Ground_numbers_are_shared_across_rooms_sources_and_party_views()
    {
        var service = new DungeonRunDomainService();
        var helper = new CharacterId(2);
        var run = BeginPartyWithCell(service,
            [new(new AccountId(1), Character, 0, true), new(new AccountId(2), helper, 1, false)],
            groundItems: [DeclaredItem(1)]);
        _ = Take(service, run, 1);
        var target = new MazeCell(1, 1);
        var moved = Assert.IsType<DungeonRunState>(service.TryMove(
            Character, run.RunId, run.RoomVersion, target,
            CellSeed(target, Tutorial.Rooms[target],
                actors: [CellActor(FirstSequence, new DungeonRoomActor(61670, 1, 0),
                    pendingLoot: [new RolledDrop(99, 1, DropRarity.Common, DropItemKind.Stackable)])],
                groundItems: [DeclaredItem(1)])));
        Assert.Equal((ushort)2, Assert.Single(moved.CurrentCellState.GroundItems).Number);
        Assert.Null(service.TryClaimGroundDrop(Character, run.RunId, moved.RoomVersion, 1));
        var claim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        var death = Assert.IsType<DungeonActorDeathChange>(service.CommitActorDeathChange(claim));
        Assert.Equal((ushort)3, Assert.Single(death.AddedGroundItems).Number);
        var addition = Assert.IsType<DungeonGroundItemsAddedChange>(service.TryAddGroundItems(
            helper, run.RunId, moved.RoomVersion, death.CellRevision,
            [new DungeonGroundItemDraft(3020, 1, 0, false, DungeonGroundItemSource.FromPlayer())]));
        Assert.Equal((ushort)4, Assert.Single(addition.AddedGroundItems).Number);
        Assert.Equal(new ushort[] { 2, 3, 4 }, service.Find(helper)!.CurrentCellState.GroundItems.Select(x => x.Number));
        var back = Assert.IsType<DungeonRunState>(service.TryMove(Character, Tutorial.StartCell));
        Assert.Empty(back.CurrentCellState.GroundItems);
        var returned = Assert.IsType<DungeonRunState>(service.TryMove(Character, target));
        Assert.Equal(new ushort[] { 2, 3, 4 }, returned.CurrentCellState.GroundItems.Select(x => x.Number));
        var fresh = BeginPartyWithCell(service,
            [new(new AccountId(3), new CharacterId(3), 0, true)],
            groundItems: [DeclaredItem(1)]);
        Assert.Equal((ushort)1, Assert.Single(fresh.CurrentCellState.GroundItems).Number);
    }

    [Fact]
    public void Exhausted_run_numbers_do_not_wrap_or_partially_publish_a_new_room()
    {
        var service = new DungeonRunDomainService();
        var items = Enumerable.Range(0, ushort.MaxValue)
            .Select(_ => DeclaredItem(1)).ToArray();
        var run = BeginWithCell(service, groundItems: items);
        Assert.Equal(ushort.MaxValue, run.CurrentCellState.GroundItems[^1].Number);
        var target = new MazeCell(1, 1);
        Assert.Throws<InvalidOperationException>(() => service.TryMove(
            Character, run.RunId, run.RoomVersion, target,
            CellSeed(target, Tutorial.Rooms[target], groundItems: [DeclaredItem(1)])));
        Assert.Equal(run.CurrentCell, service.Find(Character)!.CurrentCell);
        _ = Take(service, run, 1);
        Assert.Throws<InvalidOperationException>(() => service.TryMove(
            Character, run.RunId, run.RoomVersion, target,
            CellSeed(target, Tutorial.Rooms[target], groundItems: [DeclaredItem(1)])));
        Assert.NotNull(MoveToEmptyCell(service, service.Find(Character)!, target));
    }

    private static DungeonRunState BeginWithCell(
        DungeonRunDomainService service,
        IReadOnlyList<DungeonCellActorState>? actors = null,
        IReadOnlyList<DungeonGroundItem>? groundItems = null)
    {
        var participant = new DungeonRunParticipant(
            new AccountId(1), Character, Slot: 0, IsLeader: true);
        return service.PreparePartyEntry(
            [participant],
            Tutorial,
            difficulty: 0,
            CellSeed(
                Tutorial.StartCell,
                Tutorial.StartRoomMapId,
                actors,
                groundItems),
            DungeonRunId.New(),
            DateTimeOffset.UtcNow).Run;
    }

    private static DungeonRunState BeginPartyWithCell(
        DungeonRunDomainService service,
        IReadOnlyList<DungeonRunParticipant> participants,
        IReadOnlyList<DungeonCellActorState>? actors = null,
        IReadOnlyList<DungeonGroundItem>? groundItems = null,
        DungeonDifficulty difficulty = 0) =>
        service.PreparePartyEntry(
            participants,
            Tutorial,
            difficulty,
            CellSeed(
                Tutorial.StartCell,
                Tutorial.StartRoomMapId,
                actors,
                groundItems),
            DungeonRunId.New(),
            DateTimeOffset.UtcNow).Run;

    private static DungeonRunState? MoveToEmptyCell(
        DungeonRunDomainService service,
        DungeonRunState run,
        MazeCell target) =>
        service.TryMove(
            run.Participant!.CharacterId,
            run.RunId,
            run.RoomVersion,
            target,
            CellSeed(target, run.Layout.Rooms[target]));

    private static DungeonCellSeed CellSeed(
        MazeCell cell,
        int mapId,
        IReadOnlyList<DungeonCellActorState>? actors = null,
        IReadOnlyList<DungeonGroundItem>? groundItems = null) =>
        new(
            cell,
            mapId,
            FirstSequence,
            actors ?? [],
            groundItems ?? []);

    private static DungeonCellActorState CellActor(
        ushort sequence,
        DungeonRoomActor actor,
        bool isAi = false,
        IReadOnlyList<RolledDrop>? pendingLoot = null) =>
        new(
            sequence,
            actor,
            isAi,
            IsDead: false,
            pendingLoot ?? [],
            IsMonster: !isAi && MonsterExperienceRules.IsMonster(actor.ActorType));

    private static DungeonGroundItem DeclaredItem(ushort number) =>
        new(
            number,
            ItemId: 14400,
            Count: 1,
            Durability: 25,
            IsEquipment: true,
            DungeonGroundItemSource.Declared(checked((byte)(number - 1))),
            Kind: DropItemKind.Equipment);

    private static DungeonRunState? Take(
        DungeonRunDomainService service,
        DungeonRunState run,
        ushort number)
    {
        var claim = service.TryClaimGroundDrop(
            Character, run.RunId, run.RoomVersion, number);
        return claim is null
            || service.CommitGroundDrop(claim, DungeonPickupKind.Item) is null
                ? null
                : service.Find(Character);
    }

    private sealed class FailingCommit : ITransactionParticipant
    {
        public bool CommitCanFail => true;

        public ValueTask PrepareAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask CommitAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException(new InvalidOperationException("injected durable failure"));

        public ValueTask RollbackAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class CallbackCommit(Action callback) : ITransactionParticipant
    {
        public bool CommitCanFail => true;

        public ValueTask PrepareAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            callback();
            return ValueTask.CompletedTask;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
