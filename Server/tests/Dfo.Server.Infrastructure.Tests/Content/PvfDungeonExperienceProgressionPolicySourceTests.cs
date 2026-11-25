using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Data.Characters;

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
        Assert.Equal(17_699_999_999UL, policy.MaximumHonorExperience);
        Assert.Equal(AccountHonorLevel.Initial, policy.Honor.ProgressFor(0));
        Assert.Equal(new AccountHonorLevel(9, 89_999_999), policy.Honor.ProgressFor(449_999_999));
        Assert.Equal(new AccountHonorLevel(10, 0) { Grade = 2 }, policy.Honor.ProgressFor(450_000_000));
        Assert.Equal(new AccountHonorLevel(30, 0) { Grade = 4 }, policy.Honor.ProgressFor(4_350_000_000));
        Assert.Equal(new AccountHonorLevel(59, 589_999_999) { Grade = 6 }, policy.Honor.ProgressFor(ulong.MaxValue));
        Assert.True(policy.GrowthCapsuleMaximum > 0);
        Assert.True(policy.GrowthCapsuleRatePercent >= 0);
    }
}
