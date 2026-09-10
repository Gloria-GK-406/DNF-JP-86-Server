namespace Dfo.Server.Data.Configuration;

public sealed record PublicNetworkEndpoint(string Host, int Port);

public sealed record PublicGameLoginEndpoints(
    string Host,
    int ConnectionPort,
    int EndpointPortA,
    int EndpointPortB);

public sealed record PublicEndpointSnapshot(
    PublicNetworkEndpoint ChannelBootstrap,
    IReadOnlyList<PublicNetworkEndpoint> ChannelGameEndpoints,
    PublicGameLoginEndpoints GameLogin);
