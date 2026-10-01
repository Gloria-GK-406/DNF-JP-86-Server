using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Inventory;
using System.Collections.Immutable;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfRecipeContentSource(PvfArchiveProvider provider) : IRecipeContentSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, RecipePlan?> cache = [];
    private Dictionary<int, string>? paths;
    public ValueTask<RecipePlan?> GetAsync(int drawingId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!cache.TryGetValue(drawingId, out var plan)) cache[drawingId] = plan = Read(drawingId);
            return ValueTask.FromResult(plan);
        }
    }
    private RecipePlan? Read(int id)
    {
        var archive = provider.Archive;
        paths ??= PvfListFile.Read(archive, "stackable/stackable.lst").ToDictionary(x => x.Id, x => x.RelativePath);
        if (!paths.TryGetValue(id, out var path)) return null;
        var sections = PvfNpcShopDefinitionSource.Sections(archive, "stackable/" + path);
        var type = sections.SingleOrDefault(s => s.Name == "[stackable type]");
        if (type is null || type.Values.Count != 2 || !type.Values[0].IsString
            || archive.ResolveString(type.Values[0].Value) != "[recipe]"
            || type.Values[1].Kind != PvfTokenKind.Int32 || type.Values[1].Value != 0) return null;
        // Professional, conditional and probabilistic recipes require their own semantics; never silently grant success.
        if (sections.Any(s => s.Name is "[int data]" or "[success rate]" or "[failure item]" or "[fail item]"
            || s.Name == "[need skill]" && s.Values.Count != 0)) return null;
        var input = Rows("[input item]"); var output = Rows("[output item]");
        return input.IsDefaultOrEmpty || output.IsDefaultOrEmpty ? null : new(input, output);
        ImmutableArray<RecipeAmount> Rows(string tag)
        {
            var rows = sections.Where(s => s.Name == tag).ToArray();
            if (rows.Length != 1 || rows[0].Values.Count is 0 or > 128 || rows[0].Values.Count % 2 != 0
                || rows[0].Values.Any(t => t.Kind != PvfTokenKind.Int32 || t.Value <= 0)) return [];
            return rows[0].Values.Chunk(2).Select(pair => new RecipeAmount(pair[0].Value, pair[1].Value)).ToImmutableArray();
        }
    }
}
