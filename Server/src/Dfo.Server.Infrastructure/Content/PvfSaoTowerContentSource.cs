using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfSaoTowerContentSource(PvfArchiveProvider provider) : ISaoTowerContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private readonly Lazy<SaoTowerCatalog> catalog = new(() => Read(provider));
    public SaoTowerCatalog Load() => catalog.Value;
    public string Name => "sao-tower";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Load().Floors.Count);

    private static SaoTowerCatalog Read(PvfArchiveProvider provider)
    {
        var archive = provider.Archive;
        var root = PvfDocument.Open(archive, "etc/sao_towerofdespair.etc");
        var maximum = root.Read(PvfField.Int32("[top layer]", minimum: 1, maximum: 100));
        var rows = root.Read(PvfField.Int32List("[each layer matching dungeon]"));
        if (rows.Count != maximum * 3) throw new InvalidDataException("Invalid SAO floor table.");
        var listed = PvfListFile.Read(archive, "dungeon/dungeon.lst").ToDictionary(x => x.Id, x => x.RelativePath);
        var floors = new List<SaoTowerFloor>();
        for (var i = 0; i < rows.Count; i += 3)
        {
            if (rows[i] != i / 3 + 1 || !listed.TryGetValue(rows[i + 1], out var path))
                throw new InvalidDataException("Invalid SAO dungeon binding.");
            var dgn = PvfDocument.Open(archive, "dungeon/" + path);
            var minimum = dgn.Read(PvfField.Int32("[minimum required level]", minimum: 1, maximum: 255));
            var values = dgn.Read(PvfField.Int32List("[required item]"));
            if (values.Count == 0 || values.Count % 3 != 0) throw new InvalidDataException("Invalid SAO tickets.");
            var tickets = new List<DungeonRequiredItem>();
            for (var j = 0; j < values.Count; j += 3)
            {
                if (values[j] <= 0 || values[j + 1] <= 0 || values[j + 2] != 1)
                    throw new InvalidDataException("Invalid SAO ticket cost.");
                tickets.Add(new(values[j], values[j + 1], true) { AllowSharedMaterial = false });
            }
            floors.Add(new(rows[i], rows[i + 1], minimum, tickets.AsReadOnly()));
        }
        if (floors.Select(x => x.DungeonId).Distinct().Count() != floors.Count)
            throw new InvalidDataException("Duplicate SAO dungeon binding.");
        var rewards = PvfDocument.Open(archive, "etc/sao_clearreward.etc");
        return new(floors.AsReadOnly(), rewards.Read(PvfField.Int32("[SAO first clear reward]", minimum: 1)));
    }
}
