using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Characters;

public enum CollectionBoxAction { Select, Put, Withdraw, Extend }
public enum CollectionBoxRejection
{
    InvalidOwner, Permanent, UnknownBox, WrongBox, InvalidItem, LegacyConflict,
    SourceUnavailable, MissingItem, LockedOrTimed, InvalidAction,
}
public sealed record CollectionBoxInventoryChange(BusinessSlot Slot, ItemSnapshot? Item);
public sealed record CollectionBoxMutation(CollectionBoxSnapshot State, IReadOnlyList<CollectionBoxInventoryChange> Inventory);
public sealed class CollectionBoxRejectedException(CollectionBoxRejection reason) : Exception($"Collection box request rejected: {reason}.")
{ public CollectionBoxRejection Reason { get; } = reason; }
public interface ICollectionBoxDomainService
{
    ValueTask<CollectionBoxSnapshot> ReadAsync(CharacterId character, CancellationToken ct);
    ValueTask<CollectionBoxMutation> ExecuteAsync(AccountId account, CharacterId character, CollectionBoxAction action,
        byte boxId, BusinessSlot? source, int itemId, CancellationToken ct);
}
public interface ICollectionBoxRuleService
{
    ValueTask<IReadOnlyList<CollectionBoxDefinition>> CatalogAsync(CancellationToken ct);
}
