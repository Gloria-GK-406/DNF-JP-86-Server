using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfItemSaleDefinitionSource(PvfArchiveProvider provider) : IItemSaleDefinitionSource
{
    private static class Fields
    {
        internal static readonly PvfField<bool> BoosterInfoField = PvfField.Marker("[booster info]", optional: true);
        internal static readonly PvfField<bool> CloseBoosterInfoField = PvfField.Marker("[/booster info]", optional: true);
        internal static readonly PvfField<int> ValueField = PvfField.Int32("[value]", optional: true);
        internal static readonly PvfField<int> DurabilityField = PvfField.Int32("[durability]", optional: true);
        internal static readonly PvfField<IReadOnlyList<string>> ImpossibleContentsField = PvfField.Strings("[impossible contents]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> NotSellField = PvfField.Tokens("[not sell]", optional: true);
        internal static readonly PvfField<IReadOnlyList<PvfToken>> CannotSellField = PvfField.Tokens("[cannot sell]", optional: true);
    }

    private readonly Lock gate = new();
    private readonly Dictionary<int, ItemSaleDefinition?> cache = [];
    private Dictionary<int, string>? equipment;
    private Dictionary<int, string>? stackables;

    public ValueTask<ItemSaleDefinition?> GetAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!cache.TryGetValue(itemId, out var definition))
                cache[itemId] = definition = Read(itemId);
            return ValueTask.FromResult(definition);
        }
    }

    private ItemSaleDefinition? Read(int itemId)
    {
        var archive = provider.Archive;
        equipment ??= PvfListFile.Read(archive, "equipment/equipment.lst").ToDictionary(x => x.Id, x => x.RelativePath);
        stackables ??= PvfListFile.Read(archive, "stackable/stackable.lst").ToDictionary(x => x.Id, x => x.RelativePath);
        var isEquipment = equipment.TryGetValue(itemId, out var relative);
        if (!isEquipment && !stackables.TryGetValue(itemId, out relative))
            return null;
        var path = (isEquipment ? "equipment/" : "stackable/") + relative;
        var sections = PvfNpcShopDefinitionSource.Sections(archive, path);
        if (sections.Count == 0)
            return null;
        var values = new List<int>();
        var durabilityValues = new List<int>();
        var permitted = true;
        var boosterDepth = 0;
        var restrictions = 0;
        for(var index=0;index<sections.Count;index++)
        {
            var section=sections[index];
            if (section.Name == Fields.BoosterInfoField.Name) { boosterDepth++; continue; }
            if (section.Name == Fields.CloseBoosterInfoField.Name) { if (boosterDepth-- <= 0) permitted = false; continue; }
            if (boosterDepth != 0) continue;
            if (section.Name == Fields.ValueField.Name)
            {
                if (section.Values.Count != 1 || section.Values[0].Kind != PvfTokenKind.Int32)
                    permitted = false;
                else
                    values.Add(section.Values[0].Value);
            }
            if (section.Name == Fields.DurabilityField.Name)
            {
                if (section.Values.Count != 1 || section.Values[0].Kind != PvfTokenKind.Int32)
                    permitted = false;
                else
                    durabilityValues.Add(section.Values[0].Value);
            }
            if(section.Name == Fields.ImpossibleContentsField.Name)
            {
                // "gift" forbids gifting, not NPC sale. Other restrictions remain closed until interpreted.
                permitted &= ++restrictions == 1 && section.Values.Count == 1 && section.Values[0].IsString
                    && archive.ResolveString(section.Values[0].Value).Trim().Trim('`').Trim().Trim('[',']')
                        .Equals("gift",StringComparison.OrdinalIgnoreCase)
                    && index+1<sections.Count && sections[index+1].Name == "[/impossible contents]"
                    && sections[index+1].Values.Count == 0;
            }
            if (section.Name == Fields.NotSellField.Name || section.Name == Fields.CannotSellField.Name)
                permitted = false;
        }
        permitted &= boosterDepth == 0 && values.Count == 1 && values[0] >= 0;
        permitted &= durabilityValues.Count <= 1
            && (durabilityValues.Count == 0 || durabilityValues[0] is >= 0 and <= ushort.MaxValue);
        var rate = 200;
        if (isEquipment)
        {
            var table = PvfNpcShopDefinitionSource.Sections(archive, "equipment/pricetable.tbl");
            // The archive's first section is a localized tag followed by the three sale rates.
            if (table.Count == 0 || table[0].Values.Count != 3
                || table[0].Values.Any(x => x.Kind != PvfTokenKind.Int32 || x.Value < 0))
                return null;
            rate = table[0].Values[0].Value;
        }
        return new(values.Count == 1 ? values[0] : 0, rate, permitted, isEquipment,
            durabilityValues.Count == 1 ? (ushort)Math.Clamp(durabilityValues[0], 0, ushort.MaxValue) : (ushort)0);
    }
}
