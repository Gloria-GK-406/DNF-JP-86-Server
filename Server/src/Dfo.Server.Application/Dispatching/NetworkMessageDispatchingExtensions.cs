using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Dispatching;

public static class NetworkMessageDispatchingExtensions
{
    public static IServiceCollection AddNetworkMessageDispatching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddLogging();
        Commands.CommandDispatchingExtensions.AddCommandDispatching(services);
        services.TryAddSingleton<MessageHandlerRegistry>();
        services.TryAddSingleton<INetworkMessageDispatcher, NetworkMessageDispatcher>();
        services.TryAddSingleton<INetworkConnectionLifecycleDispatcher, NetworkConnectionLifecycleDispatcher>();
        services.TryAddSingleton<Commands.ICommandConnectionLifecycle, Commands.CommandConnectionLifecycle>();
        services.AddInternalCommandHandler<Commands.ConnectedLifecycleCommand, bool, Commands.ConnectedLifecycleCommandHandler>();
        services.AddInternalCommandHandler<Commands.DisconnectedLifecycleCommand, bool, Commands.DisconnectedLifecycleCommandHandler>();
        services.AddInternalCommandHandler<Commands.CloseLifecycleDepthRootCommand, bool, Commands.CloseLifecycleDepthRootCommandHandler>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, MessageHandlerRegistryValidationService>());
        return services;
    }

    public static IServiceCollection RegisterProtocolMessageHandler<THandler, TMessage, TCommand>(
        this IServiceCollection services, ClientMessageType messageType, params Type[] allowedMessageClrTypes)
        where THandler : class, IProtocolCommandMessageHandler<TCommand>
        where TMessage : class, IClientNetworkMessage
        where TCommand : class, Commands.IProtocolCommand
    {
        services.AddTransient<THandler>();
        services.AddSingleton(new MessageHandlerRegistration(messageType, typeof(THandler),
            Route: MessageHandlerRouteKind.ProtocolCommand, MessageClrType: typeof(TMessage), ProtocolCommandType: typeof(TCommand),
            AllowedMessageClrTypes: Array.AsReadOnly((allowedMessageClrTypes.Length == 0 ? [typeof(TMessage)] : allowedMessageClrTypes).ToArray())));
        return services;
    }

    /// <summary>
    /// Registers a pure telemetry handler (RQ-020, spec appendix B): it runs off the gameplay executor,
    /// so it may only inject logging; the registry refuses it at startup otherwise.
    /// </summary>
    public static IServiceCollection RegisterTelemetryMessageHandler<THandler>(
        this IServiceCollection services,
        ClientMessageType messageType)
        where THandler : class, IClientMessageHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<THandler>();
        services.AddSingleton(new MessageHandlerRegistration(messageType, typeof(THandler), IsTelemetry: true));
        return services;
    }
}

internal sealed class MessageHandlerRegistryValidationService(MessageHandlerRegistry registry) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = registry;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
