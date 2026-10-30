using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.TauKingdom;

internal sealed record TauTicketRefresh(ImmutableArray<Dfo.Server.Data.Inventory.BusinessSlot> Slots, DateTimeOffset At)
    : INotification<TauTicketRefresh>;
internal sealed class TauTicketNotificationSubscriber(IEventService events) : IDomainEventSubscriber<TauKingdomTicketRefilled>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<TauKingdomTicketRefilled> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.CharacterId]),
            new TauTicketRefresh(message.Payload.Slots, message.OccurredAt), ct).ConfigureAwait(false);
}
internal sealed class TauTicketRefreshHandler(ISelectedSessionDomainService sessions,
    ICharacterInventoryEntryDomainService inventories, IAccountInventoryEntryDomainService accounts) : INotificationHandler<TauTicketRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, TauTicketRefresh notification, CancellationToken ct)
    {
        if (!sessions.TryGetInWorld(recipient.ConnectionId, out var session) || session.CharacterId != recipient.CharacterId
            || recipient.ExpectedSession is { } expected && expected != session) return [];
        var inventory = await inventories.ReadCurrentAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var account = await accounts.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var messages = notification.Slots.Select(x => A21InventoryLayout.ToGameSlot(x).Space).Distinct()
            .Select(space => (IServerNetworkMessage)new InventoryFullSnapshotMessage(
                inventory.Containers.Single(x => x.ContainerType == space), inventory, account, notification.At)).ToArray();
        return sessions.IsInWorld(session) ? messages : [];
    }
}
