using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Protocol.Game.ClientSpecProfile;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Game;

public sealed class ClientSpecProfileHandlerTests
{
    [Fact]
    public async Task HandleAsync_LogsTheSemanticDecodeWithAccountAttribution()
    {
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(new NetworkConnectionId("game-42"), NetworkEndpointKind.Game, 10011));
        var onlineUsers = new StubOnlineUserQueryService(new OnlineUserSnapshot(
            new OnlineUserId(Guid.NewGuid()),
            context.Connection,
            new AccountId(12),
            CurrentCharacterId: null,
            State: OnlineUserState.Authenticated,
            Generation: 1));
        var logger = new RecordingLogger();
        var handler = new ClientSpecProfileHandler(onlineUsers, logger);

        await handler.HandleAsync(context, CreateReport(), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("account=12", entry.Message);
        Assert.Contains("cores=12 clock=2611MHz", entry.Message);
        Assert.Contains("pci=8086:A7A8", entry.Message);
        Assert.Contains("Windows10/11", entry.Message);
        Assert.Contains("video=4096MB total=8192MB", entry.Message);
        Assert.Contains("directX=9.0.0", entry.Message);
        Assert.Contains("startLoading=unmeasured returnVillage=unmeasured", entry.Message);
        Assert.Contains("off19=6", entry.Message);
        Assert.Contains("6rows all-zero", entry.Message);
        Assert.Contains("entries=none", entry.Message);
    }

    [Fact]
    public async Task HandleAsync_ToleratesAnUnattributedConnection()
    {
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(new NetworkConnectionId("game-7"), NetworkEndpointKind.Game, 10011));
        var handler = new ClientSpecProfileHandler(
            new StubOnlineUserQueryService(snapshot: null), new RecordingLogger());

        await handler.HandleAsync(context, CreateReport(), CancellationToken.None);
    }

    [Fact]
    public async Task HandleAsync_SurfacesAPopulatedTableAndEntryList()
    {
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(new NetworkConnectionId("game-9"), NetworkEndpointKind.Game, 10011));
        var logger = new RecordingLogger();
        var handler = new ClientSpecProfileHandler(new StubOnlineUserQueryService(snapshot: null), logger);
        var rows = CreateTable();
        rows[2] = new ClientSpecProfileRow([0, 7, 0, 0], rows[2].Pairs);
        rows[4] = new ClientSpecProfileRow(rows[4].Words, [.. rows[4].Pairs.Take(5), new ClientSpecProfilePair(0, 2.5f)]);
        var report = CreateReport() with
        {
            Table = rows,
            Entries = [new ClientSpecProfileEntry(3, 1200), new ClientSpecProfileEntry(1, 40)],
        };

        await handler.HandleAsync(context, report, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("6rows 2populated", entry.Message);
        Assert.Contains("entries=3:1200,1:40", entry.Message);
    }

    [Fact]
    public async Task HandleAsync_ReportsMeasuredTimingsAsDurations()
    {
        // Every sample so far carries -1 for both timings because the session never entered the
        // game. A session that did would carry real seconds, which must not read as "unmeasured".
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(new NetworkConnectionId("game-11"), NetworkEndpointKind.Game, 10011));
        var logger = new RecordingLogger();
        var handler = new ClientSpecProfileHandler(new StubOnlineUserQueryService(snapshot: null), logger);
        var report = CreateReport() with { StartLoadingSeconds = 23, ReturnVillageSeconds = 0 };

        await handler.HandleAsync(context, report, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("startLoading=23s returnVillage=0s", entry.Message);
    }

    private static ClientSpecProfileRow[] CreateTable() =>
        [.. Enumerable.Range(0, 6).Select(_ => new ClientSpecProfileRow(
            new ushort[4],
            [.. Enumerable.Range(0, 6).Select(_ => new ClientSpecProfilePair(0, 0f))]))];

    private static ClientSpecProfileReportRequest CreateReport() => new(
        new GameMessageMetadata(0x01, 0x00C2, 0, 8),
        CpuClockMhz: 2611,
        CpuLogicalCores: 12,
        GpuPciVendorId: 0x8086,
        GpuPciDeviceId: 0xA7A8,
        NormalizedVideoMemoryMegabytes: 4096,
        NormalizedTotalMemoryMegabytes: 8192,
        OperatingSystemKind: 0x0E,
        DirectXVersion: 0x00090000,
        Unknown19: 6,
        StartLoadingSeconds: -1,
        ReturnVillageSeconds: -1,
        Table: CreateTable(),
        Entries: []);

    private sealed class StubOnlineUserQueryService(OnlineUserSnapshot? snapshot) : IOnlineUserQueryService
    {
        public OnlineUserSnapshot GetRequired(OnlineUserId onlineUserId) =>
            snapshot ?? throw new InvalidOperationException("No online user registered.");

        public bool TryGetByConnection(NetworkConnectionId connectionId, out OnlineUserSnapshot? onlineUser)
        {
            onlineUser = snapshot;
            return snapshot is not null;
        }
    }

    private sealed class RecordingLogger : ILogger<ClientSpecProfileHandler>
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
