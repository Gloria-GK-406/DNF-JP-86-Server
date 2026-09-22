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
    private sealed record Pending(SelectedSessionIdentity Session, int Slot, InventoryItemCoreState Source,
        OpeningReward Reward, OpeningPackageDefinition Definition, DateTimeOffset Expires);
    private readonly Dictionary<CharacterId, Pending> pending = [];

    public async ValueTask<LotteryOpeningResult> ExecuteAsync(SelectedSessionIdentity session, int? slot,
        bool start, CancellationToken token)
    {
        var tx = transactions.Current ?? throw new InvalidOperationException("Lottery requires a transaction.");
        var now = clock.GetUtcNow();
        // Transactional commands are serialized by the command pipeline. Remove stale session-owned state.
        foreach (var stale in pending.Where(p => p.Value.Expires <= now || sessions.FindByCharacter(p.Key) != p.Value.Session).Select(p => p.Key).ToArray())
            pending.Remove(stale);
        pending.TryGetValue(session.CharacterId, out var current);
        if (start)
        {
            if (slot is null) throw Reject("Missing start slot.");
            var source = await inventory.SourceAsync(session, slot.Value, now, token).ConfigureAwait(false);
            var definition = await rules.DefinitionAsync(source.ItemId, OpeningPackageKind.Lottery, token).ConfigureAwait(false);
            if (definition.MaterialCount != 0 || definition.Pools.Length != 1 || definition.Pools[0].Draws != 1)
                throw Reject("Unsupported lottery layout.");
            if (await characters.Get(session.CharacterId).GoldAsync(token).ConfigureAwait(false) < definition.GoldCost)
                throw Reject("Insufficient lottery gold.");
            if (current is null || current.Session != session || current.Slot != slot || current.Source != source.Core)
            {
                var reward = selector.Select(definition);
                if (reward.Count != 1) throw Reject("Only one equipment result per pot is supported.");
                var prepared = await rules.PrepareAsync([reward], now, token).ConfigureAwait(false);
                // Native ordinary result format only: avatar/pet result variants require their own evidence.
                if (prepared[0].Item.Placement.WorldClass != ItemWorldClass.Equipment || prepared[0].Item.Placement.ItemKind != 1
                    || prepared[0].Item.PremiumType != 0 || prepared[0].Item.Placement.Destination.Space != BusinessInventorySpace.CarriedEquipment)
                    throw Reject("This lottery result variant is not supported yet.");
                var next = new Pending(session, slot.Value, source.Core, reward, definition, now.AddMinutes(5));
                tx.Enlist((this, session.CharacterId), () => new PendingChange(this, session.CharacterId, next));
            }
            return new(true);
        }
        if (current is null || current.Session != session || slot is { } requested && requested != current.Slot)
            throw Reject("No matching pending lottery opening.");
        var actual = await inventory.SourceAsync(session, current.Slot, now, token).ConfigureAwait(false);
        if (actual.Core != current.Source) throw Reject("Lottery source changed after preview.");
        var grants = await rules.PrepareAsync([current.Reward], now, token).ConfigureAwait(false);
        var gold = await characters.Get(session.CharacterId).TrySpendGoldAsync(current.Definition.GoldCost, token).ConfigureAwait(false);
        if (gold is null) throw Reject("Insufficient lottery gold.");
        var receipt = await inventory.SettleAsync(session, [new(current.Slot, actual.ItemId, current.Definition.SourceCount)], grants, now, token).ConfigureAwait(false);
        if (receipt.Rewards.Length != 1 || receipt.Updates.Count(x => x.ContainerType == InventoryContainerType.Main) != 1)
            throw Reject("Unexpected lottery receipt layout.");
        tx.Enlist((this, session.CharacterId), () => new PendingChange(this, session.CharacterId, null));
        return new(false, receipt, current.Slot, gold);
    }
    private static OpeningRejectedException Reject(string message) => new(OpeningRejection.StateChanged, message);
    private sealed class PendingChange(LotteryOpeningDomainService owner, CharacterId id, Pending? next) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken token)
        {
            if (next is null) owner.pending.Remove(id); else owner.pending[id] = next;
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
}
