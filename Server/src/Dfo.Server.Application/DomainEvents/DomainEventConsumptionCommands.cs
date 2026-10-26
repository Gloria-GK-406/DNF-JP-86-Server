using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.DomainEvents;

internal sealed record ConsumeDomainEventCommand(PendingDomainEventDelivery Delivery) : ICommand<bool>;
internal sealed record SettleDepthExceededDomainEventCommand(PendingDomainEventDelivery Delivery, GameplayCallChain Refused, string RefusedCommand) : ICommand<string>;
public interface ICommandDomainEventConsumption
{
    internal ValueTask<bool> ConsumeAsync(CommandExecutionContext context, PendingDomainEventDelivery delivery, CancellationToken token);
    internal ValueTask<string> SettleAsync(CommandExecutionContext context, SettleDepthExceededDomainEventCommand command, CancellationToken token);
}
internal sealed class ConsumeDomainEventCommandHandler(ICommandDomainEventConsumption events) : ICommandHandler<ConsumeDomainEventCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, ConsumeDomainEventCommand command, CancellationToken token) =>
        events.ConsumeAsync(context, command.Delivery, token);
}
internal sealed class SettleDepthExceededDomainEventCommandHandler(ICommandDomainEventConsumption events) : ICommandHandler<SettleDepthExceededDomainEventCommand, string>
{
    public ValueTask<string> HandleAsync(CommandExecutionContext context, SettleDepthExceededDomainEventCommand command, CancellationToken token) =>
        events.SettleAsync(context, command, token);
}
internal sealed class CommandDomainEventConsumption(DomainEventRegistry registry, IDomainEventPersistence persistence,
    IServiceScopeFactory scopes, ICommandWorkUnits units, DomainEventInstance instance, TimeProvider clock,
    DomainEventDispatchOptions options, ILogger<DomainEventDispatcher> logger, DomainEventCallChains chains,
    GameplayDepthGuard guard, GameplayOutboxes outboxes) : ICommandDomainEventConsumption
{
    private sealed record Attempt(bool Claimed, bool Invoke = false, Exception? Failure = null);
    private static void Demand(CommandExecutionContext context, Type command)
    {
        var request = context.DemandRequest();
        if (context.CommandType != command || request is not CommandRequestContext.TrustedInternal
            { Origin.Kind: GameplayCallSource.Event or GameplayCallSource.StartupRecovery, Origin.SourceConnection: null })
            throw new InvalidOperationException("Event consumption requires its exact internal command.");
    }
    private void Forget(PendingDomainEventDelivery delivery) => chains.Forget(delivery.Event.EventId, delivery.SubscriberId);
    private static bool BusinessFailure(Exception error) => error is not OperationCanceledException
        && !TransactionOutcomeUncertainException.Contains(error) && !DomainEventScopeCleanupException.Contains(error);
    private static void DemandNoEncodingFailure(GameplayOutbox outbox, int start)
    {
        if (outbox.UnhandledRequiredEncodingFailureSince(start) is { } error) ExceptionDispatchInfo.Throw(error);
    }
    public async ValueTask<bool> ConsumeAsync(CommandExecutionContext context, PendingDomainEventDelivery delivery, CancellationToken ct)
    {
        Demand(context, typeof(ConsumeDomainEventCommand));
        DomainSubscriberRegistration subscriber;
        object envelope;
        try
        {
            var type = registry.For(delivery.Event.EventType, delivery.Event.SchemaVersion);
            subscriber = registry.SubscriberFor(type.PayloadType, delivery.SubscriberId);
            envelope = type.Rehydrate(delivery.Event);
        }
        catch (Exception error) when (BusinessFailure(error))
        {
            context.DemandRequest();
            await FailAsync(context, delivery, error, ct).ConfigureAwait(false);
            return true;
        }
        return subscriber.Policy == DomainEventDeliveryPolicy.Reliable
            ? await ReliableAsync(context, delivery, subscriber, envelope, ct).ConfigureAwait(false)
            : await LiveOnlyAsync(context, delivery, subscriber, envelope, ct).ConfigureAwait(false);
    }
    private async ValueTask<bool> ReliableAsync(CommandExecutionContext context, PendingDomainEventDelivery delivery,
        DomainSubscriberRegistration subscriber, object envelope, CancellationToken ct)
    {
        AsyncServiceScope? scope = null;
        var outbox = outboxes.Current;
        var encodingStart = outbox.RequiredEncodingFailurePosition;
        CommandWriteResult<Attempt> write;
        try
        {
            write = await units.WriteResultAsync(context, new("event.reliable", []), async (unit, token) =>
            {
                try
                {
                    if (!await persistence.TryBeginAsync(delivery.Event.EventId, delivery.SubscriberId, clock.GetUtcNow(), token).ConfigureAwait(false))
                    { unit.RequireRollback(); return new Attempt(false); }
                    scope = scopes.CreateAsyncScope();
                    encodingStart = outbox.RequiredEncodingFailurePosition;
                    await subscriber.Invoke(scope.Value.ServiceProvider, envelope, token).ConfigureAwait(false);
                    DemandNoEncodingFailure(outbox, encodingStart);
                    await persistence.CompleteAsync(delivery.Event.EventId, delivery.SubscriberId, clock.GetUtcNow(), "", token).ConfigureAwait(false);
                    return new Attempt(true);
                }
                catch (Exception error) when (BusinessFailure(error) && outbox.UnhandledRequiredEncodingFailureSince(encodingStart) is null)
                {
                    if (scope is { } failedScope)
                    { scope = null; await DomainEventScopeCleanupException.DisposeAsync(failedScope, error).ConfigureAwait(false); }
                    context.DemandRequest();
                    unit.RequireRollback(error);
                    return new Attempt(true, Failure: error);
                }
            }, ct).ConfigureAwait(false);
            if (write is CommandWriteResult<Attempt>.Returned { Value: { Claimed: true, Failure: null } }) Forget(delivery);
        }
        catch (Exception error)
        {
            if (scope is { } failedScope)
            { scope = null; await DomainEventScopeCleanupException.DisposeAsync(failedScope, error).ConfigureAwait(false); }
            throw;
        }
        if (scope is { } ownedScope)
        { scope = null; await DomainEventScopeCleanupException.DisposeAsync(ownedScope, write is CommandWriteResult<Attempt>.CommitRolledBack rollback ? rollback.Cause : null).ConfigureAwait(false); }
        context.DemandRequest();
        var failure = write is CommandWriteResult<Attempt>.CommitRolledBack rejected ? rejected.Cause
            : ((CommandWriteResult<Attempt>.Returned)write).Value.Failure;
        if (failure is not null) { await FailAsync(context, delivery, failure, ct).ConfigureAwait(false); return true; }
        return ((CommandWriteResult<Attempt>.Returned)write).Value.Claimed;
    }
    private async ValueTask<bool> LiveOnlyAsync(CommandExecutionContext context, PendingDomainEventDelivery delivery,
        DomainSubscriberRegistration subscriber, object envelope, CancellationToken ct)
    {
        var write = await units.WriteResultAsync(context, new("event.live-consumption", []), async (unit, token) =>
        {
            try
            {
                var now = clock.GetUtcNow();
                if (!await persistence.TryBeginAsync(delivery.Event.EventId, delivery.SubscriberId, now, token).ConfigureAwait(false))
                { unit.RequireRollback(); return new Attempt(false); }
                var reason = "";
                if (delivery.Event.OriginInstanceId != instance.Id) reason = "Previous process instance; live hint skipped.";
                else if (now - delivery.Event.OccurredAt > options.LiveEventLifetime) reason = "Expired live hint.";
                else if (delivery.CoalescingKey is { } key && await persistence.HasCompletedCoalescingKeyAsync(
                    delivery.Event.BatchId, delivery.SubscriberId, key, token).ConfigureAwait(false)) reason = "Coalesced live hint.";
                await persistence.CompleteAsync(delivery.Event.EventId, delivery.SubscriberId, now, reason, token).ConfigureAwait(false);
                return new Attempt(true, reason.Length == 0);
            }
            catch (Exception error) when (BusinessFailure(error))
            { context.DemandRequest(); unit.RequireRollback(error); return new Attempt(true, Failure: error); }
        }, ct).ConfigureAwait(false);
        var failure = write is CommandWriteResult<Attempt>.CommitRolledBack rejected ? rejected.Cause
            : ((CommandWriteResult<Attempt>.Returned)write).Value.Failure;
        if (failure is not null) { await FailAsync(context, delivery, failure, ct).ConfigureAwait(false); return true; }
        var result = ((CommandWriteResult<Attempt>.Returned)write).Value;
        if (!result.Claimed) return false;
        Forget(delivery);
        if (!result.Invoke) return true;
        var scope = scopes.CreateAsyncScope();
        Exception? invocation = null;
        var outbox = outboxes.Current;
        var encodingStart = outbox.RequiredEncodingFailurePosition;
        try
        {
            try { await subscriber.Invoke(scope.ServiceProvider, envelope, ct).ConfigureAwait(false); }
            catch (Exception error) when (BusinessFailure(error) && outbox.UnhandledRequiredEncodingFailureSince(encodingStart) is null)
            {
                context.DemandRequest();
                logger.LogWarning(error, "Consumed live event {EventId} subscriber {SubscriberId} failed; no replay.", delivery.Event.EventId, delivery.SubscriberId);
            }
            DemandNoEncodingFailure(outbox, encodingStart);
        }
        catch (Exception error) { invocation = error; throw; }
        finally { await DomainEventScopeCleanupException.DisposeAsync(scope, invocation).ConfigureAwait(false); }
        return true;
    }
    private async ValueTask FailAsync(CommandExecutionContext context, PendingDomainEventDelivery delivery, Exception error, CancellationToken ct)
    {
        context.DemandRequest();
        var terminal = error is JsonException or InvalidDataException || delivery.AttemptCount + 1 >= options.MaximumAttempts;
        logger.LogWarning(error, "Domain event {EventId} subscriber {SubscriberId} failed; terminal={Terminal}", delivery.Event.EventId, delivery.SubscriberId, terminal);
        var delay = TimeSpan.FromTicks((long)Math.Min(options.MaximumRetryDelay.Ticks,
            options.RetryDelay.Ticks * Math.Pow(2, Math.Min(delivery.AttemptCount, 30))));
        var description = error.ToString();
        await units.WriteAsync(context, new("event.retry-record", []), async (_, token) =>
        {
            await persistence.FailAsync(delivery.Event.EventId, delivery.SubscriberId, clock.GetUtcNow() + delay,
                description[..Math.Min(description.Length, 4000)], terminal, token).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
        if (terminal) Forget(delivery);
    }
    public async ValueTask<string> SettleAsync(CommandExecutionContext context, SettleDepthExceededDomainEventCommand command, CancellationToken ct)
    {
        Demand(context, typeof(SettleDepthExceededDomainEventCommand));
        var delivery = command.Delivery;
        var live = false;
        try { var type = registry.For(delivery.Event.EventType, delivery.Event.SchemaVersion); live = registry.SubscriberFor(type.PayloadType, delivery.SubscriberId).Policy == DomainEventDeliveryPolicy.LiveOnly; }
        catch (Exception) { }
        var claimed = await units.WriteAsync(context, new("event.depth-settlement", []), async (unit, token) =>
        {
            if (live)
            {
                if (!await persistence.TryBeginAsync(delivery.Event.EventId, delivery.SubscriberId, clock.GetUtcNow(), token).ConfigureAwait(false))
                { unit.RequireRollback(); return false; }
                await persistence.CompleteAsync(delivery.Event.EventId, delivery.SubscriberId, clock.GetUtcNow(), DomainEventDispatcher.DepthExceededReason, token).ConfigureAwait(false);
            }
            else
            {
                var description = "Depth limit exceeded: " + command.Refused.Describe(command.RefusedCommand);
                await persistence.FailAsync(delivery.Event.EventId, delivery.SubscriberId, clock.GetUtcNow(),
                    description[..Math.Min(description.Length, 4000)], terminal: true, token).ConfigureAwait(false);
            }
            return true;
        }, ct).ConfigureAwait(false);
        if (!claimed) return "by another consumer";
        Forget(delivery);
        guard.CloseRootConnection(command.Refused);
        return live ? "completed" : "terminal";
    }
}
