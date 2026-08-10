namespace Dfo.Server.Data.Networking;

public enum ClientMessageType
{
    ChannelConnect = 1
}

public enum ServerMessageType
{
    ChannelConnect = 1
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
