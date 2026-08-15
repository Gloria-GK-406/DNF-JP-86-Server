using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Login;

public sealed record GameLoginRequest(
    GameMessageMetadata Metadata,
    string LoginId,
    string PasswordHash) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameLogin;
}

public sealed record GameLoginResponse(bool Succeeded) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLogin;
}
