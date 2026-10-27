using System.Reflection;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.RoomMechanisms;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    // Method-level recovery oracle: actual old Direct subscriber vs new command projection hook.
    // The controlled notification bus checks audience/outcome, not actual encoding or sender behavior.
    [Theory]
    [InlineData("empty")]
    [InlineData("unstarted")]
    [InlineData("current")]
    [InlineData("replacement")]
    [InlineData("failed")]
    public async Task Task44_mechanism_snapshot_requeries_current_selection_and_preserves_failed_projection(string mode)
    {
        var bus = new MechanismBus(mode == "failed");
        using var f = Fixture(configure: services => services.AddSingleton<INotificationBus>(bus));
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var layout = new DungeonLayout(150, 0, 1, 1, new(0, 0), new(0, 0), 800, 42,
                new Dictionary<Dfo.Server.Abstractions.Content.MazeCell, Dfo.Server.Abstractions.Content.DungeonCellDefinition> { [new(0, 0)] = Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(800) });
            var definition = new DungeonRoomMechanismDefinition(DungeonRoomMechanismKind.Elevator, TimeSpan.FromSeconds(1), 3);
            var cell = DungeonCellSeed.CompleteEmpty(new(0, 0), 800).Snapshot() with
            { Mechanisms = mode == "empty" ? [] : [new(definition, mode == "unstarted" ? null : DateTimeOffset.UnixEpoch, 2)] };
            var run = new DungeonRunState(layout, new(0, 0), 800, new HashSet<Dfo.Server.Abstractions.Content.MazeCell> { new(0, 0) }, DateTimeOffset.UnixEpoch,
                RunId: DungeonRunId.New(), CurrentCellState: cell);
            var expected = f.Session;
            if (mode == "replacement")
            {
                expected = f.Session with { ConnectionId = new("task44-replacement"), ConnectionGeneration = f.Session.ConnectionGeneration + 1 };
                f.Services.GetRequiredService<ISelectedSessionDomainService>().Select(expected);
            }
            var type = typeof(FinishLoadingHandler).Assembly.GetType("Dfo.Server.Protocol.Game.GameMapLoadingCommandHandler");
            async Task Invoke()
            {
                Assert.NotNull(type);
                var handler = ActivatorUtilities.CreateInstance(f.Services, type);
                await (ValueTask)type.GetMethod("LoadingMechanismsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(handler, [f.Session, run, CancellationToken.None])!;
            }
            if (mode == "failed")
            { var error = await Assert.ThrowsAsync<IOException>(Invoke); Assert.Equal("Room mechanism projection failed; the current snapshot will be retried.", error.Message); }
            else await Invoke();
            if (mode is "empty" or "unstarted") Assert.Empty(bus.Audiences);
            else
            {
                Assert.Equal(expected, Assert.Single(Assert.IsType<NotificationAudience.TheseSessions>(Assert.Single(bus.Audiences)).Sessions));
                var notification = Assert.Single(bus.Values);
                Assert.Equal(run.RunId, notification.View.RunId); Assert.Equal(run.RoomVersion, notification.View.RoomVersion);
                Assert.Equal(cell.Mechanisms, notification.View.States); Assert.Equal(f.Session.CharacterId, Assert.Single(notification.View.Recipients));
            }
        });
    }
    private sealed class MechanismBus(bool fail) : INotificationBus
    {
        internal readonly List<NotificationAudience> Audiences = [];
        internal readonly List<DungeonRoomMechanismsNotification> Values = [];
        public ValueTask<NotificationDelivery> PublishAsync<T>(NotificationAudience audience, T notification, CancellationToken token) where T : class, INotification<T>
        {
            Audiences.Add(audience); var value = Assert.IsType<DungeonRoomMechanismsNotification>(notification); Values.Add(value);
            return ValueTask.FromResult(new NotificationDelivery([new(value.View.Recipients.Single(), fail ? NotificationOutcome.Failed : NotificationOutcome.Registered)]));
        }
    }
}
