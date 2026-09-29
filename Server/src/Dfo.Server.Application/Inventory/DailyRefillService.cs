using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>
/// Gives a character what the day owes it, once per day.
/// </summary>
/// <remarks>
/// Two rules answer the questions this asks -- whether today has already been claimed, and where
/// a grant lands. What is here is the order they are asked in and what follows from each answer:
/// the counter is claimed whether or not anything was granted, so a rule that grants nothing today
/// is not asked again today; and a band with no room fails the whole entry rather than quietly
/// leaving the player short.
///
/// All of it settles with the use case it runs inside. It used to be its own transaction; a
/// failure now takes back the whole entry instead of only the refill, which is the same outcome
/// the caller reached anyway by refusing an entry whose refill failed.
///
/// Where a grant lands is the bag's own business and is asked of it directly. This used to be
/// carried out here, in a switch over the arrival that the tutorial reward had written out for
/// itself as well; both now ask the same bag and read back what it did. Refill runs after the
/// character is bound to its container -- entry binds before it dispatches this -- so an unbound
/// character is a mistake rather than a case, and the bag repository says so by refusing.
/// </remarks>
internal sealed class DailyRefillService(
    ICharacterTable characters,
    ICharacterItemTable items,
    ICharacterDailyCounterTable counters,
    IDailyRefillRuleService refillRules,
    ICharacterBusinessInventoryDomainService inventories,
    IItemArrivalRuleService arrivals,
    IItemPlacementRuleService placementRules) : IEntryInventoryMutationRepository
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
            var held = (await bag.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                .Where(item => item.ItemId == rule.ItemId).ToArray();
            var grant = refillRules.GrantFor(rule, held.Sum(item => Math.Max(0, item.Count)));
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

        return new DailyRefillMutationResult(
            [.. committed
                .OrderBy(value => value.Slot.Space)
                .ThenBy(value => value.Slot.Index)]);
    }

    /// <summary>Reads every occupied slot of the main inventory, in the order the client draws them.</summary>
    /// <remarks>
    /// A row is named by what the archive makes of it, not by the key of the row it was read out
    /// of: a thing with a name keeps that name across every write of it, and a pile is handed none
    /// because two of it are the same thing.
    /// </remarks>
    public async ValueTask<IReadOnlyList<InventoryItemState>> ReadMainItemsAsync(
        CharacterId characterId, CancellationToken cancellationToken)
    {
        var read = new List<InventoryItemState>();
        foreach (var row in await items.ReadMainAsync(characterId, cancellationToken)
                     .ConfigureAwait(false))
        {
            var worldClass = await placementRules
                .ClassifyAsync(row.Core.ItemId, cancellationToken).ConfigureAwait(false);
            read.Add(new InventoryItemState(
                worldClass is ItemWorldClass.Interchangeable
                    ? null
                    : new ItemIdentity(checked((int)row.ItemUid)),
                row.SlotIndex,
                row.Core));
        }

        return read;
    }

    /// <remarks>
    /// Every answer the bag can give is read back into this entry's own terms. A band with no room
    /// fails the whole entry rather than quietly leaving the player short -- an entry that skipped
    /// the refill would look to the player exactly like a day that owed them nothing.
    /// </remarks>
    private static async ValueTask GrantAsync(
        IBusinessItemContainer bag,
        IItemArrivalRuleService arrivals,
        DailyRefillRule rule,
        int grant,
        List<DailyRefillCommittedGrant> committed,
        CancellationToken cancellationToken)
    {
        var held = await bag.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var candidates = Enumerable.Range(rule.Destination.FirstIndex,
                rule.Destination.LastIndex - rule.Destination.FirstIndex + 1)
            .Select(index => new BusinessSlot(rule.Destination.Space, index)).ToArray();
        var arrival = arrivals.ForDailyRefill(
            held.Where(item => item.ItemId == rule.ItemId && item.IsPile)
                .Select(item => new ItemPile(new(rule.Destination.Space, item.SlotIndex), item.Count)).ToArray(),
            grant, rule.Destination,
            held.Select(item => new BusinessSlot(rule.Destination.Space, item.SlotIndex))
                .Where(candidates.Contains).ToHashSet());
        ItemPlacementOutcome outcome;
        switch (arrival)
        {
            case ItemArrival.OntoExistingPile pile:
                var existing = held.Single(item => item.SlotIndex == pile.Slot.Index);
                await bag.PutAsync(pile.Slot.Index, existing.Core with { Value = pile.Total }, cancellationToken)
                    .ConfigureAwait(false);
                outcome = new ItemPlacementOutcome.AddedToPile(pile.Slot.Index, pile.Added, pile.Total);
                break;
            case ItemArrival.IntoEmptySlot empty:
                if (rule.ItemKind == 0)
                    throw new InvalidDataException($"Daily refill item {rule.ItemId} has no kind.");
                await bag.PutAsync(empty.Slot.Index,
                    StackableItemCore.Create(rule.ItemKind, rule.ItemId, empty.Count), cancellationToken)
                    .ConfigureAwait(false);
                outcome = new ItemPlacementOutcome.PutInEmptySlot(empty.Slot.Index, empty.Count);
                break;
            case ItemArrival.NoRoom:
                outcome = new ItemPlacementOutcome.NoRoom();
                break;
            default:
                throw new InvalidOperationException("Unrecognized daily refill arrival.");
        }
        await bag.FlushAsync(cancellationToken).ConfigureAwait(false);
        switch (outcome)
        {
            case ItemPlacementOutcome.AddedToPile topped:
                committed.Add(new DailyRefillCommittedGrant(
                    new BusinessSlot(rule.Destination.Space, topped.SlotIndex), rule.ItemId,
                    topped.Added, topped.Total));
                return;

            case ItemPlacementOutcome.PutInEmptySlot opened:
                committed.Add(new DailyRefillCommittedGrant(
                    new BusinessSlot(rule.Destination.Space, opened.SlotIndex), rule.ItemId,
                    opened.Count, opened.Count));
                return;

            case ItemPlacementOutcome.NoRoom:
                throw new InvalidDataException(
                    $"Daily refill inventory range {rule.Destination.Space}/"
                        + $"{rule.Destination.FirstIndex}-{rule.Destination.LastIndex} is full.");

            default:
                // A refill names its own item, so the bag cannot answer that it does not know it;
                // listed anyway, because an outcome nobody read would leave the counter claimed
                // for a day the player was never given anything.
                throw new ArgumentOutOfRangeException(
                    nameof(rule), outcome, "No reading was decided for this outcome.");
        }
    }
}
