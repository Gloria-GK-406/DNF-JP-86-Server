using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Channel;
using Dfo.Server.Application.Commands.Game;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Game;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Users;
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
        services.TryAddSingleton(
            provider => new UserService(
                () => provider.GetRequiredService<IAccountRepository>()));
        services.TryAddSingleton<IAccountService>(
            provider => provider.GetRequiredService<UserService>());
        services.TryAddSingleton<IOnlineUserService>(
            provider => provider.GetRequiredService<UserService>());
        services.TryAddSingleton<IOnlineUserQueryService>(
            provider => provider.GetRequiredService<UserService>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<INetworkConnectionObserver, OnlineUserConnectionObserver>());
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
        services.RegisterCommandHandler<AcknowledgeGameExitCommandHandler>(
            ApplicationCommandType.AcknowledgeGameExit,
            CommandExecutionKind.Direct);
        services.RegisterCommandHandler<LoginGameUserCommandHandler>(
            ApplicationCommandType.LoginGameUser,
            CommandExecutionKind.Direct,
            OnlineUserAccessMode.Exclusive);
        services.AddNetworkMessageDispatching();
        services.RegisterMessageHandler<ChannelConnectHandler>(ClientMessageType.ChannelConnect);
        services.RegisterMessageHandler<ChannelScriptVersionHandler>(ClientMessageType.ChannelScriptVersion);
        services.RegisterMessageHandler<ChannelScriptHandler>(ClientMessageType.ChannelScript);
        services.RegisterMessageHandler<ChannelInfoHandler>(ClientMessageType.ChannelInfo);
        services.RegisterMessageHandler<GameExitHandler>(ClientMessageType.GameExit);
        services.RegisterMessageHandler<GameLoginHandler>(ClientMessageType.GameLogin);
    }
}
