using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemGrantDomainService(ICharacterBusinessInventoryDomainService inventories,
    IItemPlacementRuleService placements) : IItemGrantDomainService
{
    public async ValueTask<BusinessItemAdditionResult> GrantAsync(AccountId accountId, CharacterId characterId,
        ICharacterContainerDomainService containers, int itemId, int count, CancellationToken cancellationToken, bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(containers);
        var placement = await placements.PlacementForAsync(itemId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Item {itemId} has no placement definition.");
        var space = placement.Destination.Space;
        var kind = BusinessInventoryOwnership.StoreOf(space);
        var container = inventories.GetStore(characterId, kind).GetContainer(space, isDefault);
        return await container.AddAsync(itemId, count, cancellationToken).ConfigureAwait(false);
    }
}
