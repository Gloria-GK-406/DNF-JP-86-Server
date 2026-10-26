using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Dispatching;

internal sealed class NetworkMessageDispatcher(
    MessageHandlerRegistry registry,
    IServiceScopeFactory scopeFactory,
    Commands.CommandJobEntry entry) : INetworkMessageDispatcher
{
    public ValueTask DispatchAsync(
        NetworkDispatchContext context,
        IClientNetworkMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

        if (!registry.TryGet(message.MessageType, out var route))
            return entry.RejectUnregisteredAsync(context, message, cancellationToken);
        return route.IsTelemetry
            ? DispatchTelemetryAsync(context, message, route.HandlerType, cancellationToken)
            : DispatchProtocolAsync(context, message, route, cancellationToken);
    }

    private async ValueTask DispatchProtocolAsync(NetworkDispatchContext context, IClientNetworkMessage message,
        MessageHandlerRoute route, CancellationToken ct)
    {
        if (route.AllowedMessageClrTypes?.Contains(message.GetType()) != true)
            throw new InvalidOperationException("The protocol route received an unbound message CLR type.");
        await using var scope = scopeFactory.CreateAsyncScope();
        var handler = (IClientMessageHandler)scope.ServiceProvider.GetRequiredService(route.HandlerType);
        if (!handler.CanHandle(message)) throw new InvalidOperationException("The protocol handler rejected its registered message.");
        await handler.HandleAsync(context, message, ct).ConfigureAwait(false);
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
