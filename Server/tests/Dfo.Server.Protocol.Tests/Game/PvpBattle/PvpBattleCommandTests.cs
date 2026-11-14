using System.Collections.Immutable;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.PvpBattle;
using Dfo.Server.Protocol.Game.PvpRoom;
using Dfo.Server.Protocol.Tests.Game.PvpRoom;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.PvpBattle;

/// <summary>
/// The battle reports against a scripted room owner: what reaches the room, and what each fighter and the lobby are
/// shown, in order (design-r1 §4 b; Spec AC-031, AC-032, AC-034, AC-035 projection side, AC-075).
/// </summary>
public sealed class PvpBattleCommandTests
{
    private static readonly GameMessageMetadata Meta = GameMessageMetadata.Empty;

    public static TheoryData<string, int> Malformed => new()
    {
        { "load", 1 }, { "heartbeat", 2 }, { "close", 1 }, { "link", 3 }, { "death", 2 }, { "death", 7 }, { "timeout", 31 }, { "timeout", 33 },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public Task A_malformed_report_never_reaches_the_room(string report, int length) => Run(async world =>
    {
        world.Rooms.Room = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B);
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.Heartbeat, world.Rooms.Room, world.Rooms.Room);

        Assert.False(await SendAsync(world, world.A, report, new byte[length]));

        Assert.Empty(world.Rooms.Calls);
        Assert.Empty(world.Timeline.Frames);
    });

    [Theory]
    [InlineData("load", nameof(IPvpRoomDomainService.ReportLoadedAsync))]
    [InlineData("link", nameof(IPvpRoomDomainService.ReportPeerLinkedAsync))]
    [InlineData("heartbeat", nameof(IPvpRoomDomainService.HeartbeatAsync))]
    [InlineData("death", nameof(IPvpRoomDomainService.ReportDeathAsync))]
    [InlineData("timeout", nameof(IPvpRoomDomainService.ReportTimeoutAsync))]
    [InlineData("rank", nameof(IPvpRoomDomainService.AcknowledgeRankAsync))]
    [InlineData("close", nameof(IPvpRoomDomainService.AcknowledgeResultAsync))]
    public Task A_report_the_room_ignores_changes_nothing_and_answers_nothing(string report, string call) => Run(async world =>
    {
        world.Rooms.Room = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B);
        world.Rooms.Next = new PvpRoomOutcome.Refused(PvpRoomRefusal.WrongPhase);

        Assert.False(await SendAsync(world, world.A, report, WellFormed(report, world.A)));

        Assert.Equal([call], world.Rooms.Calls);
        Assert.Empty(world.Timeline.Frames);
        Assert.Empty(world.Settlement.Calls);
    });

    [Fact]
    public Task The_time_out_values_reach_the_room_as_they_are() => Run(async world =>
    {
        world.Rooms.Next = new PvpRoomOutcome.Refused(PvpRoomRefusal.TooEarly);
        var values = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();
        await SendAsync(world, world.A, "timeout", values);
        Assert.Equal(values, world.Rooms.LastTimeOut.ToArray());
    });

    [Theory]
    [InlineData("fighter", true)]
    [InlineData("nobody", false)]
    [InlineData("stranger", false)]
    public Task A_death_names_its_killer_by_the_fighter_holding_that_user_id(string killer, bool credited) => Run(async world =>
    {
        world.Rooms.Room = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B);
        world.Rooms.Next = new PvpRoomOutcome.Refused(PvpRoomRefusal.NotAlive);
        ushort uid = killer switch { "fighter" => world.A.WireUserId, "nobody" => 0, _ => world.C.WireUserId };

        await world.SendAsync(world.Battle(), new GameDiePvpCharacterCommand(PvpBattleWorld.Dispatch(world.B),
            PvpBattlePayloadCodec.DecodeDeath(Meta, Death(world.B.WireUserId, uid))));

        Assert.Equal([nameof(IPvpRoomDomainService.ReportDeathAsync)], world.Rooms.Calls);
        Assert.Equal(credited ? world.A.CharacterId : null, world.Rooms.LastKiller);
    });

    [Fact]
    public Task A_death_reported_for_someone_else_is_refused_before_the_room() => Run(async world =>
    {
        world.Rooms.Room = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B);

        Assert.False(await world.SendAsync(world.Battle(), new GameDiePvpCharacterCommand(PvpBattleWorld.Dispatch(world.B),
            PvpBattlePayloadCodec.DecodeDeath(Meta, Death(world.A.WireUserId, world.B.WireUserId)))));

        Assert.Empty(world.Rooms.Calls);
    });

    [Fact]
    public Task An_ending_death_shows_the_members_the_death_then_the_ranking_and_the_lobby_nothing() => Run(async world =>
    {
        var fighting = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B);
        var ended = fighting with { Phase = PvpRoomPhase.AwaitRank };
        world.Rooms.Room = fighting;
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.Died, fighting, ended) with
        {
            Death = new PvpDeath(1, 0),
            Concluded = PvpBattleWorld.ConclusionOf(ended, new PvpMatchTerminal(PvpMatchOutcomeKind.Decided, 0, PvpBattleWorld.Now)),
        };

        Assert.True(await SendAsync(world, world.B, "death", Death(world.B.WireUserId, world.A.WireUserId)));

        foreach (var member in new[] { world.A, world.B })
            Assert.Collection(world.For(member),
                frame => Assert.Equal((1, (int?)0), (Assert.IsType<PvpDeathMessage>(frame).VictimSeat, ((PvpDeathMessage)frame).KillerSeat)),
                frame => Assert.IsType<PvpRankRequestMessage>(frame));
        Assert.Empty(world.For(world.C));
    });

    [Fact]
    public Task A_death_that_does_not_end_the_fight_shows_only_the_death() => Run(async world =>
    {
        var fighting = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B, world.C);
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.Died, fighting, fighting) with { Death = new PvpDeath(2, null) };

        Assert.True(await SendAsync(world, world.C, "death", Death(world.C.WireUserId, 0)));

        Assert.Equal(new byte[] { 2, 0xFF }, PvpBattlePayloadCodec.Encode(Assert.IsType<PvpDeathMessage>(Assert.Single(world.For(world.A)))));
    });

    [Fact]
    public Task The_last_rank_acknowledgement_settles_and_each_fighter_sees_its_result_then_its_record() => Run(async world =>
    {
        var ranking = PvpBattleWorld.RoomOf(PvpRoomPhase.AwaitRank, world.A, world.B);
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.RankAcknowledged, ranking, ranking) with { SettlementDue = PvpBattleWorld.MatchId };
        world.Settlement.Next = world.Settled(ranking);

        Assert.True(await SendAsync(world, world.B, "rank", new byte[70]));

        Assert.Equal([PvpBattleWorld.MatchId], world.Settlement.Calls);
        AssertResult(world.For(world.A), winner: 0, grade: 1, change: 100, record: world.Records.Views[world.A.CharacterId]);
        AssertResult(world.For(world.B), winner: 0, grade: 1, change: -50, record: world.Records.Views[world.B.CharacterId]);
        Assert.Empty(world.For(world.C));
    });

    [Fact]
    public Task A_settlement_the_room_no_longer_waits_on_shows_nothing() => Run(async world =>
    {
        var ranking = PvpBattleWorld.RoomOf(PvpRoomPhase.AwaitRank, world.A, world.B);
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.RankAcknowledged, ranking, ranking) with { SettlementDue = PvpBattleWorld.MatchId };
        var settled = world.Settled(ranking);
        world.Settlement.Next = settled with { Room = null };

        Assert.True(await SendAsync(world, world.B, "rank", []));

        Assert.Empty(world.Timeline.Frames);
    });

    [Fact]
    public Task Death_ranking_results_and_the_room_waiting_again_arrive_in_that_order() => Run(async world =>
    {
        var fighting = PvpBattleWorld.RoomOf(PvpRoomPhase.Fighting, world.A, world.B);
        var ranking = fighting with { Phase = PvpRoomPhase.AwaitRank };
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.Died, fighting, ranking) with
        {
            Death = new PvpDeath(1, 0),
            Concluded = PvpBattleWorld.ConclusionOf(ranking, new PvpMatchTerminal(PvpMatchOutcomeKind.Decided, 0, PvpBattleWorld.Now)),
        };
        await SendAsync(world, world.B, "death", Death(world.B.WireUserId, world.A.WireUserId));
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.RankAcknowledged, ranking, ranking) with { SettlementDue = PvpBattleWorld.MatchId };
        var settled = world.Settled(ranking);
        world.Settlement.Next = settled;
        await SendAsync(world, world.A, "rank", []);
        var result = settled.Room!.After!;
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.ResultAcknowledged, result, PvpBattleWorld.RoomOf(PvpRoomPhase.Waiting, world.A, world.B));
        await SendAsync(world, world.B, "close", []);

        Assert.Collection(world.For(world.A),
            frame => Assert.IsType<PvpDeathMessage>(frame),
            frame => Assert.IsType<PvpRankRequestMessage>(frame),
            frame => Assert.IsType<EndPvpMessage>(frame),
            frame => Assert.IsType<PvpRecordMessage>(frame),
            frame => Assert.Equal(1, Assert.IsType<PvpRoomStateMessage>(frame).State));
        // The lobby sees only the room waiting again.
        Assert.Equal(1, Assert.IsType<PvpRoomStateMessage>(Assert.Single(world.For(world.C))).State);
    });

    [Fact]
    public Task Closing_the_result_screen_shows_the_owed_seats_before_the_room_waiting_again() => Run(async world =>
    {
        var result = PvpBattleWorld.Without(PvpBattleWorld.RoomOf(PvpRoomPhase.AwaitResult, world.A, world.B), 1);
        world.Deferrals.Defer(world.A, result.Id);
        var waiting = PvpBattleWorld.RoomOf(PvpRoomPhase.Waiting, world.A);
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.ResultAcknowledged, result, waiting);

        Assert.True(await SendAsync(world, world.A, "close", []));

        Assert.Collection(world.For(world.A),
            frame => AssertAllSeats(frame, waiting),
            frame => Assert.Equal(1, Assert.IsType<PvpRoomStateMessage>(frame).State),
            frame => Assert.Equal(1, Assert.IsType<PvpRoomStateMessage>(frame).State));
        Assert.False(world.Deferrals.Owes(world.A, result.Id));
    });

    [Fact]
    public Task A_close_accepted_while_others_still_look_shows_the_owed_seats_alone() => Run(async world =>
    {
        var result = PvpBattleWorld.Without(PvpBattleWorld.RoomOf(PvpRoomPhase.AwaitResult, world.A, world.B, world.C), 2);
        world.Deferrals.Defer(world.A, result.Id);
        world.Deferrals.Defer(world.B, result.Id);
        world.Rooms.Next = PvpRooms.Changed(PvpRoomChangeKind.ResultAcknowledged, result, result);

        Assert.True(await SendAsync(world, world.A, "close", []));

        Assert.Collection(world.For(world.A), frame => AssertAllSeats(frame, result), frame => Assert.IsType<PvpRoomStateMessage>(frame));
        Assert.Empty(world.For(world.B));
        Assert.True(world.Deferrals.Owes(world.B, result.Id));
    });

    [Fact]
    public Task A_late_close_releases_nothing() => Run(async world =>
    {
        var waiting = PvpBattleWorld.RoomOf(PvpRoomPhase.Waiting, world.A);
        world.Deferrals.Defer(world.A, waiting.Id);
        world.Rooms.Next = new PvpRoomOutcome.Refused(PvpRoomRefusal.WrongPhase);

        Assert.False(await SendAsync(world, world.A, "close", []));

        Assert.Empty(world.Timeline.Frames);
        Assert.True(world.Deferrals.Owes(world.A, waiting.Id));
    });

    internal static void AssertResult(IReadOnlyList<IGameServerMessage> frames, byte winner, byte grade, long change, PvpRecordView record,
        int fighters = 2) =>
        Assert.Collection(frames,
            frame =>
            {
                var result = Assert.IsType<EndPvpMessage>(frame);
                Assert.Equal((winner, grade, change, false), (result.WinnerSeat, result.Grade, result.ExperienceChange, result.Draw));
                Assert.Equal(fighters, result.Fighters.Count);
                Assert.Equal(26 + 19 * fighters, PvpBattlePayloadCodec.Encode(result).Length);
            },
            frame =>
            {
                Assert.Equal(record, Assert.IsType<PvpRecordMessage>(frame).Record);
                Assert.Equal(51, PvpBattlePayloadCodec.Encode((PvpRecordMessage)frame).Length);
            });

    internal static void AssertAllSeats(IGameServerMessage frame, PvpRoomSnapshot room)
    {
        var seats = Assert.IsType<PvpSeatStateMessage>(frame);
        Assert.Equal(room.Number, seats.Number);
        Assert.Equal(Enumerable.Range(0, PvpRoomSnapshot.SeatCount), seats.Seats.Select(x => x.Seat));
        Assert.Equal(PvpRoomLobbyView.Of(room).Seats, seats.Seats.Select(x => x.State));
    }

    private static byte[] Death(ushort victim, ushort killer) =>
        [(byte)victim, (byte)(victim >> 8), (byte)killer, (byte)(killer >> 8), 0];

    private static byte[] WellFormed(string report, SelectedSessionIdentity sender) => report switch
    {
        "link" => [1, 0, 1],
        "death" => Death(sender.WireUserId, 0),
        "timeout" => new byte[32],
        "rank" => new byte[70],
        _ => [],
    };

    private static Task<bool> SendAsync(PvpBattleWorld world, SelectedSessionIdentity sender, string report, byte[] body)
    {
        var dispatch = PvpBattleWorld.Dispatch(sender);
        var handler = world.Battle();
        return report switch
        {
            "load" => world.SendAsync(handler, new GameCompleteLoadPvpCommand(dispatch, new(Meta, body.Length))),
            "link" => world.SendAsync(handler, new GameConnectP2pPvpCommand(dispatch, PvpBattlePayloadCodec.DecodeConnectP2p(Meta, body))),
            "heartbeat" => world.SendAsync(handler, new GamePvpHeartbeatCommand(dispatch, new(Meta, body.Length))),
            "death" => world.SendAsync(handler, new GameDiePvpCharacterCommand(dispatch, PvpBattlePayloadCodec.DecodeDeath(Meta, body))),
            "timeout" => world.SendAsync(handler, new GamePvpTimeOutCommand(dispatch, PvpBattlePayloadCodec.DecodeTimeOut(Meta, body))),
            "rank" => world.SendAsync(handler, new GamePvpRankResponseCommand(dispatch, new(Meta, body.Length))),
            "close" => world.SendAsync(handler, new GameEndPvpResultCommand(dispatch, new(Meta, body.Length))),
            _ => throw new ArgumentOutOfRangeException(nameof(report)),
        };
    }

    private static Task Run(Func<PvpBattleWorld, Task> body) => TestGameplay.RunAsync(() => body(new PvpBattleWorld()));
}
