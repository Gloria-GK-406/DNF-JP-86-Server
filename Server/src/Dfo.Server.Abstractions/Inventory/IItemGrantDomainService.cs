using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Inventory;

public interface IItemGrantDomainService
{
    ValueTask<BusinessItemAdditionResult> GrantAsync(
        AccountId accountId,
        CharacterId characterId,
        ICharacterContainerDomainService containers,
        int itemId,
        int count,
        CancellationToken cancellationToken);
}
