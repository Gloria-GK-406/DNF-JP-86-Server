using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.World.IllusionTower;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfIllusionTowerContentSource(PvfArchiveProvider provider) : IIllusionTowerContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, IllusionTowerDefinition> cached = [];

    // The archive gives both towers the same [dungeon type]; only their identities and the
    // deathtower.etc reward fields tell them apart.
    private static readonly IReadOnlyDictionary<int, IllusionTowerVariant> Towers = new Dictionary<int, IllusionTowerVariant>
    {
        [11000] = IllusionTowerVariant.Death,
        [11001] = IllusionTowerVariant.Illusion,
    };

    public string Name => "illusion-towers";

    public async ValueTask<int> WarmAsync(CancellationToken cancellationToken)
    {
        foreach (var dungeonId in Towers.Keys) await GetAsync(cancellationToken, dungeonId).ConfigureAwait(false);
        return Towers.Count;
    }

    public bool Supports(int dungeonId) => Towers.ContainsKey(dungeonId);

    public ValueTask<IllusionTowerDefinition> GetAsync(CancellationToken cancellationToken, int dungeonId)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Towers.TryGetValue(dungeonId, out var variant)) throw new ArgumentOutOfRangeException(nameof(dungeonId));
        lock (gate)
        {
            if (!cached.TryGetValue(dungeonId, out var definition)) cached[dungeonId] = definition = Read(provider.Archive, dungeonId, variant);
            return ValueTask.FromResult(definition);
        }
    }

    private static IllusionTowerDefinition Read(PvfArchive archive, int dungeonId, IllusionTowerVariant variant)
    {
        var dungeonPath = PvfListFile.Resolve(archive, "dungeon", dungeonId);
        var dungeon = PvfDocument.Open(archive, dungeonPath).AtRoot();
        PvfField.Require(dungeon.Read(PvfField.String("[dungeon type]")) == "tower of death", dungeonPath, "unexpected dungeon type");
        var minimumLevel = dungeon.Read(PvfField.Int32("[minimum required level]", minimum: 1, maximum: 255));
        var basisLevel = dungeon.Read(PvfField.Int32("[basis level]", minimum: 1, maximum: 255));
        var items = Ints(dungeon, "[required item]");
        PvfField.Require(items.Count % 3 == 0, dungeonPath, "required item triples are truncated");
        var requiredItems = new List<TowerRequiredItem>();
        for (var i = 0; i < items.Count; i += 3)
        {
            PvfField.Require(items[i] > 0 && items[i + 1] > 0 && items[i + 2] is 0 or 1, dungeonPath, "invalid required item");
            requiredItems.Add(new(items[i], items[i + 1], items[i + 2] == 1));
        }
        var stages = Ints(dungeon, "[death tower map indexes]");
        PvfField.Require(stages.Count > 0 && stages[0] > 0 && stages.Count == 1 + stages[0] * 2, dungeonPath, "invalid tower stage table");
        var apcPaths = PvfListFile.Read(archive, "aicharacter/aicharacter.lst")
            .GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => $"aicharacter/{group.First().RelativePath}");
        var apcs = new Dictionary<int, TowerApcCandidate>();
        TowerApcCandidate ReadApc(int id)
        {
            if (apcs.TryGetValue(id, out var existing)) return existing;
            if (!apcPaths.TryGetValue(id, out var path)) throw new InvalidDataException($"Missing tower APC {id}.");
            var document = PvfDocument.Open(archive, path);
            var values = document.Read(PvfField.Tokens("[minimum info]"));
            PvfField.Require(values.Count == 16 && values[0].Kind == PvfTokenKind.QuotedString
                && values.Skip(1).Take(11).All(value => value.Kind == PvfTokenKind.Int32)
                && values[12].Kind == PvfTokenKind.QuotedString
                && values.Skip(13).All(value => value.Kind == PvfTokenKind.Int32), path, "invalid APC minimum info");
            PvfField.Require(values[5].Value is > 0 and <= 255, path, "invalid APC level");
            return apcs[id] = new(id, (byte)values[5].Value, AppearancePoint(document, path)) { Items = ReadItems(document, path) };
        }

        var rooms = new List<TowerRoomDefinition>();
        for (var index = 0; index < stages[0]; index++)
        {
            var level = stages[1 + index * 2];
            var mapId = stages[2 + index * 2];
            PvfField.Require(level == index + 1 && mapId > 0, dungeonPath, "stage numbers must be consecutive and ordered");
            rooms.Add(ReadRoom(archive, dungeonId, level, mapId, ReadApc));
        }
        foreach (var (id, path) in apcPaths.OrderBy(pair => pair.Key))
        {
            var document = PvfDocument.Open(archive, path);
            if (AppearancePoint(document, path) > 0) ReadApc(id);
        }
        var rewards = PvfDocument.Open(archive, "etc/deathtower.etc");
        var experienceWeights = rewards.Read(PvfField.Numbers(variant == IllusionTowerVariant.Illusion ? "[illusion reward exp weight]" : "[reward exp weight]"));
        var cards = Ints(rewards, variant == IllusionTowerVariant.Illusion ? "[illusion reward card num]" : "[reward card num]");
        PvfField.Require(experienceWeights.Count >= rooms.Count && cards.Count >= rooms.Count
            && experienceWeights.All(value => value >= 0) && cards.All(value => value >= 0), dungeonPath, "reward table does not cover every stage");
        return new(dungeonId, variant, minimumLevel, basisLevel, requiredItems.AsReadOnly(), rooms.AsReadOnly(),
            Array.AsReadOnly(apcs.Values.Where(apc => apc.AppearancePoint > 0).OrderBy(apc => apc.TemplateId).ToArray()),
            new(experienceWeights, cards, Ints(rewards, "[reward item prob]"),
                rewards.Read(PvfField.Number("[reward gold weight]")),
                rewards.Read(PvfField.Number("[apc exp weight]")),
                rewards.Read(PvfField.Number("[monster exp weight]"))),
            new(dungeon.Read(PvfField.Int32("[tower max clear item num]", minimum: 0)),
                dungeon.Read(PvfField.Int32("[tower fp cubepiece]")),
                dungeon.Read(PvfField.Int32("[tower limit of stackable item]")),
                dungeon.TryRead(PvfField.Int32("[tower item drop]", optional: true), out var itemDrop) ? itemDrop : 1,
                Ints(dungeon, "[tower high skill initial cool time]"),
                dungeon.Read(PvfField.Numbers("[tower high skill initial cool time rate]")),
                dungeon.ReadAll(PvfField.Int32List("[tower recovery]"))));
    }

    private static TowerRoomDefinition ReadRoom(PvfArchive archive, int dungeonId, int level, int mapId, Func<int, TowerApcCandidate> readApc)
    {
        var path = PvfListFile.Resolve(archive, "map", mapId);
        var document = PvfDocument.Open(archive, path);
        PvfField.Require(document.Read(PvfField.Int32("[dungeon]")) == dungeonId, path, "map belongs to another dungeon");
        var actors = new List<TowerActorDeclaration>();
        foreach (var row in Rows(document, "[monster]", 10, path))
        {
            PvfField.Require(row.Take(8).All(value => value.Kind == PvfTokenKind.Int32)
                && row.Skip(8).All(value => value.Kind == PvfTokenKind.QuotedString), path, "invalid monster row");
            PvfField.Require(row[0].Value > 0 && row[1].Value is 0 or 1, path, "invalid monster id or level mode");
            actors.Add(new(row[0].Value, row[2].Value, row[1].Value == 1,
                Rank(archive.ResolveString(row[9].Value), path), true,
                new(row[3].Value, row[4].Value, row[5].Value))
            { Items = ReadItems(PvfDocument.Open(archive, PvfListFile.Resolve(archive, "monster", row[0].Value)), path) });
        }
        foreach (var row in Rows(document, "[ai character]", 8, path))
        {
            PvfField.Require(row.Take(4).Concat(row.Skip(6)).All(value => value.Kind == PvfTokenKind.Int32)
                && row.Skip(4).Take(2).All(value => value.Kind == PvfTokenKind.QuotedString), path, "invalid APC row");
            // Current illusion maps declare hostile [monster] APCs. Do not turn unknown/allied roles into enemies.
            PvfField.Require(archive.ResolveString(row[4].Value) == "[monster]", path, "unsupported APC allegiance");
            var apc = readApc(row[0].Value);
            actors.Add(new(apc.TemplateId, apc.Level, false,
                (byte)(5 + Rank(archive.ResolveString(row[5].Value), path)), true,
                new(row[1].Value, row[2].Value, row[3].Value)) { Items = apc.Items });
        }
        var budget = document.Read(PvfField.Int32("[apc random point]", optional: true, minimum: 0));
        var positions = new List<TowerPosition>();
        if (document.TryRead(PvfField.Int32List("[monster spawn pos]", optional: true), out var spawn))
        {
            PvfField.Require(spawn.Count > 0 && spawn[0] >= 0 && spawn.Count == 1 + spawn[0] * 3, path, "invalid random APC positions");
            for (var i = 1; i < spawn.Count; i += 3) positions.Add(new(spawn[i], spawn[i + 1], spawn[i + 2]));
        }
        var objects = new List<TowerObjectDeclaration>();
        if (document.TryRead(PvfField.Int32List("[passive object]", optional: true), out var values))
        {
            PvfField.Require(values.Count % 4 == 0, path, "invalid passive object rows");
            for (var i = 0; i < values.Count; i += 4) objects.Add(new(values[i], new(values[i + 1], values[i + 2], values[i + 3])));
        }
        return new(level, mapId, actors.AsReadOnly(), budget, positions.AsReadOnly(), objects.AsReadOnly());
    }

    private static IReadOnlyList<TowerItemChance> ReadItems(PvfDocument document, string path)
    {
        if (!document.TryRead(PvfField.Int32List("[death tower item]", optional: true), out var values)) return [];
        PvfField.Require(values.Count % 2 == 0, path, "invalid tower item pairs");
        var items = new List<TowerItemChance>();
        for (var index = 0; index < values.Count; index += 2)
        {
            PvfField.Require(values[index] > 0 && values[index + 1] is >= 0 and <= 10000, path, "invalid tower item rate");
            items.Add(new(values[index], values[index + 1]));
        }
        return items.AsReadOnly();
    }

    private static IEnumerable<PvfToken[]> Rows(PvfDocument document, string name, int width, string path)
    {
        foreach (var values in document.ReadAll(PvfField.Tokens(name, optional: true)))
        {
            PvfField.Require(values.Count % width == 0, path, $"truncated {name} row");
            for (var i = 0; i < values.Count; i += width) yield return values.Skip(i).Take(width).ToArray();
        }
    }

    private static byte Rank(string word, string path) => word switch
    {
        "[normal]" => 0, "[champion]" => 1, "[super champion]" => 2, "[boss]" => 3,
        _ => throw new InvalidDataException($"PVF '{path}': unsupported actor rank '{word}'."),
    };
    private static int AppearancePoint(PvfDocument document, string path)
    {
        // Some current AIC files repeat the same zero declaration (for example sandspoon.aic).
        // Identical declarations are unambiguous; conflicting declarations must not choose a pool silently.
        var values = document.ReadAll(PvfField.Int32("[appearance point]", optional: true, minimum: 0)).Distinct().ToArray();
        PvfField.Require(values.Length <= 1, path, "conflicting APC appearance points");
        return values.Length == 0 ? 0 : values[0];
    }
    private static IReadOnlyList<int> Ints(PvfDocument document, string name) => document.Read(PvfField.Int32List(name));
}
