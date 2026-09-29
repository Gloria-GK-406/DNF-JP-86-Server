using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Creatures;

internal sealed class CreatureDomainService(
    ICharacterBusinessInventoryDomainService inventories,
    ICreatureDomainStore creatures,
    IDomainTransactionAccessor transactions) : ICreatureDomainService
{
    public ICreatureStore GetStore(CharacterId characterId) =>
        new CreatureStore(characterId,
            inventories.GetStore(characterId, BusinessInventoryStoreKind.Creature),
            creatures, transactions);

    private sealed class CreatureStore(CharacterId characterId, IBusinessInventoryStore inventory,
        ICreatureDomainStore creatures, IDomainTransactionAccessor transactions) : ICreatureStore
    {
        public BusinessInventoryStoreKind Kind => inventory.Kind;

        public IBusinessItemContainer? FindContainer(BusinessInventorySpace space) =>
            inventory.FindContainer(space);

        public IBusinessItemContainer GetContainer(BusinessInventorySpace space) =>
            inventory.GetContainer(space);

        public ValueTask<CreatureState?> FindAsync(int itemUid, CancellationToken cancellationToken) =>
            this.Session().ReadAsync(characterId, itemUid, cancellationToken);

        public ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CancellationToken cancellationToken) =>
            this.Session().ReadAllAsync(characterId, cancellationToken);

        public ValueTask<CreatureState?> CurrentAsync(CancellationToken cancellationToken) =>
            this.Session().ReadCurrentAsync(characterId, cancellationToken);

        public ValueTask SaveAsync(CreatureState creature, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Session().Write(characterId, creature);
            return ValueTask.CompletedTask;
        }

        public ValueTask ReviveIfEmptyAsync(int itemUid, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Session().ReviveIfEmpty(characterId, itemUid);
            return ValueTask.CompletedTask;
        }

        private ICreatureDomainStoreSession Session() =>
            creatures.SessionIn(transactions.Current ?? throw new InvalidOperationException(
                "Creature operations require a domain transaction."));
    }
}
