using System.Collections.Immutable;
using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

internal sealed partial class MonsterDropDomainService
{
    private static readonly ImmutableArray<DropDrawEntry> OrdinaryEntries =
        [DropDrawEntry.MonsterGold, DropDrawEntry.MonsterStackable, DropDrawEntry.MonsterEquipment];

    private IReadOnlyList<RolledDrop> GenerateRandomDrops(MonsterDropRequest request, DropDrawMultipliers multipliers)
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
        result.AddRange(this.GenerateExtraRounds(request, multipliers));
        return result;
    }

    /// <remarks>
    /// Each later round repeats the whole K-attempt unit: the Extra flag suppresses ordinary items in
    /// its own round only. Slots gate independently, so drawing them per entry keeps their odds
    /// while letting gold and equipment carry different multipliers.
    /// </remarks>
    private List<RolledDrop> GenerateExtraRounds(MonsterDropRequest request, DropDrawMultipliers multipliers)
    {
        var result = new List<RolledDrop>();
        var rounds = Math.Max(multipliers.For(DropDrawEntry.MonsterExtra),
            OrdinaryEntries.Max(multipliers.For));
        if (rounds <= 1) return result;
        var rates = dropRules.RatesFor(request) is { } found ? adjuster.Adjust(request.CharacterId, found) : null;
        for (var round = 1; round < rounds; round++)
        {
            var extraHit = false;
            if (round < multipliers.For(DropDrawEntry.MonsterExtra))
                for (var attempt = 0; attempt < request.Context.RandomDropCount; attempt++)
                {
                    var drawn = extraDrops.Draw(request with { Draw = new(DropDrawEntry.MonsterExtra, round) }, attempt);
                    extraHit |= drawn.Count > 0;
                    result.AddRange(drawn);
                }
            if (rates is null) continue;
            foreach (var entry in OrdinaryEntries)
            {
                if (round >= multipliers.For(entry)) continue;
                for (var attempt = 0; attempt < request.Context.RandomDropCount; attempt++)
                    result.AddRange(dropRules.DrawFor(
                        request with { Draw = new(entry, round) }, rates, !extraHit, attempt));
            }
        }
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

    /// <summary>Repeats one draw unit for each round of its entry, from <paramref name="first"/> on.</summary>
    private static IEnumerable<RolledDrop> Rounds(MonsterDropRequest request, DropDrawEntry entry,
        DropDrawMultipliers multipliers, Func<MonsterDropRequest, IReadOnlyList<RolledDrop>> draw, int first = 0)
    {
        for (var round = first; round < multipliers.For(entry); round++)
            foreach (var drop in draw(request with { Draw = new(entry, round) }))
                yield return drop;
    }

    // One fixed-basis draw per monster, independent of party size.
    private IReadOnlyList<RolledDrop> GenerateMonsterDeclared(MonsterDropRequest request, DropDrawMultipliers multipliers) =>
        Rounds(request, DropDrawEntry.MonsterDeclared, multipliers, declaredDrops.Draw).ToArray();

    // Hell/event rewards need their own trigger and replacement rules, not one payout per actor.
    private static IReadOnlyList<RolledDrop> GenerateSpecialMode(MonsterDropRequest request) => [];
}
