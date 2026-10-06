using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Forging;

public sealed record ForgingCompleted(EquipmentForgingReceipt Receipt)
    : INotification<ForgingCompleted>;

public sealed class ForgingCompletedHandler(
    ISelectedSessionDomainService sessions,
    IInventorySlotProjectionDomainService slots,
    IBusinessInventorySlotDomainService inventory)
    : INotificationHandler<ForgingCompleted>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, ForgingCompleted notification,
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
        var targetGame = slots.Project(receipt.TargetSlot);
        var materialGame = slots.Project(receipt.MaterialSlot);
        var remaining = material?.ItemId == receipt.MaterialItemId ? material.Count : 0;
        var messages = new List<IServerNetworkMessage>
        {
            new ForgingResponse(true, MaterialSlot: checked((short)materialGame.Index),
                MaterialRemaining: remaining, OldLevel: receipt.OldLevel,
                ForgeSucceeded: receipt.Succeeded, NewLevel: receipt.NewLevel,
                TargetListType: (byte)targetGame.Space,
                TargetSlot: checked((short)targetGame.Index)),
        };
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
