using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Initialization;
namespace Dfo.Server.Abstractions.Inventory;

public interface IDailyRefillConfigurationDomainService
{
    ValueTask<DailyRefillList> ListAsync(DateTimeOffset at, CancellationToken ct);
    ValueTask<DailyRefillList> ChangeAsync(DailyRefillChange change, string actor, DateTimeOffset at, CancellationToken ct);
    ValueTask<IReadOnlyList<DailyRefillRule>> EffectiveAsync(DateTimeOffset at, CancellationToken ct);
}
public interface IDailyRefillSettingsPersistence
{
    ValueTask AcquireWriteAsync(CancellationToken ct);
    ValueTask<InitializationStoredDocument?> ReadAsync(CancellationToken ct);
    ValueTask<bool> SaveAsync(long expectedRevision, string input, DateTimeOffset at, string actor, CancellationToken ct);
}
