using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

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

    [Fact]
    public void Party_entry_changes_share_one_instance_and_readiness_is_monotonic_and_idempotent()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var service = new DungeonRunDomainService();

        var prepared = service.PreparePartyEntry([leader, member], Tutorial, difficulty: 2);

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
        var prepared = service.PreparePartyEntry([leader, member], Tutorial, difficulty: 2);

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
        var prepared = service.PreparePartyEntry([leader, member], Tutorial, difficulty: 2);

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
        var oldEntry = service.PreparePartyEntry([leader, member], Tutorial, difficulty: 2);

        var replacement = service.PreparePartyEntry(
            [member with { Slot = 0, IsLeader = true }],
            Tutorial,
            difficulty: 2);

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
        var prepared = service.PreparePartyEntry([leader, member], Tutorial, difficulty: 2);

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
        var prepared = service.PreparePartyEntry([leader, member], Tutorial, difficulty: 2);
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
    public void Two_members_share_world_facts_but_keep_rewards_and_lifetime_per_participant()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        DungeonRunParticipant[] party = [leader, member];
        var service = new DungeonRunDomainService();

        var started = service.BeginParty(party, Tutorial, difficulty: 2);
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
            leader.CharacterId, leaderRun.RunId, leaderRun.RoomVersion, new MazeCell(1, 1));
        Assert.NotNull(moved);
        Assert.Equal(moved.CurrentCell, service.Find(member.CharacterId)!.CurrentCell);
        Assert.Equal(moved.RoomVersion, service.Find(member.CharacterId)!.RoomVersion);
        Assert.Null(service.TryMove(
            leader.CharacterId, leaderRun.RunId, leaderRun.RoomVersion, Tutorial.BossCell));

        Assert.NotNull(service.TryReportActorDead(member.CharacterId, FirstSequence));
        Assert.Null(service.TryReportActorDead(leader.CharacterId, FirstSequence));
        Assert.Contains(
            new RoomActorId(moved.CurrentCell, FirstSequence),
            service.Find(leader.CharacterId)!.DeadActors);

        var rolled = new RolledDrop(
            ItemId: 10087807,
            Count: 1,
            Rarity: DropRarity.Common,
            Kind: DropItemKind.Stackable);
        var allocated = service.AllocateMonsterDrops(
            member.CharacterId, declaredDropCount: 0, FirstSequence, [rolled]);
        var drop = Assert.Single(allocated);
        Assert.Equal(drop, Assert.Single(service.Find(leader.CharacterId)!.MonsterDrops).Drop);
        Assert.NotNull(service.TryTakeGroundDrop(leader.CharacterId, drop.Number));
        Assert.Null(service.TryTakeGroundDrop(member.CharacterId, drop.Number));

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

    private static readonly DungeonRoomDrop[] OneBreakableObject = [new DungeonRoomDrop(0, 14400, 25)];

    [Fact]
    public void A_run_opens_with_nothing_taken_off_its_floor()
    {
        var service = new DungeonRunDomainService();

        var run = service.Begin(Character, Tutorial, difficulty: 0);

        Assert.Empty(run.TakenGroundDrops);
        Assert.Single(run.RemainingGroundDrops(OneBreakableObject));
    }

    [Fact]
    public void A_piece_can_be_taken_once()
    {
        var service = new DungeonRunDomainService();
        service.Begin(Character, Tutorial, difficulty: 0);

        var afterTaking = service.TryTakeGroundDrop(Character, 1);

        Assert.NotNull(afterTaking);
        Assert.Empty(afterTaking.RemainingGroundDrops(OneBreakableObject));
        Assert.Null(service.TryTakeGroundDrop(Character, 1));
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
        service.Begin(Character, Tutorial, difficulty: 0);
        service.TryTakeGroundDrop(Character, 1);

        service.TryMove(Character, new MazeCell(1, 1));
        var returned = service.TryMove(Character, new MazeCell(0, 1));

        Assert.NotNull(returned);
        Assert.Empty(returned.RemainingGroundDrops(OneBreakableObject));
        Assert.Null(service.TryTakeGroundDrop(Character, 1));
    }

    /// <summary>Emptying one room's floor leaves the next room's alone.</summary>
    [Fact]
    public void The_next_room_still_has_what_it_declares()
    {
        var service = new DungeonRunDomainService();
        service.Begin(Character, Tutorial, difficulty: 0);
        service.TryTakeGroundDrop(Character, 1);

        var moved = service.TryMove(Character, new MazeCell(1, 1));

        Assert.NotNull(moved);
        Assert.Single(moved.RemainingGroundDrops(OneBreakableObject));
        Assert.NotNull(service.TryTakeGroundDrop(Character, 1));
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
        DungeonRoomActor[] declared =
        [
            new DungeonRoomActor(61670, 1, 0),
            new DungeonRoomActor(461, 80, 5),
        ];
        var service = new DungeonRunDomainService();
        var run = service.Begin(Character, Tutorial, difficulty: 0);

        Assert.False(run.IsCleared(declared, FirstSequence));

        var afterKill = service.TryReportActorDead(Character, FirstSequence);

        Assert.NotNull(afterKill);
        Assert.True(afterKill.IsCleared(declared, FirstSequence));
        Assert.Null(service.TryReportActorDead(Character, FirstSequence));
    }

    [Fact]
    public void AFailedDurableKillCanReleaseItsDeathClaimForOneRetry()
    {
        var service = new DungeonRunDomainService();
        service.Begin(Character, Tutorial, difficulty: 0);
        var claimed = service.TryClaimActorDeath(Character, FirstSequence);
        Assert.NotNull(claimed);

        Assert.True(service.ReleaseActorDeath(claimed!));
        var retry = service.TryClaimActorDeath(Character, FirstSequence);
        Assert.NotNull(retry);
        Assert.False(service.ReleaseActorDeath(claimed));
        Assert.NotNull(service.CommitActorDeath(retry!));
        Assert.Null(service.TryClaimActorDeath(Character, FirstSequence));
    }

    [Fact]
    public void A_retried_party_death_claims_only_the_participant_whose_effect_is_unfinished()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        service.BeginParty([leader, member], Tutorial, difficulty: 0);

        var firstDeath = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(leader.CharacterId, FirstSequence));
        var leaderEffect = Assert.IsType<DungeonParticipantKillEffectClaim>(
            service.TryClaimParticipantKillEffect(firstDeath, leader.CharacterId));
        Assert.True(service.CommitParticipantKillEffect(leaderEffect));
        var failedMemberEffect = Assert.IsType<DungeonParticipantKillEffectClaim>(
            service.TryClaimParticipantKillEffect(firstDeath, member.CharacterId));
        Assert.True(service.ReleaseParticipantKillEffect(failedMemberEffect));
        Assert.True(service.ReleaseActorDeath(firstDeath));

        var retry = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(member.CharacterId, FirstSequence));
        Assert.Null(service.TryClaimParticipantKillEffect(retry, leader.CharacterId));
        var retriedMember = Assert.IsType<DungeonParticipantKillEffectClaim>(
            service.TryClaimParticipantKillEffect(retry, member.CharacterId));
        Assert.True(service.CommitParticipantKillEffect(retriedMember));
        Assert.NotNull(service.CommitActorDeath(retry));
        Assert.Null(service.TryClaimActorDeath(leader.CharacterId, FirstSequence));
    }

    [Fact]
    public void Monster_drop_allocation_is_fixed_once_even_when_the_actor_attempt_is_retried()
    {
        var service = new DungeonRunDomainService();
        service.Begin(Character, Tutorial, difficulty: 0);
        var first = new RolledDrop(1001, 1, DropRarity.Common, DropItemKind.Stackable);
        var replacement = new RolledDrop(2002, 1, DropRarity.Rare, DropItemKind.Stackable);

        var allocated = service.AllocateMonsterDrops(Character, 0, FirstSequence, [first]);
        var replayed = service.AllocateMonsterDrops(Character, 0, FirstSequence, [replacement]);

        Assert.Equal(allocated, replayed);
        Assert.Equal(1001, Assert.Single(replayed).Rolled.ItemId);
        Assert.Single(service.Find(Character)!.MonsterDrops);
    }

    [Fact]
    public void A_failed_settlement_claim_can_retry_but_a_committed_one_cannot()
    {
        var service = new DungeonRunDomainService();
        service.Begin(Character, Tutorial, difficulty: 0);

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
            Character, run.RunId, run.RoomVersion, new MazeCell(1, 1)));
    }

    [Fact]
    public void Exact_end_does_not_remove_a_replacement_run()
    {
        var service = new DungeonRunDomainService();
        var old = service.Begin(Character, Tutorial, difficulty: 0);
        var replacement = service.Begin(Character, Tutorial, difficulty: 1);

        Assert.False(service.End(Character, old.RunId));
        Assert.Equal(replacement.RunId, service.Find(Character)!.RunId);
        Assert.True(service.End(Character, replacement.RunId));
    }

    [Fact]
    public void Pending_death_is_not_clear_and_blocks_room_movement_until_commit_or_release()
    {
        var service = new DungeonRunDomainService();
        var run = service.Begin(Character, Tutorial, difficulty: 0);
        var claim = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));

        Assert.DoesNotContain(claim.Actor, service.Find(Character)!.DeadActors);
        Assert.Null(service.TryMove(
            Character, run.RunId, run.RoomVersion, new MazeCell(1, 1)));

        Assert.True(service.ReleaseActorDeath(claim));
        Assert.NotNull(service.TryMove(
            Character, run.RunId, run.RoomVersion, new MazeCell(1, 1)));
    }

    [Fact]
    public void Room_clear_linearizes_on_the_last_committed_death_not_on_pending_claims()
    {
        DungeonRoomActor[] declared =
        [
            new DungeonRoomActor(61670, 1, 0),
            new DungeonRoomActor(61671, 1, 0),
        ];
        var service = new DungeonRunDomainService();
        service.Begin(Character, Tutorial, difficulty: 0);
        var first = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, FirstSequence));
        var second = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, (ushort)(FirstSequence + 1)));

        var firstCommitted = Assert.IsType<DungeonRunState>(service.CommitActorDeath(first));
        Assert.False(firstCommitted.IsCleared(declared, FirstSequence));
        Assert.True(service.ReleaseActorDeath(second));
        Assert.False(service.Find(Character)!.IsCleared(declared, FirstSequence));

        var retry = Assert.IsType<DungeonActorDeathClaim>(
            service.TryClaimActorDeath(Character, (ushort)(FirstSequence + 1)));
        var lastCommitted = Assert.IsType<DungeonRunState>(service.CommitActorDeath(retry));
        Assert.True(lastCommitted.IsCleared(declared, FirstSequence));
    }

    [Fact]
    public void Exact_drop_allocation_stays_on_the_claimed_room_and_fixes_an_empty_roll()
    {
        var service = new DungeonRunDomainService();
        var run = service.Begin(Character, Tutorial, difficulty: 0);
        var actor = new RoomActorId(run.CurrentCell, FirstSequence);

        var empty = service.TryAllocateMonsterDrops(
            Character, run.RunId, run.RoomVersion, actor, 0, []);
        var rerolled = service.TryAllocateMonsterDrops(
            Character,
            run.RunId,
            run.RoomVersion,
            actor,
            0,
            [new RolledDrop(99, 1, DropRarity.Rare, DropItemKind.Stackable)]);

        Assert.NotNull(empty);
        Assert.Empty(empty);
        Assert.NotNull(rerolled);
        Assert.Empty(rerolled);
        var replacement = service.Begin(Character, Tutorial, difficulty: 1);
        Assert.Null(service.TryAllocateMonsterDrops(
            Character, run.RunId, run.RoomVersion, actor, 0, []));
        Assert.NotEqual(run.RunId, replacement.RunId);
    }

    [Fact]
    public void Exact_settlement_cannot_claim_a_replacement_run()
    {
        var service = new DungeonRunDomainService();
        var old = service.Begin(Character, Tutorial, difficulty: 0);
        var replacement = service.Begin(Character, Tutorial, difficulty: 1);

        Assert.Null(service.TryClaimSettlement(Character, old.RunId));
        Assert.NotNull(service.TryClaimSettlement(Character, replacement.RunId));
    }

    [Fact]
    public void Ground_drop_reservation_releases_for_retry_and_commits_once()
    {
        var service = new DungeonRunDomainService();
        var run = service.Begin(Character, Tutorial, difficulty: 0);
        var first = Assert.IsType<DungeonGroundDropClaim>(
            service.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, 1));

        Assert.Null(service.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, 1));
        Assert.True(service.ReleaseGroundDrop(first));
        var retry = Assert.IsType<DungeonGroundDropClaim>(
            service.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, 1));
        Assert.NotNull(service.CommitGroundDrop(retry));
        Assert.Null(service.TryClaimGroundDrop(Character, run.RunId, run.RoomVersion, 1));
    }

    [Fact]
    public void A_durable_ground_drop_claim_can_commit_after_the_picker_leaves_a_shared_run()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), Slot: 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), Slot: 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        var run = service.BeginParty([leader, member], Tutorial, difficulty: 0);
        var claim = Assert.IsType<DungeonGroundDropClaim>(
            service.TryClaimGroundDrop(
                leader.CharacterId, run.RunId, run.RoomVersion, groundDropNumber: 1));

        Assert.True(service.End(leader.CharacterId, run.RunId));
        Assert.NotNull(service.CommitGroundDrop(claim));
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

        var first = Assert.IsType<DungeonClearRewardCardClaim>(
            service.TryReserveClearRewardCard(
                Character, run.RunId, ClearRewardCardKind.Free, 0));
        Assert.Null(service.TryReserveClearRewardCard(
            Character, run.RunId, ClearRewardCardKind.Free, 0));
        Assert.True(service.ReleaseClearRewardCard(first));
        var retry = Assert.IsType<DungeonClearRewardCardClaim>(
            service.TryReserveClearRewardCard(
                Character, run.RunId, ClearRewardCardKind.Free, 0));
        Assert.True(service.CommitClearRewardCard(retry));
        Assert.Null(service.TryReserveClearRewardCard(
            Character, run.RunId, ClearRewardCardKind.Free, 0));
    }

    [Fact]
    public void Departed_party_member_cannot_claim_an_old_run_kill_effect()
    {
        var leader = new DungeonRunParticipant(
            new AccountId(101), new CharacterId(1), 0, IsLeader: true);
        var member = new DungeonRunParticipant(
            new AccountId(202), new CharacterId(2), 1, IsLeader: false);
        var service = new DungeonRunDomainService();
        var run = service.BeginParty([leader, member], Tutorial, difficulty: 0);
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

        var ordinary = service.TryMove(Character, new MazeCell(1, 1));
        Assert.NotNull(ordinary);
        Assert.True(ordinary.MayLeaveCurrentRoom);

        var boss = service.TryMove(Character, Tutorial.BossCell);
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
        DungeonRoomActor[] declared = [new DungeonRoomActor(61670, 1, 0)];
        var service = new DungeonRunDomainService();
        service.Begin(Character, Tutorial, difficulty: 0);
        service.TryReportActorDead(Character, FirstSequence);

        var moved = service.TryMove(Character, new MazeCell(1, 1));

        Assert.NotNull(moved);
        Assert.False(moved.IsCleared(declared, FirstSequence));
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

        Assert.Null(service.TryTakeGroundDrop(Character, 1));
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
        runs.Begin(Character, Tutorial, difficulty: 0);

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
        runs.Begin(Character, Tutorial, difficulty: 0);
        runs.TrySettle(Character);

        Assert.NotNull(runs.Find(Character));
        Assert.True(runs.End(Character));
    }

    /// <summary>A character in no run has nothing to settle.</summary>
    [Fact]
    public void ACharacterInNoRunHasNothingToSettle() =>
        Assert.Null(new DungeonRunDomainService().TrySettle(Character));
}
