using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Achievements;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfAchievementDefinitionSource(PvfArchiveProvider provider) : IAchievementDefinitionSource
{
    private readonly Lazy<IReadOnlyList<AchievementDefinition>> catalog = new(() => Read(provider));
    public IReadOnlyList<AchievementDefinition> Load() => catalog.Value;
    private static IReadOnlyList<AchievementDefinition> Read(PvfArchiveProvider provider)
    {
        var archive = provider.Archive;
        var result = new List<AchievementDefinition>();
        foreach (var (id, relative) in PvfListFile.Read(archive, "n_quest/quest.lst"))
        {
            var path = "n_quest/" + relative;
            if (archive.FindFile(path) < 0) continue;
            var document = PvfDocument.Open(archive, path).AtRoot();
            string Text(string field)
            {
                var sections = document.Sections.Where(x => x.Name == field).ToArray();
                return sections.Length == 1 && sections[0].Values.Count == 1 && sections[0].Values[0].IsString
                    ? archive.ResolveString(sections[0].Values[0].Value).Trim('[', ']').ToLowerInvariant() : "";
            }
            int Number(string field, int fallback) => document.TryRead(PvfField.Int32(field, optional: true), out var value) ? value : fallback;
            IReadOnlyList<int> Numbers(string field) => document.ReadAll(PvfField.Int32List(field, optional: true)).SelectMany(x => x).ToArray();
            if (Text("[grade]") is not ("title" or "achievement")) continue;
            var levels = Numbers("[level]");
            result.Add(new(id, levels.Count > 0 ? levels[0] : 0, levels.Count > 1 ? levels[1] : int.MaxValue,
                new(Text("[type]"), Number("[sub type]", -1), Numbers("[int data]"), Number("[check count]", 1)),
                Numbers("[pre required quest]"))
            {
                PrerequisiteGroups = document.ReadAll(PvfField.Int32List("[pre required quest]", optional: true)).ToArray(),
                SupportedRestrictions = (!document.Sections.Any(x => x.Name == "[job]") || Text("[job]") == "all")
                    && (!Numbers("[grow type]").Any(x => x >= 0))
                    && !document.Sections.Any(x => x.Name is "[target character]" or "[creature kind]" or "[creature level]" or "[expertjob level]" or "[required quest answer]" or "[collision quest]" or "[account collision quest]"),
            });
        }
        return result.AsReadOnly();
    }
}

internal sealed class PvfTitleBookDefinitionSource(PvfArchiveProvider provider) : ITitleBookDefinitionSource
{
    private readonly Lazy<IReadOnlyList<TitleBookCategoryDefinition>> catalog = new(() => Read(provider));
    public IReadOnlyList<TitleBookCategoryDefinition> Load() => catalog.Value;
    private static IReadOnlyList<TitleBookCategoryDefinition> Read(PvfArchiveProvider provider)
    {
        const string path = "etc/titlebook.etc";
        var archive = provider.Archive;
        var result = new List<TitleBookCategoryDefinition>();
        string[] names = ["general", "specific", "pvp", "despair", "event"];
        foreach (var section in PvfDocument.Open(archive, path).Sections.Where(x => x.Name == "[title collection info]"))
        {
            var values = section.Values;
            if (values.Count < 2 || !values[0].IsString) throw new InvalidDataException("Missing title category.");
            var category = Array.IndexOf(names, archive.ResolveString(values[0].Value).Trim('[', ']').ToLowerInvariant());
            var at = 1;
            int Next()
            {
                if (at >= values.Count || values[at].Kind != PvfTokenKind.Int32) throw new InvalidDataException("Malformed title slot.");
                return values[at++].Value;
            }
            var capacity = Next();
            if (category < 0 || capacity is < 0 or > ushort.MaxValue) throw new InvalidDataException("Invalid title capacity.");
            var slots = new List<TitleBookSlotDefinition>();
            while (at < values.Count)
            {
                var index = Next(); var type = Next();
                if (index < 0 || index >= capacity || slots.Any(x => x.Index == index)) throw new InvalidDataException("Invalid title index.");
                if (type == -1) { slots.Add(new(category, index, type, -1, [])); continue; }
                var quest = Next(); var count = Next();
                if (count < 0 || count > values.Count - at) throw new InvalidDataException("Invalid allowed title count.");
                var items = Enumerable.Range(0, count).Select(_ => Next()).ToArray();
                slots.Add(new(category, index, type, quest, items));
            }
            result.Add(new(category, capacity, slots.AsReadOnly()));
        }
        if (result.Count != 5 || result.Select(x => x.Category).Distinct().Count() != 5)
            throw new InvalidDataException("Title book must define its five categories.");
        return result.OrderBy(x => x.Category).ToArray();
    }
}
