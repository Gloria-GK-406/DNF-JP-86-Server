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
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, MessageHandlerRegistryValidationService>());
        return services;
    }

    public static IServiceCollection RegisterMessageHandler<THandler>(
        this IServiceCollection services,
        ClientMessageType messageType)
        where THandler : class, IClientMessageHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<THandler>();
        services.AddSingleton(new MessageHandlerRegistration(messageType, typeof(THandler)));
        return services;
    }

    public static IServiceCollection RegisterProtocolMessageHandler<THandler, TMessage, TCommand>(
        this IServiceCollection services, ClientMessageType messageType)
        where THandler : class, IProtocolCommandMessageHandler<TCommand>
        where TMessage : class, IClientNetworkMessage
        where TCommand : class, Commands.IProtocolCommand
    {
        services.AddTransient<THandler>();
        services.AddSingleton(new MessageHandlerRegistration(messageType, typeof(THandler),
            Route: MessageHandlerRouteKind.ProtocolCommand, MessageClrType: typeof(TMessage), ProtocolCommandType: typeof(TCommand)));
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
