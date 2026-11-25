using System.Collections.Immutable;
using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpRelayBattleRules() : PvpBattleModeRules(PvpBattleMode.Relay)
{
    // ServerS4A21 allows the client to finish its initial countdown or KO animation before assigning fighters.
    private static readonly TimeSpan FirstTurnDelay = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan ReplacementDelay = TimeSpan.FromSeconds(3);

    // A21 networked rooms select the second relay timer entry (180 s), even when pvp_ref says 120 s.
    private static readonly TimeSpan ClientBattleTime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ClientRoundCountdown = TimeSpan.FromMilliseconds(4640);

    public override PvpRelayBattle Start(DateTimeOffset now) => new(null, null, now + FirstTurnDelay, []);

    private static PvpRelayBattle Round(PvpMatch match) => (PvpRelayBattle)match.ModeState!;

    public override bool IsActive(PvpMatch match, PvpFighter fighter) => Round(match).IsActive(fighter.Seat);

    public override (PvpMatchModeState? State, PvpRoomRefusal Refusal) RequestNextTurn(PvpMatch match, PvpFighter fighter)
    {
        var relay = Round(match);
        if (!fighter.Alive || relay.IsActive(fighter.Seat))
            return (null, PvpRoomRefusal.NotFighter);
        var requests = relay.Requests.Contains(fighter.Seat) ? relay.Requests.Remove(fighter.Seat) : relay.Requests.Add(fighter.Seat);
        return (relay with { Requests = requests }, default);
    }

    // The A21 relay client handles time-up by reporting an elimination.
    public override (PvpMatchTerminal? End, PvpRoomRefusal Refusal) ReportTimeout(
        PvpMatch match, DateTimeOffset now, IPvpSettlementRuleService battles, TimeSpan lead) =>
        (null, PvpRoomRefusal.UnsupportedMode);

    public override DateTimeOffset Deadline(PvpMatch match) => Round(match).TurnDue ?? match.PhaseDeadline;

    public override PvpMatch? AdvanceTurn(
        PvpMatch match, DateTimeOffset now, IPvpSettlementRuleService battles, TimeSpan overrun)
    {
        var relay = Round(match);
        if (relay.TurnDue is not { } due || due > now) return null;
        // Each turn starts its own client countdown, including after a replacement.
        var roundTime = MinimumTimeBeforeOverrun(battles.BattleTimeLimit(PvpBattleMode.Relay));
        return match with
        {
            ModeState = Advance(relay, match.Fighters),
            PhaseDeadline = now + roundTime + overrun,
        };
    }

    public override PvpMatch AfterNonterminalChange(PvpMatch match, DateTimeOffset now)
    {
        var relay = Round(match);
        var replacement = ScheduleReplacement(relay, match.Fighters, now);
        return replacement == relay ? match : match with { ModeState = replacement };
    }

    private static TimeSpan MinimumTimeBeforeOverrun(TimeSpan configuredRoundTime) =>
        ClientRoundCountdown + (configuredRoundTime < ClientBattleTime ? ClientBattleTime : configuredRoundTime);

    private static PvpRelayBattle ScheduleReplacement(PvpRelayBattle relay, ImmutableArray<PvpFighter> fighters, DateTimeOffset now)
    {
        if (relay.TurnDue is not null) return relay;
        return fighters.Any(fighter => !fighter.Alive && relay.IsActive(fighter.Seat))
            ? relay with { TurnDue = now + ReplacementDelay }
            : relay;
    }

    private static PvpRelayBattle Advance(PvpRelayBattle relay, ImmutableArray<PvpFighter> fighters)
    {
        var first = SelectSeat(relay, fighters, PvpTeam.First, relay.FirstSeat);
        var second = SelectSeat(relay, fighters, PvpTeam.Second, relay.SecondSeat);
        return new(first, second, null, relay.Requests.Intersect(fighters
            .Where(fighter => fighter.Alive && fighter.Seat != first && fighter.Seat != second)
            .Select(fighter => fighter.Seat)));
    }

    private static int? SelectSeat(PvpRelayBattle relay, ImmutableArray<PvpFighter> fighters, PvpTeam team, int? active)
    {
        return fighters.Where(fighter => fighter.Team == team && fighter.Alive)
            .OrderByDescending(fighter => fighter.Seat == active)
            .ThenByDescending(fighter => relay.Requests.Contains(fighter.Seat))
            .ThenBy(fighter => fighter.Seat)
            .FirstOrDefault()?.Seat;
    }
}
