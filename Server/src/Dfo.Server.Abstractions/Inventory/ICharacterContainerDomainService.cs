using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>
/// Which containers a character holds, and which one of each type it is currently using.
/// </summary>
/// <remarks>
/// Callers ask for the current container and get one; they never learn whether it is persistent,
/// transient, or something composed of both. That is the whole point of the seam: a dungeon run
/// registers its own container and uses it, and the rules for moving and using items carry on
/// unchanged.
///
/// Registration is how a container enters the picture, including the character's own. The first
/// one registered for a type becomes that type's current container, so ordinary entry needs no
/// separate "make this the default" step.
///
/// The registry is per character and does not survive the character leaving: this game has no
/// reconnect, so a disconnect is a clean reset rather than a state to be resumed.
/// </remarks>
public interface ICharacterContainerDomainService
{
    /// <summary>Adds a container to the character. The first of its type becomes the current one.</summary>
    /// <exception cref="InvalidOperationException">
    /// The character already holds a container under that identifier. Two containers answering to
    /// one name would make every later call ambiguous about which of them it meant.
    /// </exception>
    void Register(CharacterId characterId, IItemContainer container);

    /// <summary>
    /// Removes a container, falling back to the first non-removable one of its type when the
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

    /// <summary>The container the character is currently using for that type.</summary>
    /// <exception cref="InvalidOperationException">The character has no container of that type.</exception>
    IItemContainer GetContainer(CharacterId characterId, InventoryContainerType type);

    /// <summary>The container the character is currently using, or null when it holds none.</summary>
    IItemContainer? FindContainer(CharacterId characterId, InventoryContainerType type);

    /// <summary>Makes an already-registered container the current one for its type.</summary>
    /// <returns>False when the character holds no such container.</returns>
    bool UseContainer(CharacterId characterId, ContainerId containerId);

    /// <summary>Drops everything the character had registered.</summary>
    void Clear(CharacterId characterId);
}
