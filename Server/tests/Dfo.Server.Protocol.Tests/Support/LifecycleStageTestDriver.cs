using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
namespace Dfo.Server.Protocol.Game;

// These direct-stage fixtures exercise the lifecycle implementation; dispatcher/wire suites own bus coverage.
internal static class LifecycleStageTestDriver
{
    private static CommandExecutionContext Context(bool disconnected)
    {
        var request = new CommandRequestContext.TrustedInternal(new(GameplayCallSource.Lifecycle,
            GameplayJob.Current!.Value, disconnected ? "lifecycle:disconnected" : "lifecycle:connected"))
            { DisconnectedLifecycleCleanup = disconnected };
        return new(Guid.NewGuid(), disconnected ? typeof(DisconnectedLifecycleCommand) : typeof(ConnectedLifecycleCommand),
            typeof(OnlineUserConnectionObserver), CommandExecutionKind.Direct) { Request = request };
    }
    internal static ValueTask ConnectedAsync(this OnlineUserConnectionObserver observer, NetworkConnectionContext connection, CancellationToken token) =>
        observer.ConnectedAsync(Context(false), connection, token);
    internal static ValueTask DisconnectedAsync(this OnlineUserConnectionObserver observer, NetworkConnectionContext connection, CancellationToken token) =>
        observer.DisconnectedAsync(Context(true), connection, token);
}
internal sealed class LifecycleTestOutbox(IGameMessageSender sender) : IGameCommandOutbox
{
    public ValueTask RegisterCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken ct) => sender.SendCmdAsync(id, message, ct);
    public ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken ct) => sender.SendNotiAsync(id, message, ct);
    public ValueTask RegisterBatchAsync(NetworkConnectionContext connection, IReadOnlyList<IGameServerMessage> messages, CancellationToken ct) => sender.SendBatchAsync(connection, messages, ct);
    public ValueTask RegisterExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct) => sender.SendExplicitAsync(id, delivery, ct);
}
