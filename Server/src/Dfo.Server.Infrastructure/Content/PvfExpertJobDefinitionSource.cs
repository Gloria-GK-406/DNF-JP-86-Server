using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Data.ExpertJobs;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfExpertJobDefinitionSource(PvfArchiveProvider provider) : IExpertJobDefinitionSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<ExpertJobType, ExpertJobDefinition?> definitions = [];

    public ValueTask<ExpertJobDefinition?> FindAsync(ExpertJobType type, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (type is ExpertJobType.None || !Enum.IsDefined(type)) return ValueTask.FromResult<ExpertJobDefinition?>(null);
        lock (gate)
        {
            if (!definitions.TryGetValue(type, out var definition))
                definitions[type] = definition = Read(provider.Archive, type);
            return ValueTask.FromResult(definition);
        }
    }

    private static ExpertJobDefinition? Read(PvfArchive archive, ExpertJobType type)
    {
        const string catalog = "character/expertjob.lst";
        if (archive.FindFile(catalog) < 0) return null;
        var relative = PvfListFile.Read(archive, catalog).SingleOrDefault(entry => entry.Id == (int)type).RelativePath;
        if (string.IsNullOrEmpty(relative)) return null;
        var path = "character/" + relative;
        var doc = PvfDocument.Open(archive, path);
        var root = doc.AtRoot();
        var experience = root.Read(PvfField.Tokens("[expertness exp]"));
        if (experience.Count == 0 || experience.Count % 3 != 0)
            throw new InvalidDataException($"Invalid experience rows in '{path}'.");
        var thresholds = experience.Chunk(3).Select(row => checked((uint)Int(row[0], path))).ToArray();
        if (!thresholds.SequenceEqual(thresholds.Distinct().Order()))
            throw new InvalidDataException($"Invalid experience thresholds in '{path}'.");
        var costs = root.Read(PvfField.Int32List("[giveup cost]"));
        if (costs.Count is < 1 or > 256 || costs.Any(cost => cost < 0))
            throw new InvalidDataException($"Invalid giveup costs in '{path}'.");

        var qualifications = new Dictionary<byte, int>();
        if (type == ExpertJobType.Enchanter)
        {
            byte? rarity = null;
            foreach (var section in doc.Sections.SkipWhile(section => section.Name != "[rarity recipe]")
                         .TakeWhile(section => section.Name != "[/rarity recipe]"))
            {
                if (section.Name == "[rarity]" && section.Values.Count == 1)
                    rarity = checked((byte)Int(section.Values[0], path));
                if (section.Name == "[expert job level]" && rarity is { } key && section.Values.Count == 1)
                {
                    qualifications.Add(key, Int(section.Values[0], path));
                    rarity = null;
                }
            }
            if (qualifications.Count == 0) throw new InvalidDataException($"Missing card qualifications in '{path}'.");
        }

        return new(type, thresholds, costs,
            root.Read(PvfField.Int32List("[connect quest list]")).ToHashSet(),
            root.Read(PvfField.Int32List("[clear quests]")).ToHashSet(),
            Pairs(root, "[skill]", path).ToDictionary(pair => pair.Key, pair => checked((byte)pair.Value)),
            root.Read(PvfField.Int32("[giveup delete item]", optional: true)),
            root.Read(PvfField.Int32("[endurance initial value]", optional: true)),
            Pairs(root, "[auto learn recipe]", path), qualifications)
        {
            MaximumStoreCharge = root.Read(PvfField.Int32("[limit store charge]", optional: true)),
        };
    }

    private static Dictionary<int, int> Pairs(PvfDocument doc, string tag, string path)
    {
        var values = doc.Read(PvfField.Int32List(tag, optional: true)) ?? [];
        if (values.Count % 2 != 0) throw new InvalidDataException($"Invalid {tag} rows in '{path}'.");
        return values.Chunk(2).ToDictionary(pair => pair[0], pair => pair[1]);
    }

    private static int Int(PvfToken value, string path) => value.Kind == PvfTokenKind.Int32
        ? value.Value : throw new InvalidDataException($"Expected an integer in '{path}'.");
}
