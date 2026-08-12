namespace Dfo.Server.Abstractions.Content;

public sealed record DungeonExperienceProgressionPolicy(
    IReadOnlyList<uint> LevelThresholds,
    ulong MaximumHonorExperience,
    uint GrowthCapsuleMaximum,
    int GrowthCapsuleRatePercent);

public interface IDungeonExperienceProgressionPolicySource
{
    DungeonExperienceProgressionPolicy Load();
}
