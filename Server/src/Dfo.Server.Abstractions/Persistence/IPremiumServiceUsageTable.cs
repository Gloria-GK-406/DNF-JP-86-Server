using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Persistence;

public sealed record PremiumServiceUsageRow(int UsedCount, DateTimeOffset? LastConsumedAt);

public interface IPremiumServiceUsageTable
{
    ValueTask<PremiumServiceUsageRow?> ReadAsync(CharacterId id, string key, CancellationToken token);
    ValueTask<int> TryWriteAsync(CharacterId id, string key, PremiumServiceUsageRow? expected,
        int usedCount, DateTimeOffset consumedAt, CancellationToken token);
}
