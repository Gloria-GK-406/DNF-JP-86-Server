namespace Dfo.Server.Abstractions.Content;

/// <summary>How many challenge slots a level band opens.</summary>
public readonly record struct DailyActivitySlotAllowance(LevelRange Level, int SlotCount);

/// <summary>One challenge slot and the quests that can occupy it.</summary>
public sealed record DailyActivitySlot(int SlotIndex, IReadOnlyList<int> QuestIds);

/// <summary>A reward granted for a completion count inside a level band.</summary>
public readonly record struct DailyActivityReward(
    LevelRange Level,
    int ItemTemplateId,
    int Count);

/// <summary>
/// A daily-activity group as the content defines it: which levels it serves, how many
/// slots those levels open, which quests can fill each slot, and what completing them
/// pays. How much of it a character has done today is progress and lives elsewhere.
/// </summary>
public sealed record DailyActivityGroupDefinition(
    int GroupId,
    LevelRange Level,
    IReadOnlyList<DailyActivitySlotAllowance> SlotAllowances,
    IReadOnlyList<DailyActivitySlot> Slots,
    IReadOnlyList<DailyActivityReward> Rewards)
{
    /// <summary>
    /// Slots opened at a level, taken from the allowance table. Falls back to every
    /// declared slot when the group states no allowances.
    /// </summary>
    public int SlotCountForLevel(int level)
    {
        foreach (var allowance in SlotAllowances)
        {
            if (allowance.Level.Includes(level))
                return allowance.SlotCount;
        }

        return SlotAllowances.Count == 0 ? Slots.Count : 0;
    }
}

/// <summary>Static daily-activity definitions.</summary>
public interface IDailyActivityDefinitionSource
{
    /// <summary>Every group the content defines, in declaration order.</summary>
    ValueTask<IReadOnlyList<DailyActivityGroupDefinition>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>One group, or null when the content does not define it.</summary>
    ValueTask<DailyActivityGroupDefinition?> GetAsync(int groupId, CancellationToken cancellationToken);
}
