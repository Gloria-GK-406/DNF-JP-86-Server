using Microsoft.Extensions.DependencyInjection.Extensions;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Achievements;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Outbox;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// DR-16 (TASK-24 T15): with the title projection gate gone, a title event CMD that is queued while an entry CMD
/// is still running runs after it, and its projection is delivered on that connection after every entry batch.
/// </summary>
public sealed class TitleProjectionOrderTests
{
    [Fact]
    public async Task A_title_projection_queued_during_an_entry_is_delivered_after_the_whole_entry()
    {
        var directory = TestSessions.Directory(out var state);
        var player = directory.InWorld(state, new AccountId(8601), new CharacterId(8601), "title-1");
        await using var outbound = new XoDelivery(configure: services =>
        {
            services.AddSingleton<ISelectedSessionDomainService>(directory);
            services.AddSingleton<IAchievementDomainService>(new NoAchievements());
            services.AddEventDispatching();
            services.AddNotificationDispatching();
            services.TryAddSingleton(Dfo.Server.Testing.TestRequired.Absent<Dfo.Server.Abstractions.World.ITownAreaDomainService>());
            services.AddNotificationHandler<AchievementChanged, AchievementChangedHandler>();
            services.AddSingleton<AchievementEventSubscriber>();
        });
        outbound.Connect(player);
        outbound.Transport.Slow(player.ConnectionId, TimeSpan.FromMilliseconds(20));
        var entryRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var entry = outbound.RunAsync(async () =>
        {
            // Stands in for the entry sequence: several batches registered by one CMD, with a pause in between.
            await outbound.Game.SendNotiAsync(player.ConnectionId, new XoProjected("entry-1"), CancellationToken.None);
            entryRunning.SetResult();
            await Task.Delay(100);
            await outbound.Game.SendNotiAsync(player.ConnectionId, new XoProjected("entry-2"), CancellationToken.None);
        });
        await entryRunning.Task;
        var title = outbound.RunAsync(() => outbound.Services.GetRequiredService<AchievementEventSubscriber>().HandleAsync(
            new DomainEventEnvelope<AchievementProgressChanged>(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
                Guid.NewGuid(), new AchievementProgressChanged(player.CharacterId, 1)),
            CancellationToken.None).AsTask());
        await Task.WhenAll(entry, title);
        await outbound.SettledAsync();

        var entries = outbound.Recorder.Entries.ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal(["entry-1", "entry-2"], entries.Take(2).Select(value => ((XoProjected)value.Messages.Single()).Notification));
        Assert.IsType<AchievementStateMessage>(entries[2].Messages.Single());
        Assert.All(entries, value => Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundOutcome.Delivered, outbound.Observer.OutcomeOf(value)));
        Assert.Equal(3, outbound.Transport.Delivered.Count);
    }

    private sealed class NoAchievements : IAchievementDomainService
    {
        public ValueTask PrepareAsync(CharacterId character, int level, CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<CharacterAchievementProgressState>> GetAsync(CharacterId character, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<CharacterAchievementProgressState>>([]);
        public ValueTask<CharacterAchievementProgressState?> ReconcileAsync(CharacterId character, int achievementId, CancellationToken ct) =>
            ValueTask.FromResult<CharacterAchievementProgressState?>(null);
        public ValueTask<IReadOnlyList<int>> UnlockSatisfiedAchievementsAsync(CharacterId character, CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<int>>([]);
    }
}
