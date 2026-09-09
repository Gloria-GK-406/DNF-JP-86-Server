using System.Collections.Concurrent;
using System.Collections.Frozen;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Infrastructure.Content;

// The byte job key bounds this directory at 256 entries; levels and learned sets never enter it.
internal sealed class PvfSkillPointDefinitionSource(PvfArchiveProvider provider) : ISkillRuleDefinitionSource
{
    private readonly ConcurrentDictionary<byte, Lazy<SkillRuleCatalog>> catalogs = new();

    public SkillRuleCatalog Get(byte job) => catalogs.GetOrAdd(job,
        key => new Lazy<SkillRuleCatalog>(() => Read(key))).Value;

    private SkillRuleCatalog Read(byte job)
    {
        var archive = provider.Archive;
        var skills = ReadSkillPaths(archive, job).ToFrozenDictionary(
            pair => pair.Key, pair => ReadSkill(archive, pair.Key, pair.Value));
        var sp = ReadSectionInts(archive, "Etc/spTable.etc", "[sp table]");
        var (grants, careers) = ReadFreeGrants(archive, job);
        return new(skills, grants, Array.AsReadOnly(
            Enumerable.Range(0, sp.Count / 2).Select(i => (sp[i * 2], sp[i * 2 + 1])).ToArray()), careers);
    }

    private static (IReadOnlyList<SkillFreeGrant> Grants, IReadOnlySet<CharacterCareer> Careers) ReadFreeGrants(PvfArchive archive, byte job)
    {
        var relative = PvfListFile.Read(archive, "character/character.lst")
            .FirstOrDefault(value => value.Id == job).RelativePath;
        if (string.IsNullOrEmpty(relative)) return (Array.Empty<SkillFreeGrant>(), Array.Empty<CharacterCareer>().ToFrozenSet());
        var reader = archive.OpenScript("character/" + relative);
        var grow = -1;
        var awakening = 0;
        var accepting = false;
        var operands = new List<int>();
        var grants = new List<SkillFreeGrant>();
        var careers = new HashSet<CharacterCareer>();
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                Flush();
                var tag = archive.ResolveString(token.Value);
                accepting = false;
                if (TryNumberedTag(tag, "[growtype ", out var value)) { grow = value - 1; awakening = 0; careers.Add(new(job, grow)); }
                else if (TryNumberedTag(tag, "[awakening ", out value))
                { awakening = value; careers.Add(new(job, grow, awakening)); }
                else if (tag.Equals("[initial value]", StringComparison.OrdinalIgnoreCase)) { grow = -1; awakening = 0; }
                else if (tag.Equals("[skill]", StringComparison.OrdinalIgnoreCase)
                    || tag.Equals("[awakening skill]", StringComparison.OrdinalIgnoreCase)) accepting = true;
            }
            else if (accepting && token.Kind == PvfTokenKind.Int32) operands.Add(token.Value);
        }
        Flush();
        return (Array.AsReadOnly(grants.ToArray()), careers.ToFrozenSet());

        void Flush()
        {
            if (accepting)
                for (var i = 0; i + 1 < operands.Count; i += 2)
                    grants.Add(new(operands[i], operands[i + 1], grow, awakening));
            operands.Clear();
        }
    }

    private static Dictionary<int, string> ReadSkillPaths(PvfArchive archive, byte job)
    {
        var jobFile = PvfListFile.Read(archive, "skill/skilllist.lst")
            .FirstOrDefault(value => value.Id == job).RelativePath;
        if (string.IsNullOrEmpty(jobFile)) return [];
        return PvfListFile.Read(archive, "skill/" + jobFile)
            .GroupBy(value => value.Id).ToDictionary(group => group.Key, group => group.First().RelativePath);
    }

    private static SkillRuleDefinition ReadSkill(PvfArchive archive, int skillId, string relative)
    {
        var path = "skill/" + relative;
        var sections = ReadAllIntSections(archive, path);
        IReadOnlyList<int> Values(string tag) => sections.GetValueOrDefault(tag) ?? Array.Empty<int>();
        var usesTp = Values("[feature skill type]").Any(value => value != 0);
        var prerequisites = Values("[pre required skill]");
        if (prerequisites.Count % 2 != 0)
            throw new InvalidDataException($"Unpaired prerequisite in {path}.");
        return new SkillRuleDefinition(skillId,
            ReadStringSection(archive, path, "[name]"),
            ReadStringSection(archive, path, "[explain]"), usesTp,
            Values(usesTp ? "[special purchase cost]" : "[purchase cost]"),
            Values("[maximum level]"), Values("[growtype maximum level]"),
            Values("[second growtype maximum level]"), First(sections, "[required level]"),
            Math.Max(1, First(sections, "[required level range]", 1)),
            Array.AsReadOnly(Enumerable.Range(0, prerequisites.Count / 2)
                .Select(i => new SkillPrerequisite(prerequisites[i * 2], prerequisites[i * 2 + 1])).ToArray()),
            sections.ContainsKey("[fixed level skill]"), First(sections, "[fixed level skill]"),
            Math.Max(1, First(sections, "[interval level]", 1)),
            Math.Max(1, First(sections, "[add level per interval]", 1)),
            ReadStringSection(archive, path, "[type]").Contains("active", StringComparison.OrdinalIgnoreCase),
            First(sections, "[skill class]"), Values("[skill fitness growtype]").Count);
    }

    private static string ReadStringSection(PvfArchive archive, string path, string tag)
    {
        var reader = archive.OpenScript(path); var accepting = false;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
                accepting = archive.ResolveString(token.Value).Equals(tag, StringComparison.OrdinalIgnoreCase);
            else if (accepting && token.IsString) return archive.ResolveString(token.Value);
        }
        return string.Empty;
    }

    private static IReadOnlyList<int> ReadSectionInts(PvfArchive archive, string path, string tag) =>
        ReadAllIntSections(archive, path).GetValueOrDefault(tag) ?? [];

    private static Dictionary<string, IReadOnlyList<int>> ReadAllIntSections(PvfArchive archive, string path)
    {
        var result = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);
        var reader = archive.OpenScript(path);
        string? section = null;
        List<int>? values = null;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                if (section is not null && values is not null) result[section] = Array.AsReadOnly(values.ToArray());
                section = archive.ResolveString(token.Value);
                values = [];
            }
            else if (token.Kind == PvfTokenKind.Int32 && values is not null)
                values.Add(token.Value);
        }
        if (section is not null && values is not null) result[section] = Array.AsReadOnly(values.ToArray());
        return result;
    }

    private static int First(Dictionary<string, IReadOnlyList<int>> sections, string tag, int fallback = 0) =>
        sections.TryGetValue(tag, out var values) && values.Count > 0 ? values[0] : fallback;

    private static bool TryNumberedTag(string tag, string prefix, out int value)
    {
        value = 0;
        return tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && tag.EndsWith(']')
            && int.TryParse(tag.AsSpan(prefix.Length, tag.Length - prefix.Length - 1), out value);
    }
}
