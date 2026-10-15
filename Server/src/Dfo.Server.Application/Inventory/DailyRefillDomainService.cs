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

    public ValueTask<DailyRefillMutationResult> ApplyDailyRefillAsync(
        AccountId accountId, CharacterId characterId, IReadOnlyList<DailyRefillRule> rules,
        DateTimeOffset preparedAt, CancellationToken cancellationToken) =>
        ApplyAsync(accountId, characterId, rules, preparedAt, true, cancellationToken);

    public ValueTask<DailyRefillMutationResult> ApplyIndependentRefillAsync(
        AccountId accountId, CharacterId characterId, IReadOnlyList<DailyRefillRule> rules,
        DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Any(rule => !refillRules.UsesIndependentPeriod(rule)))
            throw new InvalidDataException("Online refill requires independent game-day rules.");
        return ApplyAsync(accountId, characterId, rules, at, false, cancellationToken);
    }

    private async ValueTask<DailyRefillMutationResult> ApplyAsync(
        AccountId accountId,
        CharacterId characterId,
        IReadOnlyList<DailyRefillRule> rules,
        DateTimeOffset preparedAt,
        bool claimOrdinaryBatch,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (!await characters.BelongsToAsync(characterId, accountId, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidDataException(
                "Daily refill character ownership does not match the account.");
        }

        var ordinaryPeriod = refillRules.PeriodOf(preparedAt);
        const string attemptKey = "daily_refill_attempt";
        var attempted = await counters.FindClaimedPeriodAsync(characterId, attemptKey, cancellationToken).ConfigureAwait(false);
        var ordinaryDue = refillRules.IsDue(attempted, ordinaryPeriod);
        var committed = new List<DailyRefillCommittedGrant>();
        foreach (var rule in rules)
        {
            if (!ordinaryDue && !refillRules.UsesIndependentPeriod(rule)) continue;
            var period = refillRules.PeriodOf(rule, preparedAt);
            var key = CounterPrefix + rule.ItemId;
            var claimed = await counters.FindClaimedPeriodAsync(characterId, key, cancellationToken)
                .ConfigureAwait(false);
            if (!refillRules.IsDue(rule, claimed, period))
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

        if (ordinaryDue && claimOrdinaryBatch
            && await counters.ClaimAsync(characterId, attemptKey, ordinaryPeriod, cancellationToken) != 1)
            throw new InvalidDataException("Daily refill attempt was not persisted.");
        return new DailyRefillMutationResult(
            [.. committed.GroupBy(value => value.Slot).Select(g => new DailyRefillCommittedGrant(g.Key, g.Last().ItemId, g.Sum(v => v.GrantedCount), g.Last().CommittedCount))
                .OrderBy(value => value.Slot.Space)
                .ThenBy(value => value.Slot.Index)]);
    }


}
