using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Game;

/// <summary>
/// Consumes the CLIENT_HARDWARE_DETAIL (0x01DD) hardware report and logs it.
/// The client sends it right after CLIENT_SPEC_STATISTIC (0x00AB) and expects no response, so this
/// handler deliberately sends nothing. There is no storage consumer yet, so the report is only
/// logged with its semantic decode; it does not mutate domain state and therefore does not go
/// through the command bus.
/// </summary>
public sealed class ClientHardwareDetailHandler(
    IOnlineUserQueryService onlineUsers,
    ILogger<ClientHardwareDetailHandler> logger) : ClientMessageHandler<ClientHardwareDetailReportRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ClientHardwareDetailReportRequest message,
        CancellationToken cancellationToken)
    {
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

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Semantic decode of the CLIENT_HARDWARE_DETAIL fields, per
/// protocol/game/client-to-server/0x01DD-client-hardware-detail.
/// The vendor bucket and OS enum share their value domains with 0x00AB; the video memory field
/// is reported as raw megabytes because its exact source API is not yet confirmed.
/// </summary>
internal static class ClientHardwareDetailDescriptor
{
    internal static string DescribeCpu(ClientHardwareDetailReportRequest message) =>
        $"cores={message.CpuLogicalCores} vendor={VendorBucket(message.CpuVendorBucket)} clock={message.CpuClockMhz}MHz";

    internal static string DescribeGraphics(ClientHardwareDetailReportRequest message) =>
        $"pci={message.GpuPciVendorId:X4}:{message.GpuPciDeviceId:X4} video={message.VideoMemoryMegabytes}MB";

    internal static string DescribeOperatingSystem(ClientHardwareDetailReportRequest message) =>
        $"{OperatingSystemName(message.OperatingSystemKind)} {Bitness(message.OperatingSystemBitness)}";

    internal static string DescribeMemory(ClientHardwareDetailReportRequest message) =>
        $"{message.TotalMemoryMegabytes}MB";

    private static string VendorBucket(byte bucket) => bucket switch
    {
        0 => "Intel",
        1 => "AMD",
        2 => "other",
        _ => $"unknown({bucket})",
    };

    private static string Bitness(byte bitness) => bitness switch
    {
        0 => "bitness=unknown",
        1 => "bitness=32",
        2 => "bitness=64",
        _ => $"bitness=unknown({bitness})",
    };

    private static string OperatingSystemName(byte kind) => kind switch
    {
        1 => "Windows98",
        3 => "WindowsME",
        4 => "Windows95/NT4",
        6 => "Windows2000",
        7 => "WindowsXP",
        9 => "WindowsVista",
        0xA => "Windows7SP1",
        0xC => "Windows8",
        0xD => "Windows8.1",
        0xE => "Windows10/11",
        _ => $"unknown({kind})",
    };
}
