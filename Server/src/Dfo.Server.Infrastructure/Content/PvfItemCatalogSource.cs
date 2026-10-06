using Dfo.Server.Data.Inventory;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfItemCatalogSource(PvfArchiveProvider provider,
    ILogger<PvfItemCatalogSource> logger) : IItemCatalogSource
{
    private static class Fields
    {
        internal static readonly PvfField<string> Name = PvfField.String("[name]", optional: true);
        internal static readonly PvfField<int> Level = PvfField.Int32("[minimum level]", optional: true, minimum: 0);
        internal static readonly PvfField<int> Rarity = PvfField.Int32("[rarity]", optional: true, minimum: 0, maximum: 6);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> EquipmentType = PvfField.Tokens("[equipment type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> StackableType = PvfField.Tokens("[stackable type]", optional: true);
    }

    private readonly Lock gate = new();
    private readonly SemaphoreSlim buildGate = new(1);
    private Dictionary<int, (string Path, bool Equipment)>? paths;
    private readonly Dictionary<int, ItemCatalogEntry?> cache = [];
    private ItemCatalogEntry[]? searchable;

    public ValueTask<ItemCatalogEntry?> FindAsync(int itemId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate)
        {
            LoadPaths();
            if (cache.TryGetValue(itemId, out var cached)) return ValueTask.FromResult(cached);
            if (!paths!.TryGetValue(itemId, out var location)) return ValueTask.FromResult<ItemCatalogEntry?>(null);
            ItemCatalogEntry? entry;
            try { entry = Read(itemId, location.Path, location.Equipment); }
            catch (InvalidDataException error)
            {
                logger.LogWarning(error, "Item catalog omits malformed template {ItemId} at {Path}.", itemId, location.Path);
                entry = null;
            }
            cache.Add(itemId, entry);
            return ValueTask.FromResult(entry);
        }
    }

    public async ValueTask<IReadOnlyList<ItemCatalogEntry>> SearchAsync(ItemCatalogFilter filter, CancellationToken token)
    {
        if (filter.ItemId is { } id)
        {
            var item = await FindAsync(id, token);
            return item is not null && Matches(item, filter) ? [item] : [];
        }
        await buildGate.WaitAsync(token);
        try
        {
            if (searchable is null)
            {
                int[] ids;
                lock (gate) { LoadPaths(); ids = paths!.Keys.Order().ToArray(); }
                // Retain display metadata; the catalog domain applies purpose admission before pagination.
                searchable = await Task.Run(async () =>
                {
                    var rows = new List<ItemCatalogEntry>();
                    foreach (var itemId in ids)
                        if (await FindAsync(itemId, token) is { } entry) rows.Add(entry);
                    return rows.ToArray();
                }, token);
            }
            return searchable.Where(item => Matches(item, filter)).ToArray();
        }
        finally { buildGate.Release(); }
    }

    private void LoadPaths()
    {
        if (paths is not null) return;
        var result = new Dictionary<int, (string, bool)>();
        foreach (var row in PvfListFile.Read(provider.Archive, "stackable/stackable.lst"))
            result.TryAdd(row.Id, ("stackable/" + row.RelativePath, false));
        foreach (var row in PvfListFile.Read(provider.Archive, "equipment/equipment.lst"))
            result.TryAdd(row.Id, ("equipment/" + row.RelativePath, true));
        paths = result;
    }

    private ItemCatalogEntry? Read(int itemId, string path, bool equipment)
    {
        var archive = provider.Archive;
        var document = PvfDocument.Open(archive, path).AtRoot();
        var type = TypeName(archive, document, equipment ? Fields.EquipmentType : Fields.StackableType, path);
        var kind = equipment ? (byte)1 : PvfEntryLifecycleContentSource.ResolveStackableItemKind(itemId, type);
        var category = equipment ? "equipment" : kind switch { 3 or 10 or 11 => "material", 4 => "quest", 2 => "consumable", _ => "other" };
        var name = document.Read(Fields.Name);
        return new(itemId, string.IsNullOrWhiteSpace(name) ? $"物品 {itemId}" : name, category,
            equipment ? type : null, document.TryRead(Fields.Level, out var level) ? level : null,
            document.TryRead(Fields.Rarity, out var rarity) ? rarity : null);
    }

    private static string? TypeName(PvfArchive archive, PvfDocument document,
        PvfField<IReadOnlyList<PvfToken>> field, string path)
    {
        if (!document.TryRead(field, out var values)) return null;
        if (values.Count == 0 || !values[0].IsString || values.Skip(1).Any(value => value.Kind != PvfTokenKind.Int32))
            throw new InvalidDataException($"PVF '{path}', field '{field}': invalid type declaration.");
        return archive.ResolveString(values[0].Value).Trim().Trim('`', '[', ']').ToLowerInvariant();
    }

    private static bool Matches(ItemCatalogEntry item, ItemCatalogFilter filter) =>
        (string.IsNullOrWhiteSpace(filter.Q) || item.Name.Contains(filter.Q.Trim(), StringComparison.OrdinalIgnoreCase))
        && (filter.Category is null || item.Category == filter.Category)
        && (filter.EquipmentType is null || string.Equals(item.EquipmentType, filter.EquipmentType, StringComparison.OrdinalIgnoreCase))
        && (filter.MinimumLevel is null || item.Level >= filter.MinimumLevel)
        && (filter.MaximumLevel is null || item.Level <= filter.MaximumLevel)
        && (filter.Rarity is null || item.Rarity == filter.Rarity);
}
