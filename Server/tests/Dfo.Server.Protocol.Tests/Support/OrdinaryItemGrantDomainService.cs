using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Protocol.Tests.Support;

/// <summary>Test adapter for scenarios that deliberately exercise only character inventory.</summary>
internal sealed class OrdinaryItemGrantDomainService : IItemGrantDomainService
{
    public async ValueTask<ItemGrantOutcome> GrantAsync(
        AccountId accountId,
        IItemContainer characterContainer,
        int itemId,
        int count,
        CancellationToken cancellationToken)
    {
        var placement = await characterContainer.AddAsync(itemId, count, cancellationToken)
            .ConfigureAwait(false);
        return new ItemGrantOutcome.CharacterItem(placement);
    }
}
