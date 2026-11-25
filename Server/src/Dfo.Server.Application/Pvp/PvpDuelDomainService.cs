using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Pvp;

/// <summary>Free rooms and ordinary matchmaking use these admission and ranking rules.</summary>
internal sealed class PvpDuelDomainService(IPvpRecordDomainStore records, PvpRatingSeason season)
{
    public static PvpChannelEntitlements Entitlements { get; } = new(false, false, 0, 0);
    public static PvpChannelEntitlements ColosseumEntitlements { get; } = new(true, true, 50, 10);

    public static bool AllowsMatchmaking(ChannelEntryIdentity channel, PvpBattleMode mode) =>
        channel.SupportsMatchmaking
        && mode is PvpBattleMode.Individual or PvpBattleMode.Relay;

    internal async ValueTask<bool> CanEnterColosseumAsync(CharacterId character, int minimumRp, CancellationToken ct)
    {
        var standing = await records.ReadRatingAsync(character, season.Id, ct).ConfigureAwait(false);
        return standing.PlacementGames >= PvpRatingStanding.PlacementLimit && standing.Points >= minimumRp;
    }

    public async ValueTask<Dictionary<CharacterId, PvpRatingChange>?> RatingsAsync(
        PvpMatchConclusion battle, CancellationToken cancellationToken)
    {
        if (!PvpRatingRules.AppliesTo(battle)) return null;
        var standings = new Dictionary<CharacterId, PvpRatingStanding>(battle.Fighters.Length);
        foreach (var fighter in battle.Fighters)
            standings.Add(fighter.CharacterId, await records.ReadRatingAsync(fighter.CharacterId, season.Id, cancellationToken)
                .ConfigureAwait(false));
        return PvpRatingRules.Rate(battle, standings, season);
    }
}
