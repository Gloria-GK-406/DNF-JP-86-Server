using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Game;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Game;

public sealed class ClientSpecStatisticHandlerTests
{
    [Fact]
    public async Task HandleAsync_LogsTheSemanticDecodeWithAccountAttribution()
    {
        var connectionId = new NetworkConnectionId("game-42");
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(connectionId, NetworkEndpointKind.Game, 10011));
        var onlineUsers = new StubOnlineUserQueryService(new OnlineUserSnapshot(
            new OnlineUserId(Guid.NewGuid()),
            context.Connection,
            new AccountId(12),
            CurrentCharacterId: null,
            State: OnlineUserState.Authenticated,
            Generation: 1));
        var logger = new RecordingLogger();
        var handler = new ClientSpecStatisticHandler(onlineUsers, logger);

        await handler.HandleAsync(context, CreateReport(), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("account=12", entry.Message);
        Assert.Contains("trigger=128", entry.Message);
        Assert.Contains("cores=12 vendor=Intel clock=>2450MHz", entry.Message);
        Assert.Contains("pci=8086:A7A8", entry.Message);
        Assert.Contains("Windows10/11 sp=0", entry.Message);
        Assert.Contains("bucket=7 (>2056MB)", entry.Message);
    }

    [Fact]
    public async Task HandleAsync_ToleratesAnUnattributedConnection()
    {
        var connectionId = new NetworkConnectionId("game-7");
        var context = new NetworkDispatchContext(
            new NetworkConnectionContext(connectionId, NetworkEndpointKind.Game, 10011));
        var handler = new ClientSpecStatisticHandler(
            new StubOnlineUserQueryService(snapshot: null), new RecordingLogger());

        await handler.HandleAsync(context, CreateReport(), CancellationToken.None);
    }

    private static ClientSpecStatisticReportRequest CreateReport() => new(
        new GameMessageMetadata(0x01, 0x00AB, 0, 1),
        TriggerPath: 0x80,
        ReportedFlag: 0,
        Slots:
        [
            new ClientSpecStatisticSlot(0, 12, 3),
            new ClientSpecStatisticSlot(1, 0x8086, 0xA7A8),
            new ClientSpecStatisticSlot(2, 14, 0),
            new ClientSpecStatisticSlot(3, 0, 7),
        ]);

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

    private sealed class RecordingLogger : ILogger<ClientSpecStatisticHandler>
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
