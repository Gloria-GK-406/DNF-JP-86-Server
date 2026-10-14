using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record ExpiredItemCleanupResult(bool Owned, ImmutableArray<InventoryContainerType> ChangedContainers);

public sealed record OnlineExpiredItemsRemoved(AccountId AccountId, CharacterId CharacterId,
    string ConnectionId, long ConnectionGeneration, ImmutableArray<InventoryContainerType> Containers) : IDomainEvent;

/// <summary>Removes expired items in the caller's transaction, for entry and online cleanup alike.</summary>
public interface IExpiredCharacterItemCleanupDomainService
{
    ValueTask<ExpiredItemCleanupResult> CleanupAsync(
        AccountId accountId, CharacterId characterId, CancellationToken cancellationToken);
}

public interface IOnlineExpiredItemCleanupDomainService
{
    ValueTask<bool> CleanupAsync(SelectedSessionIdentity session, CancellationToken cancellationToken);
}
