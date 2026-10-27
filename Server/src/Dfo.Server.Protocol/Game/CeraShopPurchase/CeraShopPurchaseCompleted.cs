using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.CeraShopPurchase;

public sealed record CeraShopPurchaseCompleted(CeraShopPurchaseReceipt Receipt) : INotification<CeraShopPurchaseCompleted>;

public sealed class CeraShopPurchaseCompletedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<CeraShopPurchaseCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        CeraShopPurchaseCompleted notification, CancellationToken cancellationToken)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var messages = new List<IServerNetworkMessage>();
        // The purchase-success handler derives the next offer from the current cargo capacity.
        // Refresh the upgrade state and inventory before it runs, while the window is still open.
        if (receipt.ExpandedCharacterInventory is { } character && receipt.ExpandedAccountInventory is { } account)
            foreach (var type in receipt.ExpandedContainers)
            {
                if (type is InventoryContainerType.AccountCargo or InventoryContainerType.PersonalCargo)
                    messages.Add(new CargoCapacityUpgradeMessage(type));
                messages.Add(new InventoryFullSnapshotMessage(type == InventoryContainerType.AccountCargo
                    ? account.Cargo.Container : character.Containers.Single(c => c.ContainerType == type),
                    character, account, receipt.PreparedAt));
            }
        foreach (var product in receipt.ProductIds) messages.Add(new CeraShopPurchaseResponse(product));
        messages.Add(GameInstanceDelivery.Wallet(receipt.Balances));
        if (receipt.GoldSpent) messages.Add(GameInventoryDelivery.CarriedGoldNotification(receipt.Gold, receipt.PreparedAt));
        messages.AddRange(GameInventoryDelivery.Notifications(receipt.Updates));
        if (receipt.CreaturesChanged) messages.Add(GameInstanceDelivery.Creatures(receipt.Pets));
        if (receipt.PurchaseRestrictions is { } restrictions)
            messages.Add(new PurchaseRestrictionCompatibilityMessage(restrictions));
        foreach (var premium in receipt.GrantedPremiums)
            messages.Add(GameInstanceDelivery.Premium(premium, receipt.PreparedAt));
        if (receipt.PremiumEntitlements is { } entitlements)
            messages.Add(new PremiumServiceStateMessage(entitlements, receipt.PremiumUsageSlots, receipt.PreparedAt));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(messages);
    }
}
