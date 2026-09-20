using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class DungeonDifficultyRuleService : IDungeonDifficultyRuleService
{
    public bool Allows(DungeonDifficulty requested, DungeonDifficulty maximum) =>
        Enum.IsDefined(requested) && Enum.IsDefined(maximum) && requested <= maximum;
}
