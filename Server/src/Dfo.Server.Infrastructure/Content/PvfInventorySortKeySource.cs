using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfInventorySortKeySource(PvfArchiveProvider provider,
    IEquipmentSlotDefinitionSource slots) : IInventorySortKeySource
{
    private static class Fields
    {
        internal static readonly PvfField<int> RarityField = PvfField.Int32("[rarity]", optional: true);
        internal static readonly PvfField<int> MinimumLevelField = PvfField.Int32("[minimum level]", optional: true);
        internal static readonly PvfField<string> StackableTypeField = PvfField.Custom<string>("[stackable type]",
            (archive, values, path) =>
            {
                if (values.Count == 0 || !values[0].IsString || values.Skip(1).Any(token => token.Kind != PvfTokenKind.Int32))
                    throw new InvalidDataException($"PVF '{path}', field '[stackable type]': expected type string and optional Int32 operands.");
                return archive.ResolveString(values[0].Value);
            }, optional: true);
    }

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
            var document = PvfDocument.Open(provider.Archive, path).AtRoot();
            int Number(PvfField<int> field) => document.TryRead(field, out var value) ? value : 0;
            var type = document.TryRead(Fields.StackableTypeField, out var stackableType) ? stackableType : "";
            result = new(Number(Fields.RarityField), Number(Fields.MinimumLevelField), 0, type);
        }
        var slot = await slots.GetAsync(itemId, ct).ConfigureAwait(false);
        result = result with { Part = slot?.Index ?? int.MaxValue };
        lock (gate) cache[itemId] = result;
        return result;
    }
}
