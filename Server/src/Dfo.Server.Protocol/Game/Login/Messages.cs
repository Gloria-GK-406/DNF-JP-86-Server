using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Login;

public sealed record GameLoginRequest(
    GameMessageMetadata Metadata,
    string LoginId,
    string Ticket) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameLogin;
}

public sealed record GameLoginResponse(
    bool Succeeded,
    byte Environment,
    int ConnectionPort,
    string EndpointHost,
    int EndpointPortA,
    int EndpointPortB) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLogin;
}
