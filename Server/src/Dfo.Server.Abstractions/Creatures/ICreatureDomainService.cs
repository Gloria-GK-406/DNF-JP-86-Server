using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Creatures;

public interface ICreatureDomainService
{
    ICreatureStore GetStore(CharacterId characterId);
}

public interface ICreatureStore : IBusinessInventoryStore
{
    ValueTask<CreatureState?> FindAsync(int itemUid, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CancellationToken cancellationToken);
    ValueTask<CreatureState?> CurrentAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(CreatureState creature, CancellationToken cancellationToken);
    ValueTask ReviveIfEmptyAsync(int itemUid, CancellationToken cancellationToken);
}
