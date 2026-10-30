using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.World;

internal sealed class WorldDropOptions
{
    // Reference-server policy, not a proven original-server probability base.
    public int ProbabilityBasis { get; init; } = 100000;

    // Temporary client-verification aid; never changes the ordinary roll or table weights.
    public bool DebugClearCubeOnMiss { get; init; }
}
