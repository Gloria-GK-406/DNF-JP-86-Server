using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfDespairTowerContentSource(PvfArchiveProvider provider) : IDespairTowerContentSource
{
    private DespairTowerCatalog? cached;
    private readonly Lock gate = new();
    public DespairTowerCatalog Load()
    {
        lock (gate)
        {
            if (cached is not null) return cached;
            var archive = provider.Archive;
            var root = PvfDocument.Open(archive, "etc/towerofdespair.etc");
            var minimum = root.Read(PvfField.Int32("[min Level]", minimum: 1, maximum: 255));
            var maximum = root.Read(PvfField.Int32("[top layer]", minimum: 1, maximum: 100));
            var daily = root.Read(PvfField.Int32("[enterable count]", minimum: 1));
            var rows = root.Read(PvfField.Int32List("[each layer matching dungeon]"));
            if (rows.Count != maximum * 3) throw new InvalidDataException("Invalid despair floor table.");
            var listed = PvfListFile.Read(archive, "dungeon/dungeon.lst").ToDictionary(x => x.Id, x => x.RelativePath);
            var floors = new List<DespairTowerFloor>();
            for (var i = 0; i < rows.Count; i += 3)
            {
                if (rows[i] != i / 3 + 1 || rows[i + 1] <= 0 || !listed.TryGetValue(rows[i + 1], out var path))
                    throw new InvalidDataException("Invalid despair floor binding.");
                var dgn = PvfDocument.Open(archive, "dungeon/" + path);
                var values = dgn.Read(PvfField.Int32List("[required item]"));
                if (values.Count == 0 || values.Count % 3 != 0) throw new InvalidDataException("Invalid despair invitation declaration.");
                var tickets = new List<DungeonRequiredItem>();
                for (var j = 0; j < values.Count; j += 3)
                {
                    if (values[j] <= 0 || values[j + 1] <= 0 || values[j + 2] != 1) throw new InvalidDataException("Invalid despair invitation cost.");
                    tickets.Add(new(values[j], values[j + 1], true) { AllowSharedMaterial = false });
                }
                floors.Add(new(rows[i], rows[i + 1], rows[i + 2], tickets.AsReadOnly()));
            }
            if (floors.Select(x => x.DungeonId).Distinct().Count() != floors.Count) throw new InvalidDataException("Duplicate despair dungeon binding.");
            return cached = new(minimum, daily, floors.AsReadOnly());
        }
    }
}
