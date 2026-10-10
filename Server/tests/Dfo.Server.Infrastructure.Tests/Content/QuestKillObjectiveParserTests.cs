using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class QuestKillObjectiveParserTests
{
    [Fact]
    public void Five_fields_are_routed_by_type_and_scope_not_length()
    {
        var region = QuestKillObjectiveParser.Parse("hunt monster", [-2, 31, -1, -3, 5]);
        var actor = QuestKillObjectiveParser.Parse("hunt enemy", [144, 2, 13099, 3, 5]);
        Assert.Equal(new QuestKillScope(QuestKillScopeKind.WorldMap, 31), Assert.Single(region.Targets).Scope);
        Assert.Equal(new QuestKillTarget(QuestKillTargetKind.BossMonster, -3, 1), region.Targets[0].Target);
        Assert.Equal(new QuestKillScope(QuestKillScopeKind.Dungeon, 144), Assert.Single(actor.Targets).Scope);
        Assert.Equal(new QuestKillTarget(QuestKillTargetKind.Exact, 13099, 3), actor.Targets[0].Target);
        Assert.Equal(5u, region.InitialTrigger);
        Assert.Equal(5u, actor.InitialTrigger);
        Assert.Equal(QuestKillParseStatus.Invalid,
            QuestKillObjectiveParser.Parse("hunt monster", [144, 2, 13099, 3, 5]).Status);
    }

    [Fact]
    public void Three_targets_keep_distinct_channels_and_generate_the_same_initial_trigger()
    {
        var parsed = QuestKillObjectiveParser.Parse("hunt monster",
            [144, 0, 1, 2, -1, -1, -3, 3, -3, 2, 4, 5]);
        Assert.Equal(QuestKillParseStatus.Parsed, parsed.Status);
        Assert.Equal([0, 1, 2], parsed.Targets.Select(target => target.ChannelIndex));
        Assert.Equal([2, 3, 5], parsed.Targets.Select(target => target.RequiredCount));
        Assert.Equal(2u | (3u << 9) | (5u << 18), parsed.InitialTrigger);
        Assert.Equal(QuestKillScopeKind.RecommendedDungeon, parsed.Targets[2].Scope.Kind);
    }

    [Theory]
    [InlineData(new int[] { })]
    [InlineData(new int[] { 144, 0, 1 })]
    [InlineData(new int[] { 144, 0, 1, 2, 144 })]
    [InlineData(new int[] { -2, 31, -1, -3 })]
    [InlineData(new int[] { -2, 31, -1, -3, 0 })]
    [InlineData(new int[] { -2, 31, -1, -3, 512 })]
    [InlineData(new int[] { -2, 31, -2, -3, 5 })]
    [InlineData(new int[] { -2, 0, -1, -3, 5 })]
    [InlineData(new int[] { -2, 31, -1, -3, 5, 144, 2, -1, -3, 5 })]
    public void Malformed_kill_declarations_never_fall_back_to_partial_four_field_targets(int[] values)
    {
        var result = QuestKillObjectiveParser.Parse("hunt monster", values);
        Assert.Equal(QuestKillParseStatus.Invalid, result.Status);
        Assert.Empty(result.Targets);
        Assert.Null(result.InitialTrigger);
        Assert.NotEmpty(result.Diagnostic!);
        Assert.Equal(values, result.Operands);
    }

    [Theory]
    [InlineData(-4)]
    [InlineData(-5)]
    [InlineData(-11)]
    public void Unresolved_selectors_retain_operands_and_counts_without_claiming_matching_semantics(int selector)
    {
        var result = QuestKillObjectiveParser.Parse("hunt monster", [1, -1, selector, 20]);
        Assert.Equal(QuestKillParseStatus.Unsupported, result.Status);
        Assert.Equal(QuestKillTargetKind.Unresolved, Assert.Single(result.Targets).Target.Kind);
        Assert.Equal(selector, result.Targets[0].Target.Code);
        Assert.Equal(20u, result.InitialTrigger);
    }

    [Fact]
    public void Counts_use_the_full_nine_bit_channel_without_truncation_and_operands_are_owned()
    {
        int[] operands = [1, -1, 100, 511, 2, -1, 200, 511, 3, -1, 300, 511];
        var result = QuestKillObjectiveParser.Parse("hunt monster", operands);
        operands[3] = 1;
        Assert.Equal(QuestKillParseStatus.Parsed, result.Status);
        Assert.Equal(0x7FFFFFFu, result.InitialTrigger);
        Assert.Equal(511, result.Operands[3]);
        Assert.All(result.Targets, target => Assert.Equal(511, target.RequiredCount));
    }

    [Fact]
    public void Actor_family_and_difficulty_policy_are_not_confused_with_monster_rank()
    {
        var monster = QuestKillObjectiveParser.Parse("hunt monster", [144, 1, -3, 5]);
        var actor = QuestKillObjectiveParser.Parse("hunt enemy", [144, 1, 100, 0, 5]);
        Assert.Equal(QuestKillDifficultyMatch.Minimum, Assert.Single(monster.Targets).DifficultyMatch);
        Assert.Equal(QuestKillDifficultyMatch.Exact, Assert.Single(actor.Targets).DifficultyMatch);
        Assert.Equal(QuestKillParseStatus.Unsupported, actor.Status);
        Assert.Equal(0, actor.Targets[0].Target.EnemyType);
    }

    [Fact]
    public void Unrelated_objectives_are_not_interpreted_as_kill_operands() =>
        Assert.Equal(QuestKillParseStatus.NotApplicable,
            QuestKillObjectiveParser.Parse("seeking", [-2, 31, -1, -3, 5]).Status);
}
