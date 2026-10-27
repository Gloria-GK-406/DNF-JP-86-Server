using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.MapEntryReports;

public sealed record GameMapEntryReportCommand(NetworkDispatchContext Dispatch, IMapEntryReport Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => Input switch
    {
        CeraQueryReport report => new ProtocolFrameMetadata.Game(report.Metadata),
        RuntimeTingStatisticReport report => new ProtocolFrameMetadata.Game(report.Metadata),
        _ => throw new InvalidOperationException("Unknown map-entry report shape."),
    };
}

public sealed class GameMapEntryReportCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ILogger<MapEntryReportHandler> logger) : ICommandHandler<GameMapEntryReportCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameMapEntryReportCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        var attributed = onlineUsers.TryGetByConnection(
            context.Connection.ConnectionId, out var onlineUser) && onlineUser is not null;
        var detail = message switch
        {
            CeraQueryReport => "CERA (empty body)",
            RuntimeTingStatisticReport report => $"STATICS_RUNTIME_TING value={report.Value}",
            _ => message.GetType().Name,
        };

        logger.LogInformation(
            "Map entry report {ConnectionId} {Attribution}: {Detail}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId}" : "unattributed",
            detail);

        return ValueTask.FromResult(true);
    }
}
