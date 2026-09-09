using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Content;

using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class CharacterSkillArchiveTests
{
    [SkippableFact]
    public async Task Multi_value_maximum_uses_the_career_slot()
    {
        Skip.IfNot(new PvfContentOptions().ResolveArchivePath() is not null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(new PvfContentOptions());
        var result = await new CharacterSkillRuleService(new PvfSkillPointDefinitionSource(provider)).GetAsync(3, 1, 86, [29], CancellationToken.None);
        Assert.Equal(20, result.Skills[29].OrdinaryMaximumLevel);
    }
    [SkippableFact]
    public async Task Nested_fixed_level_block_is_recognized()
    {
        Skip.IfNot(new PvfContentOptions().ResolveArchivePath() is not null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(new PvfContentOptions());
        var result = await new CharacterSkillRuleService(new PvfSkillPointDefinitionSource(provider)).GetAsync(0, 0x11, 86, [86], CancellationToken.None);
        Assert.True(result.Skills[86].IsFixedLevel);
        Assert.Equal(5, result.Skills[86].FixedLevelInterval);
        Assert.Equal(0, result.Skills[86].FixedLevelBase);
    }

    [SkippableFact]
    public async Task Real_archive_projects_totals_free_grants_and_learned_skill_costs()
    {
        Skip.IfNot(new PvfContentOptions().ResolveArchivePath() is not null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(new PvfContentOptions());
        var source = new CharacterSkillRuleService(new PvfSkillPointDefinitionSource(provider));

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
        Skip.IfNot(new PvfContentOptions().ResolveArchivePath() is not null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(new PvfContentOptions());
        var source = new CharacterSkillRuleService(new PvfSkillPointDefinitionSource(provider));
        var normal = await source.GetAsync(1, 1, 86, [], CancellationToken.None);
        var darkKnight = await source.GetAsync(9, 0, 86, [], CancellationToken.None);

        Assert.Equal([179, 174, 169, 46, 5, 511],
            normal.FreeGrants.Where(value => value.IsInitial).Take(6).Select(value => value.SkillId));
        Assert.Equal([118, 119, 120, 121, 122, 123],
            darkKnight.FreeGrants.Where(value => value.IsInitial).Take(6).Select(value => value.SkillId));
    }

    [SkippableFact]
    public void Real_archive_projects_expert_contract_premium_types()
    {
        Skip.IfNot(new PvfContentOptions().ResolveArchivePath() is not null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(new PvfContentOptions());

        var result = new PvfExpertSkillContractDefinitionSource(provider)
            .GetOverSkillPremiumTypes();

        Assert.Equal([27], result.Order());
        Assert.DoesNotContain(0, result);
    }

    [SkippableFact]
    public async Task Static_rules_are_shared_across_levels_learned_sets_and_racing_callers()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(new PvfContentOptions());
        var source = new CharacterSkillRuleService(new PvfSkillPointDefinitionSource(provider));
        var first = await source.GetAsync(0, 1, 86, [1, 2], CancellationToken.None);
        var second = await source.GetAsync(0, 1, 25, [2, 1, 64], CancellationToken.None);
        Assert.Same(first.Skills[1].Rule, second.Skills[1].Rule);
        var answers = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => source.Get(0, 1))));
        Assert.All(answers, answer => Assert.Same(first.Skills[1].Rule, answer));
        Assert.Contains(source.Get(0, 64)!, source.GetAll(0));
        Assert.Equal(new CharacterSkillPointBudget(11010, 37), source.GetTotalPoints(0, 86));
    }

    [SkippableFact]
    public void Real_skills_answer_career_prerequisite_and_cumulative_cost_questions()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(new PvfContentOptions());
        var rules = new CharacterSkillRuleService(new PvfSkillPointDefinitionSource(provider));
        var swordMaster = new CharacterCareer(0, 1);
        var cross = rules.Get(0, 64)!;
        Assert.NotEmpty(cross.Name);
        Assert.Equal(6, cross.GetMaxLevel(swordMaster, 25));
        Assert.True(cross.CanLearn(swordMaster, 25, 3));
        Assert.False(cross.CanLearn(swordMaster, 25, 7));
        Assert.Equal(45, cross.GetSP(3));
        Assert.Equal(0, cross.GetTP(3));
        Assert.Null(rules.Get(0, 76)!.GetMaxLevel(swordMaster, 86));
        Assert.False(rules.Get(0, 76)!.CanLearn(swordMaster, 86, 1));

        var elementalMaster = new CharacterCareer(3, 1);
        var chain = rules.Get(3, 29)!;
        Assert.Equal(20, chain.GetMaxLevel(elementalMaster, 86));
        Assert.Equal(0, chain.GetMaxLevel(elementalMaster, 19));
        var tp = rules.Get(3, 157)!;
        Assert.Contains(new SkillPrerequisite(29, 5), tp.Prerequisites);
        Assert.False(tp.CanLearn(elementalMaster, 86, 1, new Dictionary<int, int> { [29] = 4 }));
        Assert.True(tp.CanLearn(elementalMaster, 86, 1, new Dictionary<int, int> { [29] = 5 }));
        Assert.Equal(0, tp.GetSP(3));
        Assert.Equal(3, tp.GetTP(3));
    }

    [SkippableFact]
    public void Unsupported_careers_are_not_inferred_from_a_scalar_maximum_or_initial_grant()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(new PvfContentOptions());
        var rules = new CharacterSkillRuleService(new PvfSkillPointDefinitionSource(provider));
        var invalid = new CharacterCareer(9, 1);
        Assert.Null(rules.Get(255, 1));
        Assert.Null(rules.Get(9, 118)!.GetMaxLevel(invalid, 86));
        Assert.False(rules.Get(9, 118)!.CanLearn(invalid, 86, 1));
        Assert.Throws<ArgumentException>(() => rules.GetLedger(invalid, 86, [], new(0, 0)));
    }

    [SkippableFact]
    public async Task Real_archive_distinguishes_inapplicable_skills_from_applicable_zero_level_caps()
    {
        Skip.IfNot(new PvfContentOptions().ResolveArchivePath() is not null, "PVF_ARCHIVE_PATH is not available.");
        using var provider = new PvfArchiveProvider(new PvfContentOptions());
        var source = new CharacterSkillRuleService(new PvfSkillPointDefinitionSource(provider));

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

