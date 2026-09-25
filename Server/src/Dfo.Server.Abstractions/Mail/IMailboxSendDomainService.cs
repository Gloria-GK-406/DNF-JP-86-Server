using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
namespace Dfo.Server.Abstractions.Mail;
public sealed record MailSendSelection(InventoryContainerType Container, int Slot, int ItemId, int Count);
public sealed record PlayerMailRequest(byte[] RecipientName, int Gold, string Body, IReadOnlyList<MailSendSelection> Attachments);
public sealed record PlayerMailReceipt(MailSendReceipt Mail, CharacterId Recipient);
public interface IMailboxSendDomainService
{
    ValueTask<PlayerMailReceipt> SendAsync(CharacterId sender, PlayerMailRequest request, CancellationToken token);
}
