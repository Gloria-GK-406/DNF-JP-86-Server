using System.Collections.Frozen;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentQualityDefinitionSource(PvfArchiveProvider provider) : IEquipmentQualityDefinitionSource
{
    private static readonly PvfField<IReadOnlyList<PvfToken>> Type = PvfField.Tokens("[stackable type]", optional: true);
    private static readonly PvfField<IReadOnlyList<string>> AllowedTypes = PvfField.Strings("[usable equip type]", optional: true);
    private readonly Lock gate = new();
    private readonly Dictionary<int, EquipmentQualityMaterialDefinition?> cache = [];
    private Dictionary<int, string>? paths;
    public ValueTask<EquipmentQualityMaterialDefinition?> FindAsync(int itemId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (cache.TryGetValue(itemId, out var existing)) return ValueTask.FromResult(existing);
            var archive = provider.Archive;
            paths ??= PvfListFile.Read(archive, "stackable/stackable.lst").ToDictionary(x => x.Id, x => "stackable/" + x.RelativePath);
            EquipmentQualityMaterialDefinition? found = null;
            if (paths.TryGetValue(itemId, out var path))
            {
                var document = PvfDocument.Open(archive, path).AtRoot();
                var values = document.Read(Type);
                if (values.Count > 0 && values[0].IsString)
                {
                    var type = archive.ResolveString(values[0].Value).Trim().Trim('[', ']');
                    if (type is "kaleido" or "kaleido box" or "gold kaleido" or "gold kaleido box" || itemId is 15 or 897)
                    {
                        var allowed = document.TryRead(AllowedTypes, out var list) && list.Count > 0
                            ? list.Select(x => x.Trim().Trim('[', ']')).ToFrozenSet(StringComparer.Ordinal) : null;
                        found = new(type is "gold kaleido" or "gold kaleido box", allowed);
                    }
                }
            }
            cache[itemId] = found;
            return ValueTask.FromResult(found);
        }
    }
}
