using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Tests.Scheduling;

internal delegate ValueTask TestScheduledTaskCallback(TestScheduledTaskContext context, CancellationToken token);
internal sealed record TestScheduledTaskContext(IServiceProvider Services, string TaskId, DateTimeOffset ScheduledAt, DateTimeOffset StartedAt, int Attempt);
internal sealed record TestScheduledTaskRegistration(string Id, IScheduledTaskSchedule Schedule, TestScheduledTaskCallback Execute)
{
    public ScheduledTaskMissedRunPolicy MissedRun { get; init; } = ScheduledTaskMissedRunPolicy.Skip;
    public int MaximumAttempts { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    internal ScheduledTaskRegistration Real => new(Id, Schedule, typeof(TestScheduledTaskHandler))
        { MissedRun = MissedRun, MaximumAttempts = MaximumAttempts, RetryDelay = RetryDelay };
}
internal sealed class TestScheduledTaskHandler(IServiceProvider services, IEnumerable<TestScheduledTaskRegistration> registrations) : IScheduledGameplayTask
{
    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(CommandExecutionContext execution, ScheduledTaskContext context, CancellationToken token)
    {
        await registrations.Single(x => x.Id == context.TaskId).Execute(new(services, context.TaskId, context.ScheduledAt, context.StartedAt, context.Attempt), token);
        return ScheduledTaskBusinessResult.Success;
    }
}
internal static class TestScheduledTasks
{
    internal static IServiceCollection AddTestScheduledTask(this IServiceCollection services, TestScheduledTaskRegistration registration) =>
        services.AddTestScheduledTaskFactory(_ => registration);
    internal static IServiceCollection AddTestScheduledTaskFactory(this IServiceCollection services, Func<IServiceProvider, TestScheduledTaskRegistration> factory)
    {
        services.AddTaskScheduling();
        services.TryAddScoped<TestScheduledTaskHandler>();
        var holder = new Holder(factory);
        services.AddSingleton<TestScheduledTaskRegistration>(sp => holder.Get(sp));
        services.AddSingleton<ScheduledTaskRegistration>(sp => holder.Get(sp).Real);
        return services;
    }
    private sealed class Holder(Func<IServiceProvider, TestScheduledTaskRegistration> factory)
    {
        private TestScheduledTaskRegistration? value;
        internal TestScheduledTaskRegistration Get(IServiceProvider services) => value ??= factory(services);
    }
    internal static async ValueTask<DateTimeOffset> RunActualAsync(IServiceProvider services, string id, CancellationToken token = default) =>
        (await services.GetRequiredService<ICommandBus>().SendInternalAsync(new ScheduledGameplayTaskCommand(id,
            new ScheduledTaskRunState { Requested = services.GetRequiredService<TimeProvider>().GetUtcNow() }, 1),
            new(Dfo.Server.Abstractions.Execution.GameplayCallSource.Timer,
                Dfo.Server.Abstractions.Execution.GameplayCallChain.NewRoot(Dfo.Server.Abstractions.Execution.GameplayCallSource.Timer),
                "timer:ScheduledTaskHostedService/" + id, InternalCommandCancellation.BodyOnly), token).ConfigureAwait(false)).ScheduledAt;
}
