using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.SessionMetrics;

public sealed record GameSessionMetricsCommand(NetworkDispatchContext Dispatch, SessionMetricsReportRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameSessionMetricsCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ILogger<SessionMetricsHandler> logger) : ICommandHandler<GameSessionMetricsCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameSessionMetricsCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        var attributed = onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            && onlineUser is not null;

        logger.LogInformation(
            "Session metrics {ConnectionId} {Attribution}: means={Means} section7={Section7} section8={Section8}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId} onlineUser={onlineUser.OnlineUserId}" : "unattributed",
            SessionMetricsDescriptor.DescribeMeans(message.Means),
            SessionMetricsDescriptor.DescribeSection(message.Section7),
            SessionMetricsDescriptor.DescribeSection(message.Section8));

        return ValueTask.FromResult(true);
    }
}
