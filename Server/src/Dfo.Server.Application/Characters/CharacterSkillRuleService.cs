using System.Collections.Concurrent;
using System.Collections.Frozen;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Characters;

internal sealed class CharacterSkillRuleService(ISkillRuleDefinitionSource source)
    : ICharacterSkillRuleService, ISkillPointDefinitionSource
{
    private sealed record Rules(SkillRuleCatalog Content, IReadOnlyDictionary<int, CharacterSkillRule> Skills)
    {
        public IReadOnlyList<ICharacterSkillRule> All { get; } = Array.AsReadOnly(
            Skills.Values.OrderBy(rule => rule.SkillId).Cast<ICharacterSkillRule>().ToArray());
        public IReadOnlyList<CharacterSkillPointBudget> Budgets { get; } = Array.AsReadOnly(
            Enumerable.Range(0, 87).Select(level => new CharacterSkillPointBudget(
                Content.SpTable.Where(row => row.Level <= level).Sum(row => (long)row.Sp),
                // ServerS4A21 SkillPointLedger: TP is awarded from level 50, one per level.
                level < 50 ? 0 : level - 49)).ToArray());
    }
    private readonly ConcurrentDictionary<byte, Lazy<Rules>> jobs = new();

    private Rules For(byte job) => jobs.GetOrAdd(job, key => new(() =>
    {
        var content = source.Get(key);
        return new(content, content.Skills.ToFrozenDictionary(pair => pair.Key,
            pair => new CharacterSkillRule(key, pair.Value,
                content.FreeGrants.Where(grant => grant.SkillId == pair.Key), content.SupportedCareers)));
    })).Value;

    public ICharacterSkillRule? Get(byte job, int skillId) => For(job).Skills.GetValueOrDefault(skillId);

    public IReadOnlyList<ICharacterSkillRule> GetAll(byte job) => For(job).All;

    public CharacterSkillPointBudget GetTotalPoints(byte job, int characterLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characterLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(characterLevel, 86);
        var rules = For(job);
        if (rules.Content.SupportedCareers.Count == 0)
            throw new ArgumentException($"Unsupported skill job: {job}.", nameof(job));
        return rules.Budgets[characterLevel];
    }

    public CharacterSkillPointLedger GetLedger(CharacterCareer career, int characterLevel,
        IReadOnlyList<CharacterSkillPageState> pages, CharacterSkillPointBasisState basis) =>
        SkillPointLedgerRules.Calculate(Project(career, characterLevel,
            pages.SelectMany(page => page.Skills).Select(skill => (int)skill.SkillId)),
            characterLevel, pages, basis);

    public ValueTask<SkillPointDefinitionSet> GetAsync(byte job, byte packedGrowType, byte characterLevel,
        IReadOnlyCollection<int> learnedSkillIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Project(CharacterCareer.FromPacked(job, packedGrowType),
            characterLevel, learnedSkillIds));
    }

    private SkillPointDefinitionSet Project(CharacterCareer career, int level, IEnumerable<int> learnedIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        var job = For(career.Job);
        if (!job.Content.SupportedCareers.Contains(career))
            throw new ArgumentException($"Unsupported skill career: {career}.", nameof(career));
        var grants = job.Content.FreeGrants.Where(grant => Applies(grant, career))
            .GroupBy(grant => grant.SkillId)
            .Select(group => group.OrderByDescending(grant => grant.Level).First())
            .Select(grant => new SkillPointGrantDefinition(grant.SkillId,
                checked((byte)grant.Level), grant.IsInitial)).ToArray();
        var skills = learnedIds.Concat(grants.Select(grant => grant.SkillId)).Distinct()
            .Where(job.Skills.ContainsKey).ToFrozenDictionary(id => id, id => job.Skills[id].Project(career, level));
        var budget = GetTotalPoints(career.Job, level);
        return new(checked((int)budget.Sp), checked((int)budget.Tp), Array.AsReadOnly(grants), skills);
    }

    internal static bool Applies(SkillFreeGrant grant, CharacterCareer career) =>
        grant.IsInitial || grant.GrowType == career.GrowType && grant.Awakening <= career.Awakening;
}

