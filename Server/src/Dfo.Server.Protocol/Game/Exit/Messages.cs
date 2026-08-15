using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Exit;

public sealed record GameExitRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameExit;
}

public sealed record GameExitResponse(bool Succeeded) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameExit;
}
