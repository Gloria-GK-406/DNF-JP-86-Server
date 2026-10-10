using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public interface IDungeonDifficultyRuleService
{
    bool Allows(DungeonDifficulty requested, DungeonDifficulty maximum);
    ValueTask<bool> AllowsAsync(int dungeonId, DungeonDifficulty requested, DungeonDifficulty maximum,
        CancellationToken cancellationToken) => ValueTask.FromResult(Allows(requested, maximum));
    /// <summary>Supported, unlocked tiers, without story-entry progression exemptions.</summary>
    ValueTask<bool> IsUnlockedAsync(int dungeonId, DungeonDifficulty requested, DungeonDifficulty maximum,
        CancellationToken cancellationToken);
    ValueTask<DungeonDifficulty?> NearestUnlockedAsync(int dungeonId, DungeonDifficulty requested,
        DungeonDifficulty maximum, CancellationToken cancellationToken);
    ValueTask<DungeonDifficulty?> NextAfterClearAsync(int dungeonId, DungeonDifficulty cleared, CancellationToken cancellationToken);
}
