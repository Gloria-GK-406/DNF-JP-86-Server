using System.Globalization;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Identifies one container among the several a character may hold of the same type.</summary>
/// <remarks>
/// A string rather than a number because the identifiers come from unrelated places -- the
/// character's own storage, a dungeon run, a trade -- and a shared numeric space would make two of
/// them collide the moment a second producer appears.
/// </remarks>
public readonly record struct ContainerId(string Value)
{
    private const string CharacterScope = "character";

    /// <summary>The identifier of a character's own container of one type.</summary>
    public static ContainerId ForCharacter(CharacterId characterId, InventoryContainerType type) =>
        new($"{CharacterScope}:{characterId.Value}:{type}");

    /// <summary>
    /// Reads back the owner of an identifier minted by <see cref="ForCharacter"/>, or answers false
    /// for one minted anywhere else.
    /// </summary>
    /// <remarks>
    /// Kept beside the minting so the two cannot drift apart. A persistent store has to get from an
    /// identifier to the rows it names, and the alternative -- a registry the store is told about
    /// as containers appear -- would put a per-character lifetime inside a singleton for a fact
    /// that is already written down here.
    ///
    /// Only what <see cref="ForCharacter"/> would have produced is accepted, which is stricter than
    /// reading the parts back. Identifiers are compared as written wherever containers are held, so
    /// two spellings of one container -- a numeric container type, a padded or signed number, the
    /// comma list that <see cref="Enum"/> parsing is willing to add together -- would be two
    /// containers to everything upstream while being one container here. A caller holding one is
    /// told it is not a character's, rather than handed a second view of a container it already has.
    /// </remarks>
    public bool TryGetCharacterOwner(out CharacterId characterId, out InventoryContainerType type)
    {
        characterId = default;
        type = default;
        if (Value is null)
        {
            return false;
        }

        var parts = Value.Split(':');
        if (parts.Length != 3
            || parts[0] != CharacterScope
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var owner)
            || owner.ToString(CultureInfo.InvariantCulture) != parts[1]
            || !Enum.TryParse(parts[2], out type)
            || !Enum.IsDefined(type)
            || Enum.GetName(type) != parts[2])
        {
            return false;
        }

        characterId = new CharacterId(owner);
        return true;
    }

    public override string ToString() => Value;
}

/// <summary>
/// What the client needs in order to draw a container: which list it is, and its capacity
/// parameter.
/// </summary>
/// <remarks>
/// Capacity is per character rather than per type -- a character who expanded their cargo has a
/// larger one -- so the shape travels with the container instead of being looked up from the type.
/// This is also what a transient container is built from: an empty container of the same shape is
/// one the client will draw exactly as it drew the original.
/// </remarks>
public sealed record ContainerShape(InventoryContainerType ContainerType, ushort Parameter);

/// <summary>
/// A set of items addressed by slot, which can be replaced by another implementation.
/// </summary>
/// <remarks>
/// The container is bound to one character. Its methods therefore do not take a character: an
/// instance that served several characters would have to be trusted to keep them apart, and every
/// caller would have to keep passing the one fact the container already knows.
///
/// Two kinds of member sit here. The reads and writes are what a container is made of, and address
/// one slot at a time. The verbs -- putting an arriving item away, topping a character up, moving
/// something between two slots -- are what a container is asked to do, and each decides for itself
/// whether it is allowed before carrying it out as some of those reads and writes.
///
/// The verbs used to live in the callers, which is what made "where does an arriving item go"
/// something two of them had written out separately. Nothing beneath here learned anything by their
/// moving in: the store is still told only to read and to write, and `ItemContainerOperations` --
/// which composes a move without the store knowing what a move is -- is the standing proof that
/// composition never needed the store's help. What makes a verb safe to express as several writes
/// is the use case boundary: everything one request does settles together, so an operation made of
/// four writes is still one change.
///
/// A verb answers with what happened rather than throwing, because "there was no room" and "the
/// archive declares no such item" are ordinary answers that different callers treat differently --
/// one leaves the item out of an acknowledgement, another fails the whole entry.
/// </remarks>
public interface IItemContainer
{
    ContainerId Id { get; }

    InventoryContainerType Type { get; }

    ContainerShape Shape { get; }

