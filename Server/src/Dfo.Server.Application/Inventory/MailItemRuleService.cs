using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class MailItemRuleService(IMailItemDefinitionSource definitions) : IMailItemRuleService
{
    public async ValueTask<MailItemSendDecision> CanSendAsync(
        InventoryItemCoreState core, bool sameAccount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(core);
        if (core.ItemId <= 0)
            return new(MailItemSendRefusal.InvalidItem);
        if (core.EquipmentLockId != 0)
            return new(MailItemSendRefusal.InstanceLocked);
        if (core.TradeRestriction != 0)
            return new(MailItemSendRefusal.InstanceTradeRestricted);

        var definition = await definitions.GetAsync(core.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
            return new(MailItemSendRefusal.UnknownTemplate);

        // The local A21 PVF proves these token values, not the client's mail policy. The
        // reference ServerS4A21 MailboxSendPolicy supplies this conservative transfer reading.
        return definition.AttachType switch
        {
            MailItemAttachType.Free => new(MailItemSendRefusal.None),
            MailItemAttachType.Sealing when core.SealFlag != 0 => new(MailItemSendRefusal.None),
            MailItemAttachType.Sealing => new(MailItemSendRefusal.Unsealed),
            MailItemAttachType.Account when sameAccount => new(MailItemSendRefusal.None),
            MailItemAttachType.Account => new(MailItemSendRefusal.AccountBound),
            // A stack's transfer count is carried on the instance. The current mail attachment
            // stores only template and quantity, so accepting it would reset that count.
            MailItemAttachType.TradeLimit => new(MailItemSendRefusal.UnsupportedTradeLimit),
            _ => new(MailItemSendRefusal.TemplateTradeRestricted),
        };
    }
}
