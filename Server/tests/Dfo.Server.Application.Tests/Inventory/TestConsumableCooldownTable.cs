using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Inventory;

internal sealed class TestConsumableCooldownTable : IConsumableCooldownTable
{
    private readonly Dictionary<(CharacterId, ItemCooldownKey), ConsumableCooldownRow> rows = [];

    public ValueTask<bool> IsActiveAsync(CharacterId character, ItemCooldownKey key, DateTimeOffset at, CancellationToken ct) =>
        ValueTask.FromResult(rows.TryGetValue((character, key), out var row) && row.ExpiresAt > at);

    public ValueTask<IReadOnlyList<ConsumableCooldownRow>> ReadActiveAsync(CharacterId character, DateTimeOffset at, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ConsumableCooldownRow>>(rows
            .Where(pair => pair.Key.Item1 == character && pair.Value.ExpiresAt > at)
            .Select(pair => pair.Value).ToArray());

    public async ValueTask<bool> TryStartAsync(CharacterId character, ConsumableCooldownRow cooldown, DateTimeOffset at, CancellationToken ct)
    {
        if (await IsActiveAsync(character, cooldown.Key, at, ct)) return false;
        rows[(character, cooldown.Key)] = cooldown;
        return true;
    }
}
