using System.Collections.Frozen;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfEquipmentMagicSealGradeDefinitionSource(PvfArchiveProvider provider,
    IEquipmentMagicSealContentSource equipment) : IEquipmentMagicSealGradeDefinitionSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private static readonly PvfField<IReadOnlyList<int>> Grades = PvfField.Int32List("[postfix grade modification]");
    private static readonly PvfField<IReadOnlyList<int>> Exclusions = PvfField.Int32List("[unable to modify postfix grade]");
    private readonly Lock gate = new();
    private FrozenDictionary<int, EquipmentMagicSealGradeDefinition>? definitions;

    public string Name => "equipment-magic-seal-grades";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            DefinitionsIndex();
            return ValueTask.FromResult(definitions.Count);
        }
    }

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(definitions))]
    private void DefinitionsIndex()
    {
        definitions ??= Read();
    }

    public async ValueTask<EquipmentMagicSealGradeDefinition?> FindAsync(int equipmentId, CancellationToken ct)
    {
        var item = await equipment.GetEquipmentAsync(equipmentId, ct).ConfigureAwait(false);
        if (item is null) return null;
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            DefinitionsIndex();
            return definitions.GetValueOrDefault(item.Rarity);
        }
    }

    private FrozenDictionary<int, EquipmentMagicSealGradeDefinition> Read()
    {
        const string path = "etc/randomoption/randomizedoptionoverall2.etc";
        var archive = provider.Archive;
        var document = PvfDocument.Open(archive, path).AtRoot();
        var rows = document.Read(Grades);
        var exclusions = document.Read(Exclusions);
        if (rows.Count == 0 || rows.Count % 5 != 0 || exclusions.Any(x => x is < 1 or > 255))
            throw new InvalidDataException($"PVF '{path}': invalid magic-seal reset rows.");
        var blocked = exclusions.Select(x => (byte)x).ToFrozenSet();
        var known = PvfListFile.Read(archive, "etc/randomoption/randomoption.lst")
            .Where(x => x.Id is > 0 and <= 255).Select(x => (byte)x.Id).ToFrozenSet();
        var byRarity = new Dictionary<int, List<MagicSealGradeBand>>();
        var seen = new HashSet<(int, int)>();
        for (var i = 0; i < rows.Count; i += 5)
        {
            var rarity = rows[i];
            if (rarity < 0 || rows[i + 1] < 1 || !seen.Add((rarity, rows[i + 1]))
                || rows[i + 2] < 0 || rows[i + 3] > 101 || rows[i + 2] >= rows[i + 3] || rows[i + 4] < 0)
                throw new InvalidDataException($"PVF '{path}': invalid or duplicate reset band.");
            if (!byRarity.TryGetValue(rarity, out var bands)) byRarity[rarity] = bands = [];
            bands.Add(new(rows[i + 2], rows[i + 3], rows[i + 4]));
        }
        return byRarity.ToFrozenDictionary(x => x.Key,
            x => new EquipmentMagicSealGradeDefinition(x.Value.AsReadOnly(), known, blocked));
    }
}
