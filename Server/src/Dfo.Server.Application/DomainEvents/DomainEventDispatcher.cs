using System.Text.Json;
using System.Threading.Channels;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.DomainEvents;

internal sealed class DomainEventWakeups(DomainEventRegistry registry)
{
    private readonly Dictionary<string, Channel<byte>> channels = registry.Groups.Keys.ToDictionary(id => id,
        _ => System.Threading.Channels.Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite,
            AllowSynchronousContinuations = false
        }), StringComparer.Ordinal);
    public ChannelReader<byte> Reader(string subscriberId) => channels[subscriberId].Reader;
    public void WakeAll() { foreach (var channel in channels.Values) channel.Writer.TryWrite(0); }
}

/// <summary>Each subscriber ID is serial; retries can be overtaken by later due facts.</summary>
/// <remarks>
/// The dispatcher is a producer (RQ-041): it reads due deliveries off the executor (read-only SQLite) and
/// consumes each one as its own event CMD, waiting for it before the next. A group stays serial because
/// it has one drain loop, and that loop waits for each event CMD before it queues the next. A test that
/// drains a group from its own thread while the loop runs interleaves the reads, not the consumption:
/// each delivery is still consumed once, and a backed-off one is not retried early, because
/// <c>TryBeginAsync</c> claims a delivery only while it is pending and due.
/// </remarks>
internal sealed class DomainEventDispatcher
{
    private readonly DomainEventRegistry registry;
    private readonly IServiceProvider services;
    private readonly IServiceScopeFactory scopes;
    private readonly DomainTransactionScope transactions;
    private readonly DomainEventWakeups wakeups;
    private readonly DomainEventInstance instance;
    private readonly TimeProvider clock;
    private readonly DomainEventDispatchOptions options;
    private readonly ILogger<DomainEventDispatcher> logger;
    private readonly IGameplayQueue queue;
    private IDomainEventPersistence Persistence => services.GetRequiredService<IDomainEventPersistence>();

    public DomainEventDispatcher(DomainEventRegistry registry, IServiceProvider services, IServiceScopeFactory scopes,
        DomainTransactionScope transactions, DomainEventWakeups wakeups,
        DomainEventInstance instance, TimeProvider clock, DomainEventDispatchOptions options,
        ILogger<DomainEventDispatcher> logger, IGameplayQueue queue)
    {
        options.Validate();
        this.registry = registry; this.services = services; this.scopes = scopes; this.transactions = transactions;
        this.wakeups = wakeups; this.instance = instance; this.clock = clock;
        this.options = options; this.logger = logger; this.queue = queue;
    }

