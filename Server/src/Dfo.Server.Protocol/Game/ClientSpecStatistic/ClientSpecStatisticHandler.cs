using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.ClientSpecStatistic;

/// <summary>
/// Consumes the CLIENT_SPEC_STATISTIC (0x00AB) hardware report and logs it.
/// The client sends it once after Game LOGIN and expects no response, so this handler
/// deliberately sends nothing. There is no storage consumer yet, so the report is only
/// logged with its semantic decode; it does not mutate domain state and therefore does
/// not go through the command bus.
/// </summary>
public sealed class ClientSpecStatisticHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ILogger<ClientSpecStatisticHandler> logger) : ClientMessageHandler<ClientSpecStatisticReportRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ClientSpecStatisticReportRequest message,
        CancellationToken cancellationToken)
    {
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

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Semantic decode of the four CLIENT_SPEC_STATISTIC slots, per
/// protocol/game/client-to-server/0x00AB.md (instruction-confirmed).
/// </summary>
internal static class ClientSpecStatisticSlotDescriptor
{
    private const int SlotCount = 4;

    internal static string DescribeCpu(IReadOnlyList<ClientSpecStatisticSlot> slots) =>
        Slot(slots, 0) is { } slot
            ? $"cores={slot.Value1 & 0x00FFFFFF} vendor={VendorBucket(slot.Value1 >> 24)} clock={ClockBucket(slot.Value2)}"
            : "missing";

    internal static string DescribeGraphics(IReadOnlyList<ClientSpecStatisticSlot> slots) =>
        Slot(slots, 1) is { } slot
            ? $"pci={slot.Value1:X4}:{slot.Value2:X4}"
            : "missing";

    internal static string DescribeOperatingSystem(IReadOnlyList<ClientSpecStatisticSlot> slots) =>
        Slot(slots, 2) is { } slot
            ? $"{OperatingSystemName(slot.Value1)} sp={slot.Value2}"
            : "missing";

    internal static string DescribeMemory(IReadOnlyList<ClientSpecStatisticSlot> slots) =>
        Slot(slots, 3) is { } slot
            ? $"bucket={slot.Value2} ({MemoryBucketFloor(slot.Value2)})"
            : "missing";

    private static ClientSpecStatisticSlot? Slot(IReadOnlyList<ClientSpecStatisticSlot> slots, byte tag) =>
        slots.Count == SlotCount && slots[tag].Tag == tag
            ? slots[tag]
            : null;

    private static string VendorBucket(uint bucket) => bucket switch
    {
        0 => "Intel",
        1 => "AMD",
        2 => "other",
        _ => $"unknown({bucket})",
    };

    private static string ClockBucket(uint bucket) => bucket switch
    {
        0 => "<=1050MHz",
        1 => "<=1650MHz",
        2 => "<=2450MHz",
        3 => ">2450MHz",
        _ => $"unknown({bucket})",
    };

    private static string OperatingSystemName(uint kind) => kind switch
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

    private static string MemoryBucketFloor(uint bucket) => bucket switch
    {
        0 => "<=136MB",
        1 => "<=200MB",
        2 => "<=264MB",
        3 => "<=392MB",
        4 => "<=520MB",
        5 => "<=1032MB",
        6 => "<=2056MB",
        7 => ">2056MB",
        _ => "unknown",
    };
}
