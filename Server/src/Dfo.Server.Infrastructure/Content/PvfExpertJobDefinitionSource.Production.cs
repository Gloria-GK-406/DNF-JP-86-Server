using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.ExpertJobs;

namespace Dfo.Server.Infrastructure.Content;

internal sealed partial class PvfExpertJobDefinitionSource
{
    private static Dictionary<int, ExpertJobType> ReadDrawingJobs(PvfArchive archive)
    {
        var result = new Dictionary<int, ExpertJobType>();
        if (archive.FindFile(Catalog) < 0) return result;
        foreach (var (id, relative) in PvfListFile.Read(archive, Catalog))
        {
            var type = (ExpertJobType)id;
            if (type is not (ExpertJobType.Enchanter or ExpertJobType.Alchemist or ExpertJobType.DollController)) continue;
            var path = "character/" + relative;
            if (PvfDocument.TryOpen(archive, path) is not { } document) continue;
            var doc = document.AtRoot();
            var productionIds = type == ExpertJobType.Enchanter
                ? ReadRows(doc, "[product exp]", 3, path, optional: true).Select(row => row[0]).ToHashSet() : null;
            foreach (var row in ReadRows(doc, "[items]", 4, path, optional: true))
                if (row[1] > 0 && (productionIds is null || productionIds.Contains(row[1])))
                    result.Add(row[1], type);
            if (type == ExpertJobType.Enchanter)
                foreach (var group in ReadCardRecipeGroups(document, path)) result.Add(group.RecipeId, type);
        }
        return result;
    }

