using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Pvp;
using Dfo.Server.Application.Tests.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Pvp;

/// <summary>
/// The settlement decision table, run against the A21 archive's grade table so a misread of
/// <c>etc/pvp_ref.etc</c> shows here rather than at the first real battle.
/// </summary>
public sealed class PvpGradeRulesTests
{
    private static readonly PvpChannelEntitlements Ranked = new(true, true, 50, 10);
    private static readonly PvpChannelEntitlements Unrecorded = new(false, false, 0, 0);

    [SkippableTheory]
    [InlineData(0L, 12000L)]
    [InlineData(5000L, 7000L)]
    public void A_first_counted_win_at_grade_0_places_the_fighter_at_the_bottom_of_grade_1(long experience, long change)
    {
        var decision = Decide(Facts(experience, PvpOutcome.Win));

        Assert.Equal(new PvpSettlementDecision(1, 12000, change, true), decision);
    }

    [SkippableFact]
    public void A_record_without_experience_starts_at_its_grade_lower_bound()
    {
        var decision = Decide(Facts(null, PvpOutcome.Win) with { Grade = 5 });

        Assert.Equal(new PvpSettlementDecision(5, 126000, 1000, true), decision);
    }

    [SkippableTheory]
    [InlineData(12000L, 1, 13000L, 1)]
    [InlineData(26500L, 1, 27500L, 2)]
    [InlineData(5648500L, 19, 5649500L, 20)]
    public void A_counted_win_below_the_top_grade_adds_that_grade_s_win_experience(long experience, byte before,
        long after, byte grade)
    {
        Assert.Equal(before, Rules().GradeOf(experience));
        Assert.Equal(new PvpSettlementDecision(grade, after, 1000, true), Decide(Facts(experience, PvpOutcome.Win)));
    }

    [SkippableFact]
    public void A_win_at_the_top_grade_adds_nothing_but_a_loss_still_costs()
    {
        Assert.Equal(new PvpSettlementDecision(20, 5700000, 0, true), Decide(Facts(5700000, PvpOutcome.Win)));
        Assert.Equal(new PvpSettlementDecision(20, 5698924, -1076, true), Decide(Facts(5700000, PvpOutcome.Loss)));
        Assert.Equal(new PvpSettlementDecision(19, 5647924, -1076, true), Decide(Facts(5649000, PvpOutcome.Loss)));
    }

    [SkippableTheory]
    [InlineData(49, 1000L)]
    [InlineData(50, 0L)]
    [InlineData(80, 0L)]
    public void Only_the_first_fifty_wins_of_a_game_day_earn_experience(int earlier, long change)
    {
        var decision = Decide(Facts(13000, PvpOutcome.Win) with { WinsEarlierToday = earlier });

        Assert.Equal(new PvpSettlementDecision(1, 13000 + change, change, true), decision);
    }

    [SkippableFact]
    public void The_fifty_first_win_at_grade_0_does_not_promote()
    {
        var decision = Decide(Facts(0, PvpOutcome.Win) with { WinsEarlierToday = 50 });

        Assert.Equal(new PvpSettlementDecision(0, 0, 0, true), decision);
    }

    [SkippableTheory]
    [InlineData(9, -429L)]
    [InlineData(10, 0L)]
    public void Only_the_first_ten_losses_of_a_game_day_cost_experience(int earlier, long change)
    {
        var decision = Decide(Facts(13000, PvpOutcome.Loss) with { LossesEarlierToday = earlier });

        Assert.Equal(new PvpSettlementDecision(1, 13000 + change, change, true), decision);
    }

    [SkippableFact]
    public void A_loss_can_drop_a_grade_and_grade_0_loses_nothing()
    {
        Assert.Equal(new PvpSettlementDecision(0, 11571, -429, true), Decide(Facts(12000, PvpOutcome.Loss)));
        Assert.Equal(new PvpSettlementDecision(0, 300, 0, true), Decide(Facts(300, PvpOutcome.Loss)));
    }

