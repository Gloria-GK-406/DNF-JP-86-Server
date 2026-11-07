using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

/// <summary>Reads growth-weapon content and answers what kills and days do to a piece.</summary>
/// <remarks>
/// A21 evidence and the decisions taken where it is silent are in
/// <c>Document/tasks/dragon-weapon/research-and-design.md</c>. In short: the client never reads
/// <c>[exp acquire]</c>, so the server computes the gain; <c>[kind]</c> 1..4 is taken as the client's
/// monster rank plus one, and the six <c>[level correction]</c> values, whose meaning nothing shows,
/// are not applied.
/// </remarks>
internal sealed class EquipmentGrowthRuleService(IEquipmentEmancipateDefinitionSource emancipation,
    IEquipmentGrowthDefinitionSource growth) : IEquipmentGrowthRuleService
{
    public async ValueTask<EquipmentGrowthRule?> ForAsync(int templateId, CancellationToken ct)
    {
        if (templateId <= 0) return null;
        var block = (await emancipation.GetEquipmentAsync(templateId, ct).ConfigureAwait(false))
            .FirstOrDefault(x => x.Growth is not null);
        if (block?.Growth is not { } position) return null;
        var group = await growth.GetGroupAsync(position.Group, ct).ConfigureAwait(false);
        var star = group?.Stars.FirstOrDefault(x => x.Star == position.Star);
        if (group is null || star is null) return null;
        int? next = block.Outputs is [{ Count: 1 } output] && position.Star < group.MaxLevel ? output.ItemId : null;
        var fatigue = star.Penalties.FirstOrDefault(x => x.Kind == "fatigue");
        return new(templateId, position, group.MaxLevel, star.MaxExperience, star.GrowsByExperience, next,
            fatigue?.Threshold ?? 0, fatigue?.Percent ?? 0,
            group.Acquisitions.FirstOrDefault(x => x.Event == "death"));
    }

    public int KillExperience(EquipmentGrowthRule rule, MonsterRank rank, DungeonDifficulty difficulty, DungeonRunKind runKind) =>
        GrowsIn(runKind) ? EquipmentGrowthRules.KillExperience(rule, rank, difficulty) : 0;

    public int ExperienceAfterDays(EquipmentGrowthRule rule, int experience, int lastDayMaintained, int daysElapsed) =>
        EquipmentGrowthRules.ExperienceAfterDays(rule, experience, lastDayMaintained, daysElapsed);

    public byte MaintainedFatigue(EquipmentGrowthRule rule, int consumedToday) =>
        (byte)Math.Clamp(rule.FatigueThreshold > 0 ? Math.Min(consumedToday, rule.FatigueThreshold) : consumedToday, 0, byte.MaxValue);

    // The weapons' own text: growth is "invalid in the Valley of the Dead" (SAO).
    private static bool GrowsIn(DungeonRunKind kind) => kind switch
    {
        DungeonRunKind.Sao => false,
        DungeonRunKind.Ordinary or DungeonRunKind.Dimension or DungeonRunKind.Hell or DungeonRunKind.Mirror
            or DungeonRunKind.Despair or DungeonRunKind.Tournament or DungeonRunKind.Altar or DungeonRunKind.Guardian
            or DungeonRunKind.GiantDragon or DungeonRunKind.SouthernDale or DungeonRunKind.TauKingdom
            or DungeonRunKind.SealedTreasure or DungeonRunKind.Heras or DungeonRunKind.GoldSilverHorn
            or DungeonRunKind.Couple or DungeonRunKind.LostLand or DungeonRunKind.DarkElfRuins => true,
    };
}

internal static class EquipmentGrowthRules
{
    /// <summary><c>point × kind% × difficulty%</c>, floored; kind is the monster rank plus one.</summary>
    public static int KillExperience(EquipmentGrowthRule rule, MonsterRank rank, DungeonDifficulty difficulty)
    {
        if (rule.Kill is not { } kill || rule.IsLastStar) return 0;
        if (!kill.KindRates.TryGetValue((int)rank + 1, out var kindRate)) return 0;
        var index = (int)difficulty;
        var difficultyRate = index >= 0 && index < kill.DifficultyCorrection.Count ? kill.DifficultyCorrection[index] : 100;
        return (int)Math.Max(0, (long)kill.Point * kindRate * difficultyRate / 10000);
    }

    /// <summary>
    /// The last settled day loses its share if it fell short; every whole day after it, on which
    /// nothing was spent, loses it again.
    /// </summary>
    public static int ExperienceAfterDays(EquipmentGrowthRule rule, int experience, int lastDayMaintained, int daysElapsed)
    {
        if (daysElapsed <= 0 || rule.FatigueThreshold <= 0 || rule.FatiguePenaltyPercent <= 0 || rule.IsLastStar)
            return experience;
        var penalties = (lastDayMaintained < rule.FatigueThreshold ? 1 : 0) + (daysElapsed - 1);
        long left = experience;
        for (var i = 0; i < penalties && left > 0; i++) left = left * (100 - rule.FatiguePenaltyPercent) / 100;
        return (int)left;
    }
}
