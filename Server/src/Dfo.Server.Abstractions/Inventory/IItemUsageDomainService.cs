using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record ItemUsageState(int ItemId, int UsedCount, int? DailyLimit,
    DateTimeOffset? LastUsedAt, DateTimeOffset? CooldownUntil)
{
    public int? Remaining => DailyLimit is { } limit ? Math.Max(0, limit - UsedCount) : null;
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
