using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

/// <summary>The complete USERINFO projection, shared by all appearance changes.</summary>
internal static class CharacterUserStateProjection
{
    internal static IReadOnlyList<IGameServerMessage> Compose(SelectCharacterResult current, bool owner = true) =>
        Compose(current.Character, current.CharacterEntry.Public, current.CharacterEntry.Skills,
            current.CharacterInventory.Containers.SingleOrDefault(c => c.ContainerType == InventoryContainerType.Equipment)?.Items ?? [],
            current.CharacterEntry.Features.Pets, current.World.PreparedAt, owner);

    internal static IReadOnlyList<IGameServerMessage> Compose(ActiveCharacterState character,
        CharacterPublicEntryState state, CharacterSkillsEntryState skills, IReadOnlyList<InventoryItemState> equipment,
        IReadOnlyList<CharacterPetRuntimeState> pets, DateTimeOffset at, bool owner = true)
    {
        var messages = new List<GameStateSynchronizationMessage>
        {
            new CurrentUserStateMessage(character, state, equipment, pets),
        };
        // Subtype one is the owner's private detail view. Nearby players get the full public
        // subtype zero; dungeon entry has a separate routed subtype-one handshake.
        if (owner) messages.Add(new ExtendedUserStateMessage(character, state, skills, equipment, at));
        // Validate the complete set before any caller can send its first frame.
        foreach (var message in messages) _ = message.EncodeBody();
        return messages;
    }
}
