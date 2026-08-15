using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

public sealed record SelectCharacterRequest(
    GameMessageMetadata Metadata,
    ushort SlotIndex) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSelectCharacter;
}

public sealed record SelectCharacterResponse(
    Dfo.Server.Data.Game.SelectCharacterResult Result) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSelectCharacter;
}
