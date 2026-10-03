using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Tests.Support;

/// <summary>Test adapter for scenarios that deliberately exercise only character inventory.</summary>
internal sealed class OrdinaryItemGrantDomainService : IItemGrantDomainService
{
    public async ValueTask<BusinessItemAdditionResult> GrantAsync(
        AccountId accountId,
        CharacterId characterId,
        ICharacterContainerDomainService containers,
        int itemId,
        int count,
        CancellationToken cancellationToken)
    {
        var physical = containers.GetContainer(characterId, InventoryContainerType.Main);
        var placement = await physical.AddAsync(itemId, count, cancellationToken)
            .ConfigureAwait(false);
        var slot = placement switch
        {
            ItemPlacementOutcome.AddedToPile pile => pile.SlotIndex,
            ItemPlacementOutcome.PutInEmptySlot opened => opened.SlotIndex,
            _ => 9,
        };
        var business = LegacyInventoryStorageCoordinates.ToBusiness(
            new(InventoryContainerType.Main, slot));
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
