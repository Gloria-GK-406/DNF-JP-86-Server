using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

internal sealed partial class MonsterDropDomainService
{
    private IReadOnlyList<RolledDrop> GenerateOrdinary(MonsterDropRequest kill)
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

        return dropRules.DrawFor(kill, adjuster.Adjust(kill.CharacterId, rates));
    }

    // Disabled until extra-table replacement/append conditions are established.
    private static IReadOnlyList<RolledDrop> GenerateExtra(MonsterDropRequest request) => [];

    // One fixed-basis draw per monster, independent of party size.
    private IReadOnlyList<RolledDrop> GenerateMonsterDeclared(MonsterDropRequest request) => declaredDrops.Draw(request);

    // Quantity axis and embedded-list sampling remain unresolved; external pools are absent.
    private static IReadOnlyList<RolledDrop> GenerateIndependent(MonsterDropRequest request) => [];

    // Hell/event rewards need their own trigger and replacement rules, not one payout per actor.
    private static IReadOnlyList<RolledDrop> GenerateSpecialMode(MonsterDropRequest request) => [];
}
