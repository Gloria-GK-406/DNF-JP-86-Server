using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Abstractions.Mail;

public sealed record PreparedMailAttachment(MailAsset Asset, InventoryItemCoreState? Core = null);

public interface IMailboxRepository
{
    // Must run before reading mutable facts in a mailbox write use case.
    ValueTask AcquireWriteAsync(CancellationToken cancellationToken);
    ValueTask<long> InsertAsync(Dfo.Server.Data.Mail.Mail mail, string senderName, DateTimeOffset now,
        IReadOnlyList<PreparedMailAttachment> attachments, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<MailHeader>> ReadInboxHeadersAsync(CharacterId recipient, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<MailView>> ReadInboxAsync(CharacterId recipient, CancellationToken cancellationToken);
    ValueTask<MailView?> FindAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken);
    ValueTask<MailView?> FindByAttachmentAsync(CharacterId recipient, long attachmentId, CancellationToken cancellationToken);
    ValueTask<bool> HoldsEquipmentAsync(ItemIdentity identity, CancellationToken cancellationToken);
    ValueTask MarkReadAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken);
    ValueTask<bool> MarkClaimedAsync(long attachmentId, CancellationToken cancellationToken);
    ValueTask MarkDeletedAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken);
}
