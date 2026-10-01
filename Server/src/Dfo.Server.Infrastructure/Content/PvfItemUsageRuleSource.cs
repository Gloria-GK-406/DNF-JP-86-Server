using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfItemUsageRuleSource(PvfArchiveProvider provider) : IItemUsageRuleSource
{
    private static class Fields
    {
        internal static readonly PvfField<int> TotalUsableCountField = PvfField.Int32("[total usable count]", optional: true);
        internal static readonly PvfField<int> CoolTimeField = PvfField.Int32("[cool time]", optional: true);
    }

    private readonly Lock gate = new();
    private readonly Dictionary<int, ItemUsageRule?> cache = [];
    private IReadOnlyDictionary<int, string>? paths;
    public ValueTask<ItemUsageRule?> GetAsync(int itemId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (cache.TryGetValue(itemId, out var found)) return ValueTask.FromResult(found);
            var archive = provider.Archive;
            paths ??= PvfListFile.Read(archive, "stackable/stackable.lst")
                .ToDictionary(x => x.Id, x => "stackable/" + x.RelativePath);
            if (!paths.TryGetValue(itemId, out var path)) return ValueTask.FromResult<ItemUsageRule?>(null);
            var reader = archive.OpenScript(path);
            var unreadable = false;
            var limitTag = archive.GetTag(Fields.TotalUsableCountField);
            var limit = limitTag.IsResolved && reader.TryMovePast(limitTag)
                ? PvfItemUseRuleDefinitionSource.FirstInt32(archive, ref reader, Fields.TotalUsableCountField, ref unreadable) : -1;
            var cooldown = PvfItemUseRuleDefinitionSource.FirstInt32(archive, ref reader,
                Fields.CoolTimeField, ref unreadable);
            found = new(limit, cooldown, !unreadable && limit >= -1 && cooldown >= 0);
            cache[itemId] = found;
            return ValueTask.FromResult<ItemUsageRule?>(found);
        }
    }
}
