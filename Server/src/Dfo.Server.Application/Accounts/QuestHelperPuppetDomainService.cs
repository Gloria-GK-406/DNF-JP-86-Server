using System.Globalization;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Dfo.Server.Application.Transactions;

namespace Dfo.Server.Application.Accounts;

internal sealed class QuestHelperPuppetDomainService(
    IPremiumServiceDomainService premiums, IQuestHelperPuppetRuleService rules,
    IUsageQuotaDomainService checks, IDailyResetRuleService days,
    ICharacterTable characters, ICharacterItemTable items,
    IMailboxRepository repository, IMailboxDomainService mailbox, IItemDeliveryDomainService delivery,
    IDomainTransactionAccessor transactions, TimeProvider clock, IEventService events,
    IItemDomainStore itemStore) : IQuestHelperPuppetDomainService
{
    private const string CheckKey = "quest_helper_puppets_checked";
    private const string Source = "魔王之契约：任务助手";
    private const string BatchPrefix = "quest-helper:";

    public async ValueTask<bool> EnsureDailyAsync(AccountId accountId, CharacterId characterId, CancellationToken token) =>
        (await ReconcileAsync(accountId, characterId, token).ConfigureAwait(false)).Checked;

    public async ValueTask<PuppetDailyReconciliation> ReconcileAsync(AccountId accountId, CharacterId characterId, CancellationToken token)
    {
        var transaction = transactions.Current ?? throw new InvalidOperationException("Daily puppet delivery requires a transaction.");
        try { return await ReconcileCoreAsync(accountId, characterId, token).ConfigureAwait(false); }
        catch
        {
            if (transaction is not DomainTransaction { IsSettled: true }) transaction.RequireRollback();
            throw;
        }
    }

    private async ValueTask<PuppetDailyReconciliation> ReconcileCoreAsync(AccountId accountId, CharacterId characterId, CancellationToken token)
    {
        var transaction = transactions.Current!;
        await repository.AcquireWriteAsync(token).ConfigureAwait(false);
        if (!await characters.BelongsToAsync(characterId, accountId, token).ConfigureAwait(false))
            throw new InvalidOperationException("Puppet recipient does not belong to the account.");
        var now = clock.GetUtcNow();
        var active = await premiums.For(accountId).IsActiveAsync(PremiumServiceType.QuestHelper, token).ConfigureAwait(false);
        var previous = (await checks.ReadAsync(characterId, UsageQuotaKey.Premium(CheckKey), token).ConfigureAwait(false)).Premium();
        if (!active && previous is null) return new(false, [], false);
        var period = days.PeriodStart(now);
        var end = period.AddDays(1);
        var ids = rules.PuppetItemIds.ToHashSet();
        var stale = new HashSet<int>();
        var changed = new HashSet<InventoryContainerType>();
        var replacement = new List<ItemDeliveryGrant>();
        var mailChanged = false;
        foreach (var mail in await repository.ReadInboxAsync(characterId, token).ConfigureAwait(false))
        {
            if (mail.SenderCharacterId != 0) continue;
            var tagged = mail.DeliveryKey.StartsWith(BatchPrefix, StringComparison.Ordinal);
            // Only the exact old system delivery is eligible for conversion; ordinary/mixed mail stays intact.
            var legacy = mail.DeliveryKey.Length == 0 && mail.Title == Source && mail.Attachments.Length > 0
                && mail.Attachments.All(a => a.Asset is MailAsset.Stackable stack && ids.Contains(stack.ItemId));
            if (!tagged && !legacy) continue;
            var mailPeriod = legacy ? days.PeriodStart(mail.CreatedAt) : ParsePeriod(mail.DeliveryKey);
            if (!legacy && mailPeriod >= period && mail.ExpiresAt > now) continue;
            await mailbox.RevokeSystemDeliveryAsync(characterId, mail.Id, mail.DeliveryKey, token).ConfigureAwait(false);
            mailChanged = true;
            foreach (var attachment in mail.Attachments.Where(a => !a.Claimed))
            {
                if (attachment.Asset is not MailAsset.Stackable stack || !ids.Contains(stack.ItemId))
                    throw new InvalidDataException("Puppet delivery contains an unrelated attachment.");
                if (legacy && mailPeriod == period) replacement.Add(new(stack.ItemId, stack.Count));
                else stale.Add(stack.ItemId);
            }
        }

        var session = itemStore.SessionIn(transaction);
        foreach (var row in await items.ReadByCharacterAsync(characterId, token).ConfigureAwait(false))
        {
            if (!ids.Contains(row.Core.ItemId) || row.ListType is not ((int)InventoryContainerType.Main
                or (int)InventoryContainerType.QuickSlot or (int)InventoryContainerType.PersonalCargo)) continue;
            var container = ContainerId.ForCharacter(characterId, (InventoryContainerType)row.ListType);
            var item = await session.GetAsync(container, row.SlotIndex, token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Puppet disappeared during reconciliation.");
            var deadline = CorrectLegacyDeadline(item.Snapshot.ExpireTime, previous?.LastConsumedAt ?? now);
            if (deadline <= now)
            {
                await session.DeleteItemAsync(await session.TakeAsync(item, null, token).ConfigureAwait(false), token).ConfigureAwait(false);
                stale.Add(item.ItemId);
            }
            else if (item.Snapshot.ExpireTime != deadline.ToUnixTimeSeconds())
                await session.UpdateAsync(item, snapshot => snapshot with { ExpireTime = checked((int)deadline.ToUnixTimeSeconds()) }, token).ConfigureAwait(false);
            else continue;
            changed.Add((InventoryContainerType)row.ListType);
        }

        // All old custody is gone before any replacement is created; the caller commits them together.
        var options = new ItemDeliveryOptions(end, BatchPrefix + period.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        if (replacement.Count > 0)
        {
            await delivery.DeliverAsync(accountId, characterId, replacement, Source, token, ItemDeliveryMode.MailOnly, options).ConfigureAwait(false);
            mailChanged = true;
        }
        var checkedToday = previous?.LastConsumedAt is { } recorded && days.IsCurrentPeriod(recorded, now);
        if (active)
        {
            var held = await ReadHeldPuppetsAsync(characterId, token).ConfigureAwait(false);
            // A legacy check may have counted yesterday's still-live 24h stock. Replace only that removed stock,
            // never replenish a puppet already consumed in the current period.
            var missing = ids.Where(id => !held.Contains(id) && (!checkedToday || stale.Contains(id))).ToArray();
            if (missing.Length > 0)
            {
                await delivery.DeliverAsync(accountId, characterId, missing.Select(x => new ItemDeliveryGrant(x, 1)).ToArray(),
                    Source, token, ItemDeliveryMode.MailOnly, options).ConfigureAwait(false);
                mailChanged = true;
            }
            if (!checkedToday)
                await events.DirectAsync(new PremiumUsageRecorded(characterId, [new(CheckKey, previous, 1, now)]), token).ConfigureAwait(false);
        }
        return new(active && !checkedToday, changed.Order().ToArray(), mailChanged);
    }

    private DateTimeOffset CorrectLegacyDeadline(int expiry, DateTimeOffset fallback)
    {
        if (ItemLifetime.At(expiry).IsPermanent) return days.PeriodStart(fallback).AddDays(1);
        var date = DateTimeOffset.FromUnixTimeSeconds(expiry);
        if (days.PeriodStart(date) == date) return date;
        // The former [usable period] 1 path stored creation + 86400 seconds.
        return days.PeriodStart(date.AddDays(-1)).AddDays(1);
    }

    private static DateTimeOffset ParsePeriod(string key) =>
        long.TryParse(key.AsSpan(BatchPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? DateTimeOffset.FromUnixTimeSeconds(value) : throw new InvalidDataException("Invalid puppet delivery period.");

    private async ValueTask<HashSet<int>> ReadHeldPuppetsAsync(CharacterId characterId, CancellationToken token)
    {
        // Personal cargo already shares persisted character slots. Account cargo is deliberately
        // excluded: stock and the daily check belong to each character independently.
        var held = (await items.ReadByCharacterAsync(characterId, token).ConfigureAwait(false))
            .Where(row => row.ListType is (int)InventoryContainerType.Main
                or (int)InventoryContainerType.QuickSlot or (int)InventoryContainerType.PersonalCargo)
            .Where(row => row.Core.Value > 0)
            .Select(row => row.Core.ItemId).ToHashSet();
        foreach (var mail in await mailbox.InboxAsync(characterId, token).ConfigureAwait(false))
            foreach (var attachment in mail.Attachments)
                if (!attachment.Claimed && attachment.Asset is MailAsset.Stackable { Count: > 0 } stack)
                    held.Add(stack.ItemId);
        return held;
    }
}
