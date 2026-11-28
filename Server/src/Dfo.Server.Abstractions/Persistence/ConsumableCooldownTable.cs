using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Persistence;

public sealed record ConsumableCooldownRow(ItemCooldownKey Key, int ItemId, DateTimeOffset ExpiresAt);

public interface IConsumableCooldownTable
{
    ValueTask<bool> IsActiveAsync(CharacterId character, ItemCooldownKey key, DateTimeOffset at, CancellationToken ct);
    ValueTask<IReadOnlyList<ConsumableCooldownRow>> ReadActiveAsync(CharacterId character, DateTimeOffset at, CancellationToken ct);
    ValueTask<bool> TryStartAsync(CharacterId character, ConsumableCooldownRow cooldown, DateTimeOffset at, CancellationToken ct);
}
