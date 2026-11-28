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

internal sealed class ExpertJobProductionOperations(IExpertJobStateStore states, IExpertJobRuleService rules,
    ISelectedSessionDomainService sessions, IDungeonParticipationDomainService dungeons, IExpertStoreDomainService stores,
    ICharacterSkillStateDomainService skills, ICharacterBusinessInventoryDomainService bags,
    IBusinessInventorySlotDomainService slots, IEquipmentDomainService equipment, IEquipmentDisjointDefinitionSource definitions,
    IOpeningEffectPlanRuleService materials, IPackageOpeningRuleService rewards, IInventoryEffectPreflightDomainService preflight,
    IApplyEffectsDomainService effects, IDomainTransactionAccessor transactions, IEquipmentEnchantContentSource enchantContent,
    EnchanterStoreOperations enchanterStores, TimeProvider clock)
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
        var requirements = ReadDrawingRequirements(definition, drawing.ItemId);
        if (requirements is not { } requirement
            || !await HasSkillAsync(owner, requirement.Skill, ct).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidItem);
        if (requirement.Level > definition.LevelAt(state.Experience) + 2)
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

    private static (int Level, int Skill)? ReadDrawingRequirements(ExpertJobDefinition definition, int drawing)
    {
        if (definition.Production?.Recipes.TryGetValue(drawing, out var recipe) == true)
            return (recipe.RequiredLevel, recipe.RequiredSkill);
        if (definition.Enchantment?.CardRecipes.TryGetValue(drawing, out var cardRecipe) == true)
            return (cardRecipe.RequiredLevel, cardRecipe.RequiredSkill);
        return null;
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
        if (!production.Recipes.TryGetValue(recipeId, out var recipe) || !state.LearnedRecipeIds.Contains(recipeId)
            || !await HasSkillAsync(owner, recipe.RequiredSkill, ct).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidRecipe);

        var level = definition.LevelAt(state.Experience);
        var difference = recipe.RequiredLevel - level;
        var rate = production.CompoundRates.FirstOrDefault(candidate => candidate.MaximumLevelDifference >= difference)
            ?? production.CompoundRates.LastOrDefault();
        if (rate is null && difference > 0)
            return new(ExpertJobProductionRejection.LevelTooLow);
        var attemptedCount = (long)recipe.Product.Count * attempts;
        var goldCost = (long)recipe.GoldCost * attempts;
        if (attemptedCount > int.MaxValue || goldCost > int.MaxValue)
            return new(ExpertJobProductionRejection.InvalidRecipe);

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

        var random = Random.Shared;
        var experience = rate?.Experience ?? recipe.Experience;
        var successes = 0;
        var gain = 0L;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (random.Next(100) >= (rate?.SuccessPercent ?? 100)) continue;
            successes++;
            gain += ExpertJobEquipmentRules.Between(random, experience.Minimum, experience.Maximum);
        }
        var plan = costs.Items.Select(cost => (DomainEffect)new ConsumeFromContainerEffect(cost.Slot, cost.ItemId, cost.Count)).ToList();
        if (goldCost > 0) plan.Add(new SpendGoldEffect((int)goldCost));
        if (successes > 0 && !await AddGrantsAsync(plan, [new(recipe.Product.ItemId, recipe.Product.Count * successes)],
                context.At, ct).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidRecipe);

        var rejection = await PreviewAsync(context, plan, limitRewards: false, ct).ConfigureAwait(false);
        if (rejection == ExpertJobProductionRejection.InsufficientMaterials && goldCost > 0)
            return new(ExpertJobProductionRejection.InsufficientGold);
        if (rejection != ExpertJobProductionRejection.None)
            return new(rejection);
        var receipt = await SettleAsync(context, plan, definition, state, gain, ct).ConfigureAwait(false);
        return new(ExpertJobProductionRejection.None, receipt,
            recipe.Product with { Count = (int)attemptedCount }, successes, attempts - successes,
            successes > 0 && production.Extractors.ContainsKey(recipe.Product.ItemId));
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
        if (!enchantment.CardRecipes.TryGetValue(recipeId, out var recipe) || !state.LearnedRecipeIds.Contains(recipeId)
            || !await HasSkillAsync(owner, recipe.RequiredSkill, ct).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidRecipe);
        var level = definition.LevelAt(state.Experience);
        if (level < recipe.RequiredLevel) return new(ExpertJobProductionRejection.LevelTooLow);
        if (!enchantment.CardRates.TryGetValue(level, out var rate)) return new(ExpertJobProductionRejection.InvalidRecipe);
        if (!card.Space.IsCarriedItemSpace() || card.Space == BusinessInventorySpace.TowerQuickSlot)
            return new(ExpertJobProductionRejection.InvalidItem);
        var cardSlot = slots.Find(owner, card);
        var cardItem = cardSlot is null ? null : await cardSlot.Container.GetAsync(card.Index, ct).ConfigureAwait(false);
        if (cardItem is not { IsPile: true, Count: > 0 } || cardItem.Snapshot.IsMirror
            || cardItem.Snapshot.EquipmentLockId != 0 || cardItem.Snapshot.SortLockFlag != 0
            || cardItem.Snapshot.Lifetime.IsExpired(context.At)
            || !recipe.BeadsByCardId.TryGetValue(cardItem.ItemId, out var bead))
            return new(ExpertJobProductionRejection.InvalidItem);
        var upgradeCount = cardItem.Snapshot.EnchantUpgradeCount;
        if (!await enchantContent.ValidateExpertCardAsync(cardItem.ItemId, upgradeCount, null, ct).ConfigureAwait(false)
            || await enchantContent.ResolveExpertBeadAsync(bead, upgradeCount, null, ct).ConfigureAwait(false) is null)
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

        if (!await AddGrantsAsync(plan, [new(bead, 1)], context.At, ct, upgradeCount).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidRecipe);
        // Validate capacity for the potential product before rolling, including the card and material debits.
        var rejection = await PreviewAsync(context, plan, limitRewards: false, ct).ConfigureAwait(false);
        if (rejection != ExpertJobProductionRejection.None) return new(rejection);
        var random = Random.Shared;
        var success = random.Next(100) < rate.SuccessByQualification[recipe.Qualification];
        if (!success) plan.RemoveAt(plan.Count - 1);
        var gain = success ? ExpertJobEquipmentRules.Between(random, rate.Experience.Minimum, rate.Experience.Maximum) : 0;
        var receipt = await SettleAsync(context, plan, definition, state, gain, ct).ConfigureAwait(false);
        return new(ExpertJobProductionRejection.None, receipt, new(bead, 1), success ? 1 : 0, success ? 0 : 1);
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
            || !production.Extractors.TryGetValue(tool.ItemId, out var toolDefinition)
            || toolDefinition.RequiredLevel > definition.LevelAt(state.Experience)
            || item is null || item.Snapshot.IsMirror || item.Snapshot.EquipmentLockId != 0 || item.Snapshot.SortLockFlag != 0
            || item.Snapshot.Lifetime.IsExpired(context.At))
            return new(ExpertJobProductionRejection.InvalidItem);

        var facts = await definitions.GetEquipmentAsync(item.ItemId, ct).ConfigureAwait(false);
        if (facts is not { CanDisjoint: true, Grade: { } grade })
            return new(ExpertJobProductionRejection.InvalidItem);
        var equipmentState = ExpertJobEquipmentRules.StateOf(item.Snapshot);
        if (!production.ExtractionResults.TryGetValue((tool.ItemId, facts.Rarity, equipmentState), out var rule))
            return new(equipmentState == ExpertJobEquipmentRules.UnpurifiedState
                ? ExpertJobProductionRejection.UnpurifiedEquipment : ExpertJobProductionRejection.InvalidItem);

        var random = Random.Shared;
        var basis = production.ExtractionSellGoldDivisor is { } divisor
            ? Math.Max(1, facts.SellGold) / (double)divisor : Math.Max(1, grade);
        var outputs = new Dictionary<int, int> { [rule.ItemId] = FloorCount(basis * rule.Multiplier) };
        var great = random.Next(10000) < rule.GreatChance * 100;
        var table = great ? rule.GreatTable : rule.AdditionalTable;
        if (table != 0)
        {
            var roll = random.Next(10000);
            foreach (var row in great ? production.GreatResults : production.AdditionalResults)
            {
                if (row.Table != table || grade < row.MinimumGrade || grade > row.MaximumGrade) continue;
                roll -= Math.Clamp(row.Weight, 0, 10000);
                if (roll >= 0) continue;
                outputs[row.ItemId] = checked(outputs.GetValueOrDefault(row.ItemId) + FloorCount(grade * row.Multiplier));
                break;
            }
        }
        var plan = new List<DomainEffect> { new DestroyEquipmentEffect(new(item, item.ItemId)) };
        if (!await AddGrantsAsync(plan, outputs.Select(output => new OpeningReward(output.Key, output.Value)).ToArray(),
                context.At, ct).ConfigureAwait(false))
            return new(ExpertJobProductionRejection.InvalidItem);
        var rejection = await PreviewAsync(context, plan, limitRewards: true, ct).ConfigureAwait(false);
        if (rejection != ExpertJobProductionRejection.None)
            return new(rejection);
        var gain = ExpertJobEquipmentRules.Between(random, toolDefinition.Experience.Minimum, toolDefinition.Experience.Maximum);
        return new(ExpertJobProductionRejection.None, await SettleAsync(context, plan, definition, state, gain, ct).ConfigureAwait(false));
    }

    private static int FloorCount(double value) => (int)Math.Clamp(Math.Floor(value), 1, int.MaxValue);

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
        var experience = (uint)Math.Min(uint.MaxValue, state.Experience + gain);
        var previousLevel = definition.LevelAt(state.Experience);
        var level = definition.LevelAt(experience);
        if (experience != state.Experience)
        {
            var learned = level == previousLevel ? state.LearnedRecipeIds
                : state.LearnedRecipeIds.Union(definition.RecipesAt(experience)).Order().ToArray();
            context.Footprint.DemandEffects(context.Owner);
            await states.WriteAsync(context.CharacterId, state with { Experience = experience, LearnedRecipeIds = learned }, ct)
                .ConfigureAwait(false);
        }
        var changes = await EquipmentEffectReceiptProjection.FinalSlotsAsync(context.CharacterId, applied, slots, ct).ConfigureAwait(false);
        var products = applied.Changes<PreparedItemGrantChange>().SelectMany(change => change.Rewards).ToArray();
        var emptied = applied.Changes<ContainerConsumptionChange>().Where(change => change.RemainingCount == 0)
            .Select(change => change.Slot).ToHashSet();
        return new(changes, products, context.At, applied.Changes<GoldEffectChange>().SingleOrDefault()?.Remaining,
            experience != state.Experience, level != previousLevel)
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
