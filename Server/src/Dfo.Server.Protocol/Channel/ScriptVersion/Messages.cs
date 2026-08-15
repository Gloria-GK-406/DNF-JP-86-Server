using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Channel.ScriptVersion;

public sealed record ChannelScriptVersionRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelScriptVersion;
}

public sealed record ChannelScriptVersionResponse(string Version) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelScriptVersion;
}
