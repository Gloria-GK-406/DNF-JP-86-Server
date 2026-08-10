namespace Dfo.Server.Data.Networking;

public enum ClientMessageType
{
    ChannelScriptVersion = 0x05,
    ChannelScript = 0x09,
    ChannelConnect = 0x0B,
    ChannelInfo = 0x11,
    GameExit = 0x0001_0003,
}

public enum ServerMessageType
{
    ChannelScriptVersion = 0x06,
    ChannelScript = 0x0A,
    ChannelConnect = 0x0C,
    ChannelInfo = 0x12,
    GameExit = 0x0001_0003,
}

public interface IClientNetworkMessage
{
    ClientMessageType MessageType { get; }
}

public interface IServerNetworkMessage
{
    ServerMessageType MessageType { get; }
}

public sealed record ChannelMessageMetadata(
    byte Classification,
    byte MessageNumber,
    uint Checksum,
    byte Acknowledgement)
{
    public static ChannelMessageMetadata Empty { get; } = new(0, 0, 0, 0);
}

public sealed record ChannelConnectRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelConnect;
}

public sealed record ChannelConnectResponse(string EncryptionKey) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelConnect;
}

public sealed record ChannelScriptVersionRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelScriptVersion;
}

public sealed record ChannelScriptVersionResponse(string Version) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelScriptVersion;
}

public sealed record ChannelScriptRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelScript;
}

public sealed record ChannelScriptResponse(byte[] Content) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelScript;
}

public sealed record ChannelInfoRequest(ChannelMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.ChannelInfo;
}

public sealed record ChannelInfoResponse(
    IReadOnlyList<Dfo.Server.Data.Channel.ChannelServerInfo> Channels) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.ChannelInfo;
}

public sealed record GameMessageMetadata(
    byte Command,
    ushort Type,
    uint Checksum,
    ushort Sequence)
{
    public static GameMessageMetadata Empty { get; } = new(0, 0, 0, 0);
}

public sealed record GameExitRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameExit;
}

public sealed record GameExitResponse(bool Succeeded) : IServerNetworkMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameExit;
}
