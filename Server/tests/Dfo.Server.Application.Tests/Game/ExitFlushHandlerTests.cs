using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.RosterFollowUp;
using Dfo.Server.Protocol.Game.SessionMetrics;
using Dfo.Server.Protocol.Game.TaggedIdReport;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Game;

/// <summary>
/// The three logging-only handlers added for the frames surrounding a normal client exit:
/// SESSION_METRICS (0x00FA), the tagged id report (0x008F) and the roster follow-up (0x02A8).
/// </summary>
public sealed class ExitFlushHandlerTests
{
    [Fact]
    public async Task SessionMetrics_ReportsNothingSampledForTheObservedAllZeroBody()
    {
        var logger = new RecordingLogger<SessionMetricsHandler>();
        var handler = new SessionMetricsHandler(Attributed(out var context), logger);

        await handler.HandleAsync(context, AllZeroMetrics(), CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("account=12", entry.Message);
        // Seven zero means say "the client measured nothing", which must not read as seven zeroes.
        Assert.Contains("7tags nothing-sampled", entry.Message);
        Assert.Contains("section7=empty", entry.Message);
        Assert.Contains("section8=empty", entry.Message);
    }

    [Fact]
    public async Task SessionMetrics_DistinguishesMeasuredMeansFromUnsampledOnes()
    {
        var logger = new RecordingLogger<SessionMetricsHandler>();
        var handler = new SessionMetricsHandler(Unattributed(out var context), logger);
        var means = Enumerable.Range(0, 7)
            .Select(index => new SessionMetricMean((ushort)index, index == 2 ? 480u : 0u))
            .ToArray();

        await handler.HandleAsync(
            context,
            new SessionMetricsReportRequest(GameMessageMetadata.Empty, means, [], []),
            CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("unattributed", entry.Message);
        Assert.Contains("0:-,1:-,2:480,3:-", entry.Message);
    }

    [Fact]
    public async Task SessionMetrics_RendersPopulatedSections()
    {
        var logger = new RecordingLogger<SessionMetricsHandler>();
        var handler = new SessionMetricsHandler(Unattributed(out var context), logger);
        var report = new SessionMetricsReportRequest(
            GameMessageMetadata.Empty,
            [.. Enumerable.Range(0, 7).Select(index => new SessionMetricMean((ushort)index, 0))],
            [new SessionMetricRecord(1, 2, 3, 4, 5)],
            [new SessionMetricRecord(9, 8, 7, 6, 5), new SessionMetricRecord(0, 0, 0, 0, 1)]);

        await handler.HandleAsync(context, report, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("section7=1/2/3/4/5", entry.Message);
        Assert.Contains("section8=9/8/7/6/5;0/0/0/0/1", entry.Message);
    }

    [Fact]
    public async Task TaggedIdReport_LogsIdAndFlag()
    {
        var logger = new RecordingLogger<TaggedIdReportHandler>();
        var handler = new TaggedIdReportHandler(Attributed(out var context), logger);

        await handler.HandleAsync(
            context,
            new TaggedIdReportRequest(GameMessageMetadata.Empty, 31, 1),
            CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("id=31 flag=1", entry.Message);
    }

    [Fact]
    public async Task TaggedIdReport_ToleratesAnUnattributedConnection()
    {
        // It arrives after the server has answered EXIT, so the online user may already be gone.
        var logger = new RecordingLogger<TaggedIdReportHandler>();
        var handler = new TaggedIdReportHandler(Unattributed(out var context), logger);

        await handler.HandleAsync(
            context,
            new TaggedIdReportRequest(GameMessageMetadata.Empty, 77, 0),
            CancellationToken.None);

        Assert.Contains("unattributed", Assert.Single(logger.Entries).Message);
    }

    [Fact]
    public async Task RosterFollowUp_FlagsTheSentinelButNotOtherValues()
    {
        var logger = new RecordingLogger<RosterFollowUpHandler>();
        var sender = new CountingGameMessageSender();
        var handler = new RosterFollowUpHandler(Attributed(out var context), sender, logger);

        await handler.HandleAsync(
            context,
            new RosterFollowUpRequest(GameMessageMetadata.Empty, 0xFFFF),
            CancellationToken.None);
        await handler.HandleAsync(
            context,
            new RosterFollowUpRequest(GameMessageMetadata.Empty, 0x1234),
            CancellationToken.None);

        Assert.Equal(2, logger.Entries.Count);
        Assert.Contains("0xFFFF (sentinel)", logger.Entries[0].Message);
        Assert.Contains("0x1234", logger.Entries[1].Message);
        Assert.DoesNotContain("sentinel", logger.Entries[1].Message);

        // Answered, not merely logged. Both values get a reply: the reference server's handler
        // does not look at the body at all.
        Assert.Equal(2, sender.CmdCount);
    }

    private sealed class CountingGameMessageSender : IGameMessageSender
    {
        public int CmdCount { get; private set; }

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken)
        {
            CmdCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The roster follow-up answers with a CMD.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("This handler does not use explicit delivery.");
    }

    private static SessionMetricsReportRequest AllZeroMetrics() => new(
        new GameMessageMetadata(0x01, 0x00FA, 0xF7BFEDE4, 9),
        [.. Enumerable.Range(0, 7).Select(index => new SessionMetricMean((ushort)index, 0))],
        [],
        []);

    private static IOnlineUserQueryService Attributed(out NetworkDispatchContext context)
    {
        context = new NetworkDispatchContext(
            new NetworkConnectionContext(new NetworkConnectionId("game-42"), NetworkEndpointKind.Game, 10011));
        return new StubOnlineUserQueryService(new OnlineUserSnapshot(
            new OnlineUserId(Guid.NewGuid()),
            context.Connection,
            new AccountId(12),
            CurrentCharacterId: null,
            State: OnlineUserState.Authenticated,
            Generation: 1));
    }

    private static IOnlineUserQueryService Unattributed(out NetworkDispatchContext context)
    {
        context = new NetworkDispatchContext(
            new NetworkConnectionContext(new NetworkConnectionId("game-7"), NetworkEndpointKind.Game, 10011));
        return new StubOnlineUserQueryService(snapshot: null);
    }

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

    private sealed class RecordingLogger<T> : ILogger<T>
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
