using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class SaoTowerNotificationTests
{
    [SkippableFact]
    public async Task Delayed_first_clear_event_refreshes_inventory_after_the_old_run_has_ended()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        await using var f = new LearningFixture(useArchive: true,
            configure: s => s.AddSingleton(new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path }));
        await f.SeedAsync();
        var assembly = typeof(Dfo.Server.Protocol.ProtocolModule).Assembly;
        var notificationType = assembly.GetType("Dfo.Server.Protocol.Game.SaoTowerRefresh")!;
        var handlerType = assembly.GetType("Dfo.Server.Protocol.Game.SaoTowerRefreshHandler")!;
        var reward = new SaoTowerRewardRefresh(new(71), [new(BusinessInventorySpace.Consumable, 0)]);
        var notification = Activator.CreateInstance(notificationType,
            [Guid.NewGuid(), ImmutableArray.Create(reward), DateTimeOffset.UtcNow])!;
        var handler = ActivatorUtilities.CreateInstance(f.Services, handlerType);
        var returned = handlerType.GetMethod("ComposeAsync")!.Invoke(handler,
            [new NotificationRecipient(new(71), f.Session.ConnectionId, f.Session), notification, CancellationToken.None]);
        var frames = await (ValueTask<IReadOnlyList<IServerNetworkMessage>>)returned!;
        Assert.Contains(frames, x => x.GetType().Name == "InventoryFullSnapshotMessage");
    }
}
