using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Scheduling;

public static class ScheduledTaskExtensions
{
    public static IServiceCollection AddTaskScheduling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Every attempt runs on the executor, so a container with a scheduler has one.
        services.AddCommandDispatching();
        services.TryAddScoped<ICommandScheduledTaskDispatch, CommandScheduledTaskDispatch>();
        if (!services.Any(x => x.ServiceType == typeof(ICommandHandler<ScheduledGameplayTaskCommand, ScheduledGameplayTaskResult>)))
            services.AddInternalCommandHandler<ScheduledGameplayTaskCommand, ScheduledGameplayTaskResult, ScheduledGameplayTaskCommandHandler>(internalActionCancellation: InternalActionCancellationCapability.ConfirmedRollbackWriteResult);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ScheduledTaskHostedService>());
        return services;
    }

    public static IServiceCollection AddScheduledTask(this IServiceCollection services, ScheduledTaskRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        services.AddTaskScheduling();
        services.AddSingleton(registration);
        return services;
    }

    public static IServiceCollection AddScheduledTaskFactory(this IServiceCollection services,
        Func<IServiceProvider, ScheduledTaskRegistration> registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        services.AddTaskScheduling();
        services.AddSingleton(registration);
        return services;
    }
}
