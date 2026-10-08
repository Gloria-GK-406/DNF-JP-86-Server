using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Achievements;

internal sealed class AchievementRuleService(IAchievementDefinitionSource source,
    IItemCatalogSource items, ICreatureRuleService creatures) : IAchievementRuleService
{
    public bool CanCheckSnapshot(AchievementDefinition d) => d.SupportedRestrictions && d.Condition.Count == 1 && (d.Condition.Type switch
    {
        "level up" => d.Condition.Values.Count == 1 && d.Condition.Values[0] > 0,
        "first grow" or "awakening" => d.Condition.Values.Count == 0,
        "clear quest" or "clearquest" => d.Condition.Values.Count > 0 && d.Condition.Values.All(x => x > 0),
        "seeking" => d.Condition.Values.Count > 0 && d.Condition.Values.Count % 2 == 0
            && (d.Condition.Values.Where((_, i) => i % 2 == 0).All(x => x > 0)
                || d.Condition.Values.Count == 2 && d.Condition.Values[0] == -7 && d.Condition.SubType is -1 or 25)
            && d.Condition.Values.Where((_, i) => i % 2 == 1).All(x => x > 0),
        "equipped item" => d.Condition.SubType == 0 && d.EquipmentSlots.Count > 0
            && d.Condition.Values.Count == d.EquipmentSlots.Count * 2 && d.EquipmentSlots.All(x => EquipmentSlot(x) is not null),
        _ => false,
    });
    public async ValueTask<bool?> IsSatisfiedAsync(AchievementDefinition d, AchievementCharacterFacts f, CancellationToken ct)
    {
        if (!CanCheckSnapshot(d)) return null;
        if (d.RequiresHardcore is { } mode && f.Hardcore != mode) return f.Hardcore is null ? null : false;
        var c = d.Condition;
        switch (c.Type)
        {
            case "level up": return f.Level >= c.Values[0];
            case "first grow": return f.Career is { } career ? career.GrowType > 0 : null;
            case "awakening": return f.Career is { } awakened ? awakened.Awakening > 0 : null;
            case "clear quest": case "clearquest": return c.Values.All(f.ClearedQuests.Contains);
            case "seeking":
                if (c.Values[0] == -7)
                {
                    if (c.SubType != 25) return f.AvatarCount is { } count ? count >= c.Values[1] : null;
                    var pets = 0;
                    foreach (var item in f.CreatureItems)
                    {
                        var rule = await creatures.GetAsync(item, ct);
                        if (rule is null) return null;
                        if (!rule.IsEgg) pets++;
                    }
                    return pets >= c.Values[1];
                }
                for (var i = 0; i < c.Values.Count; i += 2)
                    if (f.ItemCounts.GetValueOrDefault(c.Values[i]) < c.Values[i + 1]) return false;
                return true;
            case "equipped item":
                for (var i = 0; i < d.EquipmentSlots.Count; i++)
                {
                    var equipped = f.EquippedItems.FirstOrDefault(x => x.Slot == new BusinessSlot(BusinessInventorySpace.WornEquipment, EquipmentSlot(d.EquipmentSlots[i])!.Value));
                    if (equipped is null) return false;
                    var entry = await items.FindAsync(equipped.ItemId, ct);
                    if (entry?.Rarity is not { } rarity || entry.Level is not { } level) return null;
                    // PVF rarity 5 is Chronicle, explicitly excluded by these title rules.
                    if (rarity == 5 || rarity < c.Values[2 * i] || level < c.Values[2 * i + 1]) return false;
                }
                return true;
            default: return null;
        }
    }
    private static int? EquipmentSlot(string name) => name switch
    {
        "weapon" => 0, "coat" => 2, "shoulder" => 3, "pants" => 4, "shoes" => 5, "waist" => 6,
        "amulet" => 7, "wrist" => 8, "ring" => 9, "support" => 10, "magic stone" => 11, _ => null,
    };
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
    public IReadOnlySet<int> ActivatedAchievements(IReadOnlySet<(int Category, int Index)> unlockedSlots) =>
        Catalog().SelectMany(x => x.Slots).Where(x => x.SlotType >= 0 && x.QuestId > 0)
            .GroupBy(x => x.QuestId).Where(g => g.All(s => unlockedSlots.Contains((s.Category, s.Index))))
            .Select(g => g.Key).ToHashSet();
    public ValueTask<OpeningRewardItemDefinition?> RewardTemplateAsync(int itemId, CancellationToken ct) => items.GetRewardAsync(itemId, ct);
}
