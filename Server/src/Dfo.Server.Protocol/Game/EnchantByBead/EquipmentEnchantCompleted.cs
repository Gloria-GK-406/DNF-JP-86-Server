using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.EnchantByBead;

public sealed record EquipmentEnchantCompleted(
    EquipmentEnchantReceipt Receipt, EnchantByBeadResponse Response)
    : INotification<EquipmentEnchantCompleted>;

public sealed class EquipmentEnchantCompletedHandler(
    ISelectedSessionDomainService sessions,
    IInventorySlotProjectionDomainService slots,
    IBusinessInventorySlotDomainService inventory)
    : INotificationHandler<EquipmentEnchantCompleted>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, EquipmentEnchantCompleted notification,
        CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId
            || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session)
            return [];

        // The initial post-commit publish and any retry both read authoritative current slots.
        // This prevents an old sequence from projecting a stale bead count after later actions.
        var bead = inventory.Find(receipt.Session.CharacterId, receipt.BeadSlot);
        var target = inventory.Find(receipt.Session.CharacterId, receipt.TargetSlot);
        if (bead is null || target is null) return [];
        var beadCore = (await bead.Container.ReadSlotAsync(bead.Slot.Index, ct)
            .ConfigureAwait(false))?.Core;
        var targetCore = (await target.Container.ReadSlotAsync(target.Slot.Index, ct)
            .ConfigureAwait(false))?.Core;

        var messages = new List<IServerNetworkMessage>();
        AddSlot(messages, receipt.BeadSlot, beadCore, receipt.PreparedAt);
        AddSlot(messages, receipt.TargetSlot, targetCore, receipt.PreparedAt);
        messages.Add(notification.Response);
        return messages;
    }

    private void AddSlot(List<IServerNetworkMessage> messages, BusinessSlot slot,
        InventoryItemCoreState? core, DateTimeOffset at)
    {
        var game = slots.Project(slot);
        messages.Add(core is null
            ? GameInventoryDelivery.ClearedSlotNotification(game.Space, game.Index, at)
            : GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(game.Space,
                [new InventoryItemState(null, game.Index, core)], at)));
    }
}
