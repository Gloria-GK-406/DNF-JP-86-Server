using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
namespace Dfo.Server.Application.Mail;
internal sealed class MailboxSendDomainService(IMailboxDomainService mailbox, IMailboxQueryDomainService query,
    IItemDomainStore items, IDomainTransactionAccessor transactions, IItemPlacementRuleService placements,
    ICharacterContainerDomainService containers, TimeProvider clock) : IMailboxSendDomainService
{
    public async ValueTask<PlayerMailReceipt> SendAsync(CharacterId sender, PlayerMailRequest request, CancellationToken token)
    {
        if (request.Gold < 0 || request.Attachments.Count > 10 || request.Attachments.Select(x => (x.Container, x.Slot)).Distinct().Count() != request.Attachments.Count)
            throw new MailException(MailFailure.InvalidMail, "Invalid mail attachment selection.");
        var recipient = await query.FindRecipientAsync(request.RecipientName, token)
            ?? throw new MailException(MailFailure.CharacterNotFound, "Recipient not found.");
        if (recipient.CharacterId == sender) throw new MailException(MailFailure.InvalidMail, "Cannot send mail to self.");
        var session = items.SessionIn(transactions.Current ?? throw new InvalidOperationException("Sending mail requires a command transaction."));
        var builder = new MailBuilder(MailSource.FromCharacter(sender), recipient.CharacterId)
            .WithText(string.Empty, request.Body).ExpiresAt(clock.GetUtcNow().AddDays(30));
        foreach (var selection in request.Attachments)
        {
            if (selection.Container is not (InventoryContainerType.Main or InventoryContainerType.Pet) || selection.Slot < 0
                || selection.Container == InventoryContainerType.Main && selection.Slot < 3 || selection.Count <= 0 || selection.ItemId <= 0)
                throw new MailException(MailFailure.InvalidItem, "Only carried inventory attachments are supported.");
            var container = ContainerId.ForCharacter(sender, selection.Container);
            if (containers.GetContainer(sender, selection.Container).Id != container)
                throw new MailException(MailFailure.NotOwned, "Mail cannot take transient inventory.");
            var item = await session.ReadAsync(container, selection.Slot, token);
            if (item is null || item.Core.ItemId != selection.ItemId)
                throw new MailException(MailFailure.NotOwned, "The selected slot has changed.");
            var kind = await placements.ClassifyAsync(selection.ItemId, token);
            if (kind == ItemWorldClass.Interchangeable)
            {
                if (selection.Count > item.Core.Value) throw new MailException(MailFailure.InsufficientAssets, "Selected stack is too small.");
                builder.AddStackable(selection.ItemId, selection.Count, new GameSlot(selection.Container, selection.Slot));
            }
            else if (kind == ItemWorldClass.Equipment && selection.Count == 1)
                builder.AddEquipment(item.Identity ?? throw new MailException(MailFailure.InvalidItem, "Equipment identity is missing."));
            else throw new MailException(MailFailure.InvalidItem, "Unsupported attachment kind or quantity.");
        }
        if (request.Gold > 0) builder.AddGold(request.Gold);
        return new(await mailbox.SendAsync(builder.Build(), token), recipient.CharacterId);
    }
}