    private static ExpertJobProductionDefinition ReadProduction(PvfArchive archive, PvfDocument doc, string path, ExpertJobType type)
    {
        var extractExperience = ReadExperience(doc, "[extract exp]", path);
        var productExperience = ReadExperience(doc, "[product exp]", path, optional: true);
        var skillIds = Pairs(doc, "[skill]", path);
        var recipes = new Dictionary<int, ExpertJobRecipeDefinition>();
        var extractors = new Dictionary<int, ExpertJobExtractorDefinition>();
        var stackables = PvfListFile.Paths(archive, "stackable/stackable.lst", prefix: "stackable/");
        var name = type switch
        {
            ExpertJobType.Alchemist => "alchemist",
            ExpertJobType.DollController => "doll_controller",
            ExpertJobType.Enchanter => "enchanter",
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        var indices = new HashSet<int>();
        foreach (var row in ReadRows(doc, "[items]", 4, path))
        {
            var recipeId = row[1];
            var productId = row[2];
            var level = row[3];
            if (extractExperience.TryGetValue(productId, out var extractionGain))
            {
                var extractor = stackables.TryGetValue(productId, out var extractorPath)
                    ? PvfDocument.TryOpen(archive, extractorPath)?.AtRoot() : null;
                var profession = extractor?.Read(PvfField.Tokens("[expertjob only]", optional: true));
                var extractorIndex = extractor?.Read(PvfField.Int32($"[{name} extraction]"));
                if (level < 1 || profession is not [{ IsString: true } owner, { Kind: PvfTokenKind.Int32 } rank]
                    || !string.Equals(archive.ResolveString(owner.Value), name, StringComparison.OrdinalIgnoreCase)
                    || rank.Value != level || extractorIndex is not >= 0 || !indices.Add(extractorIndex.Value))
                    throw new InvalidDataException($"Invalid {name} extractor {productId} in '{path}'.");
                extractors.Add(productId, new(level, extractionGain));
            }

            if (!productExperience.TryGetValue(recipeId, out var productionGain) && type == ExpertJobType.Enchanter)
                continue;
            var recipePath = stackables.GetValueOrDefault(recipeId);
            var drawing = recipePath is null ? null : PvfDocument.TryOpen(archive, recipePath)?.AtRoot();
            if (drawing is null) continue;
            var drawingType = drawing.Read(PvfField.Tokens("[stackable type]", optional: true));
            var category = drawing.Read(PvfField.Tokens("[item category]", optional: true));
            if (drawingType is not [{ IsString: true } kind, { Kind: PvfTokenKind.Int32 }]
                || archive.ResolveString(kind.Value) != "[recipe]"
                || category is null || category.Any(token => !token.IsString)
                || string.Join(' ', category.Select(token => archive.ResolveString(token.Value))) != "expertjob recipe")
                continue;
            var required = drawing.Read(PvfField.Int32List("[need skill]"));
            var produced = drawing.Read(PvfField.Int32List("[output item]"));
            if (required is not [> 0, > 0] || !skillIds.ContainsKey(required[0])
                || produced is not [> 0, > 0] || produced[0] != productId || level <= 0)
                throw new InvalidDataException($"Invalid {name} drawing {recipeId} in '{recipePath}'.");

            var cost = 0;
            var materials = new List<RecipeAmount>();
            foreach (var entry in ReadRows(drawing, "[input item]", 2, recipePath!))
            {
                if (entry[0] < 0 || entry[1] <= 0)
                    throw new InvalidDataException($"Invalid expert job recipe input in '{recipePath}'.");
                if (entry[0] == 0)
                {
                    if (cost != 0) throw new InvalidDataException($"Multiple expert job gold prices in '{recipePath}'.");
                    cost = entry[1];
                }
                else materials.Add(new(entry[0], entry[1]));
            }
            recipes.Add(recipeId, new(level, required[0], required[1], materials.AsReadOnly(),
                new(productId, produced[1]), cost, productionGain ?? new(0, 0)));
        }
        if (recipes.Count == 0 || extractors.Count != extractExperience.Count)
            throw new InvalidDataException($"Missing expert job recipes or extractors in '{path}'.");

        var rates = ReadRows(doc, "[compound rate]", 4, path, optional: true)
            .Select(row => new ExpertJobCompoundRate(row[0], row[1], new(row[2], row[3]))).ToArray();
        if (!rates.Select(rate => rate.MaximumLevelDifference).SequenceEqual(
                rates.Select(rate => rate.MaximumLevelDifference).Distinct().Order())
            || rates.Any(rate => rate.SuccessPercent is < 0 or > 100 || !ValidRange(rate.Experience)))
            throw new InvalidDataException($"Invalid expert job attempt rates in '{path}'.");

        var extraction = NumberRows(doc, "[extraction result]", 8, path).ToDictionary(
            row => (Whole(row[0], path), Whole(row[1], path), Whole(row[2], path)),
            row => new ExpertJobExtractionRule(Whole(row[3], path), row[4], Whole(row[5], path), Whole(row[6], path),
                Whole(row[7], path)));
        var additional = ReadExtractionSelection(doc, "[additional result]", path);
        var great = ReadExtractionSelection(doc, "[big win result]", path);
        foreach (var ((extractorId, rarity, equipmentState), rule) in extraction)
            if (!extractors.ContainsKey(extractorId) || rarity > 6 || equipmentState > 2 || rule.ItemId <= 0
                || rule.Multiplier <= 0 || rule.GreatChance > 100
                || rule.AdditionalTable != 0 && !additional.Any(row => row.Table == rule.AdditionalTable)
                || rule.GreatTable != 0 && !great.Any(row => row.Table == rule.GreatTable))
                throw new InvalidDataException($"Invalid {name} extraction row in '{path}'.");
        int? divisor = null;
        if (type == ExpertJobType.Enchanter)
        {
            if (doc.Read(PvfField.Int32List("[enchanter extraction result item]")) is not [> 0, > 0] result)
                throw new InvalidDataException($"Invalid enchanter extraction settings in '{path}'.");
            divisor = result[1];
        }
        return new(recipes, rates, extractors, extraction, additional, great, divisor);
    }

    private static bool ValidRange(ExpertJobExperienceRange range) => range.Minimum >= 0 && range.Maximum >= range.Minimum;

    private static Dictionary<int, ExpertJobExperienceRange> ReadExperience(PvfDocument doc, string tag, string path,
        bool optional = false)
    {
        var result = ReadRows(doc, tag, 3, path, optional).ToDictionary(row => row[0], row => new ExpertJobExperienceRange(row[1], row[2]));
        if (result.Any(pair => pair.Key <= 0 || !ValidRange(pair.Value)))
            throw new InvalidDataException($"Invalid {tag} rows in '{path}'.");
        return result;
    }

    private static IEnumerable<int[]> ReadRows(PvfDocument doc, string tag, int width, string path, bool optional = false)
    {
        var tokens = doc.Read(PvfField.Int32List(tag, optional)) ?? [];
        if (tokens.Count % width != 0 || !optional && tokens.Count == 0)
            throw new InvalidDataException($"Invalid {tag} rows in '{path}'.");
        return tokens.Chunk(width);
    }

    private static ExpertJobExtractionSelectionRule[] ReadExtractionSelection(PvfDocument doc, string tag, string path)
    {
        var result = NumberRows(doc, tag, 6, path).Select(row => new ExpertJobExtractionSelectionRule(
            Whole(row[0], path), Whole(row[1], path), Whole(row[2], path), Whole(row[3], path), Whole(row[4], path), row[5])).ToArray();
        if (result.Any(row => row.Table <= 0 || row.MinimumGrade > row.MaximumGrade || row.ItemId <= 0
            || row.Weight <= 0 || row.Multiplier <= 0))
            throw new InvalidDataException($"Invalid {tag} selections in '{path}'.");
        return result;
    }
}
