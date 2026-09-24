using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Mail;

internal sealed class MailboxQueryDomainService(IMailboxRepository repository, TimeProvider time) : IMailboxQueryDomainService
{
    public async ValueTask<MailPage> PageAsync(CharacterId recipient, int pageSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var now = time.GetUtcNow();
        var live = (await repository.ReadInboxHeadersAsync(recipient, cancellationToken))
            .Where(x => x.ExpiresAt is null || x.ExpiresAt > now).ToArray();
        var chosen = live.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(pageSize)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id);
        var messages = new List<MailView>();
        foreach (var header in chosen)
            if (await repository.FindAsync(recipient, header.Id, cancellationToken) is { } mail)
                messages.Add(mail);
        return new MailPage(live.Length, messages);
    }

    public async ValueTask<IReadOnlyList<MailView>> InboxAsync(CharacterId recipient, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        return (await repository.ReadInboxAsync(recipient, cancellationToken))
            .Where(x => x.ExpiresAt is null || x.ExpiresAt > now).ToArray();
    }
}
