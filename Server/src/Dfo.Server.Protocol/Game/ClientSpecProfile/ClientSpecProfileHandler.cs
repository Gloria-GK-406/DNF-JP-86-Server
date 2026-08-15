using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.ClientSpecProfile;

/// <summary>
/// Consumes the CLIENT_SPEC_PROFILE (0x00C2) hardware report and logs it.
/// Fire-and-forget like 0x00AB and 0x01DD: no response, no domain change, no command bus.
/// The 336-byte table is summarised rather than dumped -- it is all zeroes in every sample so
/// far, and its meaning is unconfirmed, so the log records how much of it is populated instead
/// of pretending to interpret it.
/// </summary>
public sealed class ClientSpecProfileHandler(
    IOnlineUserQueryService onlineUsers,
    ILogger<ClientSpecProfileHandler> logger) : ClientMessageHandler<ClientSpecProfileReportRequest>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        ClientSpecProfileReportRequest message,
        CancellationToken cancellationToken)
    {
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
            $"off19={message.Unknown19}",
            ClientSpecProfileDescriptor.DescribeTable(message.Table),
            ClientSpecProfileDescriptor.DescribeEntries(message.Entries));

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Semantic decode of the CLIENT_SPEC_PROFILE fields that have confirmed meaning, per
/// protocol/game/client-to-server/0x00C2.md. Everything else is reported as
/// raw values so the log never asserts more than the protocol record does.
/// </summary>
internal static class ClientSpecProfileDescriptor
{
    internal static string DescribeOperatingSystem(byte kind) => kind switch
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

    /// <summary>Formats the DirectX version the way the client's own LagLog.txt does: the value is
    /// the standard major-in-high-word encoding, so 0x00090000 reads as 9.0.0.</summary>
    internal static string DescribeDirectXVersion(uint version) =>
        $"{version >> 16}.{(version >> 8) & 0xFF}.{version & 0xFF}";

    /// <summary>The client writes -1 for a timing it never measured; report that as unmeasured
    /// rather than as a negative duration.</summary>
    internal static string DescribeSeconds(short seconds) =>
        seconds < 0 ? "unmeasured" : $"{seconds}s";

    /// <summary>Reports how many of the fixed rows carry any non-zero value, so a populated table
    /// becomes visible in the log without dumping 60 numbers per report.</summary>
    internal static string DescribeTable(IReadOnlyList<ClientSpecProfileRow> table)
    {
        var populated = table.Count(IsPopulated);
        return populated == 0
            ? $"{table.Count}rows all-zero"
            : $"{table.Count}rows {populated}populated";
    }

    internal static string DescribeEntries(IReadOnlyList<ClientSpecProfileEntry> entries) =>
        entries.Count == 0
            ? "none"
            : string.Join(",", entries.Select(entry => $"{entry.Kind}:{entry.Value}"));

    private static bool IsPopulated(ClientSpecProfileRow row) =>
        row.Words.Any(word => word != 0)
        || row.Pairs.Any(pair => pair.Integer != 0 || pair.Real != 0f);
}
