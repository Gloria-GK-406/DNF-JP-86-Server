using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DeleteCharacter;

/// <summary>
/// CMD 0x0006, <c>DELETE_CHARACTER</c>: slot index, then the length-prefixed name as confirmation.
/// </summary>
public sealed record DeleteCharacterRequest(
    GameMessageMetadata Metadata,
    byte SlotIndex,
    byte[]? RawName) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameDeleteCharacter;
}

public sealed record DeleteCharacterResponse(
    CharacterDeletionStatus Status,
    CharacterId? CharacterId) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameDeleteCharacter;
}
