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
/// </remarks>
internal sealed class DailyRefillService(
    ICharacterTable characters,
    ICharacterItemTable items,
    ICharacterDailyCounterTable counters,
    IDailyRefillRuleService refillRules,
    IItemArrivalRuleService arrivalRules) : IEntryInventoryMutationRepository
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

            var held = (await items.ReadMainStacksAsync(characterId, cancellationToken)
                    .ConfigureAwait(false))
                .Where(stack => stack.ItemId == rule.ItemId).ToArray();
            var grant = refillRules.GrantFor(rule, held.Sum(stack => Math.Max(0, stack.Count)));
            if (grant > 0)
            {
                await GrantAsync(characterId, rule, held, grant, committed, cancellationToken)
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
                .OrderBy(value => (byte)value.ContainerType)
                .ThenBy(value => value.SlotIndex)]);
    }

    /// <summary>Reads every occupied slot of the main inventory, in the order the client draws them.</summary>
    public async ValueTask<IReadOnlyList<InventoryItemState>> ReadMainItemsAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        [.. (await items.ReadMainAsync(characterId, cancellationToken).ConfigureAwait(false))
            .Select(row => new InventoryItemState(row.ItemUid, row.SlotIndex, row.Core))];

    private async ValueTask GrantAsync(
        CharacterId characterId,
        DailyRefillRule rule,
        IReadOnlyList<MainStackRow> held,
        int grant,
        List<DailyRefillCommittedGrant> committed,
        CancellationToken cancellationToken)
    {
        var occupied = await items.ReadMainOccupiedSlotsAsync(characterId, cancellationToken)
            .ConfigureAwait(false);
        var arrival = arrivalRules.ForDailyRefill(
            [.. held.Select(stack => new ItemPile(stack.SlotIndex, stack.Count))],
            grant, rule.SlotStart, rule.SlotEnd, occupied);
        switch (arrival)
        {
            case ItemArrival.IntoEmptySlot opened:
                if (rule.ItemKind == 0)
                {
                    throw new InvalidDataException(
                        $"Daily refill item {rule.ItemId} has no authoritative PVF item kind.");
                }

                if (await items.InsertStackAsync(
                        characterId, opened.SlotIndex, rule.ItemId, opened.Count, rule.ItemKind,
                        cancellationToken).ConfigureAwait(false) != 1)
                {
                    throw new InvalidDataException("Daily refill item insert failed.");
                }

                committed.Add(new DailyRefillCommittedGrant(
                    InventoryContainerType.Main, opened.SlotIndex, rule.ItemId,
                    opened.Count, opened.Count));
                return;

            case ItemArrival.OntoExistingPile topped:
                var stack = held.Single(value => value.SlotIndex == topped.SlotIndex);
                if (await items.SetStackCountAsync(stack.ItemUid, topped.Total, cancellationToken)
                        .ConfigureAwait(false) != 1)
                {
                    throw new InvalidDataException("Daily refill item update failed.");
                }

                committed.Add(new DailyRefillCommittedGrant(
                    InventoryContainerType.Main, topped.SlotIndex, rule.ItemId,
                    topped.Added, topped.Total));
                return;

            default:
                // The band is full. An entry that quietly skipped the refill would leave the
                // player short with nothing said, so the whole entry fails instead.
                throw new InvalidDataException(
                    $"Daily refill inventory range {rule.SlotStart}-{rule.SlotEnd} is full.");
        }
    }
}
