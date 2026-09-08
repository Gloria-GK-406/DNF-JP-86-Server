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

/// <summary>What a contract creature carries beyond its core, from the columns of its own row.</summary>
/// <remarks>
/// The same eight facts <c>character_creatures</c> keeps, minus its keys. The key is gone because
/// the row this hangs off already is the creature: the thing and its state are one row, so there
/// is no second row left to disagree with the first, and no separate identity to keep in step.
///
/// Present on exactly the rows that are creatures, absent on the rest, exactly as
/// <see cref="AvatarDetailRow"/> is.
/// </remarks>
public sealed record CreatureDetailRow(
    int Field04,
    int ModeFlag,
    int ProgressValue,
    int Mode1Field0A,
    int Mode1Field0B,
    int FieldAfterValue,
    byte[] CreatureText,
    int TailFlag);

/// <summary>One thing in the world, and what it carries -- without saying where it is.</summary>
/// <remarks>
/// <see cref="ItemUid"/> is the identity. It is an <c>int</c> rather than a <c>long</c> because
/// there is one identity space and it is the narrow one: a creature's identity goes on the wire as
/// a signed 32-bit integer, and a second identity kept alongside the first purely to be wider is
/// two things that have to be kept in step. The allocation is bounded so this conversion can never
/// be the thing that fails.
///
/// Exactly one of <see cref="Avatar"/> and <see cref="Creature"/> is present on a row that is one
/// of those, and neither is on a row that is neither.
/// </remarks>
public sealed record WorldItemRow(
    int ItemUid,
    InventoryItemCoreState Core,
    AvatarDetailRow? Avatar,
    CreatureDetailRow? Creature);

/// <summary>Which owner's which container's which position.</summary>
public sealed record ItemPlace(string OwnerScope, int OwnerId, int ListType, int SlotIndex);

/// <summary>One row of <c>item_slots</c>: what a position holds, and how many.</summary>
/// <remarks>
/// <b>One shape for both kinds of content.</b> A slot holding a sword and a slot holding a stack of
/// potions are the same three columns; only their values differ. <see cref="Kind"/> says which of
/// the two <see cref="Id"/> is, so a value is never read as the other thing by accident -- and it
/// is the query that then decides, rather than a rule somebody has to remember.
///
/// A world slot holds exactly one, which the table itself holds it to. A template slot holds as
/// many as the pile has.
///
/// <see cref="CoreUid"/> is the row of <c>items</c> this position's eighty-two bytes are kept on.
/// For a thing with an identity it is <see cref="Id"/> again, which the table holds it to; for a
/// pile it is the only thing that says which row is the pile's, because <see cref="Id"/> names the
/// template there. It is what makes a position able to find and empty its row without <c>items</c>
/// carrying a position of its own.
/// </remarks>
public sealed record ItemSlotRow(
    ItemPlace Place, ItemSlotKind Kind, int Id, int CoreUid, int Count);

/// <summary>One row of <c>character_container_state</c>.</summary>
public sealed record ContainerStateRow(int CharacterId, int ListType, int Value);

/// <summary>One row of <c>account_cargo_state</c>.</summary>
/// <param name="Value32">
/// The gold held in this account's cargo, named after its column. Shared by every character on the
/// account and only visible once the cargo is opened -- it is not the gold a character carries.
/// </param>
public sealed record AccountCargoStateRow(
    int AccountId, int SelectionKey, int Value32, int ItemCount);

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

/// <summary>The six cube fragment counts an account carries.</summary>
/// <remarks>
/// Six colours of a crafting fragment, not six currencies. <c>GoldCube</c> is the gold-coloured
/// fragment and holds no gold: in this repository a bare <c>Gold</c> always means the money.
/// </remarks>
public sealed record CubeFragmentRow(
    int BlackCube,
    int WhiteCube,
    int RedCube,
    int BlueCube,
    int ClearCube,
    int GoldCube);

