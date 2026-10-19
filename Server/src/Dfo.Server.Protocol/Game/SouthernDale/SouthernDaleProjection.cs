using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.SouthernDale;

internal sealed record SouthernInventoryRefresh(ImmutableArray<Dfo.Server.Data.Inventory.BusinessSlot> Slots, DateTimeOffset At)
    : INotification<SouthernInventoryRefresh>;
internal sealed class SouthernInventoryNotificationSubscriber(IEventService events) : IDomainEventSubscriber<SouthernDaleInventoryChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<SouthernDaleInventoryChanged> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.CharacterId]),
            new SouthernInventoryRefresh(message.Payload.Slots, message.OccurredAt), ct).ConfigureAwait(false);
}
internal sealed class SouthernInventoryRefreshHandler(ISelectedSessionDomainService sessions,
    ICharacterInventoryEntryDomainService inventories, IAccountInventoryEntryDomainService accounts,
    IInventorySlotProjectionDomainService slots) : INotificationHandler<SouthernInventoryRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, SouthernInventoryRefresh notification, CancellationToken ct)
    {
        var session = sessions.FindByConnection(recipient.ConnectionId);
        if (session is null || session.CharacterId != recipient.CharacterId
            || recipient.ExpectedSession is { } expected && expected != session) return [];
        var inventory = await inventories.ReadCurrentAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var account = await accounts.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var messages = notification.Slots.Select(x => slots.Project(x).Space).Distinct()
            .Select(space => (IServerNetworkMessage)new InventoryFullSnapshotMessage(
                inventory.Containers.Single(x => x.ContainerType == space), inventory, account, notification.At)).ToArray();
        return sessions.FindByConnection(recipient.ConnectionId) == session ? messages : [];
    }
}
