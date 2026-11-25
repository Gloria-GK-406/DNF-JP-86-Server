using System.Collections.Immutable;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Pvp;

/// <summary>Traditional duel entitlements, total-match entry and settlement.</summary>
internal sealed class PvpTraditionalDuelDomainService(
    IPvpRecordDomainStore records,
    PvpRatingSeason season,
    IPvpTotalMatchCandidateQueryDomainService candidates,
    PvpMatchmakingRuleService matchmakingRules)
{
    public static PvpChannelEntitlements Entitlements { get; } = new(true, true, 50, 10);

    public async ValueTask<(PvpTotalMatchLineup? Lineup, bool MissingEquipment)> ReadMatchmakingLineupAsync(
        SelectedSessionIdentity session, DateTimeOffset preparedAt, CancellationToken cancellationToken)
    {
        var lineup = await candidates.ReadAsync(session, preparedAt, cancellationToken).ConfigureAwait(false);
        if (lineup is null) return (null, false);
        return matchmakingRules.HasRequiredTotalMatchEquipment(lineup) ? (lineup, false) : (null, true);
    }

    internal async ValueTask<bool> CanEnterColosseumAsync(SelectedSessionIdentity actor, DateTimeOffset now, CancellationToken ct)
    {
        var standing = await records.ReadTotalMatchRatingAsync(actor.CharacterId, season.Id, ct).ConfigureAwait(false);
        return matchmakingRules.HasCompletedPlacement(standing)
            && (await ReadMatchmakingLineupAsync(actor, now, ct).ConfigureAwait(false)).Lineup is not null;
    }

    public static bool RecordsResult(PvpMatchConclusion conclusion) =>
        conclusion.Mode != PvpBattleMode.TotalMatch
        || (!conclusion.TotalMatchAppearances.IsDefaultOrEmpty && !conclusion.Fighters.All(fighter => fighter.Fled)
            && conclusion.Terminal.Kind != PvpMatchOutcomeKind.Cancelled);

    public async ValueTask<IReadOnlyDictionary<AccountId, PvpRatingChange>> RatingsAsync(
        PvpMatchConclusion battle, CancellationToken cancellationToken)
    {
        if (!matchmakingRules.AppliesToTotalMatch(battle))
            return ImmutableDictionary<AccountId, PvpRatingChange>.Empty;

        var first = battle.Fighters[0];
        var second = battle.Fighters[1];
        var own = await records.ReadTotalMatchRatingAsync(first.CharacterId, season.Id, cancellationToken).ConfigureAwait(false);
        var opponent = await records.ReadTotalMatchRatingAsync(second.CharacterId, season.Id, cancellationToken).ConfigureAwait(false);
        return matchmakingRules.RateTotalMatch(battle, own, opponent, season);
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
