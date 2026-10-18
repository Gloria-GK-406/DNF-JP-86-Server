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
        if (recipient.CharacterId != receipt.Session.CharacterId && receipt.Housing is { } shared && receipt.HousingAudience.Contains(recipient.CharacterId))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([new HousingStorageMessage(shared)]);
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
        var inventory = receipt.GrantedSlots.GroupBy(grant => slots.ToGameSlot(grant.Slot).Space)
            .Select(group => GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(group.Key,
                group.Select(grant => new InventoryItemState(null, slots.ToGameSlot(grant.Slot).Index,
                    grant.Core.ToCore())).ToArray(), receipt.PreparedAt))).ToArray();
        if (receipt.Housing is { } housing) messages.Add(new HousingStorageMessage(housing));
        if (receipt.LovePoints is { } points) messages.Add(new Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointNotification(receipt.Session.CharacterId.Value, points));
        var projectedStack = receipt.Housing is not null || receipt.GrantedSlots.Count > 0 && receipt.GrantedSlots[0].Core.IsPile;
        // Apply the absolute stack once while the client still has the pre-purchase count, so
        // 000E reports exactly the gain. The subsequent ACK confirms without incrementing again.
        if (projectedStack) messages.AddRange(inventory);
        messages.Add(new BuyItemResponse(receipt) { InventoryAlreadyProjected = projectedStack });
        if (receipt.GoldSpent)
            messages.Add(GameInventoryDelivery.CarriedGoldNotification(receipt.Gold, receipt.PreparedAt));
        if (!projectedStack) messages.AddRange(inventory);
        // Native ACK Data also increments the shop UI's limit counter (A2B9B0). Its zero grant
        // must therefore be followed by the committed absolute counter via 02CC (D1E0FF).
        if (receipt.PurchaseCount is { } count)
            messages.Add(new ShopPurchaseCountResponse(new(NpcShopRejection.None, [count])));
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
