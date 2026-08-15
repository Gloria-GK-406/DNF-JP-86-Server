using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Connect;

public sealed record ChannelConnectRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelConnect;
}

public sealed record ChannelConnectResponse(string EncryptionKey) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelConnect;
}
