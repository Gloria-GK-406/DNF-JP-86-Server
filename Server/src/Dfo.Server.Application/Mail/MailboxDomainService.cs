using Dfo.Server.Abstractions.Persistence;
using System.Collections.Immutable;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Mail;

internal sealed class MailboxDomainService(IMailboxRepository repository, ICharacterDomainService characters,
    ICharacterTable owners, MailItemTransfers items, DomainTransactionScope transactions,
    TimeProvider time, IEventService events) : IMailboxDomainService
{
    public ValueTask<MailSendReceipt> SendAsync(Dfo.Server.Data.Mail.Mail mail, CancellationToken cancellationToken = default) =>
        SendCoreAsync(mail, null, cancellationToken);

    public ValueTask<MailSendReceipt> SendPreparedAsync(Dfo.Server.Data.Mail.Mail mail,
        IReadOnlyList<PreparedMailAttachment> attachments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        return SendCoreAsync(mail, attachments, cancellationToken);
    }

    private ValueTask<MailSendReceipt> SendCoreAsync(Dfo.Server.Data.Mail.Mail mail,
        IReadOnlyList<PreparedMailAttachment>? supplied, CancellationToken cancellationToken) =>
        RunAsync(true, async () =>
        {
            ArgumentNullException.ThrowIfNull(mail);
            if (supplied is not null && (mail.Source.Character is not null || supplied.Count != mail.Attachments.Length
                || mail.Attachments.Where((asset, index) => asset != supplied[index].Asset).Any()))
                throw Error(MailFailure.InvalidMail);
            var now = time.GetUtcNow();
            if (mail.ExpiresAt <= now) throw Error(MailFailure.Expired);
            _ = await CharacterAsync(mail.Recipient, cancellationToken);
            var senderName = string.Empty;
            var sameAccount = false;
            if (mail.Source.Character is { } sender)
            {
                var identity = await CharacterAsync(sender, cancellationToken);
                senderName = Encoding.UTF8.GetString(identity.Name);
                sameAccount = await owners.FindOwnerAsync(sender, cancellationToken) == await owners.FindOwnerAsync(mail.Recipient, cancellationToken);
            }
            var prepared = new List<PreparedMailAttachment>();
            var changes = ImmutableArray.CreateBuilder<MailAssetChange>();
            for (var index = 0; index < mail.Attachments.Length; index++)
            {
                var asset = mail.Attachments[index];
                switch (asset)
                {
                    case MailAsset.Equipment equip:
                        if (await repository.HoldsEquipmentAsync(equip.Identity, cancellationToken)) throw Error(MailFailure.NotOwned);
                        if (mail.Source.Character is { } owner)
                        {
                            var taken = await items.TakeEquipmentAsync(owner, equip.Identity, sameAccount, cancellationToken);
                            prepared.Add(new(asset, taken.Items)); changes.AddRange(taken.Changes);
                        }
                        else
                        {
                            // A UID naming an unpositioned row is not proof of ownership.
                            if (supplied is null) throw Error(MailFailure.NotOwned);
                            prepared.Add(supplied[index]);
                        }
                        break;
                    case MailAsset.Stackable stack:
                        if (supplied is not null) prepared.Add(supplied[index]);
                        else if (mail.Source.Character is { } stackOwner)
                        {
                            var taken = await items.TakeStackAsync(stackOwner, stack.ItemId, stack.Count,
                                sameAccount, cancellationToken, stack.SourceSlot);
                            prepared.Add(new(asset, taken.Items)); changes.AddRange(taken.Changes);
                        }
                        else prepared.Add(new(asset, [await items.CreateStackAsync(stack.ItemId, stack.Count, cancellationToken)]));
                        break;
                    case MailAsset.Gold gold:
                        if (supplied is not null && supplied[index].Items.Count != 0) throw Error(MailFailure.InvalidMail);
                        if (mail.Source.Character is { } goldOwner)
                        {
                            var balance = await characters.Get(goldOwner).TrySpendGoldAsync(gold.Amount, cancellationToken) ?? throw Error(MailFailure.InsufficientAssets);
                            changes.Add(new(goldOwner, null, 0, -gold.Amount, balance));
                        }
                        prepared.Add(new(asset));
                        break;
                }
            }
            var id = await repository.InsertAsync(mail, senderName, now, prepared, cancellationToken);
            await events.EnqueueAsync(new MailCreated(id, mail.Recipient), cancellationToken);
            return new MailSendReceipt(id, changes.ToImmutable());
        }, cancellationToken);

    public ValueTask<IReadOnlyList<MailView>> InboxAsync(CharacterId recipient, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<MailView>>(false, async () =>
        {
            _ = await CharacterAsync(recipient, cancellationToken);
            var now = time.GetUtcNow();
            return (await repository.ReadInboxAsync(recipient, cancellationToken)).Where(x => IsLive(x, now)).ToArray();
        }, cancellationToken);

    public ValueTask<MailView> ReadAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken = default) =>
        RunAsync(true, async () =>
        {
            var mail = RequireLive(await repository.FindAsync(recipient, mailId, cancellationToken));
            await repository.MarkReadAsync(recipient, mailId, cancellationToken);
            return mail with { Read = true };
        }, cancellationToken);

    public ValueTask<MailClaimReceipt> ClaimAsync(CharacterId recipient, long attachmentId, CancellationToken cancellationToken = default) =>
        RunAsync(true, async () =>
        {
            _ = await CharacterAsync(recipient, cancellationToken);
            var mail = RequireLive(await repository.FindByAttachmentAsync(recipient, attachmentId, cancellationToken));
            var attachment = mail.Attachments.Single(x => x.Id == attachmentId);
            if (attachment.Claimed) throw Error(MailFailure.AlreadyClaimed);
            if (attachment.LegacyUnsupported) throw Error(MailFailure.LegacyAttachment);
            var changes = ImmutableArray.CreateBuilder<MailAssetChange>();
            switch (attachment.Asset)
            {
                case MailAsset.Equipment:
                case MailAsset.Stackable:
                    var taken = await repository.TakeItemAsync(recipient, attachmentId, time.GetUtcNow(), cancellationToken);
                    changes.AddRange(await items.GrantAsync(recipient, taken, cancellationToken));
                    break;
                case MailAsset.Gold gold:
                    if (!await repository.MarkClaimedAsync(attachmentId, cancellationToken)) throw Error(MailFailure.ConcurrentChange);
                    var character = characters.Get(recipient);
                    var before = await character.GoldAsync(cancellationToken);
                    var after = await character.AddGoldAsync(gold.Amount, cancellationToken);
                    if ((long)after - before != gold.Amount) throw Error(MailFailure.GoldLimit);
                    changes.Add(new(recipient, null, 0, gold.Amount, after));
                    break;
            }
            return new MailClaimReceipt(mail.Id, attachmentId, changes.ToImmutable());
        }, cancellationToken);

    public async ValueTask DeleteAsync(CharacterId recipient, long mailId, CancellationToken cancellationToken = default) =>
        _ = await RunAsync(true, async () =>
        {
            var mail = await repository.FindAsync(recipient, mailId, cancellationToken);
            if (mail is null) return true;
            if (IsLive(mail, time.GetUtcNow()) && mail.Attachments.Any(x => !x.Claimed)) throw Error(MailFailure.UnclaimedAttachments);
            await repository.MarkDeletedAsync(recipient, mailId, cancellationToken);
            return true;
        }, cancellationToken);

    private async ValueTask<CharacterIdentityState> CharacterAsync(CharacterId id, CancellationToken ct)
    {
        if (await owners.FindOwnerAsync(id, ct) is null) throw Error(MailFailure.CharacterNotFound);
        return await characters.Get(id).IdentityAsync(ct) ?? throw Error(MailFailure.CharacterNotFound);
    }
    private static bool IsLive(MailView mail, DateTimeOffset now) => mail.ExpiresAt is null || mail.ExpiresAt > now;
    private MailView RequireLive(MailView? mail)
    {
        if (mail is null) throw Error(MailFailure.NotFound);
        if (!IsLive(mail, time.GetUtcNow())) throw Error(MailFailure.Expired);
        return mail;
    }
    private static MailException Error(MailFailure reason) => new(reason, $"Mail operation refused: {reason}.");

    private async ValueTask<T> RunAsync<T>(bool writing, Func<ValueTask<T>> action, CancellationToken ct)
    {
        var lease = transactions.Current is null ? transactions.Begin(readOnly: !writing) : null;
        try
        {
            if (writing) await repository.AcquireWriteAsync(ct);
            var result = await action();
            if (lease is not null) await lease.CommitAsync(ct);
            return result;
        }
        catch
        {
            await AbortAsync();
            throw;
        }
        finally { if (lease is not null) await lease.DisposeAsync(); }

        // A caller cannot catch a mail refusal and accidentally commit partial assets.
        ValueTask AbortAsync() => transactions.Current is DomainTransaction active
            ? active.RollbackAsync() : ValueTask.CompletedTask;
    }
}
