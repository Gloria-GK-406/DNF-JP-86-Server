using System.Globalization;
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
/// Reads and writes are all there is. A move, a split, a swap, a merge and a consumption are each
/// some of these put together, and the putting-together belongs above -- the container that offered
/// a "move" would be a container that had to be told what one is, and the store beneath it would
/// have to be told too. What makes that safe to say is the use case boundary: everything one
/// request does settles together, so an operation made of four writes is still one change.
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
    /// it are already there, and neither is a question about one slot. Unlike
    /// <see cref="SnapshotAsync"/> this is the container's own contents, so inside a use case it
    /// sees what that use case has already written.
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
}

/// <summary>What one slot of a container holds.</summary>
/// <remarks>
/// The whole of it, because that is what a write takes: an item read out of one slot and written
/// into another has to arrive with its enchantments, its rolled options and its avatar link intact,
/// and the only thing that carries all of those is the core itself.
/// </remarks>
public sealed record ContainerSlotItem(int SlotIndex, InventoryItemCoreState Core)
{
    public byte ItemKind => Core.ItemKind;

    public int ItemId => Core.ItemId;

    /// <summary>How many the slot holds. For anything that does not stack, one.</summary>
    public int Count => Core.Value;
}

/// <summary>What the two slots named by a move request hold right now.</summary>
public sealed record ContainerSlotPair(ContainerSlotItem? First, ContainerSlotItem? Second);

/// <summary>Builds a container that holds nothing and outlives nothing.</summary>
/// <remarks>
/// Shape comes from an existing container rather than from the type, because capacity is a fact
/// about the character. Handing it in also keeps the factory from having to know how any
/// particular container type is sized.
/// </remarks>
public interface IItemContainerFactory
{
    IItemContainer CreateTransient(ContainerId id, ContainerShape shape);
}