    [SkippableTheory]
    [InlineData(PvpOutcome.Win)]
    [InlineData(PvpOutcome.Draw)]
    [InlineData(PvpOutcome.Loss)]
    public void An_escape_settles_as_a_loss(PvpOutcome outcome)
    {
        var decision = Decide(Facts(13000, outcome) with { Escaped = true });

        Assert.Equal(new PvpSettlementDecision(1, 12571, -429, true), decision);
    }

    [SkippableFact]
    public void A_draw_changes_nothing_and_is_not_counted()
    {
        Assert.Equal(new PvpSettlementDecision(1, 13000, 0, false), Decide(Facts(13000, PvpOutcome.Draw)));
    }

    [SkippableTheory]
    [InlineData(PvpOutcome.Win)]
    [InlineData(PvpOutcome.Loss)]
    public void A_channel_that_does_not_record_changes_nothing(PvpOutcome outcome)
    {
        var decision = Decide(Facts(13000, outcome) with { Entitlements = Unrecorded });

        Assert.Equal(new PvpSettlementDecision(1, 13000, 0, false), decision);
    }

    [SkippableFact]
    public void Grades_follow_the_archive_thresholds()
    {
        var rules = Rules();
        Assert.Equal(0, rules.GradeOf(0));
        Assert.Equal(0, rules.GradeOf(11999));
        Assert.Equal(1, rules.GradeOf(12000));
        Assert.Equal(19, rules.GradeOf(5648999));
        Assert.Equal(20, rules.GradeOf(5649000));
        Assert.Equal(20, rules.GradeOf(long.MaxValue));
        Assert.Equal(709000, rules.LowerBound(10));
        Assert.Equal(949000, rules.UpperBound(10));
        Assert.Equal(9999999, rules.UpperBound(20));
        Assert.Equal(20, rules.MaximumGrade);
        Assert.Throws<ArgumentOutOfRangeException>(() => rules.LowerBound(21));
        Assert.Throws<ArgumentOutOfRangeException>(() => rules.GradeOf(-1));
    }

    // The archive's per-grade experience is never large enough to cross two grades in one battle,
    // so the re-grading by accumulated experience and the zero floor are proven on a steeper table.
    [Fact]
    public void Grade_is_recomputed_from_experience_and_can_cross_several_grades()
    {
        var decision = PvpGradeRules.Decide(Steep, Facts(150, PvpOutcome.Win));

        Assert.Equal(new PvpSettlementDecision(3, 650, 500, true), decision);
        Assert.Equal(new PvpSettlementDecision(0, 50, -500, true),
            PvpGradeRules.Decide(Steep, Facts(550, PvpOutcome.Loss)));
    }

    [Fact]
    public void A_stored_grade_above_the_top_without_experience_starts_at_the_top_grade_s_lower_bound()
    {
        Assert.Equal(new PvpSettlementDecision(3, 600, 0, true),
            PvpGradeRules.Decide(Steep, Facts(null, PvpOutcome.Win) with { Grade = 25 }));
        Assert.Equal(new PvpSettlementDecision(1, 100, -500, true),
            PvpGradeRules.Decide(Steep, Facts(null, PvpOutcome.Loss) with { Grade = 25 }));
    }

    [Fact]
    public void Experience_never_falls_below_zero()
    {
        var decision = PvpGradeRules.Decide(Steep, Facts(120, PvpOutcome.Loss));

        Assert.Equal(new PvpSettlementDecision(0, 0, -120, true), decision);
    }

    private static readonly PvpGradeCatalog Steep = new(
    [
        new(0, 0, 100, 500, 0),
        new(1, 100, 200, 500, -500),
        new(2, 200, 600, 500, -500),
        new(3, 600, 1000, 500, -500),
    ], 3, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(2));

    private static PvpSettlementFacts Facts(long? experience, PvpOutcome outcome) =>
        new(0, experience, outcome, false, 0, 0, Ranked);

    private static PvpSettlementDecision Decide(PvpSettlementFacts facts) => Rules().Decide(facts);

    internal static IPvpSettlementRuleService Rules()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        return new PvpSettlementRuleService(Archive.Value.GetRequiredService<IPvpGradeDefinitionSource>(),
            PvpSettlementRuleServiceTests.Days, new PvpMatchmakingRuleService());
    }

    private static readonly Lazy<ServiceProvider> Archive = new(ContentArchive.CreateContentProvider);
}
