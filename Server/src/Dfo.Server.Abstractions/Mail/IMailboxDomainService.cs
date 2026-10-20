using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Abstractions.Mail;

public interface IMailboxDomainService
{
    ValueTask<MailSendReceipt> SendAsync(Dfo.Server.Data.Mail.Mail mail, CancellationToken cancellationToken = default);
    /// <summary>System delivery of records created in the caller's active transaction.</summary>
    ValueTask<MailSendReceipt> SendPreparedAsync(Dfo.Server.Data.Mail.Mail mail,
        IReadOnlyList<PreparedMailAttachment> attachments, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<MailView>> InboxAsync(CharacterId recipient, CancellationToken cancellationToken = default);
    ValueTask<MailView> ReadAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken = default);
    ValueTask<MailClaimReceipt> ClaimAsync(CharacterId recipient, long attachmentId, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken = default);
    ValueTask RevokeSystemDeliveryAsync(CharacterId recipient, long mailId, string expectedDeliveryKey, CancellationToken cancellationToken = default);
}

public interface IMailboxQueryDomainService
{
    ValueTask<MailRecipient?> FindRecipientAsync(byte[] name, CancellationToken cancellationToken = default);
    ValueTask<Dfo.Server.Abstractions.Characters.CharacterMailboxStorageRecord> ReadStorageAsync(CharacterId recipient, CancellationToken cancellationToken = default);
    ValueTask<MailPage> PageAsync(CharacterId recipient, int pageSize, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<MailView>> InboxAsync(CharacterId recipient, CancellationToken cancellationToken = default);
}
