using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Mail;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal sealed record ExpiredItemsRefresh(SelectedSessionIdentity Owner,
    System.Collections.Immutable.ImmutableArray<InventoryContainerType> Containers, DateTimeOffset At,
    bool MailboxChanged = false) : INotification<ExpiredItemsRefresh>;

internal sealed class ExpiredItemsNotificationSubscriber(IEventService events,
    ISelectedSessionDomainService sessions, SelectedSessionTransitionGate transitions,
    ICharacterVisibleSessionDomainService visible) : IDomainEventSubscriber<OnlineExpiredItemsRemoved>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<OnlineExpiredItemsRemoved> message, CancellationToken ct)
    {
        var change = message.Payload;
        await using var lease = await transitions.BorrowOrAcquireExclusiveAsync(change.AccountId, ct).ConfigureAwait(false);
        var owner = sessions.Find(change.AccountId);
        if (owner is null || owner.CharacterId != change.CharacterId || owner.ConnectionId.Value != change.ConnectionId
            || owner.ConnectionGeneration != change.ConnectionGeneration || !sessions.IsInWorld(owner)) return;
        var recipients = change.Containers.Contains(InventoryContainerType.Equipment)
            ? visible.Resolve(owner) : [owner];
        await events.ProjectAsync(new NotificationAudience.TheseSessions(recipients), new ExpiredItemsRefresh(owner, change.Containers, message.OccurredAt, change.MailboxChanged), ct)
            .ConfigureAwait(false);
    }
}

internal sealed class ExpiredItemsRefreshHandler(ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService views, ICharacterVisibleSessionDomainService visible,
    ICharacterInventoryEntryDomainService inventories, IAccountInventoryEntryDomainService accounts,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService? housing = null,
    IMailboxQueryDomainService? mailbox = null)
    : INotificationHandler<ExpiredItemsRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ExpiredItemsRefresh notification, CancellationToken ct)
    {
        var change = notification;
        var owner = change.Owner;
        if (!sessions.TryGetInWorld(recipient.ConnectionId, out var receiver) || receiver.CharacterId != recipient.CharacterId
            || recipient.ExpectedSession is { } expected && expected != receiver
            || !sessions.IsInWorld(owner) || !visible.Resolve(owner).Contains(receiver)) return [];
        var inventory = await inventories.ReadCurrentAsync(owner.AccountId, owner.CharacterId, ct).ConfigureAwait(false);
        var account = await accounts.LoadAsync(owner.AccountId, owner.CharacterId, ct).ConfigureAwait(false);
        if (!sessions.IsInWorld(receiver)
            || !visible.Resolve(owner).Contains(receiver)) return [];
        var messages = new List<IServerNetworkMessage>();
        if (receiver == owner)
        {
            foreach (var type in change.Containers.Distinct())
            {
                // Equipment is projected by user-state frames, as on title equip/unequip.
                if (type == InventoryContainerType.Equipment) continue;
                var container = inventory.Containers.Single(x => x.ContainerType == type);
                messages.Add(new InventoryFullSnapshotMessage(container, inventory, account, change.At));
            }
            messages.Add(new InventoryLocksMessage(inventory.EquipmentLocks, account.Cargo.EquipmentLocks));
            if (change.MailboxChanged && mailbox is not null)
                messages.Add(new MailboxStateMessage(MailboxProjection.Map(await mailbox.ReadStorageAsync(owner.CharacterId, ct).ConfigureAwait(false))));
        }
        if (change.Containers.Contains(InventoryContainerType.Equipment))
        {
            var current = await views.TryLoadAsync(owner, ct).ConfigureAwait(false);
            if (current is null || !sessions.IsInWorld(receiver)
                || !visible.Resolve(owner).Contains(receiver)) return [];
            messages.AddRange(CharacterUserStateProjection.Compose(current, receiver == owner));
            if (receiver == owner) messages.AddRange(await HousingCurrentProjection.ReadAsync(housing, owner, ct).ConfigureAwait(false));
        }
        return messages;
    }
}
