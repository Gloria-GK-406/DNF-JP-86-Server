namespace Dfo.Server.Data.Inventory;

/// <summary>One tooltip line: a Chinese label and its pre-formatted static value ("186 ~ 201", "+15", "+8.0%").</summary>
public sealed record ItemTooltipStat(string Label, string Value);

/// <summary>
/// Static PVF facts for a game-style item tooltip. No instance state: quality, reinforcement, amplification and
/// forging bonuses are not applied. <see cref="Jobs"/> null means every job; <see cref="Attach"/> is the raw
/// <c>[attach type]</c> key without brackets; <see cref="Weight"/> is in kilograms; <see cref="Price"/> is the
/// declared <c>[value]</c> shown as the sell price.
/// </summary>
public sealed record ItemTooltip(int ItemId, string Name, int? Rarity, string Category, string TypeName,
    int? MinimumLevel, int? MaximumLevel, IReadOnlyList<string>? Jobs, string? Attach, int? PeriodDays, bool DailyDelete,
    IReadOnlyList<ItemTooltipStat> Stats, string? Explain, string? Flavor, int? Durability, double? Weight, int? Price, int? StackLimit);
