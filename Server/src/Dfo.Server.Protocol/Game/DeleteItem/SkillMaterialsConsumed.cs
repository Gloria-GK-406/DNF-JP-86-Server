using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record SkillMaterialsConsumed(SelectedSessionIdentity Session, SkillMaterialDebit Debit,
    DateTimeOffset PreparedAt) : INotification<SkillMaterialsConsumed>;

public sealed class SkillMaterialsConsumedHandler(ISelectedSessionDomainService sessions,
    IInventorySlotProjectionDomainService projection) : INotificationHandler<SkillMaterialsConsumed>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        SkillMaterialsConsumed notification, CancellationToken ct)
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
            update = GameInventoryDelivery.VirtualResourceNotification(
                [new(change.VirtualSlotIndex, change.ItemId, change.FinalCount)], notification.PreparedAt);
        }
        else
        {
            slot = projection.Project(((SkillMaterialLocation.Carried)debit.Cost.Location).Slot);
            update = debit.Remaining is null
                ? GameInventoryDelivery.ClearedSlotNotification(slot.Space, slot.Index, notification.PreparedAt)
                : GameInventoryDelivery.Notification(new(slot.Space,
                    [new InventoryItemState(null, slot.Index, debit.Remaining)], notification.PreparedAt));
        }
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([
            new DeleteItemResponse(slot, debit.Cost.Count, Success: true), update]);
    }
}
