using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Persistence;

/// <summary>One row of <c>character_new_items</c>, as it is stored.</summary>
/// <remarks>
/// Everything here is the column's own value, including the scope and owner that the load checks
/// against the character it was asked for. A row that carries someone else's owner is a row this
/// has to be able to describe, because refusing it is the load's job and not the table's.
/// </remarks>
public sealed record CharacterItemRow(
    long ItemUid,
    string OwnerScope,
    int OwnerId,
    int? CharacterId,
    int ListType,
    int SlotIndex,
    InventoryItemCoreState Core);

/// <summary>One row of <c>account_cargo_new_items</c>, as it is stored.</summary>
public sealed record AccountCargoItemRow(
    long ItemUid,
    int AccountId,
    int? CharacterId,
    int ListType,
    int SlotIndex,
    InventoryItemCoreState Core);

/// <summary>One item of the main list.</summary>
public sealed record MainItemRow(long ItemUid, int SlotIndex, InventoryItemCoreState Core);

/// <summary>
/// One stack of the main list, reduced to what a refill is about.
/// </summary>
/// <remarks>
/// A refill asks how many of a thing a character already has and makes it more. Which item and
/// how many of it is all of that; the rest of what an item carries is neither read nor written,
/// and is left where it is.
/// </remarks>
public sealed record MainStackRow(long ItemUid, int SlotIndex, int ItemId, int Count);

/// <summary>One row of <c>character_avatar_detail</c>.</summary>
public sealed record AvatarDetailRow(
    int OwnerId,
    int CharacterId,
    int ItemId,
    long ExpireDate,
    long ClearAvatarId,
    byte[] JewelSocket,
    long Color1,
    long Color2,
    long DeleteDate);

/// <summary>One row of <c>character_container_state</c>.</summary>
public sealed record ContainerStateRow(int CharacterId, int ListType, int Value);

/// <summary>One row of <c>account_cargo_state</c>.</summary>
public sealed record AccountCargoStateRow(
    int AccountId, int SelectionKey, int Value32, int ItemCount);

/// <summary>One row of <c>character_gold_limits</c>.</summary>
public sealed record GoldLimitRow(int CarryLimit, int AuctionLimit);

/// <summary>One row of <c>character_item_values</c>.</summary>
public sealed record ItemValueRow(
    int CharacterId, string ListKind, int SortOrder, int ItemId, int Value);

/// <summary>One row of <c>character_item_locks</c>.</summary>
public sealed record ItemLockRow(
    int CharacterId,
    int EquipmentLockId,
    int InventoryListType,
    int SlotIndex,
    int State,
    int RemainingSeconds);

/// <summary>One rental, from either of the two tables that hold them.</summary>
public sealed record RentalRow(
    int CharacterId, long ItemId, long InventoryTemplateId, long ExpireTime);

/// <summary>The six cube fragment counts an account carries.</summary>
public sealed record CubeFragmentRow(
    int Black, int White, int Red, int Blue, int Clear, int Gold);

/// <summary>The items a character carries.</summary>
public interface ICharacterItemTable
{
    /// <summary>Every one of them, in the order the load reads them.</summary>
    ValueTask<IReadOnlyList<CharacterItemRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>The main list, in the order the client draws it.</summary>
    ValueTask<IReadOnlyList<MainItemRow>> ReadMainAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>The main list in slot order, which is the order a refill fills.</summary>
    ValueTask<IReadOnlyList<MainStackRow>> ReadMainStacksAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Which slots of the main list hold something.</summary>
    ValueTask<IReadOnlySet<int>> ReadMainOccupiedSlotsAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Removes this character's own items apart from the ones named.</summary>
    ValueTask<int> DeleteOwnExceptAsync(
        CharacterId characterId, IReadOnlyList<long> keep, CancellationToken cancellationToken);

    ValueTask<int> UpdateOwnAsync(
        long itemUid,
        CharacterId characterId,
        int listType,
        int slotIndex,
        InventoryItemCoreState core,
        CancellationToken cancellationToken);

    ValueTask<int> InsertOwnAsync(
        CharacterId characterId,
        int listType,
        int slotIndex,
        InventoryItemCoreState core,
        CancellationToken cancellationToken);

    /// <summary>Makes one stack hold this many, leaving everything else about it alone.</summary>
    ValueTask<int> SetStackCountAsync(
        long itemUid, int count, CancellationToken cancellationToken);

    /// <summary>Puts a new stack of this many into an empty slot of the main list.</summary>
    ValueTask<int> InsertStackAsync(
        CharacterId characterId,
        int slotIndex,
        int itemId,
        int count,
        byte itemKind,
        CancellationToken cancellationToken);
}

/// <summary>The items an account keeps in its cargo.</summary>
public interface IAccountCargoItemTable
{
    ValueTask<IReadOnlyList<AccountCargoItemRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken);
}

/// <summary>What an avatar is, beyond the item that stands for it.</summary>
public interface ICharacterAvatarDetailTable
{
    ValueTask<AvatarDetailRow?> FindAsync(long itemUid, CancellationToken cancellationToken);

    ValueTask<int> DeleteOwnExceptAsync(
        CharacterId characterId, IReadOnlyList<int> keep, CancellationToken cancellationToken);

    ValueTask<int> UpsertOwnAsync(
        CharacterId characterId,
        int itemUid,
        int itemId,
        InventoryAvatarDetailState detail,
        CancellationToken cancellationToken);
}

/// <summary>What each of a character's containers is set to.</summary>
public interface ICharacterContainerStateTable
{
    ValueTask<IReadOnlyList<ContainerStateRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>What an account's cargo is set to.</summary>
public interface IAccountCargoStateTable
{
    /// <summary>At most two rows, so that a duplicated one can be seen rather than hidden.</summary>
    ValueTask<IReadOnlyList<AccountCargoStateRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken);
}

/// <summary>How much gold a character may hold and bid.</summary>
public interface ICharacterGoldLimitTable
{
    ValueTask<GoldLimitRow?> FindAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The cooldowns and effects a character's items carry.</summary>
public interface ICharacterItemValueTable
{
    ValueTask<IReadOnlyList<ItemValueRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>Which of a character's slots are locked.</summary>
public interface ICharacterItemLockTable
{
    ValueTask<IReadOnlyList<ItemLockRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>What a character has rented from a shop.</summary>
public interface ICharacterRentalTable
{
    ValueTask<IReadOnlyList<RentalRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The older rentals table, read alongside the newer one.</summary>
public interface ILegacyRentalTable
{
    ValueTask<IReadOnlyList<RentalRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The inventory shape that came before the packed one.</summary>
/// <remarks>
/// Nothing reads what is in it. All that is asked is whether anything is, because a character
/// whose items are only here has not been brought across and must not be entered as though it
/// had no items at all.
/// </remarks>
public interface ILegacyCharacterItemTable
{
    ValueTask<bool> AnyAsync(CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>The cargo shape that came before the packed one, asked the same question.</summary>
public interface ILegacyAccountCargoTable
{
    ValueTask<bool> AnyAsync(AccountId accountId, CancellationToken cancellationToken);
}

/// <summary>What a character has already claimed today.</summary>
public interface ICharacterDailyCounterTable
{
    /// <summary>The day on record for this counter, or null when there is none.</summary>
    ValueTask<string?> FindClaimedPeriodAsync(
        CharacterId characterId, string counterKey, CancellationToken cancellationToken);

    ValueTask<int> ClaimAsync(
        CharacterId characterId,
        string counterKey,
        string period,
        CancellationToken cancellationToken);
}
