using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.World.IllusionTower;

internal sealed class TowerInventoryDomainService(ICharacterContainerDomainService containers,
    IDomainTransactionAccessor transactions, IItemPlacementRuleService placements, IItemSlotRuleService slots,
    ITransientItemDomainStoreFactory stores, IItemGrantDomainService grants)
{
    private static ContainerId Identity(CharacterId character, Guid run) => new($"tower:{run:N}:{character.Value}");
    private IItemContainer Fresh(ContainerId id) => new MemoryItemContainer(id,
        new(InventoryContainerType.QuickSlot, 6), stores.Create(), transactions, placements, slots);

    public void Enter(CharacterId character, Guid run)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Tower inventory requires a transaction.");
        var original = containers.GetContainer(character, InventoryContainerType.QuickSlot, isDefault: true);
        containers.ReplaceDefault(character, original.Id, Fresh(Identity(character, run)));
    }
    public async ValueTask<TowerInventoryUpdate?> AddAsync(SelectedSessionIdentity session, int itemId, CancellationToken ct)
    {
        var placement = await placements.PlacementForAsync(itemId, ct).ConfigureAwait(false);
        if (placement is null || !ItemContainerAdmission.For(BusinessInventorySpace.TowerQuickSlot).Matches(placement.Traits))
            throw new InvalidDataException($"Tower drop {itemId} is not admitted to the tower quick slots.");
        var grant = await grants.GrantAsync(session.AccountId, session.CharacterId, containers, itemId, 1, ct).ConfigureAwait(false);
        var slot = grant.Placement switch
        {
            ItemPlacementOutcome.AddedToPile { Added: 1 } item => item.SlotIndex,
            ItemPlacementOutcome.PutInEmptySlot { Count: 1 } item => item.SlotIndex,
            ItemPlacementOutcome.NoRoom => -1,
            _ => throw new InvalidDataException($"Tower pickup has invalid placement: {grant.Placement}."),
        };
        if (slot < 0) return null;
        if (grant.Container.IsPersistent || grant.Container.Space != BusinessInventorySpace.TowerQuickSlot)
            throw new InvalidOperationException("Tower pickup requires the memory quick-slot container.");
        var placed = await grant.Container.GetAsync(slot, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Tower pickup disappeared.");
        return new(new(grant.Container.Space, slot), placed.Snapshot);
    }
    public void Leave(CharacterId character, Guid run)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Tower inventory requires a transaction.");
        containers.ReplaceDefault(character, Identity(character, run),
            Fresh(new ContainerId($"tower:empty:{character.Value}:{Guid.NewGuid():N}")));
    }
}
