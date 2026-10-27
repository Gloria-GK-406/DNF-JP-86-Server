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

internal sealed record SouthernInventoryRefresh(ImmutableArray<Dfo.Server.Data.Inventory.BusinessSlot> Slots, DateTimeOffset At,
    bool RefreshQuestOffers = false)
    : INotification<SouthernInventoryRefresh>;
internal sealed class SouthernInventoryNotificationSubscriber(IEventService events) : IDomainEventSubscriber<SouthernDaleInventoryChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<SouthernDaleInventoryChanged> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.CharacterId]),
            new SouthernInventoryRefresh(message.Payload.Slots, message.OccurredAt), ct).ConfigureAwait(false);
}
internal sealed class SouthernDailyRefillNotificationSubscriber(IEventService events) : IDomainEventSubscriber<SouthernDaleDailyRefillCompleted>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<SouthernDaleDailyRefillCompleted> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.CharacterId]),
            new SouthernInventoryRefresh(message.Payload.Slots, message.OccurredAt, true), ct).ConfigureAwait(false);
}
internal sealed class SouthernInventoryRefreshHandler(ISelectedSessionDomainService sessions,
    ICharacterInventoryEntryDomainService inventories, IAccountInventoryEntryDomainService accounts,
    IInventorySlotProjectionDomainService slots, Dfo.Server.Abstractions.Characters.ICharacterDomainService characters,
    Dfo.Server.Abstractions.Quests.IQuestQueryDomainService quests) : INotificationHandler<SouthernInventoryRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, SouthernInventoryRefresh notification, CancellationToken ct)
    {
        if (!sessions.TryGetInWorld(recipient.ConnectionId, out var session) || session.CharacterId != recipient.CharacterId
            || recipient.ExpectedSession is { } expected && expected != session) return [];
        var messages = new List<IServerNetworkMessage>();
        if (!notification.Slots.IsEmpty)
        {
            var inventory = await inventories.ReadCurrentAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            var account = await accounts.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            messages.AddRange(notification.Slots.Select(x => slots.Project(x).Space).Distinct()
                .Select(space => (IServerNetworkMessage)new InventoryFullSnapshotMessage(
                    inventory.Containers.Single(x => x.ContainerType == space), inventory, account, notification.At)));
        }
        if (notification.RefreshQuestOffers)
        {
            var progress = await characters.Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Character progression is unavailable.");
            messages.Add(new AvailableQuestListMessage(progress.Level,
                await quests.ReadAvailableAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false)));
        }
        return sessions.IsInWorld(session) ? messages : [];
    }
}
