using System.Collections.Immutable;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.ExpertJobs;

internal sealed class ExpertJobProductionRuleService(EquipmentDisjointRuleService equipment, IEquipmentEnchantRuleService enchant)
{
    internal (int Skill, bool LevelTooLow)? DrawingRequirements(ExpertJobDefinition definition, uint experience, int drawing)
    {
        var maximumLevel = definition.LevelAt(experience) + 2;
        if (definition.Production?.Recipes.TryGetValue(drawing, out var recipe) == true)
            return (recipe.RequiredSkill, recipe.RequiredLevel > maximumLevel);
        if (definition.Enchantment?.CardRecipes.TryGetValue(drawing, out var cardRecipe) == true)
            return (cardRecipe.RequiredSkill, cardRecipe.RequiredLevel > maximumLevel);
        return null;
    }

    internal ExpertJobRecipeDefinition? CraftRecipe(ExpertJobProductionDefinition production, ExpertJobState state, int recipeId) =>
        production.Recipes.TryGetValue(recipeId, out var recipe) && state.LearnedRecipeIds.Contains(recipeId) ? recipe : null;

    internal EnchanterCardRecipe? BeadRecipe(EnchanterDefinition enchantment, ExpertJobState state, int recipeId) =>
        enchantment.CardRecipes.TryGetValue(recipeId, out var recipe) && state.LearnedRecipeIds.Contains(recipeId) ? recipe : null;

    internal (ExpertJobProductionRejection Rejection, ExpertJobCraftPlan? Plan) ResolveCraft(ExpertJobDefinition definition,
        ExpertJobProductionDefinition production, uint experience, ExpertJobRecipeDefinition recipe, ushort attempts, int randomSeed)
    {
        var difference = recipe.RequiredLevel - definition.LevelAt(experience);
        var rate = production.CompoundRates.FirstOrDefault(candidate => candidate.MaximumLevelDifference >= difference)
            ?? production.CompoundRates.LastOrDefault();
        if (rate is null && difference > 0)
            return (ExpertJobProductionRejection.LevelTooLow, null);

        var attemptedCount = (long)recipe.Product.Count * attempts;
        var goldCost = (long)recipe.GoldCost * attempts;
        if (attemptedCount > int.MaxValue || goldCost > int.MaxValue)
            return (ExpertJobProductionRejection.InvalidRecipe, null);

        var random = new Random(randomSeed);
        var gainRange = rate?.Experience ?? recipe.Experience;
        var successes = 0;
        var gain = 0L;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (random.Next(100) >= (rate?.SuccessPercent ?? 100)) continue;
            successes++;
            gain += ExpertJobEquipmentRules.Between(random, gainRange.Minimum, gainRange.Maximum);
        }

        ImmutableArray<OpeningReward> rewards = successes > 0
            ? [new(recipe.Product.ItemId, recipe.Product.Count * successes)] : [];
        return (ExpertJobProductionRejection.None, new((int)goldCost, recipe.Product with { Count = (int)attemptedCount },
            successes, attempts - successes, rewards, gain,
            successes > 0 && production.Extractors.ContainsKey(recipe.Product.ItemId)));
    }

    internal (ExpertJobProductionRejection Rejection, ExpertJobBeadRollRule? Rule) BeadRollRule(
        ExpertJobDefinition definition, EnchanterDefinition enchantment, uint experience, EnchanterCardRecipe recipe)
    {
        var level = definition.LevelAt(experience);
        if (level < recipe.RequiredLevel) return (ExpertJobProductionRejection.LevelTooLow, null);
        return enchantment.CardRates.TryGetValue(level, out var rate)
            ? (ExpertJobProductionRejection.None, new(rate.SuccessByQualification[recipe.Qualification], rate.Experience))
            : (ExpertJobProductionRejection.InvalidRecipe, null);
    }

    internal async ValueTask<ExpertJobBeadProduct?> ResolveBeadProductAsync(EnchanterCardRecipe recipe, ItemSnapshot card,
        CancellationToken ct)
    {
        if (!recipe.BeadsByCardId.TryGetValue(card.ItemId, out var bead)
            || !await enchant.CanProduceExpertBeadAsync(card, bead, ct).ConfigureAwait(false))
            return null;
        return new(new(bead, 1), card.EnchantUpgradeCount);
    }

    internal (bool Success, int ExperienceGain) ResolveBead(ExpertJobBeadRollRule rule, int randomSeed)
    {
        var random = new Random(randomSeed);
        var success = random.Next(100) < rule.SuccessPercent;
        return (success, success ? ExpertJobEquipmentRules.Between(random, rule.Experience.Minimum, rule.Experience.Maximum) : 0);
    }

    internal async ValueTask<(ExpertJobProductionRejection Rejection, ExpertJobExtractionPlan? Plan)> ResolveExtractionAsync(
        ExpertJobDefinition definition, ExpertJobProductionDefinition production, uint experience, int extractorId,
        ItemSnapshot item, int randomSeed, CancellationToken ct)
    {
        if (!production.Extractors.TryGetValue(extractorId, out var extractor)
            || extractor.RequiredLevel > definition.LevelAt(experience)
            || await equipment.ExpertTargetAsync(item, ct).ConfigureAwait(false) is not { } target)
            return (ExpertJobProductionRejection.InvalidItem, null);

        var (facts, grade, state) = target;
        if (!production.ExtractionResults.TryGetValue((extractorId, facts.Rarity, state), out var rule))
            return (state == EquipmentDisjointRuleService.UnpurifiedState
                ? ExpertJobProductionRejection.UnpurifiedEquipment : ExpertJobProductionRejection.InvalidItem, null);

        var random = new Random(randomSeed);
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

        var gain = ExpertJobEquipmentRules.Between(random, extractor.Experience.Minimum, extractor.Experience.Maximum);
        return (ExpertJobProductionRejection.None, new(
            [.. outputs.Select(output => new OpeningReward(output.Key, output.Value))], gain));
    }

    private static int FloorCount(double value) => (int)Math.Clamp(Math.Floor(value), 1, int.MaxValue);
}

internal sealed record ExpertJobCraftPlan(int GoldCost, RecipeAmount AttemptedOutput, int Successes, int Failures,
    ImmutableArray<OpeningReward> Rewards, long ExperienceGain, bool ExtractorProduct);

internal sealed record ExpertJobBeadRollRule(int SuccessPercent, ExpertJobExperienceRange Experience);

internal sealed record ExpertJobBeadProduct(OpeningReward Reward, byte UpgradeCount);

internal sealed record ExpertJobExtractionPlan(ImmutableArray<OpeningReward> Rewards, int ExperienceGain);
