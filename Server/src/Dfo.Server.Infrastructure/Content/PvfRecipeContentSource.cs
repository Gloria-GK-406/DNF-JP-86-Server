using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Inventory;
using System.Collections.Immutable;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfRecipeContentSource(PvfArchiveProvider provider) : IRecipeContentSource
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<int>> InputItemField = PvfField.Int32List("[input item]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> OutputItemField = PvfField.Int32List("[output item]", optional: true);

        internal static readonly PvfField<IReadOnlyList<PvfToken>> StackableTypeField = PvfField.Tokens("[stackable type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> NeedSkillField = PvfField.Tokens("[need skill]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> IntDataField = PvfField.Int32List("[int data]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> SuccessRateField = PvfField.Tokens("[success rate]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> FailureItemField = PvfField.Tokens("[failure item]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> FailItemField = PvfField.Tokens("[fail item]", optional: true);
    }

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
        var type = sections.SingleOrDefault(s => s.Name == Fields.StackableTypeField.Name);
        if (type is null || type.Values.Count != 2 || !type.Values[0].IsString
            || archive.ResolveString(type.Values[0].Value) != "[recipe]"
            || type.Values[1].Kind != PvfTokenKind.Int32 || type.Values[1].Value != 0) return null;
        // Professional, conditional and probabilistic recipes require their own semantics; never silently grant success.
        if (sections.Any(s => (s.Name == Fields.IntDataField.Name || s.Name == Fields.SuccessRateField.Name || s.Name == Fields.FailureItemField.Name || s.Name == Fields.FailItemField.Name)
            || s.Name == Fields.NeedSkillField.Name && s.Values.Count != 0)) return null;
        var input = Rows(Fields.InputItemField); var output = Rows(Fields.OutputItemField);
        return input.IsDefaultOrEmpty || output.IsDefaultOrEmpty ? null : new(input, output);
        ImmutableArray<RecipeAmount> Rows(PvfField<IReadOnlyList<int>> tag)
        {
            var rows = sections.Where(s => s.Name == tag.Name).ToArray();
            if (rows.Length != 1 || rows[0].Values.Count is 0 or > 128 || rows[0].Values.Count % 2 != 0
                || rows[0].Values.Any(t => t.Kind != PvfTokenKind.Int32 || t.Value <= 0)) return [];
            return rows[0].Values.Chunk(2).Select(pair => new RecipeAmount(pair[0].Value, pair[1].Value)).ToImmutableArray();
        }
    }
}
