using Dfo.Server.Abstractions.Effects;
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

internal sealed class VendingMachineDomainService(IVendingMachineRuleService machines,
    IOpeningInventoryDomainService inventory, IPackageOpeningRuleService rewards, ILotteryRandomSource random,
    IPremiumServiceDomainService premiums, ITownAreaDomainService towns, ISelectedSessionDomainService sessions,
    IDomainTransactionAccessor transactions, TimeProvider clock, IOpeningEffectPlanRuleService plans) : IVendingMachineDomainService
{
    public async ValueTask<VendingMachineResult> UseAsync(SelectedSessionIdentity session, int machineId, int groupId,
        BusinessSlot slot, IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        _ = transactions.Current ?? throw new InvalidOperationException("Vending requires a transaction.");
        var now = clock.GetUtcNow();
        if (!sessions.IsInWorld(session) || towns.Find(session.CharacterId) is null)
            throw new OpeningRejectedException(OpeningRejection.NoSession, "Vending requires a current town character.");
        var definition = machines.Machine(machineId, groupId);
        if (!await premiums.For(session.AccountId).IsActiveAsync(PremiumServiceType.BlackDiamond, cancellationToken).ConfigureAwait(false))
            throw new OpeningRejectedException(OpeningRejection.PremiumUnavailable, "Black diamond is inactive.");
        var sourceSlot = slot;
        var source = await inventory.SourceAsync(session, sourceSlot, now, cancellationToken).ConfigureAwait(false);
        if (source.ItemId != definition.MaterialId || source.Count < definition.MaterialCount)
            throw new OpeningRejectedException(OpeningRejection.MissingMaterial, "Wrong or insufficient vending coins.");
        var reward = machines.RewardAt(definition, random.NextInt64(machines.TicketCount(definition)));
        var grants = await rewards.PrepareAsync([reward], now, cancellationToken).ConfigureAwait(false);
        // The captured machine currently grants main-bag goods only. Fail before consumption
        // if future content adds a special delivery channel not covered by this ACK.
        if (grants.Any(g => g.Item.PremiumType != 0 || g.Item.Placement.WorldClass is ItemWorldClass.Avatar or ItemWorldClass.Creature))
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported vending reward delivery.");
        var useRule = await plans.SourceRuleAsync(source.ItemId, cancellationToken).ConfigureAwait(false);
        // Vending coins are an explicit machine cost, independent of ordinary item self-consumption.
        var plan = plans.Build(source.ItemId, useRule, [new(sourceSlot, definition.MaterialId, definition.MaterialCount)], grants, [], now);
        var receipt = (await inventory.ApplyAsync(session, plan, now, footprint, cancellationToken).ConfigureAwait(false)).Inventory;
        return new(receipt, slot, source.Count - definition.MaterialCount, reward);
    }
}
