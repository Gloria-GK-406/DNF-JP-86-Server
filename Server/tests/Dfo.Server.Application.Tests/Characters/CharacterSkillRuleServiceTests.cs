using System.Collections.Frozen;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Characters;

[Trait("Category", "Unit")]
public sealed class CharacterSkillRuleServiceTests
{
    private static readonly CharacterCareer Career = new(0, 1, 1);

    [Fact]
    public void Career_caps_keep_zero_and_use_positive_awakening_before_grow_fallback()
    {
        var definition = Definition(1) with
        {
            MaximumLevels = [0, 30], GrowTypeMaximumLevels = [20, 10],
            AwakeningMaximumLevels = [0, 0, 25, 0], RequiredLevel = 20, LevelInterval = 3,
        };
        var rule = Service([definition]).Get(0, 1)!;
        Assert.Null(rule.GetMaxLevel(new(0), 86));
        Assert.Equal(0, rule.GetMaxLevel(Career, 19));
        Assert.Equal(23, rule.GetMaxLevel(Career, 86));
        Assert.Equal(10, rule.GetMaxLevel(new(0, 1, 2), 86));
        Assert.False(rule.CanLearn(Career, 86, 0));
    }

    [Fact]
    public void Cumulative_cost_repeats_the_last_price_and_ledger_subtracts_the_highest_free_baseline()
    {
        var definition = Definition(1) with
        {
            Costs = [10, 20, 30], IsFixedLevel = true, RequiredLevel = 10,
            FixedLevelBase = 0, FixedLevelInterval = 5, FixedLevelAddPerInterval = 1,
        };
        var rules = Service([definition], [new(1, 1, -1, 0), new(1, 2, 1, 0), new(1, 3, 1, 1)]);
        var skill = rules.Get(0, 1)!;
        Assert.Equal(0, skill.GetSP(0));
        Assert.Equal(120, skill.GetSP(5));
        Assert.Equal(3, skill.GetFreeLevel(Career, 25));
        Assert.Equal(4, skill.GetFreeLevel(Career, 30));
        Assert.False(skill.CanLearn(Career, 30, 5));
        var ledger = rules.GetLedger(Career, 30, Pages((0, 1, 5), (1, 1, 2)), new(7, 0));
        Assert.Equal(107 - (120 - 90), ledger.For(new(0)).Sp);
        Assert.Equal(107, ledger.For(new(1)).Sp);
        Assert.True(ledger.IsComplete);
    }

    [Fact]
    public void Ledger_keeps_signed_wide_balances_and_reports_unknown_facts_without_deleting_them()
    {
        var rules = Service([Definition(1) with { Costs = [int.MaxValue] }]);
        var pages = Pages((0, 1, 3), (0, 999, 4));
        var ledger = rules.GetLedger(Career, 50, pages, new(int.MaxValue, int.MaxValue));
        Assert.Equal(100L + int.MaxValue - 3L * int.MaxValue, ledger.For(new(0)).Sp);
        Assert.Equal(100L + int.MaxValue, ledger.For(new(1)).Sp);
        Assert.Equal(1L + int.MaxValue, ledger.For(new(0)).Tp);
        Assert.False(ledger.IsComplete);
        Assert.Equal([999], ledger.MissingSkillIds);
        Assert.Equal(2, pages[0].Skills.Count);
        Assert.Equal(new CharacterSkillProtocolMirrorState(0, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue),
            ledger.ToProtocolMirror());
    }

    [Theory]
    [InlineData(49, 0)]
    [InlineData(50, 1)]
    [InlineData(86, 37)]
    public void Tp_awards_and_sp_investments_are_separate(int level, int totalTp)
    {
        var rules = Service([Definition(1) with { UsesTp = true, Costs = [1, 2] }]);
        var ledger = rules.GetLedger(Career, level, Pages((0, 1, 3)), new(0, 4));
        Assert.Equal(100, ledger.For(new(0)).Sp);
        Assert.Equal(totalTp + 4 - 5, ledger.For(new(0)).Tp);
        Assert.Equal(totalTp + 4, ledger.For(new(1)).Tp);
    }

    [Fact]
    public void Shared_rule_prerequisites_cannot_be_changed_by_a_caller()
    {
        var requirements = new[] { new SkillPrerequisite(2, 5) };
        var rules = Service([Definition(1) with { Prerequisites = requirements }]);
        var rule = rules.Get(0, 1)!;
        requirements[0] = new(2, 0);
        Assert.False(rule.CanLearn(Career, 86, 1));
        Assert.Throws<NotSupportedException>(() => ((IList<SkillPrerequisite>)rule.Prerequisites)[0] = new(2, 0));
        Assert.True(rule.CanLearn(Career, 86, 1, new Dictionary<int, int> { [2] = 5 }));
    }

    [Fact]
    public void Existing_balance_projection_logs_missing_cost_definitions()
    {
        var logger = new LedgerLogger();
        var rules = new CharacterEntryPreparationRuleService(Service([]),
            new StubExpertContractDefinitionSource(), new StubCreatureEquipmentLifetimeSource(), logger);
        var mirror = rules.RemainingPoints(new(100, 0, [], new Dictionary<int, SkillPointCostDefinition>()),
            20, Pages((0, 999, 3)), new(0, 0));
        Assert.Equal((ushort)100, mirror.Page0Sp);
        Assert.Contains(logger.Warnings, message => message.Contains("999", StringComparison.Ordinal));
    }

    private static CharacterSkillRuleService Service(SkillRuleDefinition[] definitions, SkillFreeGrant[]? grants = null) =>
        new(new Source(new(definitions.ToFrozenDictionary(value => value.SkillId),
            grants ?? [], [(1, 100)], new[] { new CharacterCareer(0), new(0, 1), Career, new(0, 1, 2) }.ToFrozenSet())));

    private static SkillRuleDefinition Definition(int id) => new(id, "skill", "description", false,
        [10], [100], [], [], 1, 1, [], false, 0, 1, 1, false, 0, 0);

    private static IReadOnlyList<CharacterSkillPageState> Pages(params (byte Page, ushort Id, byte Level)[] values) =>
        values.GroupBy(value => value.Page).Select(group => new CharacterSkillPageState(group.Key,
            group.Select((value, slot) => new CharacterSkillState((short)slot, value.Id, value.Level, [])).ToArray())).ToArray();

    private sealed class Source(SkillRuleCatalog catalog) : ISkillRuleDefinitionSource
    {
        public SkillRuleCatalog Get(byte job) => catalog;
    }

    private sealed class LedgerLogger : ILogger<CharacterEntryPreparationRuleService>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }
}
