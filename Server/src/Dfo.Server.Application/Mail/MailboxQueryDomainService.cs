using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Mail;

internal sealed class MailboxQueryDomainService(IMailboxRepository repository, TimeProvider time, IMailboxMessageTable mailboxMessages, IMailboxAttachmentTable mailboxAttachments) : IMailboxQueryDomainService
{
    public ValueTask<MailRecipient?> FindRecipientAsync(byte[] name, CancellationToken cancellationToken = default) => repository.FindRecipientAsync(name, cancellationToken);

    public async ValueTask<CharacterMailboxStorageRecord> ReadStorageAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var inbox = await PageAsync(characterId, 20, cancellationToken);
        var page = inbox.Messages;
        var headers = (await mailboxMessages.ReadAsync(page.Select(x => x.Id).ToArray(), cancellationToken))
            .ToDictionary(x => x.MessageId);
        var now = time.GetUtcNow();
        var rows = new List<CharacterMailboxMessageStorageRow>(page.Count);
        foreach (var message in page)
        {
            var pending = message.Attachments.Where(x => !x.Claimed).ToArray();
            var pendingIds = pending.Select(x => x.Id).ToHashSet();
            var attachments = (await mailboxAttachments.ReadAsync(message.Id, cancellationToken))
                .Where(x => pendingIds.Contains(x.AttachmentId)).ToArray();
            var metadata = headers[message.Id];
            rows.Add(new CharacterMailboxMessageStorageRow(
                message.Id, message.SenderCharacterId, message.SenderName,
                metadata.MailType, metadata.SourceProtocol, message.Title, message.Body,
                checked((int)pending.Sum(x => x.Asset is MailAsset.Gold gold ? (long)gold.Amount : 0)),
                message.ExpiresAt is { } end ? (int)Math.Clamp((end - now).TotalSeconds, 0, int.MaxValue) : 0,
                checked((int)message.CreatedAt.ToUnixTimeSeconds()), message.Read ? 2 : 1, attachments)
            { GoldAttachments = pending.Where(x => x.Asset is MailAsset.Gold).Select(x => new MailGoldAttachmentState(x.Id, ((MailAsset.Gold)x.Asset).Amount)).ToArray() });
        }
        return new CharacterMailboxStorageRecord(inbox.TotalCount, rows);
    }

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
