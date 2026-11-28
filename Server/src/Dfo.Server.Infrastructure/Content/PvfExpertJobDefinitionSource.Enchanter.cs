using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.ExpertJobs;

namespace Dfo.Server.Infrastructure.Content;

internal sealed partial class PvfExpertJobDefinitionSource
{
    private sealed record CardRecipeGroup(byte Qualification, int RecipeId, int Level, IReadOnlyDictionary<int, int> Beads);

    internal static IReadOnlyDictionary<int, HashSet<int>> ReadExpertBeadCards(PvfArchive archive)
    {
        if (DefinitionPath(archive, ExpertJobType.Enchanter) is not { } path
            || PvfDocument.TryOpen(archive, path) is not { } doc)
            return new Dictionary<int, HashSet<int>>();
        return ReadCardRecipeGroups(doc, path).SelectMany(group => group.Beads)
            .GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).ToHashSet());
    }

    private static IEnumerable<CardRecipeGroup> ReadCardRecipeGroups(PvfDocument doc, string path)
    {
        var parents = doc.Sections.SkipWhile(section => section.Name != "[rarity recipe]").Skip(1)
            .TakeWhile(section => section.Name != "[/rarity recipe]").ToArray();
        for (var index = 0; index < parents.Length; index++)
        {
            if (parents[index].Name != "[rarity]") continue;
            var qualification = SingleInt(parents[index], path);
            var children = parents.Skip(index + 1).TakeWhile(section => section.Name != "[/rarity]").ToArray();
            var recipe = SingleInt(children.SingleOrDefault(section => section.Name == "[recipe]"), path);
            var level = SingleInt(children.SingleOrDefault(section => section.Name == "[expert job level]"), path);
            var results = children.SingleOrDefault(section => section.Name == "[base result]")?.Values;
            if (qualification is < 0 or > 4 || recipe <= 0 || level <= 0 || results is null
                || results.Count == 0 || results.Count % 2 != 0)
                throw new InvalidDataException($"Invalid card recipe group in '{path}'.");
            var beads = new Dictionary<int, int>();
            foreach (var row in results.Chunk(2))
            {
                var card = Int(row[0], path);
                var bead = Int(row[1], path);
                if (card <= 0 || bead <= 0 || !beads.TryAdd(card, bead))
                    throw new InvalidDataException($"Invalid card result in '{path}'.");
            }
            yield return new((byte)qualification, recipe, level, beads);
            index += children.Length + 1;
        }
    }

    private static int SingleInt(PvfSection? section, string path) =>
        section?.Values is [{ Kind: PvfTokenKind.Int32 } token] ? token.Value
            : throw new InvalidDataException($"Invalid card recipe field in '{path}'.");

    private static EnchanterDefinition ReadEnchantment(PvfArchive archive, PvfDocument doc, string path,
        IReadOnlyDictionary<int, byte> skills)
    {
        var stackables = PvfListFile.Paths(archive, "stackable/stackable.lst", prefix: "stackable/");
        var recipes = new Dictionary<int, EnchanterCardRecipe>();
        foreach (var group in ReadCardRecipeGroups(doc, path))
        {
            if (!stackables.TryGetValue(group.RecipeId, out var recipePath)
                || PvfDocument.TryOpen(archive, recipePath)?.AtRoot() is not { } drawing)
                throw new InvalidDataException($"Missing enchanter recipe {group.RecipeId} in '{path}'.");
            var kind = drawing.Read(PvfField.Tokens("[stackable type]"));
            var profession = drawing.Read(PvfField.Tokens("[expertjob only]"));
            var skill = drawing.Read(PvfField.Int32List("[need skill]"));
            if (kind is not [{ IsString: true } type, { Kind: PvfTokenKind.Int32 }] || archive.ResolveString(type.Value) != "[recipe]"
                || profession is not [{ IsString: true } owner, { Kind: PvfTokenKind.Int32 } level]
                || !string.Equals(archive.ResolveString(owner.Value), "enchanter", StringComparison.OrdinalIgnoreCase)
                || level.Value != group.Level || skill is not [> 0, > 0] || !skills.ContainsKey(skill[0]))
                throw new InvalidDataException($"Invalid card recipe in '{recipePath}'.");
            var requirements = ReadRows(drawing, "[input item]", 2, recipePath)
                .Select(row => new RecipeAmount(row[0], row[1])).ToArray();
            if (requirements.Any(cost => cost.ItemId <= 0 || cost.Count <= 0 || !stackables.ContainsKey(cost.ItemId)))
                throw new InvalidDataException($"Invalid card recipe cost in '{recipePath}'.");
            recipes.Add(group.RecipeId, new(group.Qualification, group.Level, skill[0], requirements, group.Beads));
        }

        var root = doc.AtRoot();
        var rates = ReadRows(root, "[monstercard exp]", 9, path).ToDictionary(row => row[0],
            row => new EnchanterCardRate(row[1..6], new(row[7], row[8])));
        var wear = root.Read(PvfField.Int32List("[endurance reduce]"));
        // The current archive declares two identical repair tables. Validate both if present.
        var tables = root.Sections.Where(section => section.Name == "[endurance repair cost]")
            .Select(section => section.Values.Select(token => Int(token, path)).ToArray()).ToArray();
        if (recipes.Count == 0 || rates.Any(row => row.Key <= 0 || !ValidRange(row.Value.Experience)
                || row.Value.SuccessByQualification.Any(rate => rate is < 0 or > 100))
            || wear is not [> 0, >= 0] || tables.Length == 0 || tables[0].Length == 0 || tables[0].Length % 2 != 0
            || tables[0].Any(value => value <= 0) || tables.Any(table => !table.SequenceEqual(tables[0])))
            throw new InvalidDataException($"Invalid enchanter settings in '{path}'.");
        return new(recipes, rates, wear[0], wear[1], tables[0].Chunk(2)
            .Select(row => new ExpertMachineRepairRule(row[0], row[1])).ToArray());
    }
}
