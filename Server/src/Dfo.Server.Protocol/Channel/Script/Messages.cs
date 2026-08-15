using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.Script;

public sealed record ChannelScriptRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelScript;
}

public sealed record ChannelScriptResponse(byte[] Content) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelScript;
}
