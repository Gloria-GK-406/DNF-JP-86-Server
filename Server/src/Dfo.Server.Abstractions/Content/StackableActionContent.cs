namespace Dfo.Server.Abstractions.Content;

/// <summary>Server-side identity and limits of an action-bearing stackable.</summary>
public sealed record StackableActionDefinition(
    string Action, int MinimumLevel, int MaximumLevel, long ExpiresAtUnixSeconds, bool IsReadable)
{
    public int FatigueRecovery { get; init; }
    public int DailyUseLimit { get; init; } = -1;
    public int CooldownMilliseconds { get; init; }
}

public interface IStackableActionDefinitionSource
{
    ValueTask<StackableActionDefinition?> GetAsync(int itemId, CancellationToken cancellationToken);
}
