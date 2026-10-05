using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public interface IItemGrantDomainService
{
    ValueTask<ContainerAddition> GrantAllAsync(CharacterId character, int itemId, int count, CancellationToken ct) => throw new NotSupportedException();
    ValueTask<ContainerAddition> GrantAllPersistentAsync(CharacterId character, int itemId, int count, CancellationToken ct) => throw new NotSupportedException();
    ValueTask<ItemReference> ApplyPlannedAsync(IBusinessItemContainer container, int slot, ItemSnapshot next, CancellationToken ct) =>
        throw new NotSupportedException();
    ValueTask<ItemReference> ApplyPlannedAsync(IItemContainer container, int slot, ItemSnapshot next, CancellationToken ct) =>
        throw new NotSupportedException();
    ValueTask<ItemReference> PutNewAsync(ContainerId container, int slot, ItemSnapshot item, CancellationToken ct) =>
        throw new NotSupportedException();
    ValueTask<BusinessItemAdditionResult> GrantAsync(
        AccountId accountId,
        CharacterId characterId,
        ICharacterContainerDomainService containers,
        int itemId,
        int count,
        CancellationToken cancellationToken, bool isDefault = false);
}
