namespace Dfo.Server.Abstractions.Content;

/// <summary>Server-side identity and limits of an action-bearing stackable.</summary>
public sealed record StackableActionDefinition(
    string Action, int MinimumLevel, int MaximumLevel, long ExpiresAtUnixSeconds, bool IsReadable)
{
    public int FatigueRecovery { get; init; }
    public int EntryDungeonId { get; init; }
    public int EntryIncrease { get; init; }
    public int LevelIncrease { get; init; } = 1;
    public bool BlockedInHardcore { get; init; }
    public ListedQuestClearDefinition? ListedQuestClear { get; init; }
}

public sealed record ListedQuestClearDefinition(IReadOnlyList<int> QuestIds, int MaterialItemId, int MaterialCount);

public interface IStackableActionDefinitionSource
{
    ValueTask<StackableActionDefinition?> GetAsync(int itemId, CancellationToken cancellationToken);
}
