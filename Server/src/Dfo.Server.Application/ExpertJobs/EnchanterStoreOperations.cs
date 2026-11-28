using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
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

internal sealed class EnchanterStoreOperations(IExpertJobStateStore states, IExpertJobRuleService rules,
    IExpertStoreGameplayDomainService gameplay, IExpertStoreDomainService stores, ISelectedSessionDomainService sessions,
    ITownAreaDomainService areas, IBusinessInventorySlotDomainService slots, IEquipmentDomainService equipment,
    IEquipmentEnchantContentSource content, ICharacterDomainService characters, IInventoryEffectPreflightDomainService preflight,
    IApplyEffectsDomainService effects, ICharacterBusinessInventoryDomainService bags,
    IOpeningEffectPlanRuleService materials, TimeProvider clock)
{
    public async ValueTask<EnchanterStoreUseResult> EnchantAsync(CharacterId owner, ExpertStoreVisit visit, int recipeId,
        BusinessSlot cardSlot, BusinessSlot targetSlot, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        if (owner != visit.Store.Owner.CharacterId || visit.Store.Kind != ExpertStoreKind.EnchantShop
            || await gameplay.ValidateUseAsync(visit, ct).ConfigureAwait(false) is not { Type: ExpertJobType.Enchanter } state
            || await rules.FindAsync(state.Type, ct).ConfigureAwait(false) is not { Enchantment: { } enchantment } definition)
            return new(EnchanterStoreRejection.InvalidState);
        var level = definition.LevelAt(state.Experience);
        if (!enchantment.CardRecipes.TryGetValue(recipeId, out var recipe) || recipe.RequiredLevel > level
            || !enchantment.CardRates.TryGetValue(level, out var rate))
            return new(EnchanterStoreRejection.InvalidItem);
        var wear = level >= enchantment.WearMinimumLevel ? enchantment.WearPerAttempt : 0;
        if (state.EnchanterEndurance <= 0 || state.EnchanterEndurance < wear)
            return new(EnchanterStoreRejection.Exhausted);
        if (cardSlot == targetSlot || !cardSlot.Space.IsCarriedItemSpace() || cardSlot.Space == BusinessInventorySpace.TowerQuickSlot
            || targetSlot.Space != BusinessInventorySpace.CarriedEquipment)
            return new(EnchanterStoreRejection.InvalidItem);

        var actor = visit.Visitor;
        var host = visit.Store.Owner;
        var now = clock.GetUtcNow();
        var guestContext = new EffectExecutionContext(actor.AccountId, actor.CharacterId, now, footprint, actor);
        var ownerContext = new EffectExecutionContext(host.AccountId, owner, now, footprint, host);
        footprint.DemandEffects(guestContext.Owner);
        footprint.DemandEffects(ownerContext.Owner);
        var source = slots.Find(actor.CharacterId, cardSlot);
        var card = source is null ? null : await source.Container.GetAsync(cardSlot.Index, ct).ConfigureAwait(false);
        var instance = await equipment.Of(actor.CharacterId).FindAtAsync(targetSlot, ct).ConfigureAwait(false);
        var target = instance is null ? null : await instance.ReadAsync(ct).ConfigureAwait(false);
        if (card is not { IsPile: true, Count: > 0 } || !CanUse(card.Snapshot, now)
            || target is null || !CanUse(target.Snapshot, now)
            || !recipe.BeadsByCardId.ContainsKey(card.ItemId)
            || !await content.ValidateExpertCardAsync(card.ItemId, card.Snapshot.EnchantUpgradeCount, target.ItemId, ct)
                .ConfigureAwait(false))
            return new(EnchanterStoreRejection.InvalidItem);

        var guest = characters.Get(actor.CharacterId);
        var beneficiary = characters.Get(owner);
        var price = actor == host ? 0 : visit.Store.Cost;
        if (price < 0 || price > 0 && await guest.GoldAsync(ct).ConfigureAwait(false) < price)
            return new(EnchanterStoreRejection.InsufficientGold);
        DomainEffect[] credit = price == 0 ? [] : [new GrantOpeningGoldEffect(price)];
        if (price > 0 && (await preflight.PreviewAsync(ownerContext, credit, ct).ConfigureAwait(false)).Rejection
                != InventoryEffectRejection.None)
            return new(EnchanterStoreRejection.InsufficientGold);

        var plan = new List<DomainEffect> { new ConsumeFromContainerEffect(cardSlot, card.ItemId, 1) };
        if (recipe.Materials.Count > 0)
        {
            try
            {
                var standing = await ExpertJobMaterialInventory.ReadAsync(bags, actor.CharacterId, ct, excluded: cardSlot)
                    .ConfigureAwait(false);
                var costs = materials.MaterialCosts(recipe.Materials, 1, standing, now);
                plan.AddRange(costs.Items.Select(cost => new ConsumeFromContainerEffect(cost.Slot, cost.ItemId, cost.Count)));
            }
            catch (OpeningRejectedException error) when (error.Reason is OpeningRejection.MissingMaterial
                or OpeningRejection.InvalidRequest or OpeningRejection.UnsupportedContent)
            {
                return new(error.Reason == OpeningRejection.MissingMaterial
                    ? EnchanterStoreRejection.InsufficientMaterials : EnchanterStoreRejection.InvalidItem);
            }
        }

        var random = Random.Shared;
        var succeeded = random.Next(100) < rate.SuccessByQualification[recipe.Qualification];
        if (price > 0) plan.Add(new SpendGoldEffect(price));
        if (succeeded) plan.Add(new SetEquipmentEnchantmentEffect(new(target, target.ItemId), card.ItemId, card.Snapshot.EnchantUpgradeCount));
        var prepared = await preflight.PreviewAsync(guestContext, plan, ct).ConfigureAwait(false);
        if (prepared.Rejection != InventoryEffectRejection.None)
            return new(prepared.Rejection == InventoryEffectRejection.InsufficientAssets
                ? EnchanterStoreRejection.InsufficientGold : EnchanterStoreRejection.InvalidItem);

        var endurance = state.EnchanterEndurance - wear;
        var gain = succeeded ? ExpertJobEquipmentRules.Between(random, rate.Experience.Minimum, rate.Experience.Maximum) : 0;
        var experience = (uint)Math.Min(uint.MaxValue, (long)state.Experience + gain);
        var levelChanged = definition.LevelAt(experience) != level;
        var closure = endurance == 0 ? stores.StageClose(visit.Store)
            ?? throw new InvalidOperationException("The exhausting enchant shop could not be staged for closure.") : null;
        var applied = await effects.ApplyAsync(guestContext, plan, ct).ConfigureAwait(false);
        if (price > 0)
            _ = await effects.ApplyAsync(ownerContext, credit, ct).ConfigureAwait(false);
        footprint.DemandEffects(ownerContext.Owner);
        await states.WriteAsync(owner, state with
        {
            EnchanterEndurance = endurance,
            Experience = experience,
            LearnedRecipeIds = levelChanged ? state.LearnedRecipeIds.Union(definition.RecipesAt(experience)).Order().ToArray()
                : state.LearnedRecipeIds,
        }, ct).ConfigureAwait(false);
        var changes = await EquipmentEffectReceiptProjection.FinalSlotsAsync(actor.CharacterId, applied, slots, ct).ConfigureAwait(false);
        return new(EnchanterStoreRejection.None, new(succeeded, experience, endurance,
            await guest.GoldAsync(ct).ConfigureAwait(false), await beneficiary.GoldAsync(ct).ConfigureAwait(false),
            changes, now, experience != state.Experience, levelChanged, closure));
    }

    private static bool CanUse(ItemSnapshot item, DateTimeOffset now) =>
        !item.IsMirror && item.EquipmentLockId == 0 && item.SortLockFlag == 0 && !item.Lifetime.IsExpired(now);

    public async ValueTask<ExpertMachineMaintenanceResult> RepairAsync(CharacterId owner, SelectedSessionIdentity session,
        IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        if (owner != session.CharacterId || !sessions.IsInWorld(session) || stores.HasOpenStore(session)
            || areas.Find(owner) is not { SharesTownPresence: true } standing || standing.Session != session
            || await states.ReadAsync(owner, ct).ConfigureAwait(false) is not { Type: ExpertJobType.Enchanter } state
            || await rules.FindAsync(state.Type, ct).ConfigureAwait(false) is not { Enchantment: { } enchantment } definition)
            return new(ExpertMachineMaintenanceRejection.InvalidState);
        var context = new EffectExecutionContext(session.AccountId, owner, clock.GetUtcNow(), footprint, session);
        footprint.DemandEffects(context.Owner);
        var level = definition.LevelAt(state.Experience);
        if (level <= 0 || level > enchantment.Repairs.Count) return new(ExpertMachineMaintenanceRejection.InvalidState);
        var gold = await characters.Get(owner).GoldAsync(ct).ConfigureAwait(false);
        if (ExpertMachineRepair.Quote(enchantment.Repairs[level - 1], state.EnchanterEndurance, gold) is not { } quote)
            return new(ExpertMachineMaintenanceRejection.CannotRepair);
        var (cost, endurance) = quote;
        var applied = await effects.ApplyAsync(context, [new SpendGoldEffect(cost)], ct).ConfigureAwait(false);
        footprint.DemandEffects(context.Owner);
        await states.WriteAsync(owner, state with { EnchanterEndurance = endurance }, ct).ConfigureAwait(false);
        return new(ExpertMachineMaintenanceRejection.None, applied.Changes<GoldEffectChange>().Single().Remaining, endurance);
    }
}
