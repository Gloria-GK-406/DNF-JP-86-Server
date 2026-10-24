using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class LotteryRandomSource : ILotteryRandomSource
{
    public long NextInt64(long exclusiveUpperBound) => Random.Shared.NextInt64(exclusiveUpperBound);
}

internal sealed class WeightedLotteryResultSelector(ILotteryDefinitionCache cache, ILotteryRandomSource random) : ILotteryResultSelector
{
    public OpeningReward Roll(int potItemId)
    {
        var definition = cache.GetRequired(potItemId);
        var ends = definition.CumulativeWeights;
        if (!definition.Diagnostics.IsEmpty || definition.Pools.Length != 1 || definition.Pools[0].Draws != 1
            || ends.IsEmpty || ends.Length != definition.Pools[0].Choices.Length || ends[^1] <= 0)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, $"Pot {potItemId} has no valid weighted pool.");
        var ticket = random.NextInt64(ends[^1]);
        if (ticket < 0 || ticket >= ends[^1]) throw new InvalidOperationException("Lottery random source returned an out-of-range ticket.");
        // First cumulative bound strictly greater than ticket; repeated bounds skip zero weights.
        var low = 0;
        var high = ends.Length - 1;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (ticket < ends[mid]) high = mid;
            else low = mid + 1;
        }
        return definition.Pools[0].Choices[low].Reward;
    }
}

internal sealed class LotteryOpeningDomainService(IOpeningInventoryDomainService inventory,
    IPackageOpeningRuleService rules, ILotteryResultSelector selector, ILotteryDefinitionCache cache, ICharacterDomainService characters,
    IDomainTransactionAccessor transactions, TimeProvider clock,
    IPremiumServiceRuleService premiumRules, IPremiumServiceUsageDomainService usage, IOpeningEffectPlanRuleService plans,
    ICharacterBusinessInventoryDomainService bags)
    : ILotteryOpeningDomainService
{
    public async ValueTask<LotteryOpeningResult> ExecuteAsync(SelectedSessionIdentity session, BusinessSlot slot,
        ushort mode, IEffectFootprintRecorder footprint, CancellationToken token)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        _ = transactions.Current ?? throw new InvalidOperationException("Lottery requires a transaction.");
        var now = clock.GetUtcNow();
        if (mode > 1) throw Reject("Invalid lottery mode.");
        var sourceSlot = slot;
        var source = await inventory.SourceAsync(session, sourceSlot, now, token).ConfigureAwait(false);
        _ = cache.GetRequired(source.ItemId); // No on-demand PVF fallback for an unknown pot.
        var definition = await rules.DefinitionAsync(source.ItemId, OpeningPackageKind.Lottery, token).ConfigureAwait(false);
        var goldPot = source.ItemId == 7615 && mode == 0;
        if (definition.MaterialCount != 0 && !goldPot || definition.Pools.Length != 1 || definition.Pools[0].Draws != 1)
            throw Reject("Unsupported lottery layout.");
        var useRule = await plans.SourceRuleAsync(source.ItemId, token).ConfigureAwait(false);
        DoublePotUsageDecision? quota = null;
        if (mode == 1)
        {
            if (!premiumRules.SupportsDoublePot(source.ItemId))
                throw Reject("Pot is not eligible for double rewards.");
            quota = plans.DoublePot(await usage.DoublePotFactsAsync(session.AccountId, session.CharacterId, now, token).ConfigureAwait(false));
        }
        var reward = selector.Roll(source.ItemId);
        var goldReward = source.ItemId == 7615 && reward.ItemId == 0 && mode == 0 && reward.Count > 0;
        if (!goldReward && reward.Count != 1) throw Reject("Only one equipment result per pot is supported.");
        var grants = await rules.PrepareAsync([reward with { Count = goldReward ? reward.Count : mode == 1 ? 2 : 1 }], now, token).ConfigureAwait(false);
        if (!goldReward && (grants[0].Item.Placement.WorldClass != ItemWorldClass.Equipment || grants[0].Item.Placement.ItemKind != 1
            || grants[0].Item.PremiumType != 0 || grants[0].Item.Placement.Destination.Space != BusinessInventorySpace.CarriedEquipment))
            throw Reject("This lottery result variant is not supported yet.");
        var consumed = plans.SourceCosts(sourceSlot, source.ItemId, useRule, definition.SourceCount);
        if (definition.MaterialCount > 0)
        {
            var held = new List<OpeningHeldItem>();
            foreach (var space in new[] { BusinessInventorySpace.Quest, BusinessInventorySpace.Material,
                BusinessInventorySpace.Consumable, BusinessInventorySpace.QuickSlot })
                if (bags.GetStore(session.CharacterId, BusinessInventoryStoreKind.Item).FindContainer(space) is { } bag)
                    held.AddRange((await bag.GetAllAsync(token).ConfigureAwait(false)).Select(x => new OpeningHeldItem(new(space, x.SlotIndex), x)));
            consumed = consumed.AddRange(plans.MaterialCosts([new(definition.MaterialItemId, definition.MaterialCount)], 1, held, now).Items);
        }
        var plan = plans.Build(source.ItemId, useRule, consumed,
            grants, [], now, gold: definition.GoldCost, quota: quota);
        var applied = await inventory.ApplyAsync(session, plan, now, footprint, token).ConfigureAwait(false);
        var receipt = applied.Inventory;
        var gold = applied.Applied.Changes<GoldEffectChange>().LastOrDefault()?.Remaining
            ?? await characters.Get(session.CharacterId).GoldAsync(token).ConfigureAwait(false);
        if (receipt.Rewards.Length != (mode == 1 ? 2 : 1)
            || !goldReward && receipt.Rewards.Any(x => x.Count != 1 || x.Snapshot is null))
            throw Reject("Unexpected lottery receipt layout.");
        return new(receipt, slot, gold);
    }
    private static OpeningRejectedException Reject(string message) => new(OpeningRejection.StateChanged, message);
}