    internal async ValueTask<int> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        var processed = 0;
        foreach (var id in registry.Groups.Keys)
            processed += await DrainOnceAsync(id, cancellationToken).ConfigureAwait(false);
        return processed;
    }

    internal async ValueTask<int> DrainOnceAsync(string subscriberId, CancellationToken cancellationToken = default)
    {
        if (transactions.Current is not null)
            throw new InvalidOperationException("Dispatch begins outside the publishing business transaction.");
        // Draining from inside a job would queue event CMDs and then wait for them behind itself (RQ-003).
        GameplayJob.AssertNotOnExecutor("Draining domain event deliveries");
        var due = await Persistence.ReadDueAsync(subscriberId, clock.GetUtcNow(), options.BatchSize, cancellationToken)
            .ConfigureAwait(false);
        var processed = 0;
        foreach (var delivery in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var consumed = false;
            await GameplayTimerWork.RunAsync(queue, CommandTypeOf(delivery), async ct =>
            {
                consumed = await ProcessAsync(delivery, ct).ConfigureAwait(false);
            }, cancellationToken, SourceOf(delivery)).ConfigureAwait(false);
            if (consumed) processed++;
        }
        return processed;
    }

    // Transitional: every delivery is a new root. T-007 gives it the publishing CMD's chain instead.
    private GameplayCallSource SourceOf(PendingDomainEventDelivery delivery) =>
        delivery.Event.OriginInstanceId == instance.Id ? GameplayCallSource.Event : GameplayCallSource.StartupRecovery;

    private string CommandTypeOf(PendingDomainEventDelivery delivery) =>
        (SourceOf(delivery) == GameplayCallSource.Event ? "event:" : "startup:event/") + delivery.SubscriberId;

    private async ValueTask<bool> ProcessAsync(PendingDomainEventDelivery delivery, CancellationToken ct)
    {
        try
        {
            var type = registry.For(delivery.Event.EventType, delivery.Event.SchemaVersion);
            var subscriber = registry.SubscriberFor(type.PayloadType, delivery.SubscriberId);
            var envelope = type.Rehydrate(delivery.Event);
            return subscriber.Policy == DomainEventDeliveryPolicy.Reliable
                ? await ReliableAsync(delivery, subscriber, envelope, ct).ConfigureAwait(false)
                : await LiveOnlyAsync(delivery, subscriber, envelope, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            var terminal = error is JsonException or InvalidDataException
                || delivery.AttemptCount + 1 >= options.MaximumAttempts;
            logger.LogWarning(error, "Domain event {EventId} subscriber {SubscriberId} failed; terminal={Terminal}",
                delivery.Event.EventId, delivery.SubscriberId, terminal);
            // The failed handler's entire transaction has already unwound before recording its retry.
            await using var transaction = transactions.Begin();
            var delay = RetryDelay(delivery.AttemptCount);
            var description = error.ToString();
            await Persistence.FailAsync(delivery.Event.EventId, delivery.SubscriberId, clock.GetUtcNow() + delay,
                description[..Math.Min(description.Length, 4000)], terminal, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }
    }

    private async ValueTask<bool> ReliableAsync(PendingDomainEventDelivery delivery,
        DomainSubscriberRegistration subscriber, object envelope, CancellationToken ct)
    {
        await using var transaction = transactions.Begin();
        if (!await Persistence.TryBeginAsync(delivery.Event.EventId, delivery.SubscriberId, clock.GetUtcNow(), ct)
                .ConfigureAwait(false)) return false;
        await using var scope = scopes.CreateAsyncScope();
        await subscriber.Invoke(scope.ServiceProvider, envelope, ct).ConfigureAwait(false);
        await Persistence.CompleteAsync(delivery.Event.EventId, delivery.SubscriberId, clock.GetUtcNow(), "", ct)
            .ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> LiveOnlyAsync(PendingDomainEventDelivery delivery,
        DomainSubscriberRegistration subscriber, object envelope, CancellationToken ct)
    {
        var invoke = false;
        await using (var transaction = transactions.Begin())
        {
            var now = clock.GetUtcNow();
            if (!await Persistence.TryBeginAsync(delivery.Event.EventId, delivery.SubscriberId, now, ct)
                    .ConfigureAwait(false)) return false;
            var reason = "";
            if (delivery.Event.OriginInstanceId != instance.Id) reason = "Previous process instance; live hint skipped.";
            else if (now - delivery.Event.OccurredAt > options.LiveEventLifetime) reason = "Expired live hint.";
            else if (delivery.CoalescingKey is { } key && await Persistence.HasCompletedCoalescingKeyAsync(
                    delivery.Event.BatchId, delivery.SubscriberId, key, ct).ConfigureAwait(false)) reason = "Coalesced live hint.";
            else invoke = true;
            await Persistence.CompleteAsync(delivery.Event.EventId, delivery.SubscriberId, now, reason, ct)
                .ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        if (invoke)
        {
            // Consumption is durable before the external side effect. Failure or a crash never replays a live hint.
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await subscriber.Invoke(scope.ServiceProvider, envelope, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                logger.LogWarning(error, "Consumed live event {EventId} subscriber {SubscriberId} failed; no replay.",
                    delivery.Event.EventId, delivery.SubscriberId);
            }
        }
        return true;
    }

    internal Task RunAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(registry.Groups.Keys.Select(id => RunGroupAsync(id, stoppingToken)));

    private async Task RunGroupAsync(string subscriberId, CancellationToken stoppingToken)
    {
        // SQLite may complete synchronously: start all groups before consuming a continuous backlog.
        await Task.Yield();
        var reader = wakeups.Reader(subscriberId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (reader.TryRead(out _)) { }
                if (await DrainOnceAsync(subscriberId, stoppingToken).ConfigureAwait(false) != 0)
                {
                    await Task.Yield();
                    continue;
                }
                var next = await Persistence.NextDueAsync(subscriberId, stoppingToken).ConfigureAwait(false);
                var delay = next is { } due ? due - clock.GetUtcNow() : options.RecoveryInterval;
                delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay > options.RecoveryInterval ? options.RecoveryInterval : delay;
                await WaitForWakeOrDueAsync(reader, delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (GameplayExecutorStoppedException)
            {
                // Only a container disposed outside the host's stop order gets here; deliveries stay pending.
                logger.LogInformation("Domain event dispatch group {SubscriberId} enqueues no more: the gameplay executor has stopped.",
                    subscriberId);
                return;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Domain event dispatch group {SubscriberId} could not persist or scan; backing off.", subscriberId);
                try { await Task.Delay(options.RetryDelay, clock, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            }
        }
    }

    internal async Task WaitForWakeOrDueAsync(ChannelReader<byte> reader, TimeSpan delay, CancellationToken ct)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var signal = reader.WaitToReadAsync(wait.Token).AsTask();
        var timer = Task.Delay(delay, clock, wait.Token);
        try
        {
            var completed = await Task.WhenAny(signal, timer).ConfigureAwait(false);
            await completed.ConfigureAwait(false);
        }
        finally
        {
            await wait.CancelAsync().ConfigureAwait(false);
            try { await Task.WhenAll(signal, timer).ConfigureAwait(false); }
            catch (OperationCanceledException) when (wait.IsCancellationRequested) { }
        }
        ct.ThrowIfCancellationRequested();
    }

    private TimeSpan RetryDelay(int failedAttempts) => TimeSpan.FromTicks((long)Math.Min(
        options.MaximumRetryDelay.Ticks, options.RetryDelay.Ticks * Math.Pow(2, Math.Min(failedAttempts, 30))));
}

internal sealed class DomainEventHostedService(DomainEventDispatcher dispatcher) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => dispatcher.RunAsync(stoppingToken);
}
