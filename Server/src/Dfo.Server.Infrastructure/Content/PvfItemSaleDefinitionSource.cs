using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfItemSaleDefinitionSource(PvfArchiveProvider provider) : IItemSaleDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static class Fields
    {
        internal static readonly PvfField<int> Value = PvfField.Int32("[value]", minimum: 0, optional: true);
        internal static readonly PvfField<int> Durability = PvfField.Int32("[durability]", minimum: 0,
            maximum: ushort.MaxValue, optional: true);
    }

    private readonly Lock gate = new();
    private readonly Dictionary<int, ItemSaleDefinition?> cache = [];
    private IReadOnlyDictionary<int, string>? equipment;
    private IReadOnlyDictionary<int, string>? stackables;

    public string Name => "item-sale-index";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            var archive = provider.Archive;
            EquipmentIndex(archive);
            StackablesIndex(archive);
            return ValueTask.FromResult(equipment.Count + stackables.Count);
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(equipment))]
    private void EquipmentIndex(Dfo.Pvf.PvfArchive archive)
    {
        equipment ??= PvfListFile.Paths(archive, "equipment/equipment.lst");
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(stackables))]
    private void StackablesIndex(Dfo.Pvf.PvfArchive archive)
    {
        stackables ??= PvfListFile.Paths(archive, "stackable/stackable.lst");
    }

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
        EquipmentIndex(archive);
        StackablesIndex(archive);
        var isEquipment = equipment.TryGetValue(itemId, out var relative);
        if (!isEquipment && !stackables.TryGetValue(itemId, out relative))
            return null;
        var path = (isEquipment ? "equipment/" : "stackable/") + relative;
        var permitted = true;
        try
        {
            var document = PvfDocument.Open(archive, path).AtRoot();
            permitted = !document.Sections.Any(section => section.Name is "[not sell]" or "[cannot sell]"
                || (section.Name == "[impossible contents]" && section.Values.Any(token => token.IsString
                    && archive.ResolveString(token.Value).Trim().Trim('`').Trim().Trim('[', ']')
                        .Equals("sell", StringComparison.OrdinalIgnoreCase))));
            if (!document.TryRead(Fields.Value, out var value))
                return new(0, 0, permitted, isEquipment, PricingFailure: $"PVF '{path}': missing root [value].");
            document.TryRead(Fields.Durability, out var durability);
            var rate = 200;
            if (isEquipment)
            {
                var table = PvfDocument.Open(archive, "equipment/pricetable.tbl").Sections;
                if (table.Count == 0 || table[0].Values.Count != 3
                    || table[0].Values.Any(token => token.Kind != PvfTokenKind.Int32 || token.Value < 0))
                    return new(0, 0, permitted, true, PricingFailure: "PVF 'equipment/pricetable.tbl': invalid sale rates.");
                rate = table[0].Values[0].Value;
            }
            return new(value, rate, permitted, isEquipment, (ushort)durability);
        }
        catch (InvalidDataException exception)
        {
            // Only malformed pricing content becomes zero gold; storage/transaction failures still propagate.
            return new(0, 0, permitted, isEquipment, PricingFailure: $"PVF '{path}': {exception.Message}");
        }
    }
}
