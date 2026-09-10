using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Data.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Infrastructure.Configuration;

internal sealed class ConfigurationPublicEndpointSource(PublicEndpointSnapshot snapshot)
    : IPublicEndpointSource
{
    public PublicEndpointSnapshot Snapshot { get; } = snapshot;
}

internal static class PublicEndpointConfiguration
{
    public static PublicEndpointSnapshot FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var channelBootstrap = new PublicNetworkEndpoint(
            ReadIpv4(configuration, "LauncherAuth:PublicChannel:Host"),
            ReadPort(configuration, "LauncherAuth:PublicChannel:Port"));

        var advertisedGames = configuration.GetSection("Channel:Channels").GetChildren()
            .Select(channel => new PublicNetworkEndpoint(
                ReadIpv4(channel, "Host", $"{channel.Path}:Host"),
                ReadPort(channel, "Port", $"{channel.Path}:Port")))
            .ToArray();
        if (advertisedGames.Length == 0)
        {
            throw new InvalidOperationException(
                "Required public endpoint configuration 'Channel:Channels' is missing.");
        }

        var listenerPorts = configuration.GetSection("Networking:GamePorts")
            .GetChildren()
            .Select(port => ReadPort(configuration, port.Path, port.Path))
            .ToArray();
        if (listenerPorts.Length != advertisedGames.Length)
        {
            throw new InvalidOperationException(
                "Required public endpoint configuration 'Channel:Channels' must map one-to-one " +
                "to 'Networking:GamePorts'.");
        }

        if (listenerPorts.Distinct().Count() != listenerPorts.Length)
        {
            throw new InvalidOperationException(
                "Required listener configuration 'Networking:GamePorts' contains duplicate ports.");
        }

        if (advertisedGames.Distinct().Count() != advertisedGames.Length)
        {
            throw new InvalidOperationException(
                "Required public endpoint configuration 'Channel:Channels' contains duplicate endpoints.");
        }

        var gameLogin = new PublicGameLoginEndpoints(
            ReadIpv4(configuration, "Game:EndpointHost"),
            ReadPort(configuration, "GameLogin:ConnectionPort"),
            ReadPort(configuration, "GameLogin:EndpointPortA"),
            ReadPort(configuration, "GameLogin:EndpointPortB"));

        return new PublicEndpointSnapshot(channelBootstrap, advertisedGames, gameLogin);
    }

    private static string ReadIpv4(IConfiguration configuration, string key) =>
        ReadIpv4(configuration, key, key);

    private static string ReadIpv4(
        IConfiguration configuration,
        string key,
        string displayKey)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value)
            || !IPAddress.TryParse(value, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException(
                $"Required public endpoint configuration '{displayKey}' must be an IPv4 address.");
        }

        return address.ToString();
    }

    private static int ReadPort(IConfiguration configuration, string key) =>
        ReadPort(configuration, key, key);

    private static int ReadPort(
        IConfiguration configuration,
        string key,
        string displayKey)
    {
        var value = configuration[key];
        if (!int.TryParse(value, out var port) || port is < 1 or > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"Required public endpoint configuration '{displayKey}' must be a valid TCP port.");
        }

        return port;
    }
}
