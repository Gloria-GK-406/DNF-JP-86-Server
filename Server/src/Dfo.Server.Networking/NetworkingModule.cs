using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Networking.Configuration;
using Dfo.Server.Networking.Framing;
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
        services.AddSingleton(GameLoginOptions.FromConfiguration(configuration));
        services.TryAddSingleton<TouchSocketConnectionRegistry>();
        services.TryAddSingleton<INetworkMessageSender, TouchSocketNetworkMessageSender>();
        services.TryAddSingleton<INetworkConnectionCloser, TouchSocketNetworkConnectionCloser>();
        services.TryAddSingleton<ChannelNetworkPlugin>();
        services.TryAddSingleton<GameNetworkPlugin>();
        services.TryAddSingleton<GameMessageDecoderRegistry>();
        services.TryAddSingleton<ChannelMessageDecoderRegistry>();
        services.TryAddSingleton<ServerMessageEncoderRegistry>();

        if (!options.Enabled)
        {
            return;
        }

        services.AddTcpService<IChannelTcpService, ChannelTcpService>(config => config
            .SetListenIPHosts(options.ChannelPort)
            .SetSingleStreamDataHandlingAdapter(() => new ChannelFrameDataHandlingAdapter())
            .ConfigurePlugins(plugins => plugins.Add<ChannelNetworkPlugin>()));

        services.AddTcpService<IGameTcpService, GameTcpService>(config => config
            .SetListenIPHosts(options.GamePorts.Select(port => new IPHost(port)).ToArray())
            .SetSingleStreamDataHandlingAdapter(() => new GameClientFrameDataHandlingAdapter())
            .ConfigurePlugins(plugins => plugins.Add<GameNetworkPlugin>()));
    }

    private static void ValidatePort(int port, string settingName)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{settingName} must contain ports between 1 and 65535.");
        }
    }
}
