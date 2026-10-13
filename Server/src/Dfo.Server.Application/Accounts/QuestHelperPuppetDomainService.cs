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

namespace Dfo.Server.Application.Accounts;

internal sealed class QuestHelperPuppetDomainService(
    IPremiumServiceDomainService premiums, IQuestHelperPuppetRuleService rules,
    IUsageQuotaDomainService checks, IDailyResetRuleService days,
    ICharacterTable characters, ICharacterItemTable items,
    IMailboxRepository repository, IMailboxDomainService mailbox, IItemDeliveryDomainService delivery,
    IDomainTransactionAccessor transactions, TimeProvider clock, IEventService events) : IQuestHelperPuppetDomainService
{
    private const string CheckKey = "quest_helper_puppets_checked";

    public async ValueTask<bool> EnsureDailyAsync(AccountId accountId, CharacterId characterId, CancellationToken token)
    {
        if (transactions.Current is null)
            throw new InvalidOperationException("Daily puppet delivery requires a transaction.");
        // Acquire SQLite's write lease before reading eligibility, inventory or the daily marker.
        // Concurrent login/claim work must not turn the same missing puppet into two mails.
        await repository.AcquireWriteAsync(token).ConfigureAwait(false);
        if (!await characters.BelongsToAsync(characterId, accountId, token).ConfigureAwait(false))
            throw new InvalidOperationException("Puppet recipient does not belong to the account.");
        if (!await premiums.For(accountId).IsActiveAsync(PremiumServiceType.QuestHelper, token).ConfigureAwait(false))
            return false;

        var now = clock.GetUtcNow();
        var previous = (await checks.ReadAsync(characterId, UsageQuotaKey.Premium(CheckKey), token).ConfigureAwait(false)).Premium();
        if (previous?.LastConsumedAt is { } recorded && days.IsCurrentPeriod(recorded, now))
            return false;

        var held = await ReadHeldPuppetsAsync(characterId, token).ConfigureAwait(false);
        var missing = rules.PuppetItemIds.Where(id => !held.Contains(id)).ToArray();
        if (missing.Length > 0)
        {
            await delivery.DeliverAsync(accountId, characterId, missing.Select(x => new ItemDeliveryGrant(x, 1)).ToArray(),
                "魔王之契约：任务助手", token, ItemDeliveryMode.MailOnly).ConfigureAwait(false);
        }

        // A fully stocked character also consumes today's check, not today's items.
        await events.DirectAsync(new PremiumUsageRecorded(characterId, [new(CheckKey, previous, 1, now)]), token).ConfigureAwait(false);
        return true;
    }

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
