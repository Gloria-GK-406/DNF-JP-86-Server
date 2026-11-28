using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.ExpertJobs;

internal sealed class ExpertEnchantmentRuleService(IEquipmentEnchantRuleService enchant)
{
    internal (ExpertJobProductionRejection Rejection, ExpertEnchantmentRollRule? Rule) BeadRollRule(
        ExpertJobDefinition definition, EnchanterDefinition enchantment, uint experience, EnchanterCardRecipe recipe) =>
        RollRuleAtLevel(enchantment, definition.LevelAt(experience), recipe);

    internal async ValueTask<ExpertJobBeadProduct?> ResolveBeadProductAsync(EnchanterCardRecipe recipe, ItemSnapshot card,
        CancellationToken ct)
    {
        if (!recipe.BeadsByCardId.TryGetValue(card.ItemId, out var bead)
            || !await enchant.CanProduceExpertBeadAsync(card, bead, ct).ConfigureAwait(false))
            return null;
        return new(new(bead, 1), card.EnchantUpgradeCount);
    }

    internal (EnchanterStoreRejection Rejection, ExpertStoreEnchantQuote? Quote) PrepareStoreUse(
        ExpertJobDefinition definition, EnchanterDefinition enchantment, uint experience, int endurance, int recipeId)
    {
        var level = definition.LevelAt(experience);
        if (!enchantment.CardRecipes.TryGetValue(recipeId, out var recipe)
            || RollRuleAtLevel(enchantment, level, recipe).Rule is not { } roll)
            return (EnchanterStoreRejection.InvalidItem, null);

        var wear = level >= enchantment.WearMinimumLevel ? enchantment.WearPerAttempt : 0;
        return endurance <= 0 || endurance < wear
            ? (EnchanterStoreRejection.Exhausted, null)
            : (EnchanterStoreRejection.None, new(recipe, roll, endurance - wear));
    }

    internal async ValueTask<bool> AcceptsStoreCardAsync(ExpertStoreEnchantQuote quote, ItemSnapshot card, int targetItemId,
        CancellationToken ct) =>
        quote.Recipe.BeadsByCardId.ContainsKey(card.ItemId)
        && await enchant.AcceptsExpertCardAsync(card, targetItemId, ct).ConfigureAwait(false);

    internal (bool Success, int ExperienceGain, int Endurance) ResolveStoreUse(ExpertStoreEnchantQuote quote, int randomSeed)
    {
        var (success, gain) = Roll(quote.RollRule, randomSeed);
        return (success, gain, quote.RemainingEndurance);
    }

    internal (bool Success, int ExperienceGain) Roll(ExpertEnchantmentRollRule rule, int randomSeed)
    {
        var random = new Random(randomSeed);
        var success = random.Next(100) < rule.SuccessPercent;
        return (success, success ? ExpertJobEquipmentRules.Between(random, rule.Experience.Minimum, rule.Experience.Maximum) : 0);
    }

    internal (ExpertMachineMaintenanceRejection Rejection, (int Cost, int Endurance)? Quote) QuoteRepair(
        ExpertJobDefinition definition, EnchanterDefinition enchantment, uint experience, int endurance, int gold)
    {
        var level = definition.LevelAt(experience);
        if (level <= 0 || level > enchantment.Repairs.Count)
            return (ExpertMachineMaintenanceRejection.InvalidState, null);
        return ExpertMachineRepair.Quote(enchantment.Repairs[level - 1], endurance, gold) is { } quote
            ? (ExpertMachineMaintenanceRejection.None, quote)
            : (ExpertMachineMaintenanceRejection.CannotRepair, null);
    }

    private static (ExpertJobProductionRejection Rejection, ExpertEnchantmentRollRule? Rule) RollRuleAtLevel(
        EnchanterDefinition enchantment, int level, EnchanterCardRecipe recipe)
    {
        if (level < recipe.RequiredLevel) return (ExpertJobProductionRejection.LevelTooLow, null);
        return enchantment.CardRates.TryGetValue(level, out var rate)
            ? (ExpertJobProductionRejection.None, new(rate.SuccessByQualification[recipe.Qualification], rate.Experience))
            : (ExpertJobProductionRejection.InvalidRecipe, null);
    }
}

internal sealed record ExpertEnchantmentRollRule(int SuccessPercent, ExpertJobExperienceRange Experience);
internal sealed record ExpertJobBeadProduct(OpeningReward Reward, byte UpgradeCount);
internal sealed record ExpertStoreEnchantQuote(EnchanterCardRecipe Recipe, ExpertEnchantmentRollRule RollRule, int RemainingEndurance);
