using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Achievements;

internal sealed class AchievementRuleService(IAchievementDefinitionSource source) : IAchievementRuleService
{
    public IReadOnlyList<AchievementDefinition> Catalog() => source.Load();
    public bool IsSupported(AchievementDefinition d) => d.SupportedRestrictions && d.Condition.Count is > 0 and <= ushort.MaxValue && (d.Condition.Type switch
    {
        "hunt monster" or "huntmonster" => d.Condition.Values.Count == 4 && d.Condition.Values[3] is > 0 and <= ushort.MaxValue,
        "clear quest" or "clearquest" => d.Condition.Values.Count > 0 && d.Condition.Values.All(x => x > 0),
        "use item" or "useitem" => d.Condition.Values.Count == 3 && d.Condition.Values[0] == 1 && d.Condition.Values[1] > 0 && d.Condition.Values[2] == 1,
        "condition under clear" or "conditionunderclear" => d.Condition.SubType == 6 && d.Condition.Values.Count == 3 && d.Condition.Values[2] is > 0 and <= ushort.MaxValue,
        _ => false,
    });
    public int InitialRemaining(AchievementDefinition d)
    {
        var c = d.Condition;
        var amount = c.Type switch
        {
            "hunt monster" or "huntmonster" when c.Values.Count == 4 => c.Values[3],
            "condition under clear" or "conditionunderclear" when c.SubType == 6 && c.Values.Count == 3 => c.Values[2],
            _ => c.Count,
        };
        // The native achievement list has u16 counters. Unsupported/malformed rules remain blocked.
        return Math.Clamp(amount, 1, ushort.MaxValue);
    }
    public int Contribution(AchievementDefinition d, CharacterGameplayFact f)
    {
        if (!IsSupported(d) || f.Count <= 0) return 0;
        var c = d.Condition; var v = c.Values;
        var matches = c.Type switch
        {
            "hunt monster" or "huntmonster" => f.Kind == "monster.defeated"
                && (v[0] < 0 || v[0] == f.DungeonId) && (v[1] < 0 || f.Difficulty >= v[1]) && (v[2] < 0 || v[2] == f.SubjectId),
            "use item" or "useitem" => f.Kind is "item.used" or "material.consumed" && v[1] == f.SubjectId,
            "condition under clear" or "conditionunderclear" => f.Kind == "dungeon.cleared"
                && (v[0] < 0 || v[0] == f.DungeonId) && (v[1] < 0 || f.Difficulty >= v[1]),
            _ => false,
        };
        return matches ? (int)Math.Min(ushort.MaxValue, (long)f.Count * (c.Type is "use item" or "useitem" ? v[2] : 1)) : 0;
    }
}

internal sealed class TitleBookRuleService(ITitleBookDefinitionSource source, IPackageOpeningDefinitionSource items) : ITitleBookRuleService
{
    public IReadOnlyList<TitleBookCategoryDefinition> Catalog() => source.Load();
    public ValueTask<OpeningRewardItemDefinition?> RewardTemplateAsync(int itemId, CancellationToken ct) => items.GetRewardAsync(itemId, ct);
}
