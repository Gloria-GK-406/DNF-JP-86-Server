using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Altar;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Tests.Outbox;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.Altar;

/// <summary>
/// DR-4 for the altar (TASK-24): a committed altar transition projected through the real event service and
/// notification bus into the outbox, read off the outbox and the outbound sender.
/// </summary>
public sealed class AltarProjectionFailureTests
{
    /// <summary>VC-4 (T6): one participant's projection fails in transport; only he is closed, nobody is fenced.</summary>
    [Fact]
    public async Task A_participant_whose_altar_projection_fails_in_transport_is_closed_alone_and_nobody_is_fenced()
    {
        await using var world = new AltarWorld();
        world.Outbound.Transport.Fail(world.Second.ConnectionId);

        await world.Outbound.RunFromAsync(world.First.ConnectionId, () => world.ProjectAsync());
        await world.Outbound.SettledAsync();

        Assert.Contains(world.Outbound.Messages(world.Second.ConnectionId), message => message is XoProjected);
        Assert.True(await world.Outbound.ClosedAsync(world.Second.ConnectionId));
        Assert.False(world.Outbound.Transport.IsClosed(world.First.ConnectionId));
        Assert.DoesNotContain(world.Outbound.Recorder.Entries, entry => entry.IsClose);
        Assert.False(world.Sessions.StandingOf(world.First)!.Fenced);
        Assert.False(world.Sessions.StandingOf(world.Second)!.Fenced);
    }

    /// <summary>
    /// AC-011 (fourth case, as corrected by TASK-24 ruling 1) and R4: after the commit, one participant's projection
    /// cannot be composed. Every participant is fenced and the failure becomes an uncertain outcome; on the inbound
    /// path the source connection is closed after the entries it already had, and nobody else is closed.
    /// </summary>
    [Fact]
    public async Task A_participant_whose_altar_projection_cannot_be_composed_fences_every_participant_and_closes_only_the_source()
    {
        await using var world = new AltarWorld(failing: new CharacterId(9002));

        await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() =>
            world.Outbound.RunFromAsync(world.First.ConnectionId, () => world.ProjectAsync()));
        await world.Outbound.SettledAsync();

        Assert.True(world.Sessions.StandingOf(world.First)!.Fenced);
        Assert.True(world.Sessions.StandingOf(world.Second)!.Fenced);
        var entries = world.Outbound.Recorder.Entries.ToList();
        var closes = entries.Where(entry => entry.IsClose).Select(entry => entry.Connection.ConnectionId).ToList();
        Assert.Equal([world.First.ConnectionId], closes);
        var projected = entries.FindIndex(entry => !entry.IsClose && entry.Connection.ConnectionId == world.First.ConnectionId);
        Assert.True(projected >= 0 && projected < entries.FindIndex(entry => entry.IsClose),
            "the source is closed after the projection it was already registered");
        Assert.Empty(world.Outbound.Messages(world.Second.ConnectionId));
        Assert.False(world.Outbound.Transport.IsClosed(world.Second.ConnectionId));
    }

    private sealed class AltarWorld : IAsyncDisposable
    {
        private readonly DungeonRunDomainService runs = new();
        private readonly DungeonRunDeliveryRegistry registry = new();
        private DungeonRunId runId;

        public AltarWorld(CharacterId? failing = null)
        {
            var directory = TestSessions.Directory(out var state);
            Sessions = directory;
            First = directory.InWorld(state, new AccountId(9001), new CharacterId(9001), "altar-1");
            Second = directory.InWorld(state, new AccountId(9002), new CharacterId(9002), "altar-2");
            Outbound = new XoDelivery(configure: services =>
            {
                services.AddSingleton<ISelectedSessionDomainService>(directory);
                services.AddSingleton(new AltarProbeRule(failing));
                services.AddEventDispatching();
                services.AddNotificationDispatching();
                services.AddNotificationHandler<AltarStateChanged, AltarProbeHandler>();
            });
            Outbound.Connect(First, Second);
        }

        public XoDelivery Outbound { get; }

        public ISelectedSessionDomainService Sessions { get; }

        public SelectedSessionIdentity First { get; }

        public SelectedSessionIdentity Second { get; }

        /// <summary>Prepares the run and projects one committed transition, inside the running job.</summary>
        public async Task ProjectAsync()
        {
            runId = runs.PreparePartyEntry(
                [
                    new DungeonRunParticipant(First.AccountId, First.CharacterId, Slot: 0, IsLeader: true),
                    new DungeonRunParticipant(Second.AccountId, Second.CharacterId, Slot: 1, IsLeader: false),
                ],
                Layout,
                difficulty: 0,
                new DungeonCellSeed(Layout.StartCell, Layout.StartRoomMapId, FirstActorSequence: 1, Actors: [], GroundItems: []),
                DungeonRunId.New(),
                DateTimeOffset.UtcNow).Run.RunId;
            registry.Register(runId, [First, Second]);
            var delivery = new AltarProjectionDelivery(
                null!, runs, Sessions, registry, null!, Outbound.Services.GetRequiredService<IEventService>(),
                TimeProvider.System, new AltarWakeSignal(TimeProvider.System),
                new SelectedSessionSafety(Outbound.Services, NullLogger<SelectedSessionSafety>.Instance),
                NullLogger<AltarProjectionDelivery>.Instance);
            var state = new AltarDungeonState(runId,
                new AltarDungeonDefinition(Layout.DungeonId, AltarDungeonKind.Endless, 1, 1, 1, 0, [],
                    new(0, 0, 0, 0, [], [], [], [], ""), 3),
                [], DateTimeOffset.UtcNow, 1, Layout.StartRoomMapId);
            await delivery.ProjectAsync(new AltarActionResult(state), CancellationToken.None);
        }

        public ValueTask DisposeAsync() => Outbound.DisposeAsync();

        private static readonly DungeonLayout Layout = new(
            144, 0, 1, 1, new MazeCell(0, 0), new MazeCell(0, 0), 8153, 7,
            new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153) });
    }

    private sealed record AltarProbeRule(CharacterId? Failing);

    /// <summary>Stands in for the altar's composer: one message per recipient, or a failure for the chosen one.</summary>
    private sealed class AltarProbeHandler(AltarProbeRule rule) : INotificationHandler<AltarStateChanged>
    {
        public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
            NotificationRecipient recipient, AltarStateChanged notification, CancellationToken cancellationToken) =>
            recipient.CharacterId == rule.Failing
                ? throw new InvalidOperationException("Injected altar composition failure.")
                : ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([new XoProjected(nameof(AltarStateChanged))]);
    }
}
