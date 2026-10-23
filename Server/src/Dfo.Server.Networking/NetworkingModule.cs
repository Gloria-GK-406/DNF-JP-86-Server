using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Networking.Configuration;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Outbound;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TouchSocket.Core;
using TouchSocket.Hosting;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking;

public sealed class NetworkingModule : IServerModule
{
    public string Name => "Networking";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(NetworkingOptions.SectionName);
        var gamePorts = section.GetSection(nameof(NetworkingOptions.GamePorts))
            .GetChildren()
            .Select(value => int.TryParse(value.Value, out var port) ? port : -1)
            .ToArray();
        var options = new NetworkingOptions
        {
            Enabled = bool.TryParse(section[nameof(NetworkingOptions.Enabled)], out var enabled) && enabled,
            UdpDiscoveryEnabled = bool.TryParse(
                    section[nameof(NetworkingOptions.UdpDiscoveryEnabled)], out var udpDiscoveryEnabled)
                && udpDiscoveryEnabled,
            ChannelPort = int.TryParse(section[nameof(NetworkingOptions.ChannelPort)], out var channelPort)
                ? channelPort
                : 7001,
            GamePorts = gamePorts.Length == 0 ? new[] { 10011 } : gamePorts
        };

        ValidatePort(options.ChannelPort, $"{NetworkingOptions.SectionName}:{nameof(NetworkingOptions.ChannelPort)}");
        foreach (var gamePort in options.GamePorts)
        {
            ValidatePort(gamePort, $"{NetworkingOptions.SectionName}:{nameof(NetworkingOptions.GamePorts)}");
        }

        services.AddSingleton(options);
        services.TryAddSingleton<TouchSocketConnectionRegistry>();
        services.TryAddSingleton<INetworkConnectionContexts>(
            provider => provider.GetRequiredService<TouchSocketConnectionRegistry>());
        // The sending side of the outbox (RQ-032 to RQ-037), registered with networking disabled too:
        // with no connection registered every entry is dropped as stale. Sends and closes themselves
        // are registered by the Application outbox adapters, never by the transport.
        services.TryAddSingleton(OutboundSendOptions.Default);
        services.TryAddSingleton<IOutboundTransport, TouchSocketOutboundTransport>();
        services.TryAddSingleton<IOutboundMessageEncoder, RegistryOutboundEncoder>();
        services.TryAddSingleton<IOutboundSender, OutboundSender>();
        services.TryAddSingleton<ChannelNetworkPlugin>();
        services.TryAddSingleton<GameNetworkPlugin>();
        services.TryAddSingleton<GameMessageDecoderRegistry>();
        services.TryAddSingleton<ChannelMessageDecoderRegistry>();
        services.TryAddSingleton<ServerMessageEncoderRegistry>();
        // Registered with networking disabled too: an empty registry drains at once.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGameplayProducerDrain, TouchSocketTransportDrain>());

        if (!options.Enabled)
        {
            return;
        }

        // Both services confirm flushes, so a delivered entry has left the send pipe before a later
        // close of its connection runs (RQ-012); the outbound transport refuses a session without it.
        services.AddTcpService<IChannelTcpService, ChannelTcpService>(config => OutboundTransportOptions.ConfigureSendPipe(config
            .SetListenIPHosts(options.ChannelPort)
            .SetSingleStreamDataHandlingAdapter(() => new ChannelFrameDataHandlingAdapter())
            .ConfigurePlugins(plugins => plugins.Add<ChannelNetworkPlugin>())));

        services.AddTcpService<IGameTcpService, GameTcpService>(config => OutboundTransportOptions.ConfigureSendPipe(config
            .SetListenIPHosts(options.GamePorts.Select(port => new IPHost(port)).ToArray())
            .SetSingleStreamDataHandlingAdapter(() => new GameClientFrameDataHandlingAdapter())
            .ConfigurePlugins(plugins => plugins.Add<GameNetworkPlugin>())));

        if (options.UdpDiscoveryEnabled)
        {
            services.AddHostedService<A21UdpDiscoveryService>();
        }
    }

    private static void ValidatePort(int port, string settingName)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{settingName} must contain ports between 1 and 65535.");
        }
    }
}
