using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.World;

internal sealed class DungeonDifficultyRuleService(IDungeonDefinitionSource? definitions = null) : IDungeonDifficultyRuleService
{
    public bool Allows(DungeonDifficulty requested, DungeonDifficulty maximum) =>
        Enum.IsDefined(requested) && Enum.IsDefined(maximum) && requested <= maximum;

    public async ValueTask<bool> AllowsAsync(int dungeonId, DungeonDifficulty requested, DungeonDifficulty maximum,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(requested) || !Enum.IsDefined(maximum)) return false;
        var definition = definitions is null ? null : await definitions.GetAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        var tiers = definition?.SupportedDifficulties;
        if (tiers is not { Count: > 0 }) return Allows(requested, maximum);
        if (!tiers.Contains((byte)requested)) return false;
        // The lowest declared tier is available initially, including a single fixed ancient tier.
        return (byte)requested == tiers.Min() || requested <= maximum
            || definition!.HasStoryMode || definition.ArchivePath.StartsWith("dungeon/quest/", StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask<DungeonDifficulty?> NextAfterClearAsync(int dungeonId, DungeonDifficulty cleared, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(cleared)) throw new ArgumentOutOfRangeException(nameof(cleared));
        var source = definitions ?? throw new InvalidOperationException("Dungeon progression requires content rules.");
        var definition = await source.GetAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        if (definition?.ArchivePath.StartsWith("dungeon/quest/", StringComparison.OrdinalIgnoreCase) == true)
            return null;
        var tiers = definition?.SupportedDifficulties;
        if (tiers is null || !tiers.Contains((byte)cleared)) return null;
        var next = tiers.Where(value => value > (byte)cleared).Order().Select(value => (byte?)value).FirstOrDefault();
        return next is null ? null : (DungeonDifficulty)next.Value;
    }
}
