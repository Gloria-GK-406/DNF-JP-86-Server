using System.Collections.Immutable;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfGuardianAltarContentSource(PvfArchiveProvider provider) : IGuardianAltarContentSource
{
    private readonly Lazy<GuardianAltarCatalog> catalog = new(() => Read(provider.Archive));
    public GuardianAltarCatalog Catalog => catalog.Value;

    private static GuardianAltarCatalog Read(PvfArchive archive)
    {
        var stages = Enumerable.Range(3511, 13).Select(id => ReadStage(archive, id)).ToImmutableArray();
        var mapIds = stages.SelectMany(stage => stage.Maps.Select(map => map.MapId).Concat(stage.SurvivalMaps)).Distinct();
        var maps = mapIds.ToImmutableDictionary(id => id, id => ReadMap(archive, id));
        const string configPath = "etc/advancealtar/advancealtarconfig.etc";
        var config = PvfDocument.Open(archive, configPath);
        var gage = config.Read(PvfField.Int32List("[gage up rate]"));
        Require(gage.Count > 0 && gage.Count % 3 == 0, configPath, "invalid gage levels");
        var levels = Enumerable.Range(0, gage.Count / 3).Select(i => new GuardianAltarGageLevel(
            gage[i * 3], gage[i * 3 + 1], gage[i * 3 + 2])).ToImmutableArray();
        Require(levels.Select(x => x.Level).SequenceEqual(Enumerable.Range(0, levels.Length))
            && levels.All(x => x.Capacity > 0 && x.Rate > 0), configPath, "invalid gage level values");
        var percent = config.Read(PvfField.Number("[gage upgrade cost percent]"));
        Require(percent is >= 0 and <= 1, configPath, "invalid gage upgrade fraction");
        var configuration = new GuardianAltarConfiguration(percent,
            config.Read(PvfField.Int32("[daily refill ticket]", minimum: 0)), levels);

        const string summonPath = "etc/advancealtar/advancealtarsummondata.etc";
        var summonDocument = PvfDocument.Open(archive, summonPath);
        var values = summonDocument.Read(PvfField.Tokens("[summon object]"));
        Require(values.Count % 9 == 0 && values.Count > 0, summonPath, "invalid summon rows");
        var summons = Enumerable.Range(0, values.Count / 9).Select(i =>
        {
            var row = values.Skip(i * 9).Take(9).ToArray();
            var result = new GuardianAltarSummon(Integer(row[0], summonPath), Text(archive, row[1], summonPath),
                Text(archive, row[2], summonPath), Integer(row[3], summonPath), Integer(row[4], summonPath),
                Integer(row[5], summonPath), Integer(row[6], summonPath), Integer(row[7], summonPath), Integer(row[8], summonPath));
            Require(result.Id > 0 && result.MonsterCode > 0 && result.MinimumLevel > 0
                && result.MaximumLevel >= result.MinimumLevel && result.Team == "enemy"
                && result.Rank is "normal" or "named" or "boss", summonPath, "invalid summon values");
            return result;
        }).ToImmutableDictionary(x => x.Id);
        var actions = new List<GuardianAltarAction>();
        var inActions = false;
        foreach (var section in summonDocument.Sections)
        {
            if (section.Name == "[action define]") { inActions = true; continue; }
            if (!inActions || section.Name.StartsWith("[/", StringComparison.Ordinal)) continue;
            Require(section.Name is "[normal]" or "[named]" or "[boss]", summonPath, "unknown summon action kind");
            var row = section.Values;
            Require(row.Count >= 5 && (row.Count - 1) % 4 == 0, summonPath, "invalid summon action width");
            var spawns = Enumerable.Range(0, (row.Count - 1) / 4).Select(i =>
                new GuardianAltarSpawnAction(Integer(row[i * 4 + 1], summonPath), Integer(row[i * 4 + 2], summonPath),
                    Integer(row[i * 4 + 3], summonPath), Integer(row[i * 4 + 4], summonPath))).ToImmutableArray();
            Require(spawns.All(x => summons.ContainsKey(x.SummonId) && x.DelayMilliseconds >= 0
                && x.IntervalMilliseconds > 0 && x.CountLimit >= 0), summonPath, "invalid summon action values");
            actions.Add(new(Integer(row[0], summonPath), section.Name[1..^1], spawns));
        }
        Require(actions.Count > 0 && actions.Select(x => x.Id).Distinct().Count() == actions.Count,
            summonPath, "duplicate or missing summon actions");
        foreach (var map in maps.Values)
            Require(map.Phases.All(phase => phase.Assignments.All(assignment => actions.Any(a => a.Id == assignment.ActionId))),
                $"map:{map.MapId}", "phase refers to a missing action");

        var (shop, reset) = ReadShop(archive);
        return new(stages, configuration, maps, summons, actions.ToImmutableArray(), shop, reset, ReadAchievements(archive));
    }

    private static GuardianAltarStage ReadStage(PvfArchive archive, int id)
    {
        var path = Resolve(archive, "dungeon", id);
        var document = PvfDocument.Open(archive, path);
        Require(document.Read(PvfField.String("[dungeon type]")) == "advance altar", path, "not a guardian altar");
        var kind = document.Read(PvfField.String("[advance altar type]"));
        Require(kind is "normal" or "survival", path, "invalid guardian altar kind");
        var survival = kind == "survival";
        Require(survival == (id == 3523), path, "unexpected survival dungeon");
        var rawMaps = document.Read(PvfField.Tokens("[advance altar map]"));
        Require(rawMaps.Count % 3 == 0, path, "invalid stage maps");
        var rewards = Block(document.Sections, "[advance altar clear reward]", path);
        var maps = Enumerable.Range(0, rawMaps.Count / 3).Select(i =>
        {
            var difficulty = Difficulty(Text(archive, rawMaps[i * 3], path), path);
            var name = "[" + difficulty.ToString().ToLowerInvariant() + "]";
            var reward = rewards.SingleOrDefault(x => x.Name == name)
                ?? throw new InvalidDataException($"PVF '{path}': missing {name} reward.");
            return new GuardianAltarStageMap(difficulty, Integer(rawMaps[i * 3 + 1], path),
                Integer(rawMaps[i * 3 + 2], path), Rewards(archive, reward.Values, path));
        }).ToImmutableArray();
        Require(survival ? maps.IsEmpty : maps.Length == 3 && maps.Select(x => x.Difficulty).Distinct().Count() == 3,
            path, "unexpected difficulty set");
        Require(maps.All(x => x.MapId > 0 && x.BasisLevel is >= 0 and <= 254), path, "invalid stage map values");
        var survivalMaps = (document.Read(PvfField.Int32List("[advance altar survival map]", optional: !survival)) ?? []).ToImmutableArray();
        Require(survival ? survivalMaps.Length > 0 && survivalMaps.All(x => x > 0) : survivalMaps.IsEmpty,
            path, "invalid survival map list");
        var survivalRewards = new List<GuardianAltarSurvivalReward>();
        var sections = survival ? Block(document.Sections, "[advance altar survival clear reward]", path) : [];
        foreach (var block in Blocks(sections, "[type]", path))
        {
            var range = Section(block, "[round]", path).Values.Select(x => Integer(x, path)).ToArray();
            Require(range.Length == 2 && range[0] > 0 && range[1] >= range[0], path, "invalid survival reward rounds");
            survivalRewards.Add(new(range[0], range[1], Rewards(archive, Section(block, "[list]", path).Values, path)));
        }
        Require(survival == (survivalRewards.Count > 0), path, "missing survival rewards");
        return new(id, survival, maps, survivalMaps, survivalRewards.ToImmutableArray());
    }

    private static GuardianAltarMap ReadMap(PvfArchive archive, int id)
    {
        var path = Resolve(archive, "map", id);
        var document = PvfDocument.Open(archive, path);
        var phases = Blocks(Block(document.Sections, "[time line]", path), "[phase]", path).Select(block =>
        {
            var duration = Integer(Section(block, "[duration]", path).Values.Single(), path);
            var kind = Text(archive, Section(block, "[type]", path).Values.Single(), path);
            var assigned = Section(block, "[action assign]", path).Values.Select(x => Integer(x, path)).ToArray();
            Require(duration > 0 && kind is "normal" or "named" or "boss"
                && assigned.Length > 0 && assigned.Length % 3 == 0, path, "invalid phase");
            var assignments = Enumerable.Range(0, assigned.Length / 3).Select(i =>
                new GuardianAltarActionAssignment(assigned[i * 3], assigned[i * 3 + 1], assigned[i * 3 + 2])).ToImmutableArray();
            // 70Reference: makeCurrentPhaseSummonObject compares the last two operands with
            // alive monsters; -1/-1 is the fallback. They are not additional action identifiers.
            Require(assignments.All(x => x.ActionId > 0 && (x.MinimumAliveMonsters == -1 && x.MaximumAliveMonsters == -1
                || x.MinimumAliveMonsters >= 0 && x.MaximumAliveMonsters >= x.MinimumAliveMonsters)), path, "invalid action assignment");
            return new GuardianAltarPhase(duration, kind, assignments);
        }).ToImmutableArray();
        Require(phases.Length > 0, path, "missing guardian altar phases");
        return new(id, document.Read(PvfField.Int32("[center index]", minimum: 1)), phases);
    }

    private static (ImmutableArray<GuardianAltarShopItem>, ImmutableDictionary<int, int>) ReadShop(PvfArchive archive)
    {
        const string path = "etc/advancealtar/advancealtarshop.etc";
        var document = PvfDocument.Open(archive, path);
        var fees = document.Read(PvfField.Int32List("[star reset fee gold]"));
        Require(fees.Count > 0 && fees.Count % 2 == 0, path, "invalid star reset fees");
        var reset = Enumerable.Range(0, fees.Count / 2).ToImmutableDictionary(i => fees[i * 2], i => fees[i * 2 + 1]);
        Require(reset.All(x => x.Key >= 0 && x.Value >= 0), path, "negative star reset fee");
        var shop = new List<GuardianAltarShopItem>();
        foreach (var ridable in Blocks(document.Sections, "[ridable id]", path))
        {
            var ridableId = Integer(ridable[0].Values.Single(), path);
            foreach (var kind in Enum.GetValues<GuardianAltarShopKind>())
            foreach (var item in Blocks(Block(ridable, "[" + kind.ToString().ToLowerInvariant() + "]", path), "[id]", path))
            {
                var id = Integer(item[0].Values.Single(), path);
                var unlock = Integer(Section(item, "[unlock stage index]", path).Values.Single(), path);
                Require(unlock == 0 || unlock is >= 3511 and <= 3522, path, "invalid shop unlock stage");
                var fields = Section(item, "[field name]", path).Values.Select(x => Text(archive, x, path)).ToArray();
                var rows = Section(item, "[list]", path).Values;
                Require(fields.Length > 0 && fields.Distinct().Count() == fields.Length && rows.Count > 0
                    && rows.Count % fields.Length == 0, path, "invalid shop schema or rows");
                var levels = Enumerable.Range(0, rows.Count / fields.Length).Select(i =>
                {
                    var row = fields.Select((field, j) => (field, token: rows[i * fields.Length + j])).ToDictionary(x => x.field, x => x.token);
                    int Get(string name) => row.TryGetValue(name, out var value) ? Integer(value, path)
                        : throw new InvalidDataException($"PVF '{path}': missing shop column {name}.");
                    var attributes = row.Where(x => x.Value.Kind == PvfTokenKind.Int32).ToImmutableDictionary(x => x.Key, x => x.Value.Value);
                    var level = new GuardianAltarShopLevel(Get("level"), Get("index"), Get("price"), Get("cooltime"),
                        attributes.TryGetValue("need point", out var point) ? point : null,
                        attributes.TryGetValue("set slot", out var slot) ? slot : null, attributes);
                    Require(level.Level >= 0 && level.ResourceId >= 0 && level.Price >= 0 && level.Cooltime >= 0
                        && level.NeedPoint is null or >= 0, path, "invalid shop level values");
                    if (kind == GuardianAltarShopKind.Unit)
                    {
                        var unitPath = Resolve(archive, "stackable", level.ResourceId);
                        level = level with { SummonCooldownMilliseconds = PvfDocument.Open(archive, unitPath)
                            .Read(PvfField.Int32("[cool time]", minimum: 0)) };
                    }
                    return level;
                }).ToImmutableArray();
                // The supplied archive declares unit 7000 as 1,2,3,3, with distinct resources.
                // Preserve those rows; selecting an upgrade must not silently renumber or discard one.
                var settable = item.SingleOrDefault(x => x.Name == "[is settable slot]")?.Values;
                shop.Add(new(ridableId, kind, id, unlock, levels)
                { Settable = settable is null || Integer(settable.Single(), path) != 0 });
            }
        }
        Require(shop.Count > 0 && shop.Select(x => (x.RidableId, x.Kind, x.Id)).Distinct().Count() == shop.Count,
            path, "duplicate or missing shop items");
        return (shop.ToImmutableArray(), reset);
    }

    private static ImmutableArray<GuardianAltarAchievement> ReadAchievements(PvfArchive archive)
    {
        const string path = "etc/advancealtar/advancealtarachievementreward.etc";
        return Blocks(PvfDocument.Open(archive, path).Sections, "[reward data]", path).Select(block =>
        {
            var result = new GuardianAltarAchievement(Integer(Section(block, "[type]", path).Values.Single(), path),
                Difficulty(Text(archive, Section(block, "[stage difficulty]", path).Values.Single(), path), path),
                Integer(Section(block, "[stage clear count]", path).Values.Single(), path),
                Rewards(archive, Section(block, "[reward item]", path).Values, path));
            Require(result.Id > 0 && result.ClearCount is > 0 and <= 12, path, "invalid achievement");
            return result;
        }).ToImmutableArray();
    }

    private static ImmutableArray<GuardianAltarReward> Rewards(PvfArchive archive, IReadOnlyList<PvfToken> tokens, string path)
    {
        Require(tokens.Count > 0 && tokens.Count % 3 == 0, path, "invalid reward rows");
        return Enumerable.Range(0, tokens.Count / 3).Select(i =>
        {
            var reward = new GuardianAltarReward(Text(archive, tokens[i * 3], path), Integer(tokens[i * 3 + 1], path),
                Integer(tokens[i * 3 + 2], path));
            Require(reward.Kind == "normal" && reward.ItemId > 0 && reward.Count > 0, path, "unsupported reward");
            return reward;
        }).ToImmutableArray();
    }

    private static GuardianAltarDifficulty Difficulty(string value, string path) => value switch
    {
        "easy" => GuardianAltarDifficulty.Easy, "medium" => GuardianAltarDifficulty.Medium,
        "hard" => GuardianAltarDifficulty.Hard, _ => throw new InvalidDataException($"PVF '{path}': invalid difficulty {value}."),
    };
    private static string Resolve(PvfArchive archive, string directory, int id) =>
        PvfListFile.Read(archive, $"{directory}/{directory}.lst").FirstOrDefault(x => x.Id == id).RelativePath is { } path
            ? $"{directory}/{path}" : throw new InvalidDataException($"PVF '{directory}': missing id {id}.");
    private static PvfSection Section(IReadOnlyList<PvfSection> sections, string name, string path) =>
        sections.SingleOrDefault(x => x.Name == name) ?? throw new InvalidDataException($"PVF '{path}': missing {name}.");
    private static IReadOnlyList<PvfSection> Block(IReadOnlyList<PvfSection> sections, string name, string path)
    {
        var found = Blocks(sections, name, path).ToArray();
        Require(found.Length == 1, path, $"expected one {name} block, found {found.Length}");
        return found[0];
    }

    private static IEnumerable<IReadOnlyList<PvfSection>> Blocks(IReadOnlyList<PvfSection> sections, string name, string path)
    {
        var closing = "[/" + name[1..];
        for (var i = 0; i < sections.Count; i++)
        {
            if (sections[i].Name != name) continue;
            var first = i;
            var depth = 1;
            while (++i < sections.Count)
            {
                if (sections[i].Name == name) depth++;
                if (sections[i].Name == closing) depth--;
                if (depth == 0) break;
            }
            Require(i < sections.Count, path, $"unclosed {name}");
            yield return sections.Skip(first).Take(i - first).ToArray();
        }
    }

    private static int Integer(PvfToken value, string path) => value.Kind == PvfTokenKind.Int32 ? value.Value
        : throw new InvalidDataException($"PVF '{path}': expected integer.");
    private static string Text(PvfArchive archive, PvfToken value, string path) => value.IsString ? archive.ResolveString(value.Value)
        : throw new InvalidDataException($"PVF '{path}': expected string.");
    private static void Require(bool condition, string path, string message)
    {
        if (!condition) throw new InvalidDataException($"PVF '{path}': {message}.");
    }
}
