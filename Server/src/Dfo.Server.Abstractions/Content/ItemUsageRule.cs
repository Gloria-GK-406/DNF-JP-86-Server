namespace Dfo.Server.Abstractions.Content;

public sealed record ItemUsageRule(int DailyLimit, int CooldownMilliseconds, bool IsReadable)
{
    public ItemCooldownMaintenance CooldownMaintenance { get; init; }
    public DateTimeOffset? CooldownUntil(DateTimeOffset? lastUsedAt) =>
        CooldownMaintenance.PersistOnServer && CooldownMilliseconds > 0 ? lastUsedAt?.AddMilliseconds(CooldownMilliseconds) : null;
}

/// <summary>Usage restrictions independent of the item's action/effect.</summary>
public interface IItemUsageRuleSource
{
    ValueTask<ItemUsageRule?> GetAsync(int itemId, CancellationToken token);
}
