using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.RosterFollowUp;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Protocol.Game.SessionMetrics;
using Dfo.Server.Protocol.Game.TaggedIdReport;
using Microsoft.Extensions.Logging;
using Dfo.Server.Data.Inventory;

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

    /// <remarks>
    /// Answered, not merely logged. The reference server acknowledges every flag change, and that
    /// acknowledgement is where a tutorial step's rewards travel; a flag that gives nothing is
    /// still answered, with a count of zero.
    /// </remarks>
    [Fact]
    public async Task TaggedIdReport_AcknowledgesAndLogsIdAndFlag()
    {
        var logger = new RecordingLogger<TaggedIdReportHandler>();
        var sender = new RecordingExplicitSender();
        var handler = new TaggedIdReportHandler(
            Attributed(out var context), new NoSessions(), new NoRewards(),
            Rewarding(new RecordingInventory(), new NoPlacement()), sender, logger);

        await handler.HandleAsync(
            context,
            new TaggedIdReportRequest(GameMessageMetadata.Empty, 31, 1),
            CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("id=31 flag=1", entry.Message);
        var acknowledgement = Assert.IsType<TutorialFlagAcknowledgement>(Assert.Single(sender.Sent));
        Assert.Empty(acknowledgement.Granted);
    }

    /// <remarks>
    /// A flag the archive gives items for is still acknowledged with what was actually granted.
    /// Nothing can be granted yet -- no seam inserts an arbitrary item -- and reporting the
    /// declared items anyway would tell the client to draw what the server does not hold.
    /// </remarks>
    [Fact]
    public async Task TaggedIdReport_AcknowledgesTheSlotAnItemActuallyReached()
    {
        var logger = new RecordingLogger<TaggedIdReportHandler>();
        var sender = new RecordingExplicitSender();
        var stored = new RecordingInventory();
        var handler = new TaggedIdReportHandler(
            Attributed(out var context), new OneSession(), new OneReward(),
            Rewarding(stored, new KnownPlacement()), sender, logger);

        await handler.HandleAsync(
            context,
            new TaggedIdReportRequest(GameMessageMetadata.Empty, 38, 1),
            CancellationToken.None);

        var item = Assert.Single(
            Assert.IsType<TutorialFlagAcknowledgement>(Assert.Single(sender.Sent)).Granted);
        Assert.Equal(121, item.Slot);
        Assert.Equal(8474u, item.ItemId);
        Assert.Equal(1u, item.Count);
        Assert.Equal((8474, 1), Assert.Single(stored.Granted));
    }

    /// <summary>
    /// A step that gives two of one item settles them together, and the second sees the first.
    /// </summary>
    /// <remarks>
    /// This is what the step being one command buys. Under the previous shape each item was its own
    /// transaction, so the second could not see where the first had gone; here the pile the first
    /// opened is the pile the second joins, and both are acknowledged from the same slot.
    /// </remarks>
    [Fact]
    public async Task TaggedIdReport_SettlesEveryItemOfOneStepTogether()
    {
        var sender = new RecordingExplicitSender();
        var stored = new RecordingInventory();
        var handler = new TaggedIdReportHandler(
            Attributed(out var context), new OneSession(), new TwoOfOneReward(),
            Rewarding(stored, new KnownPlacement()), sender,
            new RecordingLogger<TaggedIdReportHandler>());

        await handler.HandleAsync(
            context,
            new TaggedIdReportRequest(GameMessageMetadata.Empty, 38, 1),
            CancellationToken.None);

        // One pile of two, not two piles of one.
        Assert.Equal([(121, 8474, 2)], stored.Rows());
        var granted = Assert.IsType<TutorialFlagAcknowledgement>(Assert.Single(sender.Sent)).Granted;
        Assert.Equal(2, granted.Count);
        Assert.All(granted, item => Assert.Equal(121, item.Slot));
    }

    [Fact]
    public async Task TaggedIdReport_ToleratesAnUnattributedConnection()
    {
        // It arrives after the server has answered EXIT, so the online user may already be gone.
        var logger = new RecordingLogger<TaggedIdReportHandler>();
        var handler = new TaggedIdReportHandler(
            Unattributed(out var context), new NoSessions(), new NoRewards(),
            Rewarding(new RecordingInventory(), new NoPlacement()), new RecordingExplicitSender(),
            logger);

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

    private sealed class NoPlacement : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(null);
    }

    /// <summary>A consumable band, as the archive resolves one.</summary>
    private sealed class KnownPlacement : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(new ItemPlacement(3, 100, 121, 176));
    }

    /// <summary>
    /// The bus the acknowledgement dispatches through, wired to the real reward command.
    /// </summary>
    /// <remarks>
    /// The command is transactional, so the boundary comes with it -- the container it writes
    /// through refuses a change made outside one.
    /// </remarks>
    private static ICommandBus Rewarding(
        RecordingInventory inventory, IItemPlacementDefinitionSource placements) =>
        new DirectCommandBus(
            scope: inventory.Scope,
            reward: new GrantTutorialRewardCommandHandler(inventory.Containers(placements)));

    /// <summary>The character's main container, and what reached it.</summary>
    private sealed class RecordingInventory() : FakeMainInventory(characterId: 1)
    {
        /// <summary>What the inventory ended up holding, as (item, count) pairs.</summary>
        public List<(int ItemId, int Count)> Granted =>
            [.. this.Rows().Select(row => (row.ItemId, row.Count))];
    }

    private sealed class NoSessions : ISelectedSessionDirectory
    {
        public SelectedSessionIdentity? RegisterReplacing(SelectedSessionIdentity session) => null;
        public bool TryRemoveExact(SelectedSessionIdentity session) => false;
        public SelectedSessionIdentity? Find(AccountId accountId) => null;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => null;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => null;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => null;
        public IReadOnlyList<SelectedSessionIdentity> Current() => [];
    }

    private sealed class OneSession : ISelectedSessionDirectory
    {
        private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
            new AccountId(1), new CharacterId(1), new NetworkConnectionId("Game:tutorial"), 1, 10011);

        public SelectedSessionIdentity? RegisterReplacing(SelectedSessionIdentity session) => null;
        public bool TryRemoveExact(SelectedSessionIdentity session) => false;
        public SelectedSessionIdentity? Find(AccountId accountId) => Session;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Session;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Session;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Session;
        public IReadOnlyList<SelectedSessionIdentity> Current() => [Session];
    }

    private sealed class NoRewards : ITutorialRewardDefinitionSource
    {
        public ValueTask<IReadOnlyList<TutorialRewardItem>> GetAsync(
            uint tutorialFlag, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TutorialRewardItem>>([]);
    }

    private sealed class OneReward : ITutorialRewardDefinitionSource
    {
        public ValueTask<IReadOnlyList<TutorialRewardItem>> GetAsync(
            uint tutorialFlag, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TutorialRewardItem>>(
                [new TutorialRewardItem(8474, 1)]);
    }

    /// <summary>A step that hands over the same item twice.</summary>
    private sealed class TwoOfOneReward : ITutorialRewardDefinitionSource
    {
        public ValueTask<IReadOnlyList<TutorialRewardItem>> GetAsync(
            uint tutorialFlag, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TutorialRewardItem>>(
                [new TutorialRewardItem(8474, 1), new TutorialRewardItem(8474, 1)]);
    }

    private sealed class RecordingExplicitSender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId,
            IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The tutorial acknowledgement is sent explicitly.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId,
            IGameNotiMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The tutorial acknowledgement is a CMD.");

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
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

    private static IOnlineUserQueryDomainService Attributed(out NetworkDispatchContext context)
    {
        context = new NetworkDispatchContext(
            new NetworkConnectionContext(new NetworkConnectionId("game-42"), NetworkEndpointKind.Game, 10011));
        return new StubOnlineUserQueryDomainService(new OnlineUserSnapshot(
            new OnlineUserId(Guid.NewGuid()),
            context.Connection,
            new AccountId(12),
            CurrentCharacterId: null,
            State: OnlineUserState.Authenticated,
            Generation: 1));
    }

    private static IOnlineUserQueryDomainService Unattributed(out NetworkDispatchContext context)
    {
        context = new NetworkDispatchContext(
            new NetworkConnectionContext(new NetworkConnectionId("game-7"), NetworkEndpointKind.Game, 10011));
        return new StubOnlineUserQueryDomainService(snapshot: null);
    }

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
