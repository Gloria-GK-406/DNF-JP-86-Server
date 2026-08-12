using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfSkillPointDefinitionSource(PvfArchiveProvider provider)
    : ISkillPointDefinitionSource
{
    public ValueTask<SkillPointDefinitionSet> GetAsync(
        byte job,
        byte packedGrowType,
        byte characterLevel,
        IReadOnlyCollection<int> learnedSkillIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var archive = provider.Archive;
        var totalSp = ReadSpTotal(archive, characterLevel);
        var totalTp = characterLevel < 50 ? 0 : characterLevel - 49;
        var grants = ReadFreeGrants(archive, job, packedGrowType);
        var paths = ReadSkillPaths(archive, job);
        var skills = new Dictionary<int, SkillPointCostDefinition>();
        foreach (var skillId in learnedSkillIds.Distinct())
        {
            if (paths.TryGetValue(skillId, out var path))
                skills[skillId] = ReadSkill(archive, job, skillId, path);
        }
        return ValueTask.FromResult(new SkillPointDefinitionSet(totalSp, totalTp, grants, skills));
    }

    private static int ReadSpTotal(PvfArchive archive, int level)
    {
        var values = ReadSectionInts(archive, "Etc/spTable.etc", "[sp table]");
        var total = 0;
        for (var index = 0; index + 1 < values.Count; index += 2)
            if (values[index] <= level) total += values[index + 1];
        return total;
    }

    private static IReadOnlyList<SkillPointGrantDefinition> ReadFreeGrants(
        PvfArchive archive, byte job, byte packedGrowType)
    {
        var characterList = PvfListFile.Read(archive, "character/character.lst");
        var relative = characterList.FirstOrDefault(value => value.Id == job).RelativePath;
        if (string.IsNullOrEmpty(relative)) return [];
        var firstGrow = packedGrowType & 0x0F;
        var secondGrow = (packedGrowType >> 4) & 0x0F;
        var reader = archive.OpenScript("character/" + relative);
        var currentGrow = -1;
        var currentAwakening = 0;
        var accepting = false;
        var operands = new List<int>();
        var grants = new Dictionary<int, byte>();
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                Flush();
                var tag = archive.ResolveString(token.Value);
                accepting = false;
                if (TryNumberedTag(tag, "[growtype ", out var grow))
                {
                    currentGrow = grow - 1;
                    currentAwakening = 0;
                }
                else if (TryNumberedTag(tag, "[awakening ", out var awakening))
                    currentAwakening = awakening;
                else if (tag.Equals("[initial value]", StringComparison.OrdinalIgnoreCase))
                {
                    currentGrow = -1;
                    currentAwakening = 0;
                }
                else if (tag.Equals("[skill]", StringComparison.OrdinalIgnoreCase))
                    accepting = currentGrow < 0 || (currentGrow == firstGrow && currentAwakening == 0);
                else if (tag.Equals("[awakening skill]", StringComparison.OrdinalIgnoreCase))
                    accepting = currentGrow == firstGrow && currentAwakening is > 0 && currentAwakening <= secondGrow;
            }
            else if (accepting && token.Kind == PvfTokenKind.Int32)
                operands.Add(token.Value);
        }
        Flush();
        return grants.OrderBy(value => value.Key)
            .Select(value => new SkillPointGrantDefinition(value.Key, value.Value)).ToArray();

        void Flush()
        {
            if (accepting)
                for (var index = 0; index + 1 < operands.Count; index += 2)
                {
                    var skillId = operands[index];
                    var grantLevel = checked((byte)operands[index + 1]);
                    if (!grants.TryGetValue(skillId, out var existing) || grantLevel > existing)
                        grants[skillId] = grantLevel;
                }
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

    private static SkillPointCostDefinition ReadSkill(PvfArchive archive, byte job, int skillId, string relative)
    {
        var sections = ReadAllIntSections(archive, "skill/" + relative);
        var feature = sections.GetValueOrDefault("[feature skill type]") ?? [];
        var costs = feature.Any(value => value != 0)
            ? sections.GetValueOrDefault("[special purchase cost]") ?? []
            : sections.GetValueOrDefault("[purchase cost]") ?? [];
        var fixedValues = sections.GetValueOrDefault("[fixed level skill]") ?? [];
        return new SkillPointCostDefinition(
            skillId,
            feature.Any(value => value != 0),
            costs,
            fixedValues.Count > 0,
            First(sections, "[required level]"),
            Math.Max(1, First(sections, "[maximum level]", 1)),
            fixedValues.Count > 0 ? fixedValues[0] : 0,
            fixedValues.Count > 1 ? Math.Max(1, fixedValues[1]) : 1,
            fixedValues.Count > 2 ? fixedValues[2] : 1);
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
                if (section is not null && values is not null) result[section] = values.ToArray();
                section = archive.ResolveString(token.Value);
                values = [];
            }
            else if (token.Kind == PvfTokenKind.Int32 && values is not null)
                values.Add(token.Value);
        }
        if (section is not null && values is not null) result[section] = values.ToArray();
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
