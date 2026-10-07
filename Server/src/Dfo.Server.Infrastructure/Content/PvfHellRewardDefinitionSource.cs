using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfHellRewardDefinitionSource(PvfArchiveProvider provider) : IHellRewardDefinitionSource
{
    private readonly Lazy<HellRewardDefinition> definition = new(() => Read(provider));
    private readonly Lock gate = new();
    private readonly Dictionary<(int, bool), bool> flags = [];
    private readonly Dictionary<bool, Dictionary<int, string>> paths = [];

    public HellRewardDefinition Load() => definition.Value;

    public bool IsHellMonster(int code, bool isAiCharacter)
    {
        lock (gate)
        {
            if (flags.TryGetValue((code, isAiCharacter), out var flag)) return flag;
            var archive = provider.Archive;
            var directory = isAiCharacter ? "aicharacter" : "monster";
            if (!paths.TryGetValue(isAiCharacter, out var list))
                paths[isAiCharacter] = list = PvfListFile.Read(archive, $"{directory}/{directory}.lst")
                    .GroupBy(x => x.Item1).ToDictionary(x => x.Key, x => x.First().Item2);
            if (!list.TryGetValue(code, out var relative))
                throw new InvalidDataException($"Missing hell reward actor {directory}/{code}.");
            var path = relative.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase)
                ? relative : directory + "/" + relative;
            var document = PvfDocument.Open(archive, path).AtRoot();
            flag = document.ReadAll(PvfField.Int32("[hell monster]", optional: true)).Any(value => value != 0);
            flags[(code, isAiCharacter)] = flag;
            return flag;
        }
    }

    private static HellRewardDefinition Read(PvfArchiveProvider provider)
    {
        var archive = provider.Archive;
        const string path = "etc/itemdropinfo_monster_hell.etc";
        var document = PvfDocument.Open(archive, path);
        int[] Values(string name) => PvfField.Int32List(name)
            .Decode(archive, document.Sections.Single(x => x.Name == name).Values, path).ToArray();
        var count = Values("[drop prob count]").Single();
        var rates = Values("[dungeon difficulty drop prob]");
        if (count <= 0 || rates.Length != count * 7) throw new InvalidDataException("Invalid hell reward rate rows.");
        var bands = new List<HellRewardRateBand>();
        for (var at = 0; at < rates.Length; at += 7)
        {
            if (rates[at] <= 0 || rates[at + 1] < rates[at] || rates.Skip(at + 2).Take(5).Any(x => x < 0)
                || bands.Any(x => rates[at] <= x.High && rates[at + 1] >= x.Low))
                throw new InvalidDataException("Invalid or overlapping hell reward level band.");
            bands.Add(new(rates[at], rates[at + 1], rates.Skip(at + 2).Take(5).ToArray()));
        }
        var thresholds = Values("[basis of rarity dicision]");
        var rows = thresholds[0];
        if (rows <= 0 || (thresholds.Length - 1) % rows != 0)
            throw new InvalidDataException("Invalid hell rarity rows.");
        var width = (thresholds.Length - 1) / rows;
        if (width is < 6 or > 7) throw new InvalidDataException("Unsupported hell rarity width.");
        var ladders = new List<IReadOnlyList<int>>();
        for (var row = 0; row < rows; row++)
        {
            var ladder = thresholds.Skip(1 + row * width).Take(width).ToArray();
            // A21's unreachable final sentinel can be lower than its predecessor.
            if (ladder.Any(x => x < 0) || ladder[^1] < 1000000
                || Enumerable.Range(1, width - 1).Any(i => Math.Min(ladder[i], 1000001) < Math.Min(ladder[i - 1], 1000001)))
                throw new InvalidDataException("Invalid hell rarity cumulative thresholds.");
            ladders.Add(ladder);
        }
        var grades = Values("[item drop ref table]");
        if (grades.Length % 3 != 0) throw new InvalidDataException("Invalid hell grade rows.");
        var windows = new Dictionary<int, ItemGradeBand>();
        for (var at = 0; at < grades.Length; at += 3)
        {
            if (grades[at] <= 0 || grades[at + 1] < 0 || grades[at + 2] <= 0)
                throw new InvalidDataException("Invalid hell grade window.");
            windows.Add(grades[at], new(Math.Max(0, grades[at] - grades[at + 1]), grades[at] + grades[at + 2] - 1));
        }
        var pieces = new Dictionary<int, int>();
        const string piecePath = "etc/epicpieceinfo.etc";
        if (archive.FindFile(piecePath) >= 0)
        {
            var pieceDocument = PvfDocument.Open(archive, piecePath);
            var section = pieceDocument.Sections.Single(x => x.Name == "[piece list]");
            var values = PvfField.Int32List(section.Name).Decode(archive, section.Values, piecePath).ToArray();
            if (values.Length % 2 != 0 || values.Any(x => x <= 0)) throw new InvalidDataException("Invalid epic piece pairs.");
            for (var at = 0; at < values.Length; at += 2) pieces.Add(values[at], values[at + 1]);
        }
        return new(bands, ladders, windows, pieces);
    }
}
