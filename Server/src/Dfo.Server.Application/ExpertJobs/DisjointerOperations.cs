using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.ExpertJobs;

internal sealed class DisjointerOperations(IExpertJobStateStore states, IExpertJobRuleService definitions,
    DisjointMachineRuleService machineRules, IEquipmentDomainService equipment,
    IPackageOpeningRuleService grants, IInventoryEffectPreflightDomainService preview, IApplyEffectsDomainService effects,
    IBusinessInventorySlotDomainService slots, ICharacterDomainService characters,
    IExpertStoreGameplayDomainService gameplay, IExpertStoreDomainService stores,
    ISelectedSessionDomainService sessions, ITownAreaDomainService areas, TimeProvider clock)
{
    public IDisjointerDomainService Bind(CharacterId owner) => new Disjointer(this, owner);

    private async ValueTask<DisjointMachineUseResult> DisjointAsync(CharacterId owner, ExpertStoreVisit visit,
        BusinessSlot slot, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        if (owner != visit.Store.Owner.CharacterId || visit.Store.Kind != ExpertStoreKind.DisjointMachine
            || await gameplay.ValidateUseAsync(visit, ct).ConfigureAwait(false) is not { Type: ExpertJobType.Disjointer } state
            || await definitions.FindAsync(ExpertJobType.Disjointer, ct).ConfigureAwait(false) is not { DisjointMachine: { } machine } definition)
            return new(DisjointMachineRejection.InvalidStore);
        if (state.DisjointMachineEndurance <= 0) return new(DisjointMachineRejection.Exhausted);

        var guest = visit.Visitor;
        var deviceOwner = visit.Store.Owner;
        var selfService = guest == deviceOwner;
        var price = selfService ? 0 : visit.Store.Cost;
        var at = clock.GetUtcNow();
        var guestContext = new EffectExecutionContext(guest.AccountId, guest.CharacterId, at, footprint, guest);
        var ownerContext = new EffectExecutionContext(deviceOwner.AccountId, owner, at, footprint, deviceOwner);
        footprint.DemandEffects(guestContext.Owner);
        footprint.DemandEffects(ownerContext.Owner);
        var requester = characters.Get(guest.CharacterId);
        var beneficiary = characters.Get(owner);
        if (price < 0 || price > 0 && await requester.GoldAsync(ct).ConfigureAwait(false) < price)
            return new(DisjointMachineRejection.InsufficientGold);
        if (price > 0 && (await preview.PreviewAsync(ownerContext, [new GrantOpeningGoldEffect(price)], ct)
            .ConfigureAwait(false)).Rejection != InventoryEffectRejection.None)
            return new(DisjointMachineRejection.InsufficientGold);

        if (slot.Space != BusinessInventorySpace.CarriedEquipment
            || await equipment.Of(guest.CharacterId).FindAtAsync(slot, ct).ConfigureAwait(false) is not { } instance)
            return new(DisjointMachineRejection.InvalidItem);
        var item = await instance.ReadAsync(ct).ConfigureAwait(false);
        if (item.Snapshot.EquipmentLockId != 0 || item.Snapshot.SortLockFlag != 0 || item.Snapshot.Lifetime.IsExpired(at))
            return new(DisjointMachineRejection.InvalidItem);

        var decision = await machineRules.ResolveAsync(machine, item.Snapshot, state.DisjointMachineGrade,
            state.DisjointMachineEndurance, selfService, Random.Shared.Next(), ct).ConfigureAwait(false);
        if (decision.Plan is not { } operation) return new(decision.Rejection);

        var plan = new List<DomainEffect> { new DestroyEquipmentEffect(new EquipmentEffectTarget(item, item.ItemId)) };
        if (price > 0) plan.Add(new SpendGoldEffect(price));
        try
        {
            foreach (var grant in await grants.PrepareAsync(operation.Rewards, at, ct).ConfigureAwait(false))
            {
                if (grant.Item.Placement.Destination.Space is not (BusinessInventorySpace.Consumable
                    or BusinessInventorySpace.Material or BusinessInventorySpace.Quest
                    or BusinessInventorySpace.ExpertMaterial or BusinessInventorySpace.SharedMaterial))
                    return new(DisjointMachineRejection.InvalidItem);
                plan.Add(new GrantPreparedItemEffect(grant, ItemDeliveryMode.InventoryOnly));
            }
        }
        catch (OpeningRejectedException)
        {
            return new(DisjointMachineRejection.InvalidItem);
        }
        var preparation = await preview.PreviewAsync(guestContext, plan, ct).ConfigureAwait(false);
        if (preparation.Rejection == InventoryEffectRejection.NoRoom) return new(DisjointMachineRejection.InventoryFull);
        if (preparation.Rejection != InventoryEffectRejection.None || preparation.RewardEntries > byte.MaxValue)
            return new(DisjointMachineRejection.InvalidItem);

        var endurance = operation.Endurance;
        var progress = definition.Advance(state, operation.ExperienceGain);
        var closure = endurance == 0 ? stores.StageClose(visit.Store)
            ?? throw new InvalidOperationException("The exhausting disjoint machine could not be staged for closure.") : null;

        var applied = await effects.ApplyAsync(guestContext, plan, ct).ConfigureAwait(false);
        if (price > 0)
            _ = await effects.ApplyAsync(ownerContext, [new GrantOpeningGoldEffect(price)], ct).ConfigureAwait(false);
        footprint.DemandEffects(ownerContext.Owner);
        await states.WriteAsync(owner, progress.State with { DisjointMachineEndurance = endurance }, ct).ConfigureAwait(false);
        var output = applied.Changes<PreparedItemGrantChange>().SelectMany(change => change.Rewards).ToArray();
        var changes = await EquipmentEffectReceiptProjection.FinalSlotsAsync(guest.CharacterId, applied, slots, ct).ConfigureAwait(false);
        return new(DisjointMachineRejection.None, new(output, changes, at,
            await requester.GoldAsync(ct).ConfigureAwait(false), await beneficiary.GoldAsync(ct).ConfigureAwait(false),
            endurance, progress.ExperienceChanged, closure));
    }

    private async ValueTask<ExpertMachineMaintenanceResult> MaintainAsync(CharacterId owner, SelectedSessionIdentity session,
        bool upgrade, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        if (owner != session.CharacterId || !sessions.IsInWorld(session) || stores.HasOpenStore(session)
            || areas.Find(owner) is not { SharesTownPresence: true } standing || standing.Session != session
            || await states.ReadAsync(owner, ct).ConfigureAwait(false) is not { Type: ExpertJobType.Disjointer } state
            || await definitions.FindAsync(ExpertJobType.Disjointer, ct).ConfigureAwait(false) is not { DisjointMachine: { } machine } definition)
            return new(ExpertMachineMaintenanceRejection.InvalidState);
        var context = new EffectExecutionContext(session.AccountId, owner, clock.GetUtcNow(), footprint, session);
        footprint.DemandEffects(context.Owner);
        var grade = state.DisjointMachineGrade;
        if (grade <= 0 || grade > machine.Repairs.Count) return new(ExpertMachineMaintenanceRejection.InvalidState);

        var character = characters.Get(owner);
        var gold = await character.GoldAsync(ct).ConfigureAwait(false);
        var repair = machine.Repairs[grade - 1];
        int cost;
        int endurance;
        if (upgrade)
        {
            var target = grade + 1;
            if (state.DisjointMachineEndurance != repair.MaximumEndurance || target > machine.Repairs.Count
                || definition.LevelAt(state.Experience) < target
                || !machine.UpgradeCosts.TryGetValue(target, out cost) || cost <= 0
                || !machine.MinimumCharacterLevels.TryGetValue(target, out var minLevel))
                return new(ExpertMachineMaintenanceRejection.CannotUpgrade);
            if (await character.ProgressAsync(ct).ConfigureAwait(false) is not { } progress || progress.Level < minLevel)
                return new(ExpertMachineMaintenanceRejection.CharacterLevelTooLow);
            if (gold < cost) return new(ExpertMachineMaintenanceRejection.InsufficientGold);
            grade = target;
            endurance = machine.Repairs[grade - 1].MaximumEndurance;
        }
        else if (ExpertMachineRepair.Quote(repair, state.DisjointMachineEndurance, gold) is { } quote)
        {
            (cost, endurance) = quote;
        }
        else
        {
            return new(ExpertMachineMaintenanceRejection.CannotRepair);
        }

        var applied = await effects.ApplyAsync(context, [new SpendGoldEffect(cost)], ct).ConfigureAwait(false);
        footprint.DemandEffects(context.Owner);
        await states.WriteAsync(owner, state with { DisjointMachineGrade = grade, DisjointMachineEndurance = endurance }, ct)
            .ConfigureAwait(false);
        return new(ExpertMachineMaintenanceRejection.None, applied.Changes<GoldEffectChange>().Single().Remaining, endurance, grade);
    }

    private sealed class Disjointer(DisjointerOperations operations, CharacterId owner) : IDisjointerDomainService
    {
        public ValueTask<DisjointMachineUseResult> DisjointAsync(ExpertStoreVisit visit, BusinessSlot slot,
            IEffectFootprintRecorder footprint, CancellationToken ct) => operations.DisjointAsync(owner, visit, slot, footprint, ct);

        public ValueTask<ExpertMachineMaintenanceResult> RepairAsync(SelectedSessionIdentity actor,
            IEffectFootprintRecorder footprint, CancellationToken ct) => operations.MaintainAsync(owner, actor, false, footprint, ct);

        public ValueTask<ExpertMachineMaintenanceResult> UpgradeAsync(SelectedSessionIdentity actor,
            IEffectFootprintRecorder footprint, CancellationToken ct) => operations.MaintainAsync(owner, actor, true, footprint, ct);
    }
}

internal static class ExpertMachineRepair
{
    internal static (int Cost, int Endurance)? Quote(ExpertMachineRepairRule rule, int endurance, int gold)
    {
        if (endurance < 0 || endurance >= rule.MaximumEndurance || rule.FullCost <= 0 || gold <= 0)
            return null;
        var fullRepair = rule.FullCost - (int)((long)endurance * rule.FullCost / rule.MaximumEndurance);
        if (gold >= fullRepair) return (fullRepair, rule.MaximumEndurance);
        var unitCost = rule.FullCost / rule.MaximumEndurance;
        if (unitCost <= 0 || gold < unitCost) return null;
        var points = gold / unitCost;
        return (points * unitCost, (int)Math.Min(rule.MaximumEndurance, (long)endurance + points));
    }
}

internal static class ExpertJobEquipmentRules
{
    internal static int Between(Random random, int lower, int upper) =>
        upper <= lower ? lower : checked((int)random.NextInt64(lower, (long)upper + 1));
}
