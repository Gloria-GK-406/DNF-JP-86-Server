using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.World.IllusionTower;

/// <summary>Death Tower replaces carried items and account-material access for this participant only.</summary>
internal sealed class TowerInventoryDomainService(ICharacterContainerDomainService containers,
    IDomainTransactionAccessor transactions, IItemPlacementRuleService placements, IItemSlotRuleService slots, ITransientItemDomainStoreFactory stores, IItemGrantDomainService grants)
{
    private static readonly InventoryContainerType[] Types = [InventoryContainerType.Main, InventoryContainerType.SharedMaterial];
    private static ContainerId Identity(CharacterId character, Guid run, InventoryContainerType type) =>
        new($"tower:{run:N}:{character.Value}:{type}");
    public void Enter(CharacterId character, Guid run)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Tower inventory requires a transaction.");
        var originals = Types.Select(type => containers.GetContainer(character, type, isDefault: true)).ToArray();
        var store = stores.Create();
        foreach (var original in originals)
        {
            var temporary = new TemporaryItemContainer(Identity(character, run, original.Type), original.Shape,
                store, transactions, placements, slots);
            containers.Register(character, temporary);
            if (!containers.UseContainer(character, temporary.Id)) throw new InvalidOperationException("Tower inventory could not be selected.");
        }
    }
    public async ValueTask<TowerInventoryUpdate?> AddAsync(SelectedSessionIdentity session, int itemId, CancellationToken ct)
    {
        var grant = await grants.GrantAsync(session.AccountId, session.CharacterId, containers, itemId, 1, ct).ConfigureAwait(false);
        var slot = grant.Placement switch
        {
            ItemPlacementOutcome.AddedToPile { Added: 1 } item => item.SlotIndex,
            ItemPlacementOutcome.PutInEmptySlot { Count: 1 } item => item.SlotIndex,
            ItemPlacementOutcome.NoRoom => -1,
            _ => throw new InvalidDataException($"Tower pickup has invalid placement: {grant.Placement}."),
        };
        if (slot < 0) return null;
        if (grant.Container.IsDefault) throw new InvalidOperationException("Tower pickup cannot enter a permanent container.");
        var placed = await grant.Container.GetAsync(slot, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Tower pickup disappeared.");
        return new(new(grant.Container.Space, slot), placed.Snapshot);
    }
    public void Leave(CharacterId character, Guid run)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Tower inventory requires a transaction.");
        foreach (var type in Types) containers.Remove(character, Identity(character, run, type));
    }
}
