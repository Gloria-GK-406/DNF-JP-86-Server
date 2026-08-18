using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// Holds each character's containers and which one of every type it is currently using.
/// </summary>
/// <remarks>
/// Registration order is the whole of the policy: the first container of a type is the one the
/// character starts on and the one it returns to, so entering a dungeon is a registration and a
/// switch rather than a rule about dungeons.
/// </remarks>
internal sealed class CharacterContainerDomainService : ICharacterContainerDomainService
{
    private readonly ConcurrentDictionary<int, CharacterContainers> byCharacter = new();

    public void Register(CharacterId characterId, IItemContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        byCharacter.GetOrAdd(characterId.Value, _ => new CharacterContainers()).Register(container);
    }

    public bool Remove(CharacterId characterId, ContainerId containerId) =>
        byCharacter.TryGetValue(characterId.Value, out var held) && held.Remove(containerId);

    public IReadOnlyList<IItemContainer> GetContainers(
        CharacterId characterId, InventoryContainerType type) =>
        byCharacter.TryGetValue(characterId.Value, out var held) ? held.OfType(type) : [];

    public IItemContainer GetContainer(CharacterId characterId, InventoryContainerType type) =>
        FindContainer(characterId, type)
        ?? throw new InvalidOperationException(
            $"Character {characterId.Value} has no {type} container.");

    public IItemContainer? FindContainer(CharacterId characterId, InventoryContainerType type) =>
        byCharacter.TryGetValue(characterId.Value, out var held) ? held.Current(type) : null;

    public bool UseContainer(CharacterId characterId, ContainerId containerId) =>
        byCharacter.TryGetValue(characterId.Value, out var held) && held.Use(containerId);

    public void Clear(CharacterId characterId) => byCharacter.TryRemove(characterId.Value, out _);

    /// <summary>
    /// One character's containers. Its methods run under its own lock rather than the dictionary's,
    /// because a switch reads the registration list and writes the binding together.
    /// </summary>
    private sealed class CharacterContainers
    {
        private readonly List<IItemContainer> registered = [];

        private readonly Dictionary<InventoryContainerType, ContainerId> current = [];

        private readonly object sync = new();

        public void Register(IItemContainer container)
        {
            lock (sync)
            {
                if (registered.Any(value => value.Id == container.Id))
                {
                    throw new InvalidOperationException(
                        $"Container {container.Id} is already registered.");
                }

                registered.Add(container);
                if (!current.ContainsKey(container.Type))
                    current[container.Type] = container.Id;
            }
        }

        /// <summary>
        /// Removes a container, moving the binding off it first when it was the one in use.
        /// </summary>
        /// <remarks>
        /// The fallback is found before anything is removed. A character bound to nothing has no
        /// way back, so the case where there is nothing to fall back to has to leave the character
        /// exactly as it was rather than partway through.
        /// </remarks>
        public bool Remove(ContainerId containerId)
        {
            lock (sync)
            {
                var container = registered.SingleOrDefault(value => value.Id == containerId);
                if (container is null)
                    return false;

                if (!container.IsRemovable)
                {
                    throw new InvalidOperationException(
                        $"Container {containerId} is the fallback for {container.Type} "
                            + "and cannot be removed.");
                }

                if (current.TryGetValue(container.Type, out var bound) && bound == containerId)
                {
                    var fallback = registered.FirstOrDefault(
                        value => value.Type == container.Type && !value.IsRemovable);
                    if (fallback is null)
                    {
                        throw new InvalidOperationException(
                            $"Removing {containerId} would leave the character with no "
                                + $"{container.Type} container.");
                    }

                    current[container.Type] = fallback.Id;
                }

                registered.Remove(container);
                return true;
            }
        }

        public IReadOnlyList<IItemContainer> OfType(InventoryContainerType type)
        {
            lock (sync)
                return registered.Where(value => value.Type == type).ToArray();
        }

        public IItemContainer? Current(InventoryContainerType type)
        {
            lock (sync)
            {
                return current.TryGetValue(type, out var containerId)
                    ? registered.SingleOrDefault(value => value.Id == containerId)
                    : null;
            }
        }

        public bool Use(ContainerId containerId)
        {
            lock (sync)
            {
                var container = registered.SingleOrDefault(value => value.Id == containerId);
                if (container is null)
                    return false;
                current[container.Type] = containerId;
                return true;
            }
        }
    }
}
