using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Info;

public sealed record ChannelInfoRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelInfo;
}

public sealed record ChannelInfoResponse(
    IReadOnlyList<Dfo.Server.Data.Channel.ChannelServerInfo> Channels) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelInfo;
}