    /// <summary>
    /// Whether this container may be removed from the character.
    /// </summary>
    /// <remarks>
    /// False for the container a character falls back to. Removing that one would leave the
    /// character with nothing bound -- a state with no recovery, in a game with no reconnect --
    /// so it is refused rather than left to discipline.
    /// </remarks>
    bool IsRemovable { get; }

    /// <summary>What one slot holds, or null when it is empty.</summary>
    ValueTask<ContainerSlotItem?> ReadSlotAsync(int slotIndex, CancellationToken cancellationToken);

    /// <summary>What two slots hold, read together.</summary>
    ValueTask<ContainerSlotPair> ReadSlotsAsync(
        int firstSlotIndex,
        int secondSlotIndex,
        CancellationToken cancellationToken);

    /// <summary>Every occupied slot, for projection to the client.</summary>
    ValueTask<IReadOnlyList<InventoryItemState>> SnapshotAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Everything this container holds, in slot order.
    /// </summary>
    /// <remarks>
    /// Deciding where an arriving item goes needs to know which slots are taken and what piles of
    /// it are already there, and neither is a question about one slot. Inside a use case it sees
    /// what that use case has already written -- as does <see cref="SnapshotAsync"/>, which reads
    /// the same rows by another route and is only kept honest by every verb applying what it
    /// decided before it answers. What still separates the two is what they carry: a snapshot
    /// names facts the store's vocabulary does not, an avatar's details among them.
    /// </remarks>
    ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Puts this exact item in the slot, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// A container whose contents outlive the process refuses this outside a use case boundary,
    /// because a change with no boundary is a change nothing can take back. One whose contents do
    /// not outlive the process has nothing to take back and so accepts it either way.
    /// </remarks>
    ValueTask PutAsync(int slotIndex, InventoryItemCoreState core, CancellationToken cancellationToken);

    /// <summary>Leaves the slot empty, under the same rule as <see cref="PutAsync"/>.</summary>
    ValueTask ClearAsync(int slotIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Puts an item the game is handing over wherever this container's rules send it.
    /// </summary>
    /// <remarks>
    /// The container looks the item up for itself. A pile of it that has room takes as much as it
    /// can hold and the rest is not placed; with no such pile, the first free slot of the item's
    /// own band opens.
    /// </remarks>
    ValueTask<ItemPlacementOutcome> AddAsync(
        int itemId, int count, CancellationToken cancellationToken);

    /// <summary>
    /// Tops the character up by what a daily rule owes them.
    /// </summary>
    /// <remarks>
    /// Not the same as <see cref="AddAsync"/>, and neither can stand in for the other: a refill
    /// joins whatever pile is there however large, because its quantity was already bounded by the
    /// rule that produced it, while an ordinary grant fills piles only to the item's stack limit.
    /// The band and the kind come from that rule rather than from the item's placement, which is
    /// why they arrive as data instead of being looked up here.
    /// </remarks>
    ValueTask<ItemPlacementOutcome> RefillAsync(
        ItemRefill refill, CancellationToken cancellationToken);

    /// <summary>
    /// Moves what one slot holds to another slot of this same container.
    /// </summary>
    /// <remarks>
    /// A count of zero, or one larger than the pile, means the whole pile: a plain drag leaves the
    /// field at zero and only a split dialog fills it in.
    ///
    /// The two ends may arrive the other way round. When the slot moved from is empty, the caller
    /// is naming the slot it wants filled, and the pile to put there is the one at the other end;
    /// the reference server reads it the same way. What the container answers is what was moved,
    /// not where it ended up, because a caller that echoes ends back to a client echoes the ones it
    /// was sent rather than the ones the move used.
    /// </remarks>
    ValueTask<ItemMoveOutcome> MoveToAsync(
        int fromSlotIndex, int toSlotIndex, int count, CancellationToken cancellationToken);
}

/// <summary>What a daily rule owes, in the terms the container needs to place it.</summary>
public sealed record ItemRefill(
    int ItemId, byte ItemKind, int Count, int SlotStart, int SlotEnd);

/// <summary>What became of an item offered to a container.</summary>
/// <remarks>
/// The two ways of succeeding are told apart rather than folded into one, because callers report
/// them differently: a grant names how many it added, a refill names both that and what the slot
/// holds afterwards, and one of them treats opening a slot as the moment a missing item kind
/// becomes fatal.
/// </remarks>
public abstract record ItemPlacementOutcome
{
    private ItemPlacementOutcome()
    {
    }

