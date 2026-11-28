using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class MailItemRuleService(IMailItemDefinitionSource definitions)
    : IMailItemRuleService, IPlayerTradeItemRuleService
{
    // For limited-transfer stacks, the high three bits store the remaining transfers.
    private const int OneTransfer = 1 << 5;

    public async ValueTask<MailItemSendDecision> CanSendAsync(
        ItemSnapshot core, bool sameAccount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(core);
        var refusal = InstanceRefusal(core);
        if (refusal != MailItemSendRefusal.None) return new(refusal);

        var definition = await definitions.GetAsync(core.ItemId, cancellationToken).ConfigureAwait(false);
        return new(definition is null
            ? MailItemSendRefusal.UnknownTemplate
            : AttachmentRefusal(core, definition.AttachType, sameAccount, preservesTransferCount: false));
    }

    public async ValueTask<PlayerTradeItemDecision> CanTradeAsync(ItemSnapshot item, BusinessInventorySpace space,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (InstanceRefusal(item) != MailItemSendRefusal.None
            || item.IsMirror || item.Type is ItemTraits.Avatar or ItemTraits.Creature
            || item.AvatarDetail is not null || item.CreatureDetail is not null
            || item.SortLockFlag != 0 || item.Lifetime.IsExpired(now))
            return new(false);

        if (space == BusinessInventorySpace.SharedMaterial)
            return new(AccountMaterialCatalog.Definitions.Any(material => material.ItemId == item.ItemId
                && material.Kind <= AccountMaterialKind.GoldCube));

        var definition = await definitions.GetAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (definition is null
            || AttachmentRefusal(item, definition.AttachType, sameAccount: false, preservesTransferCount: true)
                != MailItemSendRefusal.None)
            return new(false);

        return definition.AttachType == MailItemAttachType.TradeLimit
            ? new(true, (byte)(item.Attribute - OneTransfer))
            : new(true);
    }

    private static MailItemSendRefusal InstanceRefusal(ItemSnapshot item)
    {
        if (item.ItemId <= 0) return MailItemSendRefusal.InvalidItem;
        if (item.EquipmentLockId != 0) return MailItemSendRefusal.InstanceLocked;
        if (item.TradeRestriction != 0) return MailItemSendRefusal.InstanceTradeRestricted;
        return MailItemSendRefusal.None;
    }

    private static MailItemSendRefusal AttachmentRefusal(ItemSnapshot item, MailItemAttachType attachType,
        bool sameAccount, bool preservesTransferCount) => attachType switch
    {
        // ServerS4A21 MailboxSendPolicy supplies this conservative transfer reading of the PVF tokens.
        MailItemAttachType.Free => MailItemSendRefusal.None,
        MailItemAttachType.Sealing when item.SealFlag != 0 => MailItemSendRefusal.None,
        MailItemAttachType.Sealing => MailItemSendRefusal.Unsealed,
        MailItemAttachType.Account when sameAccount => MailItemSendRefusal.None,
        MailItemAttachType.Account => MailItemSendRefusal.AccountBound,
        MailItemAttachType.TradeLimit when preservesTransferCount && item.IsPile && item.Attribute >= OneTransfer
            => MailItemSendRefusal.None,
        // Mail stores only template and quantity, so accepting a limited stack would reset its count.
        MailItemAttachType.TradeLimit => MailItemSendRefusal.UnsupportedTradeLimit,
        _ => MailItemSendRefusal.TemplateTradeRestricted,
    };
}
