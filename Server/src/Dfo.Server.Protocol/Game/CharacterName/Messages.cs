using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.CharacterName;

/// <summary>
/// CMD 0x02B5, <c>CHECK_DOUBLE_CHARACTER_NAME</c>: is this name free?
/// </summary>
/// <remarks>
/// <paramref name="RawName"/> is null when the body was too short to carry a name at all. The
/// decoder still produces the message in that case rather than returning null, because a dropped
/// frame leaves the client waiting forever on a reply it is holding a dialog open for. Refusing
/// loudly is the correct answer to a malformed request; silence is not.
/// </remarks>
public sealed record CheckCharacterNameRequest(
    GameMessageMetadata Metadata,
    byte[]? RawName) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCheckCharacterName;
}

public sealed record CheckCharacterNameResponse(CharacterNameStatus Status) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCheckCharacterName;
}
