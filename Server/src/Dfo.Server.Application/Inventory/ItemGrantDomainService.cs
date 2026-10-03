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
        var owner = space is BusinessInventorySpace.SharedMaterial or BusinessInventorySpace.AccountCargo
            ? new ContainerOwner(InventoryOwnerType.Account, accountId.Value)
            : new ContainerOwner(InventoryOwnerType.Character, characterId.Value);
        var kind = BusinessInventoryOwnership.StoreOf(space);
        // Character selection owns the current binding even when the permanent material owner is an account.
        var container = inventories.GetStore(characterId, kind).FindContainer(space, isDefault)
            ?? inventories.GetStore(owner, kind).GetContainer(space, isDefault);
        return await container.AddAsync(itemId, count, cancellationToken).ConfigureAwait(false);
    }
}
