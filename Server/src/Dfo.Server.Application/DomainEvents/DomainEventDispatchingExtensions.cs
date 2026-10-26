using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.DomainEvents;

public static class DomainEventDispatchingExtensions
{
    public static IServiceCollection AddEventDispatching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCommandDispatching();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(DomainEventDispatchOptions.Default);
        services.TryAddSingleton<DomainEventInstance>();
        services.TryAddSingleton<DomainEventRegistry>();
        services.TryAddSingleton<DomainEventWakeups>();
        services.TryAddSingleton<DomainEventCallChains>();
        services.TryAddScoped(typeof(NotificationProjectionSubscriber<>));
        services.TryAddSingleton<EventService>();
        services.TryAddSingleton<IEventService>(provider => provider.GetRequiredService<EventService>());
        services.TryAddSingleton<ICommandProjections>(provider => provider.GetRequiredService<EventService>());
        services.TryAddSingleton<DomainEventDispatcher>();
        services.TryAddScoped<ICommandDomainEventConsumption, CommandDomainEventConsumption>();
        if (!services.Any(x => x.ServiceType == typeof(ICommandHandler<ConsumeDomainEventCommand, bool>)))
        {
            services.AddInternalCommandHandler<ConsumeDomainEventCommand, bool, ConsumeDomainEventCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
            services.AddInternalCommandHandler<SettleDepthExceededDomainEventCommand, string, SettleDepthExceededDomainEventCommandHandler>();
        }
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DomainEventHostedService>());
        return services;
    }

    public static IServiceCollection AddDomainEvent<TEvent>(this IServiceCollection services,
        string stableName, int version) where TEvent : class, IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableName);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        services.AddSingleton<IDomainEventRegistration>(new DomainEventRegistration<TEvent>(stableName, version));
        return services;
    }

    public static IServiceCollection AddDomainSubscriber<TEvent, THandler>(this IServiceCollection services,
        string subscriberId, DomainEventDeliveryPolicy policy,
        Func<TEvent, string?>? coalescingKey = null)
        where TEvent : class, IDomainEvent where THandler : class, IDomainEventSubscriber<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        if (coalescingKey is not null && policy != DomainEventDeliveryPolicy.LiveOnly)
            throw new ArgumentException("Reliable subscribers consume every fact and cannot coalesce.", nameof(coalescingKey));
        services.TryAddScoped<THandler>();
        services.AddSingleton(new DomainSubscriberRegistration(typeof(TEvent), typeof(THandler), subscriberId, policy,
            payload => coalescingKey?.Invoke((TEvent)payload),
            (provider, envelope, ct) => provider.GetRequiredService<THandler>()
                .HandleAsync((DomainEventEnvelope<TEvent>)envelope, ct)));
        return services;
    }

    public static IServiceCollection AddRuntimeEventSubscriber<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IDomainEvent where THandler : class, IDomainEventSubscriber<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<THandler>();
        services.AddSingleton(new RuntimeSubscriberRegistration(typeof(TEvent), typeof(THandler),
            (provider, envelope, ct) => provider.GetRequiredService<THandler>()
                .HandleAsync((DomainEventEnvelope<TEvent>)envelope, ct)));
        return services;
    }
}

public sealed record DomainEventDispatchOptions(int BatchSize, int MaximumAttempts,
    TimeSpan RetryDelay, TimeSpan MaximumRetryDelay, TimeSpan LiveEventLifetime, TimeSpan RecoveryInterval)
{
    public static DomainEventDispatchOptions Default { get; } = new(64, 8,
        TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    internal void Validate()
    {
        if (BatchSize <= 0 || MaximumAttempts <= 0 || RetryDelay <= TimeSpan.Zero
            || MaximumRetryDelay < RetryDelay || LiveEventLifetime <= TimeSpan.Zero || RecoveryInterval <= TimeSpan.Zero)
            throw new InvalidOperationException("Domain event dispatch options must have positive bounds and delays.");
    }
}
