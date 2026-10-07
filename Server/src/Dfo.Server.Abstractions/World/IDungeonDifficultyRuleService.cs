using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public interface IDungeonDifficultyRuleService
{
    bool Allows(DungeonDifficulty requested, DungeonDifficulty maximum);
    ValueTask<DungeonDifficulty?> NextAfterClearAsync(int dungeonId, DungeonDifficulty cleared, CancellationToken cancellationToken);
}
