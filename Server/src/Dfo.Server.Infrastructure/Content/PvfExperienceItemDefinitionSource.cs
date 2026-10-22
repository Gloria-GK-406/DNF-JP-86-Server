using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfExperienceItemDefinitionSource(
    PvfArchiveProvider provider,
    PvfContentOptions? options = null)
    : IExperienceItemDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<PvfToken>> StackableType =
            PvfField.Tokens("[stackable type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> IncreaseStatusType =
            PvfField.Tokens("[increase status type]", optional: true);
        internal static readonly PvfField<int> MinimumLevel =
            PvfField.Int32("[minimum level]", optional: true);
        internal static readonly PvfField<int> MaximumLevel =
            PvfField.Int32("[maximum level]", optional: true);
    }

    private const string StackableListPath = "stackable/stackable.lst";
    private readonly Lock gate = new();
    private readonly Dictionary<int, ExperienceItemDefinition?> cache = [];
    private IReadOnlyDictionary<int, string>? paths;

    public string Name => "experience-item-index";

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
        paths ??= PvfListFile.Read(archive, StackableListPath)
                .ToDictionary(entry => entry.Id, entry => "stackable/" + entry.RelativePath);
    }

    public ValueTask<ExperienceItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            // Test and diagnostic hosts may replace the ordinary item-use source and intentionally
            // run without an archive. Keep this additive classifier absent there instead of making
            // every unrelated stackable use open Script.pvf before the configured source runs.
            // Once the list is loaded, avoid probing the filesystem again for every item use.
            if (paths is null && options is not null && options.ResolveArchivePath() is null)
                return ValueTask.FromResult<ExperienceItemDefinition?>(null);
            if (cache.TryGetValue(itemId, out var cached)) return ValueTask.FromResult(cached);
            var archive = provider.Archive;
            PathsIndex(archive);
            var found = paths.TryGetValue(itemId, out var path) ? Read(archive, path) : null;
            cache[itemId] = found;
            return ValueTask.FromResult(found);
        }
    }

    private static ExperienceItemDefinition? Read(PvfArchive archive, string path)
    {
        var document = PvfDocument.Open(archive, path).AtRoot();
        if (!document.TryRead(Fields.IncreaseStatusType, out var increase)
            || increase.Count == 0 || !increase[0].IsString)
            return null;

        var name = archive.ResolveString(increase[0].Value);
        var kind = name.Equals("[expUp]", StringComparison.OrdinalIgnoreCase)
            ? ExperienceItemGrantKind.Fixed
            : name.Equals("[expPercentUp]", StringComparison.OrdinalIgnoreCase)
                ? ExperienceItemGrantKind.PercentOfCurrentLevel
                : (ExperienceItemGrantKind?)null;
        if (kind is null) return null;

        var readable = increase.Count == 2
            && increase[1].Kind == PvfTokenKind.Int32
            && increase[1].Value > 0;
        var value = readable ? checked((uint)increase[1].Value) : 0;

        try
        {
            readable &= document.TryRead(Fields.StackableType, out var stackable)
                && stackable.Count == 2
                && stackable[0].IsString
                && archive.ResolveString(stackable[0].Value).Equals("[etc]", StringComparison.OrdinalIgnoreCase)
                && stackable[1].Kind == PvfTokenKind.Int32
                && stackable[1].Value == 0;

            var minimum = document.TryRead(Fields.MinimumLevel, out var declaredMinimum)
                ? declaredMinimum : 1;
            var maximum = document.TryRead(Fields.MaximumLevel, out var declaredMaximum)
                ? declaredMaximum : int.MaxValue;
            readable &= minimum >= 0 && maximum >= minimum;
            return new(kind.Value, value, minimum, maximum, readable);
        }
        catch (InvalidDataException)
        {
            return new(kind.Value, value, 0, 0, false);
        }
    }
}
