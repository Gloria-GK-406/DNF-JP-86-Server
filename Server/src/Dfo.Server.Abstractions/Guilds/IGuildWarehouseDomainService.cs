using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildWarehouseDomainService
{
    ValueTask<GuildWarehouseView?> OpenAsync(SelectedSessionIdentity actor, CancellationToken ct);
    ValueTask<GuildWarehouseView> ReadAsync(GuildId guild, CancellationToken ct);
    ValueTask<GuildWarehouseTransferResult> DepositAsync(SelectedSessionIdentity actor, BusinessSlot source,
        int warehouseSlot, int itemId, int count, CancellationToken ct);
    ValueTask<GuildWarehouseTransferResult> WithdrawAsync(SelectedSessionIdentity actor, int warehouseSlot,
        int itemId, int count, CancellationToken ct);
    ValueTask<GuildWarehouseMoveResult> MoveAsync(SelectedSessionIdentity actor, int sourceSlot, int sourceItem,
        int targetSlot, int targetItem, CancellationToken ct);
    ValueTask<IReadOnlyList<GuildWarehouseHistoryEntry>?> HistoryAsync(SelectedSessionIdentity actor, CancellationToken ct);
}
