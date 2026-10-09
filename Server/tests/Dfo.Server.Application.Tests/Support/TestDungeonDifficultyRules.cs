using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Support;

// Synthetic rooms in transaction/admission tests have no PVF tier progression.
internal sealed class TestDungeonDifficultyRules : IDungeonDifficultyRuleService
{
    public bool Allows(DungeonDifficulty requested, DungeonDifficulty maximum) =>
        Enum.IsDefined(requested) && Enum.IsDefined(maximum) && requested <= maximum;

    public ValueTask<DungeonDifficulty?> NextAfterClearAsync(int dungeonId, DungeonDifficulty cleared,
        CancellationToken cancellationToken) => ValueTask.FromResult<DungeonDifficulty?>(null);
}
