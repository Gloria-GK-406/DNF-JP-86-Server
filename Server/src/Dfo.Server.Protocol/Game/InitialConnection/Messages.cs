using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.InitialConnection;

public sealed record GameInitialConnectionNotice(
    int ChannelId,
    DateTimeOffset PreparedAt) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameInitialConnection;
}
