using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Persistence;

public sealed record ConsumableUsageRow(int UsedCount, DateTimeOffset? LastUsedAt);

/// <summary>One durable usage record per character and item; business-day interpretation belongs to the domain.</summary>
public interface IConsumableUsageTable
{
    ValueTask<IReadOnlyDictionary<int, ConsumableUsageRow>> ReadAllAsync(CharacterId id, CancellationToken token);
    ValueTask<ConsumableUsageRow?> ReadAsync(CharacterId id, int itemId, CancellationToken token);
    ValueTask<int> TryWriteAsync(CharacterId id, int itemId, ConsumableUsageRow? expected,
        int usedCount, DateTimeOffset usedAt, CancellationToken token);
}
