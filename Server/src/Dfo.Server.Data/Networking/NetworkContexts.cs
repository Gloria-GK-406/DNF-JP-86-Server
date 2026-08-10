namespace Dfo.Server.Data.Networking;

public readonly record struct NetworkConnectionId(string Value)
{
    public override string ToString() => Value;
}

public enum NetworkEndpointKind
{
    Unknown = 0,
    Channel = 1,
    Game = 2
}

public sealed record NetworkConnectionContext(
    NetworkConnectionId ConnectionId,
    NetworkEndpointKind EndpointKind,
    int ListenerPort);

public sealed record NetworkDispatchContext(NetworkConnectionContext Connection)
{
    public static NetworkDispatchContext Unknown { get; } = new(
        new NetworkConnectionContext(new NetworkConnectionId(string.Empty), NetworkEndpointKind.Unknown, 0));
}
