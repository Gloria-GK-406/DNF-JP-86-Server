using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.BuyItem;

public sealed record NpcShopPurchaseCompleted(
    NpcShopPurchaseReceipt Receipt,
    QuestProgressSnapshot? QuestProgress = null) : INotification<NpcShopPurchaseCompleted>;

public sealed class NpcShopPurchaseCompletedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<NpcShopPurchaseCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, NpcShopPurchaseCompleted notification, CancellationToken cancellationToken)
    {
        var slots = new InventorySlotMappingRuleService();
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session)
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var messages = new List<IServerNetworkMessage>();
        foreach (var change in receipt.MaterialChanges)
        {
            var wire = slots.ToGameSlot(change.Slot);
            messages.Add(change.Core is { } core
                ? GameInventoryDelivery.Notification(new(wire.Space,
                    [new InventoryItemState(null, wire.Index, core.ToCore())], receipt.PreparedAt))
                : GameInventoryDelivery.ClearedSlotNotification(wire.Space, wire.Index, receipt.PreparedAt));
        }
        messages.Add(new BuyItemResponse(receipt));
        if (receipt.GoldSpent)
            messages.Add(GameInventoryDelivery.CarriedGoldNotification(receipt.Gold, receipt.PreparedAt));
        foreach (var group in receipt.GrantedSlots.GroupBy(grant => slots.ToGameSlot(grant.Slot).Space))
            messages.Add(GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(group.Key,
                group.Select(grant => new InventoryItemState(null, slots.ToGameSlot(grant.Slot).Index,
                    grant.Core.ToCore())).ToArray(), receipt.PreparedAt)));
        if (notification.QuestProgress is { } progress)
        {
            messages.Add(new AcceptedQuestProgressMessage(
                [.. progress.Quests.OrderBy(quest => quest.Slot)
                    .Select(quest => new AcceptedQuestProgressState(
                        checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))]));
        }
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(messages);
    }
}
