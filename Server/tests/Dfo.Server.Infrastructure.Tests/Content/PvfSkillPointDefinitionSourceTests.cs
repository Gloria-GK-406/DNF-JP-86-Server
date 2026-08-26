using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfSkillPointDefinitionSourceTests
{
    [SkippableFact]
    public async Task Real_archive_projects_totals_free_grants_and_learned_skill_costs()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfSkillPointDefinitionSource(provider);

        var result = await source.GetAsync(0, 1, 86, [1], CancellationToken.None);

        Assert.Equal(11010, result.TotalSp);
        Assert.Equal(37, result.TotalTp);
        Assert.InRange(result.FreeGrants.Count, 14, 14);
        Assert.Equal(new SkillPointGrantDefinition(179, 7, IsInitial: true), result.FreeGrants[0]);
        Assert.True(result.Skills.TryGetValue(1, out var skill));
        Assert.Equal([20], skill!.Costs);
        Assert.Equal(5, skill.OrdinaryMaximumLevel);
    }

    [SkippableFact]
    public async Task Real_archive_preserves_initial_skill_source_order_for_normal_and_dark_knight_jobs()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfSkillPointDefinitionSource(provider);
        var normal = await source.GetAsync(1, 1, 86, [], CancellationToken.None);
        var darkKnight = await source.GetAsync(9, 1, 86, [], CancellationToken.None);

        Assert.Equal([179, 174, 169, 46, 5, 511],
            normal.FreeGrants.Where(value => value.IsInitial).Take(6).Select(value => value.SkillId));
        Assert.Equal([118, 119, 120, 121, 122, 123],
            darkKnight.FreeGrants.Where(value => value.IsInitial).Take(6).Select(value => value.SkillId));
    }

    [SkippableFact]
    public void Real_archive_projects_expert_contract_premium_types()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var result = new PvfExpertSkillContractDefinitionSource(provider)
            .GetOverSkillPremiumTypes();

        Assert.Equal([27], result.Order());
        Assert.DoesNotContain(0, result);
    }

    [SkippableFact]
    public async Task Real_archive_distinguishes_inapplicable_skills_from_applicable_zero_level_caps()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfSkillPointDefinitionSource(provider);

        var result = await source.GetAsync(
            job: 0, packedGrowType: 1, characterLevel: 1,
            Enumerable.Range(1, 1000).ToArray(), CancellationToken.None);

        Assert.Contains(result.Skills.Values, value => value.OrdinaryMaximumLevel is null);
        Assert.Contains(result.Skills.Values, value => value.OrdinaryMaximumLevel == 0);
        Assert.Equal(2, result.Skills.Values
            .Where(value => value.OrdinaryMaximumLevel is null).Min(value => value.SkillId));
        Assert.Equal(1, result.Skills.Values
            .Where(value => value.OrdinaryMaximumLevel == 0).Min(value => value.SkillId));
    }
}
