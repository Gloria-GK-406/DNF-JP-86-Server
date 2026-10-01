using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.MagicSeal;

public sealed record MagicSealCompleted(EquipmentMagicSealReceipt Receipt, bool Change)
    : INotification<MagicSealCompleted>;

public sealed class MagicSealCompletedHandler(
    ISelectedSessionDomainService sessions,
    IInventorySlotProjectionDomainService slots,
    IBusinessInventorySlotDomainService inventory,
    ICharacterDomainService characters)
    : INotificationHandler<MagicSealCompleted>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(
        NotificationRecipient recipient, MagicSealCompleted notification,
        CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId
            || recipient.ConnectionId != receipt.Session.ConnectionId
            || sessions.Find(receipt.Session.AccountId) != receipt.Session)
            return [];

        var messages = new List<IServerNetworkMessage>();
        await AddSlot(messages, receipt.Session.CharacterId, receipt.TargetSlot,
            receipt.CommittedAt, ct).ConfigureAwait(false);
        var gold = await characters.Get(receipt.Session.CharacterId).GoldAsync(ct)
            .ConfigureAwait(false);
        messages.Add(GameInventoryDelivery.CarriedGoldNotification(
            gold, receipt.CommittedAt));
        // A21 CMD 0x01B6 refreshes the conversion panel from the current equipment.
        // Publish the committed item and gold first so its callback sees the new roll.
        if (notification.Change)
            messages.Add(new ChangeRandomOptionResponse(true));
        return messages;
    }

    private async ValueTask AddSlot(List<IServerNetworkMessage> messages,
        Dfo.Server.Data.Characters.CharacterId characterId, BusinessSlot slot,
        DateTimeOffset at, CancellationToken ct)
    {
        var bound = inventory.Find(characterId, slot);
        if (bound is null) return;
        var core = (await bound.Container.ReadSlotAsync(bound.Slot.Index, ct)
            .ConfigureAwait(false))?.Core;
        var game = slots.Project(slot);
        messages.Add(core is null
            ? GameInventoryDelivery.ClearedSlotNotification(game.Space, game.Index, at)
            : GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(game.Space,
                [new InventoryItemState(null, game.Index, core)], at)));
    }
}
