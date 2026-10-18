using Dfo.Pvf.Index;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfMarriageHousingContentSource(PvfArchiveProvider provider) : IMarriageHousingContentSource
{
    private readonly Lazy<MarriageHousingContent> content = new(() => Read(provider));
    public MarriageHousingContent Load() => content.Value;
    private static MarriageHousingContent Read(PvfArchiveProvider provider)
    {
        var archive = provider.Archive;
        var paths = PvfListFile.Read(archive, "stackable/stackable.lst").ToDictionary(x => x.Id, x => "stackable/" + x.RelativePath);
        var layout = PvfDocument.Open(archive, "decoration/coupleroomiteminfo.etc");
        var sold = PvfDocument.Open(archive, "itemshop/chn_mago.shp").Read(PvfField.Int32List("[item list]")).Where(x => x > 0).ToHashSet();
        var furniture = new Dictionary<int, HousingFurnitureRule>();
        foreach (var entry in layout.ReadAll(PvfField.Tokens("[coupleroom item]")))
        {
            var id = entry[0].Value; var count = entry[1].Value;
            if (!sold.Contains(id) || count is < 1 or > 28 || entry.Count != 4 + count * 5) throw new InvalidDataException("Invalid housing decoration layout.");
            var slots = Enumerable.Range(0, count).Select(i => entry[2 + i * 5].Value).ToHashSet();
            if (slots.Any(x => x is < 0 or > 27)) throw new InvalidDataException("Invalid housing anchor.");
            var cost = PvfDocument.Open(archive, paths[id]).Read(PvfField.Int32List("[need material]"));
            if (cost.Count != 2 || cost[0] != 10002606 || cost[1] <= 0) throw new InvalidDataException("Invalid furniture point price.");
            furniture.Add(id, new(id, cost[1], slots, !slots.Contains(0) && !slots.Contains(1)));
        }
        if (!sold.SetEquals(furniture.Keys)) throw new InvalidDataException("Furniture shop and layout disagree.");
        var tree = PvfDocument.Open(archive, "decoration/coupletree.etc");
        var seeds = tree.ReadAll(PvfField.Int32List("[index]")).Select(x => x.ToArray()).ToArray();
        // The seed mapping is specified by [seed], not the order of the tree blocks.
        var seedValues = tree.Read(PvfField.Int32List("[seed]"));
        if (seedValues.Count != 6 || seeds.Length != 3) throw new InvalidDataException("Invalid tree kinds.");
        var seedMap = Enumerable.Range(0, 3).ToDictionary(i => seedValues[i * 2], i => seedValues[i * 2 + 1]);
        var water = tree.ReadAll(PvfField.Int32List("[grow point]"));
        var waters = water.First();
        if (water.Any(x => !x.SequenceEqual(waters)) || waters.Count % 2 != 0) throw new InvalidDataException("Unsupported per-kind watering rules.");
        var rewardRows = tree.ReadAll(PvfField.Int32List("[reward]"));
        var rewards = rewardRows.First();
        if (rewardRows.Any(x => !x.SequenceEqual(rewards)) || rewards.Count % 3 != 0) throw new InvalidDataException("Unsupported per-kind tree rewards.");
        if (tree.ReadAll(PvfField.Int32("[expire day]")).Any(x => x != 30) || tree.ReadAll(PvfField.Int32("[day water count]")).Any(x => x != 2)) throw new InvalidDataException("Unsupported tree lifetime/watering policy.");
        var wedding = PvfDocument.Open(archive, "etc/weddingsystem.etc");
        IReadOnlyList<HousingAttributeRule> Buffs(string tag)
        {
            var tokens = (tag == "[couple room buff]" ? layout : wedding).Read(PvfField.Tokens(tag)); var result = new List<HousingAttributeRule>();
            for (var i = 0; i < tokens.Count;)
            {
                var threshold = tokens[i++].Value; var n = tokens[i++].Value;
                var values = new Dictionary<string, int>();
                for (var k = 0; k < n; k++) { var key = archive.ResolveString(tokens[i++].Value); values.Add(key, tokens[i++].Value); }
                result.Add(new(threshold, values));
            }
            return result;
        }
        var sources = new HashSet<(int, int)>();
        foreach (var town in PvfListFile.Read(archive, "town/town.lst"))
        {
            if (town.Id == 101 || archive.FindFile("town/" + town.RelativePath) < 0) continue;
            foreach (var area in PvfDocument.Open(archive, "town/" + town.RelativePath).ReadAll(PvfField.Tokens("[area]")))
            {
                if (area.Count < 2 || !area[1].IsString) continue;
                var path = "map/" + archive.ResolveString(area[1].Value).Replace('\\', '/');
                if (!Path.GetFileName(path).StartsWith("gate_", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(path).Equals("Gate.map", StringComparison.OrdinalIgnoreCase)) continue;
                var regional = path[..(path.LastIndexOf('/') + 1)] + "(r)" + Path.GetFileName(path);
                if (archive.FindFile(regional) >= 0) path = regional;
                if (archive.FindFile(path) < 0) continue;
                var transits = PvfDocument.Open(archive, path).Read(PvfField.Int32List("[town movable area]", optional: true));
                if (transits is null) continue;
                if (transits.Count % 6 != 0) throw new InvalidDataException("Invalid housing door geometry.");
                for (var i = 0; i < transits.Count; i += 6)
                    if (transits[i + 4] == 101 && transits[i + 5] == 0) sources.Add((town.Id, area[0].Value));
            }
        }
        return new(furniture, seedMap, Enumerable.Range(0, waters.Count / 2).ToDictionary(i => waters[i * 2], i => waters[i * 2 + 1]),
            Enumerable.Range(0, rewards.Count / 3).Select(i => new HousingTreeReward(rewards[i * 3], rewards[i * 3 + 1], rewards[i * 3 + 2])).ToArray(),
            Buffs("[couple room buff]"), Buffs("[wedding ring buff]")) { EntrySources = sources };
    }
}
