using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Commands;

internal sealed class CommandJobEntry(GameplayOutboxes outboxes, IGameplayQueue queue, IServiceScopeFactory scopeFactory)
{
    public void Begin() => _ = outboxes.Current;

    public ValueTask RejectUnregisteredAsync(NetworkDispatchContext context, IClientNetworkMessage message, CancellationToken ct) =>
        queue.RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Connection, context.Connection.ConnectionId),
            MessageHandlerRegistry.CommandTypeOf(message.MessageType), async token =>
            {
                Begin();
                await using var scope = scopeFactory.CreateAsyncScope();
                var services = scope.ServiceProvider;
                var registry = services.GetRequiredService<MessageHandlerRegistry>();
                if (registry.TryGet(message.MessageType, out _))
                    throw new InvalidOperationException("A registered message cannot use the unregistered-message rejection entry.");
                if (services.GetRequiredService<SelectedSessionSafety>().IsBlocked(context.Connection)
                    || services.GetService<LoginSessionRegistry>()?.IsSuperseded(context.Connection.ConnectionId) == true) return;
                _ = registry.GetHandlerType(message.MessageType);
                throw new InvalidOperationException("A registered message cannot use the unregistered-message rejection entry.");
            }, context.Connection), ct);
}
