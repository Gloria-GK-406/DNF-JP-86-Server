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
        return queue.RunAsync(new GameplayWork<TResult>(GameplayShardKey.World, origin.Chain, origin.CommandType,
            token => ExplicitAsync(command, registration, null, origin,
                origin.Cancellation == InternalCommandCancellation.BodyOnly ? cancellationToken : token), origin.SourceConnection),
            origin.Cancellation == InternalCommandCancellation.BodyOnly ? CancellationToken.None : cancellationToken);
    }

    private async ValueTask<TResult> ExplicitAsync<TResult>(ICommand<TResult> command, CommandHandlerRegistration registration,
        IProtocolCommand? protocol, InternalCommandOrigin? origin, CancellationToken ct) where TResult : notnull
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
            failure = request is not null && safety is not null
                ? await CommandWorkUnits.FailAsync(request, cause, safety, registration.CommandType.Name).ConfigureAwait(false)
                : cause;
        }
        finally
        {
            request?.Close();
            try
            {
                if (scope is { } ownedScope)
                {
                    try { await ownedScope.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception cleanup)
                    {
                        var cause = failure is null ? cleanup : new AggregateException("Command scope cleanup failed.", failure, cleanup);
                        failure = request is not null && safety is not null
                            ? await CommandWorkUnits.FailAsync(request, cause, safety, registration.CommandType.Name).ConfigureAwait(false)
                            : cause;
                    }
                }
            }
            finally { DispatchDepth.Value = previousDepth; }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;

        async ValueTask<TResult> InvokeAsync(IServiceProvider services)
        {
            safety = services.GetRequiredService<SelectedSessionSafety>();
            if (protocol is not null)
            {
                if (registration.ProtocolMessages?.Any(binding => binding.MessageType == protocol.Message.MessageType
                    && binding.MessageClrType == protocol.Message.GetType()) != true)
                    throw new InvalidOperationException("The protocol command received an unbound message CLR type.");
                if (protocol.Message is IEffectSettlementRequest)
                    throw new NotSupportedException("Explicit effect commands are not enabled.");
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
                request = new CommandRequestContext.ProtocolNonEffect(protocol.Dispatch, protocol.Message, protocol.Frame,
                    () => !safety.IsBlocked(connection) && (authority?.Invoke() ?? true));
            }
            else
                request = new CommandRequestContext.TrustedInternal(origin ?? throw new InvalidOperationException("Missing internal origin."));
            var context = new CommandExecutionContext(Guid.NewGuid(), registration.CommandType, registration.HandlerType,
                registration.ExecutionKind) { Request = request };
            request.DemandCurrent();
            var invoker = (ICommandInvoker<TResult>)services.GetRequiredService(registration.InvokerType);
            var result = await invoker.InvokeAsync(context, command, ct).ConfigureAwait(false);
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
