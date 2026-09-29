using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class VendingMachineDomainService(IVendingMachineDefinitionSource definitions,
    IOpeningInventoryDomainService inventory, IPackageOpeningRuleService rewards, ILotteryRandomSource random,
    IPremiumServiceDomainService premiums, ITownAreaDomainService towns, ISelectedSessionDomainService sessions,
    IDomainTransactionAccessor transactions, TimeProvider clock) : IVendingMachineDomainService
{
    private sealed record Completed(SelectedSessionIdentity Session, ushort Sequence, DateTimeOffset Expires);
    private readonly Dictionary<(CharacterId Character, ushort Sequence), Completed> completed = [];
    public async ValueTask<VendingMachineResult> UseAsync(SelectedSessionIdentity session, int machineId, int groupId,
        int slot, ushort sequence, CancellationToken cancellationToken)
    {
        var tx = transactions.Current ?? throw new InvalidOperationException("Vending requires a transaction.");
        var now = clock.GetUtcNow();
        foreach (var stale in completed.Where(p => p.Value.Expires <= now || sessions.FindByCharacter(p.Key.Character) != p.Value.Session).Select(p => p.Key).ToArray())
            completed.Remove(stale);
        if (sessions.Find(session.AccountId) != session || towns.Find(session.CharacterId) is null)
            throw new OpeningRejectedException(OpeningRejection.NoSession, "Vending requires a current town character.");
        if (completed.TryGetValue((session.CharacterId, sequence), out var previous) && previous.Session == session && previous.Sequence == sequence)
            throw new OpeningRejectedException(OpeningRejection.StateChanged, "Duplicate vending request.");
        var definition = definitions.GetRequired(machineId, groupId);
        if (!await premiums.For(session.AccountId).IsActiveAsync(PremiumServiceType.BlackDiamond, cancellationToken).ConfigureAwait(false))
            throw new OpeningRejectedException(OpeningRejection.PremiumUnavailable, "Black diamond is inactive.");
        var source = await inventory.SourceAsync(session, slot, now, cancellationToken).ConfigureAwait(false);
        if (source.ItemId != definition.MaterialId || source.Count < definition.MaterialCount)
            throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "Wrong or insufficient vending coins.");
        var reward = Select(definition, random.NextInt64(definition.Rewards[^1].CumulativeWeight));
        var grants = await rewards.PrepareAsync([reward], now, cancellationToken).ConfigureAwait(false);
        // The captured machine currently grants main-bag goods only. Fail before consumption
        // if future content adds a special delivery channel not covered by this ACK.
        if (grants.Any(g => g.Item.PremiumType != 0 || g.Item.Placement.WorldClass is ItemWorldClass.Avatar or ItemWorldClass.Creature))
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported vending reward delivery.");
        var receipt = await inventory.SettleAsync(session, [new(slot, definition.MaterialId, definition.MaterialCount)], grants, now, cancellationToken).ConfigureAwait(false);
        tx.Enlist((this, session.CharacterId), () => new Remember(this, session.CharacterId, new(session, sequence, now.AddMinutes(1))));
        return new(receipt, slot, source.Count - definition.MaterialCount, reward);
    }
    internal static OpeningReward Select(VendingMachineDefinition definition, long ticket)
    {
        if (ticket < 0 || ticket >= definition.Rewards[^1].CumulativeWeight)
            throw new InvalidOperationException("Out-of-range vending random ticket.");
        return definition.Rewards.First(r => ticket < r.CumulativeWeight).Reward;
    }
    private sealed class Remember(VendingMachineDomainService owner, CharacterId id, Completed value) : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken token) { owner.completed[(id, value.Sequence)] = value; return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
}
