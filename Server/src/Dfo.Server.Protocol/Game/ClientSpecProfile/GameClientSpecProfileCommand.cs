using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.ClientSpecProfile;

public sealed record GameClientSpecProfileCommand(NetworkDispatchContext Dispatch, ClientSpecProfileReportRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameClientSpecProfileCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ILogger<ClientSpecProfileHandler> logger) : ICommandHandler<GameClientSpecProfileCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameClientSpecProfileCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        var attributed = onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            && onlineUser is not null;

        logger.LogInformation(
            "Client spec profile {ConnectionId} {Attribution}: cpu={Cpu} gpu={Gpu} os={OperatingSystem} "
            + "normalized={Normalized} directX={DirectX} timings={Timings} unknown={Unknown} "
            + "table={Table} entries={Entries}",
            context.Connection.ConnectionId,
            attributed ? $"account={onlineUser!.AccountId} onlineUser={onlineUser.OnlineUserId}" : "unattributed",
            $"cores={message.CpuLogicalCores} clock={message.CpuClockMhz}MHz",
            $"pci={message.GpuPciVendorId:X4}:{message.GpuPciDeviceId:X4}",
            ClientSpecProfileDescriptor.DescribeOperatingSystem(message.OperatingSystemKind),
            $"video={message.NormalizedVideoMemoryMegabytes}MB total={message.NormalizedTotalMemoryMegabytes}MB",
            ClientSpecProfileDescriptor.DescribeDirectXVersion(message.DirectXVersion),
            $"startLoading={ClientSpecProfileDescriptor.DescribeSeconds(message.StartLoadingSeconds)}"
            + $" returnVillage={ClientSpecProfileDescriptor.DescribeSeconds(message.ReturnVillageSeconds)}",
            $"off15={message.Unknown15} off20={message.Unknown20}",
            ClientSpecProfileDescriptor.DescribeTable(message.Table),
            ClientSpecProfileDescriptor.DescribeEntries(message.Entries));

        return ValueTask.FromResult(true);
    }
}
