using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEpicBookDefinitionSource(PvfArchiveProvider provider) : IEpicBookDefinitionSource
{
    private readonly Lazy<IReadOnlyList<EpicPieceDefinition>> catalog = new(() => Read(provider));
    public IReadOnlyList<EpicPieceDefinition> Load() => catalog.Value;
    private readonly Lazy<IReadOnlyList<EpicCraftRecipe>> recipes = new(() => ReadRecipes(provider));
    public IReadOnlyList<EpicCraftRecipe> LoadRecipes() => recipes.Value;
    private static IReadOnlyList<EpicCraftRecipe> ReadRecipes(PvfArchiveProvider provider)
    {
        const string path = "etc/epicpieceinfo.etc";
        var archive = provider.Archive;
        if (archive.FindFile(path) < 0) return [];
        var definitions = Read(provider).ToDictionary(x => x.EquipmentId, x => x.PieceId);
        var results = new List<EpicCraftRecipe>();
        var inside = false;
        int? output = null;
        int[]? costs = null;
        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            if (section.Name == "[equipment recipe info]") { inside = true; continue; }
            if (section.Name == "[/equipment recipe info]") { inside = false; continue; }
            if (!inside) continue;
            if (section.Name == "[info]") { output = null; costs = null; }
            else if (section.Name == "[output]")
            {
                var values = PvfField.Int32List(section.Name).Decode(archive, section.Values, path).ToArray();
                if (values.Length != 1 || output is not null) throw new InvalidDataException("Invalid epic output.");
                output = values[0];
            }
            else if (section.Name == "[recipe]")
            {
                if (costs is not null) throw new InvalidDataException("Duplicate epic recipe.");
                costs = PvfField.Int32List(section.Name).Decode(archive, section.Values, path).ToArray();
            }
            else if (section.Name == "[/info]")
            {
                if (output is not { } id || !definitions.TryGetValue(id, out var piece)
                    || costs is null || costs.Length < 2 || costs.Length % 2 != 0 || costs.Any(x => x <= 0))
                    throw new InvalidDataException("Invalid epic recipe block.");
                var pairs = Enumerable.Range(0, costs.Length / 2).Select(i => new RecipeAmount(costs[i * 2], costs[i * 2 + 1])).ToArray();
                var pieces = pairs.Where(x => x.ItemId == piece).ToArray();
                if (pieces.Length != 1) throw new InvalidDataException("Epic recipe must debit its mapped piece.");
                results.Add(new(id, piece, pieces[0].Count, [.. pairs.Where(x => x.ItemId != piece)]));
                output = null; costs = null;
            }
        }
        if (results.Count != definitions.Count) throw new InvalidDataException("Incomplete epic recipes.");
        return results.AsReadOnly();
    }
    private static IReadOnlyList<EpicPieceDefinition> Read(PvfArchiveProvider provider)
    {
        const string path = "etc/epicpieceinfo.etc";
        var archive = provider.Archive;
        if (archive.FindFile(path) < 0) return [];
        var document = PvfDocument.Open(archive, path);
        var section = document.Sections.Single(x => x.Name == "[piece list]");
        var values = PvfField.Int32List(section.Name).Decode(archive, section.Values, path).ToArray();
        if (values.Length % 2 != 0 || values.Any(x => x <= 0)) throw new InvalidDataException("Invalid epic piece pairs.");
        var entries = new List<EpicPieceDefinition>();
        for (var at = 0; at < values.Length; at += 2) entries.Add(new(values[at + 1], values[at]));
        return entries.AsReadOnly();
    }
}
