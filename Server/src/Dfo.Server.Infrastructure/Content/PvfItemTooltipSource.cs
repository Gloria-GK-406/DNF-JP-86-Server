using System.Globalization;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Reads the static tooltip facts of one item template, in the order the template declares its stats.</summary>
/// <remarks>
/// Only root sections are read, so conditional <c>[if]/[then]</c> effects and skill blocks never leak into the
/// stat list. Tags without a known label are skipped rather than guessed. Labels and units were checked against
/// templates 27650, 2681114, 3037 and 690001556: attack/cast/move speed are 0.1% units, weight is grams, and the
/// <c>[equipment … attack/defense]</c> pair is the quality range written high first.
/// </remarks>
internal sealed class PvfItemTooltipSource(PvfArchiveProvider provider, ILogger<PvfItemTooltipSource> logger) : IItemTooltipSource
{
    private enum Format { Signed, Range, Rate, Percent, Element }

    private static readonly Dictionary<string, (string Label, Format Format)> Stats = new(StringComparer.Ordinal)
    {
        ["[equipment physical attack]"] = ("物理攻击力", Format.Range), ["[equipment magical attack]"] = ("魔法攻击力", Format.Range),
        ["[separate attack]"] = ("独立攻击力", Format.Range),
        ["[equipment physical defense]"] = ("物理防御力", Format.Range), ["[equipment magical defense]"] = ("魔法防御力", Format.Range),
        ["[physical attack]"] = ("力量", Format.Signed), ["[magical attack]"] = ("智力", Format.Signed),
        ["[physical defense]"] = ("体力", Format.Signed), ["[magical defense]"] = ("精神", Format.Signed),
        ["[HP MAX]"] = ("HP MAX", Format.Signed), ["[MP MAX]"] = ("MP MAX", Format.Signed),
        ["[attack speed]"] = ("攻击速度", Format.Rate), ["[cast speed]"] = ("施放速度", Format.Rate), ["[move speed]"] = ("移动速度", Format.Rate),
        ["[physical critical hit]"] = ("物理暴击率", Format.Percent), ["[magical critical hit]"] = ("魔法暴击率", Format.Percent),
        ["[stuck]"] = ("命中率", Format.Signed), ["[stuck resistance]"] = ("回避率", Format.Signed),
        ["[hit recovery]"] = ("硬直", Format.Signed), ["[jump power]"] = ("跳跃力", Format.Signed),
        ["[HP regen speed]"] = ("HP恢复速度", Format.Signed), ["[MP regen speed]"] = ("MP恢复速度", Format.Signed),
        ["[elemental property]"] = ("攻击属性", Format.Element),
        ["[all elemental attack]"] = ("所有属性强化", Format.Signed), ["[fire attack]"] = ("火属性强化", Format.Signed),
        ["[water attack]"] = ("冰属性强化", Format.Signed), ["[light attack]"] = ("光属性强化", Format.Signed), ["[dark attack]"] = ("暗属性强化", Format.Signed),
        ["[all elemental resistance]"] = ("所有属性抗性", Format.Signed), ["[fire resistance]"] = ("火属性抗性", Format.Signed),
        ["[water resistance]"] = ("冰属性抗性", Format.Signed), ["[light resistance]"] = ("光属性抗性", Format.Signed), ["[dark resistance]"] = ("暗属性抗性", Format.Signed),
        ["[all activestatus resistance]"] = ("所有异常状态抗性", Format.Signed),
        ["[freeze resistance]"] = ("冰冻抗性", Format.Signed), ["[poison resistance]"] = ("中毒抗性", Format.Signed),
        ["[bleeding resistance]"] = ("出血抗性", Format.Signed), ["[curse resistance]"] = ("诅咒抗性", Format.Signed),
        ["[confuse resistance]"] = ("混乱抗性", Format.Signed), ["[burn resistance]"] = ("灼伤抗性", Format.Signed),
        ["[stun resistance]"] = ("眩晕抗性", Format.Signed), ["[blind resistance]"] = ("失明抗性", Format.Signed),
        ["[lightning resistance]"] = ("感电抗性", Format.Signed), ["[sleep resistance]"] = ("睡眠抗性", Format.Signed),
        ["[slow resistance]"] = ("减速抗性", Format.Signed), ["[stone resistance]"] = ("石化抗性", Format.Signed),
        ["[hold resistance]"] = ("束缚抗性", Format.Signed),
    };

