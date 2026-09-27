using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Hosting;
using Dfo.Server.Networking;
using Dfo.Server.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.GameServer;

/// <summary>Composes game transport and protocol on the caller's host and service provider.</summary>
public static class GameServerHostingExtensions
{
    public static IHostBuilder ConfigureGameServer(this IHostBuilder builder) =>
        builder.ConfigureServices((context, services) =>
        {
            // Register preparation before transports, also when networking is disabled. StartingAsync
            // completes before ANY hosted service starts, including Kestrel and TCP listeners.
            services.AddHostedService<GameServerStartupService>();
            IServerModule[] modules = [new NetworkingModule(), new ProtocolModule()];
            foreach (var module in modules)
            {
                module.ConfigureServices(services, context.Configuration);
                services.AddSingleton(module);
            }
            services.AddSingleton<IGameServerReadiness, GameServerReadiness>();
        });
}
