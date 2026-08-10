using Dfo.Server.Abstractions;
using Dfo.Server.Networking.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TouchSocket.Core;
using TouchSocket.Hosting;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking;

public sealed class NetworkingModule : IServerModule
{
    public string Name => "Networking";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(NetworkingOptions.SectionName);
        var options = new NetworkingOptions
        {
            Enabled = bool.TryParse(section[nameof(NetworkingOptions.Enabled)], out var enabled) && enabled,
            ListenPort = int.TryParse(section[nameof(NetworkingOptions.ListenPort)], out var port) ? port : 7001
        };

        if (options.ListenPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("Networking:ListenPort must be between 1 and 65535.");
        }

        services.AddSingleton(options);

        if (options.Enabled)
        {
            services.AddTcpService(config => config.SetListenIPHosts(options.ListenPort));
        }
    }
}
