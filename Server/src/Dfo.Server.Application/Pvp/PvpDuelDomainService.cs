using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Pvp;

/// <summary>Free rooms and ordinary matchmaking use these admission and ranking rules.</summary>
internal sealed class PvpDuelDomainService(IPvpRecordDomainStore records, PvpRatingSeason season,
    PvpMatchmakingRuleService matchmakingRules)
{
    internal async ValueTask<bool> CanEnterColosseumAsync(CharacterId character, int minimumRp, CancellationToken ct)
    {
        var standing = await records.ReadRatingAsync(character, season.Id, ct).ConfigureAwait(false);
        return matchmakingRules.MeetsRatingFloor(standing, minimumRp);
    }

    public async ValueTask<Dictionary<CharacterId, PvpRatingChange>?> RatingsAsync(
        PvpMatchConclusion battle, CancellationToken cancellationToken)
    {
        if (!matchmakingRules.AppliesTo(battle)) return null;
        var standings = new Dictionary<CharacterId, PvpRatingStanding>(battle.Fighters.Length);
        foreach (var fighter in battle.Fighters)
            standings.Add(fighter.CharacterId, await records.ReadRatingAsync(fighter.CharacterId, season.Id, cancellationToken)
                .ConfigureAwait(false));
        return matchmakingRules.Rate(battle, standings, season);
    }
}
