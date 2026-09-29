using Dfo.Pvf;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfItemUsageRuleSource(PvfArchiveProvider provider) : IItemUsageRuleSource
{
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
            var limitTag = archive.GetTag("[total usable count]");
            var limit = limitTag.IsResolved && reader.TryMovePast(limitTag)
                ? PvfItemUseRuleDefinitionSource.FirstInt32(ref reader, limitTag, ref unreadable) : -1;
            var cooldown = PvfItemUseRuleDefinitionSource.FirstInt32(ref reader,
                archive.GetTag("[cool time]"), ref unreadable);
            found = new(limit, cooldown, !unreadable && limit >= -1 && cooldown >= 0);
            cache[itemId] = found;
            return ValueTask.FromResult<ItemUsageRule?>(found);
        }
    }
}
