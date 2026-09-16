using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.World;

/// <summary>
/// Resolves the loot plan attached to a monster while its cell is being created.
/// </summary>
/// <remarks>
/// Numbering and floor ownership belong to the cell. This service only reads rules and freezes
/// their result before the cell is published.
/// </remarks>
internal sealed class MonsterLootRuleService(
    IDropRuleService dropRules,
    IDropRateAdjuster adjuster,
    IQuestMonsterDropRuleService questDrops,
    ILogger<MonsterLootRuleService> logger) : IMonsterLootRuleService
{
    public async ValueTask<IReadOnlyList<RolledDrop>> ResolveAsync(
        MonsterKill kill, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kill);
        var rolled = new List<RolledDrop>(this.Roll(kill));
        rolled.AddRange(await questDrops.DrawAsync(kill, cancellationToken).ConfigureAwait(false));
        return rolled;
    }

    /// <summary>
    /// What this kill produced, before anything is numbered.
    /// </summary>
    /// <remarks>
    /// The rates are asked for, raised on the character's behalf if anything is owed, and handed
    /// back to be drawn at. The raising happens here rather than inside the rules because what a
    /// character is owed is state that crosses runs, and the rules are not allowed to go and find
    /// it.
    /// </remarks>
    private IReadOnlyList<RolledDrop> Roll(MonsterKill kill)
    {
        if (dropRules.RatesFor(kill) is not { } rates)
        {
            logger.LogWarning(
                "Monster drop character={CharacterId}: the rate table covers no level "
                    + "{Level}, so nothing was dropped",
                kill.CharacterId.Value,
                kill.MonsterLevel);
            return [];
        }

        return dropRules.DrawFor(kill, adjuster.Adjust(kill.CharacterId, rates));
    }
}
