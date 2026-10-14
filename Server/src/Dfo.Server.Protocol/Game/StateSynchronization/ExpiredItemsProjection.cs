using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

internal sealed record ExpiredItemsRefresh(SelectedSessionIdentity Owner,
    System.Collections.Immutable.ImmutableArray<InventoryContainerType> Containers, DateTimeOffset At) : INotification<ExpiredItemsRefresh>;

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
            || owner.ConnectionGeneration != change.ConnectionGeneration) return;
        var recipients = change.Containers.Contains(InventoryContainerType.Equipment)
            ? visible.Resolve(owner) : [owner];
        await events.ProjectAsync(new NotificationAudience.TheseSessions(recipients), new ExpiredItemsRefresh(owner, change.Containers, message.OccurredAt), ct)
            .ConfigureAwait(false);
    }
}

internal sealed class ExpiredItemsRefreshHandler(ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService views, ICharacterVisibleSessionDomainService visible,
    ICharacterInventoryEntryDomainService inventories, IAccountInventoryEntryDomainService accounts)
    : INotificationHandler<ExpiredItemsRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        ExpiredItemsRefresh notification, CancellationToken ct)
    {
        var change = notification;
        var owner = change.Owner;
        var receiver = sessions.FindByConnection(recipient.ConnectionId);
        if (receiver is null || receiver.CharacterId != recipient.CharacterId
            || recipient.ExpectedSession is { } expected && expected != receiver
            || sessions.Find(owner.AccountId) != owner || !visible.Resolve(owner).Contains(receiver)) return [];
        var inventory = await inventories.ReadCurrentAsync(owner.AccountId, owner.CharacterId, ct).ConfigureAwait(false);
        var account = await accounts.LoadAsync(owner.AccountId, owner.CharacterId, ct).ConfigureAwait(false);
        if (sessions.FindByConnection(receiver.ConnectionId) != receiver
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
        }
        if (change.Containers.Contains(InventoryContainerType.Equipment))
        {
            var current = await views.TryLoadAsync(owner, ct).ConfigureAwait(false);
            if (current is null || sessions.FindByConnection(receiver.ConnectionId) != receiver
                || !visible.Resolve(owner).Contains(receiver)) return [];
            var equipment = current.CharacterInventory.Containers.Single(x => x.ContainerType == InventoryContainerType.Equipment).Items;
            if (receiver == owner)
                messages.Add(new ExtendedUserStateMessage(current.Character, current.CharacterEntry.Public,
                    current.CharacterEntry.Skills, equipment, current.World.PreparedAt));
            messages.Add(new CurrentUserStateMessage(current.Character, current.CharacterEntry.Public,
                equipment, current.CharacterEntry.Features.Pets));
        }
        return messages;
    }
}
