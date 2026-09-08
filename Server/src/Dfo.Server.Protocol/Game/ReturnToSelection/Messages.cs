using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ReturnToSelection;

public sealed record ReturnToSelectionRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameReturnToSelection;
}

public sealed record ReturnToSelectionResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameReturnToSelection;
}
