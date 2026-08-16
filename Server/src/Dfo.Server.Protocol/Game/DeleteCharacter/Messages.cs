using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DeleteCharacter;

/// <summary>
/// CMD 0x0006, <c>DELETE_CHARACTER</c>: slot index, then the length-prefixed name as confirmation.
/// </summary>
/// <remarks>
/// The slot is a <c>u16</c>, which a real request settled. Both this server and the reference read
/// it as a single byte and took the name length from offset 1, which lands on the slot's high byte
/// and yields a nonsense length -- so deletion could never have worked on either. The reference
/// presumably never exercised it.
/// </remarks>
public sealed record DeleteCharacterRequest(
    GameMessageMetadata Metadata,
    ushort SlotIndex,
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
