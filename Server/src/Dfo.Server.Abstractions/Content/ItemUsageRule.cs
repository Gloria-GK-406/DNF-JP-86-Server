namespace Dfo.Server.Abstractions.Content;

public sealed record ItemUsageRule(int DailyLimit, int CooldownMilliseconds, bool IsReadable);

/// <summary>Usage restrictions independent of the item's action/effect.</summary>
public interface IItemUsageRuleSource
{
    ValueTask<ItemUsageRule?> GetAsync(int itemId, CancellationToken token);
}
