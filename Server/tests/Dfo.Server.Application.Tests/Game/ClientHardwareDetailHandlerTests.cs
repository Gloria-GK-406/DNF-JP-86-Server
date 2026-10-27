using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Protocol.Game.ClientHardwareDetail;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Game;

public sealed class ClientHardwareDetailHandlerTests
{
    [Fact]
    public async Task HandleAsync_LogsTheSemanticDecodeWithAccountAttribution()
    {
        var connectionId = new NetworkConnectionId("game-42");
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(connectionId, NetworkEndpointKind.Game, 10011));
        var onlineUsers = new StubOnlineUserQueryDomainService(new OnlineUserSnapshot(
            new OnlineUserId(Guid.NewGuid()),
            context.Connection,
            new AccountId(12),
            CurrentCharacterId: null,
            State: OnlineUserState.Authenticated,
            Generation: 1));
        var logger = new RecordingLogger();
        var online = onlineUsers;

        await LightweightMessageDispatch.Send(context, CreateReport(), online, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("account=12", entry.Message);
        Assert.Contains("cores=12 vendor=Intel clock=2611MHz", entry.Message);
        Assert.Contains("pci=8086:A7A8 video=4093MB", entry.Message);
        Assert.Contains("Windows10/11 bitness=64", entry.Message);
        Assert.Contains("16107MB", entry.Message);
    }

    [Fact]
    public async Task HandleAsync_ToleratesAnUnattributedConnection()
    {
        var connectionId = new NetworkConnectionId("game-7");
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(connectionId, NetworkEndpointKind.Game, 10011));
        var online = new StubOnlineUserQueryDomainService(snapshot: null);
        var logger = new RecordingLogger();

        await LightweightMessageDispatch.Send(context, CreateReport(), online, logger);
    }

    [Fact]
    public async Task HandleAsync_LabelsValuesOutsideTheSampledDomainsAsUnknown()
    {
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(new NetworkConnectionId("game-9"), NetworkEndpointKind.Game, 10011));
        var logger = new RecordingLogger();
        var online = new StubOnlineUserQueryDomainService(snapshot: null);
        var report = CreateReport() with
        {
            CpuVendorBucket = 9,
            OperatingSystemKind = 0xFF,
            OperatingSystemBitness = 0,
        };

        await LightweightMessageDispatch.Send(context, report, online, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("vendor=unknown(9)", entry.Message);
        Assert.Contains("unknown(255)", entry.Message);
        Assert.Contains("bitness=unknown", entry.Message);
    }

    private static ClientHardwareDetailReportRequest CreateReport() => new(
        new GameMessageMetadata(0x01, 0x01DD, 0, 2),
        CpuVendorBucket: 0,
        CpuLogicalCores: 12,
        CpuClockMhz: 2611,
        TotalMemoryMegabytes: 16107,
        GpuPciVendorId: 0x8086,
        GpuPciDeviceId: 0xA7A8,
        VideoMemoryMegabytes: 4093,
        OperatingSystemKind: 0x0E,
        OperatingSystemBitness: 2);

    private sealed class StubOnlineUserQueryDomainService(OnlineUserSnapshot? snapshot) : IOnlineUserQueryDomainService
    {
        public OnlineUserSnapshot GetRequired(OnlineUserId onlineUserId) =>
            snapshot ?? throw new InvalidOperationException("No online user registered.");

        public bool TryGetByConnection(NetworkConnectionId connectionId, out OnlineUserSnapshot? onlineUser)
        {
            onlineUser = snapshot;
            return snapshot is not null;
        }
    }

    private sealed class RecordingLogger : ILogger<ClientHardwareDetailHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
