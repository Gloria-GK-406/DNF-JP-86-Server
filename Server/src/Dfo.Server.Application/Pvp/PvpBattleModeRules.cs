using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

/// <summary>Decisions for one mode. All per-room and per-fight state lives in the snapshots.</summary>
internal abstract class PvpBattleModeRules(PvpBattleMode mode) : IPvpBattleModeRules
{
    private readonly PvpBattleModeCapabilities capabilities = PvpBattleModeCapabilities.Of(mode);

    public bool CanSelectInFreeRoom => capabilities.Supported;

    public virtual int MaximumMembers => PvpRoomSnapshot.SeatCount;

    public virtual bool AllowsObservation => true;

    public virtual bool AcceptsChannel(byte? loginEnvironment) => true;

    public PvpTeam TeamForNewcomer(IReadOnlyList<PvpTeam> seated) =>
        capabilities.TeamAssignment switch
        {
            PvpTeamAssignment.Individual => PvpTeam.Individual,
            PvpTeamAssignment.AlternatingTwoTeams =>
                seated.Count(team => team == PvpTeam.Second) < seated.Count(team => team == PvpTeam.First)
                    ? PvpTeam.Second : PvpTeam.First,
        };

    public IReadOnlyList<PvpTeam> Reassign(int members) =>
        [.. Enumerable.Range(0, members).Select(index => capabilities.TeamAssignment switch
        {
            PvpTeamAssignment.Individual => PvpTeam.Individual,
            PvpTeamAssignment.AlternatingTwoTeams => index % 2 == 0 ? PvpTeam.First : PvpTeam.Second,
        })];

    public bool AcceptsTeam(PvpTeam team) =>
        capabilities.TeamAssignment switch
        {
            PvpTeamAssignment.Individual => team == PvpTeam.Individual,
            PvpTeamAssignment.AlternatingTwoTeams => team is PvpTeam.First or PvpTeam.Second,
        };

    public virtual PvpRoomRefusal? RefuseStart(IReadOnlyList<PvpRoomMemberFacts> members)
    {
        if (members.Count < PvpRoomRules.MinimumFighters) return PvpRoomRefusal.TooFewMembers;
        if (members.Any(member => !member.IsOwner && !member.Ready)) return PvpRoomRefusal.NotAllReady;
        if (capabilities.RequiresBalancedTeams
            && members.Count(member => member.Team == PvpTeam.First) != members.Count(member => member.Team == PvpTeam.Second))
            return PvpRoomRefusal.UnbalancedTeams;
        return null;
    }

    public virtual PvpMatchModeState? Start(DateTimeOffset now) => null;

    public virtual bool IsActive(PvpMatch match, PvpFighter fighter) => true;

    public virtual PvpRoomRefusal? RefuseDeath(PvpMatch match, PvpFighter fighter, DateTimeOffset now) =>
        IsActive(match, fighter) ? null : PvpRoomRefusal.NotFighter;

    public virtual PvpMatch RecordDeath(PvpMatch match, int index, int? killerIndex, DateTimeOffset now)
    {
        var victim = match.Fighters[index];
        var fighters = match.Fighters.SetItem(index, victim with { Alive = false, Deaths = victim.Deaths + 1 });
        if (killerIndex is { } by)
        {
            var killer = fighters[by];
            fighters = fighters.SetItem(by, killer with { Kills = killer.Kills + 1 });
        }
        return match with { Fighters = fighters };
    }

    public virtual (PvpMatchModeState? State, PvpRoomRefusal Refusal) RequestNextTurn(PvpMatch match, PvpFighter fighter) =>
        (null, PvpRoomRefusal.UnsupportedMode);

    public virtual (PvpMatchTerminal? End, PvpRoomRefusal Refusal) ReportTimeout(
        PvpMatch match, DateTimeOffset now, IPvpSettlementRuleService battles, TimeSpan lead)
    {
        if (now - match.StartedAt < battles.BattleTimeLimit(mode) - lead)
            return (null, PvpRoomRefusal.TooEarly);
        return (Draw(now), default);
    }

    public virtual DateTimeOffset Deadline(PvpMatch match) => match.PhaseDeadline;

    /// <summary>Advance a pending round, or return null if no round transition is due.</summary>
    public virtual PvpMatch? AdvanceTurn(
        PvpMatch match, DateTimeOffset now, IPvpSettlementRuleService battles, TimeSpan overrun) => null;

    public virtual PvpMatch AfterNonterminalChange(PvpMatch match, DateTimeOffset now) => match;

    public virtual PvpMatchTerminal? TerminalOf(PvpMatch match, DateTimeOffset now)
    {
        var alive = match.Fighters.Where(fighter => fighter.Alive);
        byte[] sides = capabilities.Terminal switch
        {
            PvpBattleTerminal.LastFighterStanding => [.. alive.Select(fighter => (byte)fighter.Seat)],
            PvpBattleTerminal.LastTeamStanding => [.. alive.Select(fighter => (byte)fighter.Team).Distinct()],
        };
        return sides.Length switch
        {
            0 => Draw(now),
            1 => new PvpMatchTerminal(PvpMatchOutcomeKind.Decided, sides[0], now),
            _ => null,
        };
    }

    public PvpMatchTerminal? Watchdog(PvpMatch match, DateTimeOffset now) =>
        TerminalOf(match, now) is null ? Draw(now) : null;

    private static PvpMatchTerminal Draw(DateTimeOffset now) => new(PvpMatchOutcomeKind.Draw, null, now);
}

internal sealed class PvpIndividualBattleRules() : PvpBattleModeRules(PvpBattleMode.Individual);

internal sealed class PvpTeamBattleRules() : PvpBattleModeRules(PvpBattleMode.Team);
