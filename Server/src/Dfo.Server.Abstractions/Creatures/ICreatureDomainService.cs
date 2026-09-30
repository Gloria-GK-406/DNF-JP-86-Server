using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Creatures;

public interface ICreatureDomainService
{
    ICreatureCollection Of(CharacterId characterId);
    ICreatureStore GetStore(CharacterId characterId);
}

public interface ICreatureStore : IBusinessInventoryStore
{
    ValueTask<CreatureState?> FindAsync(int itemUid, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CancellationToken cancellationToken);
    ValueTask<CreatureState?> CurrentAsync(CancellationToken cancellationToken);
    ValueTask ReviveIfEmptyAsync(int itemUid, CancellationToken cancellationToken);
}
