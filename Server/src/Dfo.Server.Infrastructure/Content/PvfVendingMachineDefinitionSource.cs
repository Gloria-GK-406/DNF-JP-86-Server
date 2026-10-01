using Dfo.Pvf.Schema;
using System.Collections.Frozen;
using System.Collections.Immutable;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfVendingMachineDefinitionSource(PvfArchiveProvider provider) : IVendingMachineDefinitionSource
{
    private static class Fields
    {
        internal static readonly PvfField<bool> ItemGroupField = PvfField.Marker("[item group]", optional: true);
        internal static readonly PvfField<int> GroupNumField = PvfField.Int32("[group num]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> MaterialField = PvfField.Int32List("[material]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> OutputField = PvfField.Int32List("[output]", optional: true);
        internal static readonly PvfField<bool> CloseOutputField = PvfField.Marker("[/output]", optional: true);
        internal static readonly PvfField<bool> CloseItemGroupField = PvfField.Marker("[/item group]", optional: true);
    }

    private FrozenDictionary<(int, int), VendingMachineDefinition>? definitions;
    private readonly Lock gate = new();
    public int Initialize()
    {
        lock (gate)
        {
            if (definitions is not null) return definitions.Count;
            // Only captured ordinary (1) and advanced (3) black-diamond machines are enabled.
            // Event machines still require their own policy and evidence.
            var parsed = new Dictionary<(int, int), VendingMachineDefinition>();
            var rows = PvfListFile.Read(provider.Archive, "etc/vendingmachine.lst");
            foreach (var machineId in new[] { 1, 3 })
            {
                var row = rows.Single(x => x.Id == machineId);
                var expectedCoin = machineId == 1 ? 7454 : 2749933;
                var sections = PvfNpcShopDefinitionSource.Sections(provider.Archive, "etc/" + row.RelativePath);
                int group = 0, material = 0, count = 0;
                ImmutableArray<VendingReward> rewards = [];
                foreach (var section in sections)
                {
                    int[] Numbers() => section.Values.Select(t => t.Kind == PvfTokenKind.Int32 ? t.Value
                        : throw new InvalidDataException($"Noninteger vending field {section.Name}.")).ToArray();
                    switch (section.Name)
                    {
                        case var fieldName when (Fields.ItemGroupField.Matches(fieldName)): group = material = count = 0; rewards = []; break;
                        case var fieldName when (Fields.GroupNumField.Matches(fieldName)): group = Numbers().Single(); break;
                        case var fieldName when (Fields.MaterialField.Matches(fieldName)):
                            var values = Numbers();
                            if (values.Length != 2) throw new InvalidDataException("Invalid vending material.");
                            material = values[0]; count = values[1]; break;
                        case var fieldName when (Fields.OutputField.Matches(fieldName)): rewards = ParseRewards(Numbers()); break;
                        case var fieldName when (Fields.CloseOutputField.Matches(fieldName)): break;
                        case var fieldName when (Fields.CloseItemGroupField.Matches(fieldName)):
                            if (group <= 0 || material != expectedCoin || count != 1 || rewards.IsEmpty)
                                throw new InvalidDataException("Unsupported black diamond machine group.");
                            parsed.Add((machineId, group), new(machineId, group, material, count, rewards)); break;
                        default: throw new InvalidDataException($"Unsupported vending section {section.Name}.");
                    }
                }
            }
            if (parsed.Count == 0) throw new InvalidDataException("Missing ordinary vending groups.");
            definitions = parsed.ToFrozenDictionary();
            return definitions.Count;
        }
    }
    internal static ImmutableArray<VendingReward> ParseRewards(int[] values)
    {
        if (values.Length == 0 || values.Length % 4 != 0) throw new InvalidDataException("Invalid vending output rows.");
        var result = ImmutableArray.CreateBuilder<VendingReward>();
        long total = 0;
        for (var i = 0; i < values.Length; i += 4)
        {
            if (values[i] <= 0 || values[i + 1] < 0 || values[i + 2] <= 0 || values[i + 3] is < 0 or > 1)
                throw new InvalidDataException("Invalid vending output value.");
            total = checked(total + values[i + 1]);
            result.Add(new(new(values[i], values[i + 2]), total, values[i + 3] == 1));
        }
        if (total <= 0) throw new InvalidDataException("Empty vending probability mass.");
        return result.ToImmutable();
    }
    public VendingMachineDefinition GetRequired(int machineId, int groupId) =>
        (definitions ?? throw new InvalidOperationException("Vending cache is not initialized."))
        .TryGetValue((machineId, groupId), out var definition) ? definition
        : throw new OpeningRejectedException(OpeningRejection.InvalidRequest, $"Unknown vending machine/group {machineId}/{groupId}.");
}
