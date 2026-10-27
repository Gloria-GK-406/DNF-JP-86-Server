using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.DarkElfRuins;

internal sealed record RuinsInventoryRefresh(IReadOnlyList<BusinessSlot> Slots, DateTimeOffset At) : INotification<RuinsInventoryRefresh>;
internal sealed class RuinsInventorySubscriber(IEventService events) : IDomainEventSubscriber<DarkElfRuinsInventoryChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<DarkElfRuinsInventoryChanged> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.CharacterId]),
            new RuinsInventoryRefresh(message.Payload.Slots, message.OccurredAt), ct).ConfigureAwait(false);
}
internal sealed class RuinsInventoryRefreshHandler(ISelectedSessionDomainService sessions,
    ICharacterInventoryEntryDomainService inventories, IAccountInventoryEntryDomainService accounts,
    IInventorySlotProjectionDomainService slots) : INotificationHandler<RuinsInventoryRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, RuinsInventoryRefresh notification, CancellationToken ct)
    {
        if (!sessions.TryGetInWorld(recipient.ConnectionId, out var session) || session.CharacterId != recipient.CharacterId
            || recipient.ExpectedSession is { } expected && expected != session || notification.Slots.Count == 0) return [];
        var inventory = await inventories.ReadCurrentAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var account = await accounts.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        return sessions.IsInWorld(session) ? notification.Slots.Select(x => slots.Project(x).Space).Distinct()
            .Select(space => (IServerNetworkMessage)new InventoryFullSnapshotMessage(
                inventory.Containers.Single(x => x.ContainerType == space), inventory, account, notification.At)).ToArray() : [];
    }
}
