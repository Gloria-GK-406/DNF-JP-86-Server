using Dfo.Pvf.Schema;
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
    private static readonly PvfFieldFamily GrowTypeFields = new("[growtype ");
    private static readonly PvfFieldFamily AwakeningFields = new("[awakening ");

    private static class Fields
    {
        internal static readonly PvfField<IReadOnlyList<int>> SpecialPurchaseCostField = PvfField.Int32List("[special purchase cost]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> PurchaseCostField = PvfField.Int32List("[purchase cost]", optional: true);

        internal static readonly PvfField<IReadOnlyList<int>> SpTableField = PvfField.Int32List("[sp table]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> FeatureSkillTypeField = PvfField.Int32List("[feature skill type]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> PreRequiredSkillField = PvfField.Int32List("[pre required skill]", optional: true);
        internal static readonly PvfField<string> NameField = PvfField.String("[name]", optional: true);
        internal static readonly PvfField<string> ExplainField = PvfField.String("[explain]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> MaximumLevelField = PvfField.Int32List("[maximum level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> GrowtypeMaximumLevelField = PvfField.Int32List("[growtype maximum level]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> SecondGrowtypeMaximumLevelField = PvfField.Int32List("[second growtype maximum level]", optional: true);
        internal static readonly PvfField<int> RequiredLevelField = PvfField.Int32("[required level]", optional: true);
        internal static readonly PvfField<int> RequiredLevelRangeField = PvfField.Int32("[required level range]", optional: true);
        internal static readonly PvfField<int> FixedLevelSkillField = PvfField.Custom<int>("[fixed level skill]", ReadFixedLevelBase, optional: true);
        internal static readonly PvfField<bool> FixedLevelSkillEndField = PvfField.Marker("[/fixed level skill]", optional: true);
        internal static readonly PvfField<int> ApplyContractSkillmasterField = PvfField.Int32("[is apply contract skillmaster]", optional: true);
        internal static readonly PvfField<int> IntervalLevelField = PvfField.Int32("[interval level]", optional: true);
        internal static readonly PvfField<int> AddLevelPerIntervalField = PvfField.Int32("[add level per interval]", optional: true);
        internal static readonly PvfField<string> TypeField = PvfField.String("[type]", optional: true);
        internal static readonly PvfField<int> SkillClassField = PvfField.Int32("[skill class]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> SkillFitnessGrowtypeField = PvfField.Int32List("[skill fitness growtype]", optional: true);
        internal static readonly PvfField<bool> InitialValueField = PvfField.Marker("[initial value]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> SkillField = PvfField.Int32List("[skill]", optional: true);
        internal static readonly PvfField<IReadOnlyList<int>> AwakeningSkillField = PvfField.Int32List("[awakening skill]", optional: true);

        internal static readonly IReadOnlyDictionary<string, PvfField> Known = new PvfField[]
        {
            SpecialPurchaseCostField, PurchaseCostField, SpTableField, FeatureSkillTypeField,
            PreRequiredSkillField, NameField, ExplainField, MaximumLevelField,
            GrowtypeMaximumLevelField, SecondGrowtypeMaximumLevelField, RequiredLevelField,
            RequiredLevelRangeField, FixedLevelSkillField, FixedLevelSkillEndField, ApplyContractSkillmasterField,
            IntervalLevelField, AddLevelPerIntervalField,
            TypeField, SkillClassField, SkillFitnessGrowtypeField, InitialValueField, SkillField, AwakeningSkillField,
        }.ToFrozenDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
    }

    private readonly ConcurrentDictionary<byte, Lazy<SkillRuleCatalog>> catalogs = new();

    public SkillRuleCatalog Get(byte job) => catalogs.GetOrAdd(job,
        key => new Lazy<SkillRuleCatalog>(() => Read(key))).Value;

    private SkillRuleCatalog Read(byte job)
    {
        var archive = provider.Archive;
        var skills = ReadSkillPaths(archive, job).ToFrozenDictionary(
            pair => pair.Key, pair => ReadSkill(archive, pair.Key, pair.Value));
        var sp = ReadSectionInts(archive, "Etc/spTable.etc", Fields.SpTableField);
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
                if (GrowTypeFields.TryGetIndex(tag, out var value)) { grow = value - 1; awakening = 0; careers.Add(new(job, grow)); }
                else if (AwakeningFields.TryGetIndex(tag, out value))
                { awakening = value; careers.Add(new(job, grow, awakening)); }
                else if (tag.Equals(Fields.InitialValueField.Name, StringComparison.OrdinalIgnoreCase)) { grow = -1; awakening = 0; }
                else if (tag.Equals(Fields.SkillField.Name, StringComparison.OrdinalIgnoreCase)
                    || tag.Equals(Fields.AwakeningSkillField.Name, StringComparison.OrdinalIgnoreCase)) accepting = true;
            }
            else if (accepting)
            {
                if (token.Kind != PvfTokenKind.Int32)
                    throw new InvalidDataException($"PVF 'character/{relative}', field '{Fields.SkillField.Name}': expected Int32 skill/level pair.");
                operands.Add(token.Value);
            }
        }
        Flush();
        return (Array.AsReadOnly(grants.ToArray()), careers.ToFrozenSet());

        void Flush()
        {
            if (accepting && operands.Count % 2 != 0)
                throw new InvalidDataException($"PVF 'character/{relative}', field '{Fields.SkillField.Name}': incomplete skill/level pair.");
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
        IReadOnlyList<int> Values(PvfField<IReadOnlyList<int>> tag) => sections.TryGetValue(tag.Name, out var tokens) ? tag.Decode(archive, tokens, path) : Array.Empty<int>();
        var usesTp = Values(Fields.FeatureSkillTypeField).Any(value => value != 0);
        var prerequisites = Values(Fields.PreRequiredSkillField);
        if (prerequisites.Count % 2 != 0)
            throw new InvalidDataException($"PVF '{path}', field '{Fields.PreRequiredSkillField}': incomplete prerequisite pair.");
        return new SkillRuleDefinition(skillId,
            ReadStringSection(archive, path, Fields.NameField),
            ReadStringSection(archive, path, Fields.ExplainField), usesTp,
            Values(usesTp ? Fields.SpecialPurchaseCostField : Fields.PurchaseCostField),
            Values(Fields.MaximumLevelField), Values(Fields.GrowtypeMaximumLevelField),
            Values(Fields.SecondGrowtypeMaximumLevelField), First(archive, sections, path, Fields.RequiredLevelField),
            Math.Max(1, First(archive, sections, path, Fields.RequiredLevelRangeField, 1)),
            Array.AsReadOnly(Enumerable.Range(0, prerequisites.Count / 2)
                .Select(i => new SkillPrerequisite(prerequisites[i * 2], prerequisites[i * 2 + 1])).ToArray()),
            sections.ContainsKey(Fields.FixedLevelSkillField.Name), First(archive, sections, path, Fields.FixedLevelSkillField),
            Math.Max(1, First(archive, sections, path, Fields.IntervalLevelField, 1)),
            Math.Max(1, First(archive, sections, path, Fields.AddLevelPerIntervalField, 1)),
            ReadStringSection(archive, path, Fields.TypeField).Contains("active", StringComparison.OrdinalIgnoreCase),
            First(archive, sections, path, Fields.SkillClassField), Values(Fields.SkillFitnessGrowtypeField).Count);
    }

    private static string ReadStringSection(PvfArchive archive, string path, PvfField<string> tag) =>
        PvfDocument.Open(archive, path).ReadAll(tag).FirstOrDefault() ?? string.Empty;

    private static IReadOnlyList<int> ReadSectionInts(PvfArchive archive, string path, PvfField<IReadOnlyList<int>> tag) =>
        ReadAllIntSections(archive, path).TryGetValue(tag.Name, out var tokens) ? tag.Decode(archive, tokens, path) : [];

    private static Dictionary<string, IReadOnlyList<PvfToken>> ReadAllIntSections(PvfArchive archive, string path)
    {
        var result = new Dictionary<string, IReadOnlyList<PvfToken>>(StringComparer.OrdinalIgnoreCase);
        var inFixedLevelBlock = false;
        foreach (var section in PvfDocument.Open(archive, path).Sections)
        {
            // Legal declarations can repeat and the old numeric projection selects the last
            // occurrence. Validate each complete occurrence before it can replace the prior one;
            // this also covers the purchase-cost branch not selected by this skill's TP flag.
            if (Fields.Known.TryGetValue(section.Name, out var field))
            {
                field.Validate(archive, section.Values, path);
                if (field == Fields.FixedLevelSkillField)
                {
                    if (inFixedLevelBlock)
                        throw new InvalidDataException($"PVF '{path}', field '{field}': a fixed-level block is already open.");
                    // Older declarations carry their base as one integer. An empty opening
                    // instead introduces the real nested form, whose base remains zero.
                    inFixedLevelBlock = section.Values.Count == 0;
                }
                else if (field == Fields.FixedLevelSkillEndField)
                {
                    if (!inFixedLevelBlock)
                        throw new InvalidDataException($"PVF '{path}', field '{field}': no matching fixed-level block opening.");
                    inFixedLevelBlock = false;
                }
                if ((field == Fields.PreRequiredSkillField || field == Fields.SpTableField
                    || field == Fields.SkillField || field == Fields.AwakeningSkillField)
                    && section.Values.Count % 2 != 0)
                    throw new InvalidDataException($"PVF '{path}', field '{field}': incomplete integer pair.");
            }
            result[section.Name] = section.Values;
        }
        if (inFixedLevelBlock)
            throw new InvalidDataException($"PVF '{path}', field '{Fields.FixedLevelSkillField}': missing closing '{Fields.FixedLevelSkillEndField}'.");
        return result;
    }

    private static int ReadFixedLevelBase(PvfArchive _, IReadOnlyList<PvfToken> values, string path)
    {
        if (values.Count == 0) return 0;
        if (values.Count == 1 && values[0].Kind == PvfTokenKind.Int32) return values[0].Value;
        throw new InvalidDataException($"PVF '{path}', field '{Fields.FixedLevelSkillField}': expected one Int32 base or an empty nested-block opening.");
    }

    private static int First(PvfArchive archive, Dictionary<string, IReadOnlyList<PvfToken>> sections, string path, PvfField<int> tag, int fallback = 0) =>
        sections.TryGetValue(tag.Name, out var values) ? tag.Decode(archive, values, path) : fallback;


}
