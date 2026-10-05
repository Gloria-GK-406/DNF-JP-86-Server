using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>Attempt every due item once per day; unavailable space loses the unplaced remainder.</summary>
internal sealed class DailyRefillDomainService(
    ICharacterTable characters,
    ICharacterDailyCounterTable counters,
    IDailyRefillRuleService refillRules,
    ICharacterBusinessInventoryDomainService inventories,
    IItemArrivalRuleService arrivals,
    IItemGrantDomainService grants) : IDailyRefillDomainService
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
                .GetContainer(rule.Destination.Space);
            var held = (await bag.GetAllAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => item.ItemId == rule.ItemId).ToArray();
            var grant = refillRules.GrantFor(rule, (int)Math.Min(int.MaxValue, held.Sum(item => (long)Math.Max(0, item.Count))));
            if (grant > 0)
            {
                await GrantAsync(bag, arrivals, rule, grant, committed, cancellationToken)
                    .ConfigureAwait(false);
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

    private async ValueTask GrantAsync(IBusinessItemContainer bag, IItemArrivalRuleService arrivals,
        DailyRefillRule rule, int remaining, List<DailyRefillCommittedGrant> committed, CancellationToken ct)
    {
        var open = await bag.OpenSlotCountAsync(ct);
        if (open <= rule.Destination.FirstIndex) return;
        var range = new BusinessSlotRange(rule.Destination.Space, rule.Destination.FirstIndex, Math.Min(rule.Destination.LastIndex, open - 1));
        while (remaining > 0)
        {
            var held = (await bag.GetAllAsync(ct)).Where(item => range.Contains(new(rule.Destination.Space, item.SlotIndex))).ToArray();
            var arrival = arrivals.ForDailyRefill(held.Where(item => item.ItemId == rule.ItemId && item.IsPile)
                .Select(item => new ItemPile(new(rule.Destination.Space, item.SlotIndex), item.Count)).ToArray(), remaining, range,
                held.Select(item => new BusinessSlot(rule.Destination.Space, item.SlotIndex)).ToHashSet(), rule.StackLimit);
            BusinessSlot slot; int added; int total;
            switch (arrival)
            {
                case ItemArrival.OntoExistingPile pile:
                    slot = pile.Slot; added = pile.Added; total = pile.Total;
                    var existing = held.Single(item => item.SlotIndex == slot.Index);
                    await grants.ApplyPlannedAsync(bag, slot.Index, existing.Snapshot with { Count = total }, ct); break;
                case ItemArrival.IntoEmptySlot empty:
                    if (rule.ItemKind == 0) throw new InvalidDataException($"Daily refill item {rule.ItemId} has no kind.");
                    slot = empty.Slot; added = empty.Count; total = empty.Count;
                    await grants.ApplyPlannedAsync(bag, slot.Index, new ItemSnapshot
                    {
                        ItemId = rule.ItemId,
                        Count = total,
                        IsPile = true,
                        LegacyKind = rule.ItemKind,
                        Type = ItemTraitClassification.For(rule.ItemKind, rule.ItemId)
                    }, ct); break;
                case ItemArrival.NoRoom: return;
                default: throw new InvalidOperationException("Unrecognized daily refill arrival.");
            }
            if (added <= 0) throw new InvalidDataException("Daily refill planner made no progress.");
            await bag.FlushAsync(ct);
            committed.Add(new(slot, rule.ItemId, added, total)); remaining -= added;
        }
    }
}
