using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.DarkElfRuins;

internal sealed record RuinsInventoryRefresh(IReadOnlyList<BusinessSlot> Slots, DateTimeOffset At) : INotification<RuinsInventoryRefresh>;
internal sealed class RuinsInventorySubscriber(IEventService events) : IDomainEventSubscriber<DarkElfRuinsInventoryChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<DarkElfRuinsInventoryChanged> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.CharacterId]),
            new RuinsInventoryRefresh(message.Payload.Slots, message.OccurredAt), ct).ConfigureAwait(false);
}
internal sealed class RuinsInventoryRefreshHandler(ISelectedSessionDomainService sessions,
    IBusinessInventorySlotDomainService inventory,
    IInventorySlotProjectionDomainService slots) : INotificationHandler<RuinsInventoryRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, RuinsInventoryRefresh notification, CancellationToken ct)
    {
        if (!sessions.TryGetInWorld(recipient.ConnectionId, out var session) || session.CharacterId != recipient.CharacterId
            || recipient.ExpectedSession is { } expected && expected != session || notification.Slots.Count == 0) return [];
        var changed = new List<(InventoryContainerType Space, InventoryItemState Item)>();
        foreach (var slot in notification.Slots.Distinct())
        {
            var bound = inventory.Find(session.CharacterId, slot);
            var item = bound is null ? null : await bound.Container.GetAsync(slot.Index, ct).ConfigureAwait(false);
            if (item is null) continue;
            var wire = slots.Project(slot);
            changed.Add((wire.Space, new(item.ItemUid, wire.Index, item.Snapshot.ToCore(), item.Snapshot.AvatarDetail)));
        }
        // A21 000E computes native gain text from positive absolute-count changes; 000D is silent.
        return sessions.IsInWorld(session) ? changed.GroupBy(x => x.Space)
            .Select(group => (IServerNetworkMessage)GameInventoryDelivery.Notification(new(group.Key,
                group.Select(x => x.Item).ToArray(), notification.At))).ToArray() : [];
    }
}
