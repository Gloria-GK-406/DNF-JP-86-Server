using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.ClientHardwareDetail;

public sealed record GameClientHardwareDetailCommand(NetworkDispatchContext Dispatch, ClientHardwareDetailReportRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameClientHardwareDetailCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ILogger<ClientHardwareDetailHandler> logger) : ICommandHandler<GameClientHardwareDetailCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameClientHardwareDetailCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        // The report follows LOGIN on the same connection; attribute it when possible but
        // stay tolerant of unexpected ordering rather than dropping the telemetry.
        var attributed = onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            && onlineUser is not null;

        logger.LogInformation(
            "Client hardware detail {ConnectionId} {Attribution}: cpu={Cpu} gpu={Gpu} os={OperatingSystem} memory={Memory}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId} onlineUser={onlineUser.OnlineUserId}" : "unattributed",
            ClientHardwareDetailDescriptor.DescribeCpu(message),
            ClientHardwareDetailDescriptor.DescribeGraphics(message),
            ClientHardwareDetailDescriptor.DescribeOperatingSystem(message),
            ClientHardwareDetailDescriptor.DescribeMemory(message));

        return ValueTask.FromResult(true);
    }
}
