using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.ExpertJobs;

internal sealed class ExpertJobProductionOperations(IExpertJobStateStore states, IExpertJobRuleService rules, ExpertJobProductionRuleService productionRules,
    ISelectedSessionDomainService sessions, IDungeonParticipationDomainService dungeons, IExpertStoreDomainService stores,
    ICharacterSkillStateDomainService skills, ICharacterBusinessInventoryDomainService bags,
    IBusinessInventorySlotDomainService slots, IEquipmentDomainService equipment,
    IOpeningEffectPlanRuleService materials, IPackageOpeningRuleService rewards, IInventoryEffectPreflightDomainService preflight,
    IApplyEffectsDomainService effects, IDomainTransactionAccessor transactions,
    ExpertEnchantmentRuleService enchantmentRules, EnchanterStoreOperations enchanterStores, TimeProvider clock)
{
    public IAlchemistDomainService BindAlchemist(CharacterId owner) => new Alchemist(this, owner);
    public IDollControllerDomainService BindDollController(CharacterId owner) => new DollController(this, owner);
    public IEnchanterDomainService BindEnchanter(CharacterId owner) => new Enchanter(this, owner);

    public async ValueTask<ExpertJobStudyResult?> TryLearnRecipeAsync(CharacterId owner, SelectedSessionIdentity actor,
        BusinessSlot source, ItemReference drawing, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        if (!drawing.IsPile || await rules.FindDrawingJobAsync(drawing.ItemId, ct).ConfigureAwait(false) is not { } type)
            return null;
        var context = Context(owner, actor, footprint);
        if (context is null || !source.Space.IsCarriedItemSpace() || source.Space == BusinessInventorySpace.TowerQuickSlot
            || drawing.Count < 1 || drawing.SlotIndex != source.Index || drawing.Snapshot.EquipmentLockId != 0
            || drawing.Snapshot.SortLockFlag != 0 || drawing.Snapshot.Lifetime.IsExpired(context.At))
            return new(ExpertJobProductionRejection.InvalidItem);
        var state = await states.ReadAsync(owner, ct).ConfigureAwait(false);
        if (state.Type != type)
            return new(ExpertJobProductionRejection.InvalidItem);
        var definition = await rules.FindAsync(type, ct).ConfigureAwait(false);
        if (definition is null)
            return new(ExpertJobProductionRejection.InvalidItem);
        var requirements = productionRules.DrawingRequirements(definition, state.Experience, drawing.ItemId);
        if (requirements is not { } requirement
            || !await HasSkillAsync(owner, requirement.Skill, ct).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidItem);
        if (requirement.LevelTooLow)
            return new(ExpertJobProductionRejection.LevelTooLow);

        DomainEffect[] plan = [new ConsumeFromContainerEffect(source, drawing.ItemId, 1)];
        var rejection = await PreviewAsync(context, plan, limitRewards: false, ct).ConfigureAwait(false);
        if (rejection != ExpertJobProductionRejection.None)
            return new(rejection);
        var applied = await effects.ApplyAsync(context, plan, ct).ConfigureAwait(false);
        if (!state.LearnedRecipeIds.Contains(drawing.ItemId))
        {
            state = state with { LearnedRecipeIds = state.LearnedRecipeIds.Append(drawing.ItemId).Order().ToArray() };
            footprint.DemandEffects(context.Owner);
            await states.WriteAsync(owner, state, ct).ConfigureAwait(false);
        }
        return new(ExpertJobProductionRejection.None, applied.Changes<ContainerConsumptionChange>().Single().RemainingCount);
    }

    private async ValueTask<ExpertJobCraftResult> CraftAsync(CharacterId owner, ExpertJobType type,
        SelectedSessionIdentity actor, int recipeId, ushort attempts, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        var context = Context(owner, actor, footprint);
        if (context is null || attempts == 0 || stores.HasOpenStore(owner))
            return new(ExpertJobProductionRejection.InvalidState);
        var state = await states.ReadAsync(owner, ct).ConfigureAwait(false);
        var definition = await rules.FindAsync(type, ct).ConfigureAwait(false);
        if (state.Type != type || definition?.Production is not { } production)
            return new(ExpertJobProductionRejection.InvalidState);
        if (productionRules.CraftRecipe(production, state, recipeId) is not { } recipe
            || !await HasSkillAsync(owner, recipe.RequiredSkill, ct).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidRecipe);

        var (refusal, outcome) = productionRules.ResolveCraft(definition, production, state.Experience, recipe, attempts, Random.Shared.Next());
        if (outcome is null) return new(refusal);

        RecipeEffectCosts costs;
        try
        {
            var standing = await ExpertJobMaterialInventory.ReadAsync(bags, owner, ct).ConfigureAwait(false);
            costs = materials.MaterialCosts(recipe.Materials, attempts, standing, context.At);
        }
        catch (OpeningRejectedException error) when (error.Reason is OpeningRejection.MissingMaterial
            or OpeningRejection.InvalidRequest or OpeningRejection.UnsupportedContent)
        {
            return new(error.Reason == OpeningRejection.MissingMaterial
                ? ExpertJobProductionRejection.InsufficientMaterials : ExpertJobProductionRejection.InvalidRecipe);
        }

        var plan = costs.Items.Select(cost => (DomainEffect)new ConsumeFromContainerEffect(cost.Slot, cost.ItemId, cost.Count)).ToList();
        if (outcome.GoldCost > 0) plan.Add(new SpendGoldEffect(outcome.GoldCost));
        if (outcome.Rewards.Length > 0 && !await AddGrantsAsync(plan, outcome.Rewards, context.At, ct).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidRecipe);

        var rejection = await PreviewAsync(context, plan, limitRewards: false, ct).ConfigureAwait(false);
        if (rejection == ExpertJobProductionRejection.InsufficientMaterials && outcome.GoldCost > 0)
            return new(ExpertJobProductionRejection.InsufficientGold);
        if (rejection != ExpertJobProductionRejection.None)
            return new(rejection);
        var receipt = await SettleAsync(context, plan, definition, state, outcome.ExperienceGain, ct).ConfigureAwait(false);
        return new(ExpertJobProductionRejection.None, receipt,
            outcome.AttemptedOutput, outcome.Successes, outcome.Failures, outcome.ExtractorProduct);
    }

    private async ValueTask<ExpertJobCraftResult> CraftBeadAsync(CharacterId owner, SelectedSessionIdentity actor,
        int recipeId, BusinessSlot card, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        var context = Context(owner, actor, footprint);
        if (context is null || stores.HasOpenStore(owner))
            return new(ExpertJobProductionRejection.InvalidState);
        var state = await states.ReadAsync(owner, ct).ConfigureAwait(false);
        if (state.Type != ExpertJobType.Enchanter
            || await rules.FindAsync(state.Type, ct).ConfigureAwait(false) is not { Enchantment: { } enchantment } definition)
            return new(ExpertJobProductionRejection.InvalidState);
        if (productionRules.BeadRecipe(enchantment, state, recipeId) is not { } recipe
            || !await HasSkillAsync(owner, recipe.RequiredSkill, ct).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidRecipe);
        var (refusal, rollRule) = enchantmentRules.BeadRollRule(definition, enchantment, state.Experience, recipe);
        if (rollRule is null) return new(refusal);
        if (!card.Space.IsCarriedItemSpace() || card.Space == BusinessInventorySpace.TowerQuickSlot)
            return new(ExpertJobProductionRejection.InvalidItem);
        var cardSlot = slots.Find(owner, card);
        var cardItem = cardSlot is null ? null : await cardSlot.Container.GetAsync(card.Index, ct).ConfigureAwait(false);
        if (cardItem is not { IsPile: true, Count: > 0 } || cardItem.Snapshot.IsMirror
            || cardItem.Snapshot.EquipmentLockId != 0 || cardItem.Snapshot.SortLockFlag != 0
            || cardItem.Snapshot.Lifetime.IsExpired(context.At)
            || await enchantmentRules.ResolveBeadProductAsync(recipe, cardItem.Snapshot, ct).ConfigureAwait(false) is not { } product)
            return new(ExpertJobProductionRejection.InvalidItem);

        var plan = new List<DomainEffect> { new ConsumeFromContainerEffect(card, cardItem.ItemId, 1) };
        try
        {
            var standing = await ExpertJobMaterialInventory.ReadAsync(bags, owner, ct, excluded: card).ConfigureAwait(false);
            var costs = materials.MaterialCosts(recipe.Materials, 1, standing, context.At);
            plan.AddRange(costs.Items.Select(cost => new ConsumeFromContainerEffect(cost.Slot, cost.ItemId, cost.Count)));
        }
        catch (OpeningRejectedException error) when (error.Reason is OpeningRejection.MissingMaterial
            or OpeningRejection.InvalidRequest or OpeningRejection.UnsupportedContent)
        {
            return new(error.Reason == OpeningRejection.MissingMaterial
                ? ExpertJobProductionRejection.InsufficientMaterials : ExpertJobProductionRejection.InvalidRecipe);
        }

        if (!await AddGrantsAsync(plan, [product.Reward], context.At, ct, product.UpgradeCount).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidRecipe);
        // Validate capacity for the potential product before rolling, including the card and material debits.
        var rejection = await PreviewAsync(context, plan, limitRewards: false, ct).ConfigureAwait(false);
        if (rejection != ExpertJobProductionRejection.None) return new(rejection);
        var outcome = enchantmentRules.Roll(rollRule, Random.Shared.Next());
        if (!outcome.Success) plan.RemoveAt(plan.Count - 1);
        var receipt = await SettleAsync(context, plan, definition, state, outcome.ExperienceGain, ct).ConfigureAwait(false);
        return new(ExpertJobProductionRejection.None, receipt, new(product.Reward.ItemId, product.Reward.Count),
            outcome.Success ? 1 : 0, outcome.Success ? 0 : 1);
    }

    private async ValueTask<ExpertJobExtractionResult> ExtractAsync(CharacterId owner, ExpertJobType type,
        SelectedSessionIdentity actor, BusinessSlot extractor, BusinessSlot target, IEffectFootprintRecorder footprint,
        CancellationToken ct)
    {
        var context = Context(owner, actor, footprint);
        if (context is null)
            return new(ExpertJobProductionRejection.InvalidState);
        var state = await states.ReadAsync(owner, ct).ConfigureAwait(false);
        var definition = await rules.FindAsync(type, ct).ConfigureAwait(false);
        if (state.Type != type || definition?.Production is not { } production)
            return new(ExpertJobProductionRejection.InvalidState);
        if (extractor == target || !extractor.Space.IsCarriedItemSpace() || extractor.Space == BusinessInventorySpace.TowerQuickSlot
            || target.Space != BusinessInventorySpace.CarriedEquipment)
            return new(ExpertJobProductionRejection.InvalidItem);

        var toolSlot = slots.Find(owner, extractor);
        var tool = toolSlot is null ? null : await toolSlot.Container.GetAsync(extractor.Index, ct).ConfigureAwait(false);
        var instance = await equipment.Of(owner).FindAtAsync(target, ct).ConfigureAwait(false);
        var item = instance is null ? null : await instance.ReadAsync(ct).ConfigureAwait(false);
        if (tool is not { IsPile: true, Count: > 0 } || tool.Snapshot.Lifetime.IsExpired(context.At)
            || item is null || item.Snapshot.IsMirror || item.Snapshot.EquipmentLockId != 0 || item.Snapshot.SortLockFlag != 0
            || item.Snapshot.Lifetime.IsExpired(context.At))
            return new(ExpertJobProductionRejection.InvalidItem);

        var (refusal, outcome) = await productionRules.ResolveExtractionAsync(definition, production, state.Experience,
            tool.ItemId, item.Snapshot, Random.Shared.Next(), ct).ConfigureAwait(false);
        if (outcome is null) return new(refusal);
        var plan = new List<DomainEffect> { new DestroyEquipmentEffect(new(item, item.ItemId)) };
        if (!await AddGrantsAsync(plan, outcome.Rewards, context.At, ct).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidItem);
        var rejection = await PreviewAsync(context, plan, limitRewards: true, ct).ConfigureAwait(false);
        if (rejection != ExpertJobProductionRejection.None)
            return new(rejection);
        return new(ExpertJobProductionRejection.None,
            await SettleAsync(context, plan, definition, state, outcome.ExperienceGain, ct).ConfigureAwait(false));
    }

    private EffectExecutionContext? Context(CharacterId owner, SelectedSessionIdentity actor, IEffectFootprintRecorder footprint)
    {
        if (transactions.Current is null)
            throw new InvalidOperationException("Expert job production requires a domain transaction.");
        if (owner != actor.CharacterId || !sessions.IsInWorld(actor) || dungeons.Find(owner) is not null)
            return null;
        var context = new EffectExecutionContext(actor.AccountId, owner, clock.GetUtcNow(), footprint, actor);
        footprint.DemandEffects(context.Owner);
        return context;
    }

    // Appointment grants the profession skill. Recipe levels are checked against profession experience.
    private async ValueTask<bool> HasSkillAsync(CharacterId owner, int skillId, CancellationToken ct) =>
        (await skills.ReadAsync(owner, ct).ConfigureAwait(false))?.Pages.Any(page =>
            page.Skills.Any(skill => skill.SkillId == skillId && skill.Level > 0)) == true;

    private async ValueTask<bool> AddGrantsAsync(List<DomainEffect> plan, IReadOnlyList<OpeningReward> outputs,
        DateTimeOffset at, CancellationToken ct, byte enchantUpgradeCount = 0)
    {
        try
        {
            var grants = await rewards.PrepareAsync(outputs, at, ct).ConfigureAwait(false);
            if (grants.Length != outputs.Count || grants.Any(grant => grant.Item.PremiumType != 0
                || grant.Item.Placement.WorldClass != ItemWorldClass.Interchangeable
                || grant.Item.Placement.Destination.Space == BusinessInventorySpace.VirtualResource))
                return false;
            plan.AddRange(grants.Select(grant => new GrantPreparedItemEffect(
                grant with { EnchantUpgradeCount = enchantUpgradeCount }, ItemDeliveryMode.InventoryOnly)));
            return true;
        }
        catch (OpeningRejectedException)
        {
            return false;
        }
    }

    private async ValueTask<ExpertJobProductionRejection> PreviewAsync(EffectExecutionContext context, IReadOnlyList<DomainEffect> plan,
        bool limitRewards, CancellationToken ct)
    {
        var preview = await preflight.PreviewAsync(context, plan, ct).ConfigureAwait(false);
        return preview.Rejection switch
        {
            InventoryEffectRejection.None => limitRewards && preview.RewardEntries > byte.MaxValue
                ? ExpertJobProductionRejection.InventoryFull : ExpertJobProductionRejection.None,
            InventoryEffectRejection.NoRoom => ExpertJobProductionRejection.InventoryFull,
            InventoryEffectRejection.InsufficientAssets => ExpertJobProductionRejection.InsufficientMaterials,
            _ => ExpertJobProductionRejection.InvalidItem,
        };
    }

    private async ValueTask<ExpertJobProductionReceipt> SettleAsync(EffectExecutionContext context, IReadOnlyList<DomainEffect> plan,
        ExpertJobDefinition definition, ExpertJobState state, long gain, CancellationToken ct)
    {
        var applied = await effects.ApplyAsync(context, plan, ct).ConfigureAwait(false);
        var progress = definition.Advance(state, gain);
        if (progress.ExperienceChanged)
        {
            context.Footprint.DemandEffects(context.Owner);
            await states.WriteAsync(context.CharacterId, progress.State, ct).ConfigureAwait(false);
        }
        var changes = await EquipmentEffectReceiptProjection.FinalSlotsAsync(context.CharacterId, applied, slots, ct).ConfigureAwait(false);
        var products = applied.Changes<PreparedItemGrantChange>().SelectMany(change => change.Rewards).ToArray();
        var emptied = applied.Changes<ContainerConsumptionChange>().Where(change => change.RemainingCount == 0)
            .Select(change => change.Slot).ToHashSet();
        return new(changes, products, context.At, applied.Changes<GoldEffectChange>().SingleOrDefault()?.Remaining,
            progress.ExperienceChanged, progress.LevelChanged)
        {
            ReplacedSlots = changes.Where(change => change.Core is not null && emptied.Contains(change.Slot))
                .Select(change => change.Slot).ToArray(),
        };
    }

    private abstract class Role(ExpertJobProductionOperations operations, CharacterId owner, ExpertJobType type) : IExpertJobProductionService
    {
        protected ExpertJobProductionOperations Operations { get; } = operations;
        protected CharacterId Owner { get; } = owner;

        public ValueTask<ExpertJobCraftResult> CraftAsync(SelectedSessionIdentity actor, int recipeId, ushort attempts,
            IEffectFootprintRecorder footprint, CancellationToken ct) =>
            Operations.CraftAsync(Owner, type, actor, recipeId, attempts, footprint, ct);

        public ValueTask<ExpertJobExtractionResult> ExtractAsync(SelectedSessionIdentity actor, BusinessSlot extractor, BusinessSlot target,
            IEffectFootprintRecorder footprint, CancellationToken ct) =>
            Operations.ExtractAsync(Owner, type, actor, extractor, target, footprint, ct);
    }

    private sealed class Alchemist(ExpertJobProductionOperations operations, CharacterId owner)
        : Role(operations, owner, ExpertJobType.Alchemist), IAlchemistDomainService;
    private sealed class DollController(ExpertJobProductionOperations operations, CharacterId owner)
        : Role(operations, owner, ExpertJobType.DollController), IDollControllerDomainService;
    private sealed class Enchanter(ExpertJobProductionOperations operations, CharacterId owner)
        : Role(operations, owner, ExpertJobType.Enchanter), IEnchanterDomainService
    {
        public ValueTask<ExpertJobCraftResult> CraftBeadAsync(SelectedSessionIdentity actor, int recipeId, BusinessSlot card,
            IEffectFootprintRecorder footprint, CancellationToken ct) =>
            Operations.CraftBeadAsync(Owner, actor, recipeId, card, footprint, ct);

        public ValueTask<EnchanterStoreUseResult> EnchantAsync(ExpertStoreVisit visit, int recipeId, BusinessSlot card,
            BusinessSlot target, IEffectFootprintRecorder footprint, CancellationToken ct) =>
            Operations.EnchanterStores.EnchantAsync(Owner, visit, recipeId, card, target, footprint, ct);

        public ValueTask<ExpertMachineMaintenanceResult> RepairAsync(SelectedSessionIdentity actor,
            IEffectFootprintRecorder footprint, CancellationToken ct) =>
            Operations.EnchanterStores.RepairAsync(Owner, actor, footprint, ct);
    }

    private EnchanterStoreOperations EnchanterStores { get; } = enchanterStores;
}
