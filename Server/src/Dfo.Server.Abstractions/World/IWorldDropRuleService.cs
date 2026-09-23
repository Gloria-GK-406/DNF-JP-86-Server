namespace Dfo.Server.Abstractions.World;

public sealed class WorldDropOptions
{
    // Reference-server policy, not a proven original-server probability base.
    public int ProbabilityBasis { get; init; } = 100000;

    // Temporary client-verification aid; never changes the ordinary roll or table weights.
    public bool DebugClearCubeOnMiss { get; init; }
}

public interface IWorldDropRuleService
{
    int Initialize();
    IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request);
}
