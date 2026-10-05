using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

public enum DailyRefillMode : byte
{
    RefillToTarget = 0,
    Add = 1,
}

public sealed record DailyRefillRule(
    int ItemId,
    int Quantity,
    DateTimeOffset ExpiresAt,
    DailyRefillMode Mode,
    int StackLimit,
    byte ItemKind,
    BusinessSlotRange Destination);

public interface IEntryLifecycleContentSource
{
    ValueTask<IReadOnlyList<DailyRefillRule>> GetDeclaredDailyRefillRulesAsync(DateTimeOffset at, CancellationToken ct) => GetDailyRefillRulesAsync(at, ct);
    ValueTask<IReadOnlyList<DailyRefillRule>> GetDailyRefillRulesAsync(
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken);
}
