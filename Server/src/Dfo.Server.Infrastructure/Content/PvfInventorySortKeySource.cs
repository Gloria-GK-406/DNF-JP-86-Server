using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfInventorySortKeySource(PvfArchiveProvider provider,
    IEquipmentSlotDefinitionSource slots) : IInventorySortKeySource
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, InventorySortKeys> cache = [];
    private Dictionary<int, string>? paths;

    public async ValueTask<InventorySortKeys> GetAsync(int itemId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        InventorySortKeys result;
        lock (gate)
        {
            if (cache.TryGetValue(itemId, out var cached)) return cached;
            paths ??= new[] { "equipment", "stackable" }.SelectMany(root =>
                PvfListFile.Read(provider.Archive, $"{root}/{root}.lst")
                    .Select(row => new KeyValuePair<int, string>(row.Id, root + "/" + row.RelativePath)))
                .ToDictionary();
            if (!paths.TryGetValue(itemId, out var path))
                throw new InvalidDataException($"No PVF sorting metadata for item {itemId}.");
            var sections = PvfNpcShopDefinitionSource.Sections(provider.Archive, path);
            int Number(string name) => sections.FirstOrDefault(section => section.Name == name)?.Values
                .FirstOrDefault(token => token.Kind == PvfTokenKind.Int32).Value ?? 0;
            var type = sections.FirstOrDefault(section => section.Name == "[stackable type]")?.Values
                .FirstOrDefault(token => token.IsString);
            result = new(Number("[rarity]"), Number("[minimum level]"), 0,
                type is { IsString: true } token ? provider.Archive.ResolveString(token.Value) : "");
        }
        var slot = await slots.GetAsync(itemId, ct).ConfigureAwait(false);
        result = result with { Part = slot?.Index ?? int.MaxValue };
        lock (gate) cache[itemId] = result;
        return result;
    }
}
