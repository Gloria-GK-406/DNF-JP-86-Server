namespace Dfo.Server.Abstractions.Content;

/// <summary>Server-side identity and limits of an action-bearing stackable.</summary>
public sealed record StackableActionDefinition(
    string Action, int MinimumLevel, int MaximumLevel, long ExpiresAtUnixSeconds, bool IsReadable)
{
    public int FatigueRecovery { get; init; }
    public int RandomFatigueDefaultRecovery { get; init; }
    public IReadOnlyList<FatigueRecoveryTier> RandomFatigueRecovery { get; init; } = [];
    public int EntryDungeonId { get; init; }
    public int EntryIncrease { get; init; }
    public int LevelIncrease { get; init; } = 1;
    public bool BlockedInHardcore { get; init; }
    public IReadOnlyList<int>? ListedQuestClear { get; init; }
}

public readonly record struct FatigueRecoveryTier(int Amount, int Weight);

public interface IStackableActionDefinitionSource
{
    ValueTask<StackableActionDefinition?> GetAsync(int itemId, CancellationToken cancellationToken);
}
