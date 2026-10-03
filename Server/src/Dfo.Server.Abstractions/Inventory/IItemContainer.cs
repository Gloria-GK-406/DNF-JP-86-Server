using System.Globalization;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public readonly record struct ContainerOwner(InventoryOwnerType Scope, int Value);

/// <summary>Identifies a container independently of the character currently accessing it.</summary>
/// <remarks>
/// A string rather than a number because the identifiers come from unrelated places -- the
/// character's own storage, a dungeon run, a trade -- and a shared numeric space would make two of
/// them collide the moment a second producer appears.
/// </remarks>
public readonly record struct ContainerId(string Value)
{
    private const string CharacterScope = "character";
    private const string AccountScope = "account";

    /// <summary>The identifier of a character's own container of one type.</summary>
    public static ContainerId ForCharacter(CharacterId characterId, InventoryContainerType type) =>
        new(FormattableString.Invariant($"{CharacterScope}:{characterId.Value}:{type}"));

    public static ContainerId ForAccount(AccountId accountId, InventoryContainerType type) =>
        new(FormattableString.Invariant($"{AccountScope}:{accountId.Value}:{type}"));

    public static ContainerId ForAccountCargo(AccountId accountId) =>
        ForAccount(accountId, InventoryContainerType.AccountCargo);

    public bool TryGetAccountCargoOwner(out AccountId accountId)
    {
        accountId = default;
        if (!TryGetAccountOwner(out var owner, out var type) || type != InventoryContainerType.AccountCargo)
            return false;
        accountId = owner;
        return true;
    }

    public bool TryGetAccountOwner(out AccountId accountId, out InventoryContainerType type)
    {
        accountId = default;
        type = default;
        if (!TryGetOwner(out var owner, out var parsedType) || owner.Scope != InventoryOwnerType.Account)
            return false;
        accountId = new AccountId(owner.Value);
        type = parsedType;
        return true;
    }

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
        if (!TryGetOwner(out var owner, out var parsedType) || owner.Scope != InventoryOwnerType.Character)
            return false;
        characterId = new CharacterId(owner.Value);
        type = parsedType;
        return true;
    }

    /// <summary>Parses canonical persistent identity only; this does not grant business access.</summary>
    public bool TryGetOwner(out ContainerOwner containerOwner, out InventoryContainerType type)
    {
        containerOwner = default;
        type = default;
        var parts = Value?.Split(':');
        if (parts is not { Length: 3 }
            || parts[0] is not (CharacterScope or AccountScope)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var owner)
            || owner <= 0
            || owner.ToString(CultureInfo.InvariantCulture) != parts[1]
            || !Enum.TryParse<InventoryContainerType>(parts[2], out var parsedType)
            || !Enum.IsDefined(parsedType)
            || Enum.GetName(parsedType) != parts[2])
        {
            return false;
        }

        containerOwner = new(parts[0] == AccountScope ? InventoryOwnerType.Account : InventoryOwnerType.Character, owner);
        type = parsedType;
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

/// <summary>A physical container, with transaction-bound item occupancy references.</summary>
public interface IItemContainer
{
    ContainerId Id { get; }
    InventoryContainerType Type { get; }
    ContainerShape Shape { get; }
    bool IsRemovable { get; }
    ValueTask<ItemReference?> GetAsync(int index, CancellationToken ct);
    ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken ct);
    ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct);
    ValueTask<ItemReference> PutAsync(int index, ItemRecord record, CancellationToken ct);
    ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct);
    ValueTask FlushAsync(CancellationToken ct);
}

/// <summary>A business grant either joins an existing pile, opens a slot, or reports a refusal.</summary>
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

    /// <summary>The destination has no open empty slot for an automatic deposit.</summary>
    ContainerFull,
}

/// <summary>What became of a move.</summary>
public abstract record ItemMoveOutcome
{
    private ItemMoveOutcome()
    {
    }

    /// <param name="MoveCount">How much of the pile moved.</param>
    /// <param name="ItemId">What moved, for the caller to report.</param>
    public sealed record Moved(int MoveCount, int ItemId) : ItemMoveOutcome
    {
        /// <summary>Set when cargo selected a different slot from the requested occupied one.</summary>
        public BusinessSlot? ActualDestinationSlot { get; init; }
    }

    /// <summary>Both ends were the same slot: nothing happened and nothing is wrong.</summary>
    public sealed record Unchanged : ItemMoveOutcome;

    public sealed record Refused(ItemMoveRefusal Reason) : ItemMoveOutcome;
}
