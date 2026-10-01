using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Dispatching;

internal sealed class NetworkMessageDispatcher(
    MessageHandlerRegistry registry,
    IServiceScopeFactory scopeFactory) : INetworkMessageDispatcher
{
    public async ValueTask DispatchAsync(
        NetworkDispatchContext context,
        IClientNetworkMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(message);

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
}