    /// <summary>It joined a pile the character already had.</summary>
    /// <param name="Added">How many this call put in, which may be fewer than were offered.</param>
    /// <param name="Total">What that slot holds afterwards.</param>
    public sealed record AddedToPile(int SlotIndex, int Added, int Total) : ItemPlacementOutcome;

    /// <summary>It opened a slot of its own band that was empty.</summary>
    public sealed record PutInEmptySlot(int SlotIndex, int Count) : ItemPlacementOutcome;

    /// <summary>The band has nothing free and no pile of it could take any more.</summary>
    public sealed record NoRoom : ItemPlacementOutcome;

    /// <summary>The archive declares no item that can be held under that identifier.</summary>
    public sealed record UnknownItem : ItemPlacementOutcome;
}

/// <summary>Why a container would not carry a move out.</summary>
/// <remarks>
/// Only the judgements a container makes. Whether the request named a list this server serves, and
/// whether the character is bound to a container at all, are decided before a container is reached
/// and are not among these.
/// </remarks>
public enum ItemMoveRefusal
{
    /// <summary>Neither named slot holds anything.</summary>
    BothSlotsEmpty,

    /// <summary>The pile may not occupy the slot it was aimed at.</summary>
    ItemCannotOccupySlot,

    /// <summary>Pouring the two piles together would burst the item's stack limit.</summary>
    MergeExceedsStackLimit,

    /// <summary>A swap was asked for with only part of the pile.</summary>
    PartialSwapNotAllowed,

    /// <summary>The pile being displaced may not occupy the slot being vacated.</summary>
    DisplacedCannotOccupySlot,
}

/// <summary>What became of a move.</summary>
public abstract record ItemMoveOutcome
{
    private ItemMoveOutcome()
    {
    }

    /// <param name="MoveCount">How much of the pile moved.</param>
    /// <param name="ItemId">What moved, for the caller to report.</param>
    public sealed record Moved(int MoveCount, int ItemId) : ItemMoveOutcome;

    /// <summary>Both ends were the same slot: nothing happened and nothing is wrong.</summary>
    public sealed record Unchanged : ItemMoveOutcome;

    public sealed record Refused(ItemMoveRefusal Reason) : ItemMoveOutcome;
}

/// <summary>What one slot of a container holds.</summary>
/// <remarks>
/// The whole of it, because that is what a write takes: an item read out of one slot and written
/// into another has to arrive with its enchantments, its rolled options and its avatar link intact,
/// and the only thing that carries all of those is the core itself.
///
/// <b>One shape for both kinds of content.</b> A slot holding a sword and a slot holding a stack
/// of potions are read as the same three facts -- which sort of thing it is, what it is, and how
/// many -- and differ only in what those three say. <see cref="WorldClass"/> is the first of them,
/// and it is the archive's answer rather than the kind byte inside the core, which is the reading
/// the classification exists to replace.
///
/// <b>No identity is among them, deliberately.</b> A slot is addressed by where it is, and the
/// name of the thing standing there is settlement's to keep. Handing one out here would create
/// exactly the token a verb could carry from a read to a write -- take the token, go elsewhere for
/// something to operate on, operate, put a new token back -- which is the round trip this whole
/// shape exists to make unsayable. What a caller who needs the name asks for instead is the
/// container's snapshot, which is a projection of what is stored and not something a write takes.
/// </remarks>
public sealed record ContainerSlotItem(
    int SlotIndex, InventoryItemCoreState Core, ItemWorldClass WorldClass)
{
    public byte ItemKind => Core.ItemKind;

    public int ItemId => Core.ItemId;

    /// <summary>How many the slot holds. For anything that does not stack, one.</summary>
    public int Count => Core.Value;

    /// <summary>Whether two of what is here would be the same thing.</summary>
    public bool IsPile => this.WorldClass is ItemWorldClass.Interchangeable;
}

/// <summary>What the two slots named by a move request hold right now.</summary>
public sealed record ContainerSlotPair(ContainerSlotItem? First, ContainerSlotItem? Second);
