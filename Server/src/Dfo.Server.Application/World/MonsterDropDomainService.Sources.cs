using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

internal sealed partial class MonsterDropDomainService
{
    private IReadOnlyList<RolledDrop> GenerateRandomDrops(MonsterDropRequest request)
    {
        if (request.MonsterRank is < 0 or > 3 || request.Context.RandomDropCount == 0)
            return [];
        var result = new List<RolledDrop>();
        // One shared success flag across the entire Extra phase, not per-attempt fallback.
        for (var attempt = 0; attempt < request.Context.RandomDropCount; attempt++)
            result.AddRange(extraDrops.Draw(request, attempt));
        var includeOrdinaryItems = result.Count == 0;
        for (var attempt = 0; attempt < request.Context.RandomDropCount; attempt++)
            result.AddRange(this.GenerateOrdinary(request, includeOrdinaryItems, attempt));
        return result;
    }

    private IReadOnlyList<RolledDrop> GenerateOrdinary(MonsterDropRequest kill, bool includeItems, int attempt)
    {
        if (dropRules.RatesFor(kill) is not { } rates)
        {
            logger.LogWarning(
                "Monster drop character={CharacterId}: the rate table covers no level "
                    + "{Level}, so no ordinary loot was generated",
                kill.CharacterId.Value,
                kill.MonsterLevel);
            return [];
        }

        return dropRules.DrawFor(kill, adjuster.Adjust(kill.CharacterId, rates), includeItems, attempt);
    }

    // One fixed-basis draw per monster, independent of party size.
    private IReadOnlyList<RolledDrop> GenerateMonsterDeclared(MonsterDropRequest request) => declaredDrops.Draw(request);

    // Quantity axis and embedded-list sampling remain unresolved; external pools are absent.
    private static IReadOnlyList<RolledDrop> GenerateIndependent(MonsterDropRequest request) => [];

    // Hell/event rewards need their own trigger and replacement rules, not one payout per actor.
    private static IReadOnlyList<RolledDrop> GenerateSpecialMode(MonsterDropRequest request) => [];
}