/// <summary>The rows of <c>items</c>, read and written as things rather than as places.</summary>
/// <remarks>
/// Beside <see cref="ICharacterItemTable"/> rather than replacing it: that one answers a
/// character's whole inventory in the order the entry load reads it, and every caller it has wants
/// exactly that. This one addresses one thing by its identity, which is the question a place cannot
/// ask today and the whole reason the identity exists.
///
/// A row in the table this addresses outlives every move of the thing it is about: where something
/// sits is a row in the slot table, so moving it rewrites that row and leaves the world row alone,
/// and a world row is created once and deleted once. **Nothing production reaches this interface**
/// -- the composition root binds only <c>ICharacterItemTable</c>, and the one implementation is
/// constructed by tests alone. Settlement writes the same table through its own statements.
///
/// Two sets of sentences stood here before, and naming both is the point. The first said nothing
/// reached this yet, that settlement still wrote a row per slot, and that moving one still
/// destroyed and rebuilt it; the last two stopped being true when settlement changed. The second
/// opened "Settlement reaches this now" -- written in the same outcome, to replace the first --
/// and that was wrong on the day it was written: what changed was the table, not this interface's
/// callers. A remark that survives the code it describes is worse than none, and a correction that
/// introduces its own false sentence is how that happens.
/// </remarks>
public interface IWorldItemTable
{
    /// <summary>Puts a thing into the world and answers the identity it was given.</summary>
    /// <remarks>
    /// Nowhere is named. A thing put into the world is in the world and nowhere else until a
    /// position says it holds it, which is a row of <c>item_slots</c> and not a column of this one.
    /// </remarks>
    /// <param name="avatar">What this thing carries as an avatar, or null when it is not one.</param>
    /// <param name="creature">What it carries as a creature, or null when it is not one.</param>
    ValueTask<int> InsertAsync(
        InventoryItemCoreState core,
        AvatarDetailRow? avatar,
        CreatureDetailRow? creature,
        CancellationToken cancellationToken);

    /// <summary>The thing with that identity, or nothing when the world holds none.</summary>
    ValueTask<WorldItemRow?> FindAsync(int itemUid, CancellationToken cancellationToken);

    /// <summary>Every thing in the world that no position holds.</summary>
    /// <remarks>
    /// The state a world row exists in order to be able to express. A thing outlives the positions
    /// it passes through, so it can be in none of them -- and something that is in none of them is
    /// something nobody can reach by looking at an inventory. This is the read that says which
    /// those are.
    /// <para>
    /// <b>Nothing here removes them.</b> Sweeping them up is later work with a schedule of its
    /// own; a read that quietly deleted would leave that work nothing to review and no way to see
    /// what it had done.
    /// </para>
    /// <para>
    /// <b>A pile's row is not one of these.</b> Something interchangeable keeps a row of
    /// <c>items</c> as well, because its core carries values a template id and a count cannot
    /// rebuild. This used to be the whole difficulty: the position holding a pile names the
    /// template rather than the row, so no slot's <c>id</c> ever named a pile's row, and telling a
    /// pile from an orphan needed the position columns <c>items</c> no longer has. A position now
    /// names its row outright in <c>core_uid</c>, whichever kind it holds, so the question is one
    /// condition and the same one for both.
    /// </para>
    /// <para>
    /// <b>The answer is exactly as truthful as the slot table.</b> A write that put a row into
    /// <c>items</c> without the position that holds it would leave something this cannot tell from
    /// a thing nobody holds. Nothing writes that way any more: the entry save and the equipment a
    /// character is created wearing both put the position down with the thing.
    /// </para>
    /// </remarks>
    ValueTask<IReadOnlyList<WorldItemRow>> ReadUnreferencedAsync(
        CancellationToken cancellationToken);

    /// <summary>Changes what the thing with that identity carries, leaving the identity alone.</summary>
    /// <remarks>
    /// The avatar and creature columns are written on every call rather than only when there are
    /// some, so a thing that stops being one stops carrying them. Leaving them would not be a
    /// harmless leftover: what a row carries is what says which kind it is.
    /// </remarks>
    ValueTask<int> UpdateAsync(
        int itemUid,
        InventoryItemCoreState core,
        AvatarDetailRow? avatar,
        CreatureDetailRow? creature,
        CancellationToken cancellationToken);