    private static readonly Dictionary<string, string> Elements = new(StringComparer.Ordinal)
    { ["fire element"] = "火属性", ["water element"] = "冰属性", ["light element"] = "光属性", ["dark element"] = "暗属性" };

    // Keyed by [item group name] or, when a template has none, the directory under .../weapon/.
    private static readonly Dictionary<string, string> Weapons = new(StringComparer.Ordinal)
    {
        ["ssword"] = "短剑", ["katana"] = "太刀", ["club"] = "钝器", ["lswd"] = "巨剑", ["beamswd"] = "光剑",
        ["knuckle"] = "手套", ["gauntlet"] = "臂铠", ["claw"] = "爪", ["bglove"] = "拳套", ["tonfa"] = "东方棍",
        ["revolver"] = "左轮枪", ["automatic"] = "自动手枪", ["musket"] = "步枪", ["hcannon"] = "手炮", ["bowgun"] = "手弩",
        ["spear"] = "矛", ["pole"] = "棍棒", ["rod"] = "魔杖", ["staff"] = "法杖", ["broom"] = "扫把",
        ["cross"] = "十字架", ["rosary"] = "念珠", ["totem"] = "图腾", ["scythe"] = "镰刀", ["axe"] = "战斧",
        ["dagger"] = "匕首", ["twinswd"] = "双剑", ["wand"] = "手杖", ["chakraweapon"] = "苦无",
        ["pike"] = "长枪", ["halberd"] = "战戟",
    };

    private static readonly Dictionary<string, string> Materials = new(StringComparer.Ordinal)
    { ["cl"] = "布甲", ["lt"] = "皮甲", ["la"] = "轻甲", ["ha"] = "重甲", ["mt"] = "板甲" };

    private static readonly Dictionary<string, string> Parts = new(StringComparer.Ordinal)
    {
        ["coat"] = "上衣", ["shoulder"] = "头肩", ["pants"] = "下装", ["waist"] = "腰带", ["shoes"] = "鞋",
        ["amulet"] = "项链", ["wrist"] = "手镯", ["ring"] = "戒指", ["support"] = "辅助装备", ["magic stone"] = "魔法石",
        ["title name"] = "称号", ["creature"] = "宠物", ["artifact red"] = "宠物装备（红）", ["artifact blue"] = "宠物装备（蓝）",
        ["artifact green"] = "宠物装备（绿）",
    };

    private static readonly Dictionary<string, string> AvatarParts = new(StringComparer.Ordinal)
    {
        ["hair"] = "发型", ["hat"] = "帽子", ["face"] = "脸部", ["breast"] = "胸部", ["coat"] = "上衣", ["pants"] = "下装",
        ["waist"] = "腰带", ["shoes"] = "鞋", ["skin"] = "皮肤", ["weapon"] = "武器", ["aurora"] = "光环",
    };

    private static readonly Dictionary<string, string> StackableTypes = new(StringComparer.Ordinal)
    {
        ["waste"] = "消耗品", ["unlimited waste"] = "消耗品", ["town and dungeon"] = "消耗品", ["unlimited town and dungeon"] = "消耗品",
        ["waste instant item"] = "消耗品", ["expert town potion"] = "消耗品", ["teleport potion"] = "消耗品", ["throw"] = "投掷消耗品",
        ["material"] = "材料", ["material expert job"] = "副职业材料", ["quest"] = "任务材料", ["quest receive"] = "任务材料",
        ["recipe"] = "设计图", ["avatar emblem"] = "徽章", ["dye"] = "染色剂", ["contract"] = "契约", ["coupon"] = "券",
        ["upgradable legacy"] = "附魔卡片", ["random upgradable legacy"] = "附魔卡片", ["multi upgradable legacy"] = "附魔卡片",
        ["booster"] = "礼包", ["booster selection"] = "礼包", ["booster random"] = "礼包", ["cera booster"] = "礼包",
        ["cera package"] = "礼包", ["usable cera package"] = "礼包",
    };

