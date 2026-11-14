using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Inventory;
using System.Collections.Immutable;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfRecipeContentSource(PvfArchiveProvider provider) : IRecipeContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
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

    internal const string FailOutputField = "[fail output]";
    internal const string MaintainAbilityField = "[maintain ability]";
    internal const string BroadcastTypeField = "[broadcast type]";

    private readonly Lock gate = new();
    private readonly Dictionary<int, RecipePlan?> cache = [];
    private IReadOnlyDictionary<int, string>? paths;
    public string Name => "recipe-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var archive = provider.Archive;
            PathsIndex(archive);
            return ValueTask.FromResult(paths.Count);
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(paths))]
    private void PathsIndex(Dfo.Pvf.PvfArchive archive)
    {
        paths ??= PvfListFile.Paths(archive, "stackable/stackable.lst");
    }

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
        PathsIndex(archive);
        if (!paths.TryGetValue(id, out var path)) return null;
        var sections = (PvfDocument.TryOpen(archive, "stackable/" + path, PvfPreamble.Ignore)?.Sections ?? []);
        var type = sections.SingleOrDefault(s => s.Name == Fields.StackableTypeField.Name);
        if (type is null || type.Values.Count != 2 || !type.Values[0].IsString
            || archive.ResolveString(type.Values[0].Value) != "[recipe]"
            || type.Values[1].Kind != PvfTokenKind.Int32 || type.Values[1].Value is not (0 or 1)) return null;
        // Professional and conditional recipes require their own semantics; never silently grant success.
        if (sections.Any(s => (s.Name == Fields.IntDataField.Name || s.Name == Fields.FailureItemField.Name || s.Name == Fields.FailItemField.Name)
            || s.Name == Fields.NeedSkillField.Name && s.Values.Count != 0)) return null;
        var input = Rows(Fields.InputItemField.Name); var output = Rows(Fields.OutputItemField.Name);
        if (input.IsDefaultOrEmpty || output.IsDefaultOrEmpty) return null;
        var rate = sections.Where(s => s.Name == Fields.SuccessRateField.Name).ToArray();
        // Every A21 drawing that declares it gives 1; the client's wording for it is "crafted with a drawing".
        var broadcast = sections.Any(s => s.Name == BroadcastTypeField
            && s.Values is [{ Kind: PvfTokenKind.Int32, Value: not 0 }]);
        // [recipe] 1 is only understood in its probabilistic requiem shape: [success rate], [fail output] and
        // [maintain ability] normal together. Any other [recipe] 1 drawing keeps being refused.
        if (type.Values[1].Value == 0) return rate.Length == 0 ? new(input, output) { Broadcast = broadcast } : null;
        var fail = Rows(FailOutputField);
        var maintain = sections.Where(s => s.Name == MaintainAbilityField).ToArray();
        if (rate is not [{ Values: [{ Kind: PvfTokenKind.Int32 } chance] }] || chance.Value is < 0 or > 10000
            || fail.IsDefaultOrEmpty
            || maintain is not [{ Values: [{ IsString: true } mode] }] || archive.ResolveString(mode.Value) != "normal")
            return null;
        return new(input, output) { Chance = new(chance.Value, fail, MaintainAbility: true), Broadcast = broadcast };
        ImmutableArray<RecipeAmount> Rows(string tag)
        {
            var rows = sections.Where(s => s.Name == tag).ToArray();
            if (rows.Length != 1 || rows[0].Values.Count is 0 or > 128 || rows[0].Values.Count % 2 != 0
                || rows[0].Values.Any(t => t.Kind != PvfTokenKind.Int32 || t.Value <= 0)) return [];
            return rows[0].Values.Chunk(2).Select(pair => new RecipeAmount(pair[0].Value, pair[1].Value)).ToImmutableArray();
        }
    }
}
