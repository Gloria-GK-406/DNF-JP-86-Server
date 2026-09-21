using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentRequirementDefinitionSource(PvfArchiveProvider provider) : IEquipmentRequirementDefinitionSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, int?> cache = [];
    private Dictionary<int, string>? paths;
    public ValueTask<int?> MinimumLevelAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (cache.TryGetValue(itemId, out var cached)) return ValueTask.FromResult(cached);
            paths ??= PvfListFile.Read(provider.Archive, "equipment/equipment.lst")
                .ToDictionary(row => row.Id, row => "equipment/" + row.RelativePath);
            int? level = null;
            if (paths.TryGetValue(itemId, out var path))
            {
                var sections = PvfNpcShopDefinitionSource.Sections(provider.Archive, path)
                    .Where(section => section.Name == "[minimum level]").ToArray();
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
