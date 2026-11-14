using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

/// <summary>Settles ended duel battles inside the caller's writable transaction.</summary>
/// <remarks>
/// The rules decide, the store writes once per battle, the event is published only by the write that created
/// the battle, and the room moves on only with that write: a failed write leaves the battle waiting, the room
/// on its ranking screen, and no event.
/// </remarks>
internal sealed class PvpSettlementDomainService(
    PvpRoomDomainService rooms,
    IPvpRecordDomainStore records,
    IPvpSettlementRuleService rules,
    IEventService events) : IPvpSettlementDomainService
{
    public async ValueTask<PvpSettlementOutcome> SettleAsync(PvpMatchId match, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpSettlementDomainService) + "." + nameof(SettleAsync));
        if (rooms.FindConclusion(match) is not { } conclusion) return new PvpSettlementOutcome.NotPending();

        var entitlements = rules.EntitlementsOf(conclusion.Channel);
        // Spec RQ-052: a battle counts toward the game day it started on, whenever it ends.
        var gameDay = rules.GameDayOf(conclusion.StartedAt);
        var fighters = new List<PvpFighterSettlement>(conclusion.Fighters.Length);
        foreach (var fighter in conclusion.Fighters)
        {
            var record = await records.ReadAsync(fighter.CharacterId, cancellationToken).ConfigureAwait(false);
            var today = await records.DailyCountsAsync(fighter.CharacterId, gameDay, cancellationToken).ConfigureAwait(false);
            var outcome = fighter.Fled ? PvpOutcome.Loss : OutcomeOf(conclusion, fighter);
            // A channel that keeps no records is decided too: the decision then is the record as it stands.
            var decision = rules.Decide(new PvpSettlementFacts(record.Grade, record.Experience, outcome, fighter.Fled,
                today.Wins, today.Losses, entitlements));
            fighters.Add(new PvpFighterSettlement(fighter.CharacterId, (byte)fighter.Seat, (byte)fighter.Team, outcome,
                fighter.Kills, fighter.Deaths, fighter.Fled, decision));
        }

        var settlement = new PvpMatchSettlementRecord(conclusion.MatchId.Value, conclusion.RoomNumber,
            conclusion.Channel.ListenerPort, conclusion.Channel.LoginEnvironment, conclusion.Mode, conclusion.Terminal.Kind,
            conclusion.Terminal.Winner, entitlements.RecordsResult, gameDay, conclusion.Terminal.At, fighters);
        var stored = await records.SettleAsync(settlement, cancellationToken).ConfigureAwait(false);
        if (stored is PvpSettleOutcome.Created created)
            await events.EnqueueAsync(new PvpMatchSettled(conclusion.MatchId.Value, conclusion.Channel.ListenerPort,
                conclusion.RoomNumber, conclusion.Mode, conclusion.Terminal.Kind, conclusion.Terminal.Winner,
                entitlements.RecordsResult, gameDay,
                [.. created.Results.Select(result => new PvpMatchSettledFighter(
                    result.Character, result.Outcome, result.Fled, result.Grade, result.ExperienceChange))]),
                cancellationToken).ConfigureAwait(false);
        return new PvpSettlementOutcome.Settled(conclusion, stored, rooms.CompleteSettlement(conclusion, stored.Results));
    }

    /// <summary>A fighter who did not flee wins with the winning side and loses with every other; a draw is everyone's.</summary>
    private static PvpOutcome OutcomeOf(PvpMatchConclusion conclusion, PvpFighter fighter)
    {
        if (conclusion.Terminal is not { Kind: PvpMatchOutcomeKind.Decided, Winner: { } winner }) return PvpOutcome.Draw;
        var side = PvpBattleModeCapabilities.Of(conclusion.Mode).Terminal switch
        {
            PvpBattleTerminal.LastFighterStanding => fighter.Seat,
            PvpBattleTerminal.LastTeamStanding => (int)fighter.Team,
        };
        return side == winner ? PvpOutcome.Win : PvpOutcome.Loss;
    }
}
