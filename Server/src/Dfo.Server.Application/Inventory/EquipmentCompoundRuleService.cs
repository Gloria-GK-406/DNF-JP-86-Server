using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentCompoundRuleService(IEquipmentCompoundDefinitionSource definitions)
{
    private const int Rare = 2;
    private const int LegacyGroup = 99;
    private const int Legendary = 6;
    private const int MaximumLevel = 85;

    // ServerS4A21 compatibility policy, independent of the archive's configurable base weight.
    private const double LegacyWeightScale = 0.01;

    internal EquipmentCompoundPlan Plan(int sourceItemId, bool directed, int part)
    {
        var content = definitions.Load();
        if (!content.Templates.TryGetValue(sourceItemId, out var source) || !Eligible(content, source))
            throw new OpeningRejectedException(OpeningRejection.WrongItem, "This equipment cannot be compounded.");
        if (directed && (!content.Parts.TryGetValue(part, out var allowed) || !allowed.Contains(source.Group)))
            throw new OpeningRejectedException(OpeningRejection.InvalidRequest, "Invalid directed equipment group.");

        var group = source.Legacy ? LegacyGroup : source.Rarity;
        if (!content.Materials.TryGetValue((group, directed), out var materials)
            || !content.Multipliers.TryGetValue((group, directed), out var multipliers)
            || !content.Levels.TryGetValue(group, out var levels))
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Missing compound recipe.");
        var multiplier = multipliers.Where(x => x.Key <= source.Level).MaxBy(x => x.Key).Value;
        var level = levels.FirstOrDefault(x => x.Level <= source.Level);
        if (multiplier <= 0 || level is null)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Missing compound level band.");

        var bands = new List<EquipmentCompoundBand>(2);
        AddBand(source.Level, level.SameWeight);
        if (source.Level <= MaximumLevel - 5) AddBand(source.Level + 5, level.HigherWeight);
        if (bands.Count == 0)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "No eligible compound result.");
        return new(materials.Select(row => row with { Count = checked(row.Count * multiplier) }).ToArray(), bands);

        void AddBand(int targetLevel, int weight)
        {
            if (weight <= 0 || !content.Pools.TryGetValue((source.Rarity, targetLevel), out var pool)) return;
            var candidates = pool.Where(x => Eligible(content, x)
                && (!directed || x.Group == source.Group)
                && (!source.Legacy || x.Legacy)
                && (source.Rarity == Rare || !x.Legacy))
                .Select(x => new EquipmentCompoundCandidate(x.ItemId,
                    content.ItemWeights.GetValueOrDefault(x.ItemId, x.Legacy ? content.LegacyWeight * LegacyWeightScale : 1)))
                .Where(x => x.Weight > 0).ToArray();
            if (candidates.Length > 0) bands.Add(new(targetLevel, weight, candidates));
        }
    }

    internal static int Draw(EquipmentCompoundPlan plan, int seed)
    {
        var random = new Random(seed);
        var band = Pick(plan.Bands, x => x.Weight, random);
        return Pick(band.Candidates, x => x.Weight, random).ItemId;
    }

    private static T Pick<T>(IReadOnlyList<T> candidates, Func<T, double> weight, Random random)
    {
        var remaining = random.NextDouble() * candidates.Sum(weight);
        foreach (var candidate in candidates)
        {
            remaining -= weight(candidate);
            if (remaining < 0) return candidate;
        }
        return candidates[^1];
    }

    private static bool Eligible(EquipmentCompoundContent content, EquipmentCompoundTemplate item) =>
        !content.ExcludedItems.Contains(item.ItemId) && item.AttachType == "sealing" && !item.GeneratedUnique
        && (item.Legacy || item.CreationRate > 0 || content.ItemWeights.ContainsKey(item.ItemId)
            || item.Rarity == Legendary && item.Level >= MaximumLevel);
}

internal sealed record EquipmentCompoundPlan(IReadOnlyList<RecipeAmount> Materials, IReadOnlyList<EquipmentCompoundBand> Bands);
internal sealed record EquipmentCompoundBand(int Level, int Weight, IReadOnlyList<EquipmentCompoundCandidate> Candidates);
internal sealed record EquipmentCompoundCandidate(int ItemId, double Weight);
