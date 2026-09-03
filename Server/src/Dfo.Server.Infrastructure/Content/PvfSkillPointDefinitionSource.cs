using System.Collections.Concurrent;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// What the archive declares about a job's skill points, read once per distinct question.
/// </summary>
/// <remarks>
/// Answering one of these means walking the archive: <c>Etc/spTable.etc</c>, the character list,
/// the job's own script, the skill list, and one script per skill the character has learned. That
/// was affordable while entry was the only caller and asked once per login. It is not any more --
/// a character is now asked what it has left to spend every time a kill, a clear or a quest sends
/// an experience frame -- and a full parse of the archive on the kill path is not a cost, it is a
/// different kind of operation.
/// <para>
/// So the answers are kept. What makes that safe is that this is a pure function of its four
/// arguments over an archive that never changes: <see cref="PvfArchiveProvider"/> opens the file
/// once and holds it read-only for the life of the process, so an answer that was right when it
/// was worked out stays right. The cache is here, at the archive, and not in the services above:
/// their contract is that a character's ledger is worked out from what is stored at the moment of
/// the question, and remembering what a <em>file</em> says does not touch that -- remembering what
/// a <em>character</em> has left would break it outright.
/// </para>
/// <para>
/// The learned skills take part in the key in a canonical form, sorted and deduplicated, because
/// the parameter is an <see cref="IReadOnlyCollection{T}"/> and promises no order -- and the
/// answer does not depend on one, since the ids are deduplicated into a dictionary before anything
/// is read. Two callers naming the same skills in different orders must not each pay for a parse.
/// </para>
/// <para>
/// Nothing is evicted. The entries are bounded by the distinct questions a running server asks --
/// a job, an advancement, a level and a set of learned skills -- so it grows as characters learn
/// skills and stops when they stop. That is a deliberate choice for a server whose archive is
/// fixed, not an oversight; if it ever needs a bound, this is the one place to put it.
/// </para>
/// </remarks>
internal sealed class PvfSkillPointDefinitionSource(PvfArchiveProvider provider)
    : ISkillPointDefinitionSource
{
    /// <summary>One distinct question, in a form two orderings of the same one agree on.</summary>
    private readonly record struct Question(
        byte Job, byte PackedGrowType, byte CharacterLevel, string LearnedSkillIds);

    private readonly ConcurrentDictionary<Question, SkillPointDefinitionSet> answers = new();

    public ValueTask<SkillPointDefinitionSet> GetAsync(
        byte job,
        byte packedGrowType,
        byte characterLevel,
        IReadOnlyCollection<int> learnedSkillIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(learnedSkillIds);
        var question = new Question(
            job,
            packedGrowType,
            characterLevel,
            string.Join(',', learnedSkillIds.Distinct().Order()));
        return ValueTask.FromResult(
            this.answers.TryGetValue(question, out var answered)
                ? answered
                // Worked out outside the dictionary rather than inside GetOrAdd's factory: two
                // callers racing on one question would both parse, which wastes a parse but keeps
                // the read off the dictionary's lock. Whichever lands first is the one everyone
                // gets, and the two are equal anyway -- the archive cannot have changed between
                // them.
                : this.answers.GetOrAdd(
                    question,
                    this.Read(job, packedGrowType, characterLevel, learnedSkillIds)));
    }

    private SkillPointDefinitionSet Read(
        byte job,
        byte packedGrowType,
        byte characterLevel,
        IReadOnlyCollection<int> learnedSkillIds)
    {
        var archive = provider.Archive;
        var totalSp = ReadSpTotal(archive, characterLevel);
        var totalTp = characterLevel < 50 ? 0 : characterLevel - 49;
        var grants = ReadFreeGrants(archive, job, packedGrowType);
        var paths = ReadSkillPaths(archive, job);
        var skills = new Dictionary<int, SkillPointCostDefinition>();
        foreach (var skillId in learnedSkillIds.Concat(grants.Select(value => value.SkillId)).Distinct())
        {
            if (paths.TryGetValue(skillId, out var path))
                skills[skillId] = ReadSkill(archive, job, skillId, path, packedGrowType, characterLevel);
        }
        return new SkillPointDefinitionSet(totalSp, totalTp, grants, skills);
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
        var grants = new Dictionary<int, (byte Level, bool Initial)>();
        var grantOrder = new List<int>();
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
        return grantOrder.Select(skillId => grants[skillId])
            .Select((value, index) => new SkillPointGrantDefinition(
                grantOrder[index], value.Level, value.Initial)).ToArray();

        void Flush()
        {
            if (accepting)
                for (var index = 0; index + 1 < operands.Count; index += 2)
                {
                    var skillId = operands[index];
                    var grantLevel = checked((byte)operands[index + 1]);
                    var initial = currentGrow < 0;
                    if (!grants.ContainsKey(skillId)) grantOrder.Add(skillId);
                    if (!grants.TryGetValue(skillId, out var existing) || grantLevel > existing.Level)
                        grants[skillId] = (grantLevel, initial);
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

    private static SkillPointCostDefinition ReadSkill(PvfArchive archive, byte job, int skillId, string relative,
        byte packedGrowType, byte characterLevel)
    {
        var sections = ReadAllIntSections(archive, "skill/" + relative);
        var feature = sections.GetValueOrDefault("[feature skill type]") ?? [];
        var costs = feature.Any(value => value != 0)
            ? sections.GetValueOrDefault("[special purchase cost]") ?? []
            : sections.GetValueOrDefault("[purchase cost]") ?? [];
        var fixedValues = sections.GetValueOrDefault("[fixed level skill]") ?? [];
        var type = ReadStringSection(archive, "skill/" + relative, "[type]");
        var fitness = sections.GetValueOrDefault("[skill fitness growtype]") ?? [];
        var grow = packedGrowType & 0x0F; var second = packedGrowType >> 4;
        var growMax = sections.GetValueOrDefault("[growtype maximum level]") ?? [];
        var secondMax = sections.GetValueOrDefault("[second growtype maximum level]") ?? [];
        var directionMax = second > 0 && grow * 2 + second - 1 < secondMax.Count
            && secondMax[grow * 2 + second - 1] > 0
            ? secondMax[grow * 2 + second - 1]
            : growMax.Count > 0 ? grow < growMax.Count ? growMax[grow] : 0
            : Math.Max(1, First(sections, "[maximum level]", 1));
        var staticMaximum = Math.Min(
            Math.Max(1, First(sections, "[maximum level]", 1)), directionMax);
        var required = First(sections, "[required level]");
        var interval = Math.Max(1, First(sections, "[required level range]", 1));
        int? ordinaryMaximum = directionMax <= 0 ? null
            : characterLevel < required ? 0
            : Math.Min(staticMaximum,
                required <= 0 ? staticMaximum : 1 + (characterLevel - required) / interval);
        return new SkillPointCostDefinition(
            skillId,
            feature.Any(value => value != 0),
            costs,
            fixedValues.Count > 0,
            required,
            Math.Max(1, First(sections, "[maximum level]", 1)),
            fixedValues.Count > 0 ? fixedValues[0] : 0,
            fixedValues.Count > 1 ? Math.Max(1, fixedValues[1]) : 1,
            fixedValues.Count > 2 ? fixedValues[2] : 1,
            type.Contains("active", StringComparison.OrdinalIgnoreCase),
            First(sections, "[skill class]"), fitness.Count, ordinaryMaximum);
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
