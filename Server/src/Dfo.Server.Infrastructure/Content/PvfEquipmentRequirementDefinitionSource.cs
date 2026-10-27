using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentRequirementDefinitionSource(PvfArchiveProvider provider) : IEquipmentRequirementDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<int> MinimumLevelField = PvfField.Int32("[minimum level]", optional: true);
    }

    private readonly Lock gate = new();
    private readonly Dictionary<int, int?> cache = [];
    private IReadOnlyDictionary<int, string>? paths;
    public string Name => "equipment-requirement-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            PathsIndex();
            return ValueTask.FromResult(paths.Count);
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(paths))]
    private void PathsIndex()
    {
        paths ??= PvfListFile.Paths(provider.Archive, "equipment/equipment.lst", prefix: "equipment/");
    }

    public ValueTask<int?> MinimumLevelAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (cache.TryGetValue(itemId, out var cached)) return ValueTask.FromResult(cached);
            PathsIndex();
            int? level = null;
            if (paths.TryGetValue(itemId, out var path))
            {
                var sections = PvfNpcShopDefinitionSource.Sections(provider.Archive, path)
                    .Where(section => section.Name == Fields.MinimumLevelField.Name).ToArray();
                if (sections.Length == 0) level = 0;
                else if (sections.Length == 1 && sections[0].Values.Count == 1
                    && sections[0].Values[0].Kind == PvfTokenKind.Int32 && sections[0].Values[0].Value >= 0)
                    level = sections[0].Values[0].Value;
            }
            cache[itemId] = level;
            return ValueTask.FromResult(level);
        }
    }
}
