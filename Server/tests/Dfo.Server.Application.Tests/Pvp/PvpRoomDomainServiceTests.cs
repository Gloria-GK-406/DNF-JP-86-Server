using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Pvp;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Pvp;

/// <summary>Spec AC-010–AC-013, AC-020 (domain part) and RQ-071 for the room owner.</summary>
public sealed class PvpRoomDomainServiceTests
{
    // ---- AC-010: create and enter

    [Fact]
    public async Task Creating_a_room_publishes_it_and_takes_the_owner_out_of_town_only_on_commit()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);

        var outcome = await f.Commit(async rooms =>
        {
            var staged = Assert.IsType<PvpRoomOutcome.Changed>(await rooms.CreateAsync(PvpRoomFixture.Creation(owner), default));
            // Staged only: the lobby does not show it, no town move has been made, and this use case sees its own room.
            Assert.Empty(rooms.RoomsOn(PvpRoomFixture.Port));
            Assert.Equal(0, f.Town.Moves);
            Assert.False(staged.Town.Applied);
            Assert.True(rooms.IsSeated(owner.CharacterId));
            return staged;
        });

        Assert.Equal(PvpRoomChangeKind.Created, outcome.Kind);
        Assert.Null(outcome.Before);
        var room = Assert.Single(f.Rooms.RoomsOn(PvpRoomFixture.Port));
        Assert.Equal(outcome.After, room);
        Assert.Equal(1, room.Number);
        Assert.Equal(1, room.Version);
        Assert.Equal(0, room.OwnerSeat);
        Assert.Equal(PvpRoomPhase.Waiting, room.Phase);
        Assert.Equal(PvpRoomOrigin.PlayerCreated, room.Origin);
        Assert.Equal(owner, room.Owner.Session);
        Assert.Equal(7, room.Seats.Count(seat => seat is PvpSeat.Open));
        Assert.DoesNotContain(owner.CharacterId, f.Town.InTown);
        Assert.Equal([TownAreaDepartureReason.PvpRoom], f.Town.DepartureReasons);
        Assert.True(outcome.Town.Applied);
        Assert.Equal(owner, Assert.Single(outcome.Town.Departures).Session);
        Assert.Equal(0, f.Wake.Count);
    }

    [Fact]
    public async Task Entering_takes_the_first_open_seat_and_leaves_town()
    {
        var f = new PvpRoomFixture();
        var (owner, guest) = (f.Join(1), f.Join(2));
        var room = await f.CreateAsync(owner);

        var outcome = Assert.IsType<PvpRoomOutcome.Changed>(
            await f.Commit(rooms => rooms.EnterAsync(guest, room.Number, null, default)));

        Assert.Equal(PvpRoomChangeKind.Entered, outcome.Kind);
        Assert.Equal(1, outcome.After!.SeatOf(guest.CharacterId));
        Assert.Equal(2, outcome.After.Version);
        Assert.DoesNotContain(guest.CharacterId, f.Town.InTown);
        Assert.Equal(guest, Assert.Single(outcome.Town.Departures).Session);
        Assert.Equal(room.Id, f.Rooms.FindSeated(guest.CharacterId)!.Id);
    }

    [Fact]
    public async Task An_observing_owner_needs_two_fighters_and_can_leave_without_causing_a_forfeit()
    {
        var f = new PvpRoomFixture();
        var (owner, first, second) = (f.Join(1), f.Join(2), f.Join(3));
        var room = await f.CreateAsync(owner, PvpBattleMode.Team);
        await f.EnterAsync(first, room);
        await AssertRefused(f, PvpRoomRefusal.InvalidSeat, rooms =>
            rooms.SetSeatAsync(first, seat: 0, PvpSeatAction.Observe, PvpTeam.Individual, default));
        await f.SeatAsync(owner, 0, PvpSeatAction.Observe);
        await f.ReadyAsync(first);
        await AssertRefused(f, PvpRoomRefusal.TooFewMembers, rooms => rooms.SetReadyAsync(owner, true, default));
        await f.EnterAsync(second, room);
        await f.ReadyAsync(second);

        var started = Assert.IsType<PvpRoomOutcome.Changed>(await f.ReadyAsync(owner)).After!;
        Assert.Equal(new[] { first.CharacterId, second.CharacterId }, started.Match!.Fighters.Select(fighter => fighter.CharacterId));
        var left = Assert.IsType<PvpRoomOutcome.Changed>(await f.SeatAsync(owner, 0, PvpSeatAction.Leave));
        Assert.Equal(PvpRoomPhase.Fighting, left.After!.Phase);
        Assert.Null(left.Concluded);
        Assert.Equal(first, left.After.Owner.Session);
        Assert.Equal(started.Match.Fighters, left.After.Match!.Fighters);
        Assert.Contains(owner.CharacterId, f.Town.InTown);
    }

    [Fact]
    public async Task A_password_room_admits_the_right_password_only()
    {
        var f = new PvpRoomFixture();
        var (owner, guest) = (f.Join(1), f.Join(2));
        var room = await f.CreateAsync(owner, password: [1, 2, 3]);
        Assert.True(room.HasPassword);

        await AssertRefused(f, PvpRoomRefusal.WrongPassword, rooms => rooms.EnterAsync(guest, room.Number, [1, 2, 4], default));
        await AssertRefused(f, PvpRoomRefusal.WrongPassword, rooms => rooms.EnterAsync(guest, room.Number, null, default));
        Assert.Equal(1, (await f.EnterAsync(guest, room, [1, 2, 3])).SeatOf(guest.CharacterId));
    }

    [Fact]
    public async Task Entry_is_refused_without_changing_anything_when_full_not_waiting_already_seated_or_in_a_party()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        var room = await f.CreateAsync(owner);
        for (var character = 2; character <= 8; character++) await f.EnterAsync(f.Join(character), room);
        var late = f.Join(9);
        await AssertUnchanged(f, room, late, PvpRoomRefusal.Full, rooms => rooms.EnterAsync(late, room.Number, null, default));

        var other = f.Join(10);
        var second = await f.CreateAsync(other);
        var seated = new CharacterId(2);
        var member = f.Rooms.Find(room.Id)!.Members.Single(x => x.Member.CharacterId == seated).Member.Session;
        await AssertUnchanged(f, second, member, PvpRoomRefusal.AlreadySeated,
            rooms => rooms.EnterAsync(member, second.Number, null, default));

        var partied = f.Join(11);
        f.Parties.InParty.Add(partied.CharacterId);
        await AssertUnchanged(f, second, partied, PvpRoomRefusal.InParty, rooms => rooms.EnterAsync(partied, second.Number, null, default));

        var guest = f.Join(12);
        await f.EnterAsync(guest, second);
        await f.ReadyAsync(guest);
        await f.ReadyAsync(other);
        var started = f.Committed(second);
        Assert.Equal(PvpRoomPhase.Fighting, started.Phase);
        var waiting = f.Join(13);
        await AssertUnchanged(f, started, waiting, PvpRoomRefusal.NotWaiting, rooms => rooms.EnterAsync(waiting, second.Number, null, default));
    }

    [Theory]
    [InlineData(PvpBattleMode.DeathMatchIndividual)]
    [InlineData(PvpBattleMode.DeathMatchTeam)]
    [InlineData((PvpBattleMode)9)]
    public async Task Creating_refuses_unsupported_battle_modes(PvpBattleMode mode)
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        await AssertRefused(f, PvpRoomRefusal.UnsupportedMode, rooms => rooms.CreateAsync(PvpRoomFixture.Creation(owner, mode), default));
        Assert.Empty(f.Rooms.RoomsOn(PvpRoomFixture.Port));
        Assert.Contains(owner.CharacterId, f.Town.InTown);
    }

    [Fact]
    public async Task Creating_is_refused_for_a_bad_map_or_password_a_party_member_or_a_seated_character()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        await AssertRefused(f, PvpRoomRefusal.InvalidMap, rooms => rooms.CreateAsync(PvpRoomFixture.Creation(owner, map: 6), default));
        await AssertRefused(f, PvpRoomRefusal.InvalidPassword,
            rooms => rooms.CreateAsync(PvpRoomFixture.Creation(owner, password: new byte[9]), default));
        f.Parties.InParty.Add(owner.CharacterId);
        await AssertRefused(f, PvpRoomRefusal.InParty, rooms => rooms.CreateAsync(PvpRoomFixture.Creation(owner), default));
        f.Parties.InParty.Clear();
        await f.CreateAsync(owner);
        await AssertRefused(f, PvpRoomRefusal.AlreadySeated, rooms => rooms.CreateAsync(PvpRoomFixture.Creation(owner), default));
        Assert.Single(f.Rooms.RoomsOn(PvpRoomFixture.Port));
    }

    [Fact]
    public async Task Room_numbers_are_the_smallest_free_one_on_the_listener()
    {
        var f = new PvpRoomFixture();
        var (a, b, c) = (f.Join(1), f.Join(2), f.Join(3));
        Assert.Equal(1, (await f.CreateAsync(a)).Number);
        Assert.Equal(2, (await f.CreateAsync(b)).Number);
        Assert.IsType<PvpRoomOutcome.Changed>(await f.SeatAsync(a, 0, PvpSeatAction.Leave));
        Assert.Equal(1, (await f.CreateAsync(c)).Number);
    }

    // ---- AC-011: seats

    [Fact]
    public async Task The_owner_kicks_a_member_back_to_where_they_stood()
    {
        var f = new PvpRoomFixture();
        var (owner, guest) = (f.Join(1), f.Join(2));
        var room = await f.CreateAsync(owner);
        await f.EnterAsync(guest, room);

        await AssertRefused(f, PvpRoomRefusal.NotOwner, rooms => rooms.SetSeatAsync(guest, 0, PvpSeatAction.Kick, default, default));
        var kicked = Assert.IsType<PvpRoomOutcome.Changed>(await f.SeatAsync(owner, 1, PvpSeatAction.Kick));

        Assert.Equal(PvpRoomChangeKind.Kicked, kicked.Kind);
        Assert.Equal(new PvpSeatRelease(1, guest, PvpSeatReleaseReason.Kicked), Assert.Single(kicked.Released));
        Assert.IsType<PvpSeat.Open>(kicked.After!.Seats[1]);
        Assert.False(f.Rooms.IsSeated(guest.CharacterId));
        Assert.Contains(guest.CharacterId, f.Town.InTown);
        var back = Assert.Single(f.Town.ReturnRequests);
        Assert.Equal((guest, PvpRoomFixture.Channel.ChannelId, PvpRoomFixture.Standing), (back.Session, back.ChannelId, back.Position));
        Assert.Equal(guest, Assert.Single(kicked.Town.Returns).Request.Session);
    }

    [Fact]
    public async Task A_member_leaves_and_the_owner_leaving_hands_the_room_to_the_next_member()
    {
        var f = new PvpRoomFixture();
        var (owner, second, third) = (f.Join(1), f.Join(2), f.Join(3));
        var room = await f.CreateAsync(owner);
        await f.EnterAsync(second, room);
        await f.EnterAsync(third, room);

        var left = Assert.IsType<PvpRoomOutcome.Changed>(await f.SeatAsync(second, 1, PvpSeatAction.Leave));
        Assert.Equal(PvpRoomChangeKind.Left, left.Kind);
        Assert.Equal(PvpSeatReleaseReason.Left, Assert.Single(left.Released).Reason);
        Assert.Contains(second.CharacterId, f.Town.InTown);

        var handed = Assert.IsType<PvpRoomOutcome.Changed>(await f.SeatAsync(owner, 0, PvpSeatAction.Leave));
        Assert.Equal(2, handed.After!.OwnerSeat);
        Assert.Equal(third, handed.After.Owner.Session);
        Assert.Contains(owner.CharacterId, f.Town.InTown);
    }

    [Fact]
    public async Task The_last_member_out_ends_the_room()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        var room = await f.CreateAsync(owner);

        var ended = Assert.IsType<PvpRoomOutcome.Changed>(await f.SeatAsync(owner, 0, PvpSeatAction.Leave));

        Assert.Null(ended.After);
        Assert.Empty(f.Rooms.RoomsOn(PvpRoomFixture.Port));
        Assert.Null(f.Rooms.Find(room.Id));
        Assert.False(f.Rooms.IsSeated(owner.CharacterId));
        Assert.Contains(owner.CharacterId, f.Town.InTown);
    }

    [Fact]
    public async Task The_owner_closes_and_opens_seats_but_a_lone_owner_keeps_one_open()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        var room = await f.CreateAsync(owner);
        for (var seat = 1; seat < 7; seat++)
            Assert.Equal(PvpRoomChangeKind.SeatClosed,
                Assert.IsType<PvpRoomOutcome.Changed>(await f.SeatAsync(owner, seat, PvpSeatAction.Close)).Kind);

        await AssertRefused(f, PvpRoomRefusal.LastOpenSeat, rooms => rooms.SetSeatAsync(owner, 7, PvpSeatAction.Close, default, default));
        await AssertRefused(f, PvpRoomRefusal.InvalidSeat, rooms => rooms.SetSeatAsync(owner, 7, PvpSeatAction.Open, default, default));
        var opened = Assert.IsType<PvpRoomOutcome.Changed>(await f.SeatAsync(owner, 3, PvpSeatAction.Open));
        Assert.Equal(PvpRoomChangeKind.SeatOpened, opened.Kind);
        Assert.IsType<PvpSeat.Open>(f.Committed(room).Seats[3]);

        // With a second member the last open seat may close: the room is simply full.
        var guest = f.Join(2);
        await f.EnterAsync(guest, room);
        Assert.IsType<PvpRoomOutcome.Changed>(await f.SeatAsync(owner, 7, PvpSeatAction.Close));
        Assert.DoesNotContain(f.Committed(room).Seats, seat => seat is PvpSeat.Open);
    }

    [Fact]
    public async Task A_member_changes_team_only_in_a_team_room()
    {
        var f = new PvpRoomFixture();
        var (owner, guest) = (f.Join(1), f.Join(2));
        var room = await f.CreateAsync(owner, PvpBattleMode.Team);
        await f.EnterAsync(guest, room);
        Assert.Equal([PvpTeam.First, PvpTeam.Second], f.Committed(room).Members.Select(x => x.Member.Team));

        var moved = Assert.IsType<PvpRoomOutcome.Changed>(await f.SeatAsync(guest, 1, PvpSeatAction.ChangeTeam, PvpTeam.First));
        Assert.Equal(PvpTeam.First, ((PvpSeat.Taken)moved.After!.Seats[1]).Team);
        await AssertRefused(f, PvpRoomRefusal.InvalidTeam,
            rooms => rooms.SetSeatAsync(guest, 1, PvpSeatAction.ChangeTeam, PvpTeam.Individual, default));
        await AssertRefused(f, PvpRoomRefusal.InvalidSeat,
            rooms => rooms.SetSeatAsync(guest, 0, PvpSeatAction.ChangeTeam, PvpTeam.Second, default));
    }

    // ---- AC-012: mode, map, start

    [Fact]
    public async Task Total_match_mode_on_channel_68_keeps_one_player_per_side()
    {
        var f = new PvpRoomFixture { DuelChannel = new(68, PvpRoomFixture.Port, ChannelEntryKind.FreeDuel, 13) };
        var (owner, guest, third) = (f.Join(1), f.Join(2), f.Join(3));
        var room = await f.CreateAsync(owner);
        await f.EnterAsync(guest, room);
        var changed = Assert.IsType<PvpRoomOutcome.Changed>(
            await f.Commit(rooms => rooms.SetModeAsync(owner, PvpBattleMode.TotalMatch, default)));

        Assert.Equal(PvpBattleMode.TotalMatch, changed.After!.Mode);
        Assert.Equal([PvpTeam.First, PvpTeam.Second], changed.After.Members.Select(x => x.Member.Team));
        await AssertRefused(f, PvpRoomRefusal.Full, rooms => rooms.EnterAsync(third, room.Number, null, default));
        await f.ReadyAsync(guest);
        await AssertRefused(f, PvpRoomRefusal.InvalidTeam, rooms => rooms.SetReadyAsync(owner, true, default));
        Assert.Equal(PvpRoomPhase.Waiting, f.Committed(room).Phase);

        await f.Commit(rooms => rooms.SetModeAsync(owner, PvpBattleMode.Individual, default));
        await f.EnterAsync(third, room);
        await AssertRefused(f, PvpRoomRefusal.Full, rooms => rooms.SetModeAsync(owner, PvpBattleMode.TotalMatch, default));
        Assert.Equal(PvpBattleMode.Individual, f.Committed(room).Mode);
    }

    [Fact]
    public async Task Integrated_manual_matching_channel_refuses_total_match_mode()
    {
        var f = new PvpRoomFixture { DuelChannel = new(50, PvpRoomFixture.Port, ChannelEntryKind.TraditionalDuel, 24) };
        var owner = f.Join(1);
        await AssertRefused(f, PvpRoomRefusal.UnsupportedMode,
            rooms => rooms.CreateAsync(PvpRoomFixture.Creation(owner, PvpBattleMode.TotalMatch), default));
        await f.CreateAsync(owner);
        await AssertRefused(f, PvpRoomRefusal.UnsupportedMode, rooms => rooms.SetModeAsync(owner, PvpBattleMode.TotalMatch, default));
    }

    [Fact]
    public async Task Switching_mode_reassigns_teams_and_unsupported_modes_are_refused()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        var room = await f.CreateAsync(owner);
        await f.EnterAsync(f.Join(2), room);
        await f.EnterAsync(f.Join(3), room);
        Assert.All(f.Committed(room).Members, x => Assert.Equal(PvpTeam.Individual, x.Member.Team));

        foreach (var mode in new[] { PvpBattleMode.DeathMatchIndividual, PvpBattleMode.DeathMatchTeam, PvpBattleMode.Practice })
            await AssertRefused(f, PvpRoomRefusal.UnsupportedMode, rooms => rooms.SetModeAsync(owner, mode, default));

        var team = Assert.IsType<PvpRoomOutcome.Changed>(await f.Commit(rooms => rooms.SetModeAsync(owner, PvpBattleMode.Team, default)));
        Assert.Equal(PvpBattleMode.Team, team.After!.Mode);
        Assert.Equal([PvpTeam.First, PvpTeam.Second, PvpTeam.First], team.After.Members.Select(x => x.Member.Team));

        var individual = Assert.IsType<PvpRoomOutcome.Changed>(
            await f.Commit(rooms => rooms.SetModeAsync(owner, PvpBattleMode.Individual, default)));
        Assert.All(individual.After!.Members, x => Assert.Equal(PvpTeam.Individual, x.Member.Team));
    }

    [Fact]
    public async Task The_map_is_an_index_into_the_map_order_and_only_the_owner_sets_it()
    {
        var f = new PvpRoomFixture();
        var (owner, guest) = (f.Join(1), f.Join(2));
        var room = await f.CreateAsync(owner);
        await f.EnterAsync(guest, room);

        await AssertRefused(f, PvpRoomRefusal.InvalidMap, rooms => rooms.SetMapAsync(owner, 6, default));
        await AssertRefused(f, PvpRoomRefusal.InvalidMap, rooms => rooms.SetMapAsync(owner, -1, default));
        await AssertRefused(f, PvpRoomRefusal.NotOwner, rooms => rooms.SetMapAsync(guest, 3, default));
        Assert.Equal(PvpRoomChangeKind.MapChanged, Assert.IsType<PvpRoomOutcome.Changed>(
            await f.Commit(rooms => rooms.SetMapAsync(owner, 0, default))).Kind);
        Assert.Equal(0, f.Committed(room).MapIndex);
    }

    [Fact]
    public async Task The_owner_cannot_start_alone_or_before_everyone_is_ready()
    {
        var f = new PvpRoomFixture();
        var (owner, guest) = (f.Join(1), f.Join(2));
        var room = await f.CreateAsync(owner);
        await AssertRefused(f, PvpRoomRefusal.TooFewMembers, rooms => rooms.SetReadyAsync(owner, true, default));
        await f.EnterAsync(guest, room);
        await AssertRefused(f, PvpRoomRefusal.NotAllReady, rooms => rooms.SetReadyAsync(owner, true, default));

        var ready = Assert.IsType<PvpRoomOutcome.Changed>(await f.ReadyAsync(guest));
        Assert.Equal(PvpRoomChangeKind.ReadyChanged, ready.Kind);
        Assert.True(((PvpSeat.Taken)ready.After!.Seats[1]).Ready);
        Assert.Equal(PvpRoomPhase.Waiting, ready.After.Phase);
        Assert.Equal(0, f.Wake.Count);
    }

    [Fact]
    public async Task A_team_battle_only_starts_with_equal_teams()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        var room = await f.CreateAsync(owner, PvpBattleMode.Team);
        var guests = new[] { f.Join(2), f.Join(3) };
        foreach (var guest in guests)
        {
            await f.EnterAsync(guest, room);
            await f.ReadyAsync(guest);
        }

        await AssertRefused(f, PvpRoomRefusal.UnbalancedTeams, rooms => rooms.SetReadyAsync(owner, true, default));
        await f.SeatAsync(guests[1], 2, PvpSeatAction.Leave);
        Assert.Equal(PvpRoomChangeKind.MatchStarted, Assert.IsType<PvpRoomOutcome.Changed>(await f.ReadyAsync(owner)).Kind);
    }

    [Fact]
    public async Task Starting_freezes_the_fighters_and_begins_the_fight_with_its_deadlines()
    {
        var f = new PvpRoomFixture();
        var (owner, guest) = (f.Join(1), f.Join(2));
        var room = await f.CreateAsync(owner);
        await f.Commit(rooms => rooms.SetMapAsync(owner, 0, default));
        await f.EnterAsync(guest, room);
        await f.ReadyAsync(guest);
        var before = f.Committed(room);

        var started = Assert.IsType<PvpRoomOutcome.Changed>(await f.ReadyAsync(owner));

        var after = started.After!;
        Assert.Equal(PvpRoomChangeKind.MatchStarted, started.Kind);
        // Spec SA-1: fighting from the start; the time limit and the heartbeats count from it.
        Assert.Equal(PvpRoomPhase.Fighting, after.Phase);
        Assert.Equal(before.Version + 1, after.Version);
        var match = Assert.IsType<PvpMatch>(after.Match);
        Assert.Equal(f.Clock.Now, match.StartedAt);
        Assert.Equal(f.Clock.Now + PvpRoomFixture.BattleTime + TimeSpan.FromSeconds(30), match.PhaseDeadline);
        Assert.All(match.Fighters, fighter => Assert.Equal(f.Clock.Now, fighter.LastHeartbeat));
        Assert.InRange(match.Map, 1, 5);
        Assert.Null(match.ModeState);
        Assert.Equal([(0, owner), (1, guest)], match.Fighters.Select(x => (x.Seat, x.Session)));
        Assert.Equal(1, f.Wake.Count);
        await AssertRefused(f, PvpRoomRefusal.NotWaiting, rooms => rooms.SetSeatAsync(guest, 1, PvpSeatAction.Leave, default, default));
    }

    // ---- AC-013: one room per character, rooms and parties exclude each other

    [Fact]
    public async Task A_seated_character_is_reported_for_the_party_invitation_check()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        Assert.False(f.Rooms.IsSeated(owner.CharacterId));
        await f.CreateAsync(owner);
        Assert.True(f.Rooms.IsSeated(owner.CharacterId));
    }

    // ---- AC-020 (domain): invitations

    [Fact]
    public async Task An_accepted_invitation_enters_the_room()
    {
        var f = new PvpRoomFixture();
        var (owner, target) = (f.Join(1), f.Join(2));
        var room = await f.CreateAsync(owner);

        var sent = Assert.IsType<PvpInviteOutcome.Sent>(await f.Commit(rooms => rooms.InviteAsync(owner, target, default)));
        Assert.Equal(room.Id, sent.Invitation.RoomId);
        Assert.Equal(f.Clock.Now + TimeSpan.FromSeconds(30), sent.Invitation.ExpiresAt);

        var accepted = Assert.IsType<PvpRoomOutcome.Changed>(
            await f.Commit(rooms => rooms.AcceptInviteAsync(target, owner.CharacterId, default)));
        Assert.Equal(PvpRoomChangeKind.InviteAccepted, accepted.Kind);
        Assert.Equal(1, accepted.After!.SeatOf(target.CharacterId));
        Assert.DoesNotContain(target.CharacterId, f.Town.InTown);
        // An invitation is answered once.
        await AssertRefused(f, PvpRoomRefusal.AlreadySeated, rooms => rooms.EnterAsync(target, room.Number, null, default));
        await AssertRefused(f, PvpRoomRefusal.InviteMissing, rooms => rooms.AcceptInviteAsync(target, owner.CharacterId, default));
    }

    [Fact]
    public async Task An_acceptance_echoing_another_peer_token_finds_no_invitation_and_leaves_it_standing()
    {
        var f = new PvpRoomFixture();
        var (owner, target) = (f.Join(1), f.Join(2));
        _ = await f.CreateAsync(owner);
        _ = Assert.IsType<PvpInviteOutcome.Sent>(await f.Commit(rooms => rooms.InviteAsync(owner, target, default, 64)));

        await AssertRefused(f, PvpRoomRefusal.InviteMissing, rooms => rooms.AcceptInviteAsync(target, owner.CharacterId, default, 65));
        var accepted = Assert.IsType<PvpRoomOutcome.Changed>(
            await f.Commit(rooms => rooms.AcceptInviteAsync(target, owner.CharacterId, default, 64)));
        Assert.Equal(PvpRoomChangeKind.InviteAccepted, accepted.Kind);
    }

    [Fact]
    public async Task An_invitation_expires_after_thirty_seconds()
    {
        var f = new PvpRoomFixture();
        var (owner, target) = (f.Join(1), f.Join(2));
        await f.CreateAsync(owner);
        await f.Commit(rooms => rooms.InviteAsync(owner, target, default));

        f.Clock.Now += TimeSpan.FromSeconds(30);
        await AssertRefused(f, PvpRoomRefusal.InviteExpired, rooms => rooms.AcceptInviteAsync(target, owner.CharacterId, default));
        await AssertRefused(f, PvpRoomRefusal.InviteMissing, rooms => rooms.AcceptInviteAsync(target, owner.CharacterId, default));
        Assert.False(f.Rooms.IsSeated(target.CharacterId));
    }

    [Fact]
    public async Task Accepting_rechecks_that_the_room_is_waiting_and_not_full()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        var room = await f.CreateAsync(owner);
        var (full, started) = (f.Join(20), f.Join(21));
        await f.Commit(rooms => rooms.InviteAsync(owner, full, default));
        await f.Commit(rooms => rooms.InviteAsync(owner, started, default));
        for (var seat = 1; seat < 7; seat++) await f.SeatAsync(owner, seat, PvpSeatAction.Close);
        await f.EnterAsync(f.Join(2), room);

        await AssertRefused(f, PvpRoomRefusal.Full, rooms => rooms.AcceptInviteAsync(full, owner.CharacterId, default));

        await f.SeatAsync(owner, 1, PvpSeatAction.Open);
        await f.ReadyAsync(f.Committed(room).Members.Single(x => x.Seat == 7).Member.Session);
        await f.ReadyAsync(owner);
        await AssertRefused(f, PvpRoomRefusal.NotWaiting, rooms => rooms.AcceptInviteAsync(started, owner.CharacterId, default));
    }

    [Fact]
    public async Task Accepting_from_a_replaced_session_is_refused()
    {
        var f = new PvpRoomFixture();
        var (owner, target) = (f.Join(1), f.Join(2));
        await f.CreateAsync(owner);
        await f.Commit(rooms => rooms.InviteAsync(owner, target, default));

        var replaced = f.Join(2);
        Assert.NotEqual(target, replaced);
        await AssertRefused(f, PvpRoomRefusal.SessionNotCurrent, rooms => rooms.AcceptInviteAsync(replaced, owner.CharacterId, default));
        Assert.False(f.Rooms.IsSeated(target.CharacterId));
    }

    [Fact]
    public async Task Inviting_is_refused_for_a_target_in_a_party_seated_outside_the_lobby_or_blacklisted()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        await f.CreateAsync(owner);

        var partied = f.Join(2);
        f.Parties.InParty.Add(partied.CharacterId);
        await AssertInviteRefused(f, PvpRoomRefusal.TargetInParty, owner, partied);

        var seated = f.Join(3);
        await f.CreateAsync(seated);
        await AssertInviteRefused(f, PvpRoomRefusal.TargetSeated, owner, seated);

        var away = f.Join(4);
        TestGameplay.Run(() => f.Lobby.ClearReady(away.CharacterId));
        await AssertInviteRefused(f, PvpRoomRefusal.TargetNotInLobby, owner, away);

        var (blocked, blocking) = (f.Join(5), f.Join(6));
        f.Relations.Add(owner, blocked);
        f.Relations.Add(blocking, owner);
        await AssertInviteRefused(f, PvpRoomRefusal.Blacklisted, owner, blocked);
        await AssertInviteRefused(f, PvpRoomRefusal.Blacklisted, owner, blocking);

        await AssertInviteRefused(f, PvpRoomRefusal.NotSeated, f.Join(7), f.Join(8));
    }

    // ---- RQ-071: the room changes with its use case's transaction

    [Fact]
    public async Task Writing_without_a_transaction_is_refused()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        await TestGameplay.RunAsync(async () =>
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Rooms.CreateAsync(PvpRoomFixture.Creation(owner), default).AsTask()));
        Assert.Empty(f.Rooms.RoomsOn(PvpRoomFixture.Port));
    }

    [Fact]
    public async Task A_failed_database_commit_leaves_rooms_numbers_town_and_scheduler_as_they_were()
    {
        var f = new PvpRoomFixture();
        var (owner, guest) = (f.Join(1), f.Join(2));
        var room = await f.CreateAsync(owner);
        await f.EnterAsync(guest, room);
        await f.ReadyAsync(guest);
        var before = f.Committed(room);
        var moves = f.Town.Moves;
        var newcomer = f.Join(3);

        await TestGameplay.RunAsync(async () =>
        {
            var transaction = new DomainTransaction();
            f.Transactions.Current = transaction;
            try
            {
                // The one participant whose commit may fail, as the SQLite store is.
                transaction.Enlist(new object(), () => new FailingStore());
                Assert.IsType<PvpRoomOutcome.Changed>(await f.Rooms.SetReadyAsync(owner, true, default));
                var created = Assert.IsType<PvpRoomOutcome.Changed>(
                    await f.Rooms.CreateAsync(PvpRoomFixture.Creation(newcomer), default));
                Assert.Equal(2, created.After!.Number);
                await Assert.ThrowsAsync<IOException>(() => transaction.CommitAsync(default).AsTask());
                Assert.False(created.Town.Applied);
            }
            finally { f.Transactions.Current = null; }
        });

        Assert.Same(before, f.Committed(room));
        Assert.Equal(PvpRoomPhase.Waiting, f.Committed(room).Phase);
        Assert.False(f.Rooms.IsSeated(newcomer.CharacterId));
        Assert.Equal(moves, f.Town.Moves);
        Assert.Contains(newcomer.CharacterId, f.Town.InTown);
        Assert.Equal(0, f.Wake.Count);
        // The number staged in the failed use case was never taken.
        Assert.Equal(2, (await f.CreateAsync(newcomer)).Number);
    }

    [Fact]
    public async Task A_room_changed_by_another_use_case_before_commit_refuses_the_commit()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(1);
        var room = await f.CreateAsync(owner);
        for (var seat = 2; seat < 8; seat++) await f.SeatAsync(owner, seat, PvpSeatAction.Close);
        var (first, second) = (f.Join(2), f.Join(3));

        await TestGameplay.RunAsync(async () =>
        {
            var slow = new DomainTransaction();
            f.Transactions.Current = slow;
            Assert.IsType<PvpRoomOutcome.Changed>(await f.Rooms.EnterAsync(first, room.Number, null, default));

            var fast = new DomainTransaction();
            f.Transactions.Current = fast;
            Assert.IsType<PvpRoomOutcome.Changed>(await f.Rooms.EnterAsync(second, room.Number, null, default));
            await fast.CommitAsync(default);

            f.Transactions.Current = slow;
            await Assert.ThrowsAsync<InvalidOperationException>(() => slow.CommitAsync(default).AsTask());
            f.Transactions.Current = null;
        });

        Assert.Equal(second, f.Committed(room).Members.Single(x => x.Seat == 1).Member.Session);
        Assert.False(f.Rooms.IsSeated(first.CharacterId));
        Assert.Contains(first.CharacterId, f.Town.InTown);
    }

    [Fact]
    public async Task A_seat_taken_by_a_session_replaced_before_commit_refuses_the_commit()
    {
        var f = new PvpRoomFixture();
        var (owner, guest) = (f.Join(1), f.Join(2));
        var room = await f.CreateAsync(owner);

        await TestGameplay.RunAsync(async () =>
        {
            var transaction = new DomainTransaction();
            f.Transactions.Current = transaction;
            Assert.IsType<PvpRoomOutcome.Changed>(await f.Rooms.EnterAsync(guest, room.Number, null, default));
            f.Transactions.Current = null;
            _ = f.Join(2);
            f.Transactions.Current = transaction;
            await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.CommitAsync(default).AsTask());
            f.Transactions.Current = null;
        });

        Assert.Equal(1, f.Committed(room).MemberCount);
    }

    private static async Task AssertRefused(PvpRoomFixture f, PvpRoomRefusal reason, Func<PvpRoomDomainService, ValueTask<PvpRoomOutcome>> call)
    {
        var outcome = await f.Commit(call);
        Assert.Equal(reason, Assert.IsType<PvpRoomOutcome.Refused>(outcome).Reason);
    }

    private static async Task AssertInviteRefused(PvpRoomFixture f, PvpRoomRefusal reason, Dfo.Server.Data.World.SelectedSessionIdentity inviter,
        Dfo.Server.Data.World.SelectedSessionIdentity invitee)
    {
        var outcome = await f.Commit(rooms => rooms.InviteAsync(inviter, invitee, default));
        Assert.Equal(reason, Assert.IsType<PvpInviteOutcome.Refused>(outcome).Reason);
    }

    private static async Task AssertUnchanged(PvpRoomFixture f, PvpRoomSnapshot room, Dfo.Server.Data.World.SelectedSessionIdentity actor,
        PvpRoomRefusal reason, Func<PvpRoomDomainService, ValueTask<PvpRoomOutcome>> call)
    {
        var before = f.Committed(room);
        var inTown = f.Town.InTown.Contains(actor.CharacterId);
        await AssertRefused(f, reason, call);
        Assert.Same(before, f.Committed(room));
        Assert.Equal(inTown, f.Town.InTown.Contains(actor.CharacterId));
    }

    private sealed class FailingStore : ITransactionParticipant
    {
        public bool CommitCanFail => true;
        public ValueTask PrepareAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken cancellationToken) => ValueTask.FromException(new IOException("disk full"));
        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
