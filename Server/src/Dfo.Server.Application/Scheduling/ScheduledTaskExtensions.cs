using Dfo.Server.Abstractions.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Scheduling;

public static class ScheduledTaskExtensions
{
    public static IServiceCollection AddTaskScheduling(this IServiceCollection services,
        ScheduledTaskSchedulerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        if (options is null) services.TryAddSingleton(new ScheduledTaskSchedulerOptions());
        else services.Replace(ServiceDescriptor.Singleton(options));
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
