using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.MercenaryInfo;

/// <summary>
/// CMD 0x01BA, the legacy <c>MERCENARY_INFO</c> query. Sent from the roster screen with an empty
/// body, and it is a query: the client registers a receive handler for the same type and reads it.
/// </summary>
public sealed record MercenaryInfoRequest(
    GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameMercenaryInfo;
}

public sealed record MercenaryInfoResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameMercenaryInfo;
}
