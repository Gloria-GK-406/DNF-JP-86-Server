using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

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
    PvpDuelDomainService duel,
    PvpTraditionalDuelDomainService traditional,
    PvpDailyMissions dailyMissions,
    IEventService events,
    PvpColosseumDomainService colosseum) : IPvpSettlementDomainService
{
    private static readonly PvpChannelEntitlements NoRecord = new(false, false, 0, 0);

    public async ValueTask<PvpSettlementOutcome> SettleAsync(PvpMatchId match, CancellationToken cancellationToken)
    {
        GameplayWriteAssertion.Writing(nameof(PvpSettlementDomainService) + "." + nameof(SettleAsync));
        if (rooms.FindConclusion(match) is not { } conclusion) return new PvpSettlementOutcome.NotPending();

        var settlement = await BuildSettlementAsync(conclusion, cancellationToken).ConfigureAwait(false);
        var stored = await records.SettleAsync(settlement, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<CharacterId, int> rewards = ImmutableDictionary<CharacterId, int>.Empty;
        if (stored is PvpSettleOutcome.Created created)
        {
            var colosseumWinners = await colosseum.SettleAsync(conclusion, cancellationToken).ConfigureAwait(false);
            rewards = await dailyMissions.AdvanceAsync(conclusion, settlement.GameDay, cancellationToken, colosseumWinners)
                .ConfigureAwait(false);
            await events.EnqueueAsync(new PvpMatchSettled(conclusion.MatchId.Value, conclusion.Channel.ListenerPort,
                conclusion.RoomNumber, conclusion.Mode, conclusion.Terminal.Kind, conclusion.Terminal.Winner,
                settlement.RecordsResult, settlement.GameDay,
                [.. created.Results.Select(result => new PvpMatchSettledFighter(
                    result.Character, result.Outcome, result.Fled, result.Grade, result.ExperienceChange))]),
                cancellationToken).ConfigureAwait(false);
        }
        var colosseumStates = new Dictionary<int, PvpColosseumLobbyState>();
        if (!conclusion.ColosseumRuns.IsEmpty)
        {
            foreach (var fighter in conclusion.Fighters)
            {
                if (fighter.Departed || !conclusion.ColosseumRuns.ContainsKey(fighter.Seat)) continue;
                colosseumStates.Add(fighter.Seat,
                    await colosseum.ReadLobbyAsync(fighter.Session, cancellationToken).ConfigureAwait(false));
            }
        }
        return new PvpSettlementOutcome.Settled(conclusion, stored, rooms.CompleteSettlement(conclusion, stored.Results))
        {
            VictoryEmblemBalances = rewards,
            ColosseumStates = colosseumStates,
        };
    }

    internal async ValueTask<PvpMatchSettlementRecord> BuildSettlementAsync(
        PvpMatchConclusion conclusion, CancellationToken cancellationToken)
    {
        var entitlements = rules.EntitlementsOf(conclusion.Channel, conclusion.Mode);
        var isTraditional = conclusion.Business == PvpDuelBusiness.Traditional;
        var traditionalRecords = isTraditional && entitlements.RecordsResult
            && PvpTraditionalDuelDomainService.RecordsResult(conclusion);
        var individualColosseum = conclusion.Mode == PvpBattleMode.Individual
            && !conclusion.ColosseumRuns.IsEmpty && PvpRatingRules.AppliesTo(conclusion);
        // Spec RQ-052: a battle counts toward the game day it started on, whenever it ends.
        var gameDay = rules.GameDayOf(conclusion.StartedAt);
        var ratings = isTraditional ? null : await duel.RatingsAsync(conclusion, cancellationToken).ConfigureAwait(false);
        var fighters = new List<PvpFighterSettlement>(conclusion.Fighters.Length);
        foreach (var fighter in conclusion.Fighters)
        {
            var outcome = fighter.Fled ? PvpOutcome.Loss : OutcomeOf(conclusion, fighter);
            var colosseumFighter = individualColosseum && conclusion.ColosseumRuns.ContainsKey(fighter.Seat);
            var fighterEntitlements = colosseumFighter ? PvpDuelDomainService.ColosseumEntitlements : entitlements;
            var participants = isTraditional
                ? PvpTraditionalDuelDomainService.SettlementCharacters(conclusion, fighter, traditionalRecords)
                : [(fighter.CharacterId, fighter.Kills, fighter.Deaths, colosseumFighter)];
            foreach (var (character, kills, deaths, updatesRecord) in participants)
            {
                var record = await records.ReadAsync(character, cancellationToken).ConfigureAwait(false);
                var today = updatesRecord
                    ? await records.DailyCountsAsync(character, gameDay, cancellationToken).ConfigureAwait(false)
                    : new PvpDailyCounts(0, 0);
                var decision = rules.Decide(new PvpSettlementFacts(record.Grade, record.Experience, outcome, fighter.Fled,
                    today.Wins, today.Losses, updatesRecord ? fighterEntitlements : NoRecord));
                fighters.Add(new PvpFighterSettlement(character, (byte)fighter.Seat, (byte)fighter.Team, outcome,
                    kills, deaths, fighter.Fled, decision, updatesRecord)
                {
                    Rating = ratings?.GetValueOrDefault(character),
                });
            }
        }

        return new PvpMatchSettlementRecord(conclusion.MatchId.Value, conclusion.RoomNumber,
            conclusion.Channel.ListenerPort, conclusion.Channel.LoginEnvironment, conclusion.Mode, conclusion.Terminal.Kind,
            conclusion.Terminal.Winner, traditionalRecords || individualColosseum, gameDay, conclusion.Terminal.At, fighters)
        {
            TotalMatchRatings = isTraditional
                ? await traditional.RatingsAsync(conclusion, cancellationToken).ConfigureAwait(false)
                : ImmutableDictionary<AccountId, PvpRatingChange>.Empty,
        };
    }

    /// <summary>A fighter who did not flee wins with the winning side and loses with every other; a draw is everyone's.</summary>
    internal static PvpOutcome OutcomeOf(PvpMatchConclusion conclusion, PvpFighter fighter)
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
