using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>Deliver every due refill once per day, preserving overflow in mail.</summary>
internal sealed class DailyRefillDomainService(
    ICharacterTable characters,
    ICharacterDailyCounterTable counters,
    IDailyRefillRuleService refillRules,
    ICharacterBusinessInventoryDomainService inventories,
    IItemDeliveryDomainService delivery) : IDailyRefillDomainService
{
    /// <summary>What a refill counter is called, with the item appended.</summary>
    private const string CounterPrefix = "pvf_daily_refill_item_";

    public async ValueTask<DailyRefillMutationResult> ApplyDailyRefillAsync(
        AccountId accountId,
        CharacterId characterId,
        IReadOnlyList<DailyRefillRule> rules,
        DateTimeOffset preparedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!await characters.BelongsToAsync(characterId, accountId, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                "Daily refill character ownership does not match the account.");
        }

        var period = refillRules.PeriodOf(preparedAt);
        const string attemptKey = "daily_refill_attempt";
        if (!refillRules.IsDue(await counters.FindClaimedPeriodAsync(characterId, attemptKey, cancellationToken), period))
            return new DailyRefillMutationResult([]);
        var committed = new List<DailyRefillCommittedGrant>();
        foreach (var rule in rules)
        {
            var key = CounterPrefix + rule.ItemId;
            var claimed = await counters.FindClaimedPeriodAsync(characterId, key, cancellationToken)
                .ConfigureAwait(false);
            if (!refillRules.IsDue(claimed, period))
            {
                continue;
            }

            var bag = inventories.GetStore(characterId, BusinessInventoryOwnership.StoreOf(rule.Destination.Space))
                .GetContainer(rule.Destination.Space, isDefault: true);
            var held = (await bag.GetAllAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => item.ItemId == rule.ItemId).ToArray();
            var pending = await delivery.PendingQuantityAsync(characterId, rule.ItemId, cancellationToken).ConfigureAwait(false);
            var grant = refillRules.GrantFor(rule, (int)Math.Min(int.MaxValue, held.Sum(item => (long)Math.Max(0, item.Count)) + pending));
            if (grant > 0)
            {
                if (rule.ItemKind == 0 && held.Length == 0) throw new InvalidDataException("Daily refill cannot open an unknown item kind.");
                var receipt = await delivery.DeliverAsync(accountId, characterId,
                    [new(rule.ItemId, grant) { PlacementConstraint = new(rule.Destination, rule.StackLimit) }],
                    "每日物品补充", cancellationToken, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
                committed.AddRange(receipt.Inventory.Select(x => new DailyRefillCommittedGrant(x.Slot, rule.ItemId, x.Count, x.Snapshot.Count)));
            }

            if (await counters.ClaimAsync(characterId, key, period, cancellationToken)
                    .ConfigureAwait(false) != 1)
            {
                throw new InvalidDataException("Daily refill claim was not persisted.");
            }
        }

        if (await counters.ClaimAsync(characterId, attemptKey, period, cancellationToken) != 1)
            throw new InvalidDataException("Daily refill attempt was not persisted.");
        return new DailyRefillMutationResult(
            [.. committed.GroupBy(value => value.Slot).Select(g => new DailyRefillCommittedGrant(g.Key, g.Last().ItemId, g.Sum(v => v.GrantedCount), g.Last().CommittedCount))
                .OrderBy(value => value.Slot.Space)
                .ThenBy(value => value.Slot.Index)]);
    }


}
