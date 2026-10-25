using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.ClientSpecStatistic;

public sealed record GameClientSpecStatisticCommand(NetworkDispatchContext Dispatch, ClientSpecStatisticReportRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameClientSpecStatisticCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ILogger<ClientSpecStatisticHandler> logger) : ICommandHandler<GameClientSpecStatisticCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameClientSpecStatisticCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        // The report follows LOGIN on the same connection; attribute it when possible but
        // stay tolerant of unexpected ordering rather than dropping the telemetry.
        var attributed = onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            && onlineUser is not null;

        logger.LogInformation(
            "Client hardware report {ConnectionId} {Attribution}: trigger={Trigger} reported={Reported} cpu={Cpu} gpu={Gpu} os={OperatingSystem} memory={Memory}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId} onlineUser={onlineUser.OnlineUserId}" : "unattributed",
            message.TriggerPath,
            message.ReportedFlag,
            ClientSpecStatisticSlotDescriptor.DescribeCpu(message.Slots),
            ClientSpecStatisticSlotDescriptor.DescribeGraphics(message.Slots),
            ClientSpecStatisticSlotDescriptor.DescribeOperatingSystem(message.Slots),
            ClientSpecStatisticSlotDescriptor.DescribeMemory(message.Slots));

        return ValueTask.FromResult(true);
    }
}
