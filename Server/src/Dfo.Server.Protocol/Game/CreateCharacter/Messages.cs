using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.CreateCharacter;

/// <summary>
/// CMD 0x0005, <c>CREATE_CHARACTER</c>: job byte, then the length-prefixed name.
/// </summary>
/// <remarks>
/// <paramref name="RawName"/> is null when the body could not be parsed. As with the name check,
/// the decoder still yields a message so the client gets a refusal instead of silence.
/// </remarks>
public sealed record CreateCharacterRequest(
    GameMessageMetadata Metadata,
    byte Job,
    byte[]? RawName) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCreateCharacter;
}

public sealed record CreateCharacterResponse(CharacterCreationStatus Status) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCreateCharacter;
}
