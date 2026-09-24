using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Abstractions.Mail;

public interface IMailboxDomainService
{
    ValueTask<MailSendReceipt> SendAsync(Dfo.Server.Data.Mail.Mail mail, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<MailView>> InboxAsync(CharacterId recipient, CancellationToken cancellationToken = default);
    ValueTask<MailView> ReadAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken = default);
    ValueTask<MailClaimReceipt> ClaimAsync(CharacterId recipient, long attachmentId, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken = default);
}

public interface IMailboxQueryDomainService
{
    ValueTask<MailPage> PageAsync(CharacterId recipient, int pageSize, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<MailView>> InboxAsync(CharacterId recipient, CancellationToken cancellationToken = default);
}
