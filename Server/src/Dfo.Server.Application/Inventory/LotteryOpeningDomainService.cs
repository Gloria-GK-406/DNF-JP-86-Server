using Dfo.Server.Abstractions.Accounts;
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
    ISelectedSessionDomainService sessions, IDomainTransactionAccessor transactions, TimeProvider clock,
    IPremiumServiceRuleService premiumRules, IPremiumServiceUsageDomainService usage)
    : ILotteryOpeningDomainService
{
    private sealed record Completed(SelectedSessionIdentity Session, ushort Sequence, DateTimeOffset Expires);
    private readonly Dictionary<CharacterId, Completed> completed = [];

    public async ValueTask<LotteryOpeningResult> ExecuteAsync(SelectedSessionIdentity session, int slot,
        ushort mode, ushort sequence, CancellationToken token)
    {
        var tx = transactions.Current ?? throw new InvalidOperationException("Lottery requires a transaction.");
        var now = clock.GetUtcNow();
        // The transactional command pipeline serializes this state with the inventory transaction.
        foreach (var stale in completed.Where(p => p.Value.Expires <= now || sessions.FindByCharacter(p.Key) != p.Value.Session).Select(p => p.Key).ToArray())
            completed.Remove(stale);
        if (mode > 1) throw Reject("Invalid lottery mode.");
        if (completed.TryGetValue(session.CharacterId, out var previous) && previous.Session == session && previous.Sequence == sequence)
            throw Reject("Duplicate lottery request.");
        var source = await inventory.SourceAsync(session, slot, now, token).ConfigureAwait(false);
        _ = cache.GetRequired(source.ItemId); // No on-demand PVF fallback for an unknown pot.
        var definition = await rules.DefinitionAsync(source.ItemId, OpeningPackageKind.Lottery, token).ConfigureAwait(false);
        if (definition.MaterialCount != 0 || definition.Pools.Length != 1 || definition.Pools[0].Draws != 1)
            throw Reject("Unsupported lottery layout.");
        if (mode == 1)
        {
            if (!premiumRules.SupportsDoublePot(source.ItemId))
                throw Reject("Pot is not eligible for double rewards.");
            if (!await usage.TryUseDoublePotAsync(session.AccountId, session.CharacterId, token).ConfigureAwait(false))
                throw new OpeningRejectedException(OpeningRejection.PremiumUnavailable, "Double pot subscription or daily quota unavailable.");
        }
        var reward = selector.Roll(source.ItemId);
        if (reward.Count != 1) throw Reject("Only one equipment result per pot is supported.");
        var grants = await rules.PrepareAsync([reward with { Count = mode == 1 ? 2 : 1 }], now, token).ConfigureAwait(false);
        if (grants[0].Item.Placement.WorldClass != ItemWorldClass.Equipment || grants[0].Item.Placement.ItemKind != 1
            || grants[0].Item.PremiumType != 0 || grants[0].Item.Placement.Destination.Space != BusinessInventorySpace.CarriedEquipment)
            throw Reject("This lottery result variant is not supported yet.");
        var gold = await characters.Get(session.CharacterId).TrySpendGoldAsync(definition.GoldCost, token).ConfigureAwait(false);
        if (gold is null) throw Reject("Insufficient lottery gold.");
        var receipt = await inventory.SettleAsync(session, [new(slot, source.ItemId, definition.SourceCount)], grants, now, token).ConfigureAwait(false);
        if (receipt.Rewards.Length != (mode == 1 ? 2 : 1) || receipt.Updates.Count(x => x.ContainerType == InventoryContainerType.Main) != 1)
            throw Reject("Unexpected lottery receipt layout.");
        tx.Enlist((this, session.CharacterId), () => new CompletedChange(this, session.CharacterId, new(session, sequence, now.AddMinutes(1))));
        return new(receipt, slot, gold.Value);
    }
    private static OpeningRejectedException Reject(string message) => new(OpeningRejection.StateChanged, message);
    private sealed class CompletedChange(LotteryOpeningDomainService owner, CharacterId id, Completed value) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken token) { owner.completed[id] = value; return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
}
