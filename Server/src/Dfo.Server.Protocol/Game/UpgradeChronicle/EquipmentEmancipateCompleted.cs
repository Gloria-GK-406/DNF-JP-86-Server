using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.UpgradeChronicle;

public sealed record EquipmentEmancipateCompleted(EquipmentEmancipateReceipt Receipt)
    : INotification<EquipmentEmancipateCompleted>;

/// <summary>
/// The ACK lowers the ticket and material piles, then absolute notifications settle every touched slot.
/// </summary>
/// <remarks>
/// The ACK never installs the new template, so the piece is cleared and refilled in place: A21 will not
/// replace an occupied slot's item with another ID through one update (same rule as the opening flow).
/// Account-material slots that reached zero are left to the ACK, which already removed them.
/// </remarks>
public sealed class EquipmentEmancipateCompletedHandler(
    ISelectedSessionDomainService sessions,
    IBusinessInventorySlotDomainService inventory)
    : INotificationHandler<EquipmentEmancipateCompleted>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, EquipmentEmancipateCompleted notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId
            || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session))
            return [];
        var debits = receipt.Consumed.Select(x =>
        {
            var game = OpeningWireSlots.Project(x.Slot);
            return new UpgradeChronicleDebit((byte)game.Space, checked((short)game.Index), x.Count);
        }).ToArray();
        var messages = new List<IServerNetworkMessage> { new UpgradeChronicleResponse(true, Debits: debits) };
        foreach (var slot in receipt.Consumed.Select(x => x.Slot).Distinct())
        {
            var bound = inventory.Find(receipt.Session.CharacterId, slot);
            var current = bound is null ? null : await bound.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false);
            var game = OpeningWireSlots.Project(slot);
            if (current is not null)
                messages.Add(Fill(game, current.Snapshot.ToCore(), receipt.CommittedAt));
            else if (slot.Space != BusinessInventorySpace.SharedMaterial)
                messages.Add(GameInventoryDelivery.ClearedSlotNotification(game.Space, game.Index, receipt.CommittedAt));
        }
        var target = A21InventoryLayout.ToGameSlot(receipt.TargetSlot);
        var targetBound = inventory.Find(receipt.Session.CharacterId, receipt.TargetSlot);
        var piece = targetBound is null ? null : await targetBound.Container.GetAsync(targetBound.Slot.Index, ct).ConfigureAwait(false);
        messages.Add(GameInventoryDelivery.ClearedSlotNotification(target.Space, target.Index, receipt.CommittedAt));
        if (piece is not null) messages.Add(Fill(target, piece.Snapshot.ToCore(), receipt.CommittedAt));
        return messages;
    }

    private static IServerNetworkMessage Fill(GameSlot slot, InventoryItemCoreState core, DateTimeOffset at) =>
        GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(slot.Space,
            [new InventoryItemState(null, slot.Index, core)], at));
}
