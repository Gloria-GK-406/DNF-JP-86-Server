using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.World;

internal sealed class DungeonDifficultyRuleService(IDungeonDefinitionSource? definitions = null,
    IDungeonAdmissionDefinitionSource? admissions = null) : IDungeonDifficultyRuleService
{
    public bool Allows(DungeonDifficulty requested, DungeonDifficulty maximum) =>
        Enum.IsDefined(requested) && Enum.IsDefined(maximum) && requested <= maximum;

    public async ValueTask<bool> AllowsAsync(int dungeonId, DungeonDifficulty requested, DungeonDifficulty maximum,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(requested) || !Enum.IsDefined(maximum)) return false;
        var definition = await DefinitionAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        return IsUnlocked(requested, maximum, definition)
            || definition?.SupportedDifficulties?.Contains((byte)requested) == true
                && (definition.HasStoryMode || definition.ArchivePath.StartsWith("dungeon/quest/", StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask<bool> IsUnlockedAsync(int dungeonId, DungeonDifficulty requested,
        DungeonDifficulty maximum, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(requested) || !Enum.IsDefined(maximum)) return false;
        var definition = await DefinitionAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        return IsUnlocked(requested, maximum, definition);
    }

    public async ValueTask<DungeonDifficulty?> NearestUnlockedAsync(int dungeonId, DungeonDifficulty requested,
        DungeonDifficulty maximum, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(requested) || !Enum.IsDefined(maximum)) return null;
        var definition = await DefinitionAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        if ((definitions is not null || admissions is not null) && definition is null) return null;
        return Enum.GetValues<DungeonDifficulty>().Where(tier => IsUnlocked(tier, maximum, definition))
            .OrderBy(tier => Math.Abs((int)tier - (int)requested)).ThenBy(tier => tier)
            .Select(tier => (DungeonDifficulty?)tier).FirstOrDefault();
    }

    private bool IsUnlocked(DungeonDifficulty requested, DungeonDifficulty maximum, DungeonAdmissionDefinition? definition)
    {
        var tiers = definition?.SupportedDifficulties;
        if (tiers is not { Count: > 0 }) return Allows(requested, maximum);
        // Even when tier zero is not declared, the lowest declared tier is initially available.
        return tiers.Contains((byte)requested) && ((byte)requested == tiers.Min() || requested <= maximum);
    }

    public async ValueTask<DungeonDifficulty?> NextAfterClearAsync(int dungeonId, DungeonDifficulty cleared, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(cleared)) throw new ArgumentOutOfRangeException(nameof(cleared));
        if (definitions is null && admissions is null) throw new InvalidOperationException("Dungeon progression requires content rules.");
        var definition = await DefinitionAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        if (definition?.ArchivePath.StartsWith("dungeon/quest/", StringComparison.OrdinalIgnoreCase) == true)
            return null;
        var tiers = definition?.SupportedDifficulties;
        if (tiers is null || !tiers.Contains((byte)cleared)) return null;
        var next = tiers.Where(value => value > (byte)cleared).Order().Select(value => (byte?)value).FirstOrDefault();
        return next is null ? null : (DungeonDifficulty)next.Value;
    }
    private async ValueTask<DungeonAdmissionDefinition?> DefinitionAsync(int id, CancellationToken ct) => admissions is not null
        ? await admissions.GetAdmissionAsync(id, ct).ConfigureAwait(false)
        : definitions is not null && await definitions.GetAsync(id, ct).ConfigureAwait(false) is { } d
            ? DungeonAdmissionDefinition.From(d) : null;

}
