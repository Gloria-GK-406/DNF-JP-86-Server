using Dfo.Server.Abstractions.Accounts;

namespace Dfo.Server.Abstractions.Content;

public sealed record DungeonExperienceProgressionPolicy(
    IReadOnlyList<uint> LevelThresholds,
    HonorExperienceScale Honor,
    uint GrowthCapsuleMaximum,
    int GrowthCapsuleRatePercent)
{
    public ulong MaximumHonorExperience => Honor.MaximumExperience;
}

public interface IDungeonExperienceProgressionPolicySource
{
    DungeonExperienceProgressionPolicy Load();
}
