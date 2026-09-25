using Dfo.Server.Abstractions.Persistence;
using System.Collections.Immutable;
using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Mail;

internal sealed class MailboxDomainService(IMailboxRepository repository, ICharacterDomainService characters,
    IEquipmentTransferDomainService equipment, IMailItemRuleService sendRules, ICharacterTable owners,
    MailStackTransfers stacks, DomainTransactionScope transactions,
    TimeProvider time) : IMailboxDomainService
{
    public ValueTask<MailSendReceipt> SendAsync(Dfo.Server.Data.Mail.Mail mail, CancellationToken cancellationToken = default) =>
        RunAsync(true, async () =>
        {
            ArgumentNullException.ThrowIfNull(mail);
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
            foreach (var asset in mail.Attachments)
            {
                switch (asset)
                {
                    case MailAsset.Equipment equip:
                        if (await repository.HoldsEquipmentAsync(equip.Identity, cancellationToken)) throw Error(MailFailure.NotOwned);
                        if (mail.Source.Character is { } owner)
                        {
                            var candidate = await equipment.ReadOwnedAsync(owner, equip.Identity, cancellationToken) ?? throw Error(MailFailure.NotOwned);
                            if (!(await sendRules.CanSendAsync(candidate.Core, sameAccount, cancellationToken)).Allowed)
                                throw Error(MailFailure.NotSendable);
                            var taken = await equipment.TakeAsync(owner, equip.Identity, cancellationToken);
                            prepared.Add(new(asset, taken.Core));
                            changes.Add(new(owner, taken.ContainerType, taken.SlotIndex, taken.Core.ItemId, -1, null));
                        }
                        else
                        {
                            var core = await equipment.ReadDetachedAsync(equip.Identity, cancellationToken) ?? throw Error(MailFailure.NotOwned);
                            prepared.Add(new(asset, core));
                        }
                        break;
                    case MailAsset.Stackable stack:
                        var stackPlacement = await stacks.ValidateAsync(stack.ItemId, cancellationToken);
                        if (mail.Source.Character is { } stackOwner)
                            changes.AddRange(await stacks.TakeAsync(stackOwner, stack.ItemId, stack.Count, sameAccount, cancellationToken, stack.SourceSlot));
                        prepared.Add(new(asset, Dfo.Server.Application.Inventory.StackableItemCore.Create(stackPlacement.ItemKind, stack.ItemId, stack.Count)));
                        break;
                    case MailAsset.Gold gold:
                        if (mail.Source.Character is { } goldOwner)
                        {
                            var balance = await characters.Get(goldOwner).TrySpendGoldAsync(gold.Amount, cancellationToken) ?? throw Error(MailFailure.InsufficientAssets);
                            changes.Add(new(goldOwner, null, null, 0, -gold.Amount, balance));
                        }
                        prepared.Add(new(asset));
                        break;
                }
            }
            var id = await repository.InsertAsync(mail, senderName, now, prepared, cancellationToken);
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
            if (!await repository.MarkClaimedAsync(attachmentId, cancellationToken)) throw Error(MailFailure.ConcurrentChange);
            var changes = ImmutableArray.CreateBuilder<MailAssetChange>();
            switch (attachment.Asset)
            {
                case MailAsset.Equipment equip:
                    var core = await equipment.ReadDetachedAsync(equip.Identity, cancellationToken) ?? throw Error(MailFailure.NotOwned);
                    var slot = await equipment.GrantAsync(recipient, equip.Identity, cancellationToken);
                    changes.Add(new(recipient, slot.Space, slot.Index, core.ItemId, 1, null));
                    break;
                case MailAsset.Stackable stack:
                    changes.AddRange(await stacks.GrantAsync(recipient, stack.ItemId, stack.Count, cancellationToken));
                    break;
                case MailAsset.Gold gold:
                    var character = characters.Get(recipient);
                    var before = await character.GoldAsync(cancellationToken);
                    var after = await character.AddGoldAsync(gold.Amount, cancellationToken);
                    if ((long)after - before != gold.Amount) throw Error(MailFailure.GoldLimit);
                    changes.Add(new(recipient, null, null, 0, gold.Amount, after));
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
        var lease = transactions.Current is null ? transactions.Begin() : null;
        try
        {
            if (writing) await repository.AcquireWriteAsync(ct);
            var result = await action();
            if (lease is not null) await lease.CommitAsync(ct);
            return result;
        }
        catch (EquipmentTransferException exception)
        {
            await AbortAsync();
            throw Error(exception.Reason switch
            {
                EquipmentTransferFailure.NoRoom => MailFailure.NoCapacity,
                EquipmentTransferFailure.NotSendable => MailFailure.NotSendable,
                _ => MailFailure.NotOwned,
            });
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
