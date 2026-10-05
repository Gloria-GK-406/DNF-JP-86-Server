using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>
/// Which containers a character holds, and which one of each type it is currently using.
/// </summary>
/// <remarks>
/// Ordinary operations use the current binding; isDefault=true selects its declared fallback.
/// Default is a binding role, not a storage lifetime. Each type has at most one default.
/// Registration, replacement, binding changes and removal join the ambient domain transaction.
///
/// The registry is per character and does not survive the character leaving: this game has no
/// reconnect, so a disconnect is a clean reset rather than a state to be resumed.
/// </remarks>
public interface ICharacterContainerDomainService
{
    /// <summary>Adds the whole quantity, resolves the destination internally, or throws without partial delivery.</summary>
    ValueTask<ContainerAddition> AddAsync(CharacterId characterId, int itemId, int count,
        CancellationToken cancellationToken) => throw new NotSupportedException("This container registry cannot add items.");

    /// <summary>Explicit offline delivery to persistent storage, independent of online bindings.</summary>
    ValueTask<ContainerAddition> AddToPersistentAsync(CharacterId characterId, int itemId, int count,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    /// <summary>Adds a container to the character. The first of its type becomes the current one.</summary>
    /// <exception cref="InvalidOperationException">
    /// The character already holds a container under that identifier. Two containers answering to
    /// one name would make every later call ambiguous about which of them it meant.
    /// </exception>
    void Register(CharacterId characterId, IItemContainer container);

    /// <summary>Atomically replaces an existing default; keeps a separately selected current container.</summary>
    void ReplaceDefault(CharacterId characterId, ContainerId expectedDefaultId, IItemContainer replacement) =>
        throw new NotSupportedException();

    /// <summary>
    /// Removes a container, falling back to its type's declared default when the
    /// removed container was current.
    /// </summary>
    /// <returns>False when the character held no such container.</returns>
    /// <exception cref="InvalidOperationException">
    /// The container was current and its type has nothing to fall back to, or the container is one
    /// that may not be removed. Leaving the character with no current container is a state nothing
    /// recovers from, so both are raised rather than represented.
    /// </exception>
    bool Remove(CharacterId characterId, ContainerId containerId);

    /// <summary>Every container the character holds of that type, in registration order.</summary>
    IReadOnlyList<IItemContainer> GetContainers(CharacterId characterId, InventoryContainerType type);

    /// <summary>The current container, or the declared default when isDefault is true.</summary>
    /// <exception cref="InvalidOperationException">The character has no container of that type.</exception>
    IItemContainer GetContainer(CharacterId characterId, InventoryContainerType type, bool isDefault = false);

    /// <summary>The selected current/default container, or null when none is registered.</summary>
    IItemContainer? FindContainer(CharacterId characterId, InventoryContainerType type, bool isDefault = false);

    /// <summary>Makes an already-registered container the current one for its type.</summary>
    /// <returns>False when the character holds no such container.</returns>
    bool UseContainer(CharacterId characterId, ContainerId containerId);

    /// <summary>Drops everything the character had registered.</summary>
    void Clear(CharacterId characterId);
}

public sealed record ContainerItemAddition(BusinessContainerId Container, BusinessSlot Slot, int Count);
public sealed record ContainerAddition(IReadOnlyList<ContainerItemAddition> Items);
public enum ContainerAdditionFailure { UnknownItem, NoRoom, CharacterNotFound }
public sealed class ContainerAdditionException(ContainerAdditionFailure reason)
    : InvalidOperationException($"Container addition refused: {reason}.")
{
    public ContainerAdditionFailure Reason { get; } = reason;
}
