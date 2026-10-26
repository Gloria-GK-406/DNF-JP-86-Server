using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Commands;

internal sealed class CommandBus(
    IServiceScopeFactory scopeFactory,
    CommandHandlerRegistry registry,
    IGameplayQueue queue,
    CommandJobEntry entry, NetworkCommandAuthorization authorization, EffectOperationContext operations) : ICommandBus
{
    private static readonly AsyncLocal<int> DispatchDepth = new();

    public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        where TResult : notnull
    {
        if (command is null) return ValueTask.FromException<TResult>(new ArgumentNullException(nameof(command)));
        if (DispatchDepth.Value > 0) return ValueTask.FromException<TResult>(NestedDispatch());
        if (GameplayLeakAssertion.LeakedFrom("bus:" + command.GetType().Name) is { } leaked)
            return ValueTask.FromException<TResult>(leaked);
        if (DispatchDepth.Value != 0) return ValueTask.FromException<TResult>(ExplicitNestedDispatch());
        var registration = registry.GetRegistration(command.GetType());
        if (registration.Route == CommandRouteKind.Internal)
            return ValueTask.FromException<TResult>(new InvalidOperationException("Internal commands require an explicit origin."));
        if (registration.Route == CommandRouteKind.Protocol)
        {
            if (GameplayJob.IsOnExecutor) return ValueTask.FromException<TResult>(ExplicitNestedDispatch());
            var protocol = (IProtocolCommand)command;
            return queue.RunAsync(new GameplayWork<TResult>(GameplayShardKey.World,
                GameplayCallChain.NewRoot(GameplayCallSource.Connection, protocol.Dispatch.Connection.ConnectionId),
                command.GetType().Name, token => ExplicitAsync(command, registration, protocol, null, token),
                protocol.Dispatch.Connection), cancellationToken);
        }
        if (GameplayJob.IsOnExecutor) return InlineAsync(command, cancellationToken);
        // This exact old external entry supplies its provenance before queue execution suppresses the flow.
        var network = authorization.Capture();
        var inherited = operations.CaptureInheritance();
        network.DemandOpen();
        inherited.DemandOpen();
        var origin = new LegacyInvocationOrigin(network.IsAbsent && inherited.IsEmpty, () =>
        {
            network.DemandOpen();
            inherited.DemandOpen();
        });
        return queue.RunAsync(new GameplayWork<TResult>(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection), "bus:" + command.GetType().Name,
            token => { entry.Begin(); return InlineAsync(command, token, origin); }), cancellationToken);
    }

    public ValueTask<TResult> SendInternalAsync<TResult>(ICommand<TResult> command, InternalCommandOrigin origin,
        CancellationToken cancellationToken = default) where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(command);
        if (GameplayLeakAssertion.LeakedFrom("bus:" + command.GetType().Name) is { } leaked)
            return ValueTask.FromException<TResult>(leaked);
        if (DispatchDepth.Value != 0 || GameplayJob.IsOnExecutor) return ValueTask.FromException<TResult>(ExplicitNestedDispatch());
        ArgumentNullException.ThrowIfNull(origin);
        origin.Validate();
        var registration = registry.GetRegistration(command.GetType());
        if (registration.Route != CommandRouteKind.Internal)
            return ValueTask.FromException<TResult>(new InvalidOperationException("Only an internal command accepts an internal origin."));
        ExceptionDispatchInfo? cancelled = null;
        var bodyOnly = origin.Cancellation == InternalCommandCancellation.BodyOnly;
        var queued = queue.RunAsync(new GameplayWork<TResult>(GameplayShardKey.World, origin.Chain, origin.CommandType,
            async token =>
            {
                var observation = bodyOnly ? new ProducerCancellationObservation(cancellationToken) : null;
                try
                {
                    return await ExplicitAsync(command, registration, null, origin,
                        bodyOnly ? cancellationToken : token, observation).ConfigureAwait(false);
                }
                catch (OperationCanceledException error) when (observation?.TryMarshal(error) == true)
                {
                    cancelled = ExceptionDispatchInfo.Capture(error);
                    return default!;
                }
            }, origin.SourceConnection), bodyOnly ? CancellationToken.None : cancellationToken);
        return bodyOnly ? new ValueTask<TResult>(CompleteAsync()) : queued;

        async Task<TResult> CompleteAsync()
        {
            var result = await queued.ConfigureAwait(false);
            cancelled?.Throw();
            return result;
        }
    }

    private async ValueTask<TResult> ExplicitAsync<TResult>(ICommand<TResult> command, CommandHandlerRegistration registration,
        IProtocolCommand? protocol, InternalCommandOrigin? origin, CancellationToken ct, ProducerCancellationObservation? cancellation = null) where TResult : notnull
    {
        entry.Begin();
        var previousDepth = DispatchDepth.Value;
        DispatchDepth.Value = -1;
        CommandRequestContext? request = null;
        SelectedSessionSafety? safety = null;
        AsyncServiceScope? scope = null;
        Exception? failure = null;
        TResult result = default!;
        try
        {
            scope = scopeFactory.CreateAsyncScope();
            result = await InvokeAsync(scope.Value.ServiceProvider).ConfigureAwait(false);
        }
        catch (Exception cause)
        {
            failure = cause;
            request?.Failed(cause);
        }
        finally
        {
            // Drain while scope and request still exist. No final raw/typed decision precedes cleanup.
            if (request is not null)
            {
                await request.DrainOperationsAsync().ConfigureAwait(false);
                failure = request.Failure ?? failure;
                request.Close();
            }
            try
            {
                if (scope is { } ownedScope)
                {
                    request?.BeginScopeCleanup(ownedScope.ServiceProvider);
                    try { await ownedScope.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception cleanup)
                    {
                        if (request is not null) request.Failed(cleanup);
                        else failure = failure is null ? cleanup : new AggregateException("Command scope cleanup failed.", failure, cleanup);
                    }
                    finally
                    {
                        request?.EndScopeCleanup();
                        failure = request?.Failure ?? failure;
                    }
                }
                if (request is not null)
                {
                    try { GameplayJob.DemandHealthy(); }
                    catch (Exception error) { cancellation?.Invalidate(); request.HttpFailure?.Invalidate(); request.Failed(error); }
                    failure = request.Failure ?? failure;
                }
                if (failure is not null && request is not null && safety is not null)
                    failure = await CommandWorkUnits.FailAsync(request, failure, safety, registration.CommandType.Name).ConfigureAwait(false);
                if (request is CommandRequestContext.ProtocolEffectSelected selected)
                    EffectOperationLedger.FinalizePending(selected.State.Entry);
            }
            finally { DispatchDepth.Value = previousDepth; }
        }
        if (failure is not null)
        {
            cancellation?.Finalize(request, failure);
            if (command is HttpRouteCommand httpCommand && request?.HttpFailure is { } http && http.TryFinalize(out var response))
                failure = new CommandHttpAuditedFailureException(httpCommand, GameplayJob.Current!.Value.Id, response, failure);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return result;

        async ValueTask<TResult> InvokeAsync(IServiceProvider services)
        {
            safety = services.GetRequiredService<SelectedSessionSafety>();
            if (protocol is not null)
            {
                if (registration.ProtocolMessages?.Any(binding => binding.MessageType == protocol.Message.MessageType
                    && binding.MessageClrType == protocol.Message.GetType()) != true)
                    throw new InvalidOperationException("The protocol command received an unbound message CLR type.");
                var connection = protocol.Dispatch.Connection;
                var frameMatches = protocol.Frame switch
                {
                    ProtocolFrameMetadata.Game game => game.Value is not null && connection.EndpointKind == NetworkEndpointKind.Game,
                    ProtocolFrameMetadata.Channel channel => channel.Value is not null && connection.EndpointKind == NetworkEndpointKind.Channel,
                    _ => false,
                };
                if (!frameMatches) throw new InvalidOperationException("The protocol frame metadata does not match its endpoint.");
                var sessions = services.GetService<LoginSessionRegistry>();
                if (safety.IsBlocked(connection) || sessions?.IsSuperseded(connection.ConnectionId) == true) return default!;
                var authority = sessions?.CaptureAuthority(connection.ConnectionId);
                Func<bool> valid = () => !safety.IsBlocked(connection) && (authority?.Invoke() ?? true);
                if (protocol.Message is IEffectSettlementRequest effect)
                {
                    if (protocol.Frame is not ProtocolFrameMetadata.Game game || !Equals(game.Value, effect.Metadata))
                        throw new InvalidOperationException("The effect frame differs from the request metadata.");
                    var admission = services.GetRequiredService<EffectOperationLedger>().Admit(connection, effect);
                    request = admission switch
                    {
                        EffectAdmission.Rejected => new CommandRequestContext.ProtocolEffectRejected(protocol.Dispatch, protocol.Message, protocol.Frame, valid),
                        EffectAdmission.Selected selected => new CommandRequestContext.ProtocolEffectSelected(protocol.Dispatch, protocol.Message, protocol.Frame, valid,
                            new(selected.Entry, selected.Recipient, selected.Minimum, safety)),
                        EffectAdmission.Replay replay => new CommandRequestContext.ProtocolEffectSelected(protocol.Dispatch, protocol.Message, protocol.Frame, valid,
                            new(replay.Entry, replay.Recipient, effect.MinimumReplayNeeds, safety, replay: true)),
                        _ => throw new InvalidOperationException("Unknown effect admission."),
                    };
                }
                else request = new CommandRequestContext.ProtocolNonEffect(protocol.Dispatch, protocol.Message, protocol.Frame, valid);
            }
            else
            {
                var internalRequest = new CommandRequestContext.TrustedInternal(origin ?? throw new InvalidOperationException("Missing internal origin."));
                request = internalRequest;
                request.ProducerCancellation = cancellation;
                if (origin?.Kind == GameplayCallSource.Timer && origin.Cancellation == InternalCommandCancellation.BodyOnly)
                    request.TimerReadOnlyCancellation = registration.TimerReadOnlyCancellation;
                cancellation?.Bind(internalRequest);
            }
            request.RegistrationExits = registration.RegistrationExits ?? [];
            request.SelfSettlement = registration.SelfSettlement;
            request.SelectedSessionCleanup = registration.SelectedSessionCleanup;
            var context = new CommandExecutionContext(Guid.NewGuid(), registration.CommandType, registration.HandlerType,
                registration.ExecutionKind) { Request = request };
            request.DemandCurrent();
            if (request is CommandRequestContext.ProtocolEffectSelected { State.IsReplay: true } replayRequest)
            {
                var replay = replayRequest.State;
                var snapshot = await services.GetRequiredService<ICommandWorkUnits>().WriteAsync(context,
                    new("committed-replay-current-state", [replay.Recipient]), (_, token) =>
                        services.GetRequiredService<Dfo.Server.Abstractions.Effects.IReplayStateDomainService>().ReadAsync(
                            replay.Recipient.Session, replay.Entry.Projection ?? throw new InvalidOperationException("Missing committed projection."),
                            replay.Recipient.Metadata, token), ct).ConfigureAwait(false);
                var attempt = registration.RegistrationExits?.Contains(CommandRegistrationExit.CommittedReplayCurrentStateBatch) == true
                    ? request.BeginRegistrationAttempt(CommandRegistrationExit.CommittedReplayCurrentStateBatch, protocol!.Dispatch.Connection) : null;
                await services.GetRequiredService<Dfo.Server.Abstractions.Effects.IReplayStateProjectionSink>().ProjectAsync(
                    protocol!.Dispatch.Connection, replay.Recipient.Session, replay.Recipient.Metadata, snapshot, attempt, ct).ConfigureAwait(false);
                request.DemandFinished();
                return default!;
            }
            var invoker = (ICommandInvoker<TResult>)services.GetRequiredService(registration.InvokerType);
            TResult result;
            try { result = await invoker.InvokeAsync(context, command, ct).ConfigureAwait(false); }
            catch (OperationCanceledException error)
            {
                cancellation?.ObserveDirectBody(request, ct, error);
                throw;
            }
            // Normal body return cannot attest propagation of a discarded or caught action failure.
            cancellation?.Invalidate();
            request.DemandFinished();
            return result;
        }
    }

    private async ValueTask<TResult> InlineAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken,
        LegacyInvocationOrigin? origin = null)
        where TResult : notnull
    {
        if (DispatchDepth.Value != 0) throw NestedDispatch();

        DispatchDepth.Value++;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var registration = registry.GetRegistration(command.GetType());

            if (registration.Route != CommandRouteKind.LegacyFineGrained)
                throw new InvalidOperationException("Only a registered legacy command can use this invoker.");

            // Registration binds the command, its result and its handler together, and
            // ICommand is invariant in its result, so TResult here is the command's own
            // declared result and this invoker necessarily produces it.
            var invoker = (ICommandInvoker<TResult>)scope.ServiceProvider
                .GetRequiredService(registration.InvokerType);

            var context = new CommandExecutionContext(
                Guid.NewGuid(),
                registration.CommandType,
                registration.HandlerType,
                registration.ExecutionKind);
            CommandExecutionDelegate<TResult> next = async () =>
            {
                using var admission = context.EnterLegacyInvocation(registration);
                using var footprint = operations.CaptureLegacy(context, authorization.Capture(),
                    origin?.Standalone == true, origin?.DemandOpen ?? (() => { }));
                context.SupplyLegacyFootprint(footprint);
                return await invoker.InvokeAsync(context, command, cancellationToken).ConfigureAwait(false);
            };

            var behaviors = scope.ServiceProvider
                .GetServices<ICommandPipelineBehavior<TResult>>()
                .OrderBy(behavior => behavior.Order)
                .ToArray();
            foreach (var behavior in behaviors.Reverse())
            {
                var capturedBehavior = behavior;
                var capturedNext = next;
                next = () => capturedBehavior.ExecuteAsync(
                    context,
                    command,
                    capturedNext,
                    cancellationToken);
            }

            return await next().ConfigureAwait(false);
        }
        finally
        {
            DispatchDepth.Value--;
        }
    }

    private static Exception ExplicitNestedDispatch()
    {
        if (GameplayJob.IsOnExecutor)
        {
            try { GameplayJob.AssertNotOnExecutor("Nested command dispatch"); }
            catch (Exception error) { return error; }
        }
        return NestedDispatch();
    }

    private static InvalidOperationException NestedDispatch() => new(
        "Nested command dispatch is not allowed. Extract shared execution logic into an application service.");
}
