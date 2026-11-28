using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Data.Raids;
using Dfo.Server.Data.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfAntonRaidContentSource(PvfArchiveProvider provider) : IAntonRaidContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private readonly Lazy<AntonRaidRules> rules = new(() => Read(provider.Archive));

    public string Name => "anton-raid";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Load().Phases.Count);

    public AntonRaidRules Load() => rules.Value;

    internal static AntonRaidRules Read(PvfArchive archive)
    {
        const string list = "etc/raid/raid.lst";
        PvfField.Require(PvfListFile.Paths(archive, list).TryGetValue(0, out var path), list, "missing Anton raid");
        var document = PvfDocument.Open(archive, path!);
        var root = document.AtRoot();
        var symbols = ReadSymbols(archive, path + ".symbol");
        var minimum = Read("[raid start minimum member]", 1)[0];
        var maximum = Read("[raid member max]", 1)[0];
        var phases = Read("[phase max]", 1)[0];
        if (minimum > maximum || maximum > 20 || phases != 2)
            throw new InvalidDataException($"{path} declares an unsupported raid size or phase count.");

        var ticket = Read("[entry cost]", 2);
        var retry = Read("[retry item]", 2);
        var limits = Read("[phase time over]", phases);
        var shield = Read("[shield charge rate]", 2);
        var hatcheries = Read("[hatchery info]", 6);
        var phaseRules = new List<AntonRaidPhaseRule>();
        var sections = document.Sections.Select(section => section with { Name = section.Name.ToUpperInvariant() }).ToArray();
        for (var index = 0; index < sections.Length; index++)
        {
            if (sections[index].Name != "[PHASE]") continue;
            var end = Closing(sections, index, "[/PHASE]", path!);
            phaseRules.Add(ReadPhase(archive, sections[(index + 1)..end], symbols, path!));
            index = end;
        }
        PvfField.Require(phaseRules.Count == phases && hatcheries[0] == hatcheries.Count - 2
            && hatcheries[1] <= hatcheries[0] && hatcheries.Skip(2).Distinct().Count() == hatcheries[0]
            && hatcheries.Skip(2).All(id => phaseRules[1].Maps.Any(map => map.DungeonId == id)), path!, "invalid raid phases or hatcheries");
        return new AntonRaidRules(minimum, maximum,
            Seconds("[start delay time]"), Seconds("[phase break time]"), Seconds("[disconn member wait time]"),
            ticket[0], ticket[1], retry[0], retry[1], Array.AsReadOnly(limits.Select(value => TimeSpan.FromSeconds(value)).ToArray()))
        {
            Phases = phaseRules.AsReadOnly(),
            Symbols = symbols,
            Hatcheries = Array.AsReadOnly(hatcheries.Skip(2).ToArray()),
            OpenHatcheries = hatcheries[1],
            ShieldChargeRate = shield[0],
            InfectedShieldChargeRate = shield[1],
            Buffs = ReadBuffs(archive, path + ".buff"),
        };

        TimeSpan Seconds(string field) => TimeSpan.FromSeconds(Read(field, 1)[0]);

        IReadOnlyList<int> Read(string field, int count)
        {
            var values = root.Read(PvfField.Int32List(root.Sections.SingleOrDefault(section =>
                section.Name.Equals(field, StringComparison.OrdinalIgnoreCase))?.Name ?? field));
            if (values.Count != count || values.Any(value => value <= 0))
                throw new InvalidDataException($"{path} declares an invalid {field}.");
            return values;
        }
    }

    private static IReadOnlyList<AntonRaidBuffRule> ReadBuffs(PvfArchive archive, string path)
    {
        var sections = PvfDocument.Open(archive, path).Sections
            .Select(section => section with { Name = section.Name.ToUpperInvariant() }).ToArray();
        var buffs = new List<AntonRaidBuffRule>();
        for (var index = 0; index < sections.Length; index++)
        {
            if (sections[index].Name != "[BUFF]") continue;
            var end = Closing(sections, index, "[/BUFF]", path);
            PvfField.Require(end == index + 3 && sections[index + 2].Name is "[RAID]" or "[PARTY]", path, "invalid raid buff");
            var scope = sections[index + 2];
            byte type = sections[index + 1].Name switch
            {
                "[INCREASE COIN]" => 0,
                "[INCREASE TIME]" => 1,
                "[ATTACK BONUS]" => 2,
                "[INVINCIBLE]" => 3,
                "[RESTORE]" => 4,
                _ => throw new InvalidDataException($"{path}: unknown Anton buff {sections[index + 1].Name}."),
            };
            buffs.Add(new(type, scope.Name == "[PARTY]", Number(scope, 0, 3, path), Number(scope, 1, 3, path),
                Number(scope, 2, 3, path)));
            index = end;
        }
        PvfField.Require(buffs.Count == 5 && buffs.Select(buff => buff.Type).Distinct().Count() == buffs.Count,
            path, "invalid Anton raid buff list");
        return buffs.AsReadOnly();
    }

    private static IReadOnlyDictionary<string, uint> ReadSymbols(PvfArchive archive, string path)
    {
        var index = archive.FindFile(path);
        PvfField.Require(index >= 0 && archive.GetEntry(index).IsScript
            && archive.GetFileBytes(index).Length % (2 * PvfToken.Size) == 0, path, "invalid symbol script");
        var reader = archive.OpenScript(index);
        var symbols = new Dictionary<string, uint>(StringComparer.Ordinal);
        var ids = new HashSet<uint>();
        while (reader.Read(out var number))
        {
            var hasName = reader.Read(out var name);
            PvfField.Require(number.Kind == PvfTokenKind.Int32 && number.Value > 0 && hasName
                && name.Kind == PvfTokenKind.Identifier, path, "expected symbol ID and name");
            var value = checked((uint)number.Value);
            PvfField.Require(ids.Add(value) && symbols.TryAdd(archive.ResolveString(name.Value).ToUpperInvariant(), value),
                path, "duplicate symbol");
        }
        PvfField.Require(symbols.Count > 0, path, "empty raid symbol table");
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, uint>(symbols);
    }

    private static AntonRaidPhaseRule ReadPhase(PvfArchive archive, PvfSection[] sections,
        IReadOnlyDictionary<string, uint> symbols, string path)
    {
        var maps = new List<AntonRaidMapRule>();
        var triggers = new List<AntonRaidTrigger>();
        for (var cursor = 0; cursor < sections.Length; cursor++)
        {
            switch (sections[cursor].Name)
            {
                case "[DUNGEON INFO]":
                    var mapEnd = Closing(sections, cursor, "[/DUNGEON INFO]", path);
                    var block = sections[(cursor + 1)..mapEnd];
                    PvfField.Require(block.Length == 5 && block[0].Name == "[INDEX]" && block[1].Name == "[INIT STATE]"
                        && block[2] is { Name: "[HP INFO]", Values.Count: 0 } && block[4].Name == "[PARTY MAX]", path, "invalid dungeon rule");
                    var id = Number(block[0], 0, 1, path);
                    var status = MapStatus(archive, block[1], 0, 1, path);
                    var clears = Number(block[3], 0, 1, path);
                    var partyLimit = Number(block[4], 0, 1, path);
                    PvfField.Require(id > 0 && clears > 0 && partyLimit > 0, path, "invalid dungeon limits");
                    var dungeonPath = PvfListFile.Resolve(archive, "dungeon", id);
                    var dungeon = PvfDocument.Open(archive, dungeonPath).AtRoot();
                    // Fleet Fire has two declarations in dev PVF. Retain the tighter budget.
                    var coinLimit = dungeon.ReadAll(PvfField.Int32("[coin limit]", minimum: 0)).Min();
                    var difficulty = dungeon.Read(PvfField.Int32("[designate dungeon difficulty]", minimum: 0, maximum: 4));
                    maps.Add(new(id, status, Symbol(symbols, block[3], path), (uint)clears, partyLimit, coinLimit,
                        (DungeonDifficulty)difficulty));
                    cursor = mapEnd;
                    break;
                case "[TRIGGER]":
                    var conditionEnd = Closing(sections, cursor, "[/TRIGGER]", path);
                    PvfField.Require(conditionEnd + 1 < sections.Length && sections[conditionEnd + 1].Name == "[BEHAVIOR]",
                        path, "trigger has no behavior");
                    var actionEnd = Closing(sections, conditionEnd + 1, "[/BEHAVIOR]", path);
                    triggers.Add(new(
                        ReadConditions(archive, sections[(cursor + 1)..conditionEnd], symbols, path),
                        ReadActions(archive, sections[(conditionEnd + 2)..actionEnd], symbols, path)));
                    cursor = actionEnd;
                    break;
            }
        }
        PvfField.Require(maps.Count != 0 && maps.Select(map => map.DungeonId).Distinct().Count() == maps.Count
            && triggers.Count != 0, path, "empty phase or duplicate map");
        return new(maps.AsReadOnly(), triggers.AsReadOnly());
    }

    private static IReadOnlyList<AntonRaidCondition> ReadConditions(PvfArchive archive, PvfSection[] sections,
        IReadOnlyDictionary<string, uint> symbols, string path)
    {
        var conditions = new List<AntonRaidCondition>();
        for (var index = 0; index < sections.Length; index++)
        {
            var section = sections[index];
            conditions.Add(section.Name switch
            {
                "[IF]" => ReadSymbolCondition(Next(sections, ref index, section, path), symbols, path),
                "[CHECK DUNGEON STATE]" => new(AntonRaidConditionKind.MapStatus, (uint)Number(section, 0, 2, path),
                    (uint)MapStatus(archive, section, 1, 2, path)),
                "[ON CHANGE DUNGEON STATE]" => new(AntonRaidConditionKind.MapChanged, (uint)Number(section, 0, 1, path)),
                "[ON CHANGE SYMBOL]" => new(AntonRaidConditionKind.SymbolChanged,
                    Symbol(symbols, Next(sections, ref index, section, path), path)),
                "[CHECK TIMER END]" => new(AntonRaidConditionKind.TimerEnded, (uint)Number(section, 1, 2, path),
                    (uint)Number(section, 0, 2, path)),
                _ => throw new InvalidDataException($"{path}: unsupported Anton raid condition {section.Name}."),
            });
        }
        return conditions.AsReadOnly();
    }

    private static AntonRaidCondition ReadSymbolCondition(PvfSection section, IReadOnlyDictionary<string, uint> symbols, string path) =>
        new(AntonRaidConditionKind.Symbol, Symbol(symbols, section, path), (uint)Number(section, 0, 1, path));

    private static IReadOnlyList<AntonRaidAction> ReadActions(PvfArchive archive, PvfSection[] sections,
        IReadOnlyDictionary<string, uint> symbols, string path)
    {
        var actions = new List<AntonRaidAction>();
        for (var index = 0; index < sections.Length; index++)
        {
            var section = sections[index];
            actions.Add(section.Name switch
            {
                "[SET DUNGEON STATE]" => new(AntonRaidActionKind.SetMap, (uint)Number(section, 0, 2, path),
                    (uint)MapStatus(archive, section, 1, 2, path)),
                "[RESERVE DUNGEON STATE]" => new(AntonRaidActionKind.ReserveMap, (uint)Number(section, 0, 3, path),
                    (uint)MapStatus(archive, section, 1, 3, path), Number(section, 2, 3, path)),
                "[SET]" => ReadSet(Next(sections, ref index, section, path), symbols, path),
                "[SET TIMER]" => new(AntonRaidActionKind.SetTimer, (uint)Number(section, 1, 3, path),
                    (uint)Number(section, 0, 3, path), Number(section, 2, 3, path)),
                "[RESET TIMER]" => new(AntonRaidActionKind.ResetTimer, (uint)Number(section, 1, 2, path),
                    (uint)Number(section, 0, 2, path)),
                "[CLEAR PHASE]" => new(AntonRaidActionKind.ClearPhase),
                "[OPEN RANDOM HATCHERY]" => new(AntonRaidActionKind.OpenHatcheries),
                _ => throw new InvalidDataException($"{path}: unsupported Anton raid action {section.Name}."),
            });
        }
        return actions.AsReadOnly();
    }

    private static AntonRaidAction ReadSet(PvfSection section, IReadOnlyDictionary<string, uint> symbols, string path) =>
        new(AntonRaidActionKind.SetSymbol, Symbol(symbols, section, path), (uint)Number(section, 0, 1, path));

    private static PvfSection Next(PvfSection[] sections, ref int index, PvfSection command, string path)
    {
        PvfField.Require(command.Values.Count == 0 && index + 1 < sections.Length, path, $"missing symbol operand for {command.Name}");
        return sections[++index];
    }

    private static uint Symbol(IReadOnlyDictionary<string, uint> symbols, PvfSection section, string path) =>
        symbols.TryGetValue(section.Name, out var value) ? value
            : throw new InvalidDataException($"{path}: undeclared symbol {section.Name}.");

    private static int Closing(PvfSection[] sections, int cursor, string close, string path)
    {
        var index = Array.FindIndex(sections, cursor + 1, section => section.Name == close);
        return index >= 0 ? index : throw new InvalidDataException($"{path}: missing {close}.");
    }

    private static int Number(PvfSection section, int index, int length, string path)
    {
        PvfField.Require(section.Values.Count == length
            && section.Values[index] is { Kind: PvfTokenKind.Int32, Value: >= 0 }, path, $"invalid operand for {section.Name}");
        return section.Values[index].Value;
    }

    private static AntonRaidMapStatus MapStatus(PvfArchive archive, PvfSection section, int index, int length, string path)
    {
        PvfField.Require(section.Values.Count == length && section.Values[index].Kind == PvfTokenKind.QuotedString,
            path, $"invalid dungeon status in {section.Name}");
        return archive.ResolveString(section.Values[index].Value).ToLowerInvariant() switch
        {
            "open" => AntonRaidMapStatus.Open,
            "close" => AntonRaidMapStatus.Closed,
            "hide" => AntonRaidMapStatus.Hidden,
            "clear" => AntonRaidMapStatus.Cleared,
            _ => throw new InvalidDataException($"{path}: unknown dungeon status in {section.Name}."),
        };
    }
}
