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

}
