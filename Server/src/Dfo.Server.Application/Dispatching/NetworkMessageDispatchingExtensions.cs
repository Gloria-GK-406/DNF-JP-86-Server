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
        services.TryAddSingleton<MessageHandlerRegistry>();
        services.TryAddSingleton<INetworkMessageDispatcher, NetworkMessageDispatcher>();
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
