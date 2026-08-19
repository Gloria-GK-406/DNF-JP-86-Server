using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>What a cooldown is recorded against.</summary>
/// <remarks>
/// A group number and an item identifier are two unrelated numbering spaces, so a bare integer key
/// would let cooldown group 70 and item 70 share a timer. The flag keeps them apart.
/// </remarks>
public readonly record struct ItemCooldownKey(bool IsShared, int Value)
{
    public static ItemCooldownKey ForGroup(int cooltimeGroup) => new(IsShared: true, cooltimeGroup);

    public static ItemCooldownKey ForItem(int itemId) => new(IsShared: false, itemId);
}

/// <summary>Which store an effect changes.</summary>
public enum ItemEffectTarget
{
    Container = 0,
    Character = 1,
    Account = 2,
}

/// <summary>One state change caused by using an item.</summary>
/// <remarks>
/// Building and sending the acknowledgement is not an effect. The client is told what happened;
/// that telling is the protocol's job, and an effect that wrote to a socket would be an effect no
/// test could apply in isolation.
/// </remarks>
public interface IItemUseEffect
{
    ItemEffectTarget Target { get; }
}

/// <summary>Takes items off the pile they were used from.</summary>
/// <remarks>
/// The pile is named by the slot it sits in, the same way everything else names one. An effect that
/// carried a storage row number instead would be an effect only one kind of container could carry
/// out, and the one a dungeon hands the player has no rows.
/// </remarks>
public sealed record ConsumeFromContainerEffect(int SlotIndex, int Count) : IItemUseEffect
{
    public ItemEffectTarget Target => ItemEffectTarget.Container;
}

/// <summary>Starts the item's cooldown against the character.</summary>
public sealed record StartItemCooldownEffect(ItemCooldownKey Key, TimeSpan Duration) : IItemUseEffect
{
    public ItemEffectTarget Target => ItemEffectTarget.Character;
}

/// <summary>What the store holds after the effects were applied.</summary>
public sealed record ItemUseApplication(int ItemId, int RemainingCount);

/// <summary>
/// Works out what using an item changes, and changes it.
/// </summary>
/// <remarks>
/// An item that survives its own use is expressed as an effect set without a consumption, not as a
/// condition guarding the decrement. The difference matters: the guard form leaves "use" meaning
/// "decrement, except sometimes", and every later effect has to be bolted onto the same exception.
/// </remarks>
public interface IItemUseEffectDomainService
{
    /// <summary>The effects using this item produces. Empty is a valid answer.</summary>
    ValueTask<IReadOnlyList<IItemUseEffect>> PlanAsync(
        int itemId,
        int slotIndex,
        CancellationToken cancellationToken);

    /// <summary>Applies the effects and answers what the slot holds afterwards.</summary>
    ValueTask<ItemUseApplication> ApplyAsync(
        CharacterId characterId,
        IItemContainer container,
        ContainerSlotItem item,
        IReadOnlyList<IItemUseEffect> effects,
        CancellationToken cancellationToken);
}

/// <summary>
/// How long ago each of a character's cooldowns started.
/// </summary>
/// <remarks>
/// Held in memory and read by comparing timestamps. There is no timer: a cooldown that has to fire
/// something when it ends needs one, and this one does not -- nothing happens when a cooldown
/// expires except that the next question gets a different answer.
///
/// Nothing is persisted, so a relog clears every cooldown. That is wrong for a thirty-minute
/// summon and unnoticeable for a ten-second potion, and the reference server has the same hole.
/// </remarks>
public interface ICharacterItemCooldownDomainService
{
    bool IsCoolingDown(CharacterId characterId, ItemCooldownKey key);

    void Start(CharacterId characterId, ItemCooldownKey key, TimeSpan duration);

    void Clear(CharacterId characterId);
}
