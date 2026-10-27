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
internal sealed class DomainEventDispatcher(DomainEventRegistry registry, IServiceProvider services,
    DomainTransactionScope transactions, DomainEventWakeups wakeups, DomainEventInstance instance,
    TimeProvider clock, DomainEventDispatchOptions options, ILogger<DomainEventDispatcher> logger,
    Dfo.Server.Application.Commands.ICommandBus commands, DomainEventCallChains chains, FailedDomainEventAttempts failures)
{
    private readonly DomainEventDispatchOptions options = Validated(options);
    private static DomainEventDispatchOptions Validated(DomainEventDispatchOptions options) { options.Validate(); return options; }
    private IDomainEventPersistence Persistence => services.GetRequiredService<IDomainEventPersistence>();
    internal const string DepthExceededReason = "Depth limit exceeded; live hint skipped.";
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
            if (failures.Find(delivery) is { } previous)
            {
                if (previous.Failure.FinalFailure is { } final)
                {
                    await SettleFailureAsync(previous, final, cancellationToken).ConfigureAwait(false);
                    processed++;
                }
                continue;
            }
            bool consumed;
            var attempt = new ConsumeDomainEventCommand(delivery);
            try
            {
                var chain = ChainOf(delivery);
                consumed = await commands.SendInternalAsync(attempt,
                    new(chain.Source, chain, CommandTypeOf(delivery), Dfo.Server.Application.Commands.InternalCommandCancellation.BodyOnly),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (GameplayDepthExceededException refused) when (!GameplayJob.EndedAJob(refused))
            {
                consumed = await SettleAsync(delivery, refused, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException error) when (attempt.Failure.WasCleanlyCancelled(error))
            {
                failures.Settled(attempt);
                throw;
            }
            catch (Exception error) when (GameplayJob.EndedAJob(error) && attempt.Failure.Observed)
            {
                failures.Finalize(attempt, error);
                cancellationToken.ThrowIfCancellationRequested();
                // The failed request is already closed; only this new command may record its retry or quarantine.
                await SettleFailureAsync(attempt, error, cancellationToken).ConfigureAwait(false);
                consumed = true;
            }
            if (consumed) processed++;
        }
        return processed;
    }

    private ValueTask<bool> SettleFailureAsync(ConsumeDomainEventCommand attempt, Exception error, CancellationToken token) =>
        commands.SendInternalAsync(new SettleFailedDomainEventCommand(attempt, error),
            new(GameplayCallSource.Event, GameplayCallChain.NewRoot(GameplayCallSource.Event),
                CommandTypeOf(attempt.Delivery) + "/failure-settlement", Dfo.Server.Application.Commands.InternalCommandCancellation.BodyOnly), token);

    private bool IsLocal(PendingDomainEventDelivery delivery) => delivery.Event.OriginInstanceId == instance.Id;

    /// <summary>
    /// RQ-050, RQ-051: the publishing CMD's child, the same parent for every retry of the delivery. A delivery of
    /// another process is a startup recovery root; one this process published off the executor, an event root.
    /// </summary>
    private GameplayCallChain ChainOf(PendingDomainEventDelivery delivery)
    {
        if (!IsLocal(delivery)) return GameplayCallChain.NewRoot(GameplayCallSource.StartupRecovery);
        return chains.TryGet(delivery.Event.EventId, delivery.SubscriberId, out var publisher)
            ? publisher.Child(GameplayCallSource.Event)
            : GameplayCallChain.NewRoot(GameplayCallSource.Event);
    }

    private string CommandTypeOf(PendingDomainEventDelivery delivery) =>
        (IsLocal(delivery) ? "event:" : "startup:event/") + delivery.SubscriberId;


    /// <summary>
    /// RQ-053(b): a system CMD, a new root so the depth check passes it, settles the refused delivery without running
    /// its subscriber, then closes the refused chain's root connection (RQ-053(a)). The refusal itself was logged by
    /// the executor. A failure of this CMD goes to the group loop, which reads the row again and settles it again.
    /// </summary>
    private async ValueTask<bool> SettleAsync(PendingDomainEventDelivery delivery, GameplayDepthExceededException refused,
        CancellationToken cancellationToken)
    {
        var outcome = await commands.SendInternalAsync(new SettleDepthExceededDomainEventCommand(delivery, refused.Chain, refused.CommandType),
            new(GameplayCallSource.Event, GameplayCallChain.NewRoot(GameplayCallSource.Event),
                CommandTypeOf(delivery) + "/depth-exceeded", Dfo.Server.Application.Commands.InternalCommandCancellation.BodyOnly),
            cancellationToken).ConfigureAwait(false);
        logger.LogWarning("Domain event {EventId} subscriber {SubscriberId} exceeded the depth limit and was settled {Outcome} " +
            "without running: refused root {RootId}, parent {ParentId}, depth {Depth}",
            delivery.Event.EventId, delivery.SubscriberId, outcome, refused.Chain.RootId, refused.Chain.ParentId, refused.Chain.Depth);
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
            catch (GameplayExecutorStoppedException stopped)
            {
                // Only a container disposed outside the host's stop order gets here; deliveries stay pending.
                // A cause is the executor's faulted consumption loop (F-31).
                logger.Log(stopped.InnerException is null ? LogLevel.Information : LogLevel.Warning, stopped,
                    "Domain event dispatch group {SubscriberId} enqueues no more: the gameplay executor has stopped.",
                    subscriberId);
                return;
            }
            catch (Exception error)
            {
                // An event CMD's own failure was logged by the executor with its stack (F-30).
                if (GameplayJob.EndedAJob(error))
                    logger.LogWarning("Domain event dispatch group {SubscriberId} could not persist or scan; backing off.", subscriberId);
                else
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


}

internal sealed class DomainEventHostedService(DomainEventDispatcher dispatcher) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => dispatcher.RunAsync(stoppingToken);
}
