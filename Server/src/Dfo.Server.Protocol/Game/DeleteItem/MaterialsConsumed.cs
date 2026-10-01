using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record MaterialsConsumed(SelectedSessionIdentity Session, MaterialDebit Debit,
    DateTimeOffset PreparedAt) : INotification<MaterialsConsumed>;

public sealed class MaterialsConsumedHandler(ISelectedSessionDomainService sessions,
    IInventorySlotProjectionDomainService projection) : INotificationHandler<MaterialsConsumed>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        MaterialsConsumed notification, CancellationToken ct)
    {
        if (recipient.CharacterId != notification.Session.CharacterId
            || recipient.ConnectionId != notification.Session.ConnectionId
            || sessions.Find(notification.Session.AccountId) != notification.Session)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var debit = notification.Debit;
        GameSlot slot;
        IGameServerMessage update;
        if (debit.AccountChange is { } change)
        {
            slot = new(InventoryContainerType.Main, change.VirtualSlotIndex);
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([
                new DeleteItemResponse(slot, debit.Cost.Count, Success: true),
                .. AccountMaterialNotifications.Compose(
                    [new(change.VirtualSlotIndex, change.ItemId, change.FinalCount)], notification.PreparedAt)]);
        }
        else
        {
            slot = projection.Project(((MaterialLocation.Carried)debit.Cost.Location).Slot);
            update = debit.Remaining is null
                ? GameInventoryDelivery.ClearedSlotNotification(slot.Space, slot.Index, notification.PreparedAt)
                : GameInventoryDelivery.Notification(new(slot.Space,
                    [new InventoryItemState(null, slot.Index, debit.Remaining)], notification.PreparedAt));
        }
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([
            new DeleteItemResponse(slot, debit.Cost.Count, Success: true), update]);
    }
}
