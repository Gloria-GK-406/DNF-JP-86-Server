using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Persistence;

/// <summary>One row of <c>items</c> belonging to a character, as it is stored.</summary>
/// <remarks>
/// Everything here is the column's own value, including the scope and owner that the load checks
/// against the character it was asked for. A row that carries someone else's owner is a row this
/// has to be able to describe, because refusing it is the load's job and not the table's.
///
/// <see cref="Avatar"/> is present on exactly the rows that are avatars, and absent on the rest.
/// Absent is not the same as zeroed: an ordinary item has no avatar facts, and an avatar with none
/// is an error.
/// </remarks>
public sealed record CharacterItemRow(
    long ItemUid,
    string OwnerScope,
    int OwnerId,
    int ListType,
    int SlotIndex,
    InventoryItemCoreState Core,
    AvatarDetailRow? Avatar);

/// <summary>One row of <c>items</c> belonging to an account's cargo, as it is stored.</summary>
/// <remarks>
/// No avatar facts here, unlike the character's own rows: an avatar sitting in the cargo has never
/// carried them and is loaded without.
/// </remarks>
public sealed record AccountCargoItemRow(
    long ItemUid,
    int AccountId,
    int? CharacterId,
    int ListType,
    int SlotIndex,
    InventoryItemCoreState Core);

/// <summary>One item of the main list.</summary>
public sealed record MainItemRow(long ItemUid, int SlotIndex, InventoryItemCoreState Core);

/// <summary>What an avatar carries beyond its core, from the columns of its own row.</summary>
/// <remarks>
/// The owner, the character and the template id used to be here as well, and each was checked
/// against the item they belonged to. They are gone because the facts are now columns of the item
/// row: there is no second row left to disagree with the first.
/// </remarks>
public sealed record AvatarDetailRow(
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

/// <summary>A character's two gold caps, which live with its other derived numbers.</summary>
public sealed record GoldLimitRow(int CarryLimit, int AuctionLimit);

/// <summary>One row of <c>character_item_cooldowns</c> or <c>character_item_effects</c>.</summary>
/// <remarks>
/// Which of the two it came from used to be a column of one table, and the value meant one thing or
/// the other depending on it. Which table a row is in says that now.
/// </remarks>
public sealed record ItemValueRow(int CharacterId, int SortOrder, int ItemId, int Value);

/// <summary>One row of <c>character_item_locks</c>.</summary>
public sealed record ItemLockRow(
    int CharacterId,
    int EquipmentLockId,
    int InventoryListType,
    int SlotIndex,
    int State,
    int RemainingSeconds);

/// <summary>One rental.</summary>
public sealed record RentalRow(
    int CharacterId, long ItemId, long InventoryTemplateId, long ExpireTime);

/// <summary>The six cube fragment counts an account carries.</summary>
public sealed record CubeFragmentRow(
    int Black, int White, int Red, int Blue, int Clear, int Gold);

/// <summary>The rows of <c>items</c> a character owns.</summary>
public interface ICharacterItemTable
{
    /// <summary>Every one of them, in the order the load reads them.</summary>
    ValueTask<IReadOnlyList<CharacterItemRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>The main list, in the order the client draws it.</summary>
    ValueTask<IReadOnlyList<MainItemRow>> ReadMainAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Removes this character's own items apart from the ones named.</summary>
    ValueTask<int> DeleteOwnExceptAsync(
        CharacterId characterId, IReadOnlyList<long> keep, CancellationToken cancellationToken);

    /// <param name="avatarDetail">
    /// What this item carries as an avatar, or null when it is not one. Null clears the avatar
    /// columns rather than leaving them: an item updated out of being an avatar that kept them
    /// would be refused by the encoder the next time it was sent.
    /// </param>
    ValueTask<int> UpdateOwnAsync(
        long itemUid,
        CharacterId characterId,
        int listType,
        int slotIndex,
        InventoryItemCoreState core,
        InventoryAvatarDetailState? avatarDetail,
        CancellationToken cancellationToken);

    /// <param name="avatarDetail">As above; null for everything that is not an avatar.</param>
    ValueTask<int> InsertOwnAsync(
        CharacterId characterId,
        int listType,
        int slotIndex,
        InventoryItemCoreState core,
        InventoryAvatarDetailState? avatarDetail,
        CancellationToken cancellationToken);
}

/// <summary>The rows of <c>items</c> an account keeps in its cargo.</summary>
public interface IAccountCargoItemTable
{
    ValueTask<IReadOnlyList<AccountCargoItemRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken);
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

/// <summary>How long until each of a character's items can be used again.</summary>
public interface ICharacterItemCooldownTable
{
    ValueTask<IReadOnlyList<ItemValueRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken);
}

/// <summary>How strong each of the effects a character's items are giving is.</summary>
public interface ICharacterItemEffectTable
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
