using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildWarehouseStore
{
    ValueTask<int> ReadWarehouseCapacityAsync(GuildId guild, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildWarehouseItem>> ReadWarehouseItemsAsync(GuildId guild, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildWarehouseHistoryEntry>> ReadWarehouseHistoryAsync(GuildId guild, int limit, CancellationToken ct);
    ValueTask AppendWarehouseHistoryAsync(GuildId guild, CharacterId actor, GuildWarehouseAction action, int itemId,
        int count, DateTimeOffset at, CancellationToken ct);
    ValueTask<bool> HasWarehouseItemsAsync(GuildId guild, CancellationToken ct);
    ValueTask SetWarehouseCapacityAsync(GuildId guild, int capacity, CancellationToken ct);
}
