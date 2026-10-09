using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Support;

// Synthetic rooms in transaction/admission tests have no PVF tier progression.
internal sealed class TestDungeonDifficultyRules : IDungeonDifficultyRuleService
{
    public bool Allows(DungeonDifficulty requested, DungeonDifficulty maximum) =>
        Enum.IsDefined(requested) && Enum.IsDefined(maximum) && requested <= maximum;
    public ValueTask<bool> IsUnlockedAsync(int dungeonId, DungeonDifficulty requested,
        DungeonDifficulty maximum, CancellationToken cancellationToken) => ValueTask.FromResult(Allows(requested, maximum));

    public ValueTask<DungeonDifficulty?> NextAfterClearAsync(int dungeonId, DungeonDifficulty cleared,
        CancellationToken cancellationToken) => ValueTask.FromResult<DungeonDifficulty?>(null);
    public ValueTask<DungeonDifficulty?> NearestUnlockedAsync(int dungeonId, DungeonDifficulty requested,
        DungeonDifficulty maximum, CancellationToken cancellationToken) => ValueTask.FromResult<DungeonDifficulty?>(
            !Enum.IsDefined(requested) || !Enum.IsDefined(maximum) ? null : requested <= maximum ? requested : maximum);
}