internal sealed class CharacterSkillRule : ICharacterSkillRule
{
    private readonly SkillRuleDefinition definition;
    private readonly IReadOnlyList<SkillFreeGrant> grants;
    private readonly IReadOnlySet<CharacterCareer> careers;

    public CharacterSkillRule(byte job, SkillRuleDefinition definition, IEnumerable<SkillFreeGrant> grants,
        IEnumerable<CharacterCareer> careers)
    {
        Job = job;
        this.careers = careers.ToFrozenSet();
        this.definition = definition with
        {
            Costs = Array.AsReadOnly(definition.Costs.ToArray()),
            MaximumLevels = Array.AsReadOnly(definition.MaximumLevels.ToArray()),
            GrowTypeMaximumLevels = Array.AsReadOnly(definition.GrowTypeMaximumLevels.ToArray()),
            AwakeningMaximumLevels = Array.AsReadOnly(definition.AwakeningMaximumLevels.ToArray()),
            Prerequisites = Array.AsReadOnly(definition.Prerequisites.ToArray()),
        };
        this.grants = Array.AsReadOnly(grants.ToArray());
    }

    public byte Job { get; }
    public int SkillId => definition.SkillId;
    public string Name => definition.Name;
    public string Description => definition.Description;
    public IReadOnlyList<SkillPrerequisite> Prerequisites => definition.Prerequisites;

    private int StaticMaximum(CharacterCareer career)
    {
        if (career.Job != Job || !careers.Contains(career)) return 0;
        var maximum = At(definition.MaximumLevels, career.GrowType, scalar: true, fallback: 1);
        var direction = career.Awakening > 0
            ? At(definition.AwakeningMaximumLevels, career.GrowType * 2 + career.Awakening - 1) : 0;
        if (direction <= 0)
            direction = At(definition.GrowTypeMaximumLevels, career.GrowType, fallback: maximum);
        return Math.Max(0, Math.Min(maximum, direction));
    }

