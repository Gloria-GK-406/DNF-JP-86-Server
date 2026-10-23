using System.Reflection;
using Dfo.Server.Abstractions.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Notifications;

public static class NotificationDispatchingExtensions
{
    public static IServiceCollection AddNotificationDispatching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<INotificationAudienceResolver, NotificationAudienceResolver>();
        services.TryAddSingleton<NotificationHandlerRegistry>();
        services.TryAddSingleton<NotificationBus>();
        services.TryAddSingleton<INotificationBus>(
            provider => provider.GetRequiredService<NotificationBus>());
        // Registered here, in the module the host applies before the one that opens the listeners,
        // so a missing handler stops the server while nobody is connected rather than after the
        // first player is. The host builds its hosted services before starting any of them, so
        // this throws before anything is running whatever the order; see
        // NotificationRegistrationTests for both halves of that.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, NotificationHandlerRegistryValidationService>());
        return services;
    }

    /// <summary>Declares that every notification type in this assembly must have a handler.</summary>
    public static IServiceCollection AddNotificationCatalog(
        this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);
        services.AddSingleton(new NotificationCatalogRegistration(assembly));
        return services;
    }

    /// <summary>
    /// Registers the handler for a notification. The two type arguments must agree: the handler
    /// must handle this notification, or the call does not compile. A publish is composed once per
    /// recipient and never retried (VC-5).
    /// </summary>
    public static IServiceCollection AddNotificationHandler<TNotification, THandler>(
        this IServiceCollection services)
        where TNotification : class, INotification<TNotification>
        where THandler : class, INotificationHandler<TNotification>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<INotificationHandler<TNotification>, THandler>();
        services.AddScoped<NotificationComposer<TNotification>>();
        services.AddSingleton(new NotificationHandlerRegistration(
            typeof(TNotification),
            typeof(THandler),
            typeof(NotificationComposer<TNotification>)));
        return services;
    }
}

internal sealed class NotificationHandlerRegistryValidationService(
    NotificationHandlerRegistry registry) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = registry;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
