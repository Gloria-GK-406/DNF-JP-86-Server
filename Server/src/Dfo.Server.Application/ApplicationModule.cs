using Dfo.Server.Abstractions;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Channel;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application;

public sealed class ApplicationModule : IServerModule
{
    public string Name => "Application";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddCommandDispatching();
        services.RegisterCommandHandler<EstablishChannelConnectionCommandHandler>(
            ApplicationCommandType.EstablishChannelConnection,
            CommandExecutionKind.Direct);
        services.AddNetworkMessageDispatching();
        services.RegisterMessageHandler<ChannelConnectHandler>(ClientMessageType.ChannelConnect);
    }
}
