using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record ItemUsageState(int ItemId, int UsedCount, int? DailyLimit,
    DateTimeOffset? LastUsedAt, DateTimeOffset? CooldownUntil)
{
    public int? Remaining => DailyLimit is { } limit ? Math.Max(0, limit - UsedCount) : null;
}

/// <summary>An item's daily limit and cooldown as the archive declares them, and what a usage record means under them.</summary>
public interface IItemUsageRuleService
{
    /// <exception cref="InvalidDataException">The archive has no readable usage rule for the item.</exception>
    ValueTask<Dfo.Server.Abstractions.Content.ItemUsageRule> RuleAsync(int itemId, CancellationToken token);

    ItemUsageState StateOf(int itemId, Dfo.Server.Abstractions.Persistence.ConsumableUsageRow? row,
        Dfo.Server.Abstractions.Content.ItemUsageRule rule, DateTimeOffset now);
}

public interface IItemUsageDomainService
{
    ValueTask<IReadOnlyList<ItemUsageState>> GetSnapshotAtAsync(CharacterId characterId, DateTimeOffset at, CancellationToken token) =>
        ValueTask.FromException<IReadOnlyList<ItemUsageState>>(new NotSupportedException("Explicit-time item usage read is unavailable."));
    ValueTask<ItemUsageState> GetAsync(CharacterId characterId, int itemId, CancellationToken token);
    /// <summary>Requires the caller's transaction; records only a successful use, never consumes inventory.</summary>
    ValueTask<ItemUsageState> RecordUseAsync(CharacterId characterId, int itemId, CancellationToken token);
    /// <summary>Known limited items and active cooldowns, including unlimited items; no byte-sized wire restrictions.</summary>
    ValueTask<IReadOnlyList<ItemUsageState>> GetSnapshotAsync(CharacterId characterId, CancellationToken token);
}

public enum ItemUsageRejection { DailyLimitReached, CoolingDown }
public sealed class ItemUsageRejectedException(ItemUsageRejection reason) : Exception(reason.ToString())
{
    public ItemUsageRejection Reason { get; } = reason;
}
