using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemGrantDomainService(ICharacterBusinessInventoryDomainService inventories,
    IItemPlacementRuleService placements) : IItemGrantDomainService
{
    public async ValueTask<BusinessItemAdditionResult> GrantAsync(AccountId accountId, CharacterId characterId,
        ICharacterContainerDomainService containers, int itemId, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(containers);
        var placement = await placements.PlacementForAsync(itemId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Item {itemId} has no placement definition.");
        var space = placement.Destination.Space;
        var owner = space is BusinessInventorySpace.SharedMaterial or BusinessInventorySpace.AccountCargo
            ? new ContainerOwner(InventoryOwnerType.Account, accountId.Value)
            : new ContainerOwner(InventoryOwnerType.Character, characterId.Value);
        var container = inventories.GetStore(owner, BusinessInventoryOwnership.StoreOf(space)).GetContainer(space);
        return await container.AddAsync(itemId, count, cancellationToken).ConfigureAwait(false);
    }
}
