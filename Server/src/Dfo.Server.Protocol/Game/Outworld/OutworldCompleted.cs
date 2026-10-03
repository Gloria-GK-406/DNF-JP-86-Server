using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Outworld;

public sealed record OutworldCompleted(EquipmentOutworldReceipt Receipt,
    bool Purify, byte WireAction) : INotification<OutworldCompleted>;

public sealed class OutworldCompletedHandler(
    ISelectedSessionDomainService sessions,
    IInventorySlotProjectionDomainService slots,
    IBusinessInventorySlotDomainService inventory)
    : INotificationHandler<OutworldCompleted>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, OutworldCompleted notification,
        CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId
            || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session)
            return [];
        var targetBound = inventory.Find(receipt.Session.CharacterId, receipt.TargetSlot);
        var materialBound = inventory.Find(receipt.Session.CharacterId, receipt.MaterialSlot);
        if (targetBound is null || materialBound is null) return [];
        var target = await targetBound.Container.GetAsync(targetBound.Slot.Index, ct)
            .ConfigureAwait(false);
        var material = await materialBound.Container.GetAsync(materialBound.Slot.Index, ct)
            .ConfigureAwait(false);
        var targetCore = target?.ItemId == receipt.TargetItemId
            ? target.Snapshot.ToCore() : receipt.UpdatedCore.ToCore();
        var materialRemaining = material?.ItemId == receipt.MaterialItemId
            ? material.Count : 0;
        var messages = new List<IServerNetworkMessage>();
        var targetGame = slots.Project(receipt.TargetSlot);
        var materialGame = slots.Project(receipt.MaterialSlot);
        if (notification.Purify)
            messages.Add(new PurifyItemResponse(true, checked((short)materialGame.Index),
                materialRemaining, checked((short)targetGame.Index),
                targetCore.AmplifyType, targetCore.AmplifyValue));
        else
            messages.Add(new InvestAmplifyOptionResponse(true, notification.WireAction,
                checked((short)materialGame.Index), materialRemaining,
                checked((short)targetGame.Index), targetCore.AmplifyType,
                targetCore.AmplifyValue));
        AddSlot(messages, receipt.TargetSlot, target?.Snapshot.ToCore(), receipt.CommittedAt);
        AddSlot(messages, receipt.MaterialSlot, material?.Snapshot.ToCore(), receipt.CommittedAt);
        return messages;
    }

    private void AddSlot(List<IServerNetworkMessage> messages,
        BusinessSlot slot, InventoryItemCoreState? core, DateTimeOffset at)
    {
        var game = slots.Project(slot);
        messages.Add(core is null
            ? GameInventoryDelivery.ClearedSlotNotification(game.Space, game.Index, at)
            : GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(game.Space,
                [new InventoryItemState(null, game.Index, core)], at)));
    }
}