    /// <summary>Takes the thing out of the world for good.</summary>
    ValueTask<int> DeleteAsync(int itemUid, CancellationToken cancellationToken);
}

/// <summary>The rows of <c>item_slots</c>: what each position holds.</summary>
/// <remarks>
/// One position at a time, which is what a caller asking about a place wants. The reads and writes
/// a whole inventory needs are on <see cref="ICharacterItemTable"/>, because they are one question
/// about a character rather than many about places.
/// </remarks>
public interface IItemSlotTable
{
    /// <summary>Everything this owner has, by container and then by position.</summary>
    ValueTask<IReadOnlyList<ItemSlotRow>> ReadByOwnerAsync(
        string ownerScope, int ownerId, CancellationToken cancellationToken);

    /// <summary>What one position holds, or nothing when it holds nothing.</summary>
    ValueTask<ItemSlotRow?> FindAsync(ItemPlace place, CancellationToken cancellationToken);

    /// <summary>Puts that into that position.</summary>
    /// <remarks>
    /// Refused when the position is taken, rather than overwriting it. A slot that quietly
    /// replaced what it held would lose a world row's only reference without anything saying so.
    /// </remarks>
    ValueTask<int> InsertAsync(ItemSlotRow row, CancellationToken cancellationToken);

    /// <summary>Empties that position.</summary>
    ValueTask<int> ClearAsync(ItemPlace place, CancellationToken cancellationToken);
}

/// <summary>A character's things, and the positions of its own that hold them.</summary>
/// <remarks>
/// <b>Writing one is two steps, and they are apart on purpose.</b> What a thing is and where it is
/// are two rows now, and a save that reshuffles an inventory would otherwise have to put a thing
/// down in a place the thing it is swapping with has not left yet. So every one of this
/// character's positions is emptied once, in the middle: <see cref="UpdateOwnAsync"/> and
/// <see cref="InsertOwnAsync"/> settle what the things are while the old positions still stand --
/// which is also what lets an update prove the thing is this character's -- and
/// <see cref="HoldOwnAsync"/> then says where each one ended up.
/// <para>
/// This is what replaces parking a row on a position nobody has for the length of a batch. There
/// is no uniqueness over positions on <c>items</c> left to dodge, because <c>items</c> holds no
/// position.
/// </para>
/// <para>
/// <b>One read here is not about a single character.</b>
/// <see cref="ReadWornByAccountAsync"/> answers what every character on an account is wearing,
/// because the character-selection screen draws them all at once. It is still a question about
/// characters' own things; the account is how they are gathered, not whose the things are.
/// </para>
/// </remarks>
public interface ICharacterItemTable
{
    /// <summary>Every one of them, in the order the load reads them.</summary>
    ValueTask<IReadOnlyList<CharacterItemRow>> ReadByCharacterAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>The main list, in the order the client draws it.</summary>
    ValueTask<IReadOnlyList<MainItemRow>> ReadMainAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>What every live character on this account is wearing.</summary>
    /// <remarks>
    /// The one read on this interface that spans an account rather than a character, and it is here
    /// rather than on a seam of its own because the thing it answers about is still a character's
    /// own things -- several characters' worth of them, gathered by the account they belong to.
    /// <para>
    /// It exists because the character-selection screen draws every character at once and there was
    /// no way to ask that question: every equipped read was by one character, so the screen read a
    /// stored blob instead, and nothing had written that blob since the characters were made.
    /// </para>
    /// <para>
    /// Ordered by character and then by slot, and returning the equipment container's rows whole.
    /// Which of them are drawn is decided above this, in the one place that decides it for both of
    /// the screens that draw a character.
    /// </para>
    /// </remarks>
    ValueTask<IReadOnlyList<WornItemRow>> ReadWornByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes this character's own things apart from the ones named, with their positions.
    /// </summary>
    /// <remarks>
    /// The positions of the things that are kept are left standing, because they are what says
    /// those things are this character's until <see cref="ClearOwnPositionsAsync"/> takes them
    /// away.
    /// </remarks>
    ValueTask<int> DeleteOwnExceptAsync(
        CharacterId characterId, IReadOnlyList<long> keep, CancellationToken cancellationToken);

