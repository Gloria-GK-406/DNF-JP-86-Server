using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Application.Creatures;

namespace Dfo.Server.Application.Tests.Creatures;

[Trait("Category", "Unit")]
public sealed class CreatureRulesTests
{
    private static readonly CreatureRule Rule = new(
        TemplateId: 63000,
        CreatureId: 1,
        MaxLevel: 3,
        HungerEnabled: true,
        ExperienceThresholds: [2, 5, 9],
        Evolutions: [],
        HatchOutputTemplateId: null,
        IsEgg: false);

    [Fact]
    public void Combat_consumes_one_point_per_minute_and_rest_recovers_one_per_six_minutes()
    {
        Assert.Equal(98.5, CreatureRules.AdvanceStomach(100, TimeSpan.FromSeconds(90), combat: true));
        Assert.Equal(99, CreatureRules.AdvanceStomach(98.5, TimeSpan.FromMinutes(3), combat: false));
        Assert.Equal(0, CreatureRules.AdvanceStomach(1, TimeSpan.FromMinutes(2), combat: true));
        Assert.Equal(100, CreatureRules.AdvanceStomach(99, TimeSpan.FromMinutes(12), combat: false));
        Assert.Equal(100, CreatureRules.AdvanceStomach(0, TimeSpan.FromHours(1), combat: true, hungerEnabled: false));
        var first = CreatureRules.AdvanceStomach(1, TimeSpan.FromSeconds(59), combat: true);
        Assert.Equal(0, CreatureRules.AdvanceStomach(first, TimeSpan.FromSeconds(1), combat: true));
    }

    [Fact]
    public void Stomach_addition_caps_at_one_hundred()
    {
        Assert.Equal(50, CreatureRules.AddStomach(20, 30));
        Assert.Equal(100, CreatureRules.AddStomach(90, 30));
    }

    [Fact]
    public void Experience_uses_archive_thresholds_and_caps_at_definition_maximum()
    {
        Assert.Equal(1, CreatureRules.LevelFor(Rule, 1));
        Assert.Equal(2, CreatureRules.LevelFor(Rule, 2));
        Assert.Equal(3, CreatureRules.LevelFor(Rule, 5));

        Assert.Equal(new CreatureExperienceResult(2, 2, true), CreatureRules.GainExperience(Rule, 1, 1));
        Assert.Equal(new CreatureExperienceResult(6, 3, true), CreatureRules.GainExperience(Rule, 2, 4));
        Assert.Equal(new CreatureExperienceResult(6, 3, false), CreatureRules.GainExperience(Rule, 6, 4));
    }

    [Fact]
    public void Artifact_percentages_apply_once_to_the_fatigue_base_before_truncation()
    {
        Assert.Equal(2, CreatureRules.CalculateExperienceGain(2, 10, 20, 15));
        Assert.Equal(29, CreatureRules.CalculateExperienceGain(20, 10, 20, 15));
    }
}
