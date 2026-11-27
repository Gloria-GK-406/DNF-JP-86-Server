using System.Collections.Immutable;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Pvp;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Pvp;

/// <summary>
/// Spec AC-030–AC-035 (domain part), AC-033, AC-041 (domain part), AC-042, AC-050 and AC-054 for the battle
/// phases, judgement, settlement, departures and shutdown of the duel room owner.
/// </summary>
public sealed class PvpBattleDomainTests
{
    private static readonly ChannelEntryIdentity Ranked = new(70, PvpRoomFixture.Port, ChannelEntryKind.TraditionalDuel, 24);

    [Fact]
    public async Task Changing_one_room_to_relay_does_not_change_the_other_rooms_battle()
    {
        var f = new PvpRoomFixture();
        var owner = f.Join(5);
        var guest = f.Join(6);
        var waiting = await f.CreateAsync(owner);
        await f.EnterAsync(guest, waiting);
        var (individual, fighters) = await f.FightAsync(2, PvpBattleMode.Individual);

        await f.Commit(rooms => rooms.SetModeAsync(owner, PvpBattleMode.Relay, default));
        await f.ReadyAsync(guest);
        await f.ReadyAsync(owner);
        Refused(await f.Commit(rooms => rooms.ReportDeathAsync(guest, owner.CharacterId, default)), PvpRoomRefusal.NotFighter);
        var individualDeath = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default)));
        Assert.Equal((byte)0, individualDeath.Concluded!.Terminal.Winner);
        Assert.Equal(PvpRoomPhase.AwaitRank, f.Committed(individual).Phase);
        Assert.Equal(PvpRoomPhase.Fighting, f.Committed(waiting).Phase);

        f.Clock.Now = f.Clock.Now.AddSeconds(7);
        Assert.NotNull(await f.Commit(rooms => rooms.AdvanceDueAsync(waiting.Id, f.Clock.Now, default)));
        var relayDeath = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(guest, owner.CharacterId, default)));
        Assert.Equal((byte)PvpTeam.First, relayDeath.Concluded!.Terminal.Winner);
    }

    [Fact]
    public async Task Relay_replaces_a_knocked_out_fighter_and_settles_only_after_the_team_is_exhausted()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var (room, fighters) = await f.FightAsync(4, PvpBattleMode.Relay);
        var match = room.Match!.Id;
        var firstTurnAt = f.Rooms.Deadlines(f.Clock.Now).Next!.Value;
        f.Clock.Now = firstTurnAt;
        Assert.NotNull(await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default)));

        var firstDeath = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default)));
        Assert.Null(firstDeath.Concluded);
        Assert.Equal(PvpRoomPhase.Fighting, firstDeath.After!.Phase);
        Assert.Empty(f.Rooms.PendingConclusions());

        Refused(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[3], fighters[0].CharacterId, default)), PvpRoomRefusal.NotFighter);
        f.Clock.Now = f.Rooms.Deadlines(f.Clock.Now).Next!.Value;
        Assert.NotNull(await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default)));

        var lastDeath = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[3], fighters[0].CharacterId, default)));
        Assert.Equal(match, lastDeath.Concluded!.MatchId);
        Assert.Equal(PvpRoomPhase.AwaitRank, lastDeath.After!.Phase);
        foreach (var fighter in fighters) await f.Commit(rooms => rooms.AcknowledgeRankAsync(fighter, default));
        var settled = Assert.IsType<PvpSettlementOutcome.Settled>(await f.SettleAsync(match));
        Assert.Equal([PvpOutcome.Win, PvpOutcome.Loss, PvpOutcome.Win, PvpOutcome.Loss], settled.Stored.Results.Select(result => result.Outcome));
        Assert.Equal(2, lastDeath.Concluded.Fighters[0].Kills);
        Assert.Equal(1, f.Records.Of(fighters[0]).Wins);
        Assert.IsType<PvpSettlementOutcome.NotPending>(await f.SettleAsync(match));
    }

    [Fact]
    public async Task Relay_prefers_a_live_reserve_who_requests_the_next_turn()
    {
        var f = new PvpRoomFixture();
        var (room, fighters) = await f.FightAsync(6, PvpBattleMode.Relay);
        f.Clock.Now = f.Rooms.Deadlines(f.Clock.Now).Next!.Value;
        await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default));

        await f.Commit(rooms => rooms.RequestRelayFightAsync(fighters[3], default));
        await f.Commit(rooms => rooms.RequestRelayFightAsync(fighters[3], default));
        await f.Commit(rooms => rooms.RequestRelayFightAsync(fighters[5], default));
        await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default));
        f.Clock.Now = f.Rooms.Deadlines(f.Clock.Now).Next!.Value;
        await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default));

        Refused(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[3], fighters[0].CharacterId, default)), PvpRoomRefusal.NotFighter);
        Assert.Equal(new PvpDeath(5, 0), Changed(await f.Commit(rooms =>
            rooms.ReportDeathAsync(fighters[5], fighters[0].CharacterId, default))).Death);
    }

    [Fact]
    public async Task Relay_second_turn_can_finish_after_the_first_turn_watchdog_would_have_expired()
    {
        var f = new PvpRoomFixture();
        var (room, fighters) = await f.FightAsync(4, PvpBattleMode.Relay);
        var start = f.Clock.Now;
        f.Clock.Now = f.Rooms.Deadlines(start).Next!.Value;
        await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default));

        f.Clock.Now = start.AddSeconds(100);
        foreach (var fighter in fighters) await f.Commit(rooms => rooms.HeartbeatAsync(fighter, default));
        await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default));
        f.Clock.Now = f.Rooms.Deadlines(f.Clock.Now).Next!.Value;
        await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default));
        var secondTurn = f.Clock.Now;

        f.Clock.Now = start.AddSeconds(157);
        foreach (var fighter in fighters) await f.Commit(rooms => rooms.HeartbeatAsync(fighter, default));
        Assert.Null(await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default)));
        Assert.Equal(PvpRoomPhase.Fighting, f.Committed(room).Phase);

        f.Clock.Now = secondTurn.AddMinutes(2);
        foreach (var fighter in fighters) await f.Commit(rooms => rooms.HeartbeatAsync(fighter, default));
        Refused(await f.Commit(rooms => rooms.ReportTimeoutAsync(fighters[0], [.. new byte[32]], default)), PvpRoomRefusal.UnsupportedMode);
        var ended = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[3], null, default)));
        Assert.Equal(PvpMatchOutcomeKind.Decided, ended.Concluded!.Terminal.Kind);
        Assert.Equal((byte)PvpTeam.First, ended.Concluded.Terminal.Winner);
    }

    [Fact]
    public async Task Relay_waits_for_the_a21_first_round_to_finish_before_using_the_watchdog()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var (room, fighters) = await f.FightAsync(4, PvpBattleMode.Relay);
        f.Clock.Now = f.Rooms.Deadlines(f.Clock.Now).Next!.Value;
        await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default));
        var firstTurn = f.Clock.Now;

        f.Clock.Now = firstTurn.AddSeconds(150);
        foreach (var fighter in fighters) await f.Commit(rooms => rooms.HeartbeatAsync(fighter, default));
        Assert.Null(await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default)));

        f.Clock.Now = firstTurn.AddMilliseconds(184640);
        foreach (var fighter in fighters) await f.Commit(rooms => rooms.HeartbeatAsync(fighter, default));
        Assert.Equal(PvpRoomPhase.Fighting, f.Committed(room).Phase);
        var timedOut = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], null, default)));
        Assert.Null(timedOut.Concluded);
        f.Clock.Now = f.Rooms.Deadlines(f.Clock.Now).Next!.Value;
        var changedTurn = await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default));
        Assert.NotNull(changedTurn);
        var eliminated = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[3], null, default)));
        Assert.Equal(PvpMatchOutcomeKind.Decided, eliminated.Concluded!.Terminal.Kind);
        Assert.Equal((byte)PvpTeam.First, eliminated.Concluded.Terminal.Winner);
    }

    // ---- AC-030: the phases, and reports outside them

    /// <summary>
    /// Spec SA-1: an A21 client in an ordinary room reports neither loading nor linking, so the fight is on from the
    /// start, and the first death ends it and leads into settlement with nothing else reported.
    /// </summary>
    [Fact]
    public async Task A_death_right_after_the_start_ends_the_fight_and_settles_it_without_any_load_or_link_report()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var start = f.Clock.Now;
        var (room, fighters) = await f.FightAsync();
        Assert.Equal(start + PvpRoomFixture.BattleTime + TimeSpan.FromSeconds(30), room.Match!.PhaseDeadline);
        Assert.Equal(start + TimeSpan.FromSeconds(30), f.Rooms.Deadlines(start).Next);
        f.Clock.Now += TimeSpan.FromSeconds(20);

        var died = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default)));

        Assert.Equal(PvpRoomChangeKind.Died, died.Kind);
        var ended = f.Committed(room);
        Assert.Equal(PvpRoomPhase.AwaitRank, ended.Phase);
        Assert.Equal(new PvpMatchTerminal(PvpMatchOutcomeKind.Decided, 0, f.Clock.Now), ended.Match!.Terminal);
        Assert.Equal(ended.Match.Terminal, died.Concluded!.Terminal);
        foreach (var fighter in fighters) await f.Commit(rooms => rooms.AcknowledgeRankAsync(fighter, default));
        var settled = Assert.IsType<PvpSettlementOutcome.Settled>(await f.SettleAsync(ended.Match.Id));
        Assert.Equal([PvpOutcome.Win, PvpOutcome.Loss], settled.Stored.Results.Select(x => x.Outcome));
        Assert.Equal(PvpRoomPhase.AwaitResult, f.Committed(room).Phase);
        Assert.Equal((1, 0), (f.Records.Of(fighters[0]).Wins, f.Records.Of(fighters[0]).Losses));
        Assert.Equal((0, 1), (f.Records.Of(fighters[1]).Wins, f.Records.Of(fighters[1]).Losses));
    }

    [Theory]
    [InlineData(PvpRoomPhase.Waiting)]
    [InlineData(PvpRoomPhase.Fighting)]
    [InlineData(PvpRoomPhase.AwaitRank)]
    [InlineData(PvpRoomPhase.AwaitResult)]
    public async Task A_report_that_does_not_belong_to_the_phase_changes_nothing(PvpRoomPhase phase)
    {
        var f = new PvpRoomFixture();
        var (room, fighters) = await ReachAsync(f, phase);
        var reporter = fighters[^1];
        var reports = new (PvpRoomPhase Phase, Func<PvpRoomDomainService, ValueTask<PvpRoomOutcome>> Call)[]
        {
            (PvpRoomPhase.Fighting, rooms => rooms.HeartbeatAsync(reporter, default)),
            (PvpRoomPhase.Fighting, rooms => rooms.ReportDeathAsync(reporter, null, default)),
            (PvpRoomPhase.Fighting, rooms => rooms.ReportTimeoutAsync(reporter, [], default)),
            (PvpRoomPhase.AwaitRank, rooms => rooms.AcknowledgeRankAsync(reporter, default)),
            (PvpRoomPhase.AwaitResult, rooms => rooms.AcknowledgeResultAsync(reporter, default)),
        };
        var before = f.Committed(room);
        var wakes = f.Wake.Count;

        foreach (var report in reports.Where(report => report.Phase != phase))
            Refused(await f.Commit(report.Call), PvpRoomRefusal.WrongPhase);

        Assert.Equal(before, f.Committed(room));
        Assert.Equal(wakes, f.Wake.Count);
    }

    // ---- AC-031: deaths and the end of a fight

    [Fact]
    public async Task A_death_reported_for_someone_else_or_twice_is_ignored()
    {
        var f = new PvpRoomFixture();
        var (room, fighters) = await f.FightAsync(3);
        var outsider = f.Join(9);

        Refused(await f.Commit(rooms => rooms.ReportDeathAsync(outsider, null, default)), PvpRoomRefusal.NotFighter);
        Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[0], fighters[1].CharacterId, default)));
        var once = f.Committed(room);
        Refused(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[0], fighters[1].CharacterId, default)), PvpRoomRefusal.NotAlive);

        Assert.Equal(once, f.Committed(room));
        Assert.Equal(1, once.Match!.Fighters[0].Deaths);
        Assert.Equal(1, once.Match.Fighters[1].Kills);
        Assert.Equal(PvpRoomPhase.Fighting, once.Phase);
    }

    [Fact]
    public async Task Only_a_live_enemy_of_the_battle_earns_the_kill()
    {
        var f = new PvpRoomFixture();
        var (room, fighters) = await f.FightAsync(4, PvpBattleMode.Team);
        var teams = f.Committed(room).Match!.Fighters.Select(x => x.Team).ToArray();
        Assert.Equal([PvpTeam.First, PvpTeam.Second, PvpTeam.First, PvpTeam.Second], teams);

        // A teammate, nobody of this battle, and a dead fighter earn nothing.
        var byTeammate = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[0], fighters[2].CharacterId, default)));
        Assert.Equal(new PvpDeath(0, null), byTeammate.Death);
        var byStranger = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], new CharacterId(99), default)));
        Assert.Equal(new PvpDeath(1, null), byStranger.Death);
        Assert.All(f.Committed(room).Match!.Fighters, fighter => Assert.Equal(0, fighter.Kills));

        var byEnemy = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[2], fighters[3].CharacterId, default)));

        Assert.Equal(new PvpDeath(2, 3), byEnemy.Death);
        var ended = f.Committed(room);
        Assert.Equal(1, ended.Match!.Fighters[3].Kills);
        // Team First has nobody left: team Second wins.
        Assert.Equal(PvpRoomPhase.AwaitRank, ended.Phase);
        Assert.Equal(new PvpMatchTerminal(PvpMatchOutcomeKind.Decided, (byte)PvpTeam.Second, f.Clock.Now), ended.Match.Terminal);
        Assert.Equal(ended.Match.Terminal, byEnemy.Concluded!.Terminal);
    }

    [Fact]
    public async Task An_individual_battle_ends_with_the_last_fighter_standing()
    {
        var f = new PvpRoomFixture();
        var (room, fighters) = await f.FightAsync(3);

        await f.Commit(rooms => rooms.ReportDeathAsync(fighters[0], fighters[1].CharacterId, default));
        Assert.Equal(PvpRoomPhase.Fighting, f.Committed(room).Phase);
        var wakes = f.Wake.Count;
        var last = Changed(await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[2].CharacterId, default)));

        var ended = f.Committed(room);
        Assert.Equal(PvpRoomPhase.AwaitRank, ended.Phase);
        Assert.Equal(new PvpMatchTerminal(PvpMatchOutcomeKind.Decided, 2, f.Clock.Now), ended.Match!.Terminal);
        Assert.Equal(f.Clock.Now + TimeSpan.FromSeconds(10), ended.Match.PhaseDeadline);
        var conclusion = Assert.Single(f.Rooms.PendingConclusions());
        Assert.Equal(last.Concluded, conclusion);
        Assert.Equal(f.Clock.Now + TimeSpan.FromSeconds(10), conclusion.SettleBy);
        Assert.Null(last.SettlementDue);
        Assert.Equal(wakes + 1, f.Wake.Count);
    }

    [Fact]
    public async Task Fighters_who_all_fall_silent_end_the_fight_in_a_draw_nobody_waits_on()
    {
        var f = new PvpRoomFixture();
        var (room, fighters) = await f.FightAsync();
        await f.Commit(rooms => rooms.HeartbeatAsync(fighters[0], default));
        f.Clock.Now += TimeSpan.FromSeconds(30);

        var advanced = Changed(await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default)));

        var ended = f.Committed(room);
        Assert.Equal(PvpRoomPhase.AwaitRank, ended.Phase);
        Assert.Equal(PvpMatchOutcomeKind.Draw, ended.Match!.Terminal!.Kind);
        Assert.All(ended.Match.Fighters, fighter => Assert.True(fighter is { Alive: false, Fled: true, Departed: true }));
        // Silent fighters keep their seats: their sessions are still in the world.
        Assert.Equal(2, ended.MemberCount);
        Assert.Equal(ended.Match.Id, advanced.SettlementDue);
        Assert.Equal(f.Clock.Now, Assert.Single(f.Rooms.PendingConclusions()).SettleBy);
    }

    [Fact]
    public async Task A_fight_past_its_overrun_is_drawn()
    {
        var f = new PvpRoomFixture();
        var (room, fighters) = await f.FightAsync();
        for (var elapsed = TimeSpan.Zero; elapsed < PvpRoomFixture.BattleTime + TimeSpan.FromSeconds(30); elapsed += TimeSpan.FromSeconds(20))
        {
            f.Clock.Now += TimeSpan.FromSeconds(20);
            foreach (var fighter in fighters) await f.Commit(rooms => rooms.HeartbeatAsync(fighter, default));
        }

        Changed(await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default)));

        var ended = f.Committed(room);
        Assert.Equal(PvpMatchOutcomeKind.Draw, ended.Match!.Terminal!.Kind);
        Assert.All(ended.Match.Fighters, fighter => Assert.False(fighter.Fled));
    }

    // ---- AC-032: time-outs

    [Fact]
    public async Task Only_the_first_time_out_near_the_limit_from_a_fighter_who_stayed_counts_and_it_draws()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var (room, fighters) = await f.FightAsync(3);
        var outsider = f.Join(9);
        var start = f.Clock.Now;
        await FallSilentAsync(f, room, fighters[2], fighters[..2]);
        Assert.True(f.Committed(room).Match!.Fighters.Single(x => x.CharacterId == fighters[2].CharacterId).Fled);

        f.Clock.Now = start + PvpRoomFixture.BattleTime - TimeSpan.FromSeconds(5) - TimeSpan.FromMilliseconds(1);
        Refused(await f.Commit(rooms => rooms.ReportTimeoutAsync(fighters[0], [], default)), PvpRoomRefusal.TooEarly);
        f.Clock.Now += TimeSpan.FromMilliseconds(1);
        Refused(await f.Commit(rooms => rooms.ReportTimeoutAsync(outsider, [], default)), PvpRoomRefusal.NotFighter);
        Refused(await f.Commit(rooms => rooms.ReportTimeoutAsync(fighters[2], [], default)), PvpRoomRefusal.NotFighter);
        Assert.Equal(PvpRoomPhase.Fighting, f.Committed(room).Phase);

        var accepted = Changed(await f.Commit(rooms => rooms.ReportTimeoutAsync(fighters[1], [1, 2, 3], default)));
        Refused(await f.Commit(rooms => rooms.ReportTimeoutAsync(fighters[0], [], default)), PvpRoomRefusal.WrongPhase);

        Assert.Equal(PvpRoomChangeKind.TimedOut, accepted.Kind);
        Assert.Equal(PvpMatchOutcomeKind.Draw, f.Committed(room).Match!.Terminal!.Kind);
        await f.SettleAsync(accepted.Concluded!.MatchId);
        foreach (var fighter in fighters[..2])
        {
            var record = f.Records.Of(fighter);
            Assert.Equal((1, 0L), (record.Draws, record.Experience ?? 0));
        }
        Assert.Equal(1, f.Records.Of(fighters[2]).Losses);
    }

    // ---- AC-033, RQ-040: departures

    [Fact]
    public async Task Leaving_a_fight_is_a_fled_loss_and_can_end_it()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        // Spec SA-1: right after the start too; no phase before the fight cancels the battle instead.
        var (room, fighters) = await f.FightAsync();

        var departed = Changed(await f.Commit(rooms => rooms.DepartAsync(fighters[1], PvpDepartureReason.Disconnected, default)));

        Assert.Equal(new PvpSeatRelease(1, fighters[1], PvpSeatReleaseReason.Departed), Assert.Single(departed.Released));
        Assert.Empty(f.Town.ReturnRequests);
        var ended = f.Committed(room);
        Assert.Equal(1, ended.MemberCount);
        Assert.Equal(new PvpMatchTerminal(PvpMatchOutcomeKind.Decided, 0, f.Clock.Now), ended.Match!.Terminal);
        Assert.True(ended.Match.Fighters[1] is { Fled: true, Alive: false, Deaths: 1 });
        Assert.Null(departed.SettlementDue);

        var acknowledged = Changed(await f.Commit(rooms => rooms.AcknowledgeRankAsync(fighters[0], default)));
        Assert.Equal(ended.Match.Id, acknowledged.SettlementDue);
        var settled = Assert.IsType<PvpSettlementOutcome.Settled>(await f.SettleAsync(ended.Match.Id));

        Assert.Equal([PvpOutcome.Win, PvpOutcome.Loss], settled.Stored.Results.Select(x => x.Outcome));
        Assert.True(settled.Stored.Results[1].Fled);
        Assert.Equal(1, f.Records.Of(fighters[1]).Losses);
        Assert.Equal(PvpRoomPhase.AwaitResult, f.Committed(room).Phase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task The_winner_leaving_after_the_end_changes_no_result(int when)
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var (room, fighters) = await f.FightAsync();
        var (winner, loser) = (fighters[0], fighters[1]);
        await f.Commit(rooms => rooms.ReportDeathAsync(loser, winner.CharacterId, default));
        var match = f.Committed(room).Match!.Id;

        switch (when)
        {
            case 0:
                // On the ranking screen: the battle is settled when the ranking times out.
                await f.Commit(rooms => rooms.DepartAsync(winner, PvpDepartureReason.Disconnected, default));
                f.Clock.Now += TimeSpan.FromSeconds(10);
                Assert.Equal([match], f.Rooms.Deadlines(f.Clock.Now).DueSettlements);
                await f.SettleAsync(match);
                break;
            case 1:
                // Right after settlement wrote the results: the room is on its result screen from that commit.
                await f.SettleAsync(match);
                Assert.Equal(PvpRoomPhase.AwaitResult, f.Committed(room).Phase);
                await f.Commit(rooms => rooms.DepartAsync(winner, PvpDepartureReason.Disconnected, default));
                break;
            default:
                // On the result screen, after the other fighter closed it: the room waits again.
                await f.SettleAsync(match);
                await f.Commit(rooms => rooms.AcknowledgeResultAsync(loser, default));
                Assert.Equal(PvpRoomPhase.AwaitResult, f.Committed(room).Phase);
                await f.Commit(rooms => rooms.DepartAsync(winner, PvpDepartureReason.Disconnected, default));
                Assert.Equal(PvpRoomPhase.Waiting, f.Committed(room).Phase);
                break;
        }

        Assert.Equal((1, 0, 1), (f.Records.Of(winner).Wins, f.Records.Of(winner).Losses, f.Records.Of(winner).WinStreak));
        Assert.Equal(PvpRoomFixture.Grades.Grades[1].LowerBound, f.Records.Of(winner).Experience);
        Assert.Equal((0, 1), (f.Records.Of(loser).Wins, f.Records.Of(loser).Losses));
        Assert.Single(f.Records.Matches);
    }

    // ---- AC-034, AC-035: acknowledgement, result screen and back to waiting

    [Fact]
    public async Task Everyone_closing_the_result_screen_lets_the_room_start_again_and_a_late_close_is_ignored()
    {
        var f = new PvpRoomFixture();
        var (room, fighters) = await ReachAsync(f, PvpRoomPhase.AwaitResult);
        var settled = f.Committed(room);
        Assert.Equal(2, settled.Match!.Results.Length);
        Assert.Equal(f.Clock.Now + TimeSpan.FromSeconds(10), settled.Match.PhaseDeadline);

        await f.Commit(rooms => rooms.AcknowledgeResultAsync(fighters[0], default));
        Assert.Equal(PvpRoomPhase.AwaitResult, f.Committed(room).Phase);
        await f.Commit(rooms => rooms.AcknowledgeResultAsync(fighters[1], default));

        var waiting = f.Committed(room);
        Assert.Equal(PvpRoomPhase.Waiting, waiting.Phase);
        Assert.Null(waiting.Match);
        Assert.All(waiting.Members, member => Assert.False(member.Member.Ready));
        Refused(await f.Commit(rooms => rooms.AcknowledgeResultAsync(fighters[0], default)), PvpRoomRefusal.WrongPhase);
        Assert.Equal(waiting, f.Committed(room));

        await f.ReadyAsync(fighters[1]);
        var again = Changed(await f.ReadyAsync(fighters[0]));
        Assert.Equal(PvpRoomChangeKind.MatchStarted, again.Kind);
        Assert.NotEqual(settled.Match.Id, again.After!.Match!.Id);
    }

    [Fact]
    public async Task Unanswered_ranking_and_result_screens_settle_once_and_wait_again()
    {
        var f = new PvpRoomFixture();
        var (room, _) = await ReachAsync(f, PvpRoomPhase.AwaitRank);
        var match = f.Committed(room).Match!.Id;
        f.Clock.Now += TimeSpan.FromSeconds(10);
        var scan = f.Rooms.Deadlines(f.Clock.Now);
        Assert.Equal([match], scan.DueSettlements);
        // A ranking screen is due through its battle's conclusion, never as a room of its own.
        Assert.Empty(scan.DueRooms);

        Assert.IsType<PvpSettlementOutcome.Settled>(await f.SettleAsync(match));
        Assert.IsType<PvpSettlementOutcome.NotPending>(await f.SettleAsync(match));
        f.Clock.Now += TimeSpan.FromSeconds(10);
        Changed(await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default)));

        Assert.Equal(PvpRoomPhase.Waiting, f.Committed(room).Phase);
        Assert.Single(f.Records.Matches);
        Assert.Single(f.Events.Published);
    }

    [Fact]
    public async Task Leaving_the_result_screen_frees_the_seat_and_stops_the_wait_for_that_fighter()
    {
        var f = new PvpRoomFixture();
        var (room, fighters) = await ReachAsync(f, PvpRoomPhase.AwaitResult);
        await f.Commit(rooms => rooms.AcknowledgeResultAsync(fighters[0], default));

        var departed = Changed(await f.Commit(rooms => rooms.DepartAsync(fighters[1], PvpDepartureReason.Disconnected, default)));

        Assert.Single(departed.Released);
        var waiting = f.Committed(room);
        Assert.Equal(PvpRoomPhase.Waiting, waiting.Phase);
        Assert.Equal(1, waiting.MemberCount);
    }

    // ---- AC-054, RQ-071: once only, and nothing without the write

    [Fact]
    public async Task A_battle_is_settled_once_and_its_event_published_once()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var (room, _) = await ReachAsync(f, PvpRoomPhase.AwaitRank);
        var match = f.Committed(room).Match!.Id;

        var first = Assert.IsType<PvpSettlementOutcome.Settled>(await f.SettleAsync(match));
        var again = await f.SettleAsync(match);

        Assert.IsType<PvpSettleOutcome.Created>(first.Stored);
        Assert.IsType<PvpSettlementOutcome.NotPending>(again);
        var published = Assert.IsType<PvpMatchSettled>(Assert.Single(f.Events.Published));
        Assert.Equal(match.Value, published.MatchId);
        Assert.True(published.RecordsResult);
        // The event store keeps the payload as JSON; it must read back as written.
        var stored = System.Text.Json.JsonSerializer.Deserialize<PvpMatchSettled>(System.Text.Json.JsonSerializer.Serialize(published))!;
        Assert.Equal(published with { Fighters = [] }, stored with { Fighters = [] });
        Assert.Equal(published.Fighters, stored.Fighters);
        Assert.Equal(PvpRoomPhase.AwaitResult, first.Room!.After!.Phase);
    }

    [Fact]
    public async Task A_battle_the_store_already_holds_is_read_back_without_a_second_event()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var (room, _) = await ReachAsync(f, PvpRoomPhase.AwaitRank);
        var conclusion = Assert.Single(f.Rooms.PendingConclusions());
        // As after a restart that lost the room but kept the database: the battle was written before.
        var earlier = await f.Run(async () => await f.Records.SettleAsync(new PvpMatchSettlementRecord(
            conclusion.MatchId.Value, conclusion.RoomNumber, PvpRoomFixture.Port, 24, conclusion.Mode,
            PvpMatchOutcomeKind.Decided, conclusion.Terminal.Winner, true, new DateOnly(2026, 11, 14), f.Clock.Now, []), default));
        var recordsBefore = f.Records.Records.ToDictionary();

        var settled = Assert.IsType<PvpSettlementOutcome.Settled>(await f.SettleAsync(conclusion.MatchId));

        Assert.IsType<PvpSettleOutcome.AlreadySettled>(settled.Stored);
        Assert.Equal(earlier.Results, settled.Stored.Results);
        Assert.Empty(f.Events.Published);
        Assert.Equal(recordsBefore, f.Records.Records);
        Assert.Equal(PvpRoomPhase.AwaitResult, f.Committed(room).Phase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_failed_write_leaves_the_battle_waiting_with_no_event_and_no_wake(bool fails)
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var (room, _) = await ReachAsync(f, PvpRoomPhase.AwaitRank);
        var match = f.Committed(room).Match!.Id;
        var before = f.Committed(room);
        var wakes = f.Wake.Count;

        var settle = f.Run(() => f.Settlement.SettleAsync(match, default), fails ? new PvpRoomFixture.FailingWrite() : null);

        if (fails)
        {
            await Assert.ThrowsAnyAsync<Exception>(() => settle);
            Assert.Equal(before, f.Committed(room));
            Assert.Single(f.Rooms.PendingConclusions());
            Assert.Empty(f.Events.Published);
            Assert.Empty(f.Records.Matches);
            Assert.Equal(wakes, f.Wake.Count);
        }
        else
        {
            // The same settlement without the failure moves everything, so the assertions above can fail.
            await settle;
            Assert.Equal(PvpRoomPhase.AwaitResult, f.Committed(room).Phase);
            Assert.Empty(f.Rooms.PendingConclusions());
            Assert.Single(f.Events.Published);
            Assert.Single(f.Records.Matches);
            Assert.Equal(wakes + 1, f.Wake.Count);
        }
    }

    // ---- AC-050, AC-051 (settlement side)

    [Theory]
    [InlineData(ChannelEntryKind.FreeDuel, (byte)13, false)]
    [InlineData(ChannelEntryKind.MatchmakingDuel, (byte)8, false)]
    [InlineData(ChannelEntryKind.TraditionalDuel, (byte)24, true)]
    public async Task Only_the_ranked_channel_changes_records(ChannelEntryKind kind, byte environment, bool records)
    {
        var f = new PvpRoomFixture { DuelChannel = new ChannelEntryIdentity(70, PvpRoomFixture.Port, kind, environment) };
        var (room, fighters) = await ReachAsync(f, PvpRoomPhase.AwaitRank);

        var settled = Assert.IsType<PvpSettlementOutcome.Settled>(await f.SettleAsync(f.Committed(room).Match!.Id));

        Assert.Equal(records, Assert.Single(f.Records.Matches).Value.Settlement.RecordsResult);
        Assert.Equal(records, f.Records.Records.Count > 0);
        if (!records)
            Assert.All(settled.Stored.Results, result => Assert.Equal((0L, false), (result.ExperienceChange, result.CountsTowardDaily)));
        Assert.Equal(records ? 1 : 0, f.Records.Of(fighters[0]).Wins);
    }

    [Fact]
    public async Task A_record_without_experience_starts_from_its_grade_lower_bound()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var (room, fighters) = await f.FightAsync();
        f.Records.Records[fighters[0].CharacterId] = new PvpRecord(1, 0, null, 0, 0, 0, 0, 0);
        await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default));

        await f.SettleAsync(f.Committed(room).Match!.Id);

        var lower = PvpRoomFixture.Grades.Grades[1];
        Assert.Equal(lower.LowerBound + lower.WinExperience, f.Records.Of(fighters[0]).Experience);
    }

    [Fact]
    public async Task A_battle_counts_toward_the_game_day_it_started_on()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        // 05:59 at the business offset, a minute before the 06:00 reset: still the previous game day.
        f.Clock.Now = new DateTimeOffset(2026, 11, 14, 21, 59, 0, TimeSpan.Zero);
        var (room, fighters) = await f.FightAsync();
        f.Clock.Now += TimeSpan.FromMinutes(2);
        await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default));

        await f.SettleAsync(f.Committed(room).Match!.Id);

        Assert.Equal(new DateOnly(2026, 11, 14), Assert.Single(f.Records.Matches).Value.Settlement.GameDay);
    }

    // ---- AC-042: shutdown

    [Fact]
    public async Task Shutdown_cancels_what_runs_settles_what_ended_and_starts_nothing_new()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var (fighting, fighters) = await f.FightAsync();
        await FallSilentAsync(f, fighting, fighters[1], fighters[..1]);
        Assert.Equal(PvpRoomPhase.AwaitRank, f.Committed(fighting).Phase);
        var (running, runners) = await f.FightAsync(first: 11);
        var (owner, guest) = (f.Join(21), f.Join(22));
        var idle = await f.CreateAsync(owner);
        await f.EnterAsync(guest, idle);
        await f.ReadyAsync(guest);

        var cancelled = await f.Commit(rooms => rooms.CancelForShutdownAsync(default));
        foreach (var conclusion in f.Rooms.PendingConclusions())
            Assert.IsType<PvpSettlementOutcome.Settled>(await f.SettleAsync(conclusion.MatchId));

        Assert.Equal(PvpRoomChangeKind.CancelledForShutdown, Assert.Single(cancelled).Kind);
        Refused(await f.ReadyAsync(owner), PvpRoomRefusal.ShuttingDown);
        Assert.Equal(PvpRoomPhase.Waiting, f.Committed(idle).Phase);
        Assert.Equal(PvpRoomPhase.Waiting, f.Committed(running).Phase);
        Assert.Equal(PvpRoomPhase.AwaitResult, f.Committed(fighting).Phase);
        Assert.Equal(1, f.Records.Of(fighters[1]).Losses);
        // The disconnects that follow find a waiting room: nobody of the cancelled battle loses anything.
        foreach (var runner in runners) await f.Commit(rooms => rooms.DepartAsync(runner, PvpDepartureReason.Disconnected, default));
        Assert.All(runners, runner => Assert.Equal(0, f.Records.Of(runner).Losses));
        Assert.Single(f.Records.Matches);
    }

    // ---- AC-041 (domain part): cleanup

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_cleanup_departure_reaches_its_sinks_only_when_the_transaction_commits(bool commits)
    {
        var scope = TestRequired.TransactionScope();
        var f = new PvpRoomFixture(scope) { DuelChannel = Ranked };
        var (room, fighters) = await f.FightAsync();
        var sink = new RecordingSink();
        var cleanup = Cleanup(f, scope, sink);

        Assert.Equal(new HashSet<SelectedSessionIdentity>(fighters), cleanup.AffectedSessions(fighters[1]));
        await TestGameplay.RunAsync(async () =>
        {
            await using var lease = scope.Begin();
            await TestEffectFootprints.Run(fighters[1], async footprint =>
                await cleanup.CleanupAsync(fighters[1], includeOwner: false, footprint, CancellationToken.None));
            Assert.Empty(sink.Received);
            if (commits) await lease.CommitAsync(default);
            return true;
        });

        if (commits)
        {
            var (departure, settled) = Assert.Single(sink.Received);
            Assert.Equal(PvpRoomChangeKind.Departed, departure.Kind);
            // Inside a caller's transaction only the departure is made; settlement is the scheduler's.
            Assert.Null(settled);
            Assert.Equal(PvpRoomPhase.AwaitRank, f.Committed(room).Phase);
        }
        else
        {
            Assert.Empty(sink.Received);
            Assert.Equal(PvpRoomPhase.Fighting, f.Committed(room).Phase);
        }
    }

    [Fact]
    public async Task A_cleanup_that_leaves_nobody_to_acknowledge_settles_in_its_own_transaction()
    {
        var scope = TestRequired.TransactionScope();
        var f = new PvpRoomFixture(scope) { DuelChannel = Ranked };
        var (room, fighters) = await f.FightAsync();
        await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default));
        await f.Commit(rooms => rooms.AcknowledgeRankAsync(fighters[1], default));
        var sink = new RecordingSink();
        var cleanup = Cleanup(f, scope, sink);

        // A disconnect opens no transaction of its own: the cleanup leaves, then settles, each step alone.
        await TestGameplay.RunAsync(async () =>
            await TestEffectFootprints.Run(fighters[0], async footprint =>
                await cleanup.CleanupAsync(fighters[0], includeOwner: false, footprint, CancellationToken.None)));

        Assert.Empty(f.Rooms.PendingConclusions());
        Assert.Equal(1, f.Records.Of(fighters[0]).Wins);
        Assert.Equal(PvpRoomPhase.AwaitResult, f.Committed(room).Phase);
        Assert.Single(f.Events.Published);
        // The members left behind are told their results through the sink.
        var (departure, settled) = Assert.Single(sink.Received);
        Assert.Equal(PvpRoomChangeKind.Departed, departure.Kind);
        var written = Assert.IsType<PvpSettlementOutcome.Settled>(settled);
        Assert.Equal(PvpRoomPhase.AwaitResult, written.Room!.After!.Phase);
        Assert.Equal(2, written.Stored.Results.Count);
    }

    [Fact]
    public async Task A_settlement_that_fails_in_cleanup_keeps_the_departure_and_leaves_the_battle_to_the_scheduler()
    {
        var scope = TestRequired.TransactionScope();
        var f = new PvpRoomFixture(scope) { DuelChannel = Ranked };
        var (room, fighters) = await f.FightAsync();
        await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default));
        await f.Commit(rooms => rooms.AcknowledgeRankAsync(fighters[1], default));
        var sink = new RecordingSink();
        var cleanup = Cleanup(f, scope, sink);
        f.Records.Fails = true;

        await TestGameplay.RunAsync(async () =>
            await TestEffectFootprints.Run(fighters[0], async footprint =>
                await cleanup.CleanupAsync(fighters[0], includeOwner: false, footprint, CancellationToken.None)));

        // The seat is gone with the session; the battle still waits, due at once.
        var after = f.Committed(room);
        Assert.Null(after.SeatOf(fighters[0].CharacterId));
        Assert.Equal(PvpRoomPhase.AwaitRank, after.Phase);
        Assert.Equal([after.Match!.Id], f.Rooms.Deadlines(f.Clock.Now).DueSettlements);
        var (departure, settled) = Assert.Single(sink.Received);
        Assert.Null(settled);
        Assert.Equal(PvpRoomChangeKind.Departed, departure.Kind);
        Assert.Empty(f.Records.Matches);
        Assert.Empty(f.Events.Published);

        f.Records.Fails = false;
        Assert.IsType<PvpSettlementOutcome.Settled>(await f.SettleAsync(after.Match.Id));
        Assert.Equal(1, f.Records.Of(fighters[0]).Wins);
    }

    [Fact]
    public async Task A_settlement_that_keeps_failing_is_retried_with_backoff_and_then_dropped()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var (room, _) = await ReachAsync(f, PvpRoomPhase.AwaitRank);
        var conclusion = Assert.Single(f.Rooms.PendingConclusions());
        f.Records.Fails = true;

        f.Clock.Now = conclusion.SettleBy;
        await Assert.ThrowsAnyAsync<Exception>(() => f.SettleAsync(conclusion.MatchId));
        Assert.Equal(f.Clock.Now + TimeSpan.FromSeconds(1), f.Rooms.Deadlines(f.Clock.Now).Next);
        f.Clock.Now = conclusion.SettleBy + TimeSpan.FromSeconds(40);
        Assert.Equal(f.Clock.Now + TimeSpan.FromSeconds(10), f.Rooms.Deadlines(f.Clock.Now).Next);
        f.Clock.Now = conclusion.SettleBy + TimeSpan.FromMinutes(4);
        var late = f.Rooms.Deadlines(f.Clock.Now);
        Assert.Equal(f.Clock.Now + TimeSpan.FromSeconds(30), late.Next);
        Assert.Equal([conclusion.MatchId], late.DueSettlements);
        Assert.Empty(late.Abandoned);

        f.Clock.Now = conclusion.SettleBy + TimeSpan.FromMinutes(5);
        var given = f.Rooms.Deadlines(f.Clock.Now);
        Assert.Empty(given.DueSettlements);
        Assert.Equal([conclusion.MatchId], given.Abandoned);
        var dropped = Changed(await f.Commit(rooms => rooms.AbandonSettlementAsync(conclusion.MatchId, default)));

        Assert.Equal(PvpRoomChangeKind.SettlementAbandoned, dropped.Kind);
        Assert.Equal(PvpRoomPhase.Waiting, f.Committed(room).Phase);
        Assert.Empty(f.Rooms.PendingConclusions());
        Assert.Null(f.Rooms.Deadlines(f.Clock.Now).Next);
        Assert.Empty(f.Records.Matches);
        Assert.Empty(f.Events.Published);
        Assert.Null(await f.Commit(rooms => rooms.AbandonSettlementAsync(conclusion.MatchId, default)));
    }

    [Fact]
    public async Task A_departure_whose_settlement_outcome_is_unknown_still_reaches_every_sink_and_the_failure_is_kept()
    {
        var scope = TestRequired.TransactionScope();
        var f = new PvpRoomFixture(scope) { DuelChannel = Ranked };
        var (_, fighters) = await f.FightAsync();
        await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default));
        await f.Commit(rooms => rooms.AcknowledgeRankAsync(fighters[1], default));
        var failing = new FailingSink();
        var sink = new RecordingSink();
        var cleanup = Cleanup(f, scope, [failing, sink], new UncertainSettlement(f.Settlement));

        var failure = await Assert.ThrowsAsync<SelectedSessionCleanupFailure>(() => TestGameplay.RunAsync(async () =>
            await TestEffectFootprints.Run(fighters[0], async footprint =>
                await cleanup.CleanupAsync(fighters[0], includeOwner: false, footprint, CancellationToken.None))));

        // The departure committed: it reaches the sink after the failing one, as unsettled.
        var (departure, settled) = Assert.Single(sink.Received);
        Assert.Equal(PvpRoomChangeKind.Departed, departure.Kind);
        Assert.Null(settled);
        Assert.Equal(1, failing.Calls);
        Assert.Contains(failure.InnerExceptions, error => Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(error));
    }

    [Fact]
    public async Task A_failing_sink_keeps_no_other_sink_from_the_departure()
    {
        var scope = TestRequired.TransactionScope();
        var f = new PvpRoomFixture(scope) { DuelChannel = Ranked };
        var (_, fighters) = await f.FightAsync();
        var failing = new FailingSink();
        var sink = new RecordingSink();
        var cleanup = Cleanup(f, scope, [failing, sink]);

        var failure = await Assert.ThrowsAsync<SelectedSessionCleanupFailure>(() => TestGameplay.RunAsync(async () =>
            await TestEffectFootprints.Run(fighters[1], async footprint =>
                await cleanup.CleanupAsync(fighters[1], includeOwner: false, footprint, CancellationToken.None))));

        Assert.Single(sink.Received);
        Assert.Contains(failure.InnerExceptions, error => error is InvalidOperationException { Message: FailingSink.Failure });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_departure_queue_holds_a_cleanup_departure_only_once_its_transaction_commits(bool commits)
    {
        var scope = TestRequired.TransactionScope();
        var f = new PvpRoomFixture(scope) { DuelChannel = Ranked };
        var (_, fighters) = await f.FightAsync();
        var wake = new CountingWake();
        var queue = new Dfo.Server.Protocol.Game.PvpBattle.PvpDepartureProjectionQueue(wake);
        var cleanup = Cleanup(f, scope, [queue]);

        var pending = await TestGameplay.RunAsync(async () =>
        {
            await using (var lease = scope.Begin())
            {
                await TestEffectFootprints.Run(fighters[1], async footprint =>
                    await cleanup.CleanupAsync(fighters[1], includeOwner: false, footprint, CancellationToken.None));
                Assert.Empty(queue.SnapshotPending());
                if (commits) await lease.CommitAsync(default);
            }
            return queue.SnapshotPending();
        });

        Assert.Equal(commits ? 1 : 0, pending.Length);
        Assert.Equal(commits ? 1 : 0, wake.Count);
    }

    [Fact]
    public async Task A_record_without_experience_is_shown_at_its_grade_lower_bound()
    {
        var f = new PvpRoomFixture();
        var character = new CharacterId(77);
        f.Records.Records[character] = new PvpRecord(2, 1, null, 4, 3, 0, 0, 0);

        var view = await new PvpRecordDomainService(f.Records, f.BattleRules, PvpRatingSeason.First, NullLogger<PvpRecordDomainService>.Instance).ViewAsync(character, default);

        Assert.Equal(new PvpRecordView(2, 1, f.BattleRules.LowerBound(2), f.BattleRules.LowerBound(2), f.BattleRules.UpperBound(2), 4, 3), view);
        f.Records.Records[character] = new PvpRecord(2, 1, f.BattleRules.LowerBound(2) + 7, 4, 3, 0, 0, 0);
        Assert.Equal(f.BattleRules.LowerBound(2) + 7, (await new PvpRecordDomainService(f.Records, f.BattleRules, PvpRatingSeason.First, NullLogger<PvpRecordDomainService>.Instance).ViewAsync(character, default)).Experience);
    }

    /// <summary>
    /// The production path for a fighter that stops sending heartbeats: the others keep sending, and the room's own
    /// deadline counts the silent one fled once its heartbeat window has passed.
    /// </summary>
    private static async Task FallSilentAsync(
        PvpRoomFixture f, PvpRoomSnapshot room, SelectedSessionIdentity silent, SelectedSessionIdentity[] talking)
    {
        var since = f.Committed(room).Match!.Fighters.Single(x => x.CharacterId == silent.CharacterId).LastHeartbeat;
        f.Clock.Now = since + TimeSpan.FromSeconds(29);
        foreach (var fighter in talking) await f.Commit(rooms => rooms.HeartbeatAsync(fighter, default));
        f.Clock.Now = since + TimeSpan.FromSeconds(30);
        Assert.Equal([room.Id], f.Rooms.Deadlines(f.Clock.Now).DueRooms);
        Changed(await f.Commit(rooms => rooms.AdvanceDueAsync(room.Id, f.Clock.Now, default)));
    }

    /// <remarks>
    /// Review F-2: a grade kept from before the archive's table was enforced must not lock the character out of
    /// selection, which reads the record on every channel; the band is the top grade's and the store is left alone.
    /// </remarks>
    [Fact]
    public async Task A_stored_grade_above_the_top_is_shown_in_the_top_grade_s_band_with_a_warning()
    {
        var f = new PvpRoomFixture();
        var character = new CharacterId(78);
        var top = PvpRoomFixture.Grades.MaximumGrade;
        var stored = new PvpRecord(25, 1, null, 4, 3, 0, 0, 0);
        f.Records.Records[character] = stored;
        var log = new Dfo.Server.Application.Tests.Execution.RecordingExecutionLogger<PvpRecordDomainService>();

        var view = await new PvpRecordDomainService(f.Records, f.BattleRules, PvpRatingSeason.First, log).ViewAsync(character, default);

        Assert.Equal(new PvpRecordView(25, 1, f.BattleRules.LowerBound(top), f.BattleRules.LowerBound(top),
            f.BattleRules.UpperBound(top), 4, 3), view);
        var warning = Assert.Single(log.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, warning.Level);
        Assert.Equal(stored, f.Records.Records[character]);
        // Counter-check: a grade inside the table is shown in its own band and logs nothing.
        f.Records.Records[character] = stored with { Grade = top };
        Assert.Equal(f.BattleRules.UpperBound(top),
            (await new PvpRecordDomainService(f.Records, f.BattleRules, PvpRatingSeason.First, log).ViewAsync(character, default)).UpperBound);
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task A_stored_grade_above_the_top_settles_from_the_top_grade_s_band()
    {
        var f = new PvpRoomFixture { DuelChannel = Ranked };
        var (room, fighters) = await f.FightAsync();
        foreach (var fighter in fighters)
            f.Records.Records[fighter.CharacterId] = new PvpRecord(25, 0, null, 0, 0, 0, 0, 0);
        await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default));

        Assert.IsType<PvpSettlementOutcome.Settled>(await f.SettleAsync(f.Committed(room).Match!.Id));

        // The fixture's top grade 3 starts at 600: a win there earns nothing, a loss of 20 drops to grade 2.
        Assert.Equal(((byte)3, (long?)600), (f.Records.Of(fighters[0]).Grade, f.Records.Of(fighters[0]).Experience));
        Assert.Equal(((byte)2, (long?)580), (f.Records.Of(fighters[1]).Grade, f.Records.Of(fighters[1]).Experience));
    }

    private static SelectedSessionCleanupService Cleanup(
        PvpRoomFixture f, Dfo.Server.Application.Transactions.DomainTransactionScope scope, IPvpRoomDepartureSink sink) =>
        Cleanup(f, scope, [sink]);

    private static SelectedSessionCleanupService Cleanup(
        PvpRoomFixture f, Dfo.Server.Application.Transactions.DomainTransactionScope scope, IPvpRoomDepartureSink[] sinks,
        IPvpSettlementDomainService? settlement = null)
    {
        var presence = new SessionPresenceDomainService();
        var parties = TestParties.Create(presence);
        return new SelectedSessionCleanupService(f.Sessions, new CharacterContainerDomainService(TestRequired.TransactionScope()),
            presence, new ExpertStoreDomainService(), f.Lobby, [],
            new OnlineCharacterRuntimeDomainService(TimeProvider.System, presence.Locations), new StubChannelEntryPolicy(),
            parties, TestParties.Runs(parties: parties), [], scope,
            creatures: TestRequired.Absent<Dfo.Server.Abstractions.Creatures.ICreatureRuntimeDomainService>(),
            illusionTower: TestRequired.Absent<Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerDomainService>(),
            illusionTowerDepartures: [],
            housing: TestRequired.Absent<Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService>(),
            bossTower: TestRequired.Absent<Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService>(),
            bossTowerDepartures: [],
            conversations: new Dfo.Server.Application.Chat.ChatConversationDomainService(),
            relations: new TestCharacterRelations(), identities: new TestCharacterRelations(),
            pvpRooms: f.Rooms, pvpSettlement: settlement ?? f.Settlement, pvpDepartures: sinks,
            logger: NullLogger<SelectedSessionCleanupService>.Instance,
            raids: TestRequired.Absent<Dfo.Server.Abstractions.Raids.IAntonRaidDomainService>(),
            guilds: TestRequired.Absent<Dfo.Server.Abstractions.Guilds.IGuildMembershipQueryDomainService>());
    }

    private sealed class FailingSink : IPvpRoomDepartureSink
    {
        public const string Failure = "The departure sink failed.";

        public int Calls { get; private set; }

        public ValueTask DepartedAsync(PvpRoomOutcome.Changed departure, PvpSettlementOutcome? settled, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException(Failure);
        }
    }

    /// <summary>Settles for real, then reports the outcome as unknown, as a commit whose acknowledgement was lost would.</summary>
    private sealed class UncertainSettlement(IPvpSettlementDomainService inner) : IPvpSettlementDomainService
    {
        public async ValueTask<PvpSettlementOutcome> SettleAsync(PvpMatchId match, CancellationToken cancellationToken)
        {
            await inner.SettleAsync(match, cancellationToken);
            throw new Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException(
                Dfo.Server.Abstractions.Transactions.TransactionUncertaintyReason.PostCommitPublicationFailure,
                "The settlement's commit was not acknowledged.", new IOException("lost"));
        }
    }

    private sealed class CountingWake : IPvpDeadlineWake
    {
        public int Count { get; private set; }

        public void Wake() => Count++;
    }

    private sealed class RecordingSink : IPvpRoomDepartureSink
    {
        public List<(PvpRoomOutcome.Changed Departure, PvpSettlementOutcome? Settled)> Received { get; } = [];

        public ValueTask DepartedAsync(
            PvpRoomOutcome.Changed departure, PvpSettlementOutcome? settled, CancellationToken cancellationToken)
        {
            Received.Add((departure, settled));
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A two-member room standing in <paramref name="phase"/>; an ended battle there is won by the first fighter.</summary>
    private static async Task<(PvpRoomSnapshot Room, SelectedSessionIdentity[] Fighters)> ReachAsync(PvpRoomFixture f, PvpRoomPhase phase)
    {
        if (phase == PvpRoomPhase.Waiting)
        {
            var (owner, guest) = (f.Join(1), f.Join(2));
            var waiting = await f.CreateAsync(owner);
            await f.EnterAsync(guest, waiting);
            return (waiting, [owner, guest]);
        }
        var (room, fighters) = await f.FightAsync();
        if (phase == PvpRoomPhase.Fighting) return (room, fighters);
        await f.Commit(rooms => rooms.ReportDeathAsync(fighters[1], fighters[0].CharacterId, default));
        if (phase == PvpRoomPhase.AwaitRank) return (room, fighters);
        await f.SettleAsync(f.Committed(room).Match!.Id);
        Assert.Equal(PvpRoomPhase.AwaitResult, f.Committed(room).Phase);
        return (room, fighters);
    }

    private static PvpRoomOutcome.Changed Changed(PvpRoomOutcome? outcome) => Assert.IsType<PvpRoomOutcome.Changed>(outcome);

    private static void Refused(PvpRoomOutcome outcome, PvpRoomRefusal reason) =>
        Assert.Equal(reason, Assert.IsType<PvpRoomOutcome.Refused>(outcome).Reason);
}
