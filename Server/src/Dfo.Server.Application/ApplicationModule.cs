using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Channel;
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
        services.TryAddSingleton<IChannelEncryptionKeyProvider, ChannelEncryptionKeyProvider>();
        services.AddCommandDispatching();
        services.RegisterCommandHandler<EstablishChannelConnectionCommandHandler>(
            ApplicationCommandType.EstablishChannelConnection,
            CommandExecutionKind.Direct);
        services.RegisterCommandHandler<GetChannelScriptVersionCommandHandler>(
            ApplicationCommandType.GetChannelScriptVersion,
            CommandExecutionKind.Direct);
        services.RegisterCommandHandler<GetChannelScriptCommandHandler>(
            ApplicationCommandType.GetChannelScript,
            CommandExecutionKind.Direct);
        services.RegisterCommandHandler<GetChannelInfoCommandHandler>(
            ApplicationCommandType.GetChannelInfo,
            CommandExecutionKind.Direct);
        services.AddNetworkMessageDispatching();
        services.RegisterMessageHandler<ChannelConnectHandler>(ClientMessageType.ChannelConnect);
        services.RegisterMessageHandler<ChannelScriptVersionHandler>(ClientMessageType.ChannelScriptVersion);
        services.RegisterMessageHandler<ChannelScriptHandler>(ClientMessageType.ChannelScript);
        services.RegisterMessageHandler<ChannelInfoHandler>(ClientMessageType.ChannelInfo);
    }
}
