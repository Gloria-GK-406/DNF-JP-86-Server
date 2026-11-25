using System.Collections.Immutable;

namespace Dfo.Server.Abstractions.Pvp;

/// <summary>Mode decisions applied to immutable snapshots by the room owner.</summary>
public interface IPvpBattleModeRules
{
    bool CanSelectInFreeRoom { get; }
    int MaximumMembers { get; }
    bool AllowsObservation { get; }
    bool AcceptsChannel(byte? loginEnvironment);
    PvpTeam TeamForNewcomer(IReadOnlyList<PvpTeam> seated);
    IReadOnlyList<PvpTeam> Reassign(int members);
    bool AcceptsTeam(PvpTeam team);
    PvpRoomRefusal? RefuseStart(IReadOnlyList<PvpRoomMemberFacts> members);
    PvpMatchModeState? Start(DateTimeOffset now);
    bool IsActive(PvpMatch match, PvpFighter fighter);
    PvpRoomRefusal? RefuseDeath(PvpMatch match, PvpFighter fighter, DateTimeOffset now);
    PvpMatch RecordDeath(PvpMatch match, int index, int? killerIndex, DateTimeOffset now);
    (PvpMatchModeState? State, PvpRoomRefusal Refusal) RequestNextTurn(PvpMatch match, PvpFighter fighter);
    (PvpMatchTerminal? End, PvpRoomRefusal Refusal) ReportTimeout(
        PvpMatch match, DateTimeOffset now, IPvpSettlementRuleService battles, TimeSpan lead);
    DateTimeOffset Deadline(PvpMatch match);
    PvpMatch? AdvanceTurn(PvpMatch match, DateTimeOffset now, IPvpSettlementRuleService battles, TimeSpan overrun);
    PvpMatch AfterNonterminalChange(PvpMatch match, DateTimeOffset now);
    PvpMatchTerminal? TerminalOf(PvpMatch match, DateTimeOffset now);
    PvpMatchTerminal? Watchdog(PvpMatch match, DateTimeOffset now);
}

public interface IPvpTotalMatchBattleRules : IPvpBattleModeRules
{
    (PvpTotalMatchBattle State, DateTimeOffset Deadline) Open(ImmutableArray<PvpTotalMatchSide> sides,
        DateTimeOffset preparedAt, DateTimeOffset startedAt, bool matchmaking, TimeSpan heartbeatTimeout, TimeSpan roundLimit);
    PvpMatch ReportOpeningHeartbeat(PvpMatch match, int seat, DateTimeOffset now);
    (PvpMatch? Match, PvpRoomRefusal Refusal) Choose(PvpMatch match, int seat, byte ordinal, DateTimeOffset now);
    PvpMatch RecordAppearances(PvpMatch match, DateTimeOffset now);
    ImmutableArray<PvpTotalMatchAppearance> AppearancesOf(PvpMatchModeState? modeState);
}