    /// <summary>Changes what one of this character's own things carries, wherever it is.</summary>
    /// <remarks>
    /// One when the thing is this character's, and nothing at all when it is not -- which is how a
    /// lease naming something stale, or something belonging to somebody else, is refused rather
    /// than acted on. Being this character's is read off the position that still holds it.
    /// </remarks>
    /// <param name="avatarDetail">
    /// What this item carries as an avatar, or null when it is not one. Null clears the avatar
    /// columns rather than leaving them: an item updated out of being an avatar that kept them
    /// would be refused by the encoder the next time it was sent.
    /// </param>
    ValueTask<int> UpdateOwnAsync(
        long itemUid,
        CharacterId characterId,
        InventoryItemCoreState core,
        InventoryAvatarDetailState? avatarDetail,
        CancellationToken cancellationToken);

    /// <summary>Puts a new thing into the world and answers its identity.</summary>
    /// <remarks>
    /// No character is named, because a new thing belongs to nobody until a position holds it.
    /// Null when the write landed on nothing, which here means an avatar UID another row holds.
    /// </remarks>
    /// <param name="avatarDetail">As above; null for everything that is not an avatar.</param>
    ValueTask<long?> InsertOwnAsync(
        InventoryItemCoreState core,
        InventoryAvatarDetailState? avatarDetail,
        CancellationToken cancellationToken);

    /// <summary>
    /// Empties every position of this character's inventory, leaving the things themselves.
    /// </summary>
    /// <remarks>
    /// The title-book containers are left alone, exactly as the reads and the scan leave them.
    /// </remarks>
    ValueTask<int> ClearOwnPositionsAsync(
        CharacterId characterId, CancellationToken cancellationToken);

    /// <summary>Says that one of this character's positions now holds that.</summary>
    /// <param name="id">
    /// What the position holds: the thing's identity when it has one, the archive's template
    /// number when it is a pile.
    /// </param>
    /// <param name="coreUid">The row of <c>items</c> this position's bytes are kept on.</param>
    ValueTask<int> HoldOwnAsync(
        CharacterId characterId,
        int listType,
        int slotIndex,
        ItemSlotKind kind,
        int id,
        int coreUid,
        int count,
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

    ValueTask InsertIfAbsentAsync(
        CharacterId characterId, int listType, int parameter, CancellationToken cancellationToken);
}

/// <summary>What an account's cargo is set to.</summary>
public interface IAccountCargoStateTable
{
    /// <summary>At most two rows, so that a duplicated one can be seen rather than hidden.</summary>
    ValueTask<IReadOnlyList<AccountCargoStateRow>> ReadByAccountAsync(
        AccountId accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds to what the account is carrying and answers what it carries afterwards.
    /// </summary>
    /// <remarks>
    /// The addition happens in the statement rather than by reading and writing back, because two
    /// pickups answered at once must not each add to the same total they both read. An account
    /// with no row yet gets one; the column defaults to zero, which is what a new account has.
    /// </remarks>
    ValueTask<int> AddValue32Async(
        AccountId accountId, int amount, CancellationToken cancellationToken);

    /// <summary>
    /// Takes that much away if the account is carrying it, and answers what is left.
    /// </summary>
    /// <remarks>
    /// <c>null</c> when the account was not carrying that much, and then nothing was written:
    /// the condition and the subtraction are one statement, so no two takes can both pass a test
    /// only one of them could afford.
    /// <para>
    /// <b>Separate from <see cref="AddValue32Async"/> rather than a negative amount handed to
    /// it.</b> That statement holds its result at zero, which turns "could not pay" into "paid,
    /// and is now empty" -- silently, and with the money gone. A caller charging for something
    /// has to be able to tell those apart.
    /// </para>
    /// <para>
    /// An account with no row carries nothing: every take above zero from it fails, and a take of
    /// zero succeeds against the zero it carries without opening it a row. Taking nothing changes
    /// nothing, so there is nothing to record.
    /// </para>
    /// </remarks>
    ValueTask<int?> TryTakeValue32Async(
        AccountId accountId, int amount, CancellationToken cancellationToken);
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