    // Sex of each job, used only to tell apart jobs whose growtype name is the same word (two 鬼剑士).
    private static readonly Dictionary<string, bool> Male = new(StringComparer.Ordinal)
    {
        ["swordman"] = true, ["at swordman"] = false, ["fighter"] = false, ["at fighter"] = true, ["gunner"] = true,
        ["at gunner"] = false, ["mage"] = false, ["at mage"] = true, ["priest"] = true, ["thief"] = false,
        ["knight"] = false, ["demonic lancer"] = true, ["demonic swordman"] = true, ["creator mage"] = false,
    };

    private readonly Lock _gate = new();
    private readonly Dictionary<int, ItemTooltip?> _cache = [];
    private Dictionary<int, (string Path, bool Equipment)>? _paths;
    private Dictionary<string, string>? _jobs;

    public ValueTask<ItemTooltip?> FindAsync(int itemId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_cache.TryGetValue(itemId, out var cached)) return ValueTask.FromResult(cached);
            var archive = provider.Archive;
            _paths ??= LoadPaths(archive);
            if (!_paths.TryGetValue(itemId, out var location)) return ValueTask.FromResult<ItemTooltip?>(null);
            ItemTooltip? tooltip;
            try { tooltip = Read(archive, itemId, location.Path, location.Equipment); }
            catch (InvalidDataException error)
            {
                logger.LogWarning(error, "Item tooltip omits malformed template {ItemId} at {Path}.", itemId, location.Path);
                tooltip = null;
            }
            _cache[itemId] = tooltip;
            return ValueTask.FromResult(tooltip);
        }
    }

    private static Dictionary<int, (string, bool)> LoadPaths(PvfArchive archive)
    {
        var result = new Dictionary<int, (string, bool)>();
        foreach (var row in PvfListFile.Read(archive, "stackable/stackable.lst")) result.TryAdd(row.Id, ("stackable/" + row.RelativePath, false));
        foreach (var row in PvfListFile.Read(archive, "equipment/equipment.lst")) result.TryAdd(row.Id, ("equipment/" + row.RelativePath, true));
        return result;
    }

    private ItemTooltip Read(PvfArchive archive, int itemId, string path, bool equipment)
    {
        var sections = PvfDocument.Open(archive, path).AtRoot().Sections;
        PvfSection? Find(string name) => sections.FirstOrDefault(section => section.Name == name);
        string? Text(string name)
        {
            var parts = Find(name)?.Values.Where(v => v.IsString).Select(v => archive.ResolveString(v.Value)).ToArray();
            var text = parts is { Length: > 0 } ? string.Concat(parts).Replace("%%", "%", StringComparison.Ordinal).Trim() : "";
            return text.Length == 0 ? null : text;
        }
        string? Key(string name) => Find(name) is { Values: [{ IsString: true } token, ..] } ? Unbracket(archive.ResolveString(token.Value)) : null;
        double? Number(string name) => Find(name) is { Values: [var token, ..] } ? Value(token) : null;
        int? Int(string name) => Number(name) is { } value ? (int)Math.Round(value) : null;

        var stats = new List<ItemTooltipStat>();
        foreach (var section in sections)
            if (Stats.TryGetValue(section.Name, out var stat) && Render(archive, section, stat.Format) is { } value) stats.Add(new(stat.Label, value));
        var jobs = Find("[usable job]")?.Values.Where(v => v.IsString).Select(v => Unbracket(archive.ResolveString(v.Value))).ToArray() ?? [];
        var weight = Number("[weight]");
        var period = Int("[usable period]");
        var type = equipment ? EquipmentType(Key("[equipment type]"), Text("[item group name]"), path) : StackableType(itemId, Key("[stackable type]"));
        return new(itemId, Text("[name]") ?? $"物品 {itemId.ToString(CultureInfo.InvariantCulture)}", Int("[rarity]"),
            equipment ? "equipment" : "stackable", type, Int("[minimum level]"), Int("[maximum level]"),
            jobs.Length == 0 || jobs.Contains("all") ? null : jobs.Select(JobName).Distinct().ToArray(),
            Key("[attach type]"), period > 0 ? period : null, Int("[daily delete item]") > 0,
            stats, Text("[explain]") ?? Text("[detail explain]") ?? Text("[basic explain]"), Text("[flavor text]"),
            Int("[durability]"), weight is { } grams ? Math.Round(grams / 1000, 3) : null, Int("[value]"), equipment ? null : Int("[stack limit]"));
    }

    private static string? Render(PvfArchive archive, PvfSection section, Format format)
    {
        if (format == Format.Element)
            return section.Values is [{ IsString: true } token, ..] && Elements.TryGetValue(Unbracket(archive.ResolveString(token.Value)), out var element) ? element : null;
        var numbers = section.Values.Select(Value).OfType<double>().ToArray();
        if (numbers.Length == 0) return null;
        return format switch
        {
            Format.Range => numbers.Length >= 2 && numbers[0] != numbers[1]
                ? $"{Plain(Math.Min(numbers[0], numbers[1]))} ~ {Plain(Math.Max(numbers[0], numbers[1]))}" : Plain(numbers[0]),
            Format.Rate => Sign(numbers[0]) + (numbers[0] / 10).ToString("0.0", CultureInfo.InvariantCulture) + "%",
            Format.Percent => Sign(numbers[0]) + Plain(numbers[0]) + "%",
            _ => Sign(numbers[0]) + Plain(numbers[0]),
        };
    }

    private static double? Value(PvfToken token) => token.Kind switch
    { PvfTokenKind.Int32 => token.Value, PvfTokenKind.Single => token.AsSingle(), _ => null };
    private static string Sign(double value) => value < 0 ? "" : "+";
    private static string Plain(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Unbracket(string value) => value.Replace("`", "", StringComparison.Ordinal).Trim().Trim('[', ']').Trim().ToLowerInvariant();

    private static string EquipmentType(string? type, string? group, string path)
    {
        if (type is null) return "装备";
        if (type.EndsWith(" avatar", StringComparison.Ordinal))
            return AvatarParts.TryGetValue(type[..^" avatar".Length], out var avatar) ? "时装 " + avatar : "时装";
        var normalized = group?.Trim().ToLowerInvariant();
        if (type == "weapon")
        {
            var segments = path.ToLowerInvariant().Split('/');
            var at = Array.IndexOf(segments, "weapon");
            var folder = at >= 0 && at + 2 < segments.Length ? segments[at + 1] : null;
            return normalized is not null && Weapons.TryGetValue(normalized, out var weapon) ? weapon
                : folder is not null && Weapons.TryGetValue(folder, out weapon) ? weapon : "武器";
        }
        if (!Parts.TryGetValue(type, out var part)) return "装备";
        return type is "coat" or "shoulder" or "pants" or "waist" or "shoes" && normalized?.Split(' ') is [var material, ..]
            && Materials.TryGetValue(material, out var name) ? name + " " + part : part;
    }

    private static string StackableType(int itemId, string? type) => type is not null && StackableTypes.TryGetValue(type, out var name) ? name
        : PvfEntryLifecycleContentSource.ResolveStackableItemKind(itemId, type is null ? null : "[" + type + "]") switch
        { 3 or 10 or 11 => "材料", 4 => "任务材料", 2 => "消耗品", _ => "其他" };

    private string JobName(string job)
    {
        _jobs ??= LoadJobs(provider.Archive);
        return _jobs.TryGetValue(job, out var name) ? name : job;
    }

    // character.lst -> each .chr: its [job] token and the first word of [growtype name] (the base class name).
    private static Dictionary<string, string> LoadJobs(PvfArchive archive)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (archive.FindFile("character/character.lst") < 0) return names;
        foreach (var (_, relative) in PvfListFile.Read(archive, "character/character.lst"))
        {
            var path = "character/" + relative;
            if (archive.FindFile(path) < 0) continue;
            var sections = PvfDocument.Open(archive, path).Sections;
            var job = sections.FirstOrDefault(s => s.Name == "[job]")?.Values.FirstOrDefault(v => v.IsString);
            var growtype = sections.FirstOrDefault(s => s.Name == "[growtype name]")?.Values.FirstOrDefault(v => v.IsString);
            if (job is not { } jobToken || growtype is not { } nameToken) continue;
            var name = archive.ResolveString(nameToken.Value).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (name is not null) names.TryAdd(Unbracket(archive.ResolveString(jobToken.Value)), name);
        }
        foreach (var group in names.GroupBy(pair => pair.Value).Where(group => group.Count() > 1).ToArray())
            foreach (var (job, name) in group)
                if (Male.TryGetValue(job, out var male)) names[job] = name + (male ? "（男）" : "（女）");
        return names;
    }
}
