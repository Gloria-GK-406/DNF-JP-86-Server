using System.Collections.Immutable;
using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpTotalMatchBattleRules(TimeSpan openingDelay, TimeSpan selectionWindow,
    TimeSpan roundIntro, TimeSpan knockoutWindow) : PvpBattleModeRules(PvpBattleMode.TotalMatch), IPvpTotalMatchBattleRules
{
    public override int MaximumMembers => 2;
    public override bool AllowsObservation => false;
    public override bool AcceptsChannel(byte? loginEnvironment) => loginEnvironment is not 24;

    public (PvpTotalMatchBattle State, DateTimeOffset Deadline) Open(ImmutableArray<PvpTotalMatchSide> sides,
        DateTimeOffset preparedAt, DateTimeOffset startedAt, bool matchmaking, TimeSpan heartbeatTimeout, TimeSpan roundLimit) =>
        (new PvpTotalMatchBattle(PvpTotalMatchPhase.Loading, sides, preparedAt, roundLimit)
            { WaitForOpeningHeartbeats = matchmaking }, startedAt + (matchmaking ? heartbeatTimeout : openingDelay));

    public override bool IsActive(PvpMatch match, PvpFighter fighter) =>
        match.ModeState is PvpTotalMatchBattle { Phase: PvpTotalMatchPhase.Fighting or PvpTotalMatchPhase.BetweenRounds } state
        && SideOf(state, fighter.Seat).Choice is not null;

    public override PvpRoomRefusal? RefuseDeath(PvpMatch match, PvpFighter fighter, DateTimeOffset now)
    {
        if (!IsActive(match, fighter)) return PvpRoomRefusal.NotFighter;
        if (now >= match.PhaseDeadline) return PvpRoomRefusal.WrongPhase;
        // A death carries only the representative's wire ID, so a previous-round report cannot count during the countdown.
        return now < ((PvpTotalMatchBattle)match.ModeState!).RoundOpensAt ? PvpRoomRefusal.TooEarly : null;
    }

    public override PvpMatch RecordDeath(PvpMatch match, int index, int? killerIndex, DateTimeOffset now)
    {
        match = RecordAppearances(match, now);
        var state = (PvpTotalMatchBattle)match.ModeState!;
        var scored = base.RecordDeath(match, index, killerIndex, now);
        var victim = scored.Fighters[index];
        var side = SideOf(state, victim.Seat);
        var defeated = side with { Defeated = (byte)(side.Defeated | (1 << side.Choice!.Value)), Choice = null };
        var sides = state.Sides.Replace(side, defeated);
        if (killerIndex is { } by)
        {
            var scorer = SideOf(state, scored.Fighters[by].Seat);
            var ordinal = scorer.Choice!.Value;
            sides = sides.Replace(scorer, scorer with
            {
                CandidateKills = scorer.CandidateKills.SetItem(ordinal, scorer.CandidateKills.GetValueOrDefault(ordinal) + 1),
            });
        }
        return scored with
        {
            ModeState = state with
            {
                Phase = PvpTotalMatchPhase.BetweenRounds,
                Sides = sides,
            },
            Fighters = scored.Fighters.SetItem(index, victim with { Alive = defeated.HasRemaining }),
            PhaseDeadline = state.Phase == PvpTotalMatchPhase.BetweenRounds ? match.PhaseDeadline : now + knockoutWindow,
        };
    }

    // A live round can only leave Fighting through a death, a terminal result, or cancellation.
    // Record both participants before the first two paths change the round. Cancellation awards nothing.
    public PvpMatch RecordAppearances(PvpMatch match, DateTimeOffset now)
    {
        if (match.ModeState is not PvpTotalMatchBattle { Phase: PvpTotalMatchPhase.Fighting } state
            || now < state.RoundOpensAt)
            return match;
        return match with
        {
            ModeState = state with
            {
                Sides = [.. state.Sides.Select(side => side with { Appeared = (byte)(side.Appeared | (1 << side.Choice!.Value)) })],
            },
        };
    }

    public ImmutableArray<PvpTotalMatchAppearance> AppearancesOf(PvpMatchModeState? modeState)
    {
        if (modeState is not PvpTotalMatchBattle total) return [];

        var appearances = ImmutableArray.CreateBuilder<PvpTotalMatchAppearance>();
        foreach (var side in total.Sides)
            for (byte ordinal = 0; ordinal < side.Lineup.Candidates.Length; ordinal++)
                if ((side.Appeared & (1 << ordinal)) != 0)
                    appearances.Add(new(side.Seat, side.Lineup.Candidates[ordinal].Character.Identity.CharacterId,
                        side.CandidateKills.GetValueOrDefault(ordinal), (side.Defeated & (1 << ordinal)) == 0 ? 0 : 1));
        return appearances.ToImmutable();
    }

    public override PvpMatchTerminal? TerminalOf(PvpMatch match, DateTimeOffset now)
    {
        if (match.Fighters.Any(fighter => fighter.Fled)
            && match.Fighters.SingleOrDefault(fighter => !fighter.Fled) is { } opponent)
            return new(PvpMatchOutcomeKind.Decided, (byte)opponent.Team, now);

        return match.ModeState is PvpTotalMatchBattle { Phase: PvpTotalMatchPhase.BetweenRounds }
            && now < match.PhaseDeadline && match.Fighters.All(fighter => !fighter.Departed)
                ? null : base.TerminalOf(match, now);
    }

    // A21 resolves time-up locally and sends the regular death report for the eliminated actors.
    public override (PvpMatchTerminal? End, PvpRoomRefusal Refusal) ReportTimeout(
        PvpMatch match, DateTimeOffset now, IPvpSettlementRuleService battles, TimeSpan lead) =>
        (null, PvpRoomRefusal.UnsupportedMode);

    public override PvpMatch? AdvanceTurn(
        PvpMatch match, DateTimeOffset now, IPvpSettlementRuleService battles, TimeSpan overrun)
    {
        if (match.PhaseDeadline > now || match.ModeState is not PvpTotalMatchBattle state)
            return null;
        return state.Phase switch
        {
            PvpTotalMatchPhase.Loading when state.WaitForOpeningHeartbeats => null,
            PvpTotalMatchPhase.Loading or PvpTotalMatchPhase.BetweenRounds => match.Fighters.All(fighter => fighter.Alive)
                ? OpenSelection(match, state, now) : match,
            PvpTotalMatchPhase.Selecting => BeginRound(match, state, now),
            _ => null,
        };
    }

    // On the matching channel A21 enables battle heartbeats when the matching UI hands off to PREPARE_BATTLE.
    // Let that state's map and spawn initialization run before 0x031E forcibly changes the client state.
    public PvpMatch ReportOpeningHeartbeat(PvpMatch match, int seat, DateTimeOffset now)
    {
        if (match.ModeState is not PvpTotalMatchBattle { Phase: PvpTotalMatchPhase.Loading, WaitForOpeningHeartbeats: true } state
            || now >= match.PhaseDeadline)
            return match;
        var bit = (byte)(1 << seat);
        if ((state.OpeningHeartbeatSeats & bit) != 0) return match;
        var heartbeatSeats = (byte)(state.OpeningHeartbeatSeats | bit);
        var ready = state.Sides.All(side => (heartbeatSeats & (1 << side.Seat)) != 0);
        return match with
        {
            ModeState = state with { OpeningHeartbeatSeats = heartbeatSeats, WaitForOpeningHeartbeats = !ready },
            PhaseDeadline = ready ? now + openingDelay : match.PhaseDeadline,
        };
    }

    public (PvpMatch? Match, PvpRoomRefusal Refusal) Choose(
        PvpMatch match, int seat, byte ordinal, DateTimeOffset now)
    {
        if (match.ModeState is not PvpTotalMatchBattle { Phase: PvpTotalMatchPhase.Selecting } state
            || match.PhaseDeadline <= now)
            return (null, PvpRoomRefusal.WrongPhase);

        var side = SideOf(state, seat);
        if (side.Choice is not null) return (null, PvpRoomRefusal.AlreadyReported);
        if (!side.CanSelect(ordinal)) return (null, PvpRoomRefusal.InvalidSeat);

        var selected = state with { Sides = state.Sides.Replace(side, side with { Choice = ordinal }) };
        return selected.Sides.All(entry => entry.Choice is not null)
            ? (BeginRound(match, selected, now), default)
            : (match with { ModeState = selected }, default);
    }

    private static PvpTotalMatchSide SideOf(PvpTotalMatchBattle state, int seat) =>
        state.Sides.Single(entry => entry.Seat == seat);

    private PvpMatch OpenSelection(PvpMatch match, PvpTotalMatchBattle state, DateTimeOffset now) => match with
    {
        ModeState = state with { Phase = PvpTotalMatchPhase.Selecting },
        PhaseDeadline = now + selectionWindow,
    };

    private PvpMatch BeginRound(PvpMatch match, PvpTotalMatchBattle state, DateTimeOffset now) => match with
    {
        ModeState = state with
        {
            Phase = PvpTotalMatchPhase.Fighting,
            RoundOpensAt = now + roundIntro,
            Sides = [.. state.Sides.Select(side => side.Choice is not null ? side
                : side with { Choice = (byte)Enumerable.Range(0, side.Lineup.Candidates.Length).First(side.CanSelect) })],
        },
        PhaseDeadline = now + roundIntro + state.RoundLimit,
    };
}
