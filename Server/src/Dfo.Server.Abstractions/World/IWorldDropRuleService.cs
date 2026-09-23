namespace Dfo.Server.Abstractions.World;

public sealed class WorldDropOptions
{
    // Reference-server policy, not a proven original-server probability base.
    public int ProbabilityBasis { get; init; } = 100000;
}

public interface IWorldDropRuleService
{
    int Initialize();
    IReadOnlyList<RolledDrop> Draw(MonsterDropRequest request);
}
