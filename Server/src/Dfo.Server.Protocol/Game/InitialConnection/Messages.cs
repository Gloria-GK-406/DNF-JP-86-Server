using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.InitialConnection;

public sealed record GameInitialConnectionNotice(
    int ChannelId,
    DateTimeOffset PreparedAt) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameInitialConnection;
}
