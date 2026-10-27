using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
namespace Dfo.Server.Protocol.Tests.Support;
// Projection algorithm fixtures use senders backed by RecordedOutbound. Production acceptance is tested through the real adapter.
internal sealed class GameOutboxFixture(IGameMessageSender sender) : IGameCommandOutbox
{
    public async ValueTask<bool> RegisterCmdWithOutcomeAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken token)
    { await sender.SendCmdAsync(id, message, token); return true; }
    public async ValueTask<bool> RegisterNotiWithOutcomeAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken token)
    { await sender.SendNotiAsync(id, message, token); return true; }
    public ValueTask RegisterCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken token) => sender.SendCmdAsync(id, message, token);
    public ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken token) => sender.SendNotiAsync(id, message, token);
    public ValueTask RegisterExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken token) => sender.SendExplicitAsync(id, delivery, token);
    public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, CancellationToken token) =>
        sender.SendExplicitAsync(connection.ConnectionId, new(messages, []), token);
}
internal sealed class ConnectionClosureFixture(INetworkConnectionCloser closer) : ICommandConnectionClosures
{
    public ValueTask RequestCloseAsync(NetworkConnectionContext context, CancellationToken token) => closer.RequestCloseAsync(context.ConnectionId, token);
    public ValueTask ResolveAndRequestCloseAsync(NetworkConnectionId id, CancellationToken token) => closer.RequestCloseAsync(id, token);
}
