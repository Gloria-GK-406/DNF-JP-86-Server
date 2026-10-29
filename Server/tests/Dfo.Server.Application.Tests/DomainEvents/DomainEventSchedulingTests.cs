using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.DomainEvents;

public sealed class DomainEventSchedulingTests
{
    [Fact]
    public async Task A_synchronously_consuming_group_with_continuous_backlog_cannot_prevent_other_groups_starting()
    {
        var probe = new SchedulingProbe();
        var services = new ServiceCollection().AddLogging();
        services.AddEventDispatching().AddSingleton(probe);
        services.AddDomainEvent<Work>("worker.probe", 1);
        services.AddDomainSubscriber<Work, BusySubscriber>("busy", DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<Work, OtherSubscriber>("other", DomainEventDeliveryPolicy.Reliable);
        services.AddSingleton<IDomainEventPersistence, SynchronousBacklog>();
        await using var provider = services.AddSessionSafetyStandIns().BuildServiceProvider();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // Run the adversarial fully synchronous loop on a test task so cancellation can diagnose starvation.
        var worker = Task.Run(() => provider.GetRequiredService<DomainEventDispatcher>().RunAsync(stop.Token));
        try { await Task.WhenAll(probe.BusyStarted.Task, probe.OtherStarted.Task).WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { await stop.CancelAsync(); await worker.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.True(probe.BusyCalls > 0);
    }

    private sealed record Work(int Amount) : IDomainEvent;
    private sealed class SchedulingProbe
    {
        public int BusyCalls;
        public bool OtherCompleted;
        public TaskCompletionSource BusyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OtherStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class BusySubscriber(SchedulingProbe probe) : IDomainEventSubscriber<Work>
    {
        public ValueTask HandleAsync(DomainEventEnvelope<Work> message, CancellationToken ct)
        { Interlocked.Increment(ref probe.BusyCalls); probe.BusyStarted.TrySetResult(); return ValueTask.CompletedTask; }
    }
    private sealed class OtherSubscriber(SchedulingProbe probe) : IDomainEventSubscriber<Work>
    {
        public ValueTask HandleAsync(DomainEventEnvelope<Work> message, CancellationToken ct)
        { probe.OtherStarted.TrySetResult(); return ValueTask.CompletedTask; }
    }

    // Models SQLite's synchronous async methods. The busy group always has a new due event.
    private sealed class SynchronousBacklog(SchedulingProbe probe) : IDomainEventPersistence
    {
        public ValueTask AppendAsync(StoredDomainEvent item, IReadOnlyList<DomainEventDeliveryTarget> targets, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<PendingDomainEventDelivery>> ReadDueAsync(string subscriberId, DateTimeOffset now, int limit, CancellationToken ct)
        {
            IReadOnlyList<PendingDomainEventDelivery> due = subscriberId == "other" && probe.OtherCompleted ? [] :
                [new(new(Guid.NewGuid(), Guid.NewGuid(), now, Guid.Empty, "worker.probe", 1, "{\"Amount\":1}"), subscriberId, null, 0)];
            return ValueTask.FromResult(due);
        }
        public ValueTask<DateTimeOffset?> NextDueAsync(string subscriberId, CancellationToken ct) => ValueTask.FromResult<DateTimeOffset?>(null);
        public ValueTask<bool> TryBeginAsync(Guid eventId, string subscriberId, DateTimeOffset now, CancellationToken ct) => ValueTask.FromResult(true);
        public ValueTask<bool> HasCompletedCoalescingKeyAsync(Guid batchId, string subscriberId, string key, CancellationToken ct) => ValueTask.FromResult(false);
        public ValueTask CompleteAsync(Guid eventId, string subscriberId, DateTimeOffset now, string reason, CancellationToken ct)
        { if (subscriberId == "other") probe.OtherCompleted = true; return ValueTask.CompletedTask; }
        public ValueTask<bool> QuarantineAsync(Guid id, string s, int expected, string error, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<bool> TryFailAsync(Guid id, string subscriber, int expected, DateTimeOffset next, string error, bool terminal, CancellationToken token) =>
            throw new NotSupportedException();
        public ValueTask FailAsync(Guid eventId, string subscriberId, DateTimeOffset nextAttemptAt, string error, bool terminal, CancellationToken ct) => throw new NotSupportedException();
    }
}
