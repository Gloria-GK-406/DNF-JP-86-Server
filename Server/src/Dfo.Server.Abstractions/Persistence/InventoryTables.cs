using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Persistence;

/// <summary>One of a character's things, together with the position holding it.</summary>
/// <remarks>
/// Two rows rather than one now: what the thing is comes from <c>items</c>, and where it is comes
/// from the <c>item_slots</c> row that names it. The scope and owner are that row's, and the load
/// still checks them against the character it was asked for -- a position that names someone else
/// is something this has to be able to describe, because refusing it is the load's job and not the
/// table's.
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

/// <summary>One thing one of an account's characters is wearing, and where it is worn.</summary>
/// <remarks>
/// Narrower than <see cref="CharacterItemRow"/> on purpose. The question this answers is "what is
/// every character on this account wearing", asked by the character-selection roster, and the
/// roster draws a piece from its slot and its core and nothing else -- so the avatar columns are
/// not selected rather than selected and dropped.
/// <para>
/// The character is on the row because the read spans several of them. Which slots of the ones it
/// returns are actually drawn is not this row's business and deliberately not the statement's: that
/// is one rule with one home, and a copy of it in SQL is how the two screens came to disagree.
/// </para>
/// </remarks>
public sealed record WornItemRow(
    int CharacterId, long ItemUid, int SlotIndex, InventoryItemCoreState Core);

/// <summary>One row of <c>items</c> belonging to an account's cargo, as it is stored.</summary>
/// <remarks>
/// No avatar facts here, unlike the character's own rows: an avatar sitting in the cargo has never
/// carried them and is loaded without.
/// </remarks>
public sealed record AccountCargoItemRow(
    long ItemUid,
    int AccountId,
    int ListType,
    int SlotIndex,
    InventoryItemCoreState Core);

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
/// <param name="Value32">
/// The gold held in this account's cargo, named after its column. Shared by every character on the
/// account and only visible once the cargo is opened -- it is not the gold a character carries.
/// </param>
public sealed record AccountCargoStateRow(
    int AccountId, int SelectionKey, int Value32);

/// <summary>A character's two gold caps, which live with its other derived numbers.</summary>
/// <param name="CarryLimit">
/// The most gold this character may carry. It caps the gold on the player's bag, which this
/// server does not keep yet, so nothing is currently held to it.
/// </param>
/// <param name="AuctionLimit">The most this character may bid. There is no auction house here.</param>
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

/// <summary>Read projections for character inventories and the character roster.</summary>
public interface ICharacterItemTable
{
    /// <summary>Every one of them, in the order the load reads them.</summary>
    ValueTask<IReadOnlyList<CharacterItemRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>What every live character on this account is wearing.</summary>
    /// <remarks>
    /// The one read on this interface that spans an account rather than a character, and it is here
    /// rather than on a seam of its own because the thing it answers about is still a character's
    /// own things -- several characters' worth of them, gathered by the account they belong to.
    /// <para>
    /// It exists because the character-selection screen draws every character at once, and every
    /// other equipped read is by one character.
    /// </para>
    /// <para>
    /// Ordered by character and then by slot, and returning the equipment container's rows whole.
    /// Which of them are drawn is decided above this, in the one place that decides it for both of
    /// the screens that draw a character.
    /// </para>
    /// </remarks>
    ValueTask<IReadOnlyList<WornItemRow>> ReadWornByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken);


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

    ValueTask InsertIfAbsentAsync(
        CharacterId characterId, int listType, int parameter, CancellationToken cancellationToken);
}

/// <summary>What an account's cargo is set to.</summary>
public interface IAccountCargoStateTable
{
    /// <summary>At most two rows, so that a duplicated one can be seen rather than hidden.</summary>
    ValueTask<IReadOnlyList<AccountCargoStateRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken);
}

/// <summary>What a character carries, and what the cap on it is worked out from.</summary>
/// <param name="Carried">The figure on the player's bag.</param>
/// <param name="Level">The character's level, which the archive prices a floor by.</param>
/// <param name="StoredCarryLimit">
/// The upgrade recorded against the character. Zero for every character today, because nothing in
/// this server sells one yet.
/// </param>
public sealed record CarriedGoldRow(int Carried, int Level, int StoredCarryLimit);

/// <summary>The gold a character is carrying -- the figure on the player's bag.</summary>
/// <remarks>
/// <b>Not stored as an item, though the client draws it as one.</b> On the wire it is the first
/// virtual slot of the main list with item id zero, which is how the client's inventory window can
/// render a currency with the same widget as a sword. That is an encoding, and encodings belong at
/// the boundary: gold has none of an item's thirty fields but one, obeys a carry cap no item
/// obeys, and cannot be moved, split, merged, locked or rented. So it is stored as what it is, a
/// figure on the character, and projected into that slot when a frame is built.
/// <para>
/// It lives in <c>character_stats</c> beside <c>gold_carry_limit</c>, the cap that governs it. The
/// row exists for every character from creation, so "no row" is not a state this has to mean
/// anything by -- a character carrying nothing carries zero, and that is a figure rather than an
/// absence.
/// </para>
/// <para>
/// <b>Both directions are one statement, and that is the whole reason this table exists rather
/// than a write through the item store.</b> That store is a unit of work: reading a figure out,
/// adding to it and writing it back is the only shape available through it, and two changes
/// answered at once would then race. Nothing serialises them -- the per-character execution gate
/// this server declares has no callers -- so the guard has to be in the statement, exactly as the
/// account cargo's is.
/// </para>
/// </remarks>
public interface ICharacterCarriedGoldTable
{
    /// <summary>
    /// What this character is carrying, and the two facts that decide what caps it.
    /// </summary>
    /// <remarks>
    /// The three arrive together because the cap is needed on the same breath as the figure and is
    /// worked out from the character's level and its stored upgrade. Reading them apart would be
    /// two round trips answering one question, and would let the level move between them.
    /// <para>
    /// <c>null</c> when this server has no such character. That is not the same as a character
    /// carrying nothing, and a caller charging or paying one has to be able to tell them apart.
    /// </para>
    /// </remarks>
    ValueTask<CarriedGoldRow?> FindAsync(CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds to what the character carries, holding the total at <paramref name="ceiling"/>, and
    /// answers what it carries afterwards.
    /// </summary>
    /// <remarks>
    /// The ceiling is passed in rather than read here, because what a character may carry is a
    /// rule and this is a table. A negative amount takes gold away; the total is held at zero as
    /// well, since a negative figure on the bag is not a thing the client can draw.
    /// </remarks>
    ValueTask<int> AddAsync(
        CharacterId characterId, int amount, int ceiling, CancellationToken cancellationToken);

    /// <summary>
    /// Takes that much from what the character carries, or takes nothing at all.
    /// </summary>
    /// <remarks>
    /// Answers what is carried afterwards, and <c>null</c> when the character was not carrying
    /// that much -- in which case nothing was written and the figure stands where it stood.
    /// <para>
    /// <b>Not <see cref="AddAsync"/> with the sign turned round.</b> That one holds its total at
    /// zero, so a character asked for more than it has would be emptied and then answer as though
    /// it had paid. Refusing is the whole reason this member exists.
    /// </para>
    /// </remarks>
    ValueTask<int?> TryTakeAsync(
        CharacterId characterId, int amount, CancellationToken cancellationToken);
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
