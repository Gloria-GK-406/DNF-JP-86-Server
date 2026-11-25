using System.Collections.Immutable;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.PvpBattle;
using Dfo.Server.Protocol.Game.PvpRoom;
using Dfo.Server.Protocol.Tests.Game.PvpRoom;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.PvpBattle;

/// <summary>
/// What the deadline, settlement, shutdown and departure seams show, and the seats owed from a battle's result
/// screen: owed to the members while their battle runs, shown when their own <c>0x0039</c> is accepted, the next
/// battle starts or they leave, never on a confirmation time-out, dropped with a session or a room that is gone
/// (Spec RQ-035, AC-035; RQ-041 projection side; design-r1 §3.3, §4 b–c).
/// </summary>
public sealed class PvpBattleProjectionTests
{
    private static readonly GameMessageMetadata Meta = GameMessageMetadata.Empty;

    [Fact]
    public Task A_fighter_leaving_mid_fight_is_shown_dead_and_its_seat_is_owed_to_the_members() => Run(async world =>
    {
        var fighting = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B);
        var left = PvpBattleWorld.Without(fighting, 1) with { Phase = PvpRoomPhase.AwaitRank };
        var departure = PvpRooms.Changed(PvpRoomChangeKind.Departed, fighting, left,
            new PvpSeatRelease(1, world.B, PvpSeatReleaseReason.Departed)) with
        {
            Concluded = PvpBattleWorld.ConclusionOf(left, new PvpMatchTerminal(PvpMatchOutcomeKind.Decided, 0, PvpBattleWorld.Now)),
        };

        world.Ready = [world.A, world.C];
        await world.Departures.DepartedAsync(departure, null, default);
        Assert.Equal(1, world.Wake.Count);
        Assert.Empty(world.Timeline.Frames);
        await ProjectDeparturesAsync(world);

        Assert.Collection(world.For(world.A),
            frame => Assert.Equal(new byte[] { 1, 0xFF }, PvpBattlePayloadCodec.Encode(Assert.IsType<PvpDeathMessage>(frame))),
            frame => Assert.IsType<PvpRankRequestMessage>(frame));
        Assert.Collection(world.For(world.C),
            frame => Assert.Equal(PvpRoomSeatOccupancy.Open, Assert.Single(Assert.IsType<PvpSeatStateMessage>(frame).Seats).State.Occupancy));
        Assert.Empty(world.For(world.B));
        Assert.True(world.Deferrals.Owes(world.A, fighting.Id));
        Assert.Empty(world.Departures.SnapshotPending());
    });

    [Fact]
    public Task A_departure_the_cleanup_settled_shows_the_results_after_the_death() => Run(async world =>
    {
        // C left the fight, which ended it; the battle has its ranking open, and C's leaving left nobody to acknowledge it.
        var ranking = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B, world.C) with { Phase = PvpRoomPhase.AwaitRank };
        var left = PvpBattleWorld.Without(ranking, 2);
        var settled = world.Settled(left);
        world.Ready = [world.A, world.B];

        await world.Departures.DepartedAsync(
            PvpRooms.Changed(PvpRoomChangeKind.Departed, ranking, left, new PvpSeatRelease(2, world.C, PvpSeatReleaseReason.Departed)),
            settled, default);
        await ProjectDeparturesAsync(world);

        var own = world.For(world.A);
        Assert.Equal(new byte[] { 2, 0xFF }, PvpBattlePayloadCodec.Encode(Assert.IsType<PvpDeathMessage>(own[0])));
        PvpBattleCommandTests.AssertResult([.. own.Skip(1)], 0, 1, 100, world.Records.Views[world.A.CharacterId], fighters: 3);
        Assert.True(world.Deferrals.Owes(world.A, ranking.Id));
    });

    [Fact]
    public Task Departures_are_claimed_in_order_and_only_once() => Run(async world =>
    {
        var room = PvpBattleWorld.RoomOf(PvpRoomPhase.Waiting, world.A, world.B);
        var change = PvpRooms.Changed(PvpRoomChangeKind.Departed, room, PvpBattleWorld.Without(room, 1),
            new PvpSeatRelease(1, world.B, PvpSeatReleaseReason.Departed));
        await world.Departures.DepartedAsync(change, null, default);
        await world.Departures.DepartedAsync(change, null, default);
        var pending = world.Departures.SnapshotPending();

        Assert.Equal(2, pending.Length);
        Assert.False(world.Departures.TryClaim(pending[1], out _));
        Assert.True(world.Departures.TryClaim(pending[0], out var first));
        Assert.Same(change, first!.Departure);
        Assert.False(await world.RunAsync<ProjectPvpDepartureCommand, bool>(new(pending[0])));
        Assert.True(await world.RunAsync<ProjectPvpDepartureCommand, bool>(new(pending[1])));
    });

    [Fact]
    public Task The_next_battle_shows_what_the_last_one_owed_before_it_starts() => Run(async world =>
    {
        var waiting = PvpBattleWorld.RoomOf(PvpRoomPhase.Waiting, world.A, world.B) with
        {
            Seats = PvpBattleWorld.RoomOf(PvpRoomPhase.Waiting, world.A, world.B).Seats
                .SetItem(1, PvpRooms.Taken(world.B, PvpTeam.Individual, ready: true)),
        };
        var fighting = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B);
        world.Deferrals.Defer(world.A, waiting.Id);
        world.Rooms.Room = waiting;
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.MatchStarted, waiting, fighting);

        Assert.True(await world.SendAsync(world.Room(), new GameSetPvpReadyStateCommand(PvpBattleWorld.Dispatch(world.A),
            new SetPvpReadyStateRequest(Meta, true, true))));

        Assert.Collection(world.For(world.A),
            frame => PvpBattleCommandTests.AssertAllSeats(frame, waiting),
            frame => Assert.Equal(1, Assert.IsType<PvpRoomStateMessage>(frame).State),
            frame => Assert.IsType<PvpReadyStateMessage>(frame),
            frame => Assert.IsType<StartPvpMessage>(frame),
            frame => Assert.Equal(2, Assert.IsType<PvpRoomStateMessage>(frame).State));
        Assert.DoesNotContain(world.For(world.B), frame => frame is PvpSeatStateMessage);
        Assert.False(world.Deferrals.Owes(world.A, waiting.Id));
    });

    [Fact]
    public Task Leaving_the_room_shows_the_room_as_the_lobby_now_sees_it() => Run(async world =>
    {
        var waiting = PvpBattleWorld.RoomOf(PvpRoomPhase.Waiting, world.A, world.B);
        var after = waiting with { Seats = waiting.Seats.SetItem(1, PvpSeat.OpenSeat) };
        world.Deferrals.Defer(world.B, waiting.Id);
        world.Rooms.Room = waiting;
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.Left, waiting, after, new PvpSeatRelease(1, world.B, PvpSeatReleaseReason.Left));

        Assert.True(await world.SendAsync(world.Room(), new GameSetPvpSeatStateCommand(PvpBattleWorld.Dispatch(world.B),
            new SetPvpSeatStateRequest(Meta, 1, 0xFE, true))));

        var own = world.For(world.B);
        Assert.Equal(3, own.Count);
        Assert.Single(Assert.IsType<PvpSeatStateMessage>(own[0]).Seats);
        PvpBattleCommandTests.AssertAllSeats(own[1], after);
        Assert.Equal(1, Assert.IsType<PvpRoomStateMessage>(own[2]).State);
        Assert.False(world.Deferrals.Owes(world.B, waiting.Id));
    });

    [Fact]
    public Task A_session_that_ended_is_owed_nothing_and_shown_nothing() => Run(async world =>
    {
        var result = PvpBattleWorld.RoomOf(PvpRoomPhase.AwaitResult, world.A, world.B, world.C);
        var left = result with
        {
            Seats = result.Seats.SetItem(0, PvpSeat.OpenSeat),
            Match = result.Match! with { Fighters = result.Match.Fighters.SetItem(0, result.Match.Fighters[0] with { Departed = true }) },
        };
        world.Deferrals.Defer(world.A, result.Id);
        world.Ready = [world.B, world.C];

        await world.Departures.DepartedAsync(
            PvpRooms.Changed(PvpRoomChangeKind.Departed, result, left, new PvpSeatRelease(0, world.A, PvpSeatReleaseReason.Departed)),
            null, default);
        await ProjectDeparturesAsync(world);

        Assert.Empty(world.For(world.A));
        Assert.False(world.Deferrals.Owes(world.A, result.Id));
        // The members still on the result screen are owed the seat it left.
        Assert.True(world.Deferrals.Owes(world.B, result.Id));
        Assert.True(world.Deferrals.Owes(world.C, result.Id));
    });

    [Fact]
    public Task A_member_who_closed_its_result_screen_is_shown_a_later_departure_at_once() => Run(async world =>
    {
        var result = PvpBattleWorld.RoomOf(PvpRoomPhase.AwaitResult, world.A, world.B, world.C);
        result = result with
        {
            Match = result.Match! with
            {
                Fighters = result.Match.Fighters.SetItem(0, result.Match.Fighters[0] with { Reports = PvpFighterReports.ResultAcknowledged }),
            },
        };
        var left = result with
        {
            Seats = result.Seats.SetItem(1, PvpSeat.OpenSeat),
            Match = result.Match with { Fighters = result.Match.Fighters.SetItem(1, result.Match.Fighters[1] with { Departed = true }) },
        };
        world.Ready = [world.A, world.C];

        await world.Departures.DepartedAsync(
            PvpRooms.Changed(PvpRoomChangeKind.Departed, result, left, new PvpSeatRelease(1, world.B, PvpSeatReleaseReason.Departed)),
            null, default);
        await ProjectDeparturesAsync(world);

        Assert.Equal(1, Assert.Single(Assert.IsType<PvpSeatStateMessage>(Assert.Single(world.For(world.A))).Seats).Seat);
        Assert.False(world.Deferrals.Owes(world.A, result.Id));
        Assert.Empty(world.For(world.C));
        Assert.True(world.Deferrals.Owes(world.C, result.Id));
    });

    [Fact]
    public Task A_confirmation_time_out_returns_the_room_to_waiting_without_paying_what_is_owed() => Run(async world =>
    {
        var result = PvpBattleWorld.Without(PvpBattleWorld.RoomOf(PvpRoomPhase.AwaitResult, world.A, world.B), 1);
        var waiting = PvpBattleWorld.RoomOf(PvpRoomPhase.Waiting, world.A);
        world.Deferrals.Defer(world.A, result.Id);
        world.Rooms.NextAdvance = PvpRooms.Changed(PvpRoomChangeKind.DeadlinePassed, result, waiting);

        Assert.True(await world.RunAsync<AdvancePvpRoomCommand, bool>(new(result.Id)));

        Assert.Equal(1, Assert.IsType<PvpRoomStateMessage>(Assert.Single(world.For(world.A))).State);
        Assert.Equal(1, Assert.IsType<PvpRoomStateMessage>(Assert.Single(world.For(world.C))).State);
        Assert.True(world.Deferrals.Owes(world.A, result.Id));
    });

    [Fact]
    public Task A_room_gone_drops_every_debt_of_it() => Run(async world =>
    {
        var result = PvpBattleWorld.RoomOf(PvpRoomPhase.AwaitResult, world.A);
        world.Deferrals.Defer(world.A, result.Id);
        world.Deferrals.Defer(world.B, result.Id);

        await world.Departures.DepartedAsync(
            PvpRooms.Changed(PvpRoomChangeKind.Departed, result, null, new PvpSeatRelease(0, world.A, PvpSeatReleaseReason.Departed)),
            null, default);
        await ProjectDeparturesAsync(world);

        Assert.False(world.Deferrals.Owes(world.A, result.Id));
        Assert.False(world.Deferrals.Owes(world.B, result.Id));
        Assert.Equal(0, Assert.IsType<PvpRoomStateMessage>(world.For(world.C)[^1]).State);
    });

    [Fact]
    public Task A_fight_drawn_at_its_deadline_opens_the_ranking() => Run(async world =>
    {
        var fighting = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B);
        var drawn = fighting with { Phase = PvpRoomPhase.AwaitRank };
        world.Rooms.NextAdvance = PvpRooms.Changed(PvpRoomChangeKind.DeadlinePassed, fighting, drawn) with
        {
            Concluded = PvpBattleWorld.ConclusionOf(drawn, new PvpMatchTerminal(PvpMatchOutcomeKind.Draw, null, PvpBattleWorld.Now)),
        };

        Assert.True(await world.RunAsync<AdvancePvpRoomCommand, bool>(new(fighting.Id)));

        Assert.IsType<PvpRankRequestMessage>(Assert.Single(world.For(world.A)));
        Assert.IsType<PvpRankRequestMessage>(Assert.Single(world.For(world.B)));
        Assert.Empty(world.For(world.C));
    });

    [Fact]
    public Task A_fighter_falling_silent_is_shown_dead_by_nobody() => Run(async world =>
    {
        var fighting = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B, world.C);
        var silent = fighting with
        {
            Match = fighting.Match! with
            {
                Fighters = fighting.Match.Fighters.SetItem(2, fighting.Match.Fighters[2] with { Alive = false, Fled = true, Departed = true }),
            },
        };
        world.Rooms.NextAdvance = PvpRooms.Changed(PvpRoomChangeKind.DeadlinePassed, fighting, silent);

        await world.RunAsync<AdvancePvpRoomCommand, bool>(new(fighting.Id));

        foreach (var member in new[] { world.A, world.B, world.C })
            Assert.Equal(new byte[] { 2, 0xFF }, PvpBattlePayloadCodec.Encode(Assert.IsType<PvpDeathMessage>(Assert.Single(world.For(member)))));
    });

    [Theory]
    [InlineData(PvpRoomPhase.AwaitResult, PvpRoomChangeKind.DeadlinePassed)]
    [InlineData(PvpRoomPhase.AwaitRank, PvpRoomChangeKind.SettlementAbandoned)]
    public Task A_result_deadline_or_abandoned_settlement_returns_the_room_and_the_lobby_to_waiting(PvpRoomPhase phase, PvpRoomChangeKind kind) => Run(async world =>
    {
        var battle = PvpBattleWorld.RoomOf(phase, world.A, world.B);
        world.Rooms.NextAdvance = PvpRooms.Changed(kind, battle, PvpBattleWorld.RoomOf(PvpRoomPhase.Waiting, world.A, world.B));

        if (kind == PvpRoomChangeKind.SettlementAbandoned)
            await world.RunAsync<AbandonPvpSettlementCommand, bool>(new(PvpBattleWorld.MatchId));
        else
            await world.RunAsync<AdvancePvpRoomCommand, bool>(new(battle.Id));

        foreach (var member in new[] { world.A, world.B, world.C })
            Assert.Equal(1, Assert.IsType<PvpRoomStateMessage>(Assert.Single(world.For(member))).State);
    });

    [Fact]
    public Task A_scheduled_settlement_shows_each_fighter_its_result() => Run(async world =>
    {
        world.Settlement.Next = world.Settled(PvpBattleWorld.RoomOf(PvpRoomPhase.AwaitRank, world.A, world.B));

        Assert.True(await world.RunAsync<SettlePvpMatchCommand, bool>(new(PvpBattleWorld.MatchId)));

        PvpBattleCommandTests.AssertResult(world.For(world.B), 0, 1, -50, world.Records.Views[world.B.CharacterId]);
    });

    [Fact]
    public Task The_shutdown_cancel_tells_the_rooms_waiting_and_a_failed_projection_does_not_stop_it() => Run(async world =>
    {
        var fighting = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B);
        world.Rooms.NextCancelled = [PvpRooms.Changed(PvpRoomChangeKind.CancelledForShutdown, fighting, PvpBattleWorld.RoomOf(PvpRoomPhase.Waiting, world.A, world.B))];

        Assert.True(await world.RunAsync<CancelPvpBattlesForShutdownCommand, bool>(new()));
        Assert.Equal(1, Assert.IsType<PvpRoomStateMessage>(Assert.Single(world.For(world.C))).State);

        world.Projections = new ThrowingProjections();
        Assert.True(await world.RunAsync<CancelPvpBattlesForShutdownCommand, bool>(new()));
    });

    [Fact]
    public void A_team_winner_is_the_first_seat_still_standing_on_it_and_a_draw_names_nobody()
    {
        var room = PvpRooms.Room(1, PvpBattleMode.Team, 0, PvpRoomPhase.AwaitRank);
        PvpFighter[] fighters =
        [
            new(0, new(1), PvpRooms.Session(1), PvpTeam.First) { Alive = false },
            new(1, new(2), PvpRooms.Session(2), PvpTeam.Second) { Alive = false },
            new(2, new(3), PvpRooms.Session(3), PvpTeam.First),
            new(3, new(4), PvpRooms.Session(4), PvpTeam.Second),
        ];
        PvpMatchConclusion Ended(PvpMatchTerminal terminal) =>
            new(PvpBattleWorld.MatchId, room.Id, 1, room.Channel, PvpBattleMode.Team, PvpBattleWorld.Now, terminal, [.. fighters], PvpBattleWorld.Now);

        Assert.Equal(2, PvpBattleProjector.WinnerSeat(Ended(new(PvpMatchOutcomeKind.Decided, (byte)PvpTeam.First, PvpBattleWorld.Now))));
        Assert.Equal(3, PvpBattleProjector.WinnerSeat(Ended(new(PvpMatchOutcomeKind.Decided, (byte)PvpTeam.Second, PvpBattleWorld.Now))));
        Assert.Equal(0xFF, PvpBattleProjector.WinnerSeat(Ended(new(PvpMatchOutcomeKind.Draw, null, PvpBattleWorld.Now))));
    }

    private static async Task ProjectDeparturesAsync(PvpBattleWorld world)
    {
        foreach (var entry in await world.RunAsync<CapturePvpDeparturesCommand, ImmutableArray<PvpDepartureEntryId>>(new()))
            Assert.True(await world.RunAsync<ProjectPvpDepartureCommand, bool>(new(entry)));
    }

    private static Task Run(Func<PvpBattleWorld, Task> body) => TestGameplay.RunAsync(() => body(new PvpBattleWorld()));
}
