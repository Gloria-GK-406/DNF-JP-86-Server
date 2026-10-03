using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>Test adapter for scenarios that deliberately exercise only character inventory.</summary>
internal sealed class OrdinaryItemGrantDomainService(IItemPlacementRuleService? placements = null,
    IItemLifecycleDomainService? lifecycle = null)
    : IItemGrantDomainService
{
    public async ValueTask<BusinessItemAdditionResult> GrantAsync(
        AccountId accountId,
        CharacterId characterId,
        ICharacterContainerDomainService containers,
        int itemId,
        int count,
        CancellationToken cancellationToken)
    {
        var declared = placements is null ? null
            : await placements.PlacementForAsync(itemId, cancellationToken);
        var type = declared is null ? InventoryContainerType.Main
            : LegacyInventoryStorageCoordinates.ToStorage(
                new BusinessSlot(declared.Destination.Space, declared.Destination.FirstIndex)).List;
        var physical = containers.GetContainer(characterId, type);
        if (physical is not IItemGrantStub stub)
        {
            var container = new BusinessItemContainer(declared?.Destination.Space ?? BusinessInventorySpace.Consumable,
                physical, placements, new ItemArrivalRuleService(), lifecycle: lifecycle);
            return await container.AddAsync(itemId, count, cancellationToken);
        }
        var placement = await stub.GrantAsync(itemId, count, cancellationToken);
        var slot = placement switch
        {
            ItemPlacementOutcome.AddedToPile pile => pile.SlotIndex,
            ItemPlacementOutcome.PutInEmptySlot opened => opened.SlotIndex,
            _ => 9,
        };
        var business = LegacyInventoryStorageCoordinates.ToBusiness(new(type, slot));
        var local = placement switch
        {
            ItemPlacementOutcome.AddedToPile pile =>
                new ItemPlacementOutcome.AddedToPile(business.Index, pile.Added, pile.Total),
            ItemPlacementOutcome.PutInEmptySlot opened =>
                new ItemPlacementOutcome.PutInEmptySlot(business.Index, opened.Count),
            _ => placement,
        };
        return new BusinessItemAdditionResult(
            new BusinessItemContainer(business.Space, physical), local);
    }
}

/// <summary>Deterministic grant outcomes and injected failures for command tests.</summary>
internal interface IItemGrantStub
{
    ValueTask<ItemPlacementOutcome> GrantAsync(int itemId, int count, CancellationToken ct);
}
