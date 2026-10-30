using System.Collections.ObjectModel;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfDarkElfRuinsContentSource(PvfArchiveProvider provider) : IDarkElfRuinsContentSource
{
    private readonly Lazy<DarkElfRuinsCatalog> cache = new(() => Read(provider.Archive));
    public DarkElfRuinsCatalog Load() => cache.Value;

    internal const string Path = "etc/dungeonetc/licensedungeoninfo.etc";

    internal static DarkElfRuinsCatalog Read(PvfArchive archive)
    {
        var document = PvfDocument.Open(archive, Path);
        var rows = document.Read(PvfField.Int32List("[dungeon license info]"));
        Require(rows.Count > 0 && rows.Count % 5 == 0, "invalid dungeon directory");
        var dungeons = new List<DarkElfRuinsDungeon>();
        for (var i = 0; i < rows.Count; i += 5)
        {
            Require(rows[i] > 0 && rows[i + 1] > 0 && rows[i + 2] is >= 1 and <= 4
                && rows[i + 3] >= 0 && rows[i + 4] >= 0, "invalid directory row");
            dungeons.Add(new(rows[i], rows[i + 1], rows[i + 2], rows[i + 3], rows[i + 4]));
        }
        Require(dungeons.Select(x => x.DungeonId).Distinct().Count() == dungeons.Count
            && dungeons.Select(x => (x.Group, x.License)).Distinct().Count() == dungeons.Count,
            "duplicate dungeon or group/license binding");
        var known = dungeons.Select(x => x.DungeonId).ToHashSet();
        var days = new Dictionary<int, IReadOnlyList<int>>();
        var rewards = new List<DarkElfRuinsTierReward>();
        int? day = null, license = null;
        var inDay = false;
        List<ItemDeliveryGrant>? clear = null;
        IReadOnlyList<DarkElfRuinsDrop>? drops = null;
        foreach (var section in document.Sections)
        {
            IReadOnlyList<int> Ints() => PvfField.Int32List(section.Name).Decode(archive, section.Values, Path);
            switch (section.Name)
            {
                case "[daily open dungeon]":
                    Require(!inDay, "nested open-day block"); inDay = true; day = null; break;
                case "[day]":
                    var value = Ints();
                    Require(inDay && day is null && value.Count == 1 && value[0] is >= 0 and <= 6, "invalid open day");
                    day = value[0]; break;
                case "[dungeon]":
                    var ids = Ints();
                    Require(inDay && day is not null && ids.Count > 0 && ids.All(known.Contains)
                        && ids.Distinct().Count() == ids.Count, "invalid open-day dungeons");
                    Require(days.TryAdd(day!.Value, ids), "duplicate open-day dungeons"); break;
                case "[/daily open dungeon]":
                    Require(inDay && day is not null && days.ContainsKey(day.Value), "incomplete open-day block");
                    inDay = false; break;
                case "[license]":
                    var tier = Ints();
                    Require(license is null && tier.Count == 1 && tier[0] is >= 1 and <= 4, "invalid reward license");
                    license = tier[0]; clear = null; drops = null; break;
                case "[dungeon clear reward]":
                    Require(license is not null && clear is null, "reward outside license or duplicated");
                    clear = Grants(Ints()); break;
                case "[groop drop item info]":
                    Require(license is not null && drops is null, "guardian drops outside license or duplicated");
                    var weights = Ints();
                    Require(weights.Count > 0 && weights.Count % 2 == 0 && weights.All(x => x > 0)
                        && weights.Where((_, i) => i % 2 == 1).Sum(x => (long)x) <= 10000, "invalid guardian drops");
                    drops = Array.AsReadOnly(Enumerable.Range(0, weights.Count / 2)
                        .Select(i => new DarkElfRuinsDrop(weights[i * 2], weights[i * 2 + 1])).ToArray()); break;
                case "[/license]":
                    Require(license is not null && clear is {Count: > 0}, "incomplete reward license");
                    rewards.Add(new(license!.Value, clear!.AsReadOnly(), drops ?? [])); license = null; break;
            }
        }
        Require(!inDay && license is null && days.Count == 7 && known.All(id => days.Values.Any(x => x.Contains(id))),
            "incomplete weekly rotation");
        Require(rewards.Select(x => x.License).Distinct().Count() == rewards.Count, "duplicate reward license");
        var difficultyTokens = document.Read(PvfField.Tokens("[dungeon difficulty by license]"));
        Require(difficultyTokens.Count > 0 && difficultyTokens.Count % 2 == 0, "invalid difficulty table");
        var difficulties = new Dictionary<int, byte>();
        for (var i = 0; i < difficultyTokens.Count; i += 2)
        {
            Require(difficultyTokens[i].Kind == Dfo.Pvf.Tokens.PvfTokenKind.Int32 && difficultyTokens[i + 1].IsString,
                "invalid difficulty row");
            var difficulty = archive.ResolveString(difficultyTokens[i + 1].Value).Trim('[', ']') switch
            {
                "normal" => (byte)0, "expert" => (byte)1, "master" => (byte)2, "king" => (byte)3,
                _ => throw new InvalidDataException($"{Path}: unsupported license difficulty"),
            };
            Require(difficulties.TryAdd(difficultyTokens[i].Value, difficulty), "duplicate difficulty license");
        }
        var rates = document.Read(PvfField.Int32List("[groop appear rate by enter count]"));
        Require(rates.Count > 0 && rates.Count % 2 == 0, "invalid guardian rate table");
        var guardianRates = new List<DarkElfRuinsRate>();
        for (var i = 0; i < rates.Count; i += 2)
        {
            Require(rates[i] > 0 && rates[i + 1] is >= 0 and <= 10000
                && (i == 0 || rates[i] > rates[i - 2]), "invalid guardian rate row");
            guardianRates.Add(new(rates[i], rates[i + 1]));
        }
        var daily = document.Read(PvfField.Int32List("[daily clear reward]"));
        Require(daily.Count > 0 && daily.Count % 3 == 0 && daily.All(x => x > 0), "invalid daily reward table");
        var dailyRewards = new Dictionary<int, ItemDeliveryGrant>();
        for (var i = 0; i < daily.Count; i += 3)
            Require(known.Contains(daily[i]) && dailyRewards.TryAdd(daily[i], new(daily[i + 1], daily[i + 2])),
                "unknown or duplicate daily reward dungeon");
        var guardianLicenses = document.Read(PvfField.Int32List("[groop apply licence]"));
        Require(guardianLicenses.Distinct().Count() == guardianLicenses.Count
            && guardianLicenses.All(tier => rewards.Any(x => x.License == tier && x.GuardianDrops.Count > 0)),
            "missing guardian reward tier");
        Require(dungeons.All(x => difficulties.ContainsKey(x.License) && rewards.Any(r => r.License == x.License)
            && dailyRewards.ContainsKey(x.DungeonId)), "incomplete difficulty or reward binding");
        var listed = PvfListFile.Read(archive, "dungeon/dungeon.lst").ToDictionary(x => x.Id, x => x.RelativePath);
        var passiveObjects = PvfListFile.Read(archive, "passiveobject/passiveobject.lst").Select(x => x.Id).ToHashSet();
        for (var i = 0; i < dungeons.Count; i++)
        {
            Require(listed.TryGetValue(dungeons[i].DungeonId, out var path), "unlisted ruins dungeon");
            var index = -1;
            var guardian = new List<int>();
            var sections = PvfDocument.Open(archive, "dungeon/" + path).Sections;
            var features = new Dictionary<int, DarkElfRuinsMazeFeatures>();
            foreach (var section in sections)
            {
                if (section.Name == "[maze info]") index++;
                if (section.Name == "[groop in maze]")
                {
                    Require(index >= 0 && !guardian.Contains(index), "invalid guardian maze marker");
                    guardian.Add(index);
                }
            }
            Require(index >= 0, "ruins dungeon has no maze");
            var ordinary = Enumerable.Range(0, index + 1).Except(guardian).ToArray();
            Require(ordinary.Length > 0 && (guardianLicenses.Contains(dungeons[i].License) ? guardian.Count > 0 : guardian.Count == 0),
                "guardian license and maze declarations disagree");
            var starts = sections.Select((section, offset) => (section, offset)).Where(x => x.section.Name == "[maze info]").Select(x => x.offset).ToArray();
            for (var maze = 0; maze < starts.Length; maze++)
                features.Add(maze, ReadFeatures(archive, sections.Skip(starts[maze]).Take((maze + 1 < starts.Length ? starts[maze + 1] : sections.Count) - starts[maze]).ToArray(), passiveObjects));
            dungeons[i] = dungeons[i] with { GuardianMazes = guardian.AsReadOnly(), OrdinaryMazes = Array.AsReadOnly(ordinary),
                MazeFeatures = new ReadOnlyDictionary<int, DarkElfRuinsMazeFeatures>(features) };
        }
        return new(dungeons.AsReadOnly(), new ReadOnlyDictionary<int, IReadOnlyList<int>>(days),
            new ReadOnlyDictionary<int, byte>(difficulties),
            document.Read(PvfField.Int32("[daily enter count]", minimum: 1, maximum: byte.MaxValue)),
            document.Read(PvfField.Int32("[groop appear count on month]", minimum: 0)),
            guardianRates.AsReadOnly(), guardianLicenses, rewards.AsReadOnly(),
            new ReadOnlyDictionary<int, ItemDeliveryGrant>(dailyRewards));
    }

    private static DarkElfRuinsMazeFeatures ReadFeatures(PvfArchive archive, PvfSection[] sections, HashSet<int> passiveObjects)
    {
        IReadOnlyList<int> Ints(PvfSection value) => PvfField.Int32List(value.Name).Decode(archive, value.Values, Path);
        int Number(PvfSection[] block, string name)
        {
            var values = Ints(block.Single(x => x.Name == name));
            Require(values.Count == 1, "invalid maze object scalar"); return values[0];
        }
        MazeCell Cell(PvfSection[] block, string name)
        {
            var values = Ints(block.Single(x => x.Name == name));
            Require(values.Count == 2 && values.All(x => x is >= 0 and <= byte.MaxValue), "invalid maze object cell");
            return new((byte)values[0], (byte)values[1]);
        }
        PvfSection[] Block(ref int offset, string closing)
        {
            var end = Array.FindIndex(sections, offset + 1, x => x.Name == closing);
            Require(end > offset, "unterminated maze feature");
            var result = sections[(offset + 1)..end]; offset = end; return result;
        }
        var warps = new Dictionary<MazeCell, MazeCell>();
        var groups = new List<DarkElfRuinsObjectGroup>();
        for (var i = 0; i < sections.Length; i++)
        {
            if (sections[i].Name == "[warp map condition]")
            {
                var block = Block(ref i, "[/warp map condition]");
                Require(warps.TryAdd(Cell(block, "[source grid pos]"), Cell(block, "[dest grid pos]")), "duplicate warp source");
            }
            else if (sections[i].Name == "[randomized object creation]")
            {
                var block = Block(ref i, "[/randomized object creation]");
                var count = Number(block, "[select]");
                var regenerate = Number(block, "[regenerate]");
                Require(regenerate is 0 or 1, "invalid object regeneration");
                var objects = new List<DungeonObjectSpawn>();
                for (var j = 0; j < block.Length; j++)
                {
                    if (block[j].Name != "[object]") continue;
                    var end = Array.FindIndex(block, j + 1, x => x.Name == "[/object]");
                    Require(end > j, "unterminated maze object");
                    var item = block[(j + 1)..end]; j = end;
                    var type = item.Single(x => x.Name == "[object type]");
                    Require(PvfField.String(type.Name).Decode(archive, type.Values, Path) == "passive object", "unsupported maze object type");
                    var id = Number(item, "[index]");
                    Require(passiveObjects.Contains(id) && item.Count(x => x.Name == "[neutral]") == 1, "unlisted or unsupported maze object");
                    var position = Ints(item.Single(x => x.Name == "[pos]"));
                    Require(position.Count == 2, "invalid maze object position");
                    // Explicit passive-object declaration selects the passive namespace; neutral is PVF team 200.
                    objects.Add(new(Cell(item, "[map]"), position[0], position[1], id, 200, 1, regenerate != 0));
                }
                Require(count > 0 && count <= objects.Count && objects.Count <= byte.MaxValue, "invalid object selection count");
                groups.Add(new(count, objects.AsReadOnly()));
            }
        }
        return new(groups.AsReadOnly(), new ReadOnlyDictionary<MazeCell, MazeCell>(warps));
    }

    private static List<ItemDeliveryGrant> Grants(IReadOnlyList<int> values)
    {
        Require(values.Count > 0 && values.Count % 2 == 0 && values.All(x => x > 0), "invalid clear rewards");
        return Enumerable.Range(0, values.Count / 2).Select(i => new ItemDeliveryGrant(values[2 * i], values[2 * i + 1])).ToList();
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidDataException($"{Path}: {reason}.");
    }
}