    public int? GetMaxLevel(CharacterCareer career, int characterLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characterLevel);
        var maximum = StaticMaximum(career);
        if (maximum == 0) return null;
        if (characterLevel < definition.RequiredLevel) return 0;
        return definition.RequiredLevel <= 0 ? maximum : Math.Min(maximum,
            1 + (characterLevel - definition.RequiredLevel) / Math.Max(1, definition.LevelInterval));
    }

    public bool CanLearn(CharacterCareer career, int characterLevel, int targetLevel,
        IReadOnlyDictionary<int, int>? learnedSkills = null) =>
        !definition.IsFixedLevel && targetLevel > 0 && GetMaxLevel(career, characterLevel) is { } maximum
        && targetLevel <= maximum && Prerequisites.All(required =>
            (learnedSkills?.GetValueOrDefault(required.SkillId) ?? 0) >= required.Level);

    public long GetSP(int skillLevel) => Cost(skillLevel, tp: false);
    public long GetTP(int skillLevel) => Cost(skillLevel, tp: true);

    private long Cost(int skillLevel, bool tp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skillLevel);
        if (definition.UsesTp != tp || definition.Costs.Count == 0) return 0;
        var count = Math.Min(skillLevel, definition.Costs.Count);
        var sum = 0L;
        for (var index = 0; index < count; index++) sum = checked(sum + definition.Costs[index]);
        return checked(sum + (long)(skillLevel - count) * definition.Costs[^1]);
    }

    public int GetFreeLevel(CharacterCareer career, int characterLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(characterLevel);
        if (career.Job != Job || !careers.Contains(career)) return 0;
        var granted = grants.Where(grant => CharacterSkillRuleService.Applies(grant, career))
            .Select(grant => grant.Level).DefaultIfEmpty().Max();
        if (!definition.IsFixedLevel || characterLevel < definition.RequiredLevel) return granted;
        // Reference ServerS4A21 2db3a135 SkillStaticData.GetFixedLevel. Missing base stays 0;
        // this is code-derived behavior, not client verification.
        var automatic = definition.FixedLevelBase
            + (long)(characterLevel - definition.RequiredLevel) / Math.Max(1, definition.FixedLevelInterval)
            * definition.FixedLevelAddPerInterval;
        return Math.Max(granted, (int)Math.Clamp(automatic, 0, StaticMaximum(career)));
    }

    internal SkillPointCostDefinition Project(CharacterCareer career, int level) => new(
        SkillId, definition.UsesTp, definition.Costs, definition.IsFixedLevel, definition.RequiredLevel,
        StaticMaximum(career), definition.FixedLevelBase, definition.FixedLevelInterval,
        definition.FixedLevelAddPerInterval, definition.IsActive, definition.RawGroup,
        definition.NumberOfGrowTypes, GetMaxLevel(career, level), this, career);

    // Old entry fixtures may supply the compatibility value directly. Production projections
    // always carry the shared Rule, and use exactly the same cost/free-level implementation.
    internal static CharacterSkillRule FromProjection(SkillPointCostDefinition value) => new(0,
        new(value.SkillId, string.Empty, string.Empty, value.UsesTp, value.Costs,
            [value.MaximumLevel], [], [], value.RequiredLevel, 1, [], value.IsFixedLevel,
            value.FixedLevelBase, value.FixedLevelInterval, value.FixedLevelAddPerInterval,
            value.IsActive, value.RawGroup, value.NumberOfGrowTypes), [], [new CharacterCareer(0)]);

    private static int At(IReadOnlyList<int> values, int index, bool scalar = false, int fallback = 0) =>
        values.Count == 0 ? fallback : scalar && values.Count == 1 ? values[0]
            : index < values.Count ? values[index] : 0;
}

internal static class SkillPointLedgerRules
{
    internal static int FreeLevel(SkillPointCostDefinition value, int granted, int level) =>
        Math.Max(granted, (value.Rule ?? CharacterSkillRule.FromProjection(value))
            .GetFreeLevel(value.Career, level));

    internal static CharacterSkillPointLedger Calculate(SkillPointDefinitionSet definitions, int level,
        IReadOnlyList<CharacterSkillPageState> pages, CharacterSkillPointBasisState basis)
    {
        var grants = definitions.FreeGrants.GroupBy(grant => grant.SkillId)
            .ToDictionary(group => group.Key, group => (int)group.Max(grant => grant.Level));
        var missing = new HashSet<int>();
        (long Sp, long Tp) Balance(byte pageIndex)
        {
            long sp = (long)definitions.TotalSp + basis.BonusSp;
            long tp = (long)definitions.TotalTp + basis.BonusTp;
            foreach (var learned in pages.SingleOrDefault(page => page.PageIndex == pageIndex)?.Skills ?? [])
            {
                var skillId = checked((int)learned.SkillId);
                if (!definitions.Skills.TryGetValue(skillId, out var value))
                {
                    // Preserve the stored fact and the legacy zero investment, but report the gap.
                    missing.Add(skillId);
                    continue;
                }
                var rule = value.Rule ?? CharacterSkillRule.FromProjection(value);
                var free = FreeLevel(value, grants.GetValueOrDefault(skillId), level);
                sp = checked(sp - Math.Max(0, rule.GetSP(learned.Level) - rule.GetSP(free)));
                tp = checked(tp - Math.Max(0, rule.GetTP(learned.Level) - rule.GetTP(free)));
            }
            return (sp, tp);
        }
        var first = Balance(0);
        var second = Balance(1);
        return new(first.Sp, second.Sp, first.Tp, second.Tp, missing);
    }
}
