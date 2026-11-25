using System.Collections.Immutable;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

/// <summary>Traditional duel entitlements, total-match entry and settlement.</summary>
internal sealed class PvpTraditionalDuelDomainService(
    IPvpRecordDomainStore records,
    PvpRatingSeason season,
    IPvpTotalMatchCandidateQueryDomainService candidates)
{
    public static PvpChannelEntitlements Entitlements { get; } = new(true, true, 50, 10);

    public static bool AllowsMatchmaking(ChannelEntryIdentity channel, PvpBattleMode mode) =>
        channel.AllowsTotalMatchmaking && mode == PvpBattleMode.TotalMatch;

    public async ValueTask<(PvpTotalMatchLineup? Lineup, bool MissingEquipment)> ReadMatchmakingLineupAsync(
        SelectedSessionIdentity session, DateTimeOffset preparedAt, CancellationToken cancellationToken)
    {
        var lineup = await candidates.ReadAsync(session, preparedAt, cancellationToken).ConfigureAwait(false);
        if (lineup is null) return (null, false);
        var hasAllEquipment = lineup.Candidates.All(candidate =>
            Enumerable.Range(12, 10).All(slot => candidate.Equipment.Any(item => item.SlotIndex == slot)));
        return hasAllEquipment ? (lineup, false) : (null, true);
    }

    internal async ValueTask<bool> CanEnterColosseumAsync(SelectedSessionIdentity actor, DateTimeOffset now, CancellationToken ct)
    {
        var standing = await records.ReadTotalMatchRatingAsync(actor.CharacterId, season.Id, ct).ConfigureAwait(false);
        return standing.PlacementGames >= PvpRatingStanding.PlacementLimit
            && (await ReadMatchmakingLineupAsync(actor, now, ct).ConfigureAwait(false)).Lineup is not null;
    }

    public static bool RecordsResult(PvpMatchConclusion conclusion) =>
        conclusion.Mode != PvpBattleMode.TotalMatch
        || (!conclusion.TotalMatchAppearances.IsDefaultOrEmpty && !conclusion.Fighters.All(fighter => fighter.Fled)
            && conclusion.Terminal.Kind != PvpMatchOutcomeKind.Cancelled);

    public async ValueTask<IReadOnlyDictionary<AccountId, PvpRatingChange>> RatingsAsync(
        PvpMatchConclusion battle, CancellationToken cancellationToken)
    {
        if (!PvpRatingRules.AppliesToTotalMatch(battle))
            return ImmutableDictionary<AccountId, PvpRatingChange>.Empty;

        var first = battle.Fighters[0];
        var second = battle.Fighters[1];
        var own = await records.ReadTotalMatchRatingAsync(first.CharacterId, season.Id, cancellationToken).ConfigureAwait(false);
        var opponent = await records.ReadTotalMatchRatingAsync(second.CharacterId, season.Id, cancellationToken).ConfigureAwait(false);
        var score = first.Fled ? 0.0 : second.Fled ? 1.0
            : battle.Terminal.Kind == PvpMatchOutcomeKind.Draw ? 0.5
            : PvpSettlementDomainService.OutcomeOf(battle, first) == PvpOutcome.Win ? 1.0 : 0.0;
        var advantage = (double)opponent.Points - own.Points;
        return new Dictionary<AccountId, PvpRatingChange>
        {
            [first.Session.AccountId] = PvpRatingRules.Update(own, advantage, score, season),
            [second.Session.AccountId] = PvpRatingRules.Update(opponent, -advantage, 1 - score, season),
        };
    }

    public static IEnumerable<(CharacterId Character, int Kills, int Deaths, bool UpdatesRecord)> SettlementCharacters(
        PvpMatchConclusion conclusion, PvpFighter fighter, bool recordsResult)
    {
        if (conclusion.Mode != PvpBattleMode.TotalMatch || !recordsResult)
        {
            yield return (fighter.CharacterId, fighter.Kills, fighter.Deaths, recordsResult);
            yield break;
        }

        var appearances = conclusion.TotalMatchAppearances.Where(role => role.Seat == fighter.Seat).ToArray();
        foreach (var role in appearances)
            yield return (role.Character, role.Kills, role.Deaths, true);
        // The representative still needs a result-screen row if a reserve fought in their place.
        if (!appearances.Any(role => role.Character == fighter.CharacterId))
            yield return (fighter.CharacterId, 0, 0, false);
    }
}
