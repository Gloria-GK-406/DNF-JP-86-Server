using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildWarehouseCustodyStore : IDomainStore<IGuildWarehouseCustodySession>;

public interface IGuildWarehouseCustodySession
{
    ValueTask<ItemSnapshot?> ReadSlotAsync(GuildId guild, int slot, CancellationToken ct);
    ValueTask DepositAsync(GuildId guild, int slot, ItemRecord record, ItemSnapshot? occupied, CancellationToken ct);
    ValueTask<ItemRecord> WithdrawAsync(GuildId guild, int slot, int count, CancellationToken ct);
}
