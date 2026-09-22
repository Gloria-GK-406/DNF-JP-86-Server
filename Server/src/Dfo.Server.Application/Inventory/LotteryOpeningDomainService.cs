using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

// Temporary deterministic adapter. Unconfigured pots fail closed; production randomness is a separate policy.
internal sealed class ConfiguredLotteryResultSelector(IServerConfigService config) : ILotteryResultSelector
{
    public OpeningReward Select(OpeningPackageDefinition definition)
    {
        var id = config.GetConfig<int>($"LotteryOpening:FixedRewards:{definition.ItemId}");
        var matches = definition.Pools.SelectMany(x => x.Choices).Where(x => x.Weight > 0 && x.Reward.ItemId == id).ToArray();
        if (matches.Length != 1) throw new OpeningRejectedException(OpeningRejection.UnsupportedContent,
            "No unique configured lottery test reward in this pot's pool.");
        return matches[0].Reward;
    }
}

internal sealed class LotteryOpeningDomainService(IOpeningInventoryDomainService inventory,
    IPackageOpeningRuleService rules, ILotteryResultSelector selector, ICharacterDomainService characters,
    ISelectedSessionDomainService sessions, IDomainTransactionAccessor transactions, TimeProvider clock)
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
        if (mode != 0) throw Reject("Premium lottery mode is not implemented.");
        if (completed.TryGetValue(session.CharacterId, out var previous) && previous.Session == session && previous.Sequence == sequence)
            throw Reject("Duplicate lottery request.");
        var source = await inventory.SourceAsync(session, slot, now, token).ConfigureAwait(false);
        var definition = await rules.DefinitionAsync(source.ItemId, OpeningPackageKind.Lottery, token).ConfigureAwait(false);
        if (definition.MaterialCount != 0 || definition.Pools.Length != 1 || definition.Pools[0].Draws != 1)
            throw Reject("Unsupported lottery layout.");
        var reward = selector.Select(definition);
        if (reward.Count != 1) throw Reject("Only one equipment result per pot is supported.");
        var grants = await rules.PrepareAsync([reward], now, token).ConfigureAwait(false);
        if (grants[0].Item.Placement.WorldClass != ItemWorldClass.Equipment || grants[0].Item.Placement.ItemKind != 1
            || grants[0].Item.PremiumType != 0 || grants[0].Item.Placement.Destination.Space != BusinessInventorySpace.CarriedEquipment)
            throw Reject("This lottery result variant is not supported yet.");
        var gold = await characters.Get(session.CharacterId).TrySpendGoldAsync(definition.GoldCost, token).ConfigureAwait(false);
        if (gold is null) throw Reject("Insufficient lottery gold.");
        var receipt = await inventory.SettleAsync(session, [new(slot, source.ItemId, definition.SourceCount)], grants, now, token).ConfigureAwait(false);
        if (receipt.Rewards.Length != 1 || receipt.Updates.Count(x => x.ContainerType == InventoryContainerType.Main) != 1)
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
