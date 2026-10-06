using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
namespace Dfo.Server.Application.Mail;
internal sealed class MailboxSendDomainService(IMailboxDomainService mailbox, IMailboxQueryDomainService query,
    IItemDomainStore items, IDomainTransactionAccessor transactions, IItemPlacementRuleService placements, IMailboxRepository repository,
    ICharacterBusinessInventoryDomainService inventories,
    TimeProvider clock) : IMailboxSendDomainService
{
    public async ValueTask<PlayerMailReceipt> SendAsync(CharacterId sender, PlayerMailRequest request, CancellationToken token)
    {
        if (request.Gold < 0 || request.Attachments.Count > 10 || request.Attachments.Select(x => x.Slot).Distinct().Count() != request.Attachments.Count)
            throw new MailException(MailFailure.InvalidMail, "Invalid mail attachment selection.");
        await repository.AcquireWriteAsync(token).ConfigureAwait(false);
        var recipient = await query.FindRecipientAsync(request.RecipientName, token)
            ?? throw new MailException(MailFailure.CharacterNotFound, "Recipient not found.");
        if (recipient.CharacterId == sender) throw new MailException(MailFailure.InvalidMail, "Cannot send mail to self.");
        var session = items.SessionIn(transactions.Current ?? throw new InvalidOperationException("Sending mail requires a command transaction."));
        var builder = new MailBuilder(MailSource.FromCharacter(sender), recipient.CharacterId)
            .WithText(string.Empty, request.Body).ExpiresAt(clock.GetUtcNow().AddDays(30));
        foreach (var selection in request.Attachments)
        {
            if (!CanCarry(selection.Slot.Space) || selection.Slot.Index < 0
                || selection.Count <= 0 || selection.ItemId <= 0)
                throw new MailException(MailFailure.InvalidItem, "Only carried inventory attachments are supported.");
            var address = selection.Slot;
            var business = inventories.GetStore(sender, BusinessInventoryOwnership.StoreOf(address.Space))
                .FindContainer(address.Space);
            var item = business is null ? null : await business.GetAsync(address.Index, token).ConfigureAwait(false);
            if (item is null || item.ItemId != selection.ItemId)
                throw new MailException(MailFailure.NotOwned, "The selected slot has changed.");
            var physical = LegacyInventoryStorageCoordinates.ToStorage(address);
            if (!business!.StorageId.TryGetCharacterOwner(out var owner, out var list)
                || owner != sender || list != physical.List)
                throw new MailException(MailFailure.NotOwned,
                    "The selected item is not in the sender's persistent container.");
            var stored = await session.GetAsync(business!.StorageId, physical.Index, token)
                .ConfigureAwait(false);
            if (stored is null || stored.ItemId != selection.ItemId)
                throw new MailException(MailFailure.NotOwned, "The selected slot has changed.");
            var kind = await placements.ClassifyAsync(selection.ItemId, token);
            if (kind == ItemWorldClass.Interchangeable)
            {
                if (selection.Count > item.Count) throw new MailException(MailFailure.InsufficientAssets, "Selected stack is too small.");
                builder.AddStackable(selection.ItemId, selection.Count, address);
            }
            else if (kind == ItemWorldClass.Equipment && selection.Count == 1)
                builder.AddEquipment(stored.ItemUid ?? throw new MailException(MailFailure.InvalidItem, "Equipment identity is missing."));
            else throw new MailException(MailFailure.InvalidItem, "Unsupported attachment kind or quantity.");
        }
        if (request.Gold > 0) builder.AddGold(request.Gold);
        return new(await mailbox.SendAsync(builder.Build(), token), recipient.CharacterId);
    }

    private static bool CanCarry(BusinessInventorySpace space) => space.IsCarriedItemSpace()
        || space is BusinessInventorySpace.Avatar or BusinessInventorySpace.CreatureInventory
            or BusinessInventorySpace.CreatureEquipment or BusinessInventorySpace.CreatureConsumable;
}
