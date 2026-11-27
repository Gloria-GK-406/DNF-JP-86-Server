using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Data.Guilds;

public sealed record GuildWarehouseItem(int Slot, ItemSnapshot Item);

public sealed record GuildWarehouseView(int Capacity, IReadOnlyList<GuildWarehouseItem> Items);

public enum GuildWarehouseFailure { None, AccessDenied, InvalidSlot, ItemChanged, InvalidCount, Untransferable, NoRoom }

public sealed record GuildWarehouseTransfer(int WarehouseSlot, BusinessSlot BagSlot, int Count,
    GuildWarehouseView Warehouse, IReadOnlyList<CharacterId> OtherMembers);

public sealed record GuildWarehouseTransferResult(GuildWarehouseFailure Failure, GuildWarehouseTransfer? Transfer = null);

public enum GuildWarehouseAction : byte { Deposit, Withdraw, Move }

public sealed record GuildWarehouseHistoryEntry(byte[] ActorName, GuildWarehouseAction Action, int ItemId,
    int Count, DateTimeOffset OccurredAt);

public sealed record GuildWarehouseMoveResult(GuildWarehouseFailure Failure, GuildWarehouseView? Warehouse = null,
    IReadOnlyList<CharacterId>? OtherMembers = null);
