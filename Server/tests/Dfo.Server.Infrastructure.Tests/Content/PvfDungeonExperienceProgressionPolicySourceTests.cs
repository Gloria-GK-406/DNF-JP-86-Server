using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfDungeonExperienceProgressionPolicySourceTests
{
    [SkippableFact]
    public void RealArchive_ProvidesExactEntryThresholdAndBoundedHonorCapsulePolicy()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var policy = new PvfDungeonExperienceProgressionPolicySource(provider).Load();

        Assert.True(policy.LevelThresholds.Count >= 85);
        Assert.True(policy.LevelThresholds[84] > policy.LevelThresholds[0]);
        Assert.True(policy.MaximumHonorExperience > 0);
        Assert.True(policy.GrowthCapsuleMaximum > 0);
        Assert.True(policy.GrowthCapsuleRatePercent >= 0);
    }
}
