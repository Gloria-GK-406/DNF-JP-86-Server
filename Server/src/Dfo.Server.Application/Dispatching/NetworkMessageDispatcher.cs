using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Dispatching;

/// <summary>
/// Runs each inbound message as one job on the gameplay executor and waits for it (RQ-005): the
/// receive callback awaits this, so a connection's next frame is decoded only after its previous
/// job has run.
/// </summary>
/// <remarks>
/// Everything the message needs is built inside the job (scope, the session fence, login
/// supersession, captured authority, the authorization frame, the effect ledger). The executor's
/// loop does not carry the producer's execution context, so a frame built out here would be
/// invisible to the handler and to the commands it runs inline, and every guard would take its
/// "no frame, let it through" branch (plan, request-level context, transitional shape).
/// <para>
/// The job carries its source connection and starts its outbox at once, so a job that fails closes
/// that connection after whatever its outbox keeps, even if it registered nothing (RQ-031(c), DR-15);
/// the returned task completes once the outbox has been handed to the sender (RQ-005).
/// </para>
/// </remarks>
internal sealed class NetworkMessageDispatcher(
    MessageHandlerRegistry registry,
    IServiceScopeFactory scopeFactory,
    IGameplayQueue queue,
    GameplayOutboxes outboxes) : INetworkMessageDispatcher
{
    public ValueTask DispatchAsync(
        NetworkDispatchContext context,
        IClientNetworkMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        // TryGet, not a lookup that throws: an unregistered type still goes through the job, where
        // the fence is checked before the handler lookup fails.
        var registered = registry.TryGet(message.MessageType, out var route);
        if (registered && route!.IsTelemetry) return DispatchTelemetryAsync(context, message, route.HandlerType, cancellationToken);
        return queue.RunAsync(new GameplayWork(
            GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection, context.Connection.ConnectionId),
            registered ? route!.CommandType : MessageHandlerRegistry.CommandTypeOf(message.MessageType),
            token =>
            {
                _ = outboxes.Current;
                return HandleOnExecutorAsync(context, message, token);
            },
            context.Connection), cancellationToken);
    }

    private async ValueTask HandleOnExecutorAsync(
        NetworkDispatchContext context,
        IClientNetworkMessage message,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var safety = scope.ServiceProvider.GetRequiredService<Commands.SelectedSessionSafety>();
        if (safety.IsBlocked(context.Connection)) return;
        // A replaced login/connection loses authority before asynchronous transport closure.
        var sessions = scope.ServiceProvider.GetService<Authentication.LoginSessionRegistry>();
        if (sessions?.IsSuperseded(context.Connection.ConnectionId) == true) return;
        var authority = sessions?.CaptureAuthority(context.Connection.ConnectionId);
        using var authorization = scope.ServiceProvider
            .GetRequiredService<Commands.NetworkCommandAuthorization>().Enter(
                () => !safety.IsBlocked(context.Connection) && (authority?.Invoke() ?? true),
                // Telemetry only; authorization and settlement never depend on reflected payload facts.
                message.GetType().GetProperty("Metadata")?.GetValue(message) as GameMessageMetadata);
        var handlerType = registry.GetHandlerType(message.MessageType);
        var handler = (IClientMessageHandler)scope.ServiceProvider.GetRequiredService(handlerType);
        if (!handler.CanHandle(message))
        {
            throw new InvalidOperationException(
                $"Handler '{handlerType.Name}' rejected message CLR type '{message.GetType().Name}' " +
                $"registered as '{message.MessageType}'.");
        }

        if (message is IEffectSettlementRequest effect)
            await scope.ServiceProvider.GetRequiredService<EffectOperationLedger>().DispatchAsync(context.Connection, effect,
                () => handler.HandleAsync(context, message, cancellationToken), cancellationToken).ConfigureAwait(false);
        else
            await handler.HandleAsync(context, message, cancellationToken);
    }

    /// <summary>
    /// Pure telemetry stays in the receive callback, off the executor (spec appendix B). The fence and
    /// supersession are still read first, as before, so a blocked or replaced connection's telemetry is
    /// dropped silently; both are reads. No authorization frame: a telemetry handler injects only logging.
    /// </summary>
    private async ValueTask DispatchTelemetryAsync(
        NetworkDispatchContext context,
        IClientNetworkMessage message,
        Type handlerType,
        CancellationToken cancellationToken)
    {
        if (message is IEffectSettlementRequest)
        {
            throw new InvalidOperationException(
                $"Effect request '{message.GetType().Name}' is registered as telemetry under '{message.MessageType}'.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        if (scope.ServiceProvider.GetRequiredService<Commands.SelectedSessionSafety>().IsBlocked(context.Connection)) return;
        if (scope.ServiceProvider.GetService<Authentication.LoginSessionRegistry>()
                ?.IsSuperseded(context.Connection.ConnectionId) == true) return;
        var handler = (IClientMessageHandler)scope.ServiceProvider.GetRequiredService(handlerType);
        if (!handler.CanHandle(message))
        {
            throw new InvalidOperationException(
                $"Handler '{handlerType.Name}' rejected message CLR type '{message.GetType().Name}' " +
                $"registered as '{message.MessageType}'.");
        }

        await handler.HandleAsync(context, message, cancellationToken);
    }
}
