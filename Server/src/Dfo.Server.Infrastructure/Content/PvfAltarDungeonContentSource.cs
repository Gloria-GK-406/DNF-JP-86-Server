using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfAltarDungeonContentSource(
    PvfArchiveProvider provider, IDungeonMapDefinitionSource maps) : IAltarDungeonContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, AltarDungeonDefinition> cached = [];

    private static readonly int[] Dungeons = [11006, 11007];

    public string Name => "altar-dungeons";

    public async ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        foreach (var dungeonId in Dungeons) await GetAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        return Dungeons.Length;
    }

    public async ValueTask<AltarDungeonDefinition> GetAsync(int dungeonId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Dungeons.Contains(dungeonId)) throw new ArgumentOutOfRangeException(nameof(dungeonId));
        lock (gate) if (cached.TryGetValue(dungeonId, out var found)) return found;
        var owned = await maps.ListForDungeonAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (!cached.TryGetValue(dungeonId, out var definition))
                cached[dungeonId] = definition = Read(provider.Archive, dungeonId, owned.Select(value => value.MapId));
            return definition;
        }
    }

    private static AltarDungeonDefinition Read(PvfArchive archive, int dungeonId, IEnumerable<int> ownedMaps)
    {
        var path = Resolve(archive, "dungeon", dungeonId);
        var dungeon = PvfDocument.Open(archive, path);
        var kind = (AltarDungeonKind)dungeon.Read(PvfField.Int32("[blood dungeon]", minimum: 1, maximum: 2));
        Require(kind == (dungeonId == 11006 ? AltarDungeonKind.Endless : AltarDungeonKind.Ultimate), path, "unexpected altar kind");
        var maximum = dungeon.Read(PvfField.Int32("[blood max round]", minimum: 1, maximum: byte.MaxValue));
        var coin = dungeon.TryRead(PvfField.Int32("[character coin limit]", optional: true, minimum: 0), out var characterCoin)
            ? characterCoin : dungeon.Read(PvfField.Int32("[coin limit]", minimum: 0));
        var mapIds = ownedMaps.Concat(ReadSpecifiedMaps(archive, dungeon, path)).Distinct().Order().ToArray();
        Require(mapIds.Length > 0, path, "no associated altar maps");
        var definitions = mapIds.Select(id => ReadMap(archive, id, kind)).ToArray();
        Require(definitions.Any(value => value.Rounds.Count > 0), path, "no altar rounds");
        return new(dungeonId, kind,
            dungeon.Read(PvfField.Int32("[minimum required level]", minimum: 1, maximum: byte.MaxValue)),
            dungeon.Read(PvfField.Int32("[basis level]", minimum: 1, maximum: byte.MaxValue)),
            maximum, coin, Array.AsReadOnly(definitions), ReadRewards(archive), ReadDailyEntries(archive, dungeonId))
        { AdmissionPricing = kind == AltarDungeonKind.Endless ? ReadAdmissionPricing(archive) : null };
    }

    private static IReadOnlyList<int> ReadSpecifiedMaps(PvfArchive archive, PvfDocument dungeon, string path)
    {
        var ids = new List<int>();
        foreach (var section in dungeon.ReadAll(PvfField.Tokens("[map specification]")))
        {
            var offset = 0;
            while (offset < section.Count)
            {
                Require(section[offset].IsString && archive.ResolveString(section[offset].Value) == "map", path, "invalid map specification row");
                Require(offset + 3 < section.Count, path, "truncated map specification");
                _ = Integer(section[offset + 1], path);
                _ = Integer(section[offset + 2], path);
                offset += 3;
                var first = offset;
                while (offset < section.Count && section[offset].Kind == PvfTokenKind.Int32)
                {
                    var id = Integer(section[offset++], path);
                    Require(id > 0, path, "invalid specified map identifier");
                    ids.Add(id);
                }
                Require(offset > first, path, "empty map candidate set");
            }
        }
        return ids.AsReadOnly();
    }

    private static AltarMapDefinition ReadMap(PvfArchive archive, int mapId, AltarDungeonKind kind)
    {
        var path = Resolve(archive, "map", mapId);
        var map = PvfDocument.Open(archive, path);
        var prefix = kind == AltarDungeonKind.Endless ? "blood" : "ultimate";
        var monsterField = PvfField.Int32List($"[{prefix} monster]", optional: true);
        var phaseField = PvfField.Tokens($"[{prefix} phase time]", optional: true);
        var rows = map.Read(monsterField) ?? [];
        var phaseRows = map.Read(phaseField) ?? [];
        Require(rows.Count % 10 == 0, path, "truncated altar monster template");
        var monsters = new List<AltarMonsterDefinition>();
        for (var index = 0; index < rows.Count; index += 10)
        {
            var row = rows.Skip(index).Take(10).ToArray();
            Require(row[0] > 0 && row.Skip(2).Take(3).All(value => value is >= 0 and <= ushort.MaxValue)
                && row[5] >= 0 && row[6] >= 0 && row[7] >= 0 && row[9] >= 0,
                path, "invalid altar monster template");
            monsters.Add(new(row[0], row[1], row[2], row[3], row[4], row[5], row[6], row[7], row[8], row[9]));
        }
        var width = kind == AltarDungeonKind.Endless ? 6 : 7;
        Require(phaseRows.Count % width == 0 && (rows.Count == 0) == (phaseRows.Count == 0), path, "invalid altar phase table");
        var phases = new List<AltarPhaseDefinition>();
        for (var index = 0; index < phaseRows.Count; index += width)
        {
            var round = Integer(phaseRows[index], path);
            var template = Integer(phaseRows[index + 1], path);
            var delay = Integer(phaseRows[index + 2], path);
            var scale = PvfField.Numbers("scale").Decode(archive, [phaseRows[index + 3]], path)[0];
            var flag = Integer(phaseRows[index + 4], path);
            var concurrent = Integer(phaseRows[index + 5], path);
            var difficulty = kind == AltarDungeonKind.Endless ? (byte)0 : Difficulty(archive, phaseRows[index + 6], path);
            Require(round >= 0 && template >= 0 && template < monsters.Count && delay >= 0
                && scale > 0 && concurrent is >= 0 and <= 10, path, "invalid altar phase values");
            phases.Add(new(round, template, delay, scale, flag, concurrent, difficulty));
        }
        var rounds = phases.GroupBy(value => value.Round).OrderBy(value => value.Key)
            .Select(group => new AltarRoundDefinition(group.Key, Array.AsReadOnly(group.ToArray()))).ToArray();
        Require(rounds.Select(value => value.Number).SequenceEqual(Enumerable.Range(0, rounds.Length)), path, "noncontiguous altar rounds");
        return new(mapId, monsters.AsReadOnly(), Array.AsReadOnly(rounds));
    }

    private static AltarAdmissionPricing ReadAdmissionPricing(PvfArchive archive)
    {
        const string path = "etc/serverparameter.etc";
        var document = PvfDocument.Open(archive, path);
        var prices = document.Read(PvfField.Int32List("[price average]"));
        var increases = document.Read(PvfField.Int32List("[admission increase]"));
        var lottery = document.Read(PvfField.Int32("[lotto cost]", minimum: 0));
        Require(prices.Count > 0 && prices.Count % 3 == 0 && increases.Count > 0 && increases.Count % 2 == 0, path, "invalid altar admission prices");
        var bands = Enumerable.Range(0, prices.Count / 3).Select(i => new AltarPriceBand(prices[i * 3], prices[i * 3 + 1], prices[i * 3 + 2])).ToArray();
        Require(bands.All(x => x.MinimumLevel > 0 && x.MaximumLevel >= x.MinimumLevel && x.Gold >= 0), path, "invalid altar admission level band");
        var rates = Enumerable.Range(0, increases.Count / 2).ToDictionary(i => increases[i * 2], i => increases[i * 2 + 1]);
        Require(rates.All(x => x.Key > 0 && x.Value >= 0), path, "invalid altar admission increase");
        return new(bands, rates, lottery);
    }

    private static AltarRewardDefinition ReadRewards(PvfArchive archive)
    {
        const string path = "etc/bloodclearreward.etc";
        var document = PvfDocument.Open(archive, path);
        var probabilities = document.Read(PvfField.Int32List("[reward item prob]"));
        var gold = document.Read(PvfField.Number("[reward gold weight]"));
        var experience = document.Read(PvfField.Numbers("[reward exp weight]"));
        var entries = document.Read(PvfField.Numbers("[reward exp inout count weight]"));
        var points = document.Read(PvfField.Int32List("[ultimate difficulty point]"));
        var ultimate = document.Read(PvfField.Int32List("[ultimate reward prob]"));
        Require(probabilities.Count == 3 && probabilities.All(value => value >= 0) && probabilities[2] > 0
            && (long)probabilities[0] + probabilities[1] <= probabilities[2] && gold > 0
            && experience.Count > 0 && experience.All(value => value >= 0)
            && entries.Count > 0 && entries.All(value => value >= 0)
            && points.Count == 2 && points.All(value => value >= 0)
            && ultimate.Count > 0 && ultimate.Count % 3 == 0, path, "invalid altar reward tables");
        var ultimateRewards = new List<AltarUltimateRewardProbability>();
        for (var index = 0; index < ultimate.Count; index += 3)
        {
            Require(ultimate[index] > 0 && ultimate[index + 1] >= 0 && ultimate[index + 2] >= 0
                && (long)ultimate[index + 1] + ultimate[index + 2] <= 100, path, "invalid ultimate reward probability");
            ultimateRewards.Add(new(ultimate[index], ultimate[index + 1], ultimate[index + 2]));
        }
        Require(ultimateRewards.Select(value => value.Point).Distinct().Count() == ultimateRewards.Count, path, "duplicate ultimate reward point");
        // Altar item candidates use the common clear-reward generator; this file only supplies altar weights.
        return new(probabilities[0], probabilities[1], probabilities[2], gold, experience, entries,
            points, ultimateRewards.AsReadOnly(), "etc/itemdropinfo_clearreward.etc");
    }

    private static int ReadDailyEntries(PvfArchive archive, int dungeonId)
    {
        const string path = "etc/dungeonetc/dungeonincountinfo.etc";
        var values = PvfDocument.Open(archive, path).Read(PvfField.Int32List("[dungeon incount info]"));
        Require(values.Count % 4 == 0, path, "truncated dungeon incount rows");
        var matches = Enumerable.Range(0, values.Count / 4).Select(index => index * 4)
            .Where(index => values[index + 1] == dungeonId).ToArray();
        Require(matches.Length == 1 && values[matches[0] + 2] > 0, path, "missing or invalid altar incount");
        return values[matches[0] + 2];
    }

    private static byte Difficulty(PvfArchive archive, PvfToken token, string path)
    {
        if (token.IsString)
            return archive.ResolveString(token.Value) switch
            {
                "A" => 1,
                "B" => 2,
                _ => throw new InvalidDataException($"PVF '{path}': invalid altar difficulty."),
            };
        var difficulty = Integer(token, path);
        Require(difficulty is 1 or 2, path, "invalid altar difficulty");
        return (byte)difficulty;
    }

    private static int Integer(PvfToken token, string path)
    {
        Require(token.Kind == PvfTokenKind.Int32, path, "expected an integer altar field");
        return token.Value;
    }

    private static string Resolve(PvfArchive archive, string directory, int id) =>
        PvfListFile.Read(archive, $"{directory}/{directory}.lst").FirstOrDefault(entry => entry.Id == id) is var entry
        && entry.RelativePath is not null ? $"{directory}/{entry.RelativePath}"
        : throw new InvalidDataException($"PVF '{directory}': missing identifier {id}.");

    private static void Require(bool condition, string path, string reason)
    {
        if (!condition) throw new InvalidDataException($"PVF '{path}': {reason}.");
    }
}
