using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Tests.Outbox;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// DR-8 (TASK-24): the room-mechanism subscriber over the real event service and notification bus. A transport
/// failure is the sender's (it closes the connection); only a composition failure is still thrown for a retry.
/// </summary>
public sealed class RoomMechanismProjectionFailureTests
{
    /// <summary>T11: the projection fails in transport; the subscriber completes and the connection is closed.</summary>
    [Fact]
    public async Task A_room_mechanism_projection_that_fails_in_transport_completes_and_closes_the_connection()
    {
        await using var world = new MechanismWorld();
        world.Outbound.Transport.Fail(world.Player.ConnectionId);

        await world.Outbound.RunAsync(() => world.HandleAsync());
        await world.Outbound.SettledAsync();

        Assert.Single(world.Outbound.Messages(world.Player.ConnectionId));
        Assert.True(await world.Outbound.ClosedAsync(world.Player.ConnectionId));
    }

    /// <summary>Plan R-5 (R8): a projection that cannot be composed is thrown as an IOException, so it is retried.</summary>
    [Fact]
    public async Task A_room_mechanism_projection_that_cannot_be_composed_is_thrown_for_a_retry()
    {
        await using var world = new MechanismWorld();
        world.Probe.Failing = true;

        await Assert.ThrowsAsync<IOException>(() => world.Outbound.RunAsync(() => world.HandleAsync()));
        await world.Outbound.SettledAsync();

        Assert.Empty(world.Outbound.Messages(world.Player.ConnectionId));
        Assert.False(world.Outbound.CloseRegistered(world.Player.ConnectionId));
    }

    private sealed class MechanismProbe
    {
        public bool Failing;
    }

    private sealed class MechanismProbeHandler(MechanismProbe probe) : INotificationHandler<DungeonRoomMechanismsNotification>
    {
        public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
            NotificationRecipient recipient, DungeonRoomMechanismsNotification notification, CancellationToken cancellationToken) =>
            probe.Failing
                ? throw new InvalidOperationException("Injected mechanism composition failure.")
                : ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([new XoProjected(nameof(DungeonRoomMechanismsNotification))]);
    }

    private sealed class OneOnline(SelectedSessionIdentity session) : ISelectedSessionSnapshotQuery
    {
        public IReadOnlyList<SelectedSessionIdentity> Snapshot() => [session];
    }

    private sealed class MechanismWorld : IAsyncDisposable
    {
        public MechanismWorld()
        {
            var directory = TestSessions.Directory(out var state);
            Player = directory.InWorld(state, new AccountId(8401), new CharacterId(8401), "mechanism-1");
            Outbound = new XoDelivery(configure: services =>
            {
                services.AddSingleton<ISelectedSessionDomainService>(directory);
                services.AddSingleton<ISelectedSessionSnapshotQuery>(new OneOnline(Player));
                services.AddSingleton(Probe);
                services.AddEventDispatching();
                services.AddNotificationDispatching();
                services.AddNotificationHandler<DungeonRoomMechanismsNotification, MechanismProbeHandler>();
                services.AddSingleton<DungeonRoomMechanismNotificationSubscriber>();
            });
            Outbound.Connect(Player);
        }

        public XoDelivery Outbound { get; }

        public MechanismProbe Probe { get; } = new();

        public SelectedSessionIdentity Player { get; }

        public async Task HandleAsync()
        {
            var view = new DungeonRoomMechanismView(DungeonRunId.New(), new MazeCell(0, 0), 1, [], [Player.CharacterId]);
            await Outbound.Services.GetRequiredService<DungeonRoomMechanismNotificationSubscriber>().HandleAsync(
                new DomainEventEnvelope<DungeonRoomMechanismsChanged>(
                    Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), new DungeonRoomMechanismsChanged(view)),
                CancellationToken.None);
        }

        public ValueTask DisposeAsync() => Outbound.DisposeAsync();
    }
}
